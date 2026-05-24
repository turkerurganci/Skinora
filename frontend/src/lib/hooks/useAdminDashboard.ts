"use client";

import { useQuery } from "@tanstack/react-query";
import { getAdminDashboard } from "@/lib/api/admin";

/**
 * AD1 dashboard fetcher (07 §9.1, S12).
 *
 * `staleTime` 30s — counters are platform-wide aggregates that don't need
 * sub-second freshness, and the admin tab usually stays open. Refetch on
 * window focus is the default React Query behavior; that handles the
 * "I tabbed away and came back" case without a hard polling loop.
 */
export function useAdminDashboard(enabled = true) {
  return useQuery({
    queryKey: ["admin", "dashboard"],
    queryFn: getAdminDashboard,
    enabled,
    staleTime: 30_000,
  });
}
