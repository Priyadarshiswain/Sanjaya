using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

public sealed class GetSourceTool(GetSourceService sources)
{
    [McpServerTool(
        Name = PublicToolNames.GetSource,
        Title = "Get C# declaration source",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<GetSourceData>))]
    [Description("Returns exact bounded C# declaration source for one chunk ID from the current structural index.")]
    public async Task<CallToolResult> GetAsync(
        [Description("Exact lowercase chunk ID returned by search_code or find_definition.")]
        string chunkId,
        [Description("Optional one-based first line of a contained source window; requires endLine.")]
        int? startLine = null,
        [Description("Optional one-based last line of a contained source window; requires startLine.")]
        int? endLine = null,
        CancellationToken cancellationToken = default)
    {
        ToolResponse<GetSourceData> response = await sources.GetAsync(
            chunkId,
            startLine,
            endLine,
            cancellationToken).ConfigureAwait(false);
        string summary = response.Data is null
            ? $"C# source retrieval failed: {response.Error!.Code}."
            : $"C# source retrieval returned {response.Data.Name}; complete: {response.Data.Complete.ToString().ToLowerInvariant()}.";
        return McpToolResultFactory.Create(response, summary);
    }
}
