"use client";

import { useQuery } from "@tanstack/react-query";
import { getUserStats } from "@/lib/api/users";

/**
 * Dashboard quick-stats hook (S05, 07 §5.2).
 *
 * `enabled` gates the request on the dashboard's auth check — calling the
 * endpoint anonymously would always 401 and inflate the error surface.
 */
export function useUserStats(enabled = true) {
  return useQuery({
    queryKey: ["users", "me", "stats"],
    queryFn: getUserStats,
    enabled,
  });
}
