"use client";

import { useQuery } from "@tanstack/react-query";
import { ApiError } from "@/lib/api/client";
import { getAccountSettings } from "@/lib/api/settings";

/**
 * Hook for U6 — GET /users/me/settings (07 §5.6). Consumed by S10
 * (04 §7.6). Skips when unauthenticated; 401 is mapped to `enabled=false`
 * to avoid retry storms (mirrors `useMyProfile`).
 *
 * Cache key is shared with mutation invalidations from
 * NotificationPreferencesSection, LinkedAccountsSection, etc. — every
 * U7/U8/U9/U11/U12/U15/U16 writer calls `queryClient.invalidateQueries(["users", "me", "settings"])`.
 */
export function useAccountSettings(enabled: boolean) {
  return useQuery({
    queryKey: ["users", "me", "settings"],
    queryFn: getAccountSettings,
    enabled,
    staleTime: 60_000,
    retry: (failureCount, error) => {
      if (error instanceof ApiError && error.status === 401) return false;
      return failureCount < 2;
    },
  });
}
