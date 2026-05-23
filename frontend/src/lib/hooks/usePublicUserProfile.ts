"use client";

import { useQuery } from "@tanstack/react-query";
import { ApiError } from "@/lib/api/client";
import { getPublicUserProfile } from "@/lib/api/users";

/**
 * Hook for U5 — GET /users/{steamId} (07 §5.5).
 *
 * Used by S09 (public profile, 04 §7.5). 404 maps to a USER_NOT_FOUND
 * branch and must not trigger retry storms; other errors fall through
 * to the default React Query retry policy.
 */
export function usePublicUserProfile(steamId: string) {
  return useQuery({
    queryKey: ["users", "public", steamId],
    queryFn: () => getPublicUserProfile(steamId),
    enabled: steamId.length > 0,
    staleTime: 60_000,
    retry: (failureCount, error) => {
      if (error instanceof ApiError && error.status === 404) return false;
      return failureCount < 2;
    },
  });
}
