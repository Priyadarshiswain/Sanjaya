using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

public sealed class IndexCodebaseTool(IndexCodebaseService index)
{
    [McpServerTool(
        Name = PublicToolNames.IndexCodebase,
        Title = "Rebuild structural index",
        ReadOnly = false,
        Destructive = true,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<IndexCodebaseData>))]
    [Description("Atomically rebuilds the bounded deterministic structural index under the repository's .sanjaya directory.")]
    public async Task<CallToolResult> RebuildAsync(CancellationToken cancellationToken = default)
    {
        ToolResponse<IndexCodebaseData> response = await index.RebuildAsync(cancellationToken).ConfigureAwait(false);
        string summary = response.Data is null
            ? $"Structural index rebuild failed: {response.Error!.Code}."
            : $"Structural index ready: {response.Data.FilesIndexed} files and {response.Data.ChunksIndexed} chunks.";
        return McpToolResultFactory.Create(response, summary);
    }
}
