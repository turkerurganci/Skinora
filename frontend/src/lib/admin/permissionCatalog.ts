/**
 * Client mirror of the backend `PermissionCatalog` (07 §9.11 / 04 §8.8 — the 12
 * admin permissions). The AD11 response already returns `availablePermissions`
 * in this order, so the S19 yetki matrix maps over the server list directly;
 * this constant exists so the i18n `adminRoles.permissions.*` block has a
 * checkable contract and the label fallback knows every documented key.
 *
 * The server `label` is Turkish-fixed, so the UI localizes each permission by
 * `key` and only falls back to the server label for a key not yet in the i18n
 * catalog (forward-compat if a 13th permission is added backend-side before the
 * frontend catches up).
 */
export const KNOWN_PERMISSION_KEYS = [
  "VIEW_FLAGS",
  "MANAGE_FLAGS",
  "VIEW_TRANSACTIONS",
  "MANAGE_SETTINGS",
  "VIEW_USERS",
  "MANAGE_ROLES",
  "VIEW_AUDIT_LOG",
  "CANCEL_TRANSACTIONS",
  "EMERGENCY_HOLD",
  "VIEW_DISPUTES",
  "MANAGE_DISPUTES",
  "MANAGE_SANCTIONS",
] as const;

export type KnownPermissionKey = (typeof KNOWN_PERMISSION_KEYS)[number];

/** i18n sub-key under `adminRoles.permissions` for a permission `key`. */
export function permissionLabelKey(key: string): string {
  return `permissions.${key}`;
}
