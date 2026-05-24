"use client";

import { useLocale, useTranslations } from "next-intl";
import { Skeleton } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import { formatNumber, formatPercent } from "@/lib/utils/format";
import type { UserStats } from "@/lib/api/users";

export interface StatsCardsProps {
  stats: UserStats | undefined;
  isLoading: boolean;
  isError: boolean;
  className?: string;
}

interface CardProps {
  label: string;
  value: string;
  className?: string;
}

function Card({ label, value, className }: CardProps) {
  return (
    <div className={cn("rounded-lg border border-gray-200 bg-white p-4 shadow-sm", className)}>
      <p className="text-xs font-medium uppercase tracking-wide text-gray-500">{label}</p>
      <p className="mt-1 text-2xl font-semibold text-gray-900 tabular-nums">{value}</p>
    </div>
  );
}

export function StatsCards({ stats, isLoading, isError, className }: StatsCardsProps) {
  const t = useTranslations("dashboard.stats");
  const locale = useLocale();

  const wrapper = cn(
    // Desktop (>=lg): vertical stack inside the right rail.
    // Mobile / tablet: 3-up horizontal grid above the list.
    "grid grid-cols-3 gap-3 lg:grid-cols-1",
    className,
  );

  if (isLoading) {
    return (
      <div className={wrapper} aria-busy="true">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-20" />
        ))}
      </div>
    );
  }

  if (isError || !stats) {
    return (
      <div className={wrapper}>
        <Card label={t("completed")} value="—" />
        <Card label={t("successRate")} value="—" />
        <Card label={t("score")} value="—" />
      </div>
    );
  }

  const completedValue = formatNumber(stats.completedTransactionCount, locale);
  const successRateValue = formatPercent(stats.successfulTransactionRate * 100, locale, 0);
  const scoreValue =
    stats.reputationScore === null
      ? "—"
      : formatNumber(stats.reputationScore, locale, {
          minimumFractionDigits: 1,
          maximumFractionDigits: 1,
        });

  return (
    <div className={wrapper}>
      <Card label={t("completed")} value={completedValue} />
      <Card label={t("successRate")} value={successRateValue} />
      <Card label={t("score")} value={scoreValue} />
    </div>
  );
}
