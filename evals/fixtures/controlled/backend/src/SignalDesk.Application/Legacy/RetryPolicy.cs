namespace SignalDesk.Legacy;

[Obsolete("Use SignalDesk.Relay.RetryPolicy.")]
public sealed class RetryPolicy
{
    public TimeSpan CalculateNextDelay(int attempt)
    {
        return TimeSpan.FromSeconds(30 * (attempt + 1));
    }
}
