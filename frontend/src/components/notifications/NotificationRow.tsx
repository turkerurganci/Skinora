"use client";

import { useRouter } from "next/navigation";
import { useFormatter, useLocale, useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import { iconForType } from "@/lib/utils/notification-icons";
import { useMarkRead } from "@/lib/hooks/useNotificationMutations";
import type { NotificationListItem } from "@/lib/api/notifications";

export interface NotificationRowProps {
  notification: NotificationListItem;
}

/**
 * Maps `targetType` + `targetId` to the in-app route segment for S11 → S07
 * navigation (04 §7.7). `null` targets render as non-clickable info rows.
 */
function targetHref(locale: string, notification: NotificationListItem): string | null {
  if (!notification.targetId || !notification.targetType) return null;
  switch (notification.targetType) {
    case "transaction":
      return `/${locale}/transactions/${notification.targetId}`;
    case "flag":
      return `/${locale}/admin/flags/${notification.targetId}`;
    default:
      return null;
  }
}

export function NotificationRow({ notification }: NotificationRowProps) {
  const t = useTranslations("notificationsInbox");
  const router = useRouter();
  const locale = useLocale();
  const format = useFormatter();
  const markRead = useMarkRead();

  const href = targetHref(locale, notification);
  const interactive = Boolean(href);
  const icon = iconForType(notification.type);
  const createdAt = new Date(notification.createdAt);

  function handleClick() {
    // Optimistic: fire mark-read, navigate in parallel. Endpoint is
    // idempotent and the React Query invalidation reconciles the badge
    // when the mutation settles.
    if (!notification.isRead) {
      markRead.mutate(notification.id);
    }
    if (href) {
      router.push(href);
    }
  }

  function handleKeyDown(event: React.KeyboardEvent<HTMLDivElement>) {
    if (!interactive) return;
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      handleClick();
    }
  }

  return (
    <div
      role={interactive ? "button" : undefined}
      tabIndex={interactive ? 0 : undefined}
      onClick={interactive ? handleClick : undefined}
      onKeyDown={interactive ? handleKeyDown : undefined}
      className={cn(
        "flex items-start gap-3 border-b border-gray-100 px-4 py-3 last:border-b-0",
        notification.isRead ? "bg-white" : "bg-blue-50/40",
        interactive &&
          "cursor-pointer hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-blue-500",
      )}
      aria-label={
        notification.isRead
          ? notification.message
          : `${t("row.unreadAriaPrefix")} — ${notification.message}`
      }
    >
      <div className="relative flex-shrink-0 pt-0.5" aria-hidden="true">
        <span className="text-xl leading-none">{icon}</span>
        {!notification.isRead && (
          <span
            className="absolute -left-2 top-1 inline-block h-2 w-2 rounded-full bg-blue-500"
            aria-hidden="true"
          />
        )}
      </div>

      <div className="min-w-0 flex-1">
        <p
          className={cn(
            "text-sm",
            notification.isRead ? "text-gray-700" : "font-medium text-gray-900",
          )}
        >
          {notification.message}
        </p>
        <p className="mt-1 text-xs text-gray-500">
          <time dateTime={notification.createdAt}>
            {format.relativeTime(createdAt, new Date())}
          </time>
        </p>
      </div>

      {!notification.isRead && <span className="sr-only">{t("row.unreadAriaPrefix")}</span>}
    </div>
  );
}
