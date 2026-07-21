using Sanjaya.Core.Contracts;
using Sanjaya.Core.Git;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class GitOutputParserTests
{
    [Fact]
    public void WorkingTreeParserPreservesIndexWorktreeAndRenameState()
    {
        string output = " M tracked.txt\0R  renamed.txt\0old.txt\0?? untracked file.txt\0UU conflict.txt\0";

        var data = GitOutputParser.ParseWorkingTree(output);

        Assert.False(data.Clean);
        Assert.Equal(4, data.Changes.Count);
        Assert.Equal(("unchanged", "modified"), (data.Changes[0].IndexStatus, data.Changes[0].WorktreeStatus));
        Assert.Equal("old.txt", data.Changes[1].OriginalPath);
        Assert.Equal(("untracked", "untracked"), (data.Changes[2].IndexStatus, data.Changes[2].WorktreeStatus));
        Assert.Equal(("unmerged", "unmerged"), (data.Changes[3].IndexStatus, data.Changes[3].WorktreeStatus));
    }

    [Fact]
    public void CommitParserReturnsRevisionSubjectAndChangedPaths()
    {
        string first = new('a', 40);
        string second = new('b', 40);
        string output = string.Concat(
            "SANJAYA_COMMIT\0", first, "\02026-07-21T12:00:00+05:30\0first subject\0\0\nA\0file.txt\0\nR100\0old.txt\0new.txt\0\n",
            "SANJAYA_COMMIT\0", second, "\02026-07-20T12:00:00+05:30\0second subject\0\0\nD\0gone.txt\0");

        var parsed = GitOutputParser.ParseCommitLog(output, 1);

        GitCommitData commit = Assert.Single(parsed.Commits);
        Assert.True(parsed.Truncated);
        Assert.Equal(first, commit.Revision);
        Assert.Equal("first subject", commit.Subject);
        Assert.Equal(["added", "renamed"], commit.Changes.Select(change => change.ChangeType));
        Assert.Equal("old.txt", commit.Changes[1].OriginalPath);
        Assert.Equal("new.txt", commit.Changes[1].Path);
    }

    [Theory]
    [InlineData(" M ../outside.txt\0")]
    [InlineData(" M ..\\outside.txt\0")]
    [InlineData(" M /absolute.txt\0")]
    [InlineData("broken")]
    public void ParserRejectsUnsafeOrMalformedWorkingTreeOutput(string output)
    {
        Assert.Throws<GitParseException>(() => GitOutputParser.ParseWorkingTree(output));
    }
}
