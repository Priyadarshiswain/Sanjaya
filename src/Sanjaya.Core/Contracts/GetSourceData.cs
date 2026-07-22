using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

public sealed record GetSourceData(
    [property: JsonPropertyName("chunkId")] string ChunkId,
    [property: JsonPropertyName("indexFingerprint")] string IndexFingerprint,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("language")] string Language,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("container")] string? Container,
    [property: JsonPropertyName("declarationRange")] SourceRange DeclarationRange,
    [property: JsonPropertyName("returnedRange")] SourceRange ReturnedRange,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("complete")] bool Complete,
    [property: JsonPropertyName("syntaxDiagnosticCount")] int SyntaxDiagnosticCount);

/// <summary>
/// One-based start-inclusive and end-exclusive source positions.
/// </summary>
public sealed record SourceRange(
    [property: JsonPropertyName("startLine")] int StartLine,
    [property: JsonPropertyName("startColumn")] int StartColumn,
    [property: JsonPropertyName("endLine")] int EndLine,
    [property: JsonPropertyName("endColumn")] int EndColumn);
