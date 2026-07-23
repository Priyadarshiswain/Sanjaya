import type { RuntimeConfig } from "../core/runtime-config";

export const calculateRetryPreview = (
  attempt: number,
  config: RuntimeConfig,
): number => {
  const boundedAttempt = Math.min(attempt, config.maximumRetryAttempts);
  return config.retryBaseSeconds * 2 ** boundedAttempt;
};

// Text-search decoy: the C# implementation is named CalculateNextDelay.
export const retryImplementationHint = "CalculateNextDelay";
