import { describe, it, expect } from "vitest";
import { isUntranslatable, UNTRANSLATABLE_TERMS } from "@/i18n/untranslatable";

describe("isUntranslatable", () => {
  it("matches known terms case-insensitively and trims whitespace", () => {
    expect(isUntranslatable("USDT")).toBe(true);
    expect(isUntranslatable("usdt")).toBe(true);
    expect(isUntranslatable("  Steam  ")).toBe(true);
    expect(isUntranslatable("gas fee")).toBe(true);
  });

  it("rejects non-terms and empty input", () => {
    expect(isUntranslatable("hello")).toBe(false);
    expect(isUntranslatable("")).toBe(false);
  });

  it("recognizes every listed term", () => {
    for (const term of UNTRANSLATABLE_TERMS) {
      expect(isUntranslatable(term)).toBe(true);
    }
  });
});
