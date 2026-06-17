"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { resolveAdminDispute } from "@/lib/api/admin";
import type { DisputeResolutionOutcome } from "@/types/enums";

interface ResolveArgs {
  id: string;
  outcome: DisputeResolutionOutcome;
  adminNote: string;
}

/**
 * WP5 — admin dispute resolve mutation (AD29, 07 §9.x). On success invalidates
 * the dispute queries so the queue + any open detail refresh.
 */
export function useAdminDisputeResolve() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, outcome, adminNote }: ResolveArgs) =>
      resolveAdminDispute(id, outcome, adminNote),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["admin", "disputes"] });
    },
  });
}
