"use client";

import { useQuery } from "@tanstack/react-query";
import { getAdminDispute } from "@/lib/api/admin";

/**
 * WP5 — admin dispute detail hook (AD28, 07 §9.x). Fetched on demand when the
 * resolve modal opens (`disputeId` non-null).
 */
export function useAdminDisputeDetail(disputeId: string | null) {
  return useQuery({
    queryKey: ["admin", "disputes", "detail", disputeId],
    queryFn: () => getAdminDispute(disputeId as string),
    enabled: disputeId !== null,
  });
}
