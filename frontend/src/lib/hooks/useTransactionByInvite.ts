"use client";

import { useQuery } from "@tanstack/react-query";
import { getTransactionByInvite } from "@/lib/api/transactions";

/**
 * OPEN_LINK invite consume hook (07 §7.5a, F-INVITE-01).
 *
 * Resolves `/invite/:token` to the public-invite surface. Keyed separately
 * from {@link useTransactionDetail} because the token — not the id — is the
 * access grant; an authenticated token holder is surfaced as a prospective
 * buyer by the backend. `staleTime` mirrors the detail hook (5s) so a
 * returning visitor sees a fresh joinable/accepted state.
 */
export function useTransactionByInvite(token: string | undefined) {
  return useQuery({
    queryKey: ["transactions", "invite", token],
    queryFn: () => getTransactionByInvite(token!),
    enabled: Boolean(token),
    staleTime: 5_000,
  });
}
