import { useTranslations } from "next-intl";
import type { TransactionDetailPayment, TransactionDetailTimeout } from "@/lib/api/transactions";
import { CopyButton, CountdownTimer } from "@/components/common";
import { formatStablecoin } from "@/lib/utils/format";
import { networkLabel, networkMeta } from "@/lib/utils/blockchain";
import { showsNetworkFeeLine } from "@/lib/transactions/networkChoice";
import { BlockchainNetwork } from "@/types/enums";
import { asFreezeReason, computeWarningSeconds } from "./helpers";

export interface PaymentInfoBlockProps {
  payment: TransactionDetailPayment;
  timeout?: TransactionDetailTimeout | null;
  /**
   * T156 — the network the buyer chose (07 §7.5 `network`). `null` means the
   * response did not say which network the address is on; the block then
   * withholds the address instead of guessing (a payment sent on the wrong
   * network is the mistake this block exists to prevent).
   */
  network: BlockchainNetwork | null;
  /** Breakdown parts, verbatim from the detail response (02 §5). */
  price: string;
  commissionAmount?: string | null;
  networkFeeAmount?: string | null;
}

/**
 * 04 §7.3 — Alıcı görünümünde SELLER_CONFIRMED state'inde ödeme bilgileri
 * paneli. T156 (02 §4.2, §5, §12.2; 03 §3.4):
 *   - the network is named in the heading and next to the token, explicitly;
 *   - the amount to send is `payment.expectedAmount` verbatim (T144: shown =
 *     expected), and on TRON its breakdown carries the TRON network fee as its
 *     own line (K8) — Ethereum has no fee line;
 *   - the exchange warning is gone (K9 — refunds go to the refund address, not
 *     back to the sender); in its place the network warning, plus on Ethereum
 *     the note that the same `0x` address exists on other EVM networks
 *     (08 §3.0 "Yanlış ağ").
 */
export function PaymentInfoBlock({
  payment,
  timeout,
  network,
  price,
  commissionAmount,
  networkFeeAmount,
}: PaymentInfoBlockProps) {
  const t = useTranslations("transactionDetail.paymentInfo");
  const showCountdown = timeout && !timeout.frozen;

  if (network === null) {
    return (
      <section
        role="alert"
        className="rounded-lg border border-red-300 bg-red-50 p-4 text-sm text-red-900"
      >
        {t("unknownNetwork")}
      </section>
    );
  }

  const label = networkLabel(network);
  const meta = networkMeta(network);
  const feeLine = showsNetworkFeeLine(network) ? (networkFeeAmount ?? null) : null;

  return (
    <section
      className="space-y-3 rounded-lg border border-yellow-300 bg-yellow-50 p-4"
      aria-labelledby="payment-info-title"
    >
      <h2 id="payment-info-title" className="text-base font-semibold text-gray-900">
        {t("title")}
      </h2>
      <p
        className="inline-flex items-center gap-2 rounded-md border border-gray-900 bg-white px-3 py-1 text-sm font-bold text-gray-900"
        data-testid="payment-network"
      >
        {t("networkLabel")}: {label}
      </p>
      <div className="space-y-1">
        <p className="text-xs font-medium uppercase text-gray-600">
          {t("addressLabel", { network: meta.name })}
        </p>
        <div className="flex items-center gap-2 rounded-md border border-gray-200 bg-white px-3 py-2 font-mono text-sm text-gray-900 break-all">
          <span className="flex-1" data-testid="payment-address">
            {payment.address}
          </span>
          <CopyButton value={payment.address} />
        </div>
      </div>
      <dl className="grid grid-cols-1 gap-2 text-sm sm:grid-cols-2">
        <div>
          <dt className="text-gray-600">{t("amountLabel")}</dt>
          <dd className="font-semibold text-gray-900" data-testid="payment-amount">
            {formatStablecoin(payment.expectedAmount, payment.stablecoin)}
          </dd>
          <dd className="mt-1 space-y-0.5 text-xs text-gray-700" data-testid="payment-breakdown">
            <span className="block">
              {t("breakdown.price")}: {formatStablecoin(price, payment.stablecoin)}
            </span>
            {commissionAmount != null && (
              <span className="block">
                {t("breakdown.commission")}:{" "}
                {formatStablecoin(commissionAmount, payment.stablecoin)}
              </span>
            )}
            {feeLine !== null && (
              <span className="block" data-testid="payment-network-fee">
                {t("breakdown.tronNetworkFee")}: {formatStablecoin(feeLine, payment.stablecoin)}
              </span>
            )}
          </dd>
        </div>
        <div>
          <dt className="text-gray-600">{t("tokenLabel")}</dt>
          <dd className="text-gray-900">
            {payment.stablecoin} ({meta.standard})
          </dd>
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
      <div
        className="rounded-md border border-yellow-300 bg-yellow-100 p-3 text-sm text-yellow-900"
        role="note"
        aria-label={t("warnings.title")}
      >
        <p className="mb-1 font-medium">{t("warnings.title")}</p>
        <ul className="list-disc space-y-0.5 pl-5">
          <li className="font-semibold" data-testid="payment-network-warning">
            {t("warnings.onlyNetwork", { token: payment.stablecoin, network: label })}
          </li>
          {network === BlockchainNetwork.ETHEREUM && (
            <li data-testid="payment-evm-warning">{t("warnings.evmSameAddress")}</li>
          )}
          <li>{t("warnings.fullAmount")}</li>
          <li>{t("warnings.noOtherToken")}</li>
        </ul>
      </div>
    </section>
  );
}
