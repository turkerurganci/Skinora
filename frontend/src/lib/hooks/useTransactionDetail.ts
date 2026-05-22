"use client";

import { useQuery } from "@tanstack/react-query";
import { getTransactionDetail } from "@/lib/api/transactions";

/**
 * S07 detail page hook (07 §7.5).
 *
 * `staleTime` is low (5s) because the detail surface is state-driven —
 * SignalR push (T96) will invalidate this query on every transition.
 * Until T96 ships, the user can manually refetch via mutations
 * (accept/cancel onSuccess) and a window-focus refetch handles short
 * polling needs. `refetchOnWindowFocus` defaults to true in React Query.
 */
export function useTransactionDetail(id: string | undefined) {
  return useQuery({
    queryKey: ["transactions", "detail", id],
    queryFn: () => getTransactionDetail(id!),
    enabled: Boolean(id),
    staleTime: 5_000,
  });
}
