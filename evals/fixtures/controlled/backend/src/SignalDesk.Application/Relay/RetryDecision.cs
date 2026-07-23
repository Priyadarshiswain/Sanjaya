namespace SignalDesk.Relay;

public sealed record RetryDecision(
    bool ShouldRetry,
    TimeSpan Delay,
    string Reason);
