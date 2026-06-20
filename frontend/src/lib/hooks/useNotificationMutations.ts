"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { markAllNotificationsRead, markNotificationRead } from "@/lib/api/notifications";

/**
 * Shared invalidator for list + unread-count queries. Both mutations affect
 * the same two query roots, so collapsing into one helper avoids drift.
 */
function invalidateNotificationQueries(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: ["notifications", "list"] });
  queryClient.invalidateQueries({ queryKey: ["notifications", "unread-count"] });
}

/**
 * N3 — POST /notifications/mark-all-read (07 §8.3).
 */
export function useMarkAllRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: markAllNotificationsRead,
    onSuccess: () => invalidateNotificationQueries(queryClient),
  });
}

/**
 * N4 — PUT /notifications/:id/read (07 §8.4).
 *
 * Used in fire-and-forget mode by the row click handler (T95 scope decision
 * — optimistic + paralel navigate). 404/403 outcomes are swallowed by the
 * caller because the optimistic UI update is already applied.
 */
export function useMarkRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => markNotificationRead(id),
    onSuccess: () => invalidateNotificationQueries(queryClient),
  });
}
