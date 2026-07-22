using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Indexing;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Core.Tests;

public sealed class GetSourceServiceTests
{
    [Fact]
    public async Task ReturnsExactCompleteSourceAndContainedWindowWithoutWriting()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("src/Sample.fake", "first\nsecond\nthird");
        TestSourceProvider provider = new();
        (GetSourceService service, string chunkId) = await BuildAsync(repository, provider);
        byte[] before = File.ReadAllBytes(IndexPath(repository));

        ToolResponse<GetSourceData> complete = await service.GetAsync(
            chunkId, null, null, CancellationToken.None);
        ToolResponse<GetSourceData> window = await service.GetAsync(
            chunkId, 2, 2, CancellationToken.None);

        Assert.Equal(ContractValues.StatusOk, complete.Status);
        Assert.True(complete.Data!.Complete);
        Assert.Equal("first\nsecond\nthird", complete.Data.Source);
        Assert.Equal("src/Sample.fake", complete.Data.Path);
        Assert.Equal(new SourceRange(1, 1, 3, 6), complete.Data.DeclarationRange);
        Assert.Equal(complete.Data.DeclarationRange, complete.Data.ReturnedRange);
        Assert.Equal("src/Sample.fake", Assert.Single(complete.Evidence).Path);

        Assert.Equal(ContractValues.StatusPartial, window.Status);
        Assert.False(window.Data!.Complete);
        Assert.Equal("second", window.Data.Source);
        Assert.Equal(new SourceRange(2, 1, 2, 7), window.Data.ReturnedRange);
        Assert.Contains("source_window_applied", window.Warnings);
        Assert.Equal(before, File.ReadAllBytes(IndexPath(repository)));
    }

    [Fact]
    public async Task ValidatesProviderChunkIdAndContainedLineRange()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "first\nsecond\nthird");
        TestSourceProvider provider = new();
        RepositoryScope scope = RepositoryScope.Create(repository.Path);
        GetSourceService noProvider = new(scope, [provider], []);
        Assert.Equal(
            ContractValues.ErrorSourceProviderUnavailable,
            (await noProvider.GetAsync("sha256:" + new string('a', 64), null, null, CancellationToken.None)).Error!.Code);

        (GetSourceService service, string chunkId) = await BuildAsync(repository, provider);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.GetAsync("SHA256:" + new string('A', 64), null, null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.GetAsync(chunkId, 2, null, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.GetAsync(chunkId, 0, 1, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.GetAsync(chunkId, 0, 0, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.GetAsync(chunkId, 3, 2, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorInvalidArgument,
            (await service.GetAsync(chunkId, 1, 4, CancellationToken.None)).Error!.Code);
        Assert.Equal(
            ContractValues.ErrorChunkNotFound,
            (await service.GetAsync("sha256:" + new string('f', 64), null, null, CancellationToken.None)).Error!.Code);
    }

    [Fact]
    public async Task DistinguishesMissingStaleResolutionAmbiguityAndCancellation()
    {
        using TemporaryDirectory missingRepository = new();
        missingRepository.WriteFile("source.fake", "first");
        TestSourceProvider provider = new();
        GetSourceService missing = new(
            RepositoryScope.Create(missingRepository.Path), [provider], [provider]);
        Assert.Equal(
            ContractValues.ErrorIndexMissing,
            (await missing.GetAsync("sha256:" + new string('a', 64), null, null, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory staleRepository = new();
        staleRepository.WriteFile("source.fake", "first");
        (GetSourceService stale, string staleId) = await BuildAsync(staleRepository, provider);
        staleRepository.WriteFile("source.fake", "changed");
        Assert.Equal(
            ContractValues.ErrorIndexStale,
            (await stale.GetAsync(staleId, null, null, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory unresolvedRepository = new();
        unresolvedRepository.WriteFile("source.fake", "first");
        TestSourceProvider unresolvedProvider = new(resolve: false);
        (GetSourceService unresolved, string unresolvedId) = await BuildAsync(
            unresolvedRepository, unresolvedProvider);
        Assert.Equal(
            ContractValues.ErrorSourceResolutionFailed,
            (await unresolved.GetAsync(unresolvedId, null, null, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory ambiguousRepository = new();
        ambiguousRepository.WriteFile("source.fake", "first");
        TestSourceProvider ambiguousProvider = new(ambiguous: true);
        (GetSourceService ambiguous, string ambiguousId) = await BuildAsync(
            ambiguousRepository, ambiguousProvider);
        Assert.Equal(
            ContractValues.ErrorSourceAmbiguous,
            (await ambiguous.GetAsync(ambiguousId, null, null, CancellationToken.None)).Error!.Code);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Equal(
            ContractValues.ErrorCancelled,
            (await ambiguous.GetAsync(ambiguousId, null, null, cancellation.Token)).Error!.Code);
    }

    [Fact]
    public async Task EnforcesUtf8OutputBoundAndReportsSyntaxRecovery()
    {
        using TemporaryDirectory largeRepository = new();
        largeRepository.WriteFile("source.fake", new string('\u00e9', SourceRetrievalLimits.MaximumSourceBytes));
        TestSourceProvider largeProvider = new();
        (GetSourceService large, string largeId) = await BuildAsync(largeRepository, largeProvider);
        Assert.Equal(
            ContractValues.ErrorSourceRangeTooLarge,
            (await large.GetAsync(largeId, null, null, CancellationToken.None)).Error!.Code);

        using TemporaryDirectory partialRepository = new();
        partialRepository.WriteFile("source.fake", "first");
        TestSourceProvider partialProvider = new(diagnostics: 2);
        (GetSourceService partial, string partialId) = await BuildAsync(partialRepository, partialProvider);
        ToolResponse<GetSourceData> response = await partial.GetAsync(
            partialId, null, null, CancellationToken.None);
        Assert.Equal(ContractValues.StatusPartial, response.Status);
        Assert.Equal(2, response.Data!.SyntaxDiagnosticCount);
        Assert.Contains("csharp_syntax_diagnostics_recovered:2", response.Warnings);
    }

    [Fact]
    public async Task WindowsSourceWithCarriageReturnLineEndings()
    {
        using TemporaryDirectory repository = new();
        repository.WriteFile("source.fake", "first\rsecond\rthird");
        TestSourceProvider provider = new();
        (GetSourceService service, string chunkId) = await BuildAsync(repository, provider);

        ToolResponse<GetSourceData> response = await service.GetAsync(
            chunkId, 2, 2, CancellationToken.None);

        Assert.Equal(ContractValues.StatusPartial, response.Status);
        Assert.Equal("second", response.Data!.Source);
        Assert.Equal(new SourceRange(2, 1, 2, 7), response.Data.ReturnedRange);
    }

    private static async Task<(GetSourceService Service, string ChunkId)> BuildAsync(
        TemporaryDirectory repository,
        TestSourceProvider provider)
    {
        RepositoryScope scope = RepositoryScope.Create(repository.Path);
        ToolResponse<IndexCodebaseData> built = await new IndexCodebaseService(
            scope, [provider], "test").RebuildAsync(CancellationToken.None);
        Assert.NotEqual(ContractValues.StatusError, built.Status);
        FindDefinitionService definitions = new(scope, [provider]);
        ToolResponse<FindDefinitionData> definition = await definitions.FindAsync(
            "Run", null, null, null, null, CancellationToken.None);
        string chunkId = Assert.Single(definition.Data!.Matches).ChunkId;
        return (new GetSourceService(scope, [provider], [provider]), chunkId);
    }

    private static string IndexPath(TemporaryDirectory repository) =>
        System.IO.Path.Combine(repository.Path, ".sanjaya", "index-v1.json");

    private sealed class TestSourceProvider(
        bool resolve = true,
        bool ambiguous = false,
        int diagnostics = 0) : IStructuralChunkProvider, ISourceRetrievalProvider
    {
        public string Id => "test-source";

        public string ContractVersion => "1";

        public IReadOnlyCollection<string> Languages { get; } = ["csharp"];

        public bool CanHandle(string relativePath) => relativePath.EndsWith(".fake", StringComparison.Ordinal);

        public IReadOnlyCollection<CapabilityDescriptor> GetCapabilities() =>
        [
            new(CapabilityKind.StructuralChunking, Id, "csharp", CapabilityStatus.Supported),
            new(CapabilityKind.Definitions, Id, "csharp", CapabilityStatus.Supported),
            new(CapabilityKind.SourceRetrieval, Id, "csharp", CapabilityStatus.Supported),
        ];

        public StructuralChunkAnalysis AnalyzeChunks(
            string relativePath,
            string sourceText,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content = sourceText.Length > SourceRetrievalLimits.MaximumSourceBytes
                ? sourceText[..SourceRetrievalLimits.MaximumSourceBytes]
                : sourceText;
            return new StructuralChunkAnalysis(
                [new StructuralChunk(
                    "method", "Run", "Demo.Sample", 1, CountLines(sourceText), content,
                    content.Length != sourceText.Length)],
                false,
                diagnostics);
        }

        public SourceRetrievalAnalysis AnalyzeSource(
            string relativePath,
            string sourceText,
            SourceRetrievalTarget target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!resolve)
            {
                return new SourceRetrievalAnalysis([], diagnostics);
            }

            SourceDeclaration declaration = new(
                sourceText, 1, 1, CountLines(sourceText), LastLineLength(sourceText) + 1);
            return new SourceRetrievalAnalysis(
                ambiguous ? [declaration, declaration] : [declaration],
                diagnostics);
        }

        private static int CountLines(string source)
        {
            int count = 1;
            for (int index = 0; index < source.Length; index++)
            {
                if (source[index] == '\r')
                {
                    count++;
                    if (index + 1 < source.Length && source[index + 1] == '\n')
                    {
                        index++;
                    }
                }
                else if (source[index] == '\n')
                {
                    count++;
                }
            }

            return count;
        }

        private static int LastLineLength(string source)
        {
            int lineStart = Math.Max(source.LastIndexOf('\n'), source.LastIndexOf('\r'));
            return source.Length - lineStart - 1;
        }
    }
}
