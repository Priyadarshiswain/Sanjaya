using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

/// <summary>
/// Reports the complete approved public surface without claiming deferred work exists.
/// </summary>
public sealed class CapabilitiesTool(
    RepositoryScope repository,
    IEnumerable<ICapabilityProvider>? capabilityProviders = null)
{
    [McpServerTool(
        Name = PublicToolNames.Capabilities,
        Title = "Sanjaya capabilities",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<CapabilityReportData>))]
    [Description("Reports which approved Sanjaya tools and language providers are currently available.")]
    public CallToolResult GetCapabilities()
    {
        ToolResponse<CapabilityReportData> response = CreateResponse();
        int supportedCount = response.Data!.Tools.Count(tool => tool.Status == ContractValues.AvailabilitySupported);

        return McpToolResultFactory.Create(
            response,
            $"Sanjaya is running with {supportedCount} of {response.Data.Tools.Count} approved tools available; repository ready: {response.Data.RepositoryReady.ToString().ToLowerInvariant()}.");
    }

    public ToolResponse<CapabilityReportData> CreateResponse()
    {
        HashSet<string> implementedTools = new(PublicToolNames.ProtocolFoundation, StringComparer.Ordinal);
        implementedTools.UnionWith(PublicToolNames.ImmediateDiscovery);
        implementedTools.UnionWith(PublicToolNames.LocalGitEvidence);
        ToolAvailability[] tools = PublicToolNames.All
            .Select(CreateAvailability)
            .ToArray();

        List<ProviderAvailability> providers = (capabilityProviders ?? [])
            .GroupBy(provider => provider.Id, StringComparer.Ordinal)
            .Select(group => CreateProviderAvailability(group.First()))
            .OrderBy(provider => provider.Id, StringComparer.Ordinal)
            .ToList();
        if (providers.All(provider => provider.Id != "csharp-roslyn-syntax"))
        {
            providers.Add(CreateDeferredProvider("csharp-roslyn-syntax", ["csharp"]));
        }

        providers.Add(CreateDeferredProvider("typescript-javascript", ["typescript", "javascript"]));
        providers.Add(repository.IsReady
            ? new ProviderAvailability(
                "generic",
                ["text"],
                ContractValues.AvailabilitySupported,
                [new ProviderCapabilityAvailability("file_outline", ContractValues.AvailabilitySupported)])
            : new ProviderAvailability(
                "generic",
                ["text"],
                ContractValues.AvailabilityUnavailable,
                [new ProviderCapabilityAvailability(
                    "file_outline",
                    ContractValues.AvailabilityUnavailable,
                    ContractValues.ReasonRepositoryRootRequired)],
                ContractValues.ReasonRepositoryRootRequired));

        CapabilityReportData data = new(
            SanjayaRuntime.BuildVersion,
            SanjayaRuntime.Transport,
            SanjayaRuntime.DefaultNetworkAccess,
            repository.IsReady,
            tools,
            providers);

        return new ToolResponse<CapabilityReportData>(
            ContractValues.StatusOk,
            PublicToolNames.Capabilities,
            "sanjaya-runtime",
            data,
            [],
            []);

        ToolAvailability CreateAvailability(string name)
        {
            if (!implementedTools.Contains(name))
            {
                return new ToolAvailability(
                    name,
                    ContractValues.AvailabilityUnavailable,
                    ContractValues.ReasonNotImplemented);
            }

            if (PublicToolNames.ImmediateDiscovery.Contains(name, StringComparer.Ordinal) && !repository.IsReady)
            {
                return new ToolAvailability(
                    name,
                    ContractValues.AvailabilityUnavailable,
                    ContractValues.ReasonRepositoryRootRequired);
            }

            if (string.Equals(name, PublicToolNames.RecentChanges, StringComparison.Ordinal))
            {
                if (!repository.IsReady)
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonRepositoryRootRequired);
                }

                if (!repository.IsGitWorktreeCandidate)
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonNotGitRepository);
                }
            }

            return new ToolAvailability(name, ContractValues.AvailabilitySupported);
        }

        ProviderAvailability CreateProviderAvailability(ICapabilityProvider provider)
        {
            ProviderCapabilityAvailability[] capabilities = provider.GetCapabilities()
                .OrderBy(descriptor => descriptor.Capability)
                .Select(descriptor =>
                {
                    if (descriptor.Status == CapabilityStatus.Supported && !repository.IsReady)
                    {
                        return new ProviderCapabilityAvailability(
                            CapabilityName(descriptor.Capability),
                            ContractValues.AvailabilityUnavailable,
                            ContractValues.ReasonRepositoryRootRequired);
                    }

                    return new ProviderCapabilityAvailability(
                        CapabilityName(descriptor.Capability),
                        descriptor.Status == CapabilityStatus.Supported
                            ? ContractValues.AvailabilitySupported
                            : ContractValues.AvailabilityUnavailable,
                        descriptor.Reason);
                })
                .ToArray();
            bool supported = repository.IsReady && capabilities.Any(
                capability => capability.Status == ContractValues.AvailabilitySupported);
            return new ProviderAvailability(
                provider.Id,
                provider.Languages.Order(StringComparer.Ordinal).ToArray(),
                supported ? ContractValues.AvailabilitySupported : ContractValues.AvailabilityUnavailable,
                capabilities,
                supported ? null : repository.IsReady
                    ? ContractValues.ReasonNotImplemented
                    : ContractValues.ReasonRepositoryRootRequired);
        }

        static ProviderAvailability CreateDeferredProvider(string id, IReadOnlyList<string> languages) =>
            new(
                id,
                languages,
                ContractValues.AvailabilityUnavailable,
                Enum.GetValues<CapabilityKind>()
                    .Select(capability => new ProviderCapabilityAvailability(
                        CapabilityName(capability),
                        ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonNotImplemented))
                    .ToArray(),
                ContractValues.ReasonNotImplemented);

        static string CapabilityName(CapabilityKind capability) => capability switch
        {
            CapabilityKind.FileOutline => "file_outline",
            CapabilityKind.StructuralChunking => "structural_chunking",
            CapabilityKind.Definitions => "definitions",
            CapabilityKind.References => "references",
            CapabilityKind.SourceRetrieval => "source_retrieval",
            CapabilityKind.CallGraph => "call_graph",
            _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, null),
        };
    }
}
