"use client";

import { useQuery } from "@tanstack/react-query";
import { getPayoutSources, type PlatformPayoutSources } from "@/lib/api/platform";
import type { BlockchainNetwork } from "@/types/enums";

const ONE_HOUR_MS = 60 * 60 * 1000;

/**
 * T167 — the platform's payout sender per network (07 §10.3). Deployment
 * configuration that changes only when a hot wallet is replaced, so one fetch
 * per hour is plenty.
 */
export function usePayoutSources() {
  return useQuery<PlatformPayoutSources>({
    queryKey: ["platform", "payout-sources"],
    queryFn: getPayoutSources,
    staleTime: ONE_HOUR_MS,
    gcTime: ONE_HOUR_MS,
    retry: 0,
  });
}

/** The sender for one network, or null while loading / when unset. */
export function payoutSourceFor(
  data: PlatformPayoutSources | undefined,
  network: BlockchainNetwork,
): string | null {
  return data?.sources.find((s) => s.network === network)?.address ?? null;
}
