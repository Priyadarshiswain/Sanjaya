using Sanjaya.Core.Contracts;

namespace Sanjaya.Core.Providers;

/// <summary>
/// Analyzes already validated, bounded source text without owning filesystem access.
/// </summary>
public interface IFileOutlineProvider : ICapabilityProvider
{
    bool CanHandle(string relativePath);

    FileOutlineAnalysis AnalyzeOutline(
        string relativePath,
        string sourceText,
        CancellationToken cancellationToken);
}

public sealed record FileOutlineAnalysis(
    IReadOnlyList<OutlineItem> Items,
    bool ItemsTruncated,
    int SyntaxDiagnosticCount);
