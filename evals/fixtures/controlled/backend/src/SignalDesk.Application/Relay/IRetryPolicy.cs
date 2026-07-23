namespace SignalDesk.Relay;

public interface IRetryPolicy
{
    TimeSpan CalculateNextDelay(int attempt, TimeSpan? retryAfter = null);
}
