using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;
using Xunit;

namespace Sanjaya.Providers.TypeScript.Tests;

public sealed class TypeScriptSyntaxProviderTests
{
    [Theory]
    [InlineData("typescript", "src/sample.ts")]
    [InlineData("typescript", "src/sample.tsx")]
    [InlineData("typescript", "src/sample.mts")]
    [InlineData("typescript", "src/sample.cts")]
    [InlineData("typescript", "src/sample.d.ts")]
    [InlineData("javascript", "src/sample.js")]
    [InlineData("javascript", "src/sample.jsx")]
    [InlineData("javascript", "src/sample.mjs")]
    [InlineData("javascript", "src/sample.cjs")]
    public void RoutesOnlyTheApprovedExtensions(string language, string path)
    {
        using FakeWorker worker = new();
        TypeScriptSyntaxProvider provider = new(language, worker);

        Assert.True(provider.CanHandle(path));
        Assert.False(provider.CanHandle("src/sample.txt"));
        Assert.False(provider.CanHandle(language == "typescript" ? "src/sample.js" : "src/sample.ts"));
    }

    [Theory]
    [InlineData("typescript", TypeScriptSyntaxProvider.TypeScriptProviderId)]
    [InlineData("javascript", TypeScriptSyntaxProvider.JavaScriptProviderId)]
    public void ReportsOnlySyntaxCapabilities(string language, string expectedProvider)
    {
        using FakeWorker worker = new();
        TypeScriptSyntaxProvider provider = new(language, worker);
        CapabilityDescriptor[] capabilities = provider.GetCapabilities().ToArray();

        Assert.Equal(expectedProvider, provider.Id);
        Assert.Equal([language], provider.Languages);
        Assert.Equal(
            [CapabilityKind.FileOutline, CapabilityKind.StructuralChunking],
            capabilities.Where(item => item.Status == CapabilityStatus.Supported).Select(item => item.Capability));
        Assert.Equal(
            [CapabilityKind.Definitions, CapabilityKind.References, CapabilityKind.SourceRetrieval, CapabilityKind.CallGraph],
            capabilities.Where(item => item.Status == CapabilityStatus.Unavailable).Select(item => item.Capability));
        Assert.All(
            capabilities.Where(item => item.Status == CapabilityStatus.Unavailable),
            item => Assert.Equal(ContractValues.ReasonNotImplemented, item.Reason));
    }

    [Fact]
    public void OutlineAndChunksUseTheSameBoundedWorkerAnalysis()
    {
        using FakeWorker worker = new();
        TypeScriptSyntaxProvider provider = new("typescript", worker);

        FileOutlineAnalysis outline = provider.AnalyzeOutline(
            "src/widget.ts",
            "export class Widget {}",
            CancellationToken.None);
        StructuralChunkAnalysis chunks = provider.AnalyzeChunks(
            "src/widget.ts",
            "export class Widget {}",
            CancellationToken.None);

        Assert.Equal(2, worker.Requests.Count);
        Assert.All(worker.Requests, request => Assert.Equal(("src/widget.ts", "typescript"), request));
        Assert.Equal("Widget", Assert.Single(outline.Items).Name);
        Assert.True(outline.ItemsTruncated);
        Assert.Equal(2, outline.SyntaxDiagnosticCount);
        Assert.Equal("Widget", Assert.Single(chunks.Chunks).Name);
        Assert.True(chunks.ChunksTruncated);
        Assert.Equal(2, chunks.SyntaxDiagnosticCount);
    }

    [Theory]
    [InlineData("typescript", "sample.ts")]
    [InlineData("javascript", "sample.js")]
    public void MissingRuntimeReportsUnavailableWithoutClaimingStructure(string language, string path)
    {
        UnavailableTypeScriptProvider provider = new(language);

        Assert.True(provider.CanHandle(path));
        Assert.All(
            provider.GetCapabilities().Where(item => item.Capability is CapabilityKind.FileOutline or CapabilityKind.StructuralChunking),
            item =>
            {
                Assert.Equal(CapabilityStatus.Unavailable, item.Status);
                Assert.Equal(ContractValues.ReasonRuntimeUnavailable, item.Reason);
            });
        StructuralProviderException exception = Assert.Throws<StructuralProviderException>(() =>
            provider.AnalyzeOutline(path, "export {};", CancellationToken.None));
        Assert.Equal(StructuralProviderFailure.Unavailable, exception.Failure);
    }

    private sealed class FakeWorker : ITypeScriptWorker
    {
        public List<(string Path, string Language)> Requests { get; } = [];

        public TypeScriptWorkerAnalysis Analyze(
            string relativePath,
            string language,
            string sourceText,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add((relativePath, language));
            return new TypeScriptWorkerAnalysis(
                [new OutlineItem("class", "Widget", "export class Widget {}", null, 1, 1)],
                true,
                [new StructuralChunk("class", "Widget", null, 1, 1, sourceText, false)],
                true,
                2);
        }

        public void Dispose()
        {
        }
    }
}
