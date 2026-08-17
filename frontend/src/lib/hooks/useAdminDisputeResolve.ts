"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { resolveAdminDispute } from "@/lib/api/admin";
import type { DisputeResolutionOutcome } from "@/types/enums";

interface ResolveArgs {
  id: string;
  outcome: DisputeResolutionOutcome;
  adminNote: string;
  /** T131 — required by AD29 when the ruling overrides a proven delivery (03 §6.4). */
  overrideReason?: string;
}

/**
 * WP5 — admin dispute resolve mutation (AD29, 07 §9.x). On success invalidates
 * the dispute queries so the queue + any open detail refresh.
 */
export function useAdminDisputeResolve() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, outcome, adminNote, overrideReason }: ResolveArgs) =>
      resolveAdminDispute(id, outcome, adminNote, overrideReason),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["admin", "disputes"] });
    },
  });
}
