import { apiClient } from "./client";
import type { PagedResult } from "@/types/api";
import { NotificationType } from "@/types/enums";

/**
 * Single bildirim row returned by N1 — GET /notifications (07 §8.1).
 * `targetType` is `"transaction"` | `"flag"` | `null` per the spec; the
 * frontend route mapping happens in the row component, not the API layer.
 */
export interface NotificationListItem {
  id: string;
  type: NotificationType;
  message: string;
  targetType: "transaction" | "flag" | null;
  targetId: string | null;
  isRead: boolean;
  createdAt: string;
}

export interface NotificationListQuery {
  page?: number;
  pageSize?: number;
}

export function listNotifications(
  query: NotificationListQuery = {},
): Promise<PagedResult<NotificationListItem>> {
  const params = new URLSearchParams();
  if (query.page !== undefined) params.set("page", String(query.page));
  if (query.pageSize !== undefined) params.set("pageSize", String(query.pageSize));
  const qs = params.toString();
  return apiClient<PagedResult<NotificationListItem>>(
    qs ? `/notifications?${qs}` : "/notifications",
  );
}

// ---------- N2 — GET /notifications/unread-count (07 §8.2) ----------

export interface UnreadCountResponse {
  unreadCount: number;
}

export function getUnreadCount(): Promise<UnreadCountResponse> {
  return apiClient<UnreadCountResponse>("/notifications/unread-count");
}

// ---------- N3 — POST /notifications/mark-all-read (07 §8.3) ----------

export interface MarkAllReadResponse {
  markedCount: number;
}

export function markAllNotificationsRead(): Promise<MarkAllReadResponse> {
  return apiClient<MarkAllReadResponse>("/notifications/mark-all-read", {
    method: "POST",
  });
}

// ---------- N4 — PUT /notifications/:id/read (07 §8.4) ----------

export function markNotificationRead(id: string): Promise<null> {
  return apiClient<null>(`/notifications/${encodeURIComponent(id)}/read`, {
    method: "PUT",
  });
}
