using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

public sealed record FindReferencesData(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("indexFingerprint")] string IndexFingerprint,
    [property: JsonPropertyName("classification")] string Classification,
    [property: JsonPropertyName("matches")] IReadOnlyList<ReferenceMatch> Matches,
    [property: JsonPropertyName("totalMatches")] int TotalMatches,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("filesScanned")] int FilesScanned,
    [property: JsonPropertyName("syntaxDiagnosticCount")] int SyntaxDiagnosticCount);

public sealed record ReferenceMatch(
    [property: JsonPropertyName("classification")] string Classification,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("syntaxKind")] string SyntaxKind,
    [property: JsonPropertyName("enclosingKind")] string? EnclosingKind,
    [property: JsonPropertyName("enclosingName")] string? EnclosingName,
    [property: JsonPropertyName("enclosingContainer")] string? EnclosingContainer,
    [property: JsonPropertyName("startLine")] int StartLine,
    [property: JsonPropertyName("startColumn")] int StartColumn,
    [property: JsonPropertyName("endLine")] int EndLine,
    [property: JsonPropertyName("endColumn")] int EndColumn,
    [property: JsonPropertyName("snippet")] string Snippet);
