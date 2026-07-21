using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

public sealed record SearchTextData(
    [property: JsonPropertyName("query")]
    string Query,
    [property: JsonPropertyName("caseSensitive")]
    bool CaseSensitive,
    [property: JsonPropertyName("matches")]
    IReadOnlyList<TextMatch> Matches,
    [property: JsonPropertyName("filesScanned")]
    int FilesScanned,
    [property: JsonPropertyName("bytesScanned")]
    long BytesScanned,
    [property: JsonPropertyName("truncated")]
    bool Truncated);

public sealed record TextMatch(
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("line")]
    int Line,
    [property: JsonPropertyName("column")]
    int Column,
    [property: JsonPropertyName("snippet")]
    string Snippet);
