"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import type { AdminAuditCategory } from "@/lib/api/admin";

/**
 * 06 §2.19 — three audit categories. Tones follow the dashboard convention:
 * fund = blue (money), admin = slate (operator action), security = amber
 * (alert — sits alongside the wallet-address / reconciliation events).
 */
const CATEGORY_TONE: Record<AdminAuditCategory, string> = {
  FUND_MOVEMENT: "bg-blue-100 text-blue-800 ring-blue-200",
  ADMIN_ACTION: "bg-slate-100 text-slate-800 ring-slate-200",
  SECURITY_EVENT: "bg-amber-100 text-amber-800 ring-amber-200",
};

export interface AuditCategoryBadgeProps {
  category: AdminAuditCategory;
  className?: string;
}

export function AuditCategoryBadge({ category, className }: AuditCategoryBadgeProps) {
  const t = useTranslations("adminAuditLog.category");
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset whitespace-nowrap",
        CATEGORY_TONE[category],
        className,
      )}
    >
      {t(category)}
    </span>
  );
}
