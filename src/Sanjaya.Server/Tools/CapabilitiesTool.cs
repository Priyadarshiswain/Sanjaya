using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Repositories;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

/// <summary>
/// Reports the complete approved public surface without claiming deferred work exists.
/// </summary>
public sealed class CapabilitiesTool(RepositoryScope repository)
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
        ToolAvailability[] tools = PublicToolNames.All
            .Select(CreateAvailability)
            .ToArray();

        ProviderAvailability[] providers =
        [
            new("csharp", ["csharp"], ContractValues.AvailabilityUnavailable, ContractValues.ReasonNotImplemented),
            new("typescript-javascript", ["typescript", "javascript"], ContractValues.AvailabilityUnavailable, ContractValues.ReasonNotImplemented),
            repository.IsReady
                ? new("generic", ["text"], ContractValues.AvailabilitySupported)
                : new("generic", ["text"], ContractValues.AvailabilityUnavailable, ContractValues.ReasonRepositoryRootRequired),
        ];

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

            return new ToolAvailability(name, ContractValues.AvailabilitySupported);
        }
    }
}
