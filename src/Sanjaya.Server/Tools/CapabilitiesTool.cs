using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

/// <summary>
/// Reports the complete approved public surface without claiming deferred work exists.
/// </summary>
public sealed class CapabilitiesTool
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
    public static CallToolResult GetCapabilities()
    {
        ToolResponse<CapabilityReportData> response = CreateResponse();
        int supportedCount = response.Data!.Tools.Count(tool => tool.Status == ContractValues.AvailabilitySupported);

        return McpToolResultFactory.Create(
            response,
            $"Sanjaya protocol foundation is running with {supportedCount} of {response.Data.Tools.Count} approved tools available.");
    }

    public static ToolResponse<CapabilityReportData> CreateResponse()
    {
        HashSet<string> supportedTools = new(PublicToolNames.ProtocolFoundation, StringComparer.Ordinal);
        ToolAvailability[] tools = PublicToolNames.All
            .Select(name => supportedTools.Contains(name)
                ? new ToolAvailability(name, ContractValues.AvailabilitySupported)
                : new ToolAvailability(
                    name,
                    ContractValues.AvailabilityUnavailable,
                    ContractValues.ReasonNotImplemented))
            .ToArray();

        ProviderAvailability[] providers =
        [
            new("csharp", ["csharp"], ContractValues.AvailabilityUnavailable, ContractValues.ReasonNotImplemented),
            new("typescript-javascript", ["typescript", "javascript"], ContractValues.AvailabilityUnavailable, ContractValues.ReasonNotImplemented),
            new("generic", ["text"], ContractValues.AvailabilityUnavailable, ContractValues.ReasonNotImplemented),
        ];

        CapabilityReportData data = new(
            SanjayaRuntime.BuildVersion,
            SanjayaRuntime.Transport,
            SanjayaRuntime.DefaultNetworkAccess,
            tools,
            providers);

        return new ToolResponse<CapabilityReportData>(
            ContractValues.StatusOk,
            PublicToolNames.Capabilities,
            "sanjaya-runtime",
            data,
            [],
            []);
    }
}
