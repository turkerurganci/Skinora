import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";
import { TransactionStatus } from "@/types/enums";
import { TIMELINE_STEPS } from "@/components/common/TransactionTimeline";

/**
 * T134 — i18n katalog bekçisi.
 *
 * `check-i18n.mjs` proves the four locales agree with EACH OTHER. It cannot see
 * whether they agree with the enum: before T134 all four carried
 * `status.ITEM_ESCROWED` and none carried `status.SELLER_CONFIRMED`, and parity
 * was green the whole time — every locale was wrong in the same way.
 *
 * This test measures the other axis: catalogue → i18n. `StatusBadge` renders
 * `t(status)` for any `ExtendedStatus`, and `TransactionTimeline` renders
 * `t("step." + step)` for each entry of TIMELINE_STEPS, so a missing key is a
 * runtime next-intl error on a real screen, not a cosmetic gap.
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

describe("i18n ↔ catalogue parity", () => {
  it("expects a non-trivial key set (guard checks itself)", () => {
    expect(EXPECTED_STATUS_KEYS).toHaveLength(13);
    expect(EXPECTED_STEP_KEYS).toHaveLength(6);
  });

  it.each(LOCALES)("%s status labels cover exactly ExtendedStatus (04 §C01)", (locale) => {
    expect(Object.keys(load(locale).status).sort()).toEqual(EXPECTED_STATUS_KEYS);
  });

  it.each(LOCALES)("%s timeline labels cover exactly TIMELINE_STEPS (04 §C05)", (locale) => {
    const timeline = load(locale).timeline as { step: Record<string, string> };
    expect(Object.keys(timeline.step).sort()).toEqual(EXPECTED_STEP_KEYS);
  });

  it.each(LOCALES)("%s has no empty status or timeline label", (locale) => {
    const data = load(locale);
    const timeline = data.timeline as { step: Record<string, string> };
    for (const [key, value] of Object.entries(data.status)) {
      expect(typeof value === "string" && value.trim().length > 0, `status.${key}`).toBe(true);
    }
    for (const [key, value] of Object.entries(timeline.step)) {
      expect(typeof value === "string" && value.trim().length > 0, `step.${key}`).toBe(true);
    }
  });
});
