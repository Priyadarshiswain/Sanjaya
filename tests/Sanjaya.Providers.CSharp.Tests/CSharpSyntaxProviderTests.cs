using Sanjaya.Core.Capabilities;
using Sanjaya.Core.Contracts;
using Sanjaya.Core.Discovery;
using Sanjaya.Core.Providers;
using Sanjaya.Core.Repositories;
using Xunit;

namespace Sanjaya.Providers.CSharp.Tests;

public sealed class CSharpSyntaxProviderTests
{
    [Fact]
    public void ReportsOnlyImplementedSyntaxCapabilitiesAsSupported()
    {
        CSharpSyntaxProvider provider = new();

        CapabilityDescriptor[] capabilities = provider.GetCapabilities().ToArray();

        Assert.Equal("1", provider.ContractVersion);
        Assert.Equal(
            [CapabilityKind.FileOutline, CapabilityKind.StructuralChunking, CapabilityKind.Definitions, CapabilityKind.References],
            capabilities.Where(item => item.Status == CapabilityStatus.Supported).Select(item => item.Capability));
        Assert.All(
            capabilities.Where(item => item.Status != CapabilityStatus.Supported),
            item => Assert.Equal(ContractValues.ReasonNotImplemented, item.Reason));
    }

    [Fact]
    public void FindsOnlyExactSyntaxReferenceCandidatesWithEnclosingEvidence()
    {
        const string source = """
            namespace Demo;
            public class Sample
            {
                public void Run() { }
                public void Call()
                {
                    Run();
                    var text = "Run";
                    // Run();
                }
            }
            """;
        CSharpSyntaxProvider provider = new();

        ReferenceAnalysis analysis = provider.AnalyzeReferences(
            "Sample.cs",
            source,
            "Run",
            CancellationToken.None);

        SyntaxReferenceCandidate match = Assert.Single(analysis.Matches);
        Assert.Equal("identifier_name", match.SyntaxKind);
        Assert.Equal("method", match.EnclosingKind);
        Assert.Equal("Call", match.EnclosingName);
        Assert.Equal("Demo.Sample", match.EnclosingContainer);
        Assert.Equal(7, match.StartLine);
        Assert.Contains("Run();", match.Snippet, StringComparison.Ordinal);
        Assert.False(analysis.MatchesTruncated);
        Assert.Equal(0, analysis.SyntaxDiagnosticCount);
    }

    [Fact]
    public void ProducesDeterministicNamespacesTypesMethodsAndProperties()
    {
        const string source = """
            namespace Acme.Tools;

            public record Widget(int Id)
            {
                public string Name { get; init; } = "demo";

                public Widget() : this(0) { }

                public void Run(string input)
                {
                    Console.WriteLine(input);
                }
            }
            """;
        CSharpSyntaxProvider provider = new();

        FileOutlineAnalysis first = provider.AnalyzeOutline("src/Widget.cs", source, CancellationToken.None);
        FileOutlineAnalysis second = provider.AnalyzeOutline("src/Widget.cs", source, CancellationToken.None);

        Assert.Equal(first.Items, second.Items);
        Assert.Equal(first.ItemsTruncated, second.ItemsTruncated);
        Assert.Equal(first.SyntaxDiagnosticCount, second.SyntaxDiagnosticCount);
        Assert.False(first.ItemsTruncated);
        Assert.Equal(0, first.SyntaxDiagnosticCount);
        Assert.Equal(
            ["namespace", "record", "property", "constructor", "method"],
            first.Items.Select(item => item.Kind));
        Assert.Equal(["Acme.Tools", "Widget", "Name", "Widget", "Run"], first.Items.Select(item => item.Name));
        Assert.Equal("Acme.Tools.Widget", first.Items.Single(item => item.Name == "Run").Container);
        Assert.Equal(9, first.Items.Single(item => item.Name == "Run").StartLine);
        Assert.Equal(12, first.Items.Single(item => item.Name == "Run").EndLine);
        Assert.DoesNotContain("Console.WriteLine", first.Items.Single(item => item.Name == "Run").Display, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileOutlineRoutesCSharpToRoslynAndKeepsGenericFallback()
    {
        using TemporaryRepository repository = new();
        repository.Write("Sample.cs", "namespace Demo; public class Sample { public void Run() { } }");
        repository.Write("notes.txt", "first\nsecond");
        CSharpSyntaxProvider provider = new();
        FileOutlineService service = new(RepositoryScope.Create(repository.Path), [provider]);

        ToolResponse<FileOutlineData> csharp = await service.OutlineAsync("Sample.cs", CancellationToken.None);
        ToolResponse<FileOutlineData> generic = await service.OutlineAsync("notes.txt", CancellationToken.None);

        Assert.Equal(CSharpSyntaxProvider.ProviderId, csharp.Provider);
        Assert.Empty(csharp.Data!.Preview);
        Assert.Equal(["namespace", "class", "method"], csharp.Data.Items!.Select(item => item.Kind));
        Assert.Equal("generic-text", generic.Provider);
        Assert.Equal(2, generic.Data!.Preview.Count);
        Assert.Empty(generic.Data.Items!);
    }

    [Fact]
    public async Task MalformedFilesReturnBoundedPartialStructureInsteadOfFailing()
    {
        using TemporaryRepository repository = new();
        repository.Write("Broken.cs", "namespace Demo; public class Broken { public void Run( { }");
        FileOutlineService service = new(
            RepositoryScope.Create(repository.Path),
            [new CSharpSyntaxProvider()]);

        ToolResponse<FileOutlineData> response = await service.OutlineAsync("Broken.cs", CancellationToken.None);

        Assert.Equal(ContractValues.StatusPartial, response.Status);
        Assert.Equal(CSharpSyntaxProvider.ProviderId, response.Provider);
        Assert.True(response.Data!.SyntaxDiagnosticCount > 0);
        Assert.Contains("csharp_syntax_diagnostics", response.Warnings);
        Assert.Contains(response.Data.Items!, item => item.Name == "Broken");
    }

    [Fact]
    public void EnforcesOutlineAndChunkBoundsAndHonorsCancellation()
    {
        string declarations = string.Join('\n', Enumerable.Range(0, DiscoveryLimits.MaximumOutlineItems + 1)
            .Select(index => $"public class C{index} {{ }}"));
        CSharpSyntaxProvider provider = new();

        FileOutlineAnalysis outline = provider.AnalyzeOutline("Many.cs", declarations, CancellationToken.None);
        StructuralChunkAnalysis chunks = provider.AnalyzeChunks(
            "Large.cs",
            $"public class Large {{ public string Value => \"{new string('x', DiscoveryLimits.MaximumChunkCharacters + 100)}\"; }}",
            CancellationToken.None);

        Assert.Equal(DiscoveryLimits.MaximumOutlineItems, outline.Items.Count);
        Assert.True(outline.ItemsTruncated);
        Assert.All(chunks.Chunks, chunk => Assert.True(chunk.Content.Length <= DiscoveryLimits.MaximumChunkCharacters));
        Assert.Contains(chunks.Chunks, chunk => chunk.ContentTruncated);

        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            provider.AnalyzeOutline("Cancelled.cs", "class Cancelled { }", cancellation.Token));
    }

    private sealed class TemporaryRepository : IDisposable
    {
        public TemporaryRepository()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sanjaya-csharp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string relativePath, string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, relativePath), content);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
