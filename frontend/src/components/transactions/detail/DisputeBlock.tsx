import { useTranslations, useLocale } from "next-intl";
import type { TransactionDetailDispute } from "@/lib/api/transactions";

export interface DisputeBlockProps {
  dispute: TransactionDetailDispute;
}

/**
 * 04 §7.3 — Aktif dispute aktif olduğunda ek bölüm: tür, durum, otomatik
 * kontrol sonucu, eylem butonları.
 *
 * Butonlar (TX Hash Gir, Admin'e İlet) burada görünür ama disabled kalır
 * — DisputeForm 3-step UX'ini T92 wires (K2). Tooltip / hint bunu açıklar.
 */
export function DisputeBlock({ dispute }: DisputeBlockProps) {
  const t = useTranslations("transactionDetail.dispute");
  const locale = useLocale();
  const dateFmt = new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  });
  return (
    <section className="space-y-2 rounded-lg border border-orange-300 bg-orange-50 p-4">
      <h2 className="text-base font-semibold text-orange-900">{t("title")}</h2>
      <dl className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-gray-600">{t("type")}</dt>
          <dd className="text-gray-900">{t(`types.${dispute.type}`)}</dd>
        </div>
        <div>
          <dt className="text-gray-600">{t("status")}</dt>
          <dd className="text-gray-900">{t(`statuses.${dispute.status}`)}</dd>
        </div>
        <div>
          <dt className="text-gray-600">{t("createdAt")}</dt>
          <dd className="text-gray-900">{dateFmt.format(new Date(dispute.createdAt))}</dd>
        </div>
        {dispute.autoCheckResult && (
          <div className="sm:col-span-2">
            <dt className="text-gray-600">{t("autoCheckResult")}</dt>
            <dd className="rounded-md bg-white p-2 text-gray-900">{dispute.autoCheckResult}</dd>
          </div>
        )}
      </dl>
      <div className="flex flex-wrap gap-2">
        {dispute.canSubmitTxHash && (
          <button
            type="button"
            disabled
            title={t("comingInT92")}
            className="rounded-md border border-orange-300 bg-white px-3 py-1.5 text-sm font-medium text-orange-900 opacity-60"
          >
            {t("submitTxHash")}
          </button>
        )}
        {dispute.canEscalate && (
          <button
            type="button"
            disabled
            title={t("comingInT92")}
            className="rounded-md bg-orange-600 px-3 py-1.5 text-sm font-medium text-white opacity-60"
          >
            {t("escalate")}
          </button>
        )}
      </div>
    </section>
  );
}
