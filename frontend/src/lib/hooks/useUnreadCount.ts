"use client";

import { useQuery } from "@tanstack/react-query";
import { getUnreadCount } from "@/lib/api/notifications";
import { ApiError } from "@/lib/api/client";

/**
 * Header zil badge count hook (07 §8.2).
 *
 * Polling cadence is intentionally absent — T96 will wire SignalR push so
 * the badge updates live. Until then mutations (`useMarkAllRead`,
 * `useMarkRead`) invalidate this query so the badge stays correct within
 * the same session, and `staleTime: 30s` keeps it fresh across navigations.
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
