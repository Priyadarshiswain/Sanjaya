namespace SignalDesk.Relay;

public sealed class RetryPolicy(TimeProvider clock) : IRetryPolicy
{
    public const int MaximumAttempts = 6;
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(5);

    public TimeSpan CalculateNextDelay(int attempt, TimeSpan? retryAfter = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        if (retryAfter is { } serverDelay)
        {
            return serverDelay <= MaximumDelay ? serverDelay : MaximumDelay;
        }

        var exponentialSeconds = Math.Pow(2, Math.Min(attempt, MaximumAttempts));
        var jitterMilliseconds = clock.GetUtcNow().Millisecond % 250;
        return TimeSpan.FromSeconds(exponentialSeconds)
            + TimeSpan.FromMilliseconds(jitterMilliseconds);
    }

    public RetryDecision Decide(int attempt, int statusCode)
    {
        if (attempt >= MaximumAttempts)
        {
            return new RetryDecision(false, TimeSpan.Zero, "attempt_limit");
        }

        var retryable = statusCode is 408 or 429 or >= 500;
        return retryable
            ? new RetryDecision(true, CalculateNextDelay(attempt), "transient")
            : new RetryDecision(false, TimeSpan.Zero, "permanent");
    }
}
