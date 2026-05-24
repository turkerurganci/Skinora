"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { Skeleton } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import { formatNumber } from "@/lib/utils/format";
import type { AdminDashboardSummaryCards } from "@/lib/api/admin";

export interface SummaryCardsProps {
  summary: AdminDashboardSummaryCards | undefined;
  isLoading: boolean;
  isError: boolean;
  className?: string;
}

interface CardProps {
  href: string;
  label: string;
  value: string;
  /** Renders the value pill in red — used for the "pending flags" urgency badge. */
  urgent?: boolean;
  ariaLabel?: string;
}

function Card({ href, label, value, urgent, ariaLabel }: CardProps) {
  return (
    <Link
      href={href}
      aria-label={ariaLabel ?? label}
      className={cn(
        "block rounded-lg border bg-white p-4 shadow-sm transition-colors",
        urgent
          ? "border-red-200 hover:border-red-300 hover:bg-red-50"
          : "border-gray-200 hover:border-blue-300 hover:bg-blue-50",
      )}
    >
      <p className="text-xs font-medium uppercase tracking-wide text-gray-500">{label}</p>
      <p
        className={cn(
          "mt-1 text-2xl font-semibold tabular-nums",
          urgent ? "text-red-700" : "text-gray-900",
        )}
      >
        {value}
      </p>
    </Link>
  );
}

/**
 * S12 summary cards row (04 §8.1). Mobile: 2-col grid; tablet+: 4-col.
 * Each card links to the deeper page per 04 §8.1 click table — target pages
 * are T100 (S13) / T101 (S15) which exist as routed stubs; filter query
 * params ride along so T101 can honor them when implemented.
 */
export function SummaryCards({ summary, isLoading, isError, className }: SummaryCardsProps) {
  const t = useTranslations("adminDashboard.summary");
  const locale = useLocale();

  const wrapper = cn("grid grid-cols-2 gap-3 md:grid-cols-4", className);

  if (isLoading) {
    return (
      <div className={wrapper} aria-busy="true">
        {[0, 1, 2, 3].map((i) => (
          <Skeleton key={i} className="h-20" />
        ))}
      </div>
    );
  }

  if (isError || !summary) {
    return (
      <div className={wrapper}>
        <Card
          href={`/${locale}/admin/transactions?tab=active`}
          label={t("activeTransactions")}
          value="—"
        />
        <Card
          href={`/${locale}/admin/flags?status=PENDING`}
          label={t("pendingFlags")}
          value="—"
          urgent
        />
        <Card
          href={`/${locale}/admin/transactions?range=daily`}
          label={t("dailyCompleted")}
          value="—"
        />
        <Card
          href={`/${locale}/admin/transactions?range=weekly`}
          label={t("weeklyCompleted")}
          value="—"
        />
      </div>
    );
  }

  return (
    <div className={wrapper}>
      <Card
        href={`/${locale}/admin/transactions?tab=active`}
        label={t("activeTransactions")}
        value={formatNumber(summary.activeTransactions, locale)}
      />
      <Card
        href={`/${locale}/admin/flags?status=PENDING`}
        label={t("pendingFlags")}
        value={formatNumber(summary.pendingFlags, locale)}
        urgent
      />
      <Card
        href={`/${locale}/admin/transactions?range=daily`}
        label={t("dailyCompleted")}
        value={formatNumber(summary.dailyCompleted, locale)}
      />
      <Card
        href={`/${locale}/admin/transactions?range=weekly`}
        label={t("weeklyCompleted")}
        value={formatNumber(summary.weeklyCompleted, locale)}
      />
    </div>
  );
}
