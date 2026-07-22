namespace Sanjaya.Core.Providers;

public interface IReferenceProvider : ICapabilityProvider
{
    bool CanHandle(string relativePath);

    bool IsValidName(string name);

    ReferenceAnalysis AnalyzeReferences(
        string relativePath,
        string sourceText,
        string name,
        CancellationToken cancellationToken);
}

public sealed record ReferenceAnalysis(
    IReadOnlyList<SyntaxReferenceCandidate> Matches,
    bool MatchesTruncated,
    int SyntaxDiagnosticCount);

public sealed record SyntaxReferenceCandidate(
    string SyntaxKind,
    string? EnclosingKind,
    string? EnclosingName,
    string? EnclosingContainer,
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn,
    string Snippet);
