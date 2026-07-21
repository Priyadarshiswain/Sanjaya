namespace Sanjaya.Core.Git;

public static class GitDiscoveryLimits
{
    public const int DefaultCommitCount = 10;
    public const int MaximumCommitCount = 50;
    public const int MaximumWorkingTreeChanges = 200;
    public const int MaximumChangesPerCommit = 200;
    public const int MaximumSubjectCharacters = 240;
}
