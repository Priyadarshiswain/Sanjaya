using Sanjaya.Core.Contracts;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Indexing;

internal sealed class IndexSourceScanner(
    RepositoryScope repository,
    IReadOnlyList<IStructuralChunkProvider> providers,
    IndexBuildLimits limits)
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".sanjaya",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "dist",
        "build",
        "coverage",
        "node_modules",
        "vendor",
    };

    public async Task<IndexSourceSnapshot> CaptureAsync(
        bool includeText,
        CancellationToken cancellationToken)
    {
        IndexScanCounters counters = new();
        List<IndexSourceFile> files = [];
        long sourceBytes = 0;

        foreach (IndexCandidateFile candidate in EnumerateCandidates(counters, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Count >= limits.MaximumEligibleFiles)
            {
                throw Failure(
                    ContractValues.ErrorIndexFileLimit,
                    $"Eligible source exceeds the {limits.MaximumEligibleFiles}-file index limit.");
            }

            TextFileReadResult file = await BoundedTextFile.ReadAsync(
                candidate.FullPath,
                DiscoveryLimits.MaximumFileBytes,
                cancellationToken).ConfigureAwait(false);
            if (!file.IsSuccess)
            {
                throw Failure(
                    ContractValues.ErrorIndexSourceUnreadable,
                    "An eligible source file is binary, oversized, inaccessible, or changed during indexing.");
            }

            if (sourceBytes + file.ByteCount > limits.MaximumSourceBytes)
            {
                throw Failure(
                    ContractValues.ErrorIndexSourceLimit,
                    $"Eligible source exceeds the {limits.MaximumSourceBytes}-byte index limit.");
            }

            sourceBytes += file.ByteCount;
            files.Add(new IndexSourceFile(
                candidate.RelativePath,
                candidate.Provider,
                file.ContentHash!,
                file.ByteCount,
                includeText ? file.Text : null));
        }

        return new IndexSourceSnapshot(files, sourceBytes, counters);
    }

    private IEnumerable<IndexCandidateFile> EnumerateCandidates(
        IndexScanCounters counters,
        CancellationToken cancellationToken)
    {
        Stack<string> directories = new();
        directories.Push(repository.CanonicalRoot!);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (counters.DirectoriesVisited >= limits.MaximumDirectories)
            {
                throw Failure(
                    ContractValues.ErrorIndexTraversalLimit,
                    "Repository traversal exceeded the directory limit.");
            }

            string current = directories.Pop();
            counters.DirectoriesVisited++;
            FileSystemInfo[] entries;
            try
            {
                entries = new DirectoryInfo(current)
                    .EnumerateFileSystemInfos()
                    .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw Failure(
                    ContractValues.ErrorIndexSourceUnreadable,
                    "A repository directory was inaccessible during indexing.");
            }

            if (counters.FileSystemEntries + entries.Length > limits.MaximumFileSystemEntries)
            {
                throw Failure(
                    ContractValues.ErrorIndexTraversalLimit,
                    "Repository traversal exceeded the filesystem-entry limit.");
            }

            counters.FileSystemEntries += entries.Length;
            for (int index = entries.Length - 1; index >= 0; index--)
            {
                FileSystemInfo entry = entries[index];
                if ((entry.Attributes & FileAttributes.Directory) == 0)
                {
                    continue;
                }

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget is not null)
                {
                    counters.SymlinkDirectories++;
                }
                else if (ExcludedDirectories.Contains(entry.Name))
                {
                    counters.ExcludedDirectories++;
                }
                else
                {
                    directories.Push(entry.FullName);
                }
            }

            foreach (FileSystemInfo entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    continue;
                }

                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.LinkTarget is not null)
                {
                    counters.SymlinkFiles++;
                    continue;
                }

                if (IsGenerated(entry.Name))
                {
                    counters.GeneratedFiles++;
                    continue;
                }

                string relativePath = Path.GetRelativePath(repository.CanonicalRoot!, entry.FullName).Replace('\\', '/');
                IStructuralChunkProvider? provider = providers.FirstOrDefault(
                    possibleProvider => possibleProvider.CanHandle(relativePath));
                if (provider is null)
                {
                    counters.UnsupportedFiles++;
                    continue;
                }

                yield return new IndexCandidateFile(entry.FullName, relativePath, provider);
            }
        }
    }

    private static bool IsGenerated(string fileName) =>
        fileName.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);

    private static IndexSourceScanFailure Failure(string code, string message) => new(code, message);

    private sealed record IndexCandidateFile(
        string FullPath,
        string RelativePath,
        IStructuralChunkProvider Provider);
}

internal sealed record IndexSourceSnapshot(
    IReadOnlyList<IndexSourceFile> Files,
    long SourceBytes,
    IndexScanCounters Counters);

internal sealed record IndexSourceFile(
    string RelativePath,
    IStructuralChunkProvider Provider,
    string ContentHash,
    int ByteCount,
    string? Text);

internal sealed class IndexSourceScanFailure(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal sealed class IndexScanCounters
{
    public int DirectoriesVisited { get; set; }

    public int FileSystemEntries { get; set; }

    public int UnsupportedFiles { get; set; }

    public int SymlinkDirectories { get; set; }

    public int SymlinkFiles { get; set; }

    public int ExcludedDirectories { get; set; }

    public int GeneratedFiles { get; set; }

    public List<string> CreateWarnings(string repositoryRoot)
    {
        List<string> warnings = [];
        Add(warnings, "symlink_directories_skipped", SymlinkDirectories);
        Add(warnings, "symlink_files_skipped", SymlinkFiles);
        Add(warnings, "excluded_directories_skipped", ExcludedDirectories);
        Add(warnings, "generated_files_skipped", GeneratedFiles);
        if (!IsExplicitlyIgnored(repositoryRoot))
        {
            warnings.Add("index_directory_not_explicitly_ignored");
        }

        return warnings;
    }

    private static void Add(List<string> warnings, string code, int count)
    {
        if (count > 0)
        {
            warnings.Add($"{code}:{count}");
        }
    }

    private static bool IsExplicitlyIgnored(string repositoryRoot)
    {
        string ignorePath = Path.Combine(repositoryRoot, ".gitignore");
        try
        {
            FileInfo ignore = new(ignorePath);
            if (!ignore.Exists
                || ignore.Length > 256 * 1024
                || ignore.LinkTarget is not null
                || (ignore.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            bool ignored = false;
            foreach (string line in File.ReadLines(ignorePath))
            {
                string rule = line.Trim();
                if (rule is ".sanjaya" or ".sanjaya/" or "/.sanjaya" or "/.sanjaya/")
                {
                    ignored = true;
                }
                else if (rule is "!.sanjaya" or "!.sanjaya/" or "!/.sanjaya" or "!/.sanjaya/")
                {
                    ignored = false;
                }
            }

            return ignored;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
