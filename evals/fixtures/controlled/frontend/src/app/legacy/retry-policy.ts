/** Historical browser-only policy retained for migration tests. */
export class RetryPolicy {
  calculateNextDelay(attempt: number): number {
    return 30 * (attempt + 1);
  }
}
