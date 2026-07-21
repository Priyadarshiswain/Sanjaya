namespace Sanjaya.Core.Capabilities;

/// <summary>
/// Describes an honestly reported provider capability.
/// </summary>
/// <param name="Capability">The operation being described.</param>
/// <param name="Provider">Stable provider identifier.</param>
/// <param name="Language">Language or fallback category.</param>
/// <param name="Status">Current runtime availability.</param>
/// <param name="Reason">Optional explanation for unavailable or unsupported states.</param>
public sealed record CapabilityDescriptor(
    CapabilityKind Capability,
    string Provider,
    string Language,
    CapabilityStatus Status,
    string? Reason = null);

