"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  approveOutboundTransfer,
  listOutboundApprovals,
  releaseDepositRefundLimit,
} from "@/lib/api/admin";

const APPROVALS_ROOT = ["admin", "wallets", "outbound-approvals"] as const;

/** T169 — the approval gate's state and the payouts/refunds waiting for an approval (AD40). */
export function useOutboundApprovals() {
  return useQuery({ queryKey: APPROVALS_ROOT, queryFn: listOutboundApprovals });
}

/**
 * AD41. An approval changes the waiting list and, once the dispatcher sends the
 * row, the wallets overview (outbound queue + recent rows).
 */
export function useApproveOutboundTransfer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (blockchainTransactionId: string) =>
      approveOutboundTransfer(blockchainTransactionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: APPROVALS_ROOT });
      queryClient.invalidateQueries({ queryKey: ["admin", "wallets", "overview"] });
    },
  });
}

/**
 * T169a AD42 — release one refund held by its network's deposit refund daily
 * limit; the dispatcher sends it on its next tick.
 */
export function useReleaseDepositRefundLimit() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (blockchainTransactionId: string) =>
      releaseDepositRefundLimit(blockchainTransactionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: APPROVALS_ROOT });
      queryClient.invalidateQueries({ queryKey: ["admin", "wallets", "overview"] });
    },
  });
}
