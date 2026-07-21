using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

public sealed record SearchCodeData(
    [property: JsonPropertyName("query")]
    string Query,
    [property: JsonPropertyName("caseSensitive")]
    bool CaseSensitive,
    [property: JsonPropertyName("indexFingerprint")]
    string IndexFingerprint,
    [property: JsonPropertyName("matches")]
    IReadOnlyList<CodeSearchMatch> Matches,
    [property: JsonPropertyName("totalMatches")]
    int TotalMatches,
    [property: JsonPropertyName("truncated")]
    bool Truncated);

public sealed record CodeSearchMatch(
    [property: JsonPropertyName("chunkId")]
    string ChunkId,
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
    [property: JsonPropertyName("score")]
    int Score,
    [property: JsonPropertyName("matchedFields")]
    IReadOnlyList<string> MatchedFields,
    [property: JsonPropertyName("snippet")]
    string Snippet);
