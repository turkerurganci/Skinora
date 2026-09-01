"use client";

import { useLocale, useTranslations } from "next-intl";
import Link from "next/link";
import { CountdownTimer } from "@/components/common";
import type { EligibilityResponse } from "@/lib/api/transactions";

/**
 * Reason codes emitted by `GET /transactions/eligibility` mapped from the
 * backend `TransactionErrorCodes.EligibilityReasons` static class. Kept inline
 * (rather than imported from a shared barrel) to make the mapping table
 * legible from the gate UI in isolation.
 */
const REASON = {
  MA: "MOBILE_AUTHENTICATOR_REQUIRED",
  FLAGGED: "ACCOUNT_FLAGGED",
  CANCEL_COOLDOWN: "CANCEL_COOLDOWN_ACTIVE",
  CONCURRENT: "CONCURRENT_LIMIT_REACHED",
  NEW_ACCOUNT: "NEW_ACCOUNT_LIMIT_REACHED",
  PAYOUT_COOLDOWN: "PAYOUT_ADDRESS_COOLDOWN_ACTIVE",
  WALLET_MISSING: "SELLER_WALLET_ADDRESS_MISSING",
} as const;

export interface EligibilityGateProps {
  eligibility: EligibilityResponse;
}

/**
 * Computes the effective gating reasons — every reason blocks.
 *
 * `SELLER_WALLET_ADDRESS_MISSING` used to be filtered out here, on the premise
 * that 04 §7.2 step 3 let the seller type the address inline. That premise was
 * never true in practice: the wizard put the typed address in the request body,
 * but the backend gate read the PROFILE, so the seller filled four steps and hit
 * a 422 dead end (`Prova-InlineSellerWalletUnreachable`). The payout address is
 * now read from the profile only, so the inline field is gone and this reason is
 * a real gate — surfaced up front with a link to the profile page.
 */
export function getBlockingReasons(eligibility: EligibilityResponse): string[] {
  if (eligibility.eligible) return [];
  return eligibility.reasons ?? [];
}

export function EligibilityGate({ eligibility }: EligibilityGateProps) {
  const t = useTranslations("newTransaction.gate");
  const locale = useLocale();
  const reasons = getBlockingReasons(eligibility);
  if (reasons.length === 0) return null;

  return (
    <div className="space-y-3" role="region" aria-label={t("regionLabel")}>
      {reasons.map((reason) => (
        <ReasonBanner
          key={reason}
          reason={reason}
          eligibility={eligibility}
          locale={locale}
          t={t}
        />
      ))}
    </div>
  );
}

interface ReasonBannerProps {
  reason: string;
  eligibility: EligibilityResponse;
  locale: string;
  t: ReturnType<typeof useTranslations<"newTransaction.gate">>;
}

function ReasonBanner({ reason, eligibility, locale, t }: ReasonBannerProps) {
  switch (reason) {
    case REASON.MA:
      return (
        <Banner tone="red" title={t("ma.title")} description={t("ma.description")}>
          <Link
            href={`/${locale}/auth/mobile-authenticator`}
            className="inline-flex items-center justify-center rounded-md bg-red-600 px-3 py-2 text-sm font-semibold text-white hover:bg-red-700"
          >
            {t("ma.cta")}
          </Link>
        </Banner>
      );
    case REASON.FLAGGED:
      return (
        <Banner tone="orange" title={t("flagged.title")} description={t("flagged.description")} />
      );
    case REASON.CONCURRENT: {
      const { current, max } = eligibility.concurrentLimit;
      return (
        <Banner
          tone="amber"
          title={t("concurrent.title")}
          description={t("concurrent.description", { current, max })}
        />
      );
    }
    case REASON.NEW_ACCOUNT: {
      const current = eligibility.newAccountLimit.current ?? 0;
      const max = eligibility.newAccountLimit.max ?? 0;
      return (
        <Banner
          tone="amber"
          title={t("newAccount.title")}
          description={t("newAccount.description", { current, max })}
        />
      );
    }
    case REASON.CANCEL_COOLDOWN: {
      const expiresAt = eligibility.cancelCooldown.expiresAt;
      return (
        <Banner tone="amber" title={t("cancelCooldown.title")}>
          <p className="text-sm text-amber-900">{t("cancelCooldown.description")}</p>
          {expiresAt && (
            <div className="mt-2">
              <CountdownTimer deadline={expiresAt} warningThresholdSeconds={300} format="verbose" />
            </div>
          )}
        </Banner>
      );
    }
    case REASON.PAYOUT_COOLDOWN:
      return (
        <Banner
          tone="amber"
          title={t("payoutCooldown.title")}
          description={t("payoutCooldown.description")}
        />
      );
    case REASON.WALLET_MISSING:
      return (
        <Banner
          tone="red"
          title={t("walletMissing.title")}
          description={t("walletMissing.description")}
        >
          <Link
            href={`/${locale}/profile`}
            className="inline-flex items-center justify-center rounded-md bg-red-600 px-3 py-2 text-sm font-semibold text-white hover:bg-red-700"
          >
            {t("walletMissing.cta")}
          </Link>
        </Banner>
      );
    default:
      return <Banner tone="red" title={t("unknown.title")} description={reason} />;
  }
}

type BannerTone = "red" | "orange" | "amber";

const TONE_STYLES: Record<BannerTone, string> = {
  red: "border-red-200 bg-red-50 text-red-900",
  orange: "border-orange-200 bg-orange-50 text-orange-900",
  amber: "border-amber-200 bg-amber-50 text-amber-900",
};

interface BannerProps {
  tone: BannerTone;
  title: string;
  description?: string;
  children?: React.ReactNode;
}

function Banner({ tone, title, description, children }: BannerProps) {
  return (
    <div role="alert" className={`rounded-lg border p-4 ${TONE_STYLES[tone]}`}>
      <h3 className="text-sm font-semibold">{title}</h3>
      {description && <p className="mt-1 text-sm">{description}</p>}
      {children && <div className="mt-3">{children}</div>}
    </div>
  );
}
