import type { TransactionDetailResponse } from "@/lib/api/transactions";
import { TimeoutFreezeReason, TransactionStatus } from "@/types/enums";
import type { ExtendedStatus } from "@/components/common";

/**
 * 04 §7.3 — terminal states surface different action / info panels than
 * active states. A single `isTerminal` check keeps the page tree readable
 * (no copies of the same disjunction across components).
 */
const TERMINAL: ReadonlySet<ExtendedStatus> = new Set<ExtendedStatus>([
  TransactionStatus.COMPLETED,
  TransactionStatus.CANCELLED_TIMEOUT,
  TransactionStatus.CANCELLED_SELLER,
  TransactionStatus.CANCELLED_BUYER,
  TransactionStatus.CANCELLED_ADMIN,
]);

export function isTerminalStatus(status: ExtendedStatus): boolean {
  return TERMINAL.has(status);
}

export function isCancelledStatus(status: ExtendedStatus): boolean {
  return (
    status === TransactionStatus.CANCELLED_TIMEOUT ||
    status === TransactionStatus.CANCELLED_SELLER ||
    status === TransactionStatus.CANCELLED_BUYER ||
    status === TransactionStatus.CANCELLED_ADMIN
  );
}

/**
 * EMERGENCY_HOLD never appears as a TransactionStatus enum value (06 §2.20)
 * — it's projected as an ExtendedStatus overlay by the backend. The detail
 * page treats it as a banner state distinct from FLAGGED.
 */
export function isEmergencyHold(status: ExtendedStatus): boolean {
  return status === "EMERGENCY_HOLD";
}

export function isFlagged(status: ExtendedStatus): boolean {
  return status === TransactionStatus.FLAGGED;
}

/**
 * Coerce the backend `frozenReason` string into the typed enum so the
 * CountdownTimer i18n lookup never crashes on an unrecognised value (extra
 * defensive — server is authoritative, but new reasons may land before the
 * frontend ships the matching translation).
 */
export function asFreezeReason(reason: string | null | undefined): TimeoutFreezeReason | undefined {
  if (!reason) return undefined;
  return (Object.values(TimeoutFreezeReason) as string[]).includes(reason)
    ? (reason as TimeoutFreezeReason)
    : undefined;
}

/**
 * Mask the middle of a long address (TRC-20 / TX hash) — e.g.
 * "TXyz1234...90ab" — for inline display. Full value remains available via
 * CopyButton + tooltip.
 */
export function maskAddress(value: string, head = 6, tail = 4): string {
  if (value.length <= head + tail + 1) return value;
  return `${value.slice(0, head)}…${value.slice(-tail)}`;
}

/**
 * Map the backend `timeout.type` string onto a payload-agnostic key for
 * i18n lookup. The backend currently emits server-side strings ("payment",
 * "buyer_acceptance", "seller_trade_offer", "buyer_trade_offer") — we
 * preserve them verbatim. Unknown types fall back to "generic".
 */
export function timeoutLabelKey(type: string): string {
  const lower = type.toLowerCase();
  const known = new Set(["payment", "buyer_acceptance", "seller_trade_offer", "buyer_trade_offer"]);
  return known.has(lower) ? lower : "generic";
}

export type CallerView = "public" | "seller" | "buyer";

export function deriveCallerView(detail: TransactionDetailResponse): CallerView {
  if (!detail.userRole) return "public";
  return detail.userRole;
}

/**
 * Warning threshold for the CountdownTimer. Backend returns a percent
 * (e.g. 75 means "warn at 75% elapsed"); we convert to seconds against
 * the full window. Falls back to 25% of remaining when the backend omits
 * the field — a conservative red-zone margin.
 */
export function computeWarningSeconds(timeout: {
  expiresAt: string;
  remainingSeconds: number;
  warningThresholdPercent?: number | null;
}): number {
  const total = totalSecondsFor(timeout);
  const percent = timeout.warningThresholdPercent ?? 75;
  return Math.floor((total * (100 - percent)) / 100);
}

function totalSecondsFor(timeout: { expiresAt: string; remainingSeconds: number }): number {
  const expires = new Date(timeout.expiresAt).getTime();
  const start = expires - timeout.remainingSeconds * 1000;
  const now = Date.now();
  const total = (expires - Math.min(start, now)) / 1000;
  return Math.max(timeout.remainingSeconds, Math.floor(total));
}
