"use client";

import { useLocale, useTranslations } from "next-intl";
import { formatDateTime, formatPercent } from "@/lib/utils/format";
import type { AdminUserDetailStats } from "@/lib/api/admin";

function StatTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-md border border-gray-100 bg-gray-50 p-3">
      <dt className="text-xs text-gray-500">{label}</dt>
      <dd className="mt-1 text-lg font-semibold text-gray-900">{value}</dd>
    </div>
  );
}

export interface UserStatsCardProps {
  stats: AdminUserDetailStats;
}

/** 04 §8.9.2 — transaction statistics. */
export function UserStatsCard({ stats }: UserStatsCardProps) {
  const t = useTranslations("adminUserDetail");
  const locale = useLocale();
  const none = t("stats.none");

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-5">
      <h2 className="text-base font-semibold text-gray-900">{t("stats.heading")}</h2>
      <dl className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
        <StatTile label={t("stats.total")} value={String(stats.totalTransactions)} />
        <StatTile label={t("stats.completed")} value={String(stats.completedTransactions)} />
        <StatTile label={t("stats.cancelled")} value={String(stats.cancelledTransactions)} />
        <StatTile label={t("stats.flagged")} value={String(stats.flaggedTransactions)} />
        <StatTile
          label={t("stats.successRate")}
          value={
            stats.successfulTransactionRate === null
              ? none
              : formatPercent(stats.successfulTransactionRate * 100, locale)
          }
        />
        <StatTile
          label={t("stats.volume")}
          value={
            stats.totalVolume === null ? none : t("stats.volumeValue", { value: stats.totalVolume })
          }
        />
        <StatTile
          label={t("stats.lastTransaction")}
          value={
            stats.lastTransactionAt === null
              ? none
              : formatDateTime(stats.lastTransactionAt, locale)
          }
        />
      </dl>
    </section>
  );
}
