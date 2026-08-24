"use client";

import { useState, type ReactNode } from "react";
import { useTranslations, useLocale } from "next-intl";
import Link from "next/link";
import { ApiError } from "@/lib/api/client";
import { cancelTransaction, type TransactionDetailResponse } from "@/lib/api/transactions";
import { CancelModal, CountdownTimer } from "@/components/common";
import {
  asFreezeReason,
  computeWarningSeconds,
  isActivePartyRow,
  panelRowFor,
  type PanelRole,
  type PanelRow,
} from "./helpers";
import { AcceptForm } from "./AcceptForm";
import { ConfirmReadyButton } from "./ConfirmReadyButton";
import { ConfirmReceiptButton } from "./ConfirmReceiptButton";
import { DisputeModal } from "./DisputeModal";
import { InventoryHiddenNotice } from "./InventoryHiddenNotice";
import { SellerTradeCta } from "./SellerTradeCta";
import { SettlementNotice } from "./SettlementNotice";
import { tDynamic } from "@/lib/i18n/dynamicKey";

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
 * 04 §7.3 — S07 State × Role aksiyon paneli (v3.0).
 *
 * The matrix itself lives in `helpers.panelRowFor`: every (status × role) pair
 * resolves to exactly one {@link PanelRow}, and this file renders exactly one
 * branch per row. Splitting the classification from the rendering is what makes
 * the matrix checkable — `StateActionPanel.matrix.test.ts` walks every cell and
 * fails on an unclassified one, instead of a new status quietly falling through
 * to an empty action area (the failure mode REFUNDED had actually been in, and
 * the one T134's validation recorded for the timeline as observation G1).
 *
 * Three shapes of row:
 *   • Self-contained — frozen, unwound, public, completed. Return their own
 *     block (or null) and no secondary actions.
 *   • Active party rows — share the countdown + primary action + secondary
 *     (cancel / dispute) frame.
 *   • Rows whose mechanics need state of their own (a mutation, a modal) live
 *     in sibling components, the way CREATED × buyer already did with
 *     `AcceptForm`: `ConfirmReadyButton`, `ConfirmReceiptButton`,
 *     `SellerTradeCta`, `SettlementNotice`.
 *
 * Suspended override (04 §7.3): every button is disabled and the read-only
 * banner is rendered above by `SuspendedBanner`; here `isSuspended` only
 * controls activation.
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
  const role: PanelRole | null = userRole ?? null;
  const row = panelRowFor(status, role);

  // 04 §7.3 FLAGGED / EMERGENCY_HOLD — every action off; the banner is already
  // rendered by FlagHoldBanner, so all that belongs here is the frozen clock.
  if (row === "frozen") {
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

  // 04 §7.3 CANCELLED_* (and REFUNDED, 07 §7.5) — the record, not an action
  // area. CancelInfoBlock owns that surface at page level; a LATE_PAYMENT note
  // comes from PaymentEventBanners.
  if (row === "unwound") return null;

  // A status the matrix has never been taught. Rendering nothing is the safe
  // half of the answer; the loud half is the matrix guard.
  if (row === "unclassified") return null;

  if (row === "publicNoAction") return null;

  // 04 §7.3 public varyant — scoped to CREATED (07 §7.5).
  if (row === "publicCreated") {
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

  // 04 §7.3 COMPLETED — SellerPayoutSummary carries the seller's breakdown at
  // page level; this is the one-line confirmation next to it.
  if (row === "completedSeller" || row === "completedBuyer") {
    return (
      <div
        className="rounded-md border border-green-300 bg-green-50 p-3 text-sm text-green-900"
        role="status"
      >
        {row === "completedSeller" ? t("completed.seller") : t("completed.buyer")}
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
        setCancelError(tDynamic(t, `cancelErrors.${err.code}`, t("cancelErrors.generic")));
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

  // 04 §7.3 PAYMENT_RECEIVED — the cancel asymmetry, both halves. The seller may
  // still walk away (02 §7), so their modal carries the consequence; the buyer
  // may not, and 04 §7.3 asks for the reason to be stated rather than left as a
  // greyed-out button with no explanation.
  const cancelRefundWarning =
    row === "paymentReceivedSeller" ? t("paymentReceived.seller.cancelWarning") : undefined;
  const cancelDisabledReason =
    row === "paymentReceivedBuyer" && cancelButtonShown && !isSuspended
      ? t("paymentReceived.buyer.cannotCancel")
      : null;

  // 04 §7.3 ITEM_DELIVERED — the settlement window owns its own countdown
  // (labelled, day/hour) for the seller and the buyer gets none at all, so the
  // generic timer at the top of the frame is suppressed for both.
  const showFrameCountdown =
    Boolean(timeout) && row !== "itemDeliveredSeller" && row !== "itemDeliveredBuyer";

  return (
    <div className="space-y-4">
      {showFrameCountdown && timeout && (
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
        row={row}
        detail={detail}
        defaultRefundAddress={defaultRefundAddress}
        defaultSteamTradeUrl={defaultSteamTradeUrl}
        isAuthenticated={isAuthenticated}
        isSuspended={isSuspended}
        onAccepted={onRefetch}
      />

      {isActivePartyRow(row) && (cancelButtonShown || disputeButtonShown) && (
        <div className="space-y-2 border-t border-gray-100 pt-3">
          <div className="flex flex-wrap gap-2">
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
          {cancelDisabledReason && (
            <p className="text-xs text-gray-600" data-testid="cancel-disabled-reason">
              {cancelDisabledReason}
            </p>
          )}
        </div>
      )}

      <CancelModal
        open={cancelOpen}
        refundDescription={cancelRefundWarning}
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
  row: PanelRow;
  detail: TransactionDetailResponse;
  defaultRefundAddress: string | null;
  defaultSteamTradeUrl: string | null;
  isAuthenticated: boolean;
  isSuspended: boolean;
  onAccepted: () => void;
}

/**
 * One branch per active party row of the 04 §7.3 matrix. The switch is
 * exhaustive over the rows `isActivePartyRow` admits; anything else is handled
 * by the caller before this component is reached.
 */
function PrimaryActionPanel({
  row,
  detail,
  defaultRefundAddress,
  defaultSteamTradeUrl,
  isAuthenticated,
  isSuspended,
  onAccepted,
}: PrimaryActionPanelProps) {
  const t = useTranslations("transactionDetail.actions");
  const { availableActions } = detail;
  // 07 §7.5 — `buyerInventoryVisible` is tri-state. `undefined` means the read
  // has not happened yet (before the seller confirms readiness) and must not be
  // reported as "hidden", so the check is strict.
  const inventoryHidden = detail.buyerInventoryVisible === false;

  switch (row) {
    // CREATED — buyer gets the Accept form, seller waits.
    case "createdBuyer": {
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
    case "createdSeller":
      return <InfoBox>{t("created.seller")}</InfoBox>;

    // ACCEPTED — the seller's readiness confirmation is the awaited action.
    case "acceptedSeller":
      return (
        <ConfirmReadyButton
          transactionId={detail.id}
          canConfirmReady={Boolean(availableActions.canConfirmReady)}
          isSuspended={isSuspended}
          onConfirmed={onAccepted}
        />
      );
    case "acceptedBuyer":
      return <InfoBox>{t("accepted.buyer")}</InfoBox>;

    // SELLER_CONFIRMED — the deposit address is open to the buyer, who sees it
    // in PaymentInfoBlock at page level (04 §7.3 "Ödeme Bilgileri Bölümü").
    // The panel carries the waiting message and, for both parties, the standing
    // "no inventory evidence" condition if it applies.
    case "sellerConfirmedSeller":
      return (
        <div className="space-y-3">
          <InfoBox>{t("sellerConfirmed.seller")}</InfoBox>
          {inventoryHidden && <InventoryHiddenNotice role="seller" />}
        </div>
      );
    case "sellerConfirmedBuyer":
      return (
        <div className="space-y-3">
          <InfoBox>{t("sellerConfirmed.buyer")}</InfoBox>
          {inventoryHidden && <InventoryHiddenNotice role="buyer" />}
        </div>
      );

    // PAYMENT_RECEIVED — 04 §7.3's most critical state: the money is escrowed
    // and the awaited action is the seller's Steam trade.
    case "paymentReceivedSeller":
      return (
        <div className="space-y-3">
          <SellerTradeCta tradeUrl={detail.steamTradeOfferUrl} item={detail.item} />
          {inventoryHidden && <InventoryHiddenNotice role="seller" />}
        </div>
      );
    case "paymentReceivedBuyer":
      return (
        <div className="space-y-3">
          <ConfirmReceiptButton
            transactionId={detail.id}
            canConfirmReceipt={Boolean(availableActions.canConfirmReceipt)}
            isSuspended={isSuspended}
            onConfirmed={onAccepted}
          />
          {inventoryHidden && <InventoryHiddenNotice role="buyer" />}
        </div>
      );

    // ITEM_DELIVERED — the settlement window (02 §4.5.1).
    case "itemDeliveredSeller":
      return <SettlementNotice role="seller" timeout={detail.timeout} />;
    case "itemDeliveredBuyer":
      return <SettlementNotice role="buyer" timeout={detail.timeout} />;

    default:
      return null;
  }
}

/** The "nothing to do, here is why" box the four waiting cells share. */
function InfoBox({ children }: { children: ReactNode }) {
  return (
    <div className="rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900">
      {children}
    </div>
  );
}
