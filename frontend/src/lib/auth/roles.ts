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
