"use client";

import { useQuery } from "@tanstack/react-query";
import { getAdminSteamAccounts } from "@/lib/api/admin";

/**
 * S18 admin Steam-account monitoring hook (AD10, 07 §9.10). The whole bot fleet
 * is returned in one call (no pagination — the fleet is small and bounded), so
 * the page groups/renders the flat list client-side. A 30s `staleTime` matches
 * the S12 dashboard's bot block: health state shifts on the minute scale, not
 * the second, so this avoids refetch churn while staying reasonably fresh.
 */
export function useAdminSteamAccounts() {
  return useQuery({
    queryKey: ["admin", "steam-accounts", "list"],
    queryFn: getAdminSteamAccounts,
    staleTime: 30_000,
  });
}
