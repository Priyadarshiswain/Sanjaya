using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

public sealed record IndexCodebaseData(
    [property: JsonPropertyName("formatVersion")]
    string FormatVersion,
    [property: JsonPropertyName("state")]
    string State,
    [property: JsonPropertyName("indexPath")]
    string IndexPath,
    [property: JsonPropertyName("repositoryFingerprint")]
    string RepositoryFingerprint,
    [property: JsonPropertyName("previousIndexState")]
    string PreviousIndexState,
    [property: JsonPropertyName("providers")]
    IReadOnlyList<IndexedProviderSummary> Providers,
    [property: JsonPropertyName("filesIndexed")]
    int FilesIndexed,
    [property: JsonPropertyName("unsupportedFiles")]
    int UnsupportedFiles,
    [property: JsonPropertyName("chunksIndexed")]
    int ChunksIndexed,
    [property: JsonPropertyName("sourceBytes")]
    long SourceBytes,
    [property: JsonPropertyName("syntaxDiagnosticCount")]
    int SyntaxDiagnosticCount,
    [property: JsonPropertyName("truncatedChunkCount")]
    int TruncatedChunkCount);

public sealed record IndexedProviderSummary(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("contractVersion")]
    string ContractVersion,
    [property: JsonPropertyName("languages")]
    IReadOnlyList<string> Languages,
    [property: JsonPropertyName("files")]
    int Files,
    [property: JsonPropertyName("chunks")]
    int Chunks);
