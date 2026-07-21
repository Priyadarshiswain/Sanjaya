using System.Text.Json.Serialization;

namespace Sanjaya.Core.Contracts;

/// <summary>
/// Current, runtime-honest availability of Sanjaya's public contract.
/// </summary>
public sealed record CapabilityReportData(
    [property: JsonPropertyName("buildVersion")]
    string BuildVersion,
    [property: JsonPropertyName("transport")]
    string Transport,
    [property: JsonPropertyName("defaultNetworkAccess")]
    bool DefaultNetworkAccess,
    [property: JsonPropertyName("repositoryReady")]
    bool RepositoryReady,
    [property: JsonPropertyName("tools")]
    IReadOnlyList<ToolAvailability> Tools,
    [property: JsonPropertyName("providers")]
    IReadOnlyList<ProviderAvailability> Providers);

public sealed record ToolAvailability(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("reason")]
    string? Reason = null);

public sealed record ProviderAvailability(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("languages")]
    IReadOnlyList<string> Languages,
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("capabilities")]
    IReadOnlyList<ProviderCapabilityAvailability> Capabilities,
    [property: JsonPropertyName("reason")]
    string? Reason = null);

public sealed record ProviderCapabilityAvailability(
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("status")]
    string Status,
    [property: JsonPropertyName("reason")]
    string? Reason = null);
