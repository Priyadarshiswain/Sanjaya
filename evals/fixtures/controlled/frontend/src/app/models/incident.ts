export type IncidentSeverity = "informational" | "warning" | "critical";
export type IncidentStatus = "open" | "acknowledged" | "resolved";

export interface Incident {
  id: string;
  service: string;
  severity: IncidentSeverity;
  status: IncidentStatus;
  createdAt: string;
}

export const requiresImmediateEscalation = (incident: Incident): boolean =>
  incident.severity === "critical" && incident.status === "open";
