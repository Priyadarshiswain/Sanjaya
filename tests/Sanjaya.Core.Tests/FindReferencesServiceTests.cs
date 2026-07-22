using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class FindReferencesServiceTests
{
    [Fact]
    public async Task ReturnsDeterministicBoundedSyntaxCandidatesAndPathScope()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("z.fake", "two");
        repository.WriteFile("a.fake", "one");
        TestReferenceProvider provider = new();
        FindReferencesService service = await BuildAsync(repository, provider);

        ToolResponse<FindReferencesData> all = await service.FindAsync("Run", null, 2, CancellationToken.None);
        ToolResponse<FindReferencesData> scoped = await service.FindAsync("Run", "z.fake", null, CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, all.Status);
        Assert.Equal(ReferenceLookupLimits.Classification, all.Data!.Classification);
        Assert.Equal(3, all.Data.TotalMatches);
        Assert.True(all.Data.Truncated);
        Assert.Equal(["a.fake", "z.fake"], all.Data.Matches.Select(match => match.Path));
        Assert.All(all.Data.Matches, match => Assert.Equal("syntax_candidate", match.Classification));
        Assert.Equal(2, all.Data.FilesScanned);
        Assert.Equal(2, scoped.Data!.TotalMatches);
        Assert.All(scoped.Data.Matches, match => Assert.Equal("z.fake", match.Path));
    }

    [Fact]
    public async Task ValidatesInputAndReportsProviderIndexAndStaleFailures()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "one");
        RepositoryScope scope = RepositoryScope.Create(repository.Path);
        FindReferencesService noProvider = new(scope, [], []);
        Assert.Equal(
            ContractValues.ErrorReferenceProviderUnavailable,
            (await noProvider.FindAsync("Run", null, null, CancellationToken.None)).Error!.Code);

        TestReferenceProvider provider = new();
        FindReferencesService missing = new(scope, [provider], [provider]);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await missing.FindAsync("two words", null, null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await missing.FindAsync("Run", null, 201, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidPath,
            (await missing.FindAsync("Run", "../outside.fake", null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorIndexMissing,
            (await missing.FindAsync("Run", null, null, CancellationToken.None)).Error!.Code);

        FindReferencesService stale = await BuildAsync(repository, provider);
        repository.WriteFile("source.fake", "changed");
        Assert.Equal(
            ContractValues.ErrorIndexStale,
            (await stale.FindAsync("Run", null, null, CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task ReportsSyntaxRecoveryLimitsAndCancellationHonestly()
    {
        using TemporaryDirectory partialRepository = new();
        partialRepository.WriteFile("source.fake", "one");
        TestReferenceProvider partialProvider = new(diagnostics: 2);
        FindReferencesService partialService = await BuildAsync(partialRepository, partialProvider);
        ToolResponse<FindReferencesData> partial = await partialService.FindAsync(
            "Run", null, null, CancellationToken.None);
        Assert.Equal(ContractValues.StatusPartial, partial.Status);
        Assert.Equal(2, partial.Data!.SyntaxDiagnosticCount);
        Assert.Contains("csharp_syntax_diagnostics_recovered:2", partial.Warnings);

        using TemporaryDirectory limitedRepository = new();
        limitedRepository.WriteFile("source.fake", "one");
        TestReferenceProvider limitedProvider = new(truncated: true);
        FindReferencesService limitedService = await BuildAsync(limitedRepository, limitedProvider);
        Assert.Equal(
            ContractValues.ErrorReferenceLimit,
            (await limitedService.FindAsync("Run", null, null, CancellationToken.None)).Error!.Code);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(
            ContractValues.ErrorCancelled,
            (await partialService.FindAsync("Run", null, null, cancellation.Token)).Error!.Code);
    }

    private static async Task<FindReferencesService> BuildAsync(
        TemporaryDirectory repository,
        TestReferenceProvider provider)
    {
        RepositoryScope scope = RepositoryScope.Create(repository.Path);
        ToolResponse<IndexCodebaseData> built = await new IndexCodebaseService(
            scope, [provider], "test").RebuildAsync(CancellationToken.None);
        Assert.NotEqual(ContractValues.StatusError, built.Status);
        return new FindReferencesService(scope, [provider], [provider]);
    }

    private sealed class TestReferenceProvider(
        int diagnostics = 0,
        bool truncated = false) : IStructuralChunkProvider, IReferenceProvider
    {
        public string Id => "test-references";

        public string ContractVersion => "1";

        public IReadOnlyCollection<string> Languages { get; } = ["csharp"];

        public bool CanHandle(string relativePath) => relativePath.EndsWith(".fake", StringComparison.Ordinal);

        public bool IsValidName(string name) => name.All(char.IsLetterOrDigit);

        public IReadOnlyCollection<CapabilityDescriptor> GetCapabilities() =>
        [
            new(CapabilityKind.StructuralChunking, Id, "csharp", CapabilityStatus.Supported),
            new(CapabilityKind.References, Id, "csharp", CapabilityStatus.Supported),
        ];

        public StructuralChunkAnalysis AnalyzeChunks(
            string relativePath, string sourceText, CancellationToken cancellationToken) =>
            new([new StructuralChunk("method", "Host", null, 1, 1, sourceText, false)], false, 0);

        public ReferenceAnalysis AnalyzeReferences(
            string relativePath,
            string sourceText,
            string name,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = sourceText == "two" ? 2 : 1;
            SyntaxReferenceCandidate[] matches = Enumerable.Range(1, count)
                .Select(line => new SyntaxReferenceCandidate(
                    "identifier_name", "method", "Host", "Demo", line, 1, line, 4, "Run();"))
                .ToArray();
            return new ReferenceAnalysis(matches, truncated, diagnostics);
        }
    }
}
