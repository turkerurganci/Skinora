"use client";

import { useQuery } from "@tanstack/react-query";
import { getUnreadCount } from "@/lib/api/notifications";
import { ApiError } from "@/lib/api/client";

/**
 * Header zil badge count hook (07 §8.2).
 *
 * Polling cadence is intentionally absent — `RealtimeProvider` (T96 —
 * 07 §11.2) writes the cache directly on every `UnreadCountChanged` push
 * and invalidates it on `NewNotification`. Mutations (`useMarkAllRead`,
 * `useMarkRead`) keep the badge correct as a defensive fallback when the
 * SignalR channel is down, and `staleTime: 30s` smooths first-paint after
 * navigations.
 */
export function useUnreadCount(enabled: boolean) {
  return useQuery({
    queryKey: ["notifications", "unread-count"],
    queryFn: getUnreadCount,
    enabled,
    staleTime: 30_000,
    retry: (failureCount, error) => {
      if (error instanceof ApiError && error.status === 401) return false;
      return failureCount < 2;
    },
  });
}
