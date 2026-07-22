namespace Sanjaya.Core.Providers;

public enum StructuralProviderFailure
{
    Unavailable,
    TimedOut,
    OutputLimit,
    InvalidOutput,
}

/// <summary>
/// Reports a bounded provider failure without carrying subprocess diagnostics,
/// source text, environment values, or local paths across the provider boundary.
/// </summary>
public sealed class StructuralProviderException(StructuralProviderFailure failure) : Exception
{
    public StructuralProviderFailure Failure { get; } = failure;
}
