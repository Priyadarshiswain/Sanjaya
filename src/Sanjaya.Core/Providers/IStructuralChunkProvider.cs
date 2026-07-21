namespace Sanjaya.Core.Providers;

/// <summary>
/// Produces bounded, deterministic source chunks for a future local index.
/// This interface is not itself an MCP tool.
/// </summary>
public interface IStructuralChunkProvider : ICapabilityProvider
{
    bool CanHandle(string relativePath);

    StructuralChunkAnalysis AnalyzeChunks(
        string relativePath,
        string sourceText,
        CancellationToken cancellationToken);
}

public sealed record StructuralChunkAnalysis(
    IReadOnlyList<StructuralChunk> Chunks,
    bool ChunksTruncated,
    int SyntaxDiagnosticCount);

public sealed record StructuralChunk(
    string Kind,
    string Name,
    string? Container,
    int StartLine,
    int EndLine,
    string Content,
    bool ContentTruncated);
