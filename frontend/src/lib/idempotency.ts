/** A new Idempotency-Key for one booking submit. */
export function createIdempotencyKey(): string {
  return crypto.randomUUID();
}
