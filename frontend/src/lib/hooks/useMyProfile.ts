"use client";

import { useQuery } from "@tanstack/react-query";
import { ApiError } from "@/lib/api/client";
import { getMyProfile } from "@/lib/api/users";

/**
 * Hook for U1 — GET /users/me (07 §5.1).
 *
 * Used by S07 buyer-side CREATED to prefill the refund wallet input from
 * `refundWalletAddress`. Skips when unauthenticated; 401 is mapped to
 * `enabled=false` via the `enabled` flag rather than retry storms.
 */
export function useMyProfile(enabled: boolean) {
  return useQuery({
    queryKey: ["users", "me"],
    queryFn: getMyProfile,
    enabled,
    staleTime: 60_000,
    retry: (failureCount, error) => {
      if (error instanceof ApiError && error.status === 401) return false;
      return failureCount < 2;
    },
  });
}
