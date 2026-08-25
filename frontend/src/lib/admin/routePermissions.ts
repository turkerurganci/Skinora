import type { KnownPermissionKey } from "./permissionCatalog";

/**
 * WP2c — the permission each admin route's own backing endpoint enforces.
 *
 * Every value here was read off the controller that serves the route, not
 * inferred from the route name, because the two do not always agree (see
 * `/admin/users` below). `null` means the route is gated on admin access alone
 * (`AuthPolicies.AdminAccess`) with no specific permission. An array means the
 * endpoint accepts ANY of the listed keys, mirroring a comma-separated
 * `Permission:` policy on the controller.
 *
 * This drives what the client *shows*. It is deliberately not a security
 * boundary: the backend policy is the authoritative check and still answers 403
 * for anyone who reaches an endpoint directly.
 */
export type AdminRoutePermission = KnownPermissionKey | readonly KnownPermissionKey[] | null;

export const ADMIN_ROUTE_PERMISSIONS: Readonly<Record<string, AdminRoutePermission>> = {
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
  // AD15 GET /admin/users accepts VIEW_USERS *or* MANAGE_ROLES (07 §9.15).
  // It used to require MANAGE_ROLES alone, which left an admin holding only
  // VIEW_USERS able to open a user's detail page but unable to reach the
  // directory that links to it — recorded as
  // `AdminUsersDirectoryPermissionMismatch` and closed by widening the
  // read-only directory rather than this map. Role *assignment* still lives
  // behind MANAGE_ROLES on its own endpoints.
  "/admin/users": ["VIEW_USERS", "MANAGE_ROLES"],
  // AdminController.GetAuditLogs — PolicyViewAuditLog.
  "/admin/audit-logs": "VIEW_AUDIT_LOG",
};

/**
 * Permission(s) required to display `path`, or `null` when admin access is
 * enough. An array means any one of them suffices. An unmapped path also
 * returns `null` — a new admin route stays visible until someone maps it, which
 * fails toward a 403 the backend already handles rather than toward a silently
 * missing menu entry nobody can explain.
 */
export function permissionForAdminRoute(path: string): AdminRoutePermission {
  return ADMIN_ROUTE_PERMISSIONS[path] ?? null;
}
