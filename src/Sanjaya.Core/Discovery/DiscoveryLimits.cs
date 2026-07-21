namespace Sanjaya.Core.Discovery;

/// <summary>
/// Public finite bounds for immediate, non-indexed discovery.
/// </summary>
public static class DiscoveryLimits
{
    public const int MaximumQueryCharacters = 256;
    public const int DefaultResults = 50;
    public const int MaximumResults = 200;
    public const int MaximumFiles = 10_000;
    public const int MaximumDirectories = 2_000;
    public const int MaximumFileSystemEntries = 20_000;
    public const long MaximumSearchBytes = 8 * 1024 * 1024;
    public const int MaximumFileBytes = 1024 * 1024;
    public const int MaximumLineCharacters = 16 * 1024;
    public const int MaximumMatchesPerLine = 20;
    public const int MaximumMatchesPerFile = 50;
    public const int MaximumSnippetCharacters = 320;
    public const int MaximumPreviewLines = 20;
    public const int MaximumPreviewCharactersPerLine = 240;
    public const int MaximumOutlineItems = 500;
    public const int MaximumOutlineDisplayCharacters = 240;
    public const int MaximumChunkCharacters = 64 * 1024;
}
