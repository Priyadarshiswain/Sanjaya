namespace Sanjaya.Core.Providers;

public interface ISourceRetrievalProvider : ICapabilityProvider
{
    bool CanHandle(string relativePath);

    SourceRetrievalAnalysis AnalyzeSource(
        string relativePath,
        string sourceText,
        SourceRetrievalTarget target,
        CancellationToken cancellationToken);
}

public sealed record SourceRetrievalTarget(
    string Kind,
    string Name,
    string? Container,
    int StartLine,
    int EndLine,
    string IndexedContent,
    bool IndexedContentTruncated);

public sealed record SourceRetrievalAnalysis(
    IReadOnlyList<SourceDeclaration> Matches,
    int SyntaxDiagnosticCount);

public sealed record SourceDeclaration(
    string Content,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);
