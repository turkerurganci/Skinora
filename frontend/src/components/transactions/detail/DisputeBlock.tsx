"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import type { TransactionDetailDispute } from "@/lib/api/transactions";
import { formatDateTime } from "@/lib/utils/format";
import { DisputeType } from "@/types/enums";
import { DisputeModal } from "./DisputeModal";
import { tDynamicOrKey } from "@/lib/i18n/dynamicKey";

export interface DisputeBlockProps {
  transactionId: string;
  dispute: TransactionDetailDispute;
  isSuspended: boolean;
}

/**
 * 04 §7.3 — Aktif dispute aktif olduğunda ek bölüm: tür, durum, otomatik
 * kontrol sonucu, eylem butonları.
 *
 * T92 — "TX Hash Gir" ve "Admin'e İlet" butonları artık aktif. Tıklanınca
 * DisputeModal'ı `existingDispute` modunda açar (DisputeForm step 2'den
 * devam eder). Sunucudan gelen `canSubmitTxHash` / `canEscalate` flag'leri
 * butonların görünürlüğünü ve aktivasyonunu yönetir.
 */
export function DisputeBlock({ transactionId, dispute, isSuspended }: DisputeBlockProps) {
  const t = useTranslations("transactionDetail.dispute");
  const locale = useLocale();
  const [modalOpen, setModalOpen] = useState(false);

  const anyActionShown = dispute.canSubmitTxHash || dispute.canEscalate;

  return (
    <section className="space-y-2 rounded-lg border border-orange-300 bg-orange-50 p-4">
      <h2 className="text-base font-semibold text-orange-900">{t("title")}</h2>
      <dl className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-gray-600">{t("type")}</dt>
          <dd className="text-gray-900">{tDynamicOrKey(t, `types.${dispute.type}`)}</dd>
        </div>
        <div>
          <dt className="text-gray-600">{t("status")}</dt>
          <dd className="text-gray-900">{tDynamicOrKey(t, `statuses.${dispute.status}`)}</dd>
        </div>
        <div>
          <dt className="text-gray-600">{t("createdAt")}</dt>
          <dd className="text-gray-900">{formatDateTime(dispute.createdAt, locale)}</dd>
        </div>
        {dispute.autoCheckResult && (
          <div className="sm:col-span-2">
            <dt className="text-gray-600">{t("autoCheckResult")}</dt>
            <dd className="rounded-md bg-white p-2 text-gray-900">{dispute.autoCheckResult}</dd>
          </div>
        )}
      </dl>
      {anyActionShown && (
        <div className="flex flex-wrap gap-2">
          {dispute.canSubmitTxHash && (
            <button
              type="button"
              disabled={isSuspended}
              onClick={() => setModalOpen(true)}
              className="rounded-md border border-orange-300 bg-white px-3 py-1.5 text-sm font-medium text-orange-900 hover:bg-orange-100 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {t("submitTxHash")}
            </button>
          )}
          {dispute.canEscalate && (
            <button
              type="button"
              disabled={isSuspended}
              onClick={() => setModalOpen(true)}
              className="rounded-md bg-orange-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-orange-700 disabled:cursor-not-allowed disabled:opacity-60"
            >
              {t("escalate")}
            </button>
          )}
        </div>
      )}
      <DisputeModal
        open={modalOpen}
        transactionId={transactionId}
        existingDispute={{
          disputeId: dispute.id,
          type: dispute.type as DisputeType,
          autoCheckMessage: dispute.autoCheckResult ?? null,
          canSubmitTxHash: dispute.canSubmitTxHash,
          canEscalate: dispute.canEscalate,
        }}
        onClose={() => setModalOpen(false)}
      />
    </section>
  );
}
