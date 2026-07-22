using Sanjaya.Core.Contracts;

namespace Sanjaya.Core.Repositories;

/// <summary>
/// Immutable, canonical repository scope for one server process.
/// </summary>
public sealed class RepositoryScope
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private RepositoryScope(
        string? canonicalRoot,
        string? configurationReason,
        string? configurationError,
        string? configurationRemediation)
    {
        CanonicalRoot = canonicalRoot;
        ConfigurationReason = configurationReason;
        ConfigurationError = configurationError;
        ConfigurationRemediation = configurationRemediation;
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

    public string? ConfigurationReason { get; }

    public string? ConfigurationRemediation { get; }

    internal string? CanonicalRoot { get; }

    public static RepositoryScope Create(
        string? configuredRoot,
        RepositoryConfigurationFailure parsingFailure = RepositoryConfigurationFailure.None)
    {
        if (parsingFailure != RepositoryConfigurationFailure.None)
        {
            return Failure(parsingFailure);
        }

        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Failure(RepositoryConfigurationFailure.Missing);
        }

        if (!Path.IsPathFullyQualified(configuredRoot))
        {
            return Failure(RepositoryConfigurationFailure.Relative);
        }

        try
        {
            string absolute = CanonicalizeExistingPath(Path.GetFullPath(configuredRoot));
            DirectoryInfo directory = new(absolute);
            if (!directory.Exists)
            {
                return File.Exists(absolute)
                    ? Failure(RepositoryConfigurationFailure.NotDirectory)
                    : Failure(RepositoryConfigurationFailure.NotFound);
            }

            if ((directory.Attributes & FileAttributes.Directory) == 0)
            {
                return Failure(RepositoryConfigurationFailure.NotDirectory);
            }

            return new(Path.TrimEndingDirectorySeparator(absolute), null, null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(RepositoryConfigurationFailure.Inaccessible);
        }
        catch (IOException)
        {
            return Failure(RepositoryConfigurationFailure.Inaccessible);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Failure(RepositoryConfigurationFailure.Invalid);
        }
    }

    private static RepositoryScope Failure(RepositoryConfigurationFailure failure)
    {
        (string reason, string message, string remediation) = failure switch
        {
            RepositoryConfigurationFailure.Missing => (
                ContractValues.ReasonRepositoryRootRequired,
                "No repository root was configured.",
                "Restart Sanjaya with --root <absolute-path>."),
            RepositoryConfigurationFailure.MissingValue => (
                ContractValues.ReasonRepositoryRootValueMissing,
                "The --root argument is missing its value.",
                "Use --root <absolute-path>."),
            RepositoryConfigurationFailure.Duplicate => (
                ContractValues.ReasonRepositoryRootDuplicate,
                "The --root argument was supplied more than once.",
                "Configure exactly one --root <absolute-path> argument."),
            RepositoryConfigurationFailure.UnknownArgument => (
                ContractValues.ReasonRepositoryRootUnknownArgument,
                "An unsupported Sanjaya argument was supplied.",
                "Use only --root <absolute-path>, or run sanjaya-mcp --help."),
            RepositoryConfigurationFailure.Relative => (
                ContractValues.ReasonRepositoryRootRelative,
                "The configured repository root is not an absolute path.",
                "Restart Sanjaya with --root <absolute-path>."),
            RepositoryConfigurationFailure.NotFound => (
                ContractValues.ReasonRepositoryRootNotFound,
                "The configured repository root does not exist.",
                "Choose an existing repository directory and restart Sanjaya."),
            RepositoryConfigurationFailure.NotDirectory => (
                ContractValues.ReasonRepositoryRootNotDirectory,
                "The configured repository root is not a directory.",
                "Choose a repository directory and restart Sanjaya."),
            RepositoryConfigurationFailure.Inaccessible => (
                ContractValues.ReasonRepositoryRootInaccessible,
                "The configured repository root is inaccessible.",
                "Grant read access to the repository directory and restart Sanjaya."),
            _ => (
                ContractValues.ReasonRepositoryRootInvalid,
                "The configured repository root is invalid.",
                "Choose a valid absolute repository directory and restart Sanjaya."),
        };

        return new(null, reason, message, remediation);
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

                if (!entry.Exists)
                {
                    return RepositoryPathResult.Failure(RepositoryPathError.NotFound);
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

public enum RepositoryConfigurationFailure
{
    None,
    Missing,
    MissingValue,
    Duplicate,
    UnknownArgument,
    Relative,
    NotFound,
    NotDirectory,
    Inaccessible,
    Invalid,
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
