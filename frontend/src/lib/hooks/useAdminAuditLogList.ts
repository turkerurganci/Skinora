"use client";

import { useQuery, keepPreviousData } from "@tanstack/react-query";
import { listAdminAuditLogs, type AdminAuditLogQuery } from "@/lib/api/admin";

/**
 * S21 audit-log list hook (AD18, 07 §9.19). `keepPreviousData` keeps the current
 * page visible while a filter / page change resolves instead of flashing a
 * skeleton on every change — same pattern as the flag + transaction queues.
 */
export function useAdminAuditLogList(query: AdminAuditLogQuery, enabled = true) {
  return useQuery({
    queryKey: [
      "admin",
      "audit-logs",
      "list",
      query.category ?? null,
      query.dateFrom ?? null,
      query.dateTo ?? null,
      query.search ?? null,
      query.transactionId ?? null,
      query.page ?? 1,
      query.pageSize ?? 20,
    ],
    queryFn: () => listAdminAuditLogs(query),
    enabled,
    placeholderData: keepPreviousData,
  });
}
