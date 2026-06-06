"use client";

import { useQuery } from "@tanstack/react-query";
import { getAdminFlag } from "@/lib/api/admin";

/** S14 flag-detail hook (AD3, 07 §9.3). */
export function useAdminFlagDetail(id: string, enabled = true) {
  return useQuery({
    queryKey: ["admin", "flags", "detail", id],
    queryFn: () => getAdminFlag(id),
    enabled: enabled && id.length > 0,
  });
}
