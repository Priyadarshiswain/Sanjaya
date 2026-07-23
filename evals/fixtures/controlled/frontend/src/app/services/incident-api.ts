import type { Incident } from "../models/incident";
import type { RuntimeConfig } from "../core/runtime-config";

export class IncidentApi {
  constructor(private readonly config: RuntimeConfig) {}

  async listOpen(signal?: AbortSignal): Promise<Incident[]> {
    const response = await fetch(`${this.config.apiBaseUrl}/incidents?status=open`, {
      signal,
    });

    if (!response.ok) {
      throw new Error(`Incident request failed with ${response.status}`);
    }

    return (await response.json()) as Incident[];
  }

  async acknowledge(id: string): Promise<void> {
    const response = await fetch(
      `${this.config.apiBaseUrl}/incidents/${encodeURIComponent(id)}/acknowledge`,
      { method: "POST" },
    );

    if (!response.ok) {
      throw new Error(`Acknowledge request failed with ${response.status}`);
    }
  }
}
