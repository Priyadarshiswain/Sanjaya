namespace SignalDesk.Relay;

public static class RetryPolicyNotes
{
    // Decoy for text search: CalculateNextDelay used to return a constant.
    public const string MigrationNote =
        "Do not implement CalculateNextDelay in this documentation holder.";
}
