using Sanjaya.Core.Contracts;
using Sanjaya.Core.Providers;

namespace Sanjaya.Providers.TypeScript;

public interface ITypeScriptWorker : IDisposable
{
    TypeScriptWorkerAnalysis Analyze(
        string relativePath,
        string language,
        string sourceText,
        CancellationToken cancellationToken);
}

public sealed record TypeScriptWorkerAnalysis(
    IReadOnlyList<OutlineItem> Items,
    bool ItemsTruncated,
    IReadOnlyList<StructuralChunk> Chunks,
    bool ChunksTruncated,
    int SyntaxDiagnosticCount);
