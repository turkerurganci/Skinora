"use client";

import { useQuery } from "@tanstack/react-query";
import { getAdminTransaction } from "@/lib/api/admin";

/** S16 admin transaction-detail hook (AD7, 07 §9.7). */
export function useAdminTransactionDetail(id: string, enabled = true) {
  return useQuery({
    queryKey: ["admin", "transactions", "detail", id],
    queryFn: () => getAdminTransaction(id),
    enabled: enabled && id.length > 0,
  });
}
