/**
 * In-process trade-offer auto-accept control (T109 — E2E timeout scenarios).
 *
 * The fake normally self-drives every offer `sent` → `accepted` (see
 * `routes/steam.ts`). The timeout scenarios need a transaction to *stay* parked
 * in `TRADE_OFFER_SENT_TO_SELLER` (03 §4.2) or `TRADE_OFFER_SENT_TO_BUYER`
 * (03 §4.4) so the backend's deadline scanner can fire a timeout against a real
 * 60-minute deadline (pushed into the past by the harness).
 *
 * A test suppresses the accept leg for a specific dispatch direction
 * (`SELLER_TO_BOT` / `BOT_TO_BUYER` / `BOT_TO_SELLER_REFUND`); the fake then
 * emits only `trade_offer.sent` (persisting the offer row) and withholds
 * `trade_offer.accepted`, leaving the state machine parked.
 *
 * Both Express surfaces (5100 steam / 5200 blockchain+control) run in one
 * process, so this module-level set is shared: `routes/control.ts` mutates it
 * and `routes/steam.ts` reads it.
 */
const suppressedDirections = new Set<string>();

/** Withhold `trade_offer.accepted` for offers sent with `direction`. */
export function suppressAccept(direction: string): void {
  suppressedDirections.add(direction);
}

/** Clear every suppression — restores the default self-drive for all offers. */
export function clearSuppressions(): void {
  suppressedDirections.clear();
}

/** True when the accept leg for `direction` is currently suppressed. */
export function isAcceptSuppressed(direction: string): boolean {
  return suppressedDirections.has(direction);
}

/** Snapshot of the currently suppressed directions (for control responses). */
export function listSuppressed(): string[] {
  return [...suppressedDirections];
}
