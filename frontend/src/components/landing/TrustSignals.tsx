"use client";

import { useLocale, useTranslations } from "next-intl";
import { usePlatformStats } from "@/lib/hooks/usePlatformStats";
import { cn } from "@/lib/utils/cn";
import { formatNumber, formatPercent } from "@/lib/utils/format";

export interface TrustSignalsProps {
  className?: string;
}

export function TrustSignals({ className }: TrustSignalsProps) {
  const t = useTranslations("landing.trust");
  const locale = useLocale();
  const { data, isError } = usePlatformStats();

  if (isError) {
    return null;
  }

  const formattedCount =
    data !== undefined ? formatNumber(data.totalCompletedTransactions, locale) : null;
  const formattedUptime =
    data !== undefined ? formatPercent(data.platformUptimePercent, locale) : null;

  return (
    <section
      className={cn("bg-gray-50 px-4 py-16", className)}
      aria-labelledby="trust-signals-title"
    >
      <div className="mx-auto max-w-5xl">
        <h2
          id="trust-signals-title"
          className="text-center text-2xl font-bold tracking-tight text-gray-900 sm:text-3xl"
        >
          {t("title")}
        </h2>
        <div className="mt-10 grid grid-cols-1 gap-6 sm:grid-cols-3">
          <TrustCard
            label={t("totalTransactions")}
            value={formattedCount}
            loading={data === undefined}
          />
          <TrustCard label={t("uptime")} value={formattedUptime} loading={data === undefined} />
          <TrustCard label={t("automation.title")} value={t("automation.body")} loading={false} />
        </div>
      </div>
    </section>
  );
}

interface TrustCardProps {
  label: string;
  value: string | null;
  loading: boolean;
}

function TrustCard({ label, value, loading }: TrustCardProps) {
  return (
    <div className="rounded-lg border border-gray-200 bg-white p-6 text-center shadow-sm">
      <div className="text-sm font-medium uppercase tracking-wide text-gray-500">{label}</div>
      <div className="mt-3 min-h-[2.5rem] text-2xl font-bold text-gray-900">
        {loading ? (
          <span
            aria-hidden="true"
            className="inline-block h-7 w-24 animate-pulse rounded bg-gray-200"
          />
        ) : (
          value
        )}
      </div>
    </div>
  );
}
