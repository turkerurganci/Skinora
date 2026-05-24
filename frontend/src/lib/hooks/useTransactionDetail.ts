"use client";

import { useQuery } from "@tanstack/react-query";
import { getTransactionDetail } from "@/lib/api/transactions";

/**
 * S07 detail page hook (07 §7.5).
 *
 * `staleTime` is low (5s) because the detail surface is state-driven —
 * `RealtimeProvider` (T96 — 07 §11.1) invalidates this cache key on every
 * `TransactionStatusChanged` / `PaymentDetected` / `PaymentConfirmed` /
 * `DisputeUpdate` / `FlagResolved` / `EmergencyHoldApplied` /
 * `EmergencyHoldReleased` push. `CountdownSync` patches the timeout block
 * in place (no refetch). Mutations (accept/cancel) still invalidate on
 * success as a defensive fallback when the SignalR channel is down.
 */
export function useTransactionDetail(id: string | undefined) {
  return useQuery({
    queryKey: ["transactions", "detail", id],
    queryFn: () => getTransactionDetail(id!),
    enabled: Boolean(id),
    staleTime: 5_000,
  });
}
