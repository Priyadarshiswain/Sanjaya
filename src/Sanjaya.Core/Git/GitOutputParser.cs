using System.Globalization;
using Sanjaya.Core.Contracts;

namespace Sanjaya.Core.Git;

public static class GitOutputParser
{
    private const string CommitMarker = "SANJAYA_COMMIT";

    public static IReadOnlyList<string> CommitLogArguments(int requestedCount) =>
    [
        "log",
        "-n",
        checked(requestedCount + 1).ToString(CultureInfo.InvariantCulture),
        "--date=iso-strict",
        "--no-ext-diff",
        "--find-renames=50%",
        $"--format={CommitMarker}%x00%H%x00%cI%x00%s%x00",
        "--name-status",
        "-z",
    ];

    public static GitWorkingTreeData ParseWorkingTree(string output)
    {
        string[] tokens = output.Split('\0');
        List<GitWorkingTreeChange> changes = [];
        bool truncated = false;
        for (int index = 0; index < tokens.Length; index++)
        {
            string record = tokens[index];
            if (record.Length == 0)
            {
                continue;
            }

            if (record.Length < 3 || record[2] != ' ')
            {
                throw new GitParseException();
            }

            char indexCode = record[0];
            char worktreeCode = record[1];
            string path = NormalizePath(record[3..]);
            string? originalPath = null;
            if (indexCode is 'R' or 'C' || worktreeCode is 'R' or 'C')
            {
                if (++index >= tokens.Length || tokens[index].Length == 0)
                {
                    throw new GitParseException();
                }

                originalPath = NormalizePath(tokens[index]);
            }

            if (changes.Count < GitDiscoveryLimits.MaximumWorkingTreeChanges)
            {
                changes.Add(new(
                    path,
                    originalPath,
                    MapWorkingTreeStatus(indexCode),
                    MapWorkingTreeStatus(worktreeCode)));
            }
            else
            {
                truncated = true;
            }
        }

        return new GitWorkingTreeData(true, changes.Count == 0 && !truncated, changes, truncated);
    }

    public static (IReadOnlyList<GitCommitData> Commits, bool Truncated) ParseCommitLog(
        string output,
        int requestedCount)
    {
        string[] tokens = output.Split('\0');
        List<GitCommitData> commits = [];
        int index = 0;
        while (TryMoveToRecord(tokens, ref index))
        {
            string revision = Require(tokens, ref index);
            if (revision.Length is < 40 or > 64 || revision.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new GitParseException();
            }

            string committedAt = Require(tokens, ref index);
            if (!DateTimeOffset.TryParse(
                    committedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out _))
            {
                throw new GitParseException();
            }

            string subject = Require(tokens, ref index);
            bool subjectTruncated = subject.Length > GitDiscoveryLimits.MaximumSubjectCharacters;
            if (subjectTruncated)
            {
                subject = subject[..GitDiscoveryLimits.MaximumSubjectCharacters];
            }

            SkipEmpty(tokens, ref index);
            List<GitPathChange> changes = [];
            bool changesTruncated = false;
            while (index < tokens.Length && !IsMarker(tokens[index]))
            {
                string status = TrimRecordPrefix(tokens[index++]);
                if (status.Length == 0)
                {
                    continue;
                }

                char kind = status[0];
                if (kind is 'R' or 'C')
                {
                    string original = NormalizePath(Require(tokens, ref index));
                    string path = NormalizePath(Require(tokens, ref index));
                    AddChange(new(path, original, MapCommitStatus(kind)));
                }
                else if (kind is 'A' or 'D' or 'M' or 'T' or 'U' or 'X' or 'B')
                {
                    AddChange(new(NormalizePath(Require(tokens, ref index)), null, MapCommitStatus(kind)));
                }
                else
                {
                    throw new GitParseException();
                }
            }

            commits.Add(new(
                revision,
                committedAt,
                subject,
                subjectTruncated,
                changes,
                changesTruncated));

            void AddChange(GitPathChange change)
            {
                if (changes.Count < GitDiscoveryLimits.MaximumChangesPerCommit)
                {
                    changes.Add(change);
                }
                else
                {
                    changesTruncated = true;
                }
            }
        }

        bool truncated = commits.Count > requestedCount;
        return (commits.Take(requestedCount).ToArray(), truncated);
    }

    private static bool TryMoveToRecord(string[] tokens, ref int index)
    {
        SkipEmpty(tokens, ref index);
        if (index >= tokens.Length)
        {
            return false;
        }

        if (!IsMarker(tokens[index]))
        {
            throw new GitParseException();
        }

        index++;
        return true;
    }

    private static bool IsMarker(string token) =>
        string.Equals(TrimRecordPrefix(token), CommitMarker, StringComparison.Ordinal);

    private static string TrimRecordPrefix(string value) => value.TrimStart('\r', '\n');

    private static void SkipEmpty(string[] tokens, ref int index)
    {
        while (index < tokens.Length && string.IsNullOrWhiteSpace(tokens[index]))
        {
            index++;
        }
    }

    private static string Require(string[] tokens, ref int index)
    {
        if (index >= tokens.Length)
        {
            throw new GitParseException();
        }

        return tokens[index++];
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)
            || path[0] == '/'
            || Path.IsPathRooted(path)
            || (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':')
            || path.Split(['/', '\\']).Any(segment => segment is "." or ".."))
        {
            throw new GitParseException();
        }

        return path;
    }

    private static string MapWorkingTreeStatus(char status) => status switch
    {
        ' ' => "unchanged",
        'M' => "modified",
        'A' => "added",
        'D' => "deleted",
        'R' => "renamed",
        'C' => "copied",
        'T' => "type_changed",
        'U' => "unmerged",
        '?' => "untracked",
        '!' => "ignored",
        _ => "unknown",
    };

    private static string MapCommitStatus(char status) => status switch
    {
        'A' => "added",
        'D' => "deleted",
        'M' => "modified",
        'R' => "renamed",
        'C' => "copied",
        'T' => "type_changed",
        'U' => "unmerged",
        'X' => "unknown",
        'B' => "pairing_broken",
        _ => throw new GitParseException(),
    };
}

public sealed class GitParseException : Exception;
