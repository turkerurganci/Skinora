/**
 * Admin role identifiers as emitted by the backend JWT `role` claim and the
 * `GET /auth/me` `role` field (AuthRoles.Admin / AuthRoles.SuperAdmin —
 * "admin" / "super_admin"). Kept in one place so the client-side admin route
 * guard (WP13) and the auth store stay in sync with the backend contract.
 */
export const ADMIN_ROLES = ["admin", "super_admin"] as const;

export function isAdminRole(role: string | null | undefined): boolean {
  return role === "admin" || role === "super_admin";
}

export function isSuperAdminRole(role: string | null | undefined): boolean {
  return role === "super_admin";
}

/**
 * WP2c — does this caller hold `permission`?
 *
 * **Not a security boundary.** Every admin endpoint enforces its own
 * `Permission:<KEY>` policy server-side and that stays the authoritative check;
 * this only decides whether the client shows a surface at all, so nobody is
 * walked into a 403 for a page they were never allowed to open.
 *
 * The `super_admin` branch is the load-bearing part. `AccessTokenGenerator`
 * mints **no** Permission claims for a super admin, because
 * `PermissionAuthorizationHandler` short-circuits on the role — so
 * `/auth/me` returns an empty list for the one account that can do everything.
 * Reading that list literally would hide the entire admin surface from them.
 */
export function hasPermission(
  role: string | null | undefined,
  permissions: readonly string[] | null | undefined,
  permission: string,
): boolean {
  if (isSuperAdminRole(role)) return true;
  // The role is what makes someone an admin, not the list. Permission claims
  // are only ever minted for admin roles, so a non-admin carrying keys means a
  // malformed payload — and the answer to a malformed payload is "no".
  if (!isAdminRole(role)) return false;
  return permissions?.includes(permission) ?? false;
}

/**
 * True when the caller holds ANY of `required` — the client-side mirror of a
 * comma-separated `Permission:` policy on the backend (see
 * `PermissionRequirement`). An empty list means "no permission needed", which
 * matches how an unmapped admin route behaves.
 */
export function hasAnyPermission(
  role: string | null | undefined,
  permissions: readonly string[] | null | undefined,
  required: readonly string[],
): boolean {
  if (required.length === 0) return true;
  return required.some((p) => hasPermission(role, permissions, p));
}
