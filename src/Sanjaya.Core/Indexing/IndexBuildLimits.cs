namespace Sanjaya.Core.Indexing;

public sealed record IndexBuildLimits(
    int MaximumEligibleFiles,
    long MaximumSourceBytes,
    int MaximumChunks,
    int MaximumOutputBytes,
    int MaximumDirectories,
    int MaximumFileSystemEntries)
{
    public static IndexBuildLimits Default { get; } = new(
        MaximumEligibleFiles: 5_000,
        MaximumSourceBytes: 64L * 1024 * 1024,
        MaximumChunks: 50_000,
        MaximumOutputBytes: 64 * 1024 * 1024,
        MaximumDirectories: Discovery.DiscoveryLimits.MaximumDirectories,
        MaximumFileSystemEntries: Discovery.DiscoveryLimits.MaximumFileSystemEntries);
}
