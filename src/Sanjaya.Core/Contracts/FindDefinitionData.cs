using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

public sealed record FindDefinitionData(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("kind")]
    string? Kind,
    [property: JsonPropertyName("container")]
    string? Container,
    [property: JsonPropertyName("path")]
    string? Path,
    [property: JsonPropertyName("indexFingerprint")]
    string IndexFingerprint,
    [property: JsonPropertyName("resolution")]
    string Resolution,
    [property: JsonPropertyName("matches")]
    IReadOnlyList<DefinitionMatch> Matches,
    [property: JsonPropertyName("totalMatches")]
    int TotalMatches,
    [property: JsonPropertyName("truncated")]
    bool Truncated);

public sealed record DefinitionMatch(
    [property: JsonPropertyName("chunkId")]
    string ChunkId,
    [property: JsonPropertyName("provider")]
    string Provider,
    [property: JsonPropertyName("language")]
    string Language,
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("container")]
    string? Container,
    [property: JsonPropertyName("startLine")]
    int StartLine,
    [property: JsonPropertyName("endLine")]
    int EndLine,
    [property: JsonPropertyName("snippet")]
    string Snippet);
