using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Git;
using Sanjaya.Server.Serialization;

namespace Sanjaya.Server.Tools;

public sealed class RecentChangesTool(RecentChangesService recentChanges)
{
    [McpServerTool(
        Name = PublicToolNames.RecentChanges,
        Title = "Recent local Git changes",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ToolResponse<RecentChangesData>))]
    [Description("Returns bounded local commit and working-tree evidence without contacting a remote Git service.")]
    public async Task<CallToolResult> GetAsync(
        [Description("Recent commit count (1-50, default 10).")]
        int? limit = null,
        [Description("Include bounded staged, unstaged, conflicted, and untracked paths. Defaults to true.")]
        bool includeWorkingTree = true,
        CancellationToken cancellationToken = default)
    {
        ToolResponse<RecentChangesData> response = await recentChanges.GetAsync(
            limit,
            includeWorkingTree,
            cancellationToken).ConfigureAwait(false);

        string summary = response.Data is null
            ? $"Recent local Git discovery failed: {response.Error!.Code}."
            : $"Recent local Git discovery returned {response.Data.Commits.Count} commits and {response.Data.WorkingTree.Changes.Count} working-tree paths ({response.Status}).";
        return McpToolResultFactory.Create(response, summary);
    }
}
