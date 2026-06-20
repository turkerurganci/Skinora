import { describe, it, expect } from "vitest";
import { parseTableSort, nextTableSort } from "@/lib/admin/tableSort";

const allowed = ["createdAt", "amount"] as const;

describe("parseTableSort", () => {
  it("returns the default order with no column when params are absent", () => {
    expect(parseTableSort(new URLSearchParams(), allowed)).toEqual({ by: null, order: "desc" });
  });

  it("accepts a valid sortBy + sortOrder", () => {
    const params = new URLSearchParams("sortBy=amount&sortOrder=asc");
    expect(parseTableSort(params, allowed)).toEqual({ by: "amount", order: "asc" });
  });

  it("ignores a sortBy outside allowedKeys and falls back to the default order", () => {
    const params = new URLSearchParams("sortBy=evil&sortOrder=asc");
    expect(parseTableSort(params, allowed)).toEqual({ by: null, order: "desc" });
  });

  it("uses the supplied defaultOrder when sortOrder is invalid", () => {
    const params = new URLSearchParams("sortBy=amount&sortOrder=sideways");
    expect(parseTableSort(params, allowed, "asc")).toEqual({ by: "amount", order: "asc" });
  });
});

describe("nextTableSort", () => {
  it("toggles the order when the same column is re-clicked", () => {
    expect(nextTableSort({ by: "amount", order: "asc" }, "amount")).toEqual({
      by: "amount",
      order: "desc",
    });
    expect(nextTableSort({ by: "amount", order: "desc" }, "amount")).toEqual({
      by: "amount",
      order: "asc",
    });
  });

  it("switches to a new column at the default order", () => {
    expect(nextTableSort({ by: "amount", order: "asc" }, "createdAt")).toEqual({
      by: "createdAt",
      order: "desc",
    });
  });
});
