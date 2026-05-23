"use client";

import { useTranslations } from "next-intl";
import { useMarkAllRead } from "@/lib/hooks/useNotificationMutations";

export interface MarkAllReadButtonProps {
  /**
   * When `false` the link is rendered disabled — there is nothing to mark
   * (either the list is empty or all rows are already read).
   */
  enabled: boolean;
}

export function MarkAllReadButton({ enabled }: MarkAllReadButtonProps) {
  const t = useTranslations("notificationsInbox");
  const markAll = useMarkAllRead();

  const disabled = !enabled || markAll.isPending;

  return (
    <button
      type="button"
      onClick={() => markAll.mutate()}
      disabled={disabled}
      className="text-sm font-medium text-blue-600 hover:text-blue-700 disabled:cursor-not-allowed disabled:text-gray-400"
    >
      {markAll.isPending ? t("markAllRead.pending") : t("markAllRead.label")}
    </button>
  );
}
