namespace SignalDesk.Routing;

public sealed partial class TenantPolicy
{
    private readonly HashSet<string> _allowedChannels =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "inbox",
            "email",
            "pager",
        };

    public string DefaultTeam { get; init; } = "operations";

    public string CriticalTeam { get; init; } = "incident-command";
}
