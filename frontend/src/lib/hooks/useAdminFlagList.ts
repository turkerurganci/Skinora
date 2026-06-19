"use client";

import { useQuery, keepPreviousData } from "@tanstack/react-query";
import { listAdminFlags, type AdminFlagListQuery } from "@/lib/api/admin";

/**
 * S13 flag-queue list hook (AD2, 07 §9.2). `keepPreviousData` smooths the
 * filter + pagination experience — the current page stays visible while the
 * next slice resolves instead of flashing a skeleton on every change.
 */
export function useAdminFlagList(query: AdminFlagListQuery, enabled = true) {
  return useQuery({
    queryKey: [
      "admin",
      "flags",
      "list",
      query.scope ?? null,
      query.type ?? null,
      query.reviewStatus ?? null,
      query.dateFrom ?? null,
      query.dateTo ?? null,
      query.sortBy ?? null,
      query.sortOrder ?? null,
      query.page ?? 1,
      query.pageSize ?? 20,
    ],
    queryFn: () => listAdminFlags(query),
    enabled,
    placeholderData: keepPreviousData,
  });
}
