"use client";

import type { NotificationListItem } from "@/lib/api/notifications";
import { NotificationRow } from "./NotificationRow";

export interface NotificationListProps {
  items: readonly NotificationListItem[];
}

export function NotificationList({ items }: NotificationListProps) {
  return (
    <ul
      role="list"
      className="divide-y divide-gray-100 rounded-md border border-gray-200 bg-white"
    >
      {items.map((notification) => (
        <li key={notification.id}>
          <NotificationRow notification={notification} />
        </li>
      ))}
    </ul>
  );
}
