"use client";

import { useQuery } from "@tanstack/react-query";
import { getAdminUserDetail } from "@/lib/api/admin";

/**
 * S20 admin user-detail hook (AD16, 07 §9.16). 30s staleTime — the detail is
 * read-mostly and reached via deep-links from transactions / flags / audit log.
 */
export function useAdminUserDetail(steamId: string) {
  return useQuery({
    queryKey: ["admin", "users", "detail", steamId],
    queryFn: () => getAdminUserDetail(steamId),
    enabled: steamId.length > 0,
    staleTime: 30_000,
  });
}
