"use client";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import type { AdminSteamAccountsResponse } from "@/lib/api/admin";
import { SteamAccountCard } from "./SteamAccountCard";
import { BotRecoveryQueue } from "./BotRecoveryQueue";

export interface SteamAccountsViewProps {
  data: AdminSteamAccountsResponse;
  className?: string;
}

/**
 * S18 — Platform Steam Hesapları (04 §8.7). Composes the warning banner +
 * account-card grid + a recovery-queue panel per restricted/banned bot from a
 * single AD10 response (the per-bot queues fetch AD25 lazily).
 *
 * The banner is DERIVED CLIENT-SIDE from the degraded accounts rather than
 * rendering AD10's `warningMessage`: that server field is Turkish-only
 * (`AdminSteamBotQueryService.BuildWarning`), so showing it verbatim would leak
 * Turkish onto the en/es/zh locales this admin page supports. This mirrors the
 * S12 dashboard, which also derives its banner client-side (T99 K6).
 *
 * The "yeni işlemler diğer hesaplara yönlendirildi" line is shown only when the
 * failover pipeline reports diversion (RESTRICTED_NEW_TXN_DIVERTED), which is now
 * populated live by the T103b-2 recovery domain.
 */
export function SteamAccountsView({ data, className }: SteamAccountsViewProps) {
  const t = useTranslations("adminSteamAccounts");
  const { accounts } = data;

  const degraded = accounts.filter((a) => a.status === "RESTRICTED" || a.status === "BANNED");
  const isDiverted = accounts.some((a) => a.failoverStatus === "RESTRICTED_NEW_TXN_DIVERTED");

  return (
    <div className={cn("flex flex-col gap-6", className)}>
      {degraded.length > 0 && (
        <div
          role="alert"
          className="rounded-md border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-800"
        >
          <p className="font-medium">{t("banner.warning", { count: degraded.length })}</p>
          {isDiverted && <p className="mt-1">{t("banner.diverted")}</p>}
        </div>
      )}

      <ul role="list" className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {accounts.map((account) => (
          <SteamAccountCard key={account.id} account={account} />
        ))}
      </ul>

      {degraded.map((account) => (
        <BotRecoveryQueue key={account.id} botId={account.id} botName={account.name} />
      ))}
    </div>
  );
}
