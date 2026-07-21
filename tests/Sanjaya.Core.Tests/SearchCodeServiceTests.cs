using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class SearchCodeServiceTests
{
    [Fact]
    public async Task RanksEveryFieldTierDeterministicallyAndReportsExactTruncation()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("exact.fake", "quiet");
        repository.WriteFile("prefix.fake", "quiet");
        repository.WriteFile("substring.fake", "quiet");
        repository.WriteFile("container.fake", "quiet");
        repository.WriteFile("kind.fake", "quiet");
        repository.WriteFile("RunPath.fake", "quiet");
        repository.WriteFile("content.fake", "Run only in content");
        TestStructuralProvider provider = new();
        SearchCodeService search = await BuildAsync(repository, provider);
        byte[] before = File.ReadAllBytes(IndexPath(repository));

        ToolResponse<SearchCodeData> response = await search.SearchAsync(
            "Run",
            caseSensitive: false,
            requestedMaximumResults: 5,
            CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, response.Status);
        Assert.Equal(7, response.Data!.TotalMatches);
        Assert.True(response.Data.Truncated);
        Assert.Equal(
            [1000, 800, 600, 400, 300],
            response.Data.Matches.Select(match => match.Score));
        Assert.Equal(
            ["exact.fake", "prefix.fake", "substring.fake", "container.fake", "kind.fake"],
            response.Data.Matches.Select(match => match.Path));
        Assert.Equal(response.Data.Matches.Count, response.Evidence.Count);
        Assert.Equal(before, File.ReadAllBytes(IndexPath(repository)));
    }

    [Fact]
    public async Task RequiresEveryDistinctTermAndHonorsOrdinalCaseSensitivity()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("multi.fake", "quiet");
        TestStructuralProvider provider = new();
        SearchCodeService search = await BuildAsync(repository, provider);

        ToolResponse<SearchCodeData> insensitive = await search.SearchAsync(
            " run   ALPHA run ",
            false,
            null,
            CancellationToken.None);
        ToolResponse<SearchCodeData> sensitive = await search.SearchAsync(
            "run ALPHA",
            true,
            null,
            CancellationToken.None);

        CodeSearchMatch match = Assert.Single(insensitive.Data!.Matches);
        Assert.Equal("run   ALPHA run", insensitive.Data.Query);
        Assert.Equal(1400, match.Score);
        Assert.Equal(["name", "container"], match.MatchedFields);
        Assert.Empty(sensitive.Data!.Matches);
    }

    [Fact]
    public async Task ReturnsBoundedSnippetAndPartialWarningsFromIndexEvidence()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("long.fake", new string('x', 300) + "NEEDLE" + new string('y', 300));
        TestStructuralProvider provider = new(diagnostics: 2, truncate: true);
        SearchCodeService search = await BuildAsync(repository, provider);

        ToolResponse<SearchCodeData> response = await search.SearchAsync(
            "NEEDLE",
            true,
            null,
            CancellationToken.None);

        CodeSearchMatch match = Assert.Single(response.Data!.Matches);
        Assert.Equal(ContractValues.StatusPartial, response.Status);
        Assert.Equal(SearchCodeLimits.MaximumSnippetCharacters, match.Snippet.Length);
        Assert.Contains("NEEDLE", match.Snippet, StringComparison.Ordinal);
        Assert.Contains("index_syntax_diagnostics_recovered:2", response.Warnings);
        Assert.Contains("index_chunk_content_truncated:1", response.Warnings);
    }

    [Fact]
    public async Task DistinguishesMissingCorruptIncompatibleAndStaleIndexes()
    {
        using TemporaryDirectory missingRepository = new();
        missingRepository.WriteFile("source.fake", "quiet");
        TestStructuralProvider provider = new();
        ToolResponse<SearchCodeData> missing = await new SearchCodeService(
            RepositoryScope.Create(missingRepository.Path),
            [provider]).SearchAsync("source", false, null, CancellationToken.None);

        Assert.Equal(ContractValues.ErrorIndexMissing, missing.Error!.Code);

        using TemporaryDirectory corruptRepository = new();
        corruptRepository.WriteFile("source.fake", "quiet");
        SearchCodeService corruptSearch = await BuildAsync(corruptRepository, provider);
        File.AppendAllText(IndexPath(corruptRepository), " ");
        ToolResponse<SearchCodeData> corrupt = await corruptSearch.SearchAsync(
            "source",
            false,
            null,
            CancellationToken.None);
        Assert.Equal(ContractValues.ErrorIndexCorrupt, corrupt.Error!.Code);

        using TemporaryDirectory incompatibleRepository = new();
        incompatibleRepository.WriteFile("source.fake", "quiet");
        await BuildAsync(incompatibleRepository, provider);
        TestStructuralProvider newerProvider = new(contractVersion: "2");
        ToolResponse<SearchCodeData> incompatible = await new SearchCodeService(
            RepositoryScope.Create(incompatibleRepository.Path),
            [newerProvider]).SearchAsync("source", false, null, CancellationToken.None);
        Assert.Equal(ContractValues.ErrorIndexIncompatible, incompatible.Error!.Code);

        using TemporaryDirectory staleRepository = new();
        staleRepository.WriteFile("source.fake", "quiet");
        SearchCodeService staleSearch = await BuildAsync(staleRepository, provider);
        staleRepository.WriteFile("source.fake", "changed");
        ToolResponse<SearchCodeData> stale = await staleSearch.SearchAsync(
            "source",
            false,
            null,
            CancellationToken.None);
        Assert.Equal(ContractValues.ErrorIndexStale, stale.Error!.Code);
    }

    [Fact]
    public async Task RejectsUnverifiableStateInvalidArgumentsAndCancellation()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "quiet");
        TestStructuralProvider provider = new();
        SearchCodeService search = await BuildAsync(repository, provider);
        File.WriteAllBytes(System.IO.Path.Combine(repository.Path, "source.fake"), [0, 1, 2]);

        ToolResponse<SearchCodeData> unverifiable = await search.SearchAsync(
            "source",
            false,
            null,
            CancellationToken.None);
        Assert.Equal(ContractValues.ErrorIndexStateUnverifiable, unverifiable.Error!.Code);

        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await search.SearchAsync("\n", false, null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await search.SearchAsync("source", false, 101, CancellationToken.None)).Error!.Code);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(
            ContractValues.ErrorCancelled,
            (await search.SearchAsync("source", false, null, cancellation.Token)).Error!.Code);
    }

    [Fact]
    public async Task RejectsSymlinkedIndexStorageWithoutReadingItsTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using TemporaryDirectory repository = new();
        using TemporaryDirectory outside = new();
        repository.WriteFile("source.fake", "quiet");
        TestStructuralProvider provider = new();
        SearchCodeService search = await BuildAsync(repository, provider);
        string indexPath = IndexPath(repository);
        File.Delete(indexPath);
        string outsidePath = outside.WriteFile("outside.json", "sensitive");
        File.CreateSymbolicLink(indexPath, outsidePath);

        ToolResponse<SearchCodeData> response = await search.SearchAsync(
            "sensitive",
            false,
            null,
            CancellationToken.None);

        Assert.Equal(ContractValues.ErrorIndexCorrupt, response.Error!.Code);
        Assert.DoesNotContain(outside.Path, response.Error.Message, StringComparison.Ordinal);
    }

    private static async Task<SearchCodeService> BuildAsync(
        TemporaryDirectory repository,
        TestStructuralProvider provider)
    {
        RepositoryScope scope = RepositoryScope.Create(repository.Path);
        ToolResponse<IndexCodebaseData> built = await new IndexCodebaseService(
            scope,
            [provider],
            "test").RebuildAsync(CancellationToken.None);
        Assert.NotEqual(ContractValues.StatusError, built.Status);
        return new SearchCodeService(scope, [provider]);
    }

    private static string IndexPath(TemporaryDirectory repository) =>
        System.IO.Path.Combine(repository.Path, ".sanjaya", "index-v1.json");

    private sealed class TestStructuralProvider(
        string contractVersion = "1",
        int diagnostics = 0,
        bool truncate = false) : IStructuralChunkProvider
    {
        public string Id => "test-syntax";

        public string ContractVersion => contractVersion;

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
            string stem = System.IO.Path.GetFileNameWithoutExtension(relativePath);
            StructuralChunk chunk = stem switch
            {
                "exact" => Create("type", "Run", null, "quiet"),
                "prefix" => Create("type", "Runner", null, "quiet"),
                "substring" => Create("type", "PreRunPost", null, "quiet"),
                "container" => Create("type", "Other", "Run", "quiet"),
                "kind" => Create("RunKind", "Other", null, "quiet"),
                "RunPath" => Create("type", "Other", null, "quiet"),
                "multi" => Create("method", "Run", "Alpha", "quiet"),
                _ => Create("type", stem, null, sourceText),
            };
            return new StructuralChunkAnalysis([chunk], false, diagnostics);
        }

        private StructuralChunk Create(string kind, string name, string? container, string content) =>
            new(kind, name, container, 1, 1, content, truncate);
    }
}
