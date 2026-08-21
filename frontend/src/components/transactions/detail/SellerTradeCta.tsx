"use client";

import { useTranslations } from "next-intl";
import type { TransactionDetailItem } from "@/lib/api/transactions";

export interface SellerTradeCtaProps {
  /**
   * `detail.steamTradeOfferUrl` (07 §7.5) — in PAYMENT_RECEIVED this is the
   * BUYER's own trade URL, not an offer the platform built. Null defensively:
   * the backend only omits it if the buyer's URL was never stored, in which
   * case there is no link to open and the seller is told what to do instead.
   */
  tradeUrl: string | null | undefined;
  item: TransactionDetailItem;
}

/**
 * 04 §7.3 PAYMENT_RECEIVED × satıcı — **[Steam'de Trade Offer Gönder]**.
 *
 * The most critical cell of the v3.0 matrix: the awaited action is not on the
 * platform, it is on the seller, and the platform's only lever is this link.
 * It opens the buyer's trade URL in a new tab; the item is deliberately NOT
 * preselected (the platform is not a party to the trade and cannot build the
 * offer — 02 §2.2 step 6), which is why the item is repeated underneath as a
 * reminder and why the warning about sending the wrong item is not optional
 * decoration: a different item never raises the buyer's expected class count,
 * so delivery is never verified and the case ends up in a dispute (03 §6.3).
 */
export function SellerTradeCta({ tradeUrl, item }: SellerTradeCtaProps) {
  const t = useTranslations("transactionDetail.actions.paymentReceived.seller");

  return (
    <div className="space-y-3 rounded-md border border-yellow-300 bg-yellow-50 p-3">
      <p className="text-sm font-medium text-yellow-900">{t("title")}</p>

      {tradeUrl ? (
        <a
          href={tradeUrl}
          target="_blank"
          rel="noopener noreferrer"
          data-testid="seller-trade-cta"
          className="inline-flex items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
        >
          {t("cta")}
        </a>
      ) : (
        <p
          className="rounded-md border border-red-200 bg-red-50 p-2 text-sm text-red-700"
          role="alert"
          data-testid="seller-trade-cta-missing"
        >
          {t("missingLink")}
        </p>
      )}

      <div className="flex items-center gap-3 rounded-md border border-yellow-200 bg-white p-2">
        {item.imageUrl && (
          /* eslint-disable-next-line @next/next/no-img-element */
          <img
            src={item.imageUrl}
            alt=""
            aria-hidden="true"
            className="h-10 w-10 shrink-0 object-contain"
          />
        )}
        <div className="min-w-0">
          <p className="text-xs uppercase text-gray-500">{t("itemReminderLabel")}</p>
          <p className="truncate text-sm font-medium text-gray-900">{item.name}</p>
        </div>
      </div>

      <p className="text-xs text-yellow-900">{t("warning")}</p>
    </div>
  );
}
