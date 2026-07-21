using System.Diagnostics;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Git;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class RecentChangesServiceTests
{
    [Fact]
    public async Task ReadsBoundedCommitAndWorkingTreeEvidenceFromLocalGit()
    {
        using TemporaryGitRepository repository = new();
        repository.Write("initial.txt", "first\n");
        repository.Write("rename-source.txt", "rename\n");
        repository.Run("add", "initial.txt", "rename-source.txt");
        repository.Run("commit", "-m", "initial evidence");
        repository.Write("initial.txt", "changed\n");
        repository.Run("mv", "rename-source.txt", "renamed.txt");
        repository.Write("staged.txt", "staged\n");
        repository.Run("add", "staged.txt");
        repository.Write("untracked.txt", "untracked\n");

        RepositoryScope scope = RepositoryScope.Create(repository.Path);
        RecentChangesService service = new(scope, new GitCommandRunner(scope));
        ToolResponse<RecentChangesData> response = await service.GetAsync(10, true, CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, response.Status);
        Assert.Equal("main", response.Data!.Head.Branch);
        Assert.False(response.Data.Head.Detached);
        Assert.NotNull(response.Data.Head.Revision);
        GitCommitData commit = Assert.Single(response.Data.Commits);
        Assert.Equal("initial evidence", commit.Subject);
        Assert.Contains(commit.Changes, change => change.Path == "initial.txt" && change.ChangeType == "added");
        Assert.False(response.Data.WorkingTree.Clean);
        Assert.Contains(response.Data.WorkingTree.Changes, change => change.Path == "initial.txt" && change.WorktreeStatus == "modified");
        Assert.Contains(response.Data.WorkingTree.Changes, change => change.Path == "staged.txt" && change.IndexStatus == "added");
        Assert.Contains(response.Data.WorkingTree.Changes, change => change.Path == "untracked.txt" && change.IndexStatus == "untracked");
        Assert.Contains(
            response.Data.WorkingTree.Changes,
            change => change.Path == "renamed.txt"
                && change.OriginalPath == "rename-source.txt"
                && change.IndexStatus == "renamed");
        Assert.All(response.Data.WorkingTree.Changes, change => Assert.False(System.IO.Path.IsPathRooted(change.Path)));
    }

    [Fact]
    public async Task UnbornAndDetachedHeadStatesRemainExplicit()
    {
        using TemporaryGitRepository repository = new();
        RepositoryScope scope = RepositoryScope.Create(repository.Path);
        RecentChangesService service = new(scope, new GitCommandRunner(scope));

        ToolResponse<RecentChangesData> unborn = await service.GetAsync(1, false, CancellationToken.None);
        Assert.Equal("main", unborn.Data!.Head.Branch);
        Assert.Null(unborn.Data.Head.Revision);
        Assert.False(unborn.Data.Head.Detached);
        Assert.Empty(unborn.Data.Commits);

        repository.Write("file.txt", "content\n");
        repository.Run("add", "file.txt");
        repository.Run("commit", "-m", "detached evidence");
        repository.Run("checkout", "--detach");

        ToolResponse<RecentChangesData> detached = await service.GetAsync(1, false, CancellationToken.None);
        Assert.Null(detached.Data!.Head.Branch);
        Assert.NotNull(detached.Data.Head.Revision);
        Assert.True(detached.Data.Head.Detached);
    }

    [Fact]
    public async Task MissingNonGitAndMismatchedRootsUseStableErrors()
    {
        RepositoryScope missingScope = RepositoryScope.Create(null);
        RecentChangesService missing = new(missingScope, new FakeRunner());
        Assert.Equal(
            ContractValues.ErrorRepositoryRootRequired,
            (await missing.GetAsync(null, true, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory directory = new();
        RepositoryScope nonGitScope = RepositoryScope.Create(directory.Path);
        RecentChangesService nonGit = new(nonGitScope, new FakeRunner());
        Assert.Equal(
            ContractValues.ErrorNotGitRepository,
            (await nonGit.GetAsync(null, true, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory candidate = new();
        Directory.CreateDirectory(System.IO.Path.Combine(candidate.Path, ".git"));
        RepositoryScope nestedScope = RepositoryScope.Create(candidate.Path);
        RecentChangesService mismatched = new(
            nestedScope,
            new FakeRunner(new GitCommandResult(
                GitCommandStatus.Completed,
                0,
                System.IO.Path.GetTempPath() + Environment.NewLine,
                string.Empty)));
        Assert.Equal(
            ContractValues.ErrorGitRootMismatch,
            (await mismatched.GetAsync(null, true, CancellationToken.None)).Error!.Code);
    }

    [Theory]
    [InlineData(GitCommandStatus.Unavailable, "git_unavailable")]
    [InlineData(GitCommandStatus.TimedOut, "git_timeout")]
    [InlineData(GitCommandStatus.OutputLimit, "git_output_limit")]
    [InlineData(GitCommandStatus.Cancelled, "cancelled")]
    [InlineData(GitCommandStatus.InvalidOutput, "git_command_failed")]
    public async Task RunnerFailuresUseStableSanitizedErrors(GitCommandStatus status, string expectedCode)
    {
        using TemporaryDirectory directory = new();
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, ".git"));
        RepositoryScope scope = RepositoryScope.Create(directory.Path);
        RecentChangesService service = new(
            scope,
            new FakeRunner(new GitCommandResult(status, -1, "/private/path", "secret stderr")));

        ToolResponse<RecentChangesData> response = await service.GetAsync(null, true, CancellationToken.None);

        Assert.Equal(expectedCode, response.Error!.Code);
        Assert.DoesNotContain("private", response.Error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", response.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task CommitLimitIsBounded(int limit)
    {
        using TemporaryDirectory directory = new();
        Directory.CreateDirectory(System.IO.Path.Combine(directory.Path, ".git"));
        RepositoryScope scope = RepositoryScope.Create(directory.Path);
        RecentChangesService service = new(scope, new FakeRunner());

        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.GetAsync(limit, true, CancellationToken.None)).Error!.Code);
    }

    private sealed class FakeRunner(params GitCommandResult[] results) : IGitCommandRunner
    {
        private readonly Queue<GitCommandResult> queue = new(results);

        public Task<GitCommandResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            GitCommandResult result = queue.Count > 0
                ? queue.Dequeue()
                : new GitCommandResult(GitCommandStatus.Completed, 1, string.Empty, string.Empty);
            return Task.FromResult(result);
        }
    }

    private sealed class TemporaryGitRepository : IDisposable
    {
        public TemporaryGitRepository()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sanjaya-git-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            Run("init", "-b", "main");
            Run("config", "user.name", "Sanjaya Tests");
            Run("config", "user.email", "sanjaya@example.invalid");
        }

        public string Path { get; }

        public void Write(string relativePath, string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, relativePath), content);

        public void Run(params string[] arguments)
        {
            ProcessStartInfo start = new("git")
            {
                WorkingDirectory = Path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Git.");
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(process.StandardError.ReadToEnd());
            }
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
