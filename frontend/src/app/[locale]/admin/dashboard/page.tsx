"use client";

import { useTranslations } from "next-intl";
import { RecentFlagsTable, SteamAccountsStatus, SummaryCards } from "@/components/admin";
import { useAdminDashboard } from "@/lib/hooks/useAdminDashboard";

/**
 * S12 — Admin Dashboard (04 §8.1). One AD1 fetch fans out into three
 * children; each child manages its own loading / error sub-state so a partial
 * failure on the bot block doesn't blank out the summary counters.
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

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <SteamAccountsStatus
          accounts={data?.steamAccounts}
          isLoading={isLoading}
          isError={isError}
        />
        <RecentFlagsTable flags={data?.recentFlags} isLoading={isLoading} isError={isError} />
      </div>
    </div>
  );
}
