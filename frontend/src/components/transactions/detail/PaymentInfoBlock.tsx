import { useTranslations } from "next-intl";
import type { TransactionDetailPayment, TransactionDetailTimeout } from "@/lib/api/transactions";
import { CopyButton, CountdownTimer } from "@/components/common";
import { asFreezeReason, computeWarningSeconds } from "./helpers";

export interface PaymentInfoBlockProps {
  payment: TransactionDetailPayment;
  timeout?: TransactionDetailTimeout | null;
}

/**
 * 04 §7.3 — Alıcı görünümünde ITEM_ESCROWED state'inde ödeme bilgileri
 * paneli: adres + tutar + ağ + uyarılar + countdown. Address shown in
 * full (alıcı kopyalayacak), warning list is verbatim per spec.
 */
export function PaymentInfoBlock({ payment, timeout }: PaymentInfoBlockProps) {
  const t = useTranslations("transactionDetail.paymentInfo");
  const showCountdown = timeout && !timeout.frozen;
  return (
    <section className="space-y-3 rounded-lg border border-yellow-300 bg-yellow-50 p-4">
      <h2 className="text-base font-semibold text-gray-900">{t("title")}</h2>
      <div className="space-y-1">
        <p className="text-xs font-medium uppercase text-gray-600">{t("addressLabel")}</p>
        <div className="flex items-center gap-2 rounded-md border border-gray-200 bg-white px-3 py-2 font-mono text-sm text-gray-900 break-all">
          <span className="flex-1">{payment.address}</span>
          <CopyButton value={payment.address} />
        </div>
      </div>
      <dl className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-gray-600">{t("amountLabel")}</dt>
          <dd className="font-semibold text-gray-900">
            {payment.expectedAmount} {payment.stablecoin}
          </dd>
        </div>
        <div>
          <dt className="text-gray-600">{t("tokenLabel")}</dt>
          <dd className="text-gray-900">{payment.stablecoin} (TRC-20)</dd>
        </div>
        <div>
          <dt className="text-gray-600">{t("networkLabel")}</dt>
          <dd className="text-gray-900">{payment.network}</dd>
        </div>
        {timeout && (
          <div>
            <dt className="text-gray-600">{t("remainingLabel")}</dt>
            <dd>
              {showCountdown ? (
                <CountdownTimer
                  deadline={timeout.expiresAt}
                  warningThresholdSeconds={computeWarningSeconds(timeout)}
                  format="clock"
                />
              ) : (
                <CountdownTimer
                  deadline={timeout.expiresAt}
                  warningThresholdSeconds={computeWarningSeconds(timeout)}
                  frozen
                  frozenReason={asFreezeReason(timeout.frozenReason)}
                />
              )}
            </dd>
          </div>
        )}
      </dl>
      <div className="rounded-md border border-yellow-300 bg-yellow-100 p-3 text-sm text-yellow-900">
        <p className="mb-1 font-medium">{t("warnings.title")}</p>
        <ul className="list-disc space-y-0.5 pl-5">
          <li>{t("warnings.onlyTrc20", { token: payment.stablecoin })}</li>
          <li>{t("warnings.fullAmount")}</li>
          <li>{t("warnings.noOtherToken")}</li>
          <li>{t("warnings.noExchange")}</li>
        </ul>
      </div>
    </section>
  );
}
