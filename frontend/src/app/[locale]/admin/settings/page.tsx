"use client";

import { useTranslations } from "next-intl";
import { ErrorState, Skeleton } from "@/components/common";
import { SettingsManager } from "@/components/admin";
import { useAdminSettings } from "@/lib/hooks/useAdminSettings";

/**
 * S17 — Admin Parametre Yönetimi (04 §8.6). Loads the whole AD8 catalog in one
 * request and hands it to {@link SettingsManager} for client-side grouping +
 * inline editing. No filters / pagination — the catalog is bounded (58 keys).
 */
export default function AdminSettingsPage() {
  const t = useTranslations("adminSettings");
  const { data, isLoading, isError, refetch } = useAdminSettings();

  return (
    <div className="mx-auto w-full max-w-4xl px-4 py-6">
      <h1 className="mb-4 text-2xl font-semibold text-gray-900">{t("title")}</h1>

      {isError ? (
        <ErrorState message={t("loadError")} onRetry={() => refetch()} />
      ) : isLoading ? (
        <div className="flex flex-col gap-2">
          {[0, 1, 2, 3, 4].map((i) => (
            <Skeleton key={i} className="h-16" />
          ))}
        </div>
      ) : !data || data.settings.length === 0 ? (
        <p className="text-sm text-gray-500">{t("empty")}</p>
      ) : (
        <SettingsManager settings={data.settings} />
      )}
    </div>
  );
}
