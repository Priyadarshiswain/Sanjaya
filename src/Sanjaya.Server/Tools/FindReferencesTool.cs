using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

public sealed class FindReferencesTool(FindReferencesService references)
{
    [McpServerTool(
        Name = PublicToolNames.FindReferences,
        Title = "Find C# syntax reference candidates",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<FindReferencesData>))]
    [Description("Finds exact C# identifier syntax candidates without claiming compiler-semantic symbol binding.")]
    public async Task<CallToolResult> FindAsync(
        [Description("Exact case-sensitive C# identifier name.")] string name,
        [Description("Optional repository-relative C# file path that limits the search scope.")] string? path = null,
        [Description("Maximum candidates to return, from 1 to 200. Defaults to 50.")] int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        ToolResponse<FindReferencesData> response = await references.FindAsync(
            name,
            path,
            maxResults,
            cancellationToken).ConfigureAwait(false);
        string summary = response.Data is null
            ? $"C# reference lookup failed: {response.Error!.Code}."
            : $"C# reference lookup found {response.Data.TotalMatches} syntax candidates; returned {response.Data.Matches.Count}.";
        return McpToolResultFactory.Create(response, summary);
    }
}
