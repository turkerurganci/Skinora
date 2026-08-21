import { describe, it, expect } from "vitest";
import { TransactionStatus } from "@/types/enums";
import type { ExtendedStatus } from "@/components/common";
import { isActivePartyRow, panelRowFor, PANEL_ROLES, type PanelRow } from "./helpers";

/**
 * T135 — S07 state × role matrix completeness guard (04 §7.3).
 *
 * WHY THIS EXISTS. The action panel is a switch over statuses, and a switch
 * over an enum fails SILENTLY when the enum grows: the unmatched status falls
 * through, the panel returns null, and the user is left on a transaction with
 * no action area and no explanation. Nothing in the stack notices — TypeScript
 * is satisfied because a `default` exists, `tsc`/eslint see valid code, and
 * `check-i18n.mjs` only compares locales to each other.
 *
 * This is not hypothetical. Two independent instances were on record before
 * this test:
 *   • REFUNDED — reachable since WP5 (buyer-favor dispute unwind) and again
 *     since T129 (settlement reversal). `helpers.isCancelledStatus` never
 *     listed it, so the panel matched no branch AND `!isTerminalStatus` stayed
 *     true, leaving two permanently disabled buttons under an empty panel while
 *     the page suppressed the refund summary the buyer had come to read.
 *   • T134's validation recorded the same shape for `TIMELINE_STEPS`
 *     (observation G1): an unclassified status draws as "step 1, in progress".
 *
 * So the guard measures the axis the compiler cannot: that every cell of the
 * matrix has been DECIDED, and that no branch of the union is dead.
 */

const ALL_STATUSES: ExtendedStatus[] = [...Object.values(TransactionStatus), "EMERGENCY_HOLD"];
const VIEWERS = [...PANEL_ROLES, null] as const;

/**
 * 04 §7.3, transcribed. Deliberately a literal table rather than a derivation:
 * a table generated from the same rules the implementation uses would agree
 * with a wrong implementation. This one is read off the spec.
 */
const EXPECTED: ReadonlyArray<readonly [ExtendedStatus, "seller" | "buyer" | null, PanelRow]> = [
  // Public variant — 04 §7.3 scopes it to CREATED (07 §7.5). Every other state
  // shown to a viewer with no role gets no action area at all: the party
  // messages ("your payout is scheduled", "send the item now") are addressed to
  // someone this viewer is not.
  [TransactionStatus.CREATED, null, "publicCreated"],
  [TransactionStatus.ACCEPTED, null, "publicNoAction"],
  [TransactionStatus.SELLER_CONFIRMED, null, "publicNoAction"],
  [TransactionStatus.PAYMENT_RECEIVED, null, "publicNoAction"],
  [TransactionStatus.ITEM_DELIVERED, null, "publicNoAction"],
  [TransactionStatus.COMPLETED, null, "publicNoAction"],

  // CREATED
  [TransactionStatus.CREATED, "seller", "createdSeller"],
  [TransactionStatus.CREATED, "buyer", "createdBuyer"],

  // ACCEPTED — "Göndermeye Hazırım" belongs to the seller.
  [TransactionStatus.ACCEPTED, "seller", "acceptedSeller"],
  [TransactionStatus.ACCEPTED, "buyer", "acceptedBuyer"],

  // SELLER_CONFIRMED — payment details open to the buyer.
  [TransactionStatus.SELLER_CONFIRMED, "seller", "sellerConfirmedSeller"],
  [TransactionStatus.SELLER_CONFIRMED, "buyer", "sellerConfirmedBuyer"],

  // PAYMENT_RECEIVED — trade deep link to the seller, "Teslim Aldım" to the buyer.
  [TransactionStatus.PAYMENT_RECEIVED, "seller", "paymentReceivedSeller"],
  [TransactionStatus.PAYMENT_RECEIVED, "buyer", "paymentReceivedBuyer"],

  // ITEM_DELIVERED — settlement window.
  [TransactionStatus.ITEM_DELIVERED, "seller", "itemDeliveredSeller"],
  [TransactionStatus.ITEM_DELIVERED, "buyer", "itemDeliveredBuyer"],

  // COMPLETED
  [TransactionStatus.COMPLETED, "seller", "completedSeller"],
  [TransactionStatus.COMPLETED, "buyer", "completedBuyer"],

  // Frozen overlays — every action off, for every viewer.
  [TransactionStatus.FLAGGED, "seller", "frozen"],
  [TransactionStatus.FLAGGED, "buyer", "frozen"],
  [TransactionStatus.FLAGGED, null, "frozen"],
  ["EMERGENCY_HOLD", "seller", "frozen"],
  ["EMERGENCY_HOLD", "buyer", "frozen"],
  ["EMERGENCY_HOLD", null, "frozen"],

  // The unwind family — CancelInfoBlock owns the surface (07 §7.5).
  [TransactionStatus.CANCELLED_TIMEOUT, "seller", "unwound"],
  [TransactionStatus.CANCELLED_TIMEOUT, "buyer", "unwound"],
  [TransactionStatus.CANCELLED_TIMEOUT, null, "unwound"],
  [TransactionStatus.CANCELLED_SELLER, "seller", "unwound"],
  [TransactionStatus.CANCELLED_SELLER, "buyer", "unwound"],
  [TransactionStatus.CANCELLED_SELLER, null, "unwound"],
  [TransactionStatus.CANCELLED_BUYER, "seller", "unwound"],
  [TransactionStatus.CANCELLED_BUYER, "buyer", "unwound"],
  [TransactionStatus.CANCELLED_BUYER, null, "unwound"],
  [TransactionStatus.CANCELLED_ADMIN, "seller", "unwound"],
  [TransactionStatus.CANCELLED_ADMIN, "buyer", "unwound"],
  [TransactionStatus.CANCELLED_ADMIN, null, "unwound"],
  // REFUNDED belongs here, not in a fall-through. This row is the regression.
  [TransactionStatus.REFUNDED, "seller", "unwound"],
  [TransactionStatus.REFUNDED, "buyer", "unwound"],
  [TransactionStatus.REFUNDED, null, "unwound"],
];

describe("S07 state × role matrix (04 §7.3)", () => {
  it("expects a non-trivial catalogue (guard checks itself)", () => {
    // If the enum is ever parsed/imported into nothing, every assertion below
    // would pass vacuously over an empty list. Pin the sizes instead.
    expect(ALL_STATUSES).toHaveLength(13);
    expect(VIEWERS).toHaveLength(3);
    expect(ALL_STATUSES.length * VIEWERS.length).toBe(39);
  });

  it("classifies every (status × viewer) cell — no silent fall-through", () => {
    const unclassified: string[] = [];
    for (const status of ALL_STATUSES) {
      for (const viewer of VIEWERS) {
        if (panelRowFor(status, viewer) === "unclassified") {
          unclassified.push(`${status} × ${viewer ?? "public"}`);
        }
      }
    }
    // Named, not counted: a failure has to say WHICH cell was forgotten,
    // otherwise the next person has to rediscover the matrix to fix it.
    expect(unclassified).toEqual([]);
  });

  it.each(EXPECTED)("%s × %s → %s", (status, viewer, expected) => {
    expect(panelRowFor(status, viewer)).toBe(expected);
  });

  it("covers the whole matrix with the transcribed table", () => {
    // The table above and the cartesian product must be the same set of cells,
    // so a new status cannot be "covered" by the loop while nobody has read the
    // spec row for it.
    const tabulated = new Set(EXPECTED.map(([s, v]) => `${s}|${v ?? "public"}`));
    const all = ALL_STATUSES.flatMap((s) => VIEWERS.map((v) => `${s}|${v ?? "public"}`));
    expect([...tabulated].sort()).toEqual([...new Set(all)].sort());
  });

  it("leaves no dead branch in the PanelRow union", () => {
    // Every row except `unclassified` must be produced by some real cell.
    // An orphan row means either a branch of the panel is unreachable or the
    // matrix stopped emitting a case it still renders.
    const produced = new Set<PanelRow>();
    for (const status of ALL_STATUSES) {
      for (const viewer of VIEWERS) produced.add(panelRowFor(status, viewer));
    }
    const declared: PanelRow[] = EXPECTED.map(([, , row]) => row);
    expect([...produced].sort()).toEqual([...new Set(declared)].sort());
    expect(produced.has("unclassified")).toBe(false);
    expect(produced.size).toBe(16);
  });

  it("marks exactly the rows that share the countdown + secondary-action frame", () => {
    // Terminal, frozen and public rows return a self-contained block; only the
    // active party rows may render the cancel / dispute row underneath. Getting
    // this wrong is what put two dead buttons under a REFUNDED transaction.
    const framed = [...new Set(EXPECTED.map(([, , row]) => row))].filter(isActivePartyRow).sort();
    expect(framed).toEqual(
      [
        "acceptedBuyer",
        "acceptedSeller",
        "createdBuyer",
        "createdSeller",
        "itemDeliveredBuyer",
        "itemDeliveredSeller",
        "paymentReceivedBuyer",
        "paymentReceivedSeller",
        "sellerConfirmedBuyer",
        "sellerConfirmedSeller",
      ].sort(),
    );
    for (const row of [
      "frozen",
      "unwound",
      "publicCreated",
      "publicNoAction",
      "completedSeller",
      "completedBuyer",
    ] as const) {
      expect(isActivePartyRow(row)).toBe(false);
    }
  });
});
