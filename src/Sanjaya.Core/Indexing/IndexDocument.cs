namespace Sanjaya.Core.Indexing;

internal sealed record IndexDocument(
    string Owner,
    string FormatVersion,
    IndexProducer Producer,
    string RepositoryFingerprint,
    IReadOnlyList<IndexProvider> Providers,
    IReadOnlyList<IndexFile> Files,
    IReadOnlyList<IndexChunk> Chunks);

internal sealed record IndexProducer(string Name, string Version);

internal sealed record IndexProvider(
    string Id,
    string ContractVersion,
    IReadOnlyList<string> Languages);

internal sealed record IndexFile(
    string Path,
    string Provider,
    string ContentHash,
    int ByteCount,
    int SyntaxDiagnosticCount);

internal sealed record IndexChunk(
    string Id,
    string Provider,
    string Language,
    string Path,
    string Kind,
    string Name,
    string? Container,
    int StartLine,
    int EndLine,
    string Content,
    bool ContentTruncated);
