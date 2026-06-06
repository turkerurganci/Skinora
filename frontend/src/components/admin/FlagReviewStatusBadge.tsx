"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import type { AdminFlagReviewStatus } from "@/lib/api/admin";

const TONE: Record<AdminFlagReviewStatus, string> = {
  PENDING: "bg-amber-50 text-amber-800",
  APPROVED: "bg-emerald-50 text-emerald-800",
  REJECTED: "bg-gray-100 text-gray-800",
};

export interface FlagReviewStatusBadgeProps {
  status: AdminFlagReviewStatus;
  className?: string;
}

/** Review-status pill for fraud flags (ReviewStatus — not a TransactionStatus). */
export function FlagReviewStatusBadge({ status, className }: FlagReviewStatusBadgeProps) {
  const t = useTranslations("adminFlags.reviewStatus");
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
        TONE[status],
        className,
      )}
    >
      {t(status)}
    </span>
  );
}
