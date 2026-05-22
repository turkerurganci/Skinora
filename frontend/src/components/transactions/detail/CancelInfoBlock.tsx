import { useTranslations, useLocale } from "next-intl";
import type { TransactionDetailCancelInfo, TransactionDetailRefund } from "@/lib/api/transactions";
import { CopyButton } from "@/components/common";
import { maskAddress } from "./helpers";

export interface CancelInfoBlockProps {
  cancelInfo: TransactionDetailCancelInfo;
  refund?: TransactionDetailRefund | null;
  stablecoin: string;
}

/**
 * 04 §7.3 — CANCELLED_* state'lerinde iptal bilgisi ve (varsa) iade
 * özeti. cancelledBy enum: TIMEOUT / SELLER / BUYER / ADMIN — her birinin
 * kendi başlığı.
 */
export function CancelInfoBlock({ cancelInfo, refund, stablecoin }: CancelInfoBlockProps) {
  const t = useTranslations("transactionDetail.cancelInfo");
  const locale = useLocale();
  const dateFmt = new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  });
  return (
    <section className="space-y-3 rounded-lg border border-red-300 bg-red-50 p-4">
      <h2 className="text-base font-semibold text-red-900">
        {t(`cancelledBy.${cancelInfo.cancelledBy}`)}
      </h2>
      <dl className="space-y-1 text-sm">
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("cancelledAt")}</dt>
          <dd className="text-gray-900">{dateFmt.format(new Date(cancelInfo.cancelledAt))}</dd>
        </div>
        {cancelInfo.reason && (
          <div className="flex flex-col gap-1">
            <dt className="text-gray-600">{t("reason")}</dt>
            <dd className="rounded-md bg-white p-2 text-gray-900">{cancelInfo.reason}</dd>
          </div>
        )}
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("itemReturned")}</dt>
          <dd className="text-gray-900">{cancelInfo.itemReturned ? t("yes") : t("no")}</dd>
        </div>
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("paymentRefunded")}</dt>
          <dd className="text-gray-900">{cancelInfo.paymentRefunded ? t("yes") : t("no")}</dd>
        </div>
      </dl>
      {refund && (
        <div className="space-y-1 border-t border-red-200 pt-3 text-sm">
          <h3 className="font-medium text-gray-900">{t("refund.title")}</h3>
          <div className="flex justify-between gap-3">
            <dt className="text-gray-600">{t("refund.originalAmount")}</dt>
            <dd className="text-gray-900">
              {refund.originalAmount} {stablecoin}
            </dd>
          </div>
          <div className="flex justify-between gap-3">
            <dt className="text-gray-600">{t("refund.gasFee")}</dt>
            <dd className="text-red-700">
              −{refund.gasFee} {stablecoin}
            </dd>
          </div>
          <div className="flex justify-between gap-3 border-t border-red-200 pt-1 font-semibold">
            <dt className="text-gray-700">{t("refund.netRefundAmount")}</dt>
            <dd className="text-gray-900">
              {refund.netRefundAmount} {stablecoin}
            </dd>
          </div>
          <div className="flex flex-wrap items-center justify-between gap-2">
            <dt className="text-gray-600">{t("refund.refundAddress")}</dt>
            <dd
              className="flex items-center gap-2 font-mono text-xs text-gray-900"
              title={refund.refundAddress}
            >
              {maskAddress(refund.refundAddress, 8, 6)}
              <CopyButton value={refund.refundAddress} />
            </dd>
          </div>
          {refund.txHash && (
            <div className="flex flex-wrap items-center justify-between gap-2">
              <dt className="text-gray-600">{t("refund.txHash")}</dt>
              <dd
                className="flex items-center gap-2 font-mono text-xs text-gray-900"
                title={refund.txHash}
              >
                {maskAddress(refund.txHash, 8, 6)}
                <CopyButton value={refund.txHash} />
              </dd>
            </div>
          )}
          {refund.refundedAt && (
            <div className="flex justify-between gap-3">
              <dt className="text-gray-600">{t("refund.refundedAt")}</dt>
              <dd className="text-gray-900">{dateFmt.format(new Date(refund.refundedAt))}</dd>
            </div>
          )}
        </div>
      )}
    </section>
  );
}
