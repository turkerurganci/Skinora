"use client";

import type { ReactNode } from "react";
import { useLocale, useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import { formatDateLong, formatPercent } from "@/lib/utils/format";
import type { AdminAccountStatus, AdminUserDetailProfile } from "@/lib/api/admin";

const STATUS_TONE: Record<AdminAccountStatus, string> = {
  ACTIVE: "bg-emerald-100 text-emerald-800 ring-emerald-200",
  SUSPENDED: "bg-amber-100 text-amber-900 ring-amber-300",
  DEACTIVATED: "bg-gray-100 text-gray-700 ring-gray-300",
  DELETED: "bg-red-100 text-red-800 ring-red-200",
};

function Badge({ tone, children }: { tone: string; children: ReactNode }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset whitespace-nowrap",
        tone,
      )}
    >
      {children}
    </span>
  );
}

export interface UserProfileCardProps {
  profile: AdminUserDetailProfile;
}

/**
 * 04 §8.9.1 — profile header: avatar, identity, status badges, reputation.
 * Beyond the base account-status badge, two conditional badges surface: amber
 * "active transaction" (suspended + an active transaction) and red "on hold"
 * (an EMERGENCY_HOLD on one of them). They are independent — suspension never
 * applies a hold automatically.
 */
export function UserProfileCard({ profile }: UserProfileCardProps) {
  const t = useTranslations("adminUserDetail");
  const locale = useLocale();

  const showActiveBadge = profile.isSuspended && profile.activeTransactionCount > 0;
  const showHoldBadge = profile.hasTransactionOnHold;

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-5">
      <div className="flex items-start gap-4">
        {profile.avatarUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={profile.avatarUrl} alt="" className="h-16 w-16 rounded-full bg-gray-100" />
        ) : (
          <span className="h-16 w-16 rounded-full bg-gray-200" aria-hidden="true" />
        )}
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h1 className="truncate text-xl font-semibold text-gray-900">{profile.displayName}</h1>
            <Badge tone={STATUS_TONE[profile.accountStatus]}>
              {t(`profile.status.${profile.accountStatus}`)}
            </Badge>
            {showActiveBadge ? (
              <Badge tone="bg-amber-100 text-amber-900 ring-amber-300">
                {t("profile.badge.activeTransactions")}
              </Badge>
            ) : null}
            {showHoldBadge ? (
              <Badge tone="bg-red-200 text-red-900 ring-red-400">{t("profile.badge.onHold")}</Badge>
            ) : null}
          </div>
          <p className="mt-1 font-mono text-sm text-gray-500">{profile.steamId}</p>

          <dl className="mt-4 grid grid-cols-1 gap-x-6 gap-y-2 sm:grid-cols-3">
            <div>
              <dt className="text-xs text-gray-500">{t("profile.accountAge")}</dt>
              <dd className="text-sm text-gray-900">{profile.accountAge}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{t("profile.joinedAt")}</dt>
              <dd className="text-sm text-gray-900">{formatDateLong(profile.createdAt, locale)}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{t("profile.reputation")}</dt>
              <dd className="text-sm text-gray-900">
                {profile.reputationScore === null
                  ? t("profile.reputationNew")
                  : profile.reputationScore.toFixed(1)}
              </dd>
            </div>
          </dl>

          {/* 04 §8.9.1 / §7.4.2 — reputation breakdown: the figures the score is
              built from. Rates are fractions 0..1 → rendered as percentages. */}
          <dl className="mt-3 grid grid-cols-1 gap-x-6 gap-y-2 border-t border-gray-100 pt-3 sm:grid-cols-3">
            <div>
              <dt className="text-xs text-gray-500">{t("profile.completedCount")}</dt>
              <dd className="text-sm text-gray-900">{profile.completedTransactionCount}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{t("profile.successRate")}</dt>
              <dd className="text-sm text-gray-900">
                {profile.successfulTransactionRate === null
                  ? t("profile.rateNone")
                  : formatPercent(profile.successfulTransactionRate * 100, locale)}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{t("profile.cancelRate")}</dt>
              <dd className="text-sm text-gray-900">
                {profile.cancelRate === null
                  ? t("profile.rateNone")
                  : formatPercent(profile.cancelRate * 100, locale)}
              </dd>
            </div>
          </dl>

          {profile.isSuspended ? (
            <div className="mt-4 rounded-md bg-amber-50 p-3 text-sm text-amber-900 ring-1 ring-amber-200 ring-inset">
              <p className="font-medium">
                {profile.suspensionExpiresAt
                  ? t("profile.suspendedUntil", {
                      date: formatDateLong(profile.suspensionExpiresAt, locale),
                    })
                  : t("profile.suspendedPermanent")}
              </p>
              {profile.suspensionReason ? <p className="mt-1">{profile.suspensionReason}</p> : null}
            </div>
          ) : null}
        </div>
      </div>
    </section>
  );
}
