"use client";

import { useQuery } from "@tanstack/react-query";
import { getPlatformMaintenance, type PlatformMaintenance } from "@/lib/api/platform";

const THIRTY_SECONDS_MS = 30 * 1000;

/**
 * Platform bakım/kesinti durumu (07 §10.2). 30 sn TTL.
 * Anlık değişiklikler RT2 (T96) ile push edilir.
 */
export function usePlatformMaintenance() {
  return useQuery<PlatformMaintenance>({
    queryKey: ["platform", "maintenance"],
    queryFn: getPlatformMaintenance,
    staleTime: THIRTY_SECONDS_MS,
    gcTime: THIRTY_SECONDS_MS,
    retry: 0,
  });
}
