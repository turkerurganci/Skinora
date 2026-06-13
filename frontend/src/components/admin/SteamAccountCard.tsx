"use client";

import { useLocale, useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import { formatRelativeTime } from "@/lib/utils/format";
import type { AdminSteamAccount, AdminSteamAccountStatus } from "@/lib/api/admin";

// 04 §8.7 status table prescribes these glyphs verbatim (Aktif ✅, Kısıtlı ⚠,
// Banned ❌). OFFLINE is not in the spec table (06 §2.15 enum extra) — a neutral
// ○ is used. NOTE: this intentionally differs from the S12 dashboard block,
// which uses monochrome ✓/✕ + color classes; S18 follows its own spec table.
const STATUS_ICON: Record<AdminSteamAccountStatus, string> = {
  ACTIVE: "✅",
  RESTRICTED: "⚠",
  BANNED: "❌",
  OFFLINE: "○",
};

interface StatusTone {
  cardClass: string;
  iconClass: string;
  badgeClass: string;
}

/**
 * 04 §8.7 status indicators — four DISTINCT tones. Unlike the compact S12
 * dashboard block (which collapses RESTRICTED + BANNED into a single red
 * variant), S18 wants Aktif = yeşil, Kısıtlı = turuncu (vurgulu), Banned =
 * kırmızı (vurgulu), Offline = gri, so this mapping is intentionally separate
 * from `SteamAccountsStatus.statusTone`.
 */
function statusTone(status: AdminSteamAccountStatus): StatusTone {
  switch (status) {
    case "ACTIVE":
      return {
        cardClass: "border-gray-200",
        iconClass: "text-emerald-600",
        badgeClass: "bg-emerald-50 text-emerald-700",
      };
    case "RESTRICTED":
      return {
        cardClass: "border-amber-300 bg-amber-50/40 ring-1 ring-amber-200",
        iconClass: "text-amber-600",
        badgeClass: "bg-amber-100 text-amber-800",
      };
    case "BANNED":
      return {
        cardClass: "border-red-300 bg-red-50/40 ring-1 ring-red-200",
        iconClass: "text-red-600",
        badgeClass: "bg-red-100 text-red-800",
      };
    case "OFFLINE":
    default:
      return {
        cardClass: "border-gray-200",
        iconClass: "text-gray-500",
        badgeClass: "bg-gray-100 text-gray-700",
      };
  }
}

export interface SteamAccountCardProps {
  account: AdminSteamAccount;
  className?: string;
}

/**
 * One S18 platform-bot card (04 §8.7). Shows Steam ID, status badge, escrowed
 * item count, daily trade-offer usage (x / 200 ToS limit) and last health
 * check (relative). RESTRICTED / BANNED cards are highlighted and carry an
 * inline warning; if the bot holds escrowed items, the recovery/manual-
 * intervention note (02 §15, 03 §11.2a) is shown and the items themselves are
 * listed in the per-bot recovery queue below the grid (T103b-2 — AD25).
 */
export function SteamAccountCard({ account, className }: SteamAccountCardProps) {
  const t = useTranslations("adminSteamAccounts");
  const locale = useLocale();
  const tone = statusTone(account.status);

  const isDegraded = account.status === "RESTRICTED" || account.status === "BANNED";
  const hasEscrow = account.escrowedItemCount > 0;

  return (
    <li
      className={cn(
        "flex flex-col rounded-lg border bg-white p-4 shadow-sm",
        tone.cardClass,
        className,
      )}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          {/* Card label, not a document heading — keeps the page outline clean
              (h1 page title → h2 recovery panel) without an h2-less h3 skip. */}
          <p className="flex items-center gap-1.5 text-sm font-semibold text-gray-900">
            <span aria-hidden="true">🎮</span>
            <span className="truncate">{account.name}</span>
          </p>
          <p className="mt-0.5 truncate font-mono text-xs text-gray-500">
            {t("card.steamId")}: {account.steamId}
          </p>
        </div>
        <span
          className={cn(
            "inline-flex shrink-0 items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium",
            tone.badgeClass,
          )}
        >
          <span aria-hidden="true" className={tone.iconClass}>
            {STATUS_ICON[account.status]}
          </span>
          {t(`status.${account.status}`)}
        </span>
      </div>

      <dl className="mt-3 grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <div>
          <dt className="text-xs text-gray-500">{t("card.escrow")}</dt>
          <dd className="tabular-nums text-gray-900">
            {t("card.escrowValue", { count: account.escrowedItemCount })}
          </dd>
        </div>
        <div>
          <dt className="text-xs text-gray-500">{t("card.dailyTrade")}</dt>
          <dd className="tabular-nums text-gray-900">
            {t("card.dailyTradeValue", {
              count: account.dailyTradeOfferCount,
              limit: account.dailyTradeOfferLimit,
            })}
          </dd>
        </div>
        <div className="col-span-2">
          <dt className="text-xs text-gray-500">{t("card.lastCheck")}</dt>
          <dd className="text-gray-900">
            {account.lastHealthCheck ? (
              <time dateTime={account.lastHealthCheck}>
                {formatRelativeTime(account.lastHealthCheck, locale)}
              </time>
            ) : (
              <span className="text-gray-400">{t("card.neverChecked")}</span>
            )}
          </dd>
        </div>
      </dl>

      {isDegraded && (
        <div
          role="alert"
          className={cn(
            "mt-3 rounded-md border px-3 py-2 text-xs",
            account.status === "BANNED"
              ? "border-red-200 bg-red-50 text-red-800"
              : "border-amber-200 bg-amber-50 text-amber-800",
          )}
        >
          <p className="font-medium">
            {account.restrictionReason ??
              (account.status === "BANNED" ? t("banned.warning") : t("restricted.warning"))}
          </p>
          {hasEscrow && (
            <>
              <p className="mt-1">
                {t("restricted.escrowWarning", { count: account.escrowedItemCount })}
              </p>
              <p className="mt-1 text-[11px] text-gray-500">
                {t("restricted.escrowItemsInQueue")}
              </p>
            </>
          )}
        </div>
      )}
    </li>
  );
}
