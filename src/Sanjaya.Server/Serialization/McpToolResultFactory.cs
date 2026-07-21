using System.Text.Json;
using ModelContextProtocol.Protocol;
using Sanjaya.Core.Contracts;

namespace Sanjaya.Server.Serialization;

/// <summary>
/// Creates MCP results with machine-readable content and a compact human fallback.
/// </summary>
public static class McpToolResultFactory
{
    public static CallToolResult Create<T>(ToolResponse<T> response, string summary)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = summary }],
            StructuredContent = JsonSerializer.SerializeToElement(response, SanjayaJson.Options),
            IsError = string.Equals(response.Status, ContractValues.StatusError, StringComparison.Ordinal),
        };
    }
}
