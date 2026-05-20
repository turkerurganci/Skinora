"use client";

import { useQuery } from "@tanstack/react-query";
import { getPlatformStats, type PlatformStats } from "@/lib/api/platform";

const FIFTEEN_MINUTES_MS = 15 * 60 * 1000;

/**
 * Landing page trust signals (07 §10.1). 15 dk TTL — server cache aligned.
 */
export function usePlatformStats() {
  return useQuery<PlatformStats>({
    queryKey: ["platform", "stats"],
    queryFn: getPlatformStats,
    staleTime: FIFTEEN_MINUTES_MS,
    gcTime: FIFTEEN_MINUTES_MS,
    retry: 0,
  });
}
