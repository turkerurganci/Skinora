"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import { DisputeStatus } from "@/types/enums";

const STATUS_CLASS: Record<DisputeStatus, string> = {
  [DisputeStatus.OPEN]: "bg-amber-100 text-amber-800",
  [DisputeStatus.ESCALATED]: "bg-red-100 text-red-800",
  [DisputeStatus.CLOSED]: "bg-gray-100 text-gray-700",
  [DisputeStatus.RESOLVED_FOR_SELLER]: "bg-emerald-100 text-emerald-800",
  [DisputeStatus.RESOLVED_FOR_BUYER]: "bg-blue-100 text-blue-800",
};

export interface DisputeStatusBadgeProps {
  status: DisputeStatus;
  className?: string;
}

/** WP5 — dispute status pill (queue + detail). Localized via adminDisputes.status. */
export function DisputeStatusBadge({ status, className }: DisputeStatusBadgeProps) {
  const t = useTranslations("adminDisputes.status");
  return (
    <span
      className={cn(
        "inline-flex rounded-full px-2 py-0.5 text-xs font-medium",
        STATUS_CLASS[status],
        className,
      )}
    >
      {t(status)}
    </span>
  );
}
