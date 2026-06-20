import { describe, it, expect } from "vitest";
import { formatStablecoin, formatPercent, formatRelativeTime } from "@/lib/utils/format";

describe("formatStablecoin", () => {
  it("passes decimal strings through verbatim with the symbol", () => {
    expect(formatStablecoin("100.50000000", "USDT")).toBe("100.50000000 USDT");
  });

  it("formats numbers locale-invariantly with a dot and 2 fraction digits", () => {
    expect(formatStablecoin(100.5, "USDT")).toBe("100.50 USDT");
  });

  it("honors fractionDigits and never groups thousands", () => {
    expect(formatStablecoin(1234, "USDC", { fractionDigits: 0 })).toBe("1234 USDC");
  });
});

describe("formatPercent", () => {
  it("uses the en decimal point", () => {
    expect(formatPercent(99.5, "en")).toBe("99.5%");
  });

  it("uses the tr decimal comma", () => {
    expect(formatPercent(99.5, "tr")).toBe("99,5%");
  });

  it("falls back to the default locale for unsupported locales", () => {
    // Exercises @/i18n/routing -> normalizeLocale fallback under vitest:
    // an unsupported locale must format identically to the default (en).
    expect(formatPercent(1234.5, "qq-unsupported")).toBe(formatPercent(1234.5, "en"));
    expect(formatPercent(1234.5, "en")).toBe("1,234.5%");
  });
});

describe("formatRelativeTime", () => {
  it("formats a 30-seconds-ago instant with an injected now (deterministic)", () => {
    const now = new Date("2026-01-01T00:00:30Z");
    const then = new Date("2026-01-01T00:00:00Z");
    expect(formatRelativeTime(then, "en", now)).toMatch(/30 sec/);
  });
});
