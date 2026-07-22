using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class FindDefinitionServiceTests
{
    [Fact]
    public async Task ResolvesOneExactCaseSensitiveDeclarationWithAllFilters()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("src/Sample.fake", "unique");
        TestDefinitionProvider provider = new();
        FindDefinitionService service = await BuildAsync(repository, provider);
        byte[] before = File.ReadAllBytes(IndexPath(repository));

        ToolResponse<FindDefinitionData> response = await service.FindAsync(
            "Run",
            "method",
            "Acme.Sample",
            "src/Sample.fake",
            null,
            CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, response.Status);
        Assert.Equal(ContractValues.ResolutionUnique, response.Data!.Resolution);
        DefinitionMatch match = Assert.Single(response.Data.Matches);
        Assert.Equal("src/Sample.fake", match.Path);
        Assert.Equal("method", match.Kind);
        Assert.Equal("Run", match.Name);
        Assert.Equal("Acme.Sample", match.Container);
        Assert.Equal(match.Path, Assert.Single(response.Evidence).Path);
        Assert.Equal(before, File.ReadAllBytes(IndexPath(repository)));

        ToolResponse<FindDefinitionData> wrongCase = await service.FindAsync(
            "run",
            null,
            null,
            null,
            null,
            CancellationToken.None);
        Assert.Equal(ContractValues.ResolutionNotFound, wrongCase.Data!.Resolution);
        Assert.Empty(wrongCase.Data.Matches);
    }

    [Fact]
    public async Task ReportsAmbiguityExactCountAndDeterministicBoundedMatches()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("z.fake", "duplicate");
        repository.WriteFile("a.fake", "duplicate");
        repository.WriteFile("m.fake", "duplicate");
        TestDefinitionProvider provider = new();
        FindDefinitionService service = await BuildAsync(repository, provider);

        ToolResponse<FindDefinitionData> response = await service.FindAsync(
            "Run",
            null,
            null,
            null,
            2,
            CancellationToken.None);

        Assert.Equal(ContractValues.ResolutionAmbiguous, response.Data!.Resolution);
        Assert.Equal(3, response.Data.TotalMatches);
        Assert.True(response.Data.Truncated);
        Assert.Equal(["a.fake", "m.fake"], response.Data.Matches.Select(match => match.Path));
    }

    [Fact]
    public async Task ValidatesNamesFiltersPathsAndBoundsBeforeIndexAccess()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "unique");
        FindDefinitionService service = new(
            RepositoryScope.Create(repository.Path),
            [new TestDefinitionProvider()]);

        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.FindAsync(" ", null, null, null, null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.FindAsync("Run", "field", null, null, null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.FindAsync("Run", null, " ", null, null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.FindAsync("Run", null, null, null, 101, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidPath,
            (await service.FindAsync("Run", null, null, "../outside.fake", null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorFileNotFound,
            (await service.FindAsync("Run", null, null, "missing.fake", null, CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task DistinguishesProviderMissingIndexIncompatibleAndStaleFailures()
    {
        using TemporaryDirectory providerRepository = new();
        FindDefinitionService noProvider = new(RepositoryScope.Create(providerRepository.Path), []);
        Assert.Equal(
            ContractValues.ErrorDefinitionProviderUnavailable,
            (await noProvider.FindAsync("Run", null, null, null, null, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory missingRepository = new();
        missingRepository.WriteFile("source.fake", "unique");
        TestDefinitionProvider provider = new();
        FindDefinitionService missing = new(RepositoryScope.Create(missingRepository.Path), [provider]);
        Assert.Equal(
            ContractValues.ErrorIndexMissing,
            (await missing.FindAsync("Run", null, null, null, null, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory incompatibleRepository = new();
        incompatibleRepository.WriteFile("source.fake", "unique");
        await BuildAsync(incompatibleRepository, provider);
        FindDefinitionService incompatible = new(
            RepositoryScope.Create(incompatibleRepository.Path),
            [new TestDefinitionProvider(contractVersion: "2")]);
        Assert.Equal(
            ContractValues.ErrorIndexIncompatible,
            (await incompatible.FindAsync("Run", null, null, null, null, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory staleRepository = new();
        staleRepository.WriteFile("source.fake", "unique");
        FindDefinitionService stale = await BuildAsync(staleRepository, provider);
        staleRepository.WriteFile("source.fake", "changed");
        Assert.Equal(
            ContractValues.ErrorIndexStale,
            (await stale.FindAsync("Run", null, null, null, null, CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task PreservesPartialEvidenceAndCancellationSemantics()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", new string('x', 300) + "Run" + new string('y', 300));
        TestDefinitionProvider provider = new(diagnostics: 2, truncate: true);
        FindDefinitionService service = await BuildAsync(repository, provider);

        ToolResponse<FindDefinitionData> partial = await service.FindAsync(
            "Run",
            null,
            null,
            null,
            null,
            CancellationToken.None);

        Assert.Equal(ContractValues.StatusPartial, partial.Status);
        Assert.Equal(DefinitionLookupLimits.MaximumSnippetCharacters, Assert.Single(partial.Data!.Matches).Snippet.Length);
        Assert.Contains("index_syntax_diagnostics_recovered:2", partial.Warnings);
        Assert.Contains("index_chunk_content_truncated:1", partial.Warnings);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(
            ContractValues.ErrorCancelled,
            (await service.FindAsync("Run", null, null, null, null, cancellation.Token)).Error!.Code);
    }

    private static async Task<FindDefinitionService> BuildAsync(
        TemporaryDirectory repository,
        TestDefinitionProvider provider)
    {
        RepositoryScope scope = RepositoryScope.Create(repository.Path);
        ToolResponse<IndexCodebaseData> built = await new IndexCodebaseService(
            scope,
            [provider],
            "test").RebuildAsync(CancellationToken.None);
        Assert.NotEqual(ContractValues.StatusError, built.Status);
        return new FindDefinitionService(scope, [provider]);
    }

    private static string IndexPath(TemporaryDirectory repository) =>
        System.IO.Path.Combine(repository.Path, ".sanjaya", "index-v1.json");

    private sealed class TestDefinitionProvider(
        string contractVersion = "1",
        int diagnostics = 0,
        bool truncate = false) : IStructuralChunkProvider
    {
        public string Id => "test-definitions";

        public string ContractVersion => contractVersion;

        public IReadOnlyCollection<string> Languages { get; } = ["csharp"];

        public bool CanHandle(string relativePath) =>
            relativePath.EndsWith(".fake", StringComparison.Ordinal);

        public IReadOnlyCollection<CapabilityDescriptor> GetCapabilities() =>
        [
            new(CapabilityKind.StructuralChunking, Id, "csharp", CapabilityStatus.Supported),
            new(CapabilityKind.Definitions, Id, "csharp", CapabilityStatus.Supported),
        ];

        public StructuralChunkAnalysis AnalyzeChunks(
            string relativePath,
            string sourceText,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content = sourceText.Contains("Run", StringComparison.Ordinal)
                ? sourceText
                : "public void Run() { }";
            return new StructuralChunkAnalysis(
                [new StructuralChunk("method", "Run", "Acme.Sample", 1, 1, content, truncate)],
                false,
                diagnostics);
        }
    }
}
