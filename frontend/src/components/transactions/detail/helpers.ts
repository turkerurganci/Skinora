import type { TransactionDetailResponse } from "@/lib/api/transactions";
import { TimeoutFreezeReason, TransactionStatus } from "@/types/enums";
import type { ExtendedStatus } from "@/components/common";

/**
 * The unwind family: transactions that ended without the item changing hands
 * for money, and therefore carry a `cancelInfo` block and — when the buyer had
 * already paid — a `refund` breakdown.
 *
 * REFUNDED belongs here even though it is not literally a cancellation. 07 §7.5
 * says so twice, for both blocks: "`cancelInfo` — dört `CANCELLED_*` statüsü
 * **ve `REFUNDED`** bu kapsamdadır" and "`refund` — `CANCELLED_*` ve `REFUNDED`
 * statüleri bu kapsamdadır"; 07 §7.1 likewise files it under the `cancelled`
 * tab. It is reached by the WP5 buyer-favor dispute unwind and by T129's
 * settlement reversal, and in both the buyer's money came back — which is
 * precisely what these two blocks exist to show.
 *
 * T135 — this set is the single source for BOTH predicates below. They used to
 * be two independent literal lists and had drifted apart from `StatusBadge` and
 * `TransactionTimeline` (which have carried REFUNDED since T134): the FE showed
 * a refunded buyer neither their cancellation record nor the money they got
 * back, and `!isTerminalStatus` left two permanently-disabled action buttons
 * under an otherwise empty panel. Deriving both from one set is what stops that
 * from happening again — the same lesson T134 wrote about copied catalogues.
 */
const UNWOUND: ReadonlySet<ExtendedStatus> = new Set<ExtendedStatus>([
  TransactionStatus.CANCELLED_TIMEOUT,
  TransactionStatus.CANCELLED_SELLER,
  TransactionStatus.CANCELLED_BUYER,
  TransactionStatus.CANCELLED_ADMIN,
  TransactionStatus.REFUNDED,
]);

/**
 * 04 §7.3 — terminal states surface different action / info panels than
 * active states. A single `isTerminal` check keeps the page tree readable
 * (no copies of the same disjunction across components).
 */
const TERMINAL: ReadonlySet<ExtendedStatus> = new Set<ExtendedStatus>([
  ...UNWOUND,
  TransactionStatus.COMPLETED,
]);

export function isTerminalStatus(status: ExtendedStatus): boolean {
  return TERMINAL.has(status);
}

export function isCancelledStatus(status: ExtendedStatus): boolean {
  return UNWOUND.has(status);
}

// ---------------------------------------------------------------------------
// T135 — S07 state × role action matrix (04 §7.3)
// ---------------------------------------------------------------------------

/** The two party roles the 04 §7.3 matrix has a column for. */
export const PANEL_ROLES = ["seller", "buyer"] as const;
export type PanelRole = (typeof PANEL_ROLES)[number];

/**
 * One cell of the 04 §7.3 matrix. Every (status × role) pair resolves to
 * exactly one of these, and `StateActionPanel` renders one branch per value —
 * so the matrix in the spec and the branches in the component are the same
 * list, checked by the compiler in one direction and by
 * `StateActionPanel.matrix.test.ts` in the other.
 *
 * `unclassified` is the guard's target, not a state: it is what a status the
 * matrix has never heard of falls into. It exists so that adding a
 * `TransactionStatus` value fails a test with the cell named, instead of
 * silently rendering an action panel with nothing in it. That silent-null is
 * exactly the failure mode T134's validation recorded for `TIMELINE_STEPS`
 * (observation G1) and the one REFUNDED had actually fallen into here.
 */
export type PanelRow =
  | "frozen"
  | "unwound"
  | "publicCreated"
  | "publicNoAction"
  | "createdSeller"
  | "createdBuyer"
  | "acceptedSeller"
  | "acceptedBuyer"
  | "sellerConfirmedSeller"
  | "sellerConfirmedBuyer"
  | "paymentReceivedSeller"
  | "paymentReceivedBuyer"
  | "itemDeliveredSeller"
  | "itemDeliveredBuyer"
  | "completedSeller"
  | "completedBuyer"
  | "unclassified";

/**
 * Resolve the 04 §7.3 matrix cell for a viewer.
 *
 * Order is load-bearing and matches the spec's own precedence:
 *   1. FLAGGED / EMERGENCY_HOLD freeze every action for everyone, including a
 *      public viewer (04 §7.3 FLAGGED / EMERGENCY_HOLD rows).
 *   2. The unwind family shows its record, not an action area — `CancelInfoBlock`
 *      owns that surface at page level (04 §7.3 CANCELLED_* row).
 *   3. A viewer with no role is on the public variant, which 04 §7.3 scopes to
 *      CREATED. Any other state gets no action area rather than a party message.
 */
export function panelRowFor(status: ExtendedStatus, role: PanelRole | null): PanelRow {
  if (isEmergencyHold(status) || isFlagged(status)) return "frozen";
  if (isCancelledStatus(status)) return "unwound";
  if (role === null) {
    return status === TransactionStatus.CREATED ? "publicCreated" : "publicNoAction";
  }
  const seller = role === "seller";
  switch (status) {
    case TransactionStatus.CREATED:
      return seller ? "createdSeller" : "createdBuyer";
    case TransactionStatus.ACCEPTED:
      return seller ? "acceptedSeller" : "acceptedBuyer";
    case TransactionStatus.SELLER_CONFIRMED:
      return seller ? "sellerConfirmedSeller" : "sellerConfirmedBuyer";
    case TransactionStatus.PAYMENT_RECEIVED:
      return seller ? "paymentReceivedSeller" : "paymentReceivedBuyer";
    case TransactionStatus.ITEM_DELIVERED:
      return seller ? "itemDeliveredSeller" : "itemDeliveredBuyer";
    case TransactionStatus.COMPLETED:
      return seller ? "completedSeller" : "completedBuyer";
    default:
      // Unreachable for today's catalogue: FLAGGED and the five unwind statuses
      // are taken by the guards above, and the six below are enumerated. A new
      // TransactionStatus value lands here and the matrix guard names it.
      return "unclassified";
  }
}

/**
 * Rows that render the shared active-transaction frame: countdown, primary
 * action, then the secondary cancel / dispute row. The terminal and public
 * rows return their own self-contained block instead.
 */
const ACTIVE_PARTY_ROWS: ReadonlySet<PanelRow> = new Set<PanelRow>([
  "createdSeller",
  "createdBuyer",
  "acceptedSeller",
  "acceptedBuyer",
  "sellerConfirmedSeller",
  "sellerConfirmedBuyer",
  "paymentReceivedSeller",
  "paymentReceivedBuyer",
  "itemDeliveredSeller",
  "itemDeliveredBuyer",
]);

export function isActivePartyRow(row: PanelRow): boolean {
  return ACTIVE_PARTY_ROWS.has(row);
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
