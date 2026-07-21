using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

/// <summary>
/// Common structured response envelope for public tools.
/// </summary>
public sealed record ToolResponse<T>(
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("capability")]
    string Capability,
    [property: JsonPropertyName("provider")]
    string Provider,
    [property: JsonPropertyName("data")]
    T? Data,
    [property: JsonPropertyName("evidence")]
    IReadOnlyList<EvidenceLocation> Evidence,
    [property: JsonPropertyName("warnings")]
    IReadOnlyList<string> Warnings,
    [property: JsonPropertyName("error")]
    ErrorDetail? Error = null)
{
    public const string CurrentSchemaVersion = "1";

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
}
