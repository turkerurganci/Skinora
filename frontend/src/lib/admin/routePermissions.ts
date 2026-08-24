import type { KnownPermissionKey } from "./permissionCatalog";

/**
 * WP2c — the permission each admin route's own backing endpoint enforces.
 *
 * Every value here was read off the controller that serves the route, not
 * inferred from the route name, because the two do not always agree (see
 * `/admin/users` below). `null` means the route is gated on admin access alone
 * (`AuthPolicies.AdminAccess`) with no specific permission.
 *
 * This drives what the client *shows*. It is deliberately not a security
 * boundary: the backend policy is the authoritative check and still answers 403
 * for anyone who reaches an endpoint directly.
 */
export const ADMIN_ROUTE_PERMISSIONS: Readonly<
  Record<string, KnownPermissionKey | null>
> = {
  // AdminController.Dashboard — AuthPolicies.AdminAccess, no permission key.
  "/admin/dashboard": null,
  // AdminFlagsController — PolicyViewFlags on the list + detail.
  "/admin/flags": "VIEW_FLAGS",
  // AdminDisputesController — PolicyViewDisputes on the list + detail.
  "/admin/disputes": "VIEW_DISPUTES",
  // AdminTransactionsController — PolicyViewTransactions on the list + detail.
  "/admin/transactions": "VIEW_TRANSACTIONS",
  // AdminController.GetSettings — PolicyManageSettings (there is no read-only
  // settings permission; viewing and changing share one key).
  "/admin/settings": "MANAGE_SETTINGS",
  // AdminController role endpoints — PolicyManageRoles.
  "/admin/roles": "MANAGE_ROLES",
  // AD15 GET /admin/users is PolicyManageRoles, NOT VIEW_USERS — 07 §9.15
  // defines it as the S19 role-assignment list, and the code matches the spec.
  // VIEW_USERS covers AD16/AD16b, the S20 detail page this directory links to.
  // The consequence is recorded in the backlog as
  // `AdminUsersDirectoryPermissionMismatch`: an admin holding only VIEW_USERS
  // can open a user's detail page but cannot see the directory that leads to
  // it. Mirroring the measured policy is the right call here — showing a link
  // that answers 403 would be worse than hiding one.
  "/admin/users": "MANAGE_ROLES",
  // AdminController.GetAuditLogs — PolicyViewAuditLog.
  "/admin/audit-logs": "VIEW_AUDIT_LOG",
};

/**
 * Permission required to display `path`, or `null` when admin access is enough.
 * An unmapped path also returns `null` — a new admin route stays visible until
 * someone maps it, which fails toward a 403 the backend already handles rather
 * than toward a silently missing menu entry nobody can explain.
 */
export function permissionForAdminRoute(path: string): KnownPermissionKey | null {
  return ADMIN_ROUTE_PERMISSIONS[path] ?? null;
}
