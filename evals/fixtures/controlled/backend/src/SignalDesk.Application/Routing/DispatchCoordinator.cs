using SignalDesk.Domain.Incidents;
using SignalDesk.Domain.Notifications;
using SignalDesk.Notifications;

namespace SignalDesk.Routing;

public sealed class DispatchCoordinator(
    IncidentRouter router,
    NotificationDispatcher dispatcher)
{
    public async Task<RouteDecision> DispatchAsync(
        Incident incident,
        CancellationToken cancellationToken = default)
    {
        var route = router.Route(incident);
        var notification = new Notification(
            incident.Id,
            route.Channel,
            route.Team,
            $"Incident {incident.Id} requires attention.");

        await dispatcher.DispatchAsync(notification, cancellationToken);
        return route;
    }
}
