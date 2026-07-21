using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

/// <summary>
/// Health of the protocol foundation that is actually running.
/// </summary>
public sealed record HealthReportData(
    [property: JsonPropertyName("registeredToolCount")]
    int RegisteredToolCount,
    [property: JsonPropertyName("checks")]
    IReadOnlyList<HealthCheckEntry> Checks);

public sealed record HealthCheckEntry(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("message")]
    string Message);
