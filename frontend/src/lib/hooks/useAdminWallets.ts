"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { getAdminWallets, hotToColdTransfer, type HotToColdTransferRequest } from "@/lib/api/admin";

export const ADMIN_WALLETS_KEY = ["admin", "wallets", "overview"] as const;

/** AD38 — per-network platform wallet overview (T157). */
export function useAdminWallets() {
  return useQuery({
    queryKey: ADMIN_WALLETS_KEY,
    queryFn: getAdminWallets,
  });
}

/**
 * AD20 — hot→cold transfer. Money moved, so the overview is refetched on
 * success to show the new balances and the new row in the cold-transfer list.
 */
export function useHotToColdTransfer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: HotToColdTransferRequest) => hotToColdTransfer(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ADMIN_WALLETS_KEY });
    },
  });
}
