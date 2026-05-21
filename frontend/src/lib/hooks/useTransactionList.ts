"use client";

import { useQuery, keepPreviousData } from "@tanstack/react-query";
import { listTransactions, type TransactionListQuery } from "@/lib/api/transactions";

/**
 * Dashboard transaction list hook (S05, 07 §7.1).
 *
 * `keepPreviousData` smooths the tab + pagination experience: when the user
 * flips to the next page the previous page stays visible until the new one
 * resolves, avoiding a skeleton flash on every click.
 */
export function useTransactionList(query: TransactionListQuery, enabled = true) {
  return useQuery({
    queryKey: ["transactions", query.tab, query.page ?? 1, query.pageSize ?? 20],
    queryFn: () => listTransactions(query),
    enabled,
    placeholderData: keepPreviousData,
  });
}
