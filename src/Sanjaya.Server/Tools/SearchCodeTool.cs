using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

public sealed class SearchCodeTool(SearchCodeService search)
{
    [McpServerTool(
        Name = PublicToolNames.SearchCode,
        Title = "Search indexed code",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<SearchCodeData>))]
    [Description("Searches the current deterministic structural index using bounded lexical ranking and returns repository-relative evidence.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("Whitespace-separated terms that every result must match across indexed name, container, kind, path, or content fields.")]
        string query,
        [Description("Use ordinal case-sensitive matching. Defaults to false.")]
        bool caseSensitive = false,
        [Description("Maximum matches to return, from 1 to 100. Defaults to 25.")]
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        ToolResponse<SearchCodeData> response = await search.SearchAsync(
            query,
            caseSensitive,
            maxResults,
            cancellationToken).ConfigureAwait(false);
        string summary = response.Data is null
            ? $"Indexed code search failed: {response.Error!.Code}."
            : $"Indexed code search found {response.Data.TotalMatches} matching chunks; returned {response.Data.Matches.Count}.";
        return McpToolResultFactory.Create(response, summary);
    }
}
