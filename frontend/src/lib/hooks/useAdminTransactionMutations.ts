"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  cancelAdminTransaction,
  applyEmergencyHold,
  releaseEmergencyHold,
  type EmergencyHoldReleaseAction,
} from "@/lib/api/admin";

/**
 * Invalidate every admin surface an AD19/AD19b/AD19c lifecycle action can
 * change: the S15 list, the S16 detail, and the S12 dashboard
 * active-transaction counter (`["admin","dashboard"]`).
 */
function invalidateAdminTransactionQueries(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: ["admin", "transactions"] });
  queryClient.invalidateQueries({ queryKey: ["admin", "dashboard"] });
}

/** AD19 — admin cancel ("İşlemi İptal Et", 03 §8.7). */
export function useCancelTransaction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      cancelAdminTransaction(id, reason),
    onSuccess: () => invalidateAdminTransactionQueries(queryClient),
  });
}

/** AD19b — apply emergency hold ("Emergency Hold Uygula", 03 §8.8). */
export function useApplyEmergencyHold() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => applyEmergencyHold(id, reason),
    onSuccess: () => invalidateAdminTransactionQueries(queryClient),
  });
}

/** AD19c — release an emergency hold, RESUME or CANCEL ("Hold Kaldır", 03 §8.8). */
export function useReleaseEmergencyHold() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      action,
      note,
    }: {
      id: string;
      action: EmergencyHoldReleaseAction;
      note: string;
    }) => releaseEmergencyHold(id, action, note),
    onSuccess: () => invalidateAdminTransactionQueries(queryClient),
  });
}
