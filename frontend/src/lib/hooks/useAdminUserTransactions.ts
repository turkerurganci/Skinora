"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { getAdminUserTransactions } from "@/lib/api/admin";

/**
 * S20 per-user transaction history hook (AD16b, 07 §9.17). `keepPreviousData`
 * keeps the current page visible while the next slice resolves.
 */
export function useAdminUserTransactions(steamId: string, page = 1, pageSize = 20) {
  return useQuery({
    queryKey: ["admin", "users", "transactions", steamId, page, pageSize],
    queryFn: () => getAdminUserTransactions(steamId, page, pageSize),
    enabled: steamId.length > 0,
    placeholderData: keepPreviousData,
  });
}
