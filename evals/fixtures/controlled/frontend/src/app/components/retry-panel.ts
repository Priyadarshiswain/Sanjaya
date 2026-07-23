import type { RuntimeConfig } from "../core/runtime-config";
import { calculateRetryPreview } from "../services/retry-preview";

export class RetryPanel {
  constructor(private readonly config: RuntimeConfig) {}

  rows(attempts: number): ReadonlyArray<{ attempt: number; delaySeconds: number }> {
    return Array.from({ length: attempts }, (_, attempt) => ({
      attempt,
      delaySeconds: calculateRetryPreview(attempt, this.config),
    }));
  }
}
