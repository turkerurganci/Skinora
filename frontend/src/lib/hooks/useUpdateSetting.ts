"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { updateAdminSetting } from "@/lib/api/admin";

/**
 * AD9 — update a single setting ("Kaydet", 04 §8.6). On success the whole
 * `["admin","settings"]` cache is invalidated so the row reflects the persisted
 * value (and `updatedAt`) the backend returned.
 */
export function useUpdateSetting() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ key, value }: { key: string; value: string }) => updateAdminSetting(key, value),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["admin", "settings"] });
    },
  });
}
