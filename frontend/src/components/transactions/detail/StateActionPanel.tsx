"use client";

import { useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import Link from "next/link";
import { ApiError } from "@/lib/api/client";
import { cancelTransaction, type TransactionDetailResponse } from "@/lib/api/transactions";
import { CancelModal, CountdownTimer } from "@/components/common";
import { TransactionStatus } from "@/types/enums";
import {
  asFreezeReason,
  computeWarningSeconds,
  isCancelledStatus,
  isEmergencyHold,
  isFlagged,
  isTerminalStatus,
} from "./helpers";
import { AcceptForm } from "./AcceptForm";
import { DisputeModal } from "./DisputeModal";

export interface StateActionPanelProps {
  detail: TransactionDetailResponse;
  defaultRefundAddress: string | null;
  defaultSteamTradeUrl: string | null;
  isAuthenticated: boolean;
  isSuspended: boolean;
  onRefetch: () => void;
  /**
   * Relative path (locale-less) the public login CTA returns to after Steam
   * auth. Defaults to the id-based detail route; the OPEN_LINK invite page
   * passes `/invite/:token` so the visitor lands back on the invite as a
   * prospective buyer. The auth flow reads this as the `returnUrl` query
   * param and prepends the locale (auth/callback localePath).
   */
  loginReturnTo?: string;
}

/**
 * 04 §7.3 — State × Role aksiyon paneli. Switch tree explicit on purpose;
 * each branch matches one row of the state×role matrix in the spec.
 *
 * Tek bir component'te tutmamızın sebebi: countdown + role mesajları + iptal
 * butonu sürekli aynı 3-bölümlü iskelet kullanır. Branch'lerin ne yaptığı
 * tek bir dosyada görünmesi, spec ile diff almayı kolaylaştırır.
 *
 * Suspended override (04 §7.3): tüm butonlar disabled, salt-okunur banner
 * SuspendedBanner ile üst seviyede gösterilir; burada `isSuspended` flag'i
 * yalnız buton aktivasyonunu kontrol eder.
 */
export function StateActionPanel({
  detail,
  defaultRefundAddress,
  defaultSteamTradeUrl,
  isAuthenticated,
  isSuspended,
  onRefetch,
  loginReturnTo,
}: StateActionPanelProps) {
  const t = useTranslations("transactionDetail.actions");
  const locale = useLocale();
  const [cancelOpen, setCancelOpen] = useState(false);
  const [cancelError, setCancelError] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState(false);
  const [disputeOpen, setDisputeOpen] = useState(false);

  const { status, timeout, userRole, availableActions } = detail;
  const role = userRole;

  // 04 §7.3 — FLAGGED/EMERGENCY_HOLD aksiyonları tamamen devre dışı;
  // banner FlagHoldBanner tarafından zaten gösterilmiştir, burada sadece
  // countdown frozen state'ini gösteren placeholder bırakıyoruz.
  if (isEmergencyHold(status) || isFlagged(status)) {
    return (
      <div className="rounded-lg border border-gray-200 bg-gray-50 p-4 text-sm text-gray-700">
        {t("frozenInfo")}
        {timeout && (
          <div className="mt-2">
            <CountdownTimer
              deadline={timeout.expiresAt}
              warningThresholdSeconds={computeWarningSeconds(timeout)}
              frozen
              frozenReason={asFreezeReason(timeout.frozenReason)}
            />
          </div>
        )}
      </div>
    );
  }

  // Terminal cancelled states — CancelInfoBlock üst seviyede bilgileri
  // gösterir; burada sadece ek mesaj veya gecikmeli ödeme banner
  // PaymentEventBanners (LATE_PAYMENT) tarafından yönetilir.
  if (isCancelledStatus(status)) {
    return null;
  }

  // COMPLETED — SellerPayoutSummary (satıcı view) veya simple confirmation
  if (status === TransactionStatus.COMPLETED) {
    return (
      <div
        className="rounded-md border border-green-300 bg-green-50 p-3 text-sm text-green-900"
        role="status"
      >
        {role === "seller" ? t("completed.seller") : t("completed.buyer")}
      </div>
    );
  }

  // Public (unauthenticated, CREATED only — 07 §7.5)
  if (!role) {
    if (status !== TransactionStatus.CREATED) return null;
    // The auth flow reads `returnUrl` (login + callback both) and prepends the
    // locale on success; pass a locale-less relative path.
    const returnTarget = loginReturnTo ?? `/transactions/${detail.id}`;
    return (
      <div className="space-y-3 rounded-lg border border-blue-300 bg-blue-50 p-4">
        <p className="text-sm text-gray-800">{t("public.acceptHint")}</p>
        <Link
          href={`/${locale}/auth/login?returnUrl=${encodeURIComponent(returnTarget)}`}
          className="inline-flex items-center justify-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700"
        >
          {t("public.loginCta")}
        </Link>
      </div>
    );
  }

  async function handleCancelConfirm(reason: string) {
    setCancelling(true);
    setCancelError(null);
    try {
      await cancelTransaction(detail.id, { reason });
      setCancelOpen(false);
      onRefetch();
    } catch (err) {
      if (err instanceof ApiError) {
        setCancelError(
          t.has(`cancelErrors.${err.code}`)
            ? t(`cancelErrors.${err.code}`)
            : t("cancelErrors.generic"),
        );
      } else {
        setCancelError(t("cancelErrors.generic"));
      }
    } finally {
      setCancelling(false);
    }
  }

  const cancelButtonShown = availableActions.canCancel != null;
  const cancelButtonEnabled = Boolean(availableActions.canCancel) && !isSuspended && !cancelling;
  // T92 — server's `canDispute` flag drives both visibility and enablement.
  // The button is shown whenever the server surfaces the flag (i.e. the
  // user is the buyer + transaction is in a disputable state — 02 §10.2)
  // and enabled only when it's true + the session isn't suspended.
  const disputeButtonShown = availableActions.canDispute != null;
  const disputeButtonEnabled = Boolean(availableActions.canDispute) && !isSuspended;

  return (
    <div className="space-y-4">
      {timeout && (
        <div className="flex items-center gap-3 rounded-md border border-gray-200 bg-white p-3">
          <span className="text-xs font-medium uppercase text-gray-500">{t("countdownLabel")}</span>
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

      <PrimaryActionPanel
        detail={detail}
        defaultRefundAddress={defaultRefundAddress}
        defaultSteamTradeUrl={defaultSteamTradeUrl}
        isAuthenticated={isAuthenticated}
        isSuspended={isSuspended}
        onAccepted={onRefetch}
      />

      {!isTerminalStatus(status) && (cancelButtonShown || disputeButtonShown) && (
        <div className="flex flex-wrap gap-2 border-t border-gray-100 pt-3">
          {cancelButtonShown && (
            <button
              type="button"
              disabled={!cancelButtonEnabled}
              onClick={() => setCancelOpen(true)}
              className="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm font-medium text-red-700 hover:bg-red-50 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {t("cancel")}
            </button>
          )}
          {disputeButtonShown && (
            <button
              type="button"
              disabled={!disputeButtonEnabled}
              onClick={() => setDisputeOpen(true)}
              className="rounded-md border border-orange-300 bg-white px-3 py-1.5 text-sm font-medium text-orange-700 hover:bg-orange-50 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {t("dispute")}
            </button>
          )}
        </div>
      )}

      <CancelModal
        open={cancelOpen}
        onConfirm={handleCancelConfirm}
        onClose={() => {
          if (!cancelling) {
            setCancelOpen(false);
            setCancelError(null);
          }
        }}
      />
      {cancelError && (
        <p
          className="rounded-md border border-red-200 bg-red-50 p-2 text-sm text-red-700"
          role="alert"
        >
          {cancelError}
        </p>
      )}
      <DisputeModal
        open={disputeOpen}
        transactionId={detail.id}
        onClose={() => {
          setDisputeOpen(false);
          onRefetch();
        }}
      />
    </div>
  );
}

interface PrimaryActionPanelProps {
  detail: TransactionDetailResponse;
  defaultRefundAddress: string | null;
  defaultSteamTradeUrl: string | null;
  isAuthenticated: boolean;
  isSuspended: boolean;
  onAccepted: () => void;
}

function PrimaryActionPanel({
  detail,
  defaultRefundAddress,
  defaultSteamTradeUrl,
  isAuthenticated,
  isSuspended,
  onAccepted,
}: PrimaryActionPanelProps) {
  const t = useTranslations("transactionDetail.actions");
  const { status, userRole, availableActions } = detail;
  const role = userRole!;

  // CREATED — buyer'a Accept form, seller'a "alıcı bekleniyor" mesajı
  if (status === TransactionStatus.CREATED) {
    if (role === "buyer") {
      const cantAccept = !availableActions.canAccept;
      return (
        <AcceptForm
          transactionId={detail.id}
          defaultRefundAddress={defaultRefundAddress}
          defaultSteamTradeUrl={defaultSteamTradeUrl}
          disabled={cantAccept || isSuspended || !isAuthenticated}
          disabledReason={
            isSuspended
              ? t("suspendedReadOnly")
              : cantAccept
                ? t("created.buyer.cannotAcceptReason")
                : undefined
          }
          onAccepted={onAccepted}
        />
      );
    }
    return (
      <div className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900">
        {t("created.seller")}
      </div>
    );
  }

  if (status === TransactionStatus.ACCEPTED) {
    return (
      <div className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900">
        {role === "seller" ? t("accepted.seller") : t("accepted.buyer")}
      </div>
    );
  }

  if (status === TransactionStatus.TRADE_OFFER_SENT_TO_SELLER) {
    return (
      <div className="space-y-2 rounded-md border border-yellow-200 bg-yellow-50 p-3 text-sm text-yellow-900">
        <p>{role === "seller" ? t("tradeOfferToSeller.seller") : t("tradeOfferToSeller.buyer")}</p>
        {detail.steamTradeOfferUrl && (
          <SteamTradeOfferLink url={detail.steamTradeOfferUrl} label={t("viewTradeOffer")} />
        )}
      </div>
    );
  }

  if (status === TransactionStatus.ITEM_ESCROWED) {
    return (
      <div className="rounded-md border border-yellow-200 bg-yellow-50 p-3 text-sm text-yellow-900">
        {role === "seller" ? t("itemEscrowed.seller") : t("itemEscrowed.buyer")}
      </div>
    );
  }

  if (status === TransactionStatus.PAYMENT_RECEIVED) {
    return (
      <div className="rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-900">
        {role === "seller" ? t("paymentReceived.seller") : t("paymentReceived.buyer")}
      </div>
    );
  }

  if (status === TransactionStatus.TRADE_OFFER_SENT_TO_BUYER) {
    return (
      <div className="space-y-2 rounded-md border border-yellow-200 bg-yellow-50 p-3 text-sm text-yellow-900">
        <p>{role === "seller" ? t("tradeOfferToBuyer.seller") : t("tradeOfferToBuyer.buyer")}</p>
        {detail.steamTradeOfferUrl && (
          <SteamTradeOfferLink url={detail.steamTradeOfferUrl} label={t("viewTradeOffer")} />
        )}
      </div>
    );
  }

  if (status === TransactionStatus.ITEM_DELIVERED) {
    return (
      <div className="rounded-md border border-green-200 bg-green-50 p-3 text-sm text-green-900">
        {role === "seller" ? t("itemDelivered.seller") : t("itemDelivered.buyer")}
      </div>
    );
  }

  return null;
}

/**
 * WP12 backend / WP13 FE — "Go to Steam trade offer" deep link, shown in the
 * TRADE_OFFER_SENT_TO_* states when the backend populated `steamTradeOfferUrl`
 * (07 §7.5). Opens the offer in a new tab so the recipient can accept it.
 */
function SteamTradeOfferLink({ url, label }: { url: string; label: string }) {
  return (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      className="inline-flex items-center gap-1 rounded-md bg-yellow-600 px-3 py-1.5 text-sm font-medium text-white hover:bg-yellow-700"
    >
      {label}
      <span aria-hidden="true">↗</span>
    </a>
  );
}
