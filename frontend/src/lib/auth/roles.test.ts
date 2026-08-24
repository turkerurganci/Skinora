import { describe, it, expect } from "vitest";
import { isAdminRole, hasPermission, ADMIN_ROLES } from "@/lib/auth/roles";

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

describe("hasPermission (WP2c)", () => {
  it("grants a super admin everything, including keys nobody was issued", () => {
    // AccessTokenGenerator mints NO Permission claims for a super admin because
    // PermissionAuthorizationHandler short-circuits on the role. This branch is
    // what keeps /auth/me's empty list from hiding the whole admin surface.
    expect(hasPermission("super_admin", [], "MANAGE_ROLES")).toBe(true);
    expect(hasPermission("super_admin", undefined, "VIEW_FLAGS")).toBe(true);
    expect(hasPermission("super_admin", null, "ANYTHING_AT_ALL")).toBe(true);
  });

  it("grants a scoped admin exactly the keys on their token", () => {
    const held = ["VIEW_FLAGS", "MANAGE_FLAGS"];
    expect(hasPermission("admin", held, "VIEW_FLAGS")).toBe(true);
    expect(hasPermission("admin", held, "MANAGE_FLAGS")).toBe(true);
    expect(hasPermission("admin", held, "MANAGE_ROLES")).toBe(false);
    expect(hasPermission("admin", held, "VIEW_AUDIT_LOG")).toBe(false);
  });

  it("does not promote a plain user who somehow carries permission keys", () => {
    // Defence in depth against a malformed payload: the role, not the list, is
    // what makes someone an admin.
    expect(hasPermission("admin", [], "VIEW_FLAGS")).toBe(false);
    expect(hasPermission("user", ["MANAGE_ROLES"], "MANAGE_ROLES")).toBe(false);
  });

  it("treats a missing list as holding nothing", () => {
    expect(hasPermission("admin", undefined, "VIEW_FLAGS")).toBe(false);
    expect(hasPermission("admin", null, "VIEW_FLAGS")).toBe(false);
    expect(hasPermission(null, null, "VIEW_FLAGS")).toBe(false);
  });
});
