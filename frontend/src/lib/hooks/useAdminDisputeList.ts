"use client";

import { useQuery, keepPreviousData } from "@tanstack/react-query";
import { listAdminDisputes, type AdminDisputeListQuery } from "@/lib/api/admin";

/**
 * WP5 — admin dispute queue list hook (AD27, 07 §9.x). `keepPreviousData`
 * smooths filter + pagination changes (no skeleton flash on every change).
 */
export function useAdminDisputeList(query: AdminDisputeListQuery, enabled = true) {
  return useQuery({
    queryKey: [
      "admin",
      "disputes",
      "list",
      query.status ?? null,
      query.type ?? null,
      query.page ?? 1,
      query.pageSize ?? 20,
    ],
    queryFn: () => listAdminDisputes(query),
    enabled,
    placeholderData: keepPreviousData,
  });
}
