"use client";

import { useEffect } from "react";
import {
  transactionsHubClient,
  type TransactionHubHandlers,
} from "@/lib/signalr/TransactionsHubClient";

/**
 * Subscribes the calling component to the `/hubs/transactions` SignalR room
 * for a single transaction id (T96 — 07 §11.1 RT1). The S07 detail page
 * mounts this hook with the route param; per-event handlers can be passed
 * for ad-hoc UI behaviour (e.g. flashing a banner) on top of the global
 * cache invalidation already wired in {@link RealtimeProvider}.
 *
 * The subscription is no-op when `transactionId` is undefined (loading
 * fallback). When the id changes the prior subscription is disposed first
 * so the SignalR group membership is kept tight.
 */
export function useTransactionRealtime(
  transactionId: string | undefined,
  handlers: TransactionHubHandlers = {},
) {
  useEffect(() => {
    if (!transactionId) return;
    const dispose = transactionsHubClient.subscribe(transactionId, handlers);
    return dispose;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [transactionId]);
}
