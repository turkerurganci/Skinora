import { useTranslations, useLocale } from "next-intl";
import type { TransactionDetailResponse } from "@/lib/api/transactions";

export interface TransactionInfoPanelProps {
  detail: TransactionDetailResponse;
}

/**
 * 04 §7.3 sabit layout — sağ taraftaki "İşlem Bilgileri" kutusu.
 * Commission/total satırları sadece authenticated callers için döner
 * (public surface bunları null tutar — 07 §7.5).
 */
export function TransactionInfoPanel({ detail }: TransactionInfoPanelProps) {
  const t = useTranslations("transactionDetail.info");
  const locale = useLocale();
  const dateFmt = new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  });
  return (
    <dl className="space-y-2 rounded-lg border border-gray-200 bg-white p-4 text-sm">
      <div className="flex justify-between gap-3">
        <dt className="text-gray-600">{t("price")}</dt>
        <dd className="font-semibold text-gray-900">
          {detail.price} {detail.stablecoin}
        </dd>
      </div>
      {detail.commissionAmount != null && (
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("commission")}</dt>
          <dd className="text-gray-900">
            {detail.commissionAmount} {detail.stablecoin}
          </dd>
        </div>
      )}
      {detail.totalAmount != null && (
        <div className="flex justify-between gap-3 border-t border-gray-100 pt-2">
          <dt className="font-medium text-gray-700">{t("total")}</dt>
          <dd className="font-semibold text-gray-900">
            {detail.totalAmount} {detail.stablecoin}
          </dd>
        </div>
      )}
      <div className="flex justify-between gap-3">
        <dt className="text-gray-600">{t("token")}</dt>
        <dd className="text-gray-900">{detail.stablecoin} TRC-20</dd>
      </div>
      {detail.createdAt && (
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("createdAt")}</dt>
          <dd className="text-gray-900">{dateFmt.format(new Date(detail.createdAt))}</dd>
        </div>
      )}
    </dl>
  );
}
