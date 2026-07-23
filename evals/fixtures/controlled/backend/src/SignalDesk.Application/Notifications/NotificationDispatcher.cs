using SignalDesk.Domain.Notifications;

namespace SignalDesk.Notifications;

public sealed class NotificationDispatcher(
    IEnumerable<INotificationChannel> channels)
{
    public Task DispatchAsync(
        Notification notification,
        CancellationToken cancellationToken = default)
    {
        return DispatchAsync([notification], cancellationToken);
    }

    public async Task DispatchAsync(
        IReadOnlyList<Notification> notifications,
        CancellationToken cancellationToken = default)
    {
        foreach (var notification in notifications)
        {
            var channel = channels.Single(candidate =>
                candidate.Name.Equals(notification.Channel, StringComparison.OrdinalIgnoreCase));
            await channel.SendAsync(notification, cancellationToken);
        }
    }
}
