using Sanjaya.Core.Contracts;
using Sanjaya.Core.Repositories;

namespace Sanjaya.Core.Discovery;

public sealed class SearchTextService(RepositoryScope repository)
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
        "packages",
        "vendor",
    };

    public async Task<ToolResponse<SearchTextData>> SearchAsync(
        string? query,
        bool caseSensitive,
        int? requestedMaximumResults,
        CancellationToken cancellationToken)
    {
        if (!repository.IsReady)
        {
            return Error(
                repository.ConfigurationReason!,
                repository.ConfigurationError!,
                repository.ConfigurationRemediation);
        }

        if (string.IsNullOrEmpty(query)
            || query.Length > DiscoveryLimits.MaximumQueryCharacters
            || query.Contains('\r')
            || query.Contains('\n')
            || query.Contains('\0'))
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"Query must be a single line containing 1 to {DiscoveryLimits.MaximumQueryCharacters} characters.");
        }

        int maximumResults = requestedMaximumResults ?? DiscoveryLimits.DefaultResults;
        if (maximumResults is < 1 or > DiscoveryLimits.MaximumResults)
        {
            return Error(
                ContractValues.ErrorInvalidArgument,
                $"maxResults must be between 1 and {DiscoveryLimits.MaximumResults}.");
        }

        List<TextMatch> matches = [];
        List<EvidenceLocation> evidence = [];
        SearchCounters counters = new();
        bool truncated = false;
        bool cancelled = false;

        try
        {
            foreach (string path in EnumerateFiles(counters, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (counters.FilesConsidered >= DiscoveryLimits.MaximumFiles
                    || counters.BytesScanned >= DiscoveryLimits.MaximumSearchBytes)
                {
                    truncated = true;
                    break;
                }

                counters.FilesConsidered++;
                TextFileReadResult file = await BoundedTextFile.ReadAsync(
                    path,
                    DiscoveryLimits.MaximumFileBytes,
                    cancellationToken).ConfigureAwait(false);

                if (!file.IsSuccess)
                {
                    counters.Count(file.Error);
                    continue;
                }

                if (counters.BytesScanned + file.ByteCount > DiscoveryLimits.MaximumSearchBytes)
                {
                    truncated = true;
                    break;
                }

                counters.FilesScanned++;
                counters.BytesScanned += file.ByteCount;
                SearchFile(path, file.Text!, query, caseSensitive, maximumResults, matches, evidence, counters);
                if (matches.Count >= maximumResults)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
            truncated = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            counters.InaccessibleDirectories++;
            truncated = true;
        }

        if (counters.TraversalLimitReached || counters.MatchLimitReached)
        {
            truncated = true;
        }

        List<string> warnings = counters.CreateWarnings();
        if (truncated && !cancelled)
        {
            warnings.Add("search_limit_reached");
        }

        if (cancelled)
        {
            warnings.Add("search_cancelled");
        }

        string status = cancelled || truncated || counters.HasIncompleteIncludedContent
            ? ContractValues.StatusPartial
            : ContractValues.StatusOk;
        ErrorDetail? error = cancelled && matches.Count == 0
            ? new ErrorDetail(ContractValues.ErrorCancelled, "Search was cancelled.")
            : null;

        SearchTextData data = new(
            query,
            caseSensitive,
            matches,
            counters.FilesScanned,
            counters.BytesScanned,
            truncated);

        return new ToolResponse<SearchTextData>(
            status,
            PublicToolNames.SearchText,
            "generic-text",
            data,
            evidence,
            warnings,
            error);
    }

    private IEnumerable<string> EnumerateFiles(SearchCounters counters, CancellationToken cancellationToken)
    {
        Stack<string> directories = new();
        directories.Push(repository.CanonicalRoot!);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (counters.DirectoriesVisited >= DiscoveryLimits.MaximumDirectories)
            {
                counters.TraversalLimitReached = true;
                yield break;
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
                counters.InaccessibleDirectories++;
                continue;
            }

            if (counters.FileSystemEntries + entries.Length > DiscoveryLimits.MaximumFileSystemEntries)
            {
                counters.TraversalLimitReached = true;
                yield break;
            }

            counters.FileSystemEntries += entries.Length;
            for (int index = entries.Length - 1; index >= 0; index--)
            {
                FileSystemInfo entry = entries[index];
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
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

                yield return entry.FullName;
            }
        }
    }

    private void SearchFile(
        string fullPath,
        string text,
        string query,
        bool caseSensitive,
        int maximumResults,
        List<TextMatch> matches,
        List<EvidenceLocation> evidence,
        SearchCounters counters)
    {
        StringComparison comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string relativePath = Path.GetRelativePath(repository.CanonicalRoot!, fullPath).Replace('\\', '/');
        using StringReader reader = new(text);
        int lineNumber = 0;
        int matchesInFile = 0;
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            lineNumber++;
            if (line.Length > DiscoveryLimits.MaximumLineCharacters)
            {
                line = line[..DiscoveryLimits.MaximumLineCharacters];
                counters.LongLines++;
            }

            int start = 0;
            int matchesInLine = 0;
            while (start <= line.Length - query.Length)
            {
                int column = line.IndexOf(query, start, comparison);
                if (column < 0)
                {
                    break;
                }

                matches.Add(new TextMatch(relativePath, lineNumber, column + 1, CreateSnippet(line, column, query.Length)));
                evidence.Add(new EvidenceLocation(relativePath, lineNumber, lineNumber));
                matchesInLine++;
                matchesInFile++;

                if (matches.Count >= maximumResults
                    || matchesInLine >= DiscoveryLimits.MaximumMatchesPerLine
                    || matchesInFile >= DiscoveryLimits.MaximumMatchesPerFile)
                {
                    if (matchesInLine >= DiscoveryLimits.MaximumMatchesPerLine
                        || matchesInFile >= DiscoveryLimits.MaximumMatchesPerFile)
                    {
                        counters.MatchLimitReached = true;
                    }

                    break;
                }

                start = column + Math.Max(1, query.Length);
            }

            if (matches.Count >= maximumResults || matchesInFile >= DiscoveryLimits.MaximumMatchesPerFile)
            {
                break;
            }
        }
    }

    private static string CreateSnippet(string line, int matchStart, int queryLength)
    {
        if (line.Length <= DiscoveryLimits.MaximumSnippetCharacters)
        {
            return line;
        }

        int availableContext = DiscoveryLimits.MaximumSnippetCharacters - queryLength;
        int start = Math.Max(0, matchStart - (availableContext / 2));
        start = Math.Min(start, line.Length - DiscoveryLimits.MaximumSnippetCharacters);
        return line.Substring(start, DiscoveryLimits.MaximumSnippetCharacters);
    }

    private static bool IsGenerated(string fileName) =>
        fileName.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);

    private static ToolResponse<SearchTextData> Error(string code, string message, string? remediation = null) =>
        new(
            ContractValues.StatusError,
            PublicToolNames.SearchText,
            "generic-text",
            null,
            [],
            [],
            new ErrorDetail(code, message, remediation));

    private sealed class SearchCounters
    {
        public int FilesScanned { get; set; }

        public int FilesConsidered { get; set; }

        public long BytesScanned { get; set; }

        public int DirectoriesVisited { get; set; }

        public int FileSystemEntries { get; set; }

        public bool TraversalLimitReached { get; set; }

        public bool MatchLimitReached { get; set; }

        public int BinaryFiles { get; set; }

        public int OversizedFiles { get; set; }

        public int InaccessibleFiles { get; set; }

        public int InaccessibleDirectories { get; set; }

        public int SymlinkDirectories { get; set; }

        public int SymlinkFiles { get; set; }

        public int ExcludedDirectories { get; set; }

        public int GeneratedFiles { get; set; }

        public int LongLines { get; set; }

        public bool HasIncompleteIncludedContent =>
            OversizedFiles > 0
            || InaccessibleFiles > 0
            || InaccessibleDirectories > 0
            || LongLines > 0;

        public void Count(TextFileReadError error)
        {
            switch (error)
            {
                case TextFileReadError.Binary:
                    BinaryFiles++;
                    break;
                case TextFileReadError.TooLarge:
                    OversizedFiles++;
                    break;
                case TextFileReadError.Inaccessible:
                    InaccessibleFiles++;
                    break;
            }
        }

        public List<string> CreateWarnings()
        {
            List<string> warnings = [];
            Add(warnings, "binary_files_skipped", BinaryFiles);
            Add(warnings, "oversized_files_skipped", OversizedFiles);
            Add(warnings, "inaccessible_files_skipped", InaccessibleFiles);
            Add(warnings, "inaccessible_directories_skipped", InaccessibleDirectories);
            Add(warnings, "symlink_directories_skipped", SymlinkDirectories);
            Add(warnings, "symlink_files_skipped", SymlinkFiles);
            Add(warnings, "excluded_directories_skipped", ExcludedDirectories);
            Add(warnings, "generated_files_skipped", GeneratedFiles);
            Add(warnings, "long_lines_truncated", LongLines);
            if (TraversalLimitReached)
            {
                warnings.Add("traversal_limit_reached");
            }

            if (MatchLimitReached)
            {
                warnings.Add("match_limit_reached");
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
    }
}
