using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

public sealed class FindDefinitionTool(FindDefinitionService definitions)
{
    [McpServerTool(
        Name = PublicToolNames.FindDefinition,
        Title = "Find C# syntax definitions",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<FindDefinitionData>))]
    [Description("Finds exact C# syntax declarations in the current structural index and reports explicit ambiguity without claiming project-semantic resolution.")]
    public async Task<CallToolResult> FindAsync(
        [Description("Exact case-sensitive C# declaration name.")]
        string name,
        [Description("Optional exact declaration kind, such as class, method, property, or constructor.")]
        string? kind = null,
        [Description("Optional exact namespace/type container returned by search_code or file_outline.")]
        string? container = null,
        [Description("Optional exact repository-relative C# file path.")]
        string? path = null,
        [Description("Maximum matches to return, from 1 to 100. Defaults to 25.")]
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        ToolResponse<FindDefinitionData> response = await definitions.FindAsync(
            name,
            kind,
            container,
            path,
            maxResults,
            cancellationToken).ConfigureAwait(false);
        string summary = response.Data is null
            ? $"C# definition lookup failed: {response.Error!.Code}."
            : $"C# definition lookup resolved {response.Data.Name} as {response.Data.Resolution} with {response.Data.TotalMatches} matches; returned {response.Data.Matches.Count}.";
        return McpToolResultFactory.Create(response, summary);
    }
}
