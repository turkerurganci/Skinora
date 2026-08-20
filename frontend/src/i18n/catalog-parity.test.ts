import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { AuditAction, TransactionStatus } from "@/types/enums";
import { TIMELINE_STEPS } from "@/components/common/TransactionTimeline";

/**
 * T134 — i18n katalog bekçisi.
 *
 * `check-i18n.mjs` proves the four locales agree with EACH OTHER. It cannot see
 * whether they agree with the enum: before T134 all four carried
 * `status.ITEM_ESCROWED` and none carried `status.SELLER_CONFIRMED`, and parity
 * was green the whole time — every locale was wrong in the same way.
 *
 * This test measures the other axis: catalogue → i18n, over every i18n block
 * that is KEYED BY an enum this frontend copies:
 *
 *   • `status`            ↔ ExtendedStatus (TransactionStatus + EMERGENCY_HOLD)
 *   • `timeline.step`     ↔ TIMELINE_STEPS
 *   • `adminAuditLog.action` ↔ AuditAction
 *
 * The first two fail LOUDLY when a key is missing — `StatusBadge` renders
 * `t(status)` and `TransactionTimeline` renders `t("step." + step)`, so a gap is
 * a runtime next-intl error on a real screen. The third fails QUIETLY, which is
 * why it needs a guard the most: `AuditLogTable` renders
 * `tAction.has(row.action) ? tAction(row.action) : row.action`, so a missing
 * label degrades to the raw enum name in S21 instead of throwing. T134's
 * validation found exactly that — the turn moved `AuditAction` 32 → 29 and left
 * this catalogue at 26 (`SETTLEMENT_CLEARED_ADMIN` unlabelled, retired
 * `BOT_STATUS_CHANGED` orphaned, three WP7/WP16 actions never added).
 *
 * SCOPE — state it, do not assume it. Other SCREAMING_SNAKE i18n blocks are not
 * keyed by an enum in `types/enums.ts` and are deliberately out of scope:
 * `adminRoles.permissions` mirrors the backend `PermissionCatalog` and is owned
 * by T136 (`T133a-FePermissionCatalogKeys`); `adminAuditLog.category`,
 * `adminFlags.signalType`, `adminTransactions.statusGroup` and the
 * `adminSteamAccounts.*` blocks are API projections/vocabularies, not enum
 * copies.
 */

const LOCALES = ["en", "tr", "es", "zh"] as const;
const messagesDir = join(dirname(fileURLToPath(import.meta.url)), "messages");

function load(locale: string): Record<string, Record<string, unknown>> {
  return JSON.parse(readFileSync(join(messagesDir, `${locale}.json`), "utf8"));
}

// StatusBadge's ExtendedStatus = TransactionStatus | "EMERGENCY_HOLD". The
// overlay is not an enum value (04 §C01 note) but it IS rendered as a badge,
// so the label catalogue must carry it.
const EXPECTED_STATUS_KEYS = [...Object.values(TransactionStatus), "EMERGENCY_HOLD"].sort();
const EXPECTED_STEP_KEYS = [...TIMELINE_STEPS].sort();
const EXPECTED_ACTION_KEYS = [...Object.values(AuditAction)].sort();

describe("i18n ↔ catalogue parity", () => {
  it("expects a non-trivial key set (guard checks itself)", () => {
    expect(EXPECTED_STATUS_KEYS).toHaveLength(13);
    expect(EXPECTED_STEP_KEYS).toHaveLength(6);
    expect(EXPECTED_ACTION_KEYS).toHaveLength(29);
  });

  it.each(LOCALES)("%s status labels cover exactly ExtendedStatus (04 §C01)", (locale) => {
    expect(Object.keys(load(locale).status).sort()).toEqual(EXPECTED_STATUS_KEYS);
  });

  it.each(LOCALES)("%s timeline labels cover exactly TIMELINE_STEPS (04 §C05)", (locale) => {
    const timeline = load(locale).timeline as { step: Record<string, string> };
    expect(Object.keys(timeline.step).sort()).toEqual(EXPECTED_STEP_KEYS);
  });

  it.each(LOCALES)("%s audit-log action labels cover exactly AuditAction (04 §8.10)", (locale) => {
    const auditLog = load(locale).adminAuditLog as { action: Record<string, string> };
    expect(Object.keys(auditLog.action).sort()).toEqual(EXPECTED_ACTION_KEYS);
  });

  it.each(LOCALES)("%s has no empty status, timeline or action label", (locale) => {
    const data = load(locale);
    const timeline = data.timeline as { step: Record<string, string> };
    const auditLog = data.adminAuditLog as { action: Record<string, string> };
    for (const [key, value] of Object.entries(data.status)) {
      expect(typeof value === "string" && value.trim().length > 0, `status.${key}`).toBe(true);
    }
    for (const [key, value] of Object.entries(timeline.step)) {
      expect(typeof value === "string" && value.trim().length > 0, `step.${key}`).toBe(true);
    }
    for (const [key, value] of Object.entries(auditLog.action)) {
      expect(typeof value === "string" && value.trim().length > 0, `action.${key}`).toBe(true);
    }
  });
});
