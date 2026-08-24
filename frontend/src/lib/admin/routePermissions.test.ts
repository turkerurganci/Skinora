import { describe, expect, it } from "vitest";
import { KNOWN_PERMISSION_KEYS } from "./permissionCatalog";
import { ADMIN_ROUTE_PERMISSIONS, permissionForAdminRoute } from "./routePermissions";
import { hasPermission } from "@/lib/auth/roles";

/**
 * WP2c (FE-permission-guard) — the map decides what the admin menu shows, so
 * what it must never do is invent a permission the backend does not have, or
 * "helpfully" relax a route to a weaker key than the endpoint enforces.
 */
describe("admin route → permission map", () => {
  it("only uses keys the backend catalogue defines", () => {
    for (const [path, key] of Object.entries(ADMIN_ROUTE_PERMISSIONS)) {
      if (key === null) continue;
      expect(KNOWN_PERMISSION_KEYS, `${path} maps to an unknown key`).toContain(key);
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
      "/admin/users": "MANAGE_ROLES",
      "/admin/audit-logs": "VIEW_AUDIT_LOG",
    });
  });

  it("keeps /admin/users on MANAGE_ROLES, matching AD15 rather than its name", () => {
    // The route reads like a VIEW_USERS surface but AD15 (07 §9.15) is the S19
    // role-assignment list and the controller enforces MANAGE_ROLES. Mirroring
    // the weaker-looking key would show a link that answers 403.
    expect(permissionForAdminRoute("/admin/users")).toBe("MANAGE_ROLES");
    expect(permissionForAdminRoute("/admin/users")).not.toBe("VIEW_USERS");
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
    return required === null || hasPermission(role, permissions, required);
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
