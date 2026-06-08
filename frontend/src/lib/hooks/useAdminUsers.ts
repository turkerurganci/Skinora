"use client";

import { useQuery, keepPreviousData } from "@tanstack/react-query";
import { listAdminUsers, type AdminUserListQuery } from "@/lib/api/admin";

/**
 * S19 admin-user list hook (AD15, 07 §9.15) for the "Kullanıcı-Rol Atama"
 * section. `keepPreviousData` smooths the search + pagination experience — the
 * current page stays visible while the next slice resolves instead of flashing
 * a skeleton on every keystroke / page change.
 */
export function useAdminUsers(query: AdminUserListQuery, enabled = true) {
  return useQuery({
    queryKey: [
      "admin",
      "users",
      "list",
      query.search ?? null,
      query.roleId ?? null,
      query.page ?? 1,
      query.pageSize ?? 20,
    ],
    queryFn: () => listAdminUsers(query),
    enabled,
    placeholderData: keepPreviousData,
  });
}
