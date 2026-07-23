import { Component } from "./component";
import type { Incident, IncidentSeverity } from "../models/incident";
import { requiresImmediateEscalation } from "../models/incident";

@Component({
  selector: "signaldesk-incident-board",
  template: `
    <section>
      <h1>Open incidents</h1>
      <ul></ul>
    </section>
  `,
})
export class IncidentBoardComponent {
  private incidents: Incident[] = [];
  private severityFilter: IncidentSeverity | "all" = "all";

  setIncidents(incidents: Incident[]): void {
    this.incidents = [...incidents];
  }

  setSeverityFilter(filter: IncidentSeverity | "all"): void {
    this.severityFilter = filter;
  }

  visibleIncidents(): Incident[] {
    return this.incidents
      .filter((incident) =>
        this.severityFilter === "all"
          || incident.severity === this.severityFilter)
      .sort((left, right) =>
        Number(requiresImmediateEscalation(right))
        - Number(requiresImmediateEscalation(left)));
  }
}
