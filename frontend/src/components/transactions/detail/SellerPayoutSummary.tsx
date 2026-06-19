import { useTranslations, useLocale } from "next-intl";
import type { TransactionDetailSellerPayout } from "@/lib/api/transactions";
import { CopyButton } from "@/components/common";
import { formatDateTime, formatStablecoin } from "@/lib/utils/format";
import { maskAddress } from "./helpers";
import { TxHashLink } from "./TxHashLink";

export interface SellerPayoutSummaryProps {
  payout: TransactionDetailSellerPayout;
  stablecoin: string;
}

/**
 * 04 §7.3 — COMPLETED state satıcı görünümü: ödeme özeti.
 * Gas fee kısmen komisyondan, kısmen satıcının alacağından kesilirse 4
 * satırlı detay; tamamen komisyondan karşılandığında 1 satır gizlenir.
 * Backend her zaman tam DTO döner; ayrımı `gasFeeFromSeller === "0"` veya
 * "0.00" / sıfır parse karşılığı ile yapıyoruz.
 */
export function SellerPayoutSummary({ payout, stablecoin }: SellerPayoutSummaryProps) {
  const t = useTranslations("transactionDetail.sellerPayout");
  const locale = useLocale();
  const sellerCovered = parseFloat(payout.gasFeeFromSeller) > 0;
  return (
    <section className="space-y-3 rounded-lg border border-green-300 bg-green-50 p-4">
      <h2 className="text-base font-semibold text-gray-900">{t("title")}</h2>
      <dl className="space-y-1 text-sm">
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("grossAmount")}</dt>
          <dd className="text-gray-900">{formatStablecoin(payout.grossAmount, stablecoin)}</dd>
        </div>
        {sellerCovered && (
          <div className="flex justify-between gap-3">
            <dt className="text-gray-600">{t("gasFeeFromSeller")}</dt>
            <dd className="text-red-700">
              −{formatStablecoin(payout.gasFeeFromSeller, stablecoin)}
            </dd>
          </div>
        )}
        <div className="flex justify-between gap-3 border-t border-green-200 pt-2 text-base">
          <dt className="font-semibold text-gray-700">{t("netAmount")}</dt>
          <dd className="font-bold text-green-700">
            {formatStablecoin(payout.netAmount, stablecoin)}
          </dd>
        </div>
        {sellerCovered && (
          <div className="rounded-md bg-white p-2 text-xs text-gray-700">
            <p className="mb-0.5 font-medium">{t("gasFeeDetail.title")}</p>
            <p>{t("gasFeeDetail.total", { value: payout.gasFee, token: stablecoin })}</p>
            <p>
              {t("gasFeeDetail.fromCommission", {
                value: payout.gasFeeFromCommission,
                token: stablecoin,
              })}
            </p>
            <p>
              {t("gasFeeDetail.fromSeller", {
                value: payout.gasFeeFromSeller,
                token: stablecoin,
              })}
            </p>
          </div>
        )}
        <div className="space-y-1 border-t border-green-200 pt-2 text-sm">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <dt className="text-gray-600">{t("walletAddress")}</dt>
            <dd
              className="flex items-center gap-2 font-mono text-xs text-gray-900"
              title={payout.walletAddress}
            >
              {maskAddress(payout.walletAddress, 8, 6)}
              <CopyButton value={payout.walletAddress} />
            </dd>
          </div>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <dt className="text-gray-600">{t("txHash")}</dt>
            <dd
              className="flex items-center gap-2 font-mono text-xs text-gray-900"
              title={payout.txHash}
            >
              <TxHashLink txHash={payout.txHash} />
            </dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-gray-600">{t("sentAt")}</dt>
            <dd className="text-gray-900">{formatDateTime(payout.sentAt, locale)}</dd>
          </div>
        </div>
      </dl>
    </section>
  );
}
