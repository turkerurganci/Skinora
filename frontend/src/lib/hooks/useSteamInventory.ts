"use client";

import { useQuery } from "@tanstack/react-query";
import { getSteamInventory } from "@/lib/api/steam";

/**
 * S06 Steam inventory hook (07 §6.1).
 *
 * Backend returns the full inventory in one response (no pagination) and
 * caches per-user for 2 minutes (T67); we mirror that with `staleTime: 2 min`
 * so step navigation inside the same form doesn't re-fetch. `5/dk` rate limit
 * makes accidental refetches expensive — the hook is intentionally read-once.
 */
export function useSteamInventory(enabled = true) {
  return useQuery({
    queryKey: ["steam", "inventory"],
    queryFn: getSteamInventory,
    enabled,
    staleTime: 2 * 60 * 1000,
    retry: false,
  });
}
