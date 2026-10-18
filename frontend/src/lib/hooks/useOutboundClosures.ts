"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  approveOutboundClosure,
  listOutboundClosureCandidates,
  listOutboundClosures,
  proposeOutboundClosure,
  rejectOutboundClosure,
  type OutboundClosureStatus,
  type ProposeOutboundClosureRequest,
} from "@/lib/api/admin";

const CLOSURES_ROOT = ["admin", "wallets", "outbound-closures"] as const;

/** Open Ethereum outbound rows a closure can be proposed for. */
export function useOutboundClosureCandidates() {
  return useQuery({
    queryKey: [...CLOSURES_ROOT, "candidates"],
    queryFn: listOutboundClosureCandidates,
  });
}

/** Closures, optionally narrowed to one status. */
export function useOutboundClosures(status?: OutboundClosureStatus) {
  return useQuery({
    queryKey: [...CLOSURES_ROOT, "list", status ?? null],
    queryFn: () => listOutboundClosures(status),
  });
}

/**
 * A decision changes the closure lists, the candidate list (a live closure
 * flips `hasLiveClosure`, an approval removes the row) and the wallets
 * overview (outbound queue + recent rows).
 */
function useInvalidateClosureViews() {
  const queryClient = useQueryClient();
  return () => {
    queryClient.invalidateQueries({ queryKey: CLOSURES_ROOT });
    queryClient.invalidateQueries({ queryKey: ["admin", "wallets", "overview"] });
  };
}

export function useProposeOutboundClosure() {
  const invalidate = useInvalidateClosureViews();
  return useMutation({
    mutationFn: (request: ProposeOutboundClosureRequest) => proposeOutboundClosure(request),
    onSuccess: invalidate,
  });
}

export function useApproveOutboundClosure() {
  const invalidate = useInvalidateClosureViews();
  return useMutation({
    mutationFn: ({ id, txHash }: { id: string; txHash: string }) =>
      approveOutboundClosure(id, txHash),
    onSuccess: invalidate,
  });
}

export function useRejectOutboundClosure() {
  const invalidate = useInvalidateClosureViews();
  return useMutation({
    mutationFn: ({ id, note }: { id: string; note: string }) => rejectOutboundClosure(id, note),
    onSuccess: invalidate,
  });
}
