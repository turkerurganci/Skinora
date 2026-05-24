"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { Skeleton } from "@/components/common";
import { cn } from "@/lib/utils/cn";
import type { AdminSteamAccount, AdminSteamAccountStatus } from "@/lib/api/admin";

export interface SteamAccountsStatusProps {
  accounts: readonly AdminSteamAccount[] | undefined;
  isLoading: boolean;
  isError: boolean;
  className?: string;
}

const STATUS_ICON: Record<AdminSteamAccountStatus, string> = {
  ACTIVE: "✓",
  RESTRICTED: "⚠",
  BANNED: "✕",
  OFFLINE: "○",
};

function statusTone(status: AdminSteamAccountStatus): {
  iconClass: string;
  borderClass: string;
  badgeClass: string;
} {
  if (status === "ACTIVE") {
    return {
      iconClass: "text-emerald-600",
      borderClass: "border-gray-200",
      badgeClass: "bg-emerald-50 text-emerald-700",
    };
  }
  if (status === "OFFLINE") {
    return {
      iconClass: "text-gray-500",
      borderClass: "border-gray-200",
      badgeClass: "bg-gray-100 text-gray-700",
    };
  }
  // RESTRICTED + BANNED: highlight per 04 §8.1 "Kısıtlı/banned hesap varsa
  // kart vurgulu (kırmızı border)".
  return {
    iconClass: "text-red-600",
    borderClass: "border-red-300",
    badgeClass: "bg-red-50 text-red-700",
  };
}

/**
 * Renders the S12 Steam bot status block (04 §8.1). The whole block links to
 * S18 (admin steam-accounts page, T103 forward) — both via the header CTA and
 * per-card click target. The "restricted/banned warning" requirement is
 * satisfied by the red-border card variant on any non-ACTIVE / non-OFFLINE
 * row; the header banner kicks in whenever there's at least one such row.
 */
export function SteamAccountsStatus({
  accounts,
  isLoading,
  isError,
  className,
}: SteamAccountsStatusProps) {
  const t = useTranslations("adminDashboard.steamAccounts");
  const locale = useLocale();
  const targetHref = `/${locale}/admin/steam-accounts`;

  const wrapper = cn("rounded-lg border border-gray-200 bg-white p-4 shadow-sm", className);

  const header = (
    <div className="mb-3 flex items-center justify-between">
      <h2 className="text-sm font-semibold text-gray-900">{t("title")}</h2>
      <Link href={targetHref} className="text-xs font-medium text-blue-600 hover:text-blue-700">
        {t("manage")}
      </Link>
    </div>
  );

  if (isLoading) {
    return (
      <section className={wrapper} aria-busy="true" aria-label={t("title")}>
        {header}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {[0, 1, 2].map((i) => (
            <Skeleton key={i} className="h-20" />
          ))}
        </div>
      </section>
    );
  }

  if (isError || !accounts) {
    return (
      <section className={wrapper} aria-label={t("title")}>
        {header}
        <p className="text-sm text-gray-500">{t("loadError")}</p>
      </section>
    );
  }

  if (accounts.length === 0) {
    return (
      <section className={wrapper} aria-label={t("title")}>
        {header}
        <p className="text-sm text-gray-500">{t("empty")}</p>
      </section>
    );
  }

  const degraded = accounts.filter((a) => a.status === "RESTRICTED" || a.status === "BANNED");

  return (
    <section className={wrapper} aria-label={t("title")}>
      {header}

      {degraded.length > 0 && (
        <div
          role="alert"
          className="mb-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-800"
        >
          {t("warning", { count: degraded.length })}
        </div>
      )}

      <ul role="list" className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
        {accounts.map((account) => {
          const tone = statusTone(account.status);
          return (
            <li key={account.id}>
              <Link
                href={targetHref}
                aria-label={t("cardAriaLabel", { name: account.name, status: account.status })}
                className={cn(
                  "block rounded-md border bg-white px-3 py-2 shadow-sm transition-colors",
                  tone.borderClass,
                  "hover:bg-gray-50",
                )}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="truncate text-sm font-medium text-gray-900">{account.name}</span>
                  <span aria-hidden="true" className={cn("text-lg leading-none", tone.iconClass)}>
                    {STATUS_ICON[account.status]}
                  </span>
                </div>
                <span
                  className={cn(
                    "mt-1 inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium",
                    tone.badgeClass,
                  )}
                >
                  {t(`status.${account.status}`)}
                </span>
              </Link>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
