"use client";

import { useTranslations } from "next-intl";
import { ErrorState, Skeleton } from "@/components/common";
import { SteamAccountsView } from "@/components/admin";
import { useAdminSteamAccounts } from "@/lib/hooks/useAdminSteamAccounts";

/**
 * S18 — Platform Steam Hesapları (04 §8.7). Loads the AD10 bot fleet in one
 * request and hands it to {@link SteamAccountsView}. Account cards + status
 * states + warning banner are fully data-backed by AD10; the recovery queue
 * renders structurally but stays empty (T103 Option A — the recovery fields are
 * deferred to the T69 bot-health/failover pipeline). No filters/pagination —
 * the bot fleet is small and bounded.
 */
export default function AdminSteamAccountsPage() {
  const t = useTranslations("adminSteamAccounts");
  const { data, isLoading, isError, refetch } = useAdminSteamAccounts();

  return (
    <div className="mx-auto w-full max-w-5xl px-4 py-6">
      <h1 className="mb-4 text-2xl font-semibold text-gray-900">{t("title")}</h1>

      {isError ? (
        <ErrorState message={t("loadError")} onRetry={() => refetch()} />
      ) : isLoading ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-40" />
          ))}
        </div>
      ) : !data || data.accounts.length === 0 ? (
        <p className="text-sm text-gray-500">{t("empty")}</p>
      ) : (
        <SteamAccountsView data={data} />
      )}
    </div>
  );
}
