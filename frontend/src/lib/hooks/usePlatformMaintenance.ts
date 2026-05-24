"use client";

import { useQuery } from "@tanstack/react-query";
import { getPlatformMaintenance, type PlatformMaintenance } from "@/lib/api/platform";

const THIRTY_SECONDS_MS = 30 * 1000;

/**
 * Platform bakım/kesinti durumu (07 §10.2). 30 sn TTL.
 *
 * `RealtimeProvider` (T96 — 07 §11.2) invalidates this cache key on every
 * `MaintenanceStatusChanged` push, so the C08 banner reflects the new
 * state on the next render without waiting for the 30 sn refetch.
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
