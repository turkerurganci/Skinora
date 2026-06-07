"use client";

import { useQuery } from "@tanstack/react-query";
import { listAdminSettings } from "@/lib/api/admin";

/**
 * S17 admin system-settings hook (AD8, 07 §9.8). The whole catalog is returned
 * in one call (58 keys), so there is no pagination — the page groups the flat
 * list client-side via `lib/admin/settingsCatalog`.
 */
export function useAdminSettings() {
  return useQuery({
    queryKey: ["admin", "settings", "list"],
    queryFn: listAdminSettings,
  });
}
