"use client";

import { useLocale, useTranslations } from "next-intl";
import { formatNumber } from "@/lib/utils/format";
import { formatPercent, formatScore } from "./helpers";

export interface ReputationCardProps {
  variant: "own" | "public";
  reputationScore: number | null;
  completedTransactionCount: number;
  successfulTransactionRate: number | null;
  /** Only surfaced on the own profile — 04 §7.5 hides it on S09. */
  cancelRate?: number | null;
}

/**
 * 04 §7.4 (own — score, completed count, success rate, cancel rate) +
 * §7.5 (public — score, completed count, success rate; cancel rate
 * hidden by spec).
 */
export function ReputationCard({
  variant,
  reputationScore,
  completedTransactionCount,
  successfulTransactionRate,
  cancelRate,
}: ReputationCardProps) {
  const t = useTranslations("profile.reputation");
  const locale = useLocale();

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="mb-4 text-lg font-semibold text-gray-900">{t("title")}</h2>
      <dl className="grid grid-cols-2 gap-4 sm:grid-cols-4">
        <ReputationStat label={t("score")} value={formatScore(reputationScore, locale)} />
        <ReputationStat
          label={t("completedCount")}
          value={formatNumber(completedTransactionCount, locale)}
        />
        <ReputationStat
          label={t("successRate")}
          value={formatPercent(successfulTransactionRate, locale)}
        />
        {variant === "own" && (
          <ReputationStat
            label={t("cancelRate")}
            value={formatPercent(cancelRate ?? null, locale)}
          />
        )}
      </dl>
    </section>
  );
}

function ReputationStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col gap-1">
      <dt className="text-xs uppercase tracking-wide text-gray-500">{label}</dt>
      <dd className="text-xl font-semibold text-gray-900">{value}</dd>
    </div>
  );
}
