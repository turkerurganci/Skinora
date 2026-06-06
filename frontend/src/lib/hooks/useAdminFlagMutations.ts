"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { approveAdminFlag, rejectAdminFlag, holdUserTransactions } from "@/lib/api/admin";

/**
 * Invalidate every admin surface a flag review can change: the S13 list, the
 * S14 detail, and the S12 dashboard pending-flag badge (`["admin","dashboard"]`).
 */
function invalidateAdminFlagQueries(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: ["admin", "flags"] });
  queryClient.invalidateQueries({ queryKey: ["admin", "dashboard"] });
}

/** AD4 — approve ("İşleme Devam Et" / account "Flag Kaldır"). */
export function useApproveFlag() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, note }: { id: string; note?: string }) => approveAdminFlag(id, note),
    onSuccess: () => invalidateAdminFlagQueries(queryClient),
  });
}

/** AD5 — reject ("İptal Et" / account "Flag'i Doğrula"). */
export function useRejectFlag() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, note }: { id: string; note?: string }) => rejectAdminFlag(id, note),
    onSuccess: () => invalidateAdminFlagQueries(queryClient),
  });
}

/**
 * AD19d — bulk emergency hold of a flagged user's active transactions
 * ("Hold" action, 04 §8.3). Holding transactions doesn't change the flag's
 * review state, but it can change admin transaction views, so invalidate those.
 */
export function useHoldUserTransactions() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, reason }: { userId: string; reason: string }) =>
      holdUserTransactions(userId, reason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["admin", "transactions"] });
    },
  });
}
