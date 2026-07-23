using SignalDesk.Domain.Incidents;

namespace SignalDesk.Routing;

public sealed class IncidentRouter(TenantPolicy tenantPolicy)
{
    public RouteDecision Route(Incident incident)
    {
        if (incident.RequiresImmediateEscalation)
        {
            return new RouteDecision(
                tenantPolicy.CriticalTeam,
                "pager",
                true);
        }

        return new RouteDecision(
            tenantPolicy.DefaultTeam,
            "inbox",
            false);
    }
}
