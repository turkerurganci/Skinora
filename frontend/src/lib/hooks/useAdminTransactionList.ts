"use client";

import { useQuery, keepPreviousData } from "@tanstack/react-query";
import { listAdminTransactions, type AdminTransactionListQuery } from "@/lib/api/admin";

/**
 * S15 admin transaction-list hook (AD6, 07 §9.6). `keepPreviousData` smooths
 * the filter + pagination experience — the current page stays visible while
 * the next slice resolves instead of flashing a skeleton on every change.
 */
export function useAdminTransactionList(query: AdminTransactionListQuery, enabled = true) {
  return useQuery({
    queryKey: [
      "admin",
      "transactions",
      "list",
      query.status ?? null,
      query.statusGroup ?? null,
      query.stablecoin ?? null,
      query.dateFrom ?? null,
      query.dateTo ?? null,
      query.minAmount ?? null,
      query.maxAmount ?? null,
      query.search ?? null,
      query.sortBy ?? null,
      query.sortOrder ?? null,
      query.page ?? 1,
      query.pageSize ?? 20,
    ],
    queryFn: () => listAdminTransactions(query),
    enabled,
    placeholderData: keepPreviousData,
  });
}
