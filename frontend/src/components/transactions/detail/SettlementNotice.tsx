"use client";

import { useLocale, useTranslations } from "next-intl";
import type { TransactionDetailTimeout } from "@/lib/api/transactions";
import { CountdownTimer } from "@/components/common";
import { formatDate } from "@/lib/utils/format";
import { asFreezeReason, computeWarningSeconds } from "./helpers";
import type { PanelRole } from "./helpers";

export interface SettlementNoticeProps {
  role: PanelRole;
  /**
   * The detail `timeout` block. In ITEM_DELIVERED the backend emits
   * `type: "settlement"` with `expiresAt = PayoutEligibleAt` (07 §7.5) — a
   * countdown, not a deadline: when it elapses nothing is cancelled, the payout
   * simply becomes eligible for its final check (02 §4.5.1).
   */
  timeout?: TransactionDetailTimeout | null;
}

/**
 * 04 §7.3 ITEM_DELIVERED — the settlement window, for both roles.
 *
 * The transaction stays open ~8 days after delivery because Steam keeps trades
 * reversible for 7 (02 §4.5.1). 04 §7.3 makes explaining that a requirement
 * rather than a nicety: without it the seller reads the wait as a missing
 * payment and opens a support ticket.
 *
 * The two roles get materially different messages, and the difference is the
 * point:
 *   • Seller — when the money arrives, plus why it is not here yet. The
 *     countdown is theirs; 04 §7.3 asks for day/hour granularity, which is what
 *     `CountdownTimer`'s verbose format already produces past the 24h mark.
 *   • Buyer — a GUARANTEE, not a warning: if the seller reverses the trade
 *     inside this window, the money comes back. No countdown: they are not
 *     waiting for anything, and a ticking clock would read as a deadline of
 *     their own.
 */
export function SettlementNotice({ role, timeout }: SettlementNoticeProps) {
  const t = useTranslations("transactionDetail.actions.itemDelivered");
  const locale = useLocale();

  if (role === "buyer") {
    return (
      <div
        className="space-y-1 rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-900"
        role="status"
        data-testid="settlement-notice-buyer"
      >
        <p className="font-medium">{t("buyer.title")}</p>
        <p>{t("buyer.guarantee")}</p>
      </div>
    );
  }

  // The block is emitted only while a settlement countdown exists; a delivered
  // transaction whose PayoutEligibleAt was never armed has no date to promise,
  // so the dated sentence is dropped rather than filled with a placeholder.
  const payoutDate = timeout?.expiresAt ? formatDate(timeout.expiresAt, locale) : null;

  return (
    <div
      className="space-y-2 rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-900"
      role="status"
      data-testid="settlement-notice-seller"
    >
      <p className="font-medium">
        {payoutDate ? t("seller.titleWithDate", { date: payoutDate }) : t("seller.title")}
      </p>
      <p className="text-green-800">{t("seller.explanation")}</p>
      {timeout && (
        <div className="flex items-center gap-2">
          <span className="text-xs font-medium uppercase text-gray-600">
            {t("seller.countdownLabel")}
          </span>
          {/* A frozen window must not tick: 05 §4.5 stops the clock during a
              maintenance / outage freeze, and a running countdown would promise
              a payout date the platform is not counting down to. */}
          {timeout.frozen ? (
            <CountdownTimer
              deadline={timeout.expiresAt}
              warningThresholdSeconds={computeWarningSeconds(timeout)}
              frozen
              frozenReason={asFreezeReason(timeout.frozenReason)}
            />
          ) : (
            <CountdownTimer
              deadline={timeout.expiresAt}
              warningThresholdSeconds={computeWarningSeconds(timeout)}
              format="verbose"
            />
          )}
        </div>
      )}
    </div>
  );
}
