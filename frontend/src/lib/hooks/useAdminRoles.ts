"use client";

import { useQuery } from "@tanstack/react-query";
import { listAdminRoles } from "@/lib/api/admin";

/**
 * S19 admin role-list hook (AD11, 07 §9.11). Returns both the roles and the
 * static `availablePermissions` catalog in one call — the page renders the
 * yetki matrix from the catalog and the roles table from `roles`.
 *
 * `enabled` exists because AD11 enforces MANAGE_ROLES while AD15 (the user
 * directory) no longer does. A VIEW_USERS-only admin legitimately reaches the
 * directory but cannot read the role list, and firing a request we know
 * answers 403 would put a red herring in the network log and the server's
 * audit trail. Callers that can hold either key pass their own check here.
 */
export function useAdminRoles(options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["admin", "roles", "list"],
    queryFn: listAdminRoles,
    enabled: options?.enabled ?? true,
  });
}
