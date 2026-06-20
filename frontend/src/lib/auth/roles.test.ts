import { describe, it, expect } from "vitest";
import { isAdminRole, ADMIN_ROLES } from "@/lib/auth/roles";

describe("isAdminRole", () => {
  it("is true for the two admin roles", () => {
    expect(isAdminRole("admin")).toBe(true);
    expect(isAdminRole("super_admin")).toBe(true);
  });

  it("is false for non-admin roles and nullish values", () => {
    expect(isAdminRole("user")).toBe(false);
    expect(isAdminRole(null)).toBe(false);
    expect(isAdminRole(undefined)).toBe(false);
    expect(isAdminRole("")).toBe(false);
  });

  it("ADMIN_ROLES lists exactly admin + super_admin", () => {
    expect([...ADMIN_ROLES]).toEqual(["admin", "super_admin"]);
  });
});
