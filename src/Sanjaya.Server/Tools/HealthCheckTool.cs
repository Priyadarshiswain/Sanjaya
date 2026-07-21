using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

/// <summary>
/// Reports only health facts the current process can verify directly.
/// </summary>
public sealed class HealthCheckTool
{
    [McpServerTool(
        Name = PublicToolNames.HealthCheck,
        Title = "Sanjaya health check",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<HealthReportData>))]
    [Description("Reports the health of Sanjaya's running stdio protocol foundation.")]
    public static CallToolResult CheckHealth()
    {
        ToolResponse<HealthReportData> response = CreateResponse();
        return McpToolResultFactory.Create(
            response,
            $"Sanjaya is healthy on stdio with {response.Data!.RegisteredToolCount} registered tools.");
    }

    public static ToolResponse<HealthReportData> CreateResponse()
    {
        HealthCheckEntry[] checks =
        [
            new("server", ContractValues.StatusOk, "The MCP server is running."),
            new("transport", ContractValues.StatusOk, "JSON-RPC is using stdio."),
            new("stdout", ContractValues.StatusOk, "Stdout is reserved for MCP protocol messages."),
            new("network", ContractValues.StatusOk, "Default operation performs no network access."),
        ];

        HealthReportData data = new(SanjayaRuntime.RegisteredToolCount, checks);

        return new ToolResponse<HealthReportData>(
            ContractValues.StatusOk,
            PublicToolNames.HealthCheck,
            "sanjaya-runtime",
            data,
            [],
            []);
    }
}
