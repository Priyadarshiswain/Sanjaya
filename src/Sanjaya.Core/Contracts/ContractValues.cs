namespace Sanjaya.Core.Contracts;

/// <summary>
/// Stable string values used by the public response contract.
/// </summary>
public static class ContractValues
{
    public const string StatusOk = "ok";
    public const string StatusPartial = "partial";
    public const string StatusError = "error";

    public const string AvailabilitySupported = "supported";
    public const string AvailabilityUnavailable = "unavailable";

    public const string ReasonNotImplemented = "not_implemented";
}
