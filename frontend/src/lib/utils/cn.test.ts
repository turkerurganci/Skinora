import { describe, it, expect } from "vitest";
import { cn } from "@/lib/utils/cn";

describe("cn", () => {
  it("joins truthy class values with a single space", () => {
    expect(cn("a", "b", "c")).toBe("a b c");
  });

  it("drops falsy values (false / null / undefined / 0 / empty string)", () => {
    expect(cn("a", false, null, undefined, 0, "", "b")).toBe("a b");
  });

  it("returns an empty string when nothing is truthy", () => {
    expect(cn(false, null, undefined)).toBe("");
  });
});
