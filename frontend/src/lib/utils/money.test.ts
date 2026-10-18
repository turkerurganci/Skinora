import { describe, it, expect } from "vitest";
import { calculateCommission, calculateTotal, centsToString, toCents } from "@/lib/utils/money";

// T144 — the form previews the commission the backend will persist:
// ROUND(price × rate, 2, toward zero), total = price + commission.

describe("calculateCommission", () => {
  it.each([
    // The reported case: 10.55 × 2% = 0.211 → 0.21, total 10.76.
    ["10.55", 0.02, "0.21", "10.76"],
    // Exact half cent: truncation, not half-up (Intl would show 0.21).
    ["10.25", 0.02, "0.20", "10.45"],
    ["10.75", 0.02, "0.21", "10.96"],
    ["99.99", 0.025, "2.49", "102.48"],
    ["100.00", 0.02, "2.00", "102.00"],
    ["250.75", 0.02, "5.01", "255.76"],
    ["0.49", 0.02, "0.00", "0.49"],
    ["10.5", 0.0175, "0.18", "10.68"],
  ])("price %s at rate %s → commission %s, total %s", (price, rate, commission, total) => {
    expect(calculateCommission(price, rate)).toBe(commission);
    expect(calculateTotal(price, rate)).toBe(total);
  });

  it.each(["", "  ", "abc", "10.555", "-1", "0", "0.00", "1e3", "10,55"])(
    "returns null for %j (not a positive whole-cent price)",
    (price) => {
      expect(calculateCommission(price, 0.02)).toBeNull();
      expect(calculateTotal(price, 0.02)).toBeNull();
    },
  );

  it("returns null for a negative or non-finite rate", () => {
    expect(calculateCommission("10.00", -0.01)).toBeNull();
    expect(calculateCommission("10.00", Number.NaN)).toBeNull();
  });

  // Independent closed forms per rate (integer cents): 2% = cents / 50,
  // 2.5% = cents / 40, 1.75% = cents × 7 / 400 — each floored.
  const references: Array<[number, (cents: number) => number]> = [
    [0.02, (c) => Math.floor(c / 50)],
    [0.025, (c) => Math.floor(c / 40)],
    [0.0175, (c) => Math.floor((c * 7) / 400)],
  ];

  it.each(references)(
    "every price 10.00–99.99 at rate %s matches the reference and is 2-decimal",
    (rate, reference) => {
      for (let cents = 1000; cents <= 9999; cents += 1) {
        const price = centsToString(cents);
        const commission = calculateCommission(price, rate);
        const total = calculateTotal(price, rate);
        const expected = reference(cents);
        expect(commission).toBe(centsToString(expected));
        expect(total).toBe(centsToString(cents + expected));
        expect(total).toMatch(/^\d+\.\d{2}$/);
      }
    },
  );
});

describe("toCents / centsToString", () => {
  it("parses whole-cent strings into integer cents", () => {
    expect(toCents("10.55")).toBe(1055);
    expect(toCents("10.5")).toBe(1050);
    expect(toCents("10")).toBe(1000);
    expect(toCents(" 0.01 ")).toBe(1);
    expect(toCents("10.555")).toBeNull();
  });

  it("formats cents with two decimals", () => {
    expect(centsToString(5)).toBe("0.05");
    expect(centsToString(1076)).toBe("10.76");
    expect(centsToString(10200)).toBe("102.00");
  });
});
