namespace Sanjaya.Core.Contracts;

/// <summary>
/// Common structured response envelope for public tools.
/// </summary>
public sealed record ToolResponse<T>(
    string Status,
    string Capability,
    string Provider,
    T? Data,
    IReadOnlyList<EvidenceLocation> Evidence,
    IReadOnlyList<string> Warnings,
    ErrorDetail? Error = null)
{
    public const string CurrentSchemaVersion = "1";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;
}

