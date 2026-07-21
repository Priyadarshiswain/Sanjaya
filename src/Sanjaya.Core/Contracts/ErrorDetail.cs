namespace Sanjaya.Core.Contracts;

/// <summary>
/// Stable machine-readable failure information.
/// </summary>
public sealed record ErrorDetail(
    string Code,
    string Message,
    string? Remediation = null);

