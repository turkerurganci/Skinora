import { useTranslations, useLocale } from "next-intl";
import type { TransactionDetailFlagInfo, TransactionDetailHoldInfo } from "@/lib/api/transactions";

export interface FlagHoldBannerProps {
  flagInfo?: TransactionDetailFlagInfo | null;
  holdInfo?: TransactionDetailHoldInfo | null;
}

/**
 * 04 §7.3 — FLAGGED veya EMERGENCY_HOLD banner. holdInfo precedes flagInfo
 * because EMERGENCY_HOLD is the stronger overlay (06 §2.20).
 *
 * holdInfo.reason is intentionally not surfaced to end users — 04 §7.3 says
 * "hold sebebi gösterilmez (güvenlik)". The DTO carries it for admin views
 * only; here we display holdInfo.message verbatim.
 */
export function FlagHoldBanner({ flagInfo, holdInfo }: FlagHoldBannerProps) {
  const t = useTranslations("transactionDetail.flagHold");
  const locale = useLocale();
  const dateFmt = new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  });
  if (holdInfo) {
    return (
      <div
        className="rounded-md border border-orange-400 bg-orange-100 p-3 text-sm text-orange-900"
        role="alert"
      >
        <p className="font-semibold">{t("hold.title")}</p>
        <p>{holdInfo.message}</p>
        <p className="mt-1 text-xs text-orange-800">
          {t("hold.frozenAt", { value: dateFmt.format(new Date(holdInfo.frozenAt)) })}
        </p>
      </div>
    );
  }
  if (flagInfo) {
    return (
      <div
        className="rounded-md border border-orange-300 bg-orange-50 p-3 text-sm text-orange-900"
        role="status"
      >
        <p className="font-semibold">{t("flag.title")}</p>
        <p>{flagInfo.message}</p>
      </div>
    );
  }
  return null;
}
