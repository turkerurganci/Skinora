"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  getAdminSteamAccounts,
  getBotRecoveryQueue,
  updateBotRecoveryItem,
  type UpdateBotRecoveryRequest,
} from "@/lib/api/admin";

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

/**
 * AD25 — recovery queue for one bot (T103b-2). Only fetched for degraded bots
 * (the caller passes `enabled`); a 30s `staleTime` mirrors the account list.
 */
export function useBotRecoveryQueue(botId: string, enabled: boolean) {
  return useQuery({
    queryKey: ["admin", "steam-accounts", "recovery", botId],
    queryFn: () => getBotRecoveryQueue(botId),
    enabled,
    staleTime: 30_000,
  });
}

/**
 * AD26 — apply a recovery-item triage update (MANAGE_STEAM_RECOVERY). Invalidates
 * the bot's recovery queue and the account list so the RecoveryTransactionCount
 * badge / FailoverStatus stay in sync after a status change.
 */
export function useUpdateBotRecovery(botId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateBotRecoveryRequest }) =>
      updateBotRecoveryItem(id, body),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: ["admin", "steam-accounts", "recovery", botId],
      });
      void queryClient.invalidateQueries({ queryKey: ["admin", "steam-accounts", "list"] });
    },
  });
}
