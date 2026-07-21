using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Discovery;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

public sealed class SearchTextTool(SearchTextService search)
{
    [McpServerTool(
        Name = PublicToolNames.SearchText,
        Title = "Exact repository text search",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<SearchTextData>))]
    [Description("Searches bounded readable UTF-8 files under the configured repository for a single-line exact text query without indexing.")]
    public async Task<CallToolResult> SearchAsync(
        [Description("Single-line exact text to find (1-256 characters; no CR, LF, or NUL).")]
        string query,
        [Description("Use ordinal case-sensitive matching. Defaults to true.")]
        bool caseSensitive = true,
        [Description("Maximum results to return (1-200, default 50).")]
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        ToolResponse<SearchTextData> response = await search.SearchAsync(
            query,
            caseSensitive,
            maxResults,
            cancellationToken).ConfigureAwait(false);

        string summary = response.Data is null
            ? $"Exact text search failed: {response.Error!.Code}."
            : $"Exact text search returned {response.Data.Matches.Count} matches from {response.Data.FilesScanned} files ({response.Status}).";
        return McpToolResultFactory.Create(response, summary);
    }
}
