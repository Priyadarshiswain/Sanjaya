using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Discovery;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

public sealed class FileOutlineTool(FileOutlineService outline)
{
    [McpServerTool(
        Name = PublicToolNames.FileOutline,
        Title = "File outline",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<FileOutlineData>))]
    [Description("Returns a bounded structural C# outline or generic preview for one repository-relative readable UTF-8 file.")]
    public async Task<CallToolResult> OutlineAsync(
        [Description("Repository-relative file path.")]
        string path,
        CancellationToken cancellationToken = default)
    {
        ToolResponse<FileOutlineData> response = await outline.OutlineAsync(path, cancellationToken).ConfigureAwait(false);
        string summary = response.Data is null
            ? $"File outline failed: {response.Error!.Code}."
            : $"Outline for {response.Data.Path}: {response.Data.Items.Count} structural items, {response.Data.LineCount} lines.";
        return McpToolResultFactory.Create(response, summary);
    }
}
