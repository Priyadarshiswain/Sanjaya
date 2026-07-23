namespace SignalDesk.Domain.Notifications;

public sealed record Notification(
    Guid IncidentId,
    string Channel,
    string Recipient,
    string Message);
