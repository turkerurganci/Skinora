"use client";

import { useQuery, keepPreviousData } from "@tanstack/react-query";
import { listNotifications, type NotificationListQuery } from "@/lib/api/notifications";
import { ApiError } from "@/lib/api/client";

/**
 * S11 bildirim list hook (07 §8.1).
 *
 * `keepPreviousData` keeps the current page visible while the next one
 * loads — matches the `useTransactionList` pagination ergonomics so users
 * don't see a skeleton flash on every page click.
 */
export function useNotificationList(query: NotificationListQuery, enabled = true) {
  return useQuery({
    queryKey: ["notifications", "list", query.page ?? 1, query.pageSize ?? 20],
    queryFn: () => listNotifications(query),
    enabled,
    placeholderData: keepPreviousData,
    retry: (failureCount, error) => {
      if (error instanceof ApiError && error.status === 401) return false;
      return failureCount < 2;
    },
  });
}
