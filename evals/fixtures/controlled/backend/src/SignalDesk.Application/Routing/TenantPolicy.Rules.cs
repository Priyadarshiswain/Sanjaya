namespace SignalDesk.Routing;

public sealed partial class TenantPolicy
{
    public bool AllowsChannel(string channel) =>
        _allowedChannels.Contains(channel);
}
