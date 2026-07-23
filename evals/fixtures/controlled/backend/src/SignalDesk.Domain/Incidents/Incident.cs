namespace SignalDesk.Domain.Incidents;

public sealed record Incident(
    Guid Id,
    string Service,
    IncidentSeverity Severity,
    IncidentStatus Status,
    DateTimeOffset CreatedAt)
{
    public bool RequiresImmediateEscalation =>
        Severity == IncidentSeverity.Critical && Status == IncidentStatus.Open;
}
