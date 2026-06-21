import { describe, it, expect } from "vitest";
import { toEndOfDay } from "@/lib/utils/date";

describe("toEndOfDay", () => {
  it("widens a bare yyyy-mm-dd to the last instant of the day", () => {
    expect(toEndOfDay("2026-06-20")).toBe("2026-06-20T23:59:59.999");
  });

  it("returns undefined for undefined input", () => {
    expect(toEndOfDay(undefined)).toBeUndefined();
  });

  it("returns undefined for an empty string (matches the API truthy guard)", () => {
    expect(toEndOfDay("")).toBeUndefined();
  });
});
