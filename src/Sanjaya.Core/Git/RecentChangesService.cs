using System.Globalization;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Git;

public sealed class RecentChangesService(RepositoryScope repository, IGitCommandRunner runner)
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public bool IsRepositoryCandidate => repository.IsGitWorktreeCandidate;

    public async Task<ToolResponse<RecentChangesData>> GetAsync(
        int? requestedLimit,
        bool includeWorkingTree,
        CancellationToken cancellationToken)
    {
        if (!repository.IsReady)
        {
            return Error(
                repository.ConfigurationReason!,
                repository.ConfigurationError!,
                repository.ConfigurationRemediation);
        }

        if (!repository.IsGitWorktreeCandidate)
        {
            return Error(
                ContractValues.ErrorNotGitRepository,
                "The configured repository root does not contain Git worktree metadata.");
        }

        int limit = requestedLimit ?? GitDiscoveryLimits.DefaultCommitCount;
        if (limit is < 1 or > GitDiscoveryLimits.MaximumCommitCount)
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"limit must be between 1 and {GitDiscoveryLimits.MaximumCommitCount}.");
        }

        GitCommandResult rootResult = await runner.RunAsync(
            ["rev-parse", "--show-toplevel"],
            cancellationToken).ConfigureAwait(false);
        ToolResponse<RecentChangesData>? rootFailure = MapExecutionFailure(rootResult);
        if (rootFailure is not null)
        {
            return rootFailure;
        }

        if (rootResult.ExitCode != 0)
        {
            return Error(ContractValues.ErrorNotGitRepository, "The configured root is not a readable Git worktree.");
        }

        if (!RootMatches(rootResult.StandardOutput))
        {
            return Error(
                ContractValues.ErrorGitRootMismatch,
                "The configured Sanjaya root must be the Git worktree root.");
        }

        GitCommandResult branchResult = await runner.RunAsync(
            ["symbolic-ref", "--quiet", "--short", "HEAD"],
            cancellationToken).ConfigureAwait(false);
        ToolResponse<RecentChangesData>? branchFailure = MapExecutionFailure(branchResult);
        if (branchFailure is not null)
        {
            return branchFailure;
        }

        if (branchResult.ExitCode is not 0 and not 1)
        {
            return Error(ContractValues.ErrorGitCommandFailed, "Git could not determine the current branch state.");
        }

        string? branch = branchResult.ExitCode == 0 ? TrimTerminator(branchResult.StandardOutput) : null;
        if (branch is not null && string.IsNullOrWhiteSpace(branch))
        {
            return Error(ContractValues.ErrorGitCommandFailed, "Git returned an invalid branch state.");
        }

        GitCommandResult headResult = await runner.RunAsync(
            ["rev-parse", "--verify", "HEAD"],
            cancellationToken).ConfigureAwait(false);
        ToolResponse<RecentChangesData>? headFailure = MapExecutionFailure(headResult);
        if (headFailure is not null)
        {
            return headFailure;
        }

        if (headResult.ExitCode != 0 && branch is null)
        {
            return Error(ContractValues.ErrorGitCommandFailed, "Git could not determine the current HEAD revision.");
        }

        string? revision = headResult.ExitCode == 0 ? TrimTerminator(headResult.StandardOutput) : null;
        if (revision is not null && !IsRevision(revision))
        {
            return Error(ContractValues.ErrorGitCommandFailed, "Git returned an invalid HEAD revision.");
        }

        GitWorkingTreeData workingTree = new(false, null, [], false);
        if (includeWorkingTree)
        {
            GitCommandResult statusResult = await runner.RunAsync(
                ["status", "--porcelain=v1", "-z", "--untracked-files=all"],
                cancellationToken).ConfigureAwait(false);
            ToolResponse<RecentChangesData>? statusFailure = MapExecutionFailure(statusResult);
            if (statusFailure is not null)
            {
                return statusFailure;
            }

            if (statusResult.ExitCode != 0)
            {
                return Error(ContractValues.ErrorGitCommandFailed, "Git could not read the working tree state.");
            }

            try
            {
                workingTree = GitOutputParser.ParseWorkingTree(statusResult.StandardOutput);
            }
            catch (GitParseException)
            {
                return Error(ContractValues.ErrorGitCommandFailed, "Git returned an invalid working tree response.");
            }
        }

        IReadOnlyList<GitCommitData> commits = [];
        bool historyTruncated = false;
        if (revision is not null)
        {
            GitCommandResult logResult = await runner.RunAsync(
                GitOutputParser.CommitLogArguments(limit),
                cancellationToken).ConfigureAwait(false);
            ToolResponse<RecentChangesData>? logFailure = MapExecutionFailure(logResult);
            if (logFailure is not null)
            {
                return logFailure;
            }

            if (logResult.ExitCode != 0)
            {
                return Error(ContractValues.ErrorGitCommandFailed, "Git could not read recent commit history.");
            }

            try
            {
                (commits, historyTruncated) = GitOutputParser.ParseCommitLog(logResult.StandardOutput, limit);
            }
            catch (GitParseException)
            {
                return Error(ContractValues.ErrorGitCommandFailed, "Git returned an invalid commit history response.");
            }
        }

        bool truncated = historyTruncated
            || workingTree.Truncated
            || commits.Any(commit => commit.SubjectTruncated || commit.ChangesTruncated);
        List<string> warnings = [];
        if (historyTruncated)
        {
            warnings.Add("commit_limit_reached");
        }

        if (workingTree.Truncated)
        {
            warnings.Add("working_tree_changes_truncated");
        }

        if (commits.Any(commit => commit.SubjectTruncated))
        {
            warnings.Add("commit_subjects_truncated");
        }

        if (commits.Any(commit => commit.ChangesTruncated))
        {
            warnings.Add("commit_changes_truncated");
        }

        RecentChangesData data = new(
            new GitHeadData(revision, branch, revision is not null && branch is null),
            workingTree,
            commits,
            truncated);

        return new ToolResponse<RecentChangesData>(
            truncated ? ContractValues.StatusPartial : ContractValues.StatusOk,
            PublicToolNames.RecentChanges,
            "local-git",
            data,
            [],
            warnings);
    }

    private bool RootMatches(string gitOutput)
    {
        string topLevel = TrimTerminator(gitOutput);
        try
        {
            string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(topLevel));
            return string.Equals(canonical, repository.CanonicalRoot, PathComparison);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    private static string TrimTerminator(string value)
    {
        if (value.EndsWith('\n'))
        {
            value = value[..^1];
        }

        if (value.EndsWith('\r'))
        {
            value = value[..^1];
        }

        return value;
    }

    private static bool IsRevision(string value) =>
        value.Length is >= 40 and <= 64 && value.All(Uri.IsHexDigit);

    private static ToolResponse<RecentChangesData>? MapExecutionFailure(GitCommandResult result) => result.Status switch
    {
        GitCommandStatus.Completed => null,
        GitCommandStatus.Unavailable => Error(
            ContractValues.ErrorGitUnavailable,
            "Git is not installed or could not be started."),
        GitCommandStatus.TimedOut => Error(
            ContractValues.ErrorGitTimeout,
            $"The read-only Git command exceeded {GitCommandRunner.CommandTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds."),
        GitCommandStatus.OutputLimit => Error(
            ContractValues.ErrorGitOutputLimit,
            $"Git output exceeded the {GitCommandRunner.MaximumOutputBytes}-byte limit."),
        GitCommandStatus.Cancelled => Error(ContractValues.ErrorCancelled, "Recent Git discovery was cancelled."),
        _ => Error(ContractValues.ErrorGitCommandFailed, "Git returned output that could not be read safely."),
    };

    private static ToolResponse<RecentChangesData> Error(string code, string message, string? remediation = null) =>
        new(
            ContractValues.StatusError,
            PublicToolNames.RecentChanges,
            "local-git",
            null,
            [],
            [],
            new ErrorDetail(code, message, remediation));
}
