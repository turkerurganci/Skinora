import { describe, expect, it } from "vitest";
import { KNOWN_PERMISSION_KEYS } from "./permissionCatalog";
import { ADMIN_ROUTE_PERMISSIONS, permissionForAdminRoute } from "./routePermissions";
import { hasAnyPermission } from "@/lib/auth/roles";

/**
 * WP2c (FE-permission-guard) — the map decides what the admin menu shows, so
 * what it must never do is invent a permission the backend does not have, or
 * "helpfully" relax a route to a weaker key than the endpoint enforces.
 */
describe("admin route → permission map", () => {
  it("only uses keys the backend catalogue defines", () => {
    for (const [path, key] of Object.entries(ADMIN_ROUTE_PERMISSIONS)) {
      if (key === null) continue;
      for (const one of Array.isArray(key) ? key : [key]) {
        expect(KNOWN_PERMISSION_KEYS, `${path} maps to an unknown key`).toContain(one);
      }
    }
  });

  it("pins the policy measured on each backing endpoint", () => {
    // Read off the controllers, not inferred from the route names. If a policy
    // moves backend-side this list is what should fail first.
    expect(ADMIN_ROUTE_PERMISSIONS).toEqual({
      "/admin/dashboard": null,
      "/admin/flags": "VIEW_FLAGS",
      "/admin/disputes": "VIEW_DISPUTES",
      "/admin/transactions": "VIEW_TRANSACTIONS",
      "/admin/settings": "MANAGE_SETTINGS",
      "/admin/roles": "MANAGE_ROLES",
      "/admin/users": ["VIEW_USERS", "MANAGE_ROLES"],
      "/admin/audit-logs": "VIEW_AUDIT_LOG",
    });
  });

  it("opens /admin/users to either key, matching the widened AD15 policy", () => {
    // AD15 (07 §9.15) is a read-only directory and now accepts VIEW_USERS or
    // MANAGE_ROLES. It used to be MANAGE_ROLES alone, which hid the directory
    // from the very permission group whose detail page it links to —
    // `AdminUsersDirectoryPermissionMismatch`. The map mirrors the controller;
    // it must not widen further on its own, or it would show a 403 link.
    expect(permissionForAdminRoute("/admin/users")).toEqual(["VIEW_USERS", "MANAGE_ROLES"]);
  });

  it("leaves an unmapped route visible", () => {
    // A new admin route should stay reachable until someone maps it — the
    // backend still enforces its own policy, whereas a silently missing menu
    // entry is the kind of bug nobody can explain.
    expect(permissionForAdminRoute("/admin/something-new")).toBeNull();
  });
});

describe("menu visibility", () => {
  const visible = (role: string, permissions: string[], path: string) => {
    const required = permissionForAdminRoute(path);
    if (required === null) return true;
    return hasAnyPermission(role, permissions, Array.isArray(required) ? required : [required]);
  };

  it("shows a super admin everything even though their permission list is empty", () => {
    // The load-bearing case: the backend mints no Permission claims for a super
    // admin because authorization short-circuits on the role. Reading the list
    // literally would hide the entire admin surface from the one account that
    // can use all of it.
    for (const path of Object.keys(ADMIN_ROUTE_PERMISSIONS)) {
      expect(visible("super_admin", [], path), path).toBe(true);
    }
  });

  it("shows a scoped admin only their own surfaces plus the dashboard", () => {
    const role = "admin";
    const permissions = ["VIEW_FLAGS", "MANAGE_FLAGS"];

    expect(visible(role, permissions, "/admin/flags")).toBe(true);
    expect(visible(role, permissions, "/admin/dashboard")).toBe(true);

    expect(visible(role, permissions, "/admin/disputes")).toBe(false);
    expect(visible(role, permissions, "/admin/transactions")).toBe(false);
    expect(visible(role, permissions, "/admin/settings")).toBe(false);
    expect(visible(role, permissions, "/admin/roles")).toBe(false);
    expect(visible(role, permissions, "/admin/users")).toBe(false);
    expect(visible(role, permissions, "/admin/audit-logs")).toBe(false);
  });

  it("shows the user directory to a VIEW_USERS-only admin", () => {
    // The case the backlog line was opened for: this admin can open a user's
    // detail page (AD16), so hiding the directory that links to it left them
    // needing to know a Steam ID by heart.
    const role = "admin";
    expect(visible(role, ["VIEW_USERS"], "/admin/users")).toBe(true);
    // Widening the directory must not widen role management with it.
    expect(visible(role, ["VIEW_USERS"], "/admin/roles")).toBe(false);
    // The other holder of the key still sees it.
    expect(visible(role, ["MANAGE_ROLES"], "/admin/users")).toBe(true);
  });

  it("hides everything but the dashboard from an admin holding no permissions", () => {
    for (const path of Object.keys(ADMIN_ROUTE_PERMISSIONS)) {
      expect(visible("admin", [], path), path).toBe(path === "/admin/dashboard");
    }
  });

  it("does not let a plain user's empty list open anything gated", () => {
    expect(visible("user", [], "/admin/flags")).toBe(false);
    expect(visible("user", [], "/admin/roles")).toBe(false);
  });
});
