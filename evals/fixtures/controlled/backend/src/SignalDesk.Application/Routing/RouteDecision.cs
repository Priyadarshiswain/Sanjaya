namespace SignalDesk.Routing;

public sealed record RouteDecision(
    string Team,
    string Channel,
    bool Escalated);
