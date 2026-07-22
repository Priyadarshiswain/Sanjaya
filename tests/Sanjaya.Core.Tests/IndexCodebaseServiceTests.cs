using System.Text.Json;
using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class IndexCodebaseServiceTests
{
    [Fact]
    public async Task RebuildsByteForByteDeterministicallyWithCanonicalRelativeRecords()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile(".gitignore", ".sanjaya/\n");
        repository.WriteFile("zeta.fake", "zeta");
        repository.WriteFile("src/alpha.fake", "alpha");
        repository.WriteFile("notes.txt", "unsupported");
        IndexCodebaseService service = CreateService(repository);

        ToolResponse<IndexCodebaseData> first = await service.RebuildAsync(CancellationToken.None);
        byte[] firstBytes = File.ReadAllBytes(IndexPath(repository));
        ToolResponse<IndexCodebaseData> second = await service.RebuildAsync(CancellationToken.None);
        byte[] secondBytes = File.ReadAllBytes(IndexPath(repository));

        Assert.Equal(ContractValues.StatusOk, first.Status);
        Assert.Equal("missing", first.Data!.PreviousIndexState);
        Assert.Equal("current", second.Data!.PreviousIndexState);
        Assert.Equal(first.Data.RepositoryFingerprint, second.Data.RepositoryFingerprint);
        Assert.Equal(firstBytes, secondBytes);
        Assert.Equal(2, first.Data.FilesIndexed);
        Assert.Equal(2, first.Data.ChunksIndexed);
        Assert.Equal(2, first.Data.UnsupportedFiles);
        Assert.DoesNotContain("index_directory_not_explicitly_ignored", first.Warnings);

        using JsonDocument document = JsonDocument.Parse(firstBytes);
        JsonElement root = document.RootElement;
        Assert.Equal("sanjaya", root.GetProperty("owner").GetString());
        Assert.Equal("1", root.GetProperty("formatVersion").GetString());
        Assert.Equal(
            ["src/alpha.fake", "zeta.fake"],
            root.GetProperty("files").EnumerateArray().Select(file => file.GetProperty("path").GetString()));
        Assert.All(
            root.GetProperty("chunks").EnumerateArray(),
            chunk => Assert.StartsWith("sha256:", chunk.GetProperty("id").GetString(), StringComparison.Ordinal));
        string serialized = System.Text.Encoding.UTF8.GetString(firstBytes);
        Assert.DoesNotContain(repository.Path, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("timestamp", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationAndFailedReplacementPreserveThePreviousGoodIndex()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "first");
        IndexCodebaseService service = CreateService(repository);
        Assert.Equal(ContractValues.StatusOk, (await service.RebuildAsync(CancellationToken.None)).Status);
        byte[] original = File.ReadAllBytes(IndexPath(repository));
        repository.WriteFile("source.fake", "changed");

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        ToolResponse<IndexCodebaseData> cancelled = await service.RebuildAsync(cancellation.Token);
        IndexBuildLimits tinyOutput = IndexBuildLimits.Default with { MaximumOutputBytes = 10 };
        ToolResponse<IndexCodebaseData> limited = await CreateService(repository, tinyOutput)
            .RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.ErrorCancelled, cancelled.Error!.Code);
        Assert.Equal(ContractValues.ErrorIndexOutputLimit, limited.Error!.Code);
        Assert.Equal(original, File.ReadAllBytes(IndexPath(repository)));
        Assert.Empty(Directory.EnumerateFiles(
            System.IO.Path.Combine(repository.Path, ".sanjaya"),
            "*.tmp"));
    }

    [Fact]
    public async Task ClassifiesStaleAndIncompatiblePreviousIndexesBeforeReplacement()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "first");
        IndexCodebaseService service = CreateService(repository);
        await service.RebuildAsync(CancellationToken.None);
        repository.WriteFile("source.fake", "changed");

        ToolResponse<IndexCodebaseData> stale = await service.RebuildAsync(CancellationToken.None);

        Assert.Equal("stale", stale.Data!.PreviousIndexState);

        File.WriteAllText(
            IndexPath(repository),
            "{\"owner\":\"sanjaya\",\"formatVersion\":\"99\",\"producer\":{\"name\":\"sanjaya\",\"version\":\"old\"},\"repositoryFingerprint\":\"old\",\"providers\":[]}");
        ToolResponse<IndexCodebaseData> incompatible = await service.RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, incompatible.Status);
        Assert.Equal("incompatible", incompatible.Data!.PreviousIndexState);
        using JsonDocument replaced = JsonDocument.Parse(File.ReadAllBytes(IndexPath(repository)));
        Assert.Equal("1", replaced.RootElement.GetProperty("formatVersion").GetString());
    }

    [Fact]
    public async Task RejectsUnknownTargetsAndSymlinkedStorageWithoutOverwrite()
    {
        using TemporaryDirectory conflictRepository = new();
        conflictRepository.WriteFile("source.fake", "source");
        string unknown = conflictRepository.WriteFile(".sanjaya/index-v1.json", "user-owned");

        ToolResponse<IndexCodebaseData> conflict = await CreateService(conflictRepository)
            .RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.ErrorIndexPathConflict, conflict.Error!.Code);
        Assert.Equal("user-owned", File.ReadAllText(unknown));

        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory symlinkRepository = new();
        using TemporaryDirectory outside = new();
        symlinkRepository.WriteFile("source.fake", "source");
        Directory.CreateSymbolicLink(
            System.IO.Path.Combine(symlinkRepository.Path, ".sanjaya"),
            outside.Path);

        ToolResponse<IndexCodebaseData> symlink = await CreateService(symlinkRepository)
            .RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.ErrorIndexPathConflict, symlink.Error!.Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside.Path));
        Assert.DoesNotContain(outside.Path, symlink.Error.Message, StringComparison.Ordinal);

        using TemporaryDirectory brokenTargetRepository = new();
        brokenTargetRepository.WriteFile("source.fake", "source");
        Directory.CreateDirectory(System.IO.Path.Combine(brokenTargetRepository.Path, ".sanjaya"));
        string missingTarget = System.IO.Path.Combine(outside.Path, "missing-index.json");
        File.CreateSymbolicLink(IndexPath(brokenTargetRepository), missingTarget);

        ToolResponse<IndexCodebaseData> brokenTarget = await CreateService(brokenTargetRepository)
            .RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.ErrorIndexPathConflict, brokenTarget.Error!.Code);
        Assert.False(File.Exists(missingTarget));
    }

    [Fact]
    public async Task RejectsConcurrentWritersWithoutWaiting()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "source");
        string indexDirectory = System.IO.Path.Combine(repository.Path, ".sanjaya");
        Directory.CreateDirectory(indexDirectory);
        await using FileStream heldLock = new(
            System.IO.Path.Combine(indexDirectory, "index.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        ToolResponse<IndexCodebaseData> response = await CreateService(repository)
            .RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.ErrorIndexBusy, response.Error!.Code);
        Assert.False(File.Exists(IndexPath(repository)));
    }

    [Fact]
    public async Task HardFileAndChunkBoundsNeverPromotePartialIndexes()
    {
        using TemporaryDirectory filesRepository = new();
        filesRepository.WriteFile("one.fake", "one");
        filesRepository.WriteFile("two.fake", "two");
        IndexBuildLimits oneFile = IndexBuildLimits.Default with { MaximumEligibleFiles = 1 };

        ToolResponse<IndexCodebaseData> fileLimit = await CreateService(filesRepository, oneFile)
            .RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.ErrorIndexFileLimit, fileLimit.Error!.Code);
        Assert.False(File.Exists(IndexPath(filesRepository)));

        using TemporaryDirectory chunksRepository = new();
        chunksRepository.WriteFile("many.fake", "two-chunks");
        FakeStructuralProvider provider = new(twoChunks: true);
        IndexBuildLimits oneChunk = IndexBuildLimits.Default with { MaximumChunks = 1 };
        IndexCodebaseService service = new(
            RepositoryScope.Create(chunksRepository.Path),
            [provider],
            "test",
            oneChunk);

        ToolResponse<IndexCodebaseData> chunkLimit = await service.RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.ErrorIndexChunkLimit, chunkLimit.Error!.Code);
        Assert.False(File.Exists(IndexPath(chunksRepository)));
    }

    [Fact]
    public async Task SourceTraversalAndUnreadableBoundsFailWithoutPromotion()
    {
        using TemporaryDirectory sourceRepository = new();
        sourceRepository.WriteFile("source.fake", "four");
        IndexBuildLimits threeBytes = IndexBuildLimits.Default with { MaximumSourceBytes = 3 };
        ToolResponse<IndexCodebaseData> sourceLimit = await CreateService(sourceRepository, threeBytes)
            .RebuildAsync(CancellationToken.None);
        Assert.Equal(ContractValues.ErrorIndexSourceLimit, sourceLimit.Error!.Code);
        Assert.False(File.Exists(IndexPath(sourceRepository)));

        using TemporaryDirectory traversalRepository = new();
        traversalRepository.WriteFile("nested/source.fake", "source");
        IndexBuildLimits rootOnly = IndexBuildLimits.Default with { MaximumDirectories = 1 };
        ToolResponse<IndexCodebaseData> traversalLimit = await CreateService(traversalRepository, rootOnly)
            .RebuildAsync(CancellationToken.None);
        Assert.Equal(ContractValues.ErrorIndexTraversalLimit, traversalLimit.Error!.Code);
        Assert.False(File.Exists(IndexPath(traversalRepository)));

        using TemporaryDirectory binaryRepository = new();
        File.WriteAllBytes(System.IO.Path.Combine(binaryRepository.Path, "binary.fake"), [0, 1, 2]);
        ToolResponse<IndexCodebaseData> unreadable = await CreateService(binaryRepository)
            .RebuildAsync(CancellationToken.None);
        Assert.Equal(ContractValues.ErrorIndexSourceUnreadable, unreadable.Error!.Code);
        Assert.False(File.Exists(IndexPath(binaryRepository)));
    }

    [Fact]
    public async Task RecoveredSyntaxAndBoundedChunkContentProduceExplicitPartialIndex()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("broken.fake", "broken");
        FakeStructuralProvider provider = new(truncateContent: true);
        IndexCodebaseService service = new(
            RepositoryScope.Create(repository.Path),
            [provider],
            "test");

        ToolResponse<IndexCodebaseData> response = await service.RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.StatusPartial, response.Status);
        Assert.Equal(1, response.Data!.SyntaxDiagnosticCount);
        Assert.Equal(1, response.Data.TruncatedChunkCount);
        Assert.Contains("syntax_diagnostics_recovered:1", response.Warnings);
        Assert.Contains("chunk_content_truncated:1", response.Warnings);
        Assert.Contains("index_directory_not_explicitly_ignored", response.Warnings);
        Assert.False(File.Exists(System.IO.Path.Combine(repository.Path, ".gitignore")));
    }

    [Fact]
    public async Task MissingRootAndMissingProviderReturnStableErrorsWithoutWriting()
    {
        IndexCodebaseService missingRoot = new(
            RepositoryScope.Create(null),
            [new FakeStructuralProvider()],
            "test");
        Assert.Equal(
            ContractValues.ErrorRepositoryRootRequired,
            (await missingRoot.RebuildAsync(CancellationToken.None)).Error!.Code);

        using TemporaryDirectory repository = new();
        IndexCodebaseService missingProvider = new(
            RepositoryScope.Create(repository.Path),
            [],
            "test");
        Assert.Equal(
            ContractValues.ErrorStructuralProviderUnavailable,
            (await missingProvider.RebuildAsync(CancellationToken.None)).Error!.Code);
        Assert.False(Directory.Exists(System.IO.Path.Combine(repository.Path, ".sanjaya")));
    }

    [Theory]
    [InlineData(StructuralProviderFailure.Unavailable, ContractValues.ErrorStructuralProviderUnavailable)]
    [InlineData(StructuralProviderFailure.TimedOut, ContractValues.ErrorStructuralProviderTimeout)]
    [InlineData(StructuralProviderFailure.OutputLimit, ContractValues.ErrorStructuralProviderOutputLimit)]
    [InlineData(StructuralProviderFailure.InvalidOutput, ContractValues.ErrorStructuralProviderInvalidOutput)]
    public async Task ProviderFailurePreservesThePreviousGoodIndex(
        StructuralProviderFailure failure,
        string expectedCode)
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "first");
        Assert.Equal(
            ContractValues.StatusOk,
            (await CreateService(repository).RebuildAsync(CancellationToken.None)).Status);
        byte[] original = File.ReadAllBytes(IndexPath(repository));
        repository.WriteFile("source.fake", "SECRET_CHANGED_SOURCE");
        IndexCodebaseService failing = new(
            RepositoryScope.Create(repository.Path),
            [new FakeStructuralProvider(failure: failure)],
            "test");

        ToolResponse<IndexCodebaseData> response = await failing.RebuildAsync(CancellationToken.None);

        Assert.Equal(ContractValues.StatusError, response.Status);
        Assert.Equal(expectedCode, response.Error!.Code);
        Assert.Equal(original, File.ReadAllBytes(IndexPath(repository)));
        Assert.Empty(Directory.EnumerateFiles(
            System.IO.Path.Combine(repository.Path, ".sanjaya"),
            "*.tmp"));
        Assert.DoesNotContain("SECRET_CHANGED_SOURCE", response.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(repository.Path, response.Error.Message, StringComparison.Ordinal);
    }

    private static IndexCodebaseService CreateService(
        TemporaryDirectory repository,
        IndexBuildLimits? limits = null) =>
        new(
            RepositoryScope.Create(repository.Path),
            [new FakeStructuralProvider()],
            "test",
            limits);

    private static string IndexPath(TemporaryDirectory repository) =>
        System.IO.Path.Combine(repository.Path, ".sanjaya", "index-v1.json");

    private sealed class FakeStructuralProvider(
        bool twoChunks = false,
        bool truncateContent = false,
        StructuralProviderFailure? failure = null) : IStructuralChunkProvider
    {
        public string Id => "fake-syntax";

        public string ContractVersion => "1";

        public IReadOnlyCollection<string> Languages { get; } = ["fake"];

        public bool CanHandle(string relativePath) =>
            relativePath.EndsWith(".fake", StringComparison.Ordinal);

        public IReadOnlyCollection<CapabilityDescriptor> GetCapabilities() =>
            [new(CapabilityKind.StructuralChunking, Id, "fake", CapabilityStatus.Supported)];

        public StructuralChunkAnalysis AnalyzeChunks(
            string relativePath,
            string sourceText,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failure is not null)
            {
                throw new StructuralProviderException(failure.Value);
            }

            List<StructuralChunk> chunks =
            [
                new("type", System.IO.Path.GetFileNameWithoutExtension(relativePath), null, 1, 1, sourceText, truncateContent),
            ];
            if (twoChunks)
            {
                chunks.Add(new("method", "Second", null, 1, 1, sourceText, false));
            }

            return new StructuralChunkAnalysis(
                chunks,
                false,
                sourceText.Contains("broken", StringComparison.Ordinal) ? 1 : 0);
        }
    }
}
