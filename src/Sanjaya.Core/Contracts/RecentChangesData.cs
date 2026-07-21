using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

public sealed record RecentChangesData(
    [property: JsonPropertyName("head")]
    GitHeadData Head,
    [property: JsonPropertyName("workingTree")]
    GitWorkingTreeData WorkingTree,
    [property: JsonPropertyName("commits")]
    IReadOnlyList<GitCommitData> Commits,
    [property: JsonPropertyName("truncated")]
    bool Truncated);

public sealed record GitHeadData(
    [property: JsonPropertyName("revision")]
    string? Revision,
    [property: JsonPropertyName("branch")]
    string? Branch,
    [property: JsonPropertyName("detached")]
    bool Detached);

public sealed record GitWorkingTreeData(
    [property: JsonPropertyName("included")]
    bool Included,
    [property: JsonPropertyName("clean")]
    bool? Clean,
    [property: JsonPropertyName("changes")]
    IReadOnlyList<GitWorkingTreeChange> Changes,
    [property: JsonPropertyName("truncated")]
    bool Truncated);

public sealed record GitWorkingTreeChange(
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("originalPath")]
    string? OriginalPath,
    [property: JsonPropertyName("indexStatus")]
    string IndexStatus,
    [property: JsonPropertyName("worktreeStatus")]
    string WorktreeStatus);

public sealed record GitCommitData(
    [property: JsonPropertyName("revision")]
    string Revision,
    [property: JsonPropertyName("committedAt")]
    string CommittedAt,
    [property: JsonPropertyName("subject")]
    string Subject,
    [property: JsonPropertyName("subjectTruncated")]
    bool SubjectTruncated,
    [property: JsonPropertyName("changes")]
    IReadOnlyList<GitPathChange> Changes,
    [property: JsonPropertyName("changesTruncated")]
    bool ChangesTruncated);

public sealed record GitPathChange(
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("originalPath")]
    string? OriginalPath,
    [property: JsonPropertyName("changeType")]
    string ChangeType);
