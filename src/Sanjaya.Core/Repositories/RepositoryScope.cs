namespace Sanjaya.Core.Repositories;

/// <summary>
/// Immutable, canonical repository scope for one server process.
/// </summary>
public sealed class RepositoryScope
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private RepositoryScope(string? canonicalRoot, string? configurationError)
    {
        CanonicalRoot = canonicalRoot;
        ConfigurationError = configurationError;
    }

    public bool IsReady => CanonicalRoot is not null;

    public bool IsGitWorktreeCandidate
    {
        get
        {
            if (!IsReady)
            {
                return false;
            }

            string metadataPath = Path.Combine(CanonicalRoot!, ".git");
            try
            {
                FileSystemInfo metadata = Directory.Exists(metadataPath)
                    ? new DirectoryInfo(metadataPath)
                    : new FileInfo(metadataPath);
                return metadata.Exists
                    && metadata.LinkTarget is null
                    && (metadata.Attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public string? ConfigurationError { get; }

    internal string? CanonicalRoot { get; }

    public static RepositoryScope Create(string? configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot) || !Path.IsPathFullyQualified(configuredRoot))
        {
            return new(null, "A fully qualified repository root was not configured.");
        }

        try
        {
            string absolute = CanonicalizeExistingPath(Path.GetFullPath(configuredRoot));
            DirectoryInfo directory = new(absolute);
            if (!directory.Exists)
            {
                return new(null, "The configured repository root is not an existing directory.");
            }

            if ((directory.Attributes & FileAttributes.Directory) == 0)
            {
                return new(null, "The configured repository root is not an existing directory.");
            }

            return new(Path.TrimEndingDirectorySeparator(absolute), null);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new(null, "The configured repository root is invalid or inaccessible.");
        }
    }

    public RepositoryPathResult ResolveFile(string? repositoryRelativePath)
    {
        if (!IsReady)
        {
            return RepositoryPathResult.Failure(RepositoryPathError.RootRequired);
        }

        if (string.IsNullOrWhiteSpace(repositoryRelativePath) || Path.IsPathRooted(repositoryRelativePath))
        {
            return RepositoryPathResult.Failure(RepositoryPathError.InvalidPath);
        }

        string normalizedInput = repositoryRelativePath.Replace('\\', '/');
        if (normalizedInput[0] == '/'
            || (normalizedInput.Length >= 2 && char.IsAsciiLetter(normalizedInput[0]) && normalizedInput[1] == ':'))
        {
            return RepositoryPathResult.Failure(RepositoryPathError.InvalidPath);
        }

        string[] segments = normalizedInput.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0
            || segments.Any(segment => segment is "." or ".." || segment.Contains('\0')))
        {
            return RepositoryPathResult.Failure(RepositoryPathError.InvalidPath);
        }

        try
        {
            string current = CanonicalRoot!;
            for (int index = 0; index < segments.Length; index++)
            {
                current = Path.GetFullPath(Path.Combine(current, segments[index]));
                if (!IsContained(current))
                {
                    return RepositoryPathResult.Failure(RepositoryPathError.OutsideRepository);
                }

                FileSystemInfo entry = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);

                if (!entry.Exists)
                {
                    return RepositoryPathResult.Failure(RepositoryPathError.NotFound);
                }

                if (entry.LinkTarget is not null)
                {
                    FileSystemInfo? target = entry.ResolveLinkTarget(returnFinalTarget: true);
                    if (target is null)
                    {
                        return RepositoryPathResult.Failure(RepositoryPathError.InvalidPath);
                    }

                    current = CanonicalizeExistingPath(Path.GetFullPath(target.FullName));
                    if (!IsContained(current))
                    {
                        return RepositoryPathResult.Failure(RepositoryPathError.OutsideRepository);
                    }

                    return RepositoryPathResult.Failure(RepositoryPathError.Symlink);
                }
            }

            FileInfo file = new(current);
            if (!file.Exists)
            {
                return Directory.Exists(current)
                    ? RepositoryPathResult.Failure(RepositoryPathError.NotAFile)
                    : RepositoryPathResult.Failure(RepositoryPathError.NotFound);
            }

            if ((file.Attributes & FileAttributes.Directory) != 0)
            {
                return RepositoryPathResult.Failure(RepositoryPathError.NotAFile);
            }

            string relative = Path.GetRelativePath(CanonicalRoot!, current).Replace('\\', '/');
            return new RepositoryPathResult(current, relative, RepositoryPathError.None);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return RepositoryPathResult.Failure(RepositoryPathError.InvalidPath);
        }
    }

    internal bool IsContained(string candidate)
    {
        string relative = Path.GetRelativePath(CanonicalRoot!, candidate);
        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", PathComparison)
            && !relative.StartsWith(string.Concat("..", Path.DirectorySeparatorChar), PathComparison)
            && !relative.StartsWith(string.Concat("..", Path.AltDirectorySeparatorChar), PathComparison);
    }

    private static string CanonicalizeExistingPath(string path)
    {
        string root = Path.GetPathRoot(path)
            ?? throw new ArgumentException("Path does not have a filesystem root.", nameof(path));
        string current = root;
        string remainder = path[root.Length..];
        foreach (string segment in remainder.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo entry = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (!entry.Exists)
            {
                return Path.GetFullPath(current);
            }

            if (entry.LinkTarget is not null)
            {
                FileSystemInfo target = entry.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new IOException("Symbolic link target is unavailable.");
                current = Path.GetFullPath(target.FullName);
            }
        }

        return Path.GetFullPath(current);
    }
}

public enum RepositoryPathError
{
    None,
    RootRequired,
    InvalidPath,
    OutsideRepository,
    Symlink,
    NotFound,
    NotAFile,
}

public sealed record RepositoryPathResult(string? FullPath, string? RelativePath, RepositoryPathError Error)
{
    public bool IsSuccess => Error == RepositoryPathError.None;

    public static RepositoryPathResult Failure(RepositoryPathError error) => new(null, null, error);
}
