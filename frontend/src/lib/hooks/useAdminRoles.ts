"use client";

import { useQuery } from "@tanstack/react-query";
import { listAdminRoles } from "@/lib/api/admin";

/**
 * S19 admin role-list hook (AD11, 07 §9.11). Returns both the roles and the
 * static `availablePermissions` catalog in one call — the page renders the
 * yetki matrix from the catalog and the roles table from `roles`.
 */
export function useAdminRoles() {
  return useQuery({
    queryKey: ["admin", "roles", "list"],
    queryFn: listAdminRoles,
  });
}
