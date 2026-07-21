using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

/// <summary>
/// Repository-relative evidence supporting a tool result.
/// </summary>
public sealed record EvidenceLocation(
    [property: JsonPropertyName("path")]
    string Path,
    [property: JsonPropertyName("startLine")]
    int StartLine,
    [property: JsonPropertyName("endLine")]
    int EndLine,
    [property: JsonPropertyName("symbol")]
    string? Symbol = null);
