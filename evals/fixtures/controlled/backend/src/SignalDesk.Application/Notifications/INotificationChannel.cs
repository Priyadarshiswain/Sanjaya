using SignalDesk.Domain.Notifications;

namespace SignalDesk.Notifications;

public interface INotificationChannel
{
    string Name { get; }

    Task SendAsync(Notification notification, CancellationToken cancellationToken);
}
