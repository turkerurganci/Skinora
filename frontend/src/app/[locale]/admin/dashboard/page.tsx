"use client";

import { useTranslations } from "next-intl";
import { RecentFlagsTable, SummaryCards } from "@/components/admin";
import { useAdminDashboard } from "@/lib/hooks/useAdminDashboard";

/**
 * S12 — Admin Dashboard (04 §8.1). One AD1 fetch fans out into two children;
 * each child manages its own loading / error sub-state so a partial failure on
 * the flag table doesn't blank out the summary counters.
 *
 * The Steam bot health block was removed in T136: AD1 stopped emitting
 * `steamAccounts` in T132 and the platform runs no bot accounts at all under
 * P2P (04 §8.7).
 */
export default function AdminDashboardPage() {
  const t = useTranslations("adminDashboard");
  const query = useAdminDashboard();

  const isLoading = query.isLoading;
  const isError = query.isError;
  const data = query.data;

  return (
    <div className="mx-auto w-full max-w-6xl px-4 py-6">
      <h1 className="mb-4 text-2xl font-semibold text-gray-900">{t("title")}</h1>

      <SummaryCards
        summary={data?.summaryCards}
        isLoading={isLoading}
        isError={isError}
        className="mb-6"
      />

      <RecentFlagsTable flags={data?.recentFlags} isLoading={isLoading} isError={isError} />
    </div>
  );
}
