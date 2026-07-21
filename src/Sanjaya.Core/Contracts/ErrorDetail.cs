using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

/// <summary>
/// Stable machine-readable failure information.
/// </summary>
public sealed record ErrorDetail(
    [property: JsonPropertyName("code")]
    string Code,
    [property: JsonPropertyName("message")]
    string Message,
    [property: JsonPropertyName("remediation")]
    string? Remediation = null);
