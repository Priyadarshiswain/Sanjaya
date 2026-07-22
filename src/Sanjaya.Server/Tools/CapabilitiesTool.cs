using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Sanjaya.Providers.TypeScript;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

/// <summary>
/// Reports the complete approved public surface without claiming deferred work exists.
/// </summary>
public sealed class CapabilitiesTool(
    RepositoryScope repository,
    IEnumerable<ICapabilityProvider> capabilityProviders)
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
        implementedTools.UnionWith(PublicToolNames.StructuralIndex);
        implementedTools.UnionWith(PublicToolNames.StructuralSearch);
        implementedTools.UnionWith(PublicToolNames.DefinitionLookup);
        implementedTools.UnionWith(PublicToolNames.ReferenceLookup);
        implementedTools.UnionWith(PublicToolNames.SourceRetrieval);
        ToolAvailability[] tools = PublicToolNames.All
            .Select(CreateAvailability)
            .ToArray();

        List<ProviderAvailability> providers = capabilityProviders
            .GroupBy(provider => provider.Id, StringComparer.Ordinal)
            .Select(group => CreateProviderAvailability(group.First()))
            .OrderBy(provider => provider.Id, StringComparer.Ordinal)
            .ToList();
        if (providers.All(provider => provider.Id != "csharp-roslyn-syntax"))
        {
            providers.Add(CreateDeferredProvider("csharp-roslyn-syntax", ["csharp"]));
        }

        if (providers.All(provider => provider.Id != TypeScriptSyntaxProvider.TypeScriptProviderId))
        {
            providers.Add(CreateDeferredProvider(
                TypeScriptSyntaxProvider.TypeScriptProviderId,
                ["typescript"]));
        }

        if (providers.All(provider => provider.Id != TypeScriptSyntaxProvider.JavaScriptProviderId))
        {
            providers.Add(CreateDeferredProvider(
                TypeScriptSyntaxProvider.JavaScriptProviderId,
                ["javascript"]));
        }
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
                    repository.ConfigurationReason!)],
                repository.ConfigurationReason!));

        CapabilityReportData data = new(
            SanjayaRuntime.BuildVersion,
            SanjayaRuntime.Transport,
            SanjayaRuntime.DefaultNetworkAccess,
            repository.IsReady,
            repository.ConfigurationReason,
            repository.ConfigurationRemediation,
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
                    repository.ConfigurationReason!);
            }

            if (string.Equals(name, PublicToolNames.RecentChanges, StringComparison.Ordinal))
            {
                if (!repository.IsReady)
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        repository.ConfigurationReason!);
                }

                if (!repository.IsGitWorktreeCandidate)
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonNotGitRepository);
                }
            }

            if (string.Equals(name, PublicToolNames.IndexCodebase, StringComparison.Ordinal))
            {
                if (!repository.IsReady)
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        repository.ConfigurationReason!);
                }

                if (!capabilityProviders.OfType<IStructuralChunkProvider>().Any())
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonStructuralProviderUnavailable);
                }
            }

            if (string.Equals(name, PublicToolNames.SearchCode, StringComparison.Ordinal))
            {
                if (!capabilityProviders.OfType<IStructuralChunkProvider>().Any())
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonStructuralProviderUnavailable);
                }

                string? reason = GetIndexUnavailabilityReason();
                if (reason is not null)
                {
                    return new ToolAvailability(name, ContractValues.AvailabilityUnavailable, reason);
                }
            }

            if (string.Equals(name, PublicToolNames.FindDefinition, StringComparison.Ordinal))
            {
                if (!repository.IsReady)
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        repository.ConfigurationReason!);
                }

                bool definitionProviderAvailable = capabilityProviders
                    .OfType<IStructuralChunkProvider>()
                    .Any(provider => provider.GetCapabilities().Any(capability =>
                        capability.Capability == CapabilityKind.Definitions
                        && capability.Status == CapabilityStatus.Supported
                        && capability.Provider == provider.Id
                        && capability.Language == "csharp"));
                if (!definitionProviderAvailable)
                {
                    return new ToolAvailability(
                        name,
                        ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonDefinitionProviderUnavailable);
                }

                string? reason = GetIndexUnavailabilityReason();
                if (reason is not null)
                {
                    return new ToolAvailability(name, ContractValues.AvailabilityUnavailable, reason);
                }
            }

            if (string.Equals(name, PublicToolNames.FindReferences, StringComparison.Ordinal))
            {
                if (!repository.IsReady)
                {
                    return new ToolAvailability(name, ContractValues.AvailabilityUnavailable,
                        repository.ConfigurationReason!);
                }

                bool providerAvailable = capabilityProviders
                    .OfType<IReferenceProvider>()
                    .Any(provider => provider.GetCapabilities().Any(capability =>
                        capability.Capability == CapabilityKind.References
                        && capability.Status == CapabilityStatus.Supported
                        && capability.Provider == provider.Id
                        && capability.Language == "csharp"));
                if (!providerAvailable)
                {
                    return new ToolAvailability(name, ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonReferenceProviderUnavailable);
                }

                string? reason = GetIndexUnavailabilityReason();
                if (reason is not null)
                {
                    return new ToolAvailability(name, ContractValues.AvailabilityUnavailable, reason);
                }
            }

            if (string.Equals(name, PublicToolNames.GetSource, StringComparison.Ordinal))
            {
                if (!repository.IsReady)
                {
                    return new ToolAvailability(name, ContractValues.AvailabilityUnavailable,
                        repository.ConfigurationReason!);
                }

                bool providerAvailable = capabilityProviders
                    .OfType<ISourceRetrievalProvider>()
                    .Any(provider => provider.GetCapabilities().Any(capability =>
                        capability.Capability == CapabilityKind.SourceRetrieval
                        && capability.Status == CapabilityStatus.Supported
                        && capability.Provider == provider.Id
                        && capability.Language == "csharp"));
                if (!providerAvailable)
                {
                    return new ToolAvailability(name, ContractValues.AvailabilityUnavailable,
                        ContractValues.ReasonSourceProviderUnavailable);
                }

                string? reason = GetIndexUnavailabilityReason();
                if (reason is not null)
                {
                    return new ToolAvailability(name, ContractValues.AvailabilityUnavailable, reason);
                }
            }

            return new ToolAvailability(name, ContractValues.AvailabilitySupported);
        }

        string? GetIndexUnavailabilityReason()
        {
            if (!repository.IsReady)
            {
                return repository.ConfigurationReason!;
            }

            RepositoryPathResult index = repository.ResolveFile(IndexCodebaseService.RelativeIndexPath);
            if (!index.IsSuccess)
            {
                return index.Error == RepositoryPathError.NotFound
                    ? ContractValues.ReasonIndexMissing
                    : ContractValues.ReasonIndexInvalid;
            }

            try
            {
                long length = new FileInfo(index.FullPath!).Length;
                return length is <= 0 || length > IndexBuildLimits.Default.MaximumOutputBytes
                    ? ContractValues.ReasonIndexInvalid
                    : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return ContractValues.ReasonIndexInvalid;
            }
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
                            repository.ConfigurationReason!);
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
            string? providerReason = supported
                ? null
                : !repository.IsReady
                    ? repository.ConfigurationReason!
                    : capabilities
                        .Select(capability => capability.Reason)
                        .FirstOrDefault(reason => reason is not null
                            && reason != ContractValues.ReasonNotImplemented)
                        ?? ContractValues.ReasonNotImplemented;
            return new ProviderAvailability(
                provider.Id,
                provider.Languages.Order(StringComparer.Ordinal).ToArray(),
                supported ? ContractValues.AvailabilitySupported : ContractValues.AvailabilityUnavailable,
                capabilities,
                providerReason);
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
