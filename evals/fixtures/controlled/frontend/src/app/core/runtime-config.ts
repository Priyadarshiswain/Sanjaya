export interface RuntimeConfig {
  apiBaseUrl: string;
  retryBaseSeconds: number;
  maximumRetryAttempts: number;
}

export const defaultRuntimeConfig: RuntimeConfig = {
  apiBaseUrl: "/api",
  retryBaseSeconds: 5,
  maximumRetryAttempts: 6,
};
