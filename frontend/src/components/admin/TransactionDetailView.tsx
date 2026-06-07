"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { useLocale, useTranslations } from "next-intl";
import { StatusBadge } from "@/components/common";
import type { ExtendedStatus } from "@/components/common";
import { formatDateTime, formatStablecoin } from "@/lib/utils/format";
import { TransactionStatus } from "@/types/enums";
import type {
  AdminTransactionDetail,
  AdminTransactionParty,
  EmergencyHoldReleaseAction,
} from "@/lib/api/admin";
import { useApproveFlag, useRejectFlag } from "@/lib/hooks/useAdminFlagMutations";
import {
  useApplyEmergencyHold,
  useCancelTransaction,
  useReleaseEmergencyHold,
} from "@/lib/hooks/useAdminTransactionMutations";
import {
  FlagActionModal,
  type FlagActionReasonConfig,
  type FlagActionTone,
} from "./FlagActionModal";

const CANCEL_REASON_MIN = 10;
const HOLD_REASON_MIN = 10;
const RELEASE_NOTE_MIN = 1;
const TRONSCAN_TX = "https://tronscan.org/#/transaction/";

const TERMINAL_STATES: ReadonlySet<TransactionStatus> = new Set([
  TransactionStatus.COMPLETED,
  TransactionStatus.CANCELLED_TIMEOUT,
  TransactionStatus.CANCELLED_SELLER,
  TransactionStatus.CANCELLED_BUYER,
  TransactionStatus.CANCELLED_ADMIN,
]);

// Admin-cancel refund preview (04 §8.5 "iade bilgisi" / 03 §8.7 / AD19): the
// item sits in escrow from ITEM_ESCROWED onward → returned to the seller; the
// buyer's payment is held from PAYMENT_RECEIVED onward → refunded to the buyer.
const ITEM_ESCROWED_STATES: ReadonlySet<TransactionStatus> = new Set([
  TransactionStatus.ITEM_ESCROWED,
  TransactionStatus.PAYMENT_RECEIVED,
  TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
]);
const PAYMENT_HELD_STATES: ReadonlySet<TransactionStatus> = new Set([
  TransactionStatus.PAYMENT_RECEIVED,
  TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
]);

type Action =
  | { kind: "cancel" }
  | { kind: "hold" }
  | { kind: "release"; releaseAction: EmergencyHoldReleaseAction }
  | { kind: "approveFlag" }
  | { kind: "rejectFlag" };

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
      <h2 className="mb-3 text-sm font-semibold text-gray-900">{title}</h2>
      {children}
    </section>
  );
}

function DescRow({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="flex items-start justify-between gap-3 py-1 text-sm">
      <dt className="text-gray-500">{label}</dt>
      <dd className="text-right font-medium text-gray-900">{value}</dd>
    </div>
  );
}

function TxHashLink({ hash }: { hash: string }) {
  return (
    <a
      href={`${TRONSCAN_TX}${hash}`}
      target="_blank"
      rel="noopener noreferrer"
      className="break-all font-mono text-xs text-blue-600 hover:text-blue-700"
    >
      {hash}
    </a>
  );
}

export interface TransactionDetailViewProps {
  transaction: AdminTransactionDetail;
  onRefetch: () => void;
}

/**
 * S16 admin transaction detail (04 §8.5). Renders the eight admin-only AD7
 * sections plus the role/state-aware action rail (03 §8.7 cancel, 03 §8.8
 * emergency hold / release, plus flag-resolution for FLAGGED). Consumes the
 * AD7 `AdminTransactionDetail` DTO directly — the user-facing S07 components
 * are keyed to a different (party-perspective) shape, so this view is bespoke
 * while still reusing the shared `StatusBadge` + format helpers + the
 * `FlagActionModal` confirm/required-reason dialog.
 */
export function TransactionDetailView({ transaction: tx, onRefetch }: TransactionDetailViewProps) {
  const t = useTranslations("adminTransactions");
  const td = useTranslations("adminTransactions.detail");
  const locale = useLocale();

  const cancelTx = useCancelTransaction();
  const applyHold = useApplyEmergencyHold();
  const releaseHold = useReleaseEmergencyHold();
  const approveFlag = useApproveFlag();
  const rejectFlag = useRejectFlag();

  const [action, setAction] = useState<Action | null>(null);
  const [resultMessage, setResultMessage] = useState<string | null>(null);

  const sym = tx.stablecoin;
  const isTerminal = TERMINAL_STATES.has(tx.status);
  const isFlagged = tx.status === TransactionStatus.FLAGGED;
  const isDelivered = tx.status === TransactionStatus.ITEM_DELIVERED;
  const headerStatus: ExtendedStatus = tx.isOnHold ? "EMERGENCY_HOLD" : tx.status;
  const pendingFlagId = tx.flagHistory.find((f) => f.reviewStatus === "PENDING")?.id ?? null;
  const isSanctionsHold = tx.isOnHold && /sanction/i.test(tx.emergencyHoldReason ?? "");

  const actionPending =
    cancelTx.isPending ||
    applyHold.isPending ||
    releaseHold.isPending ||
    approveFlag.isPending ||
    rejectFlag.isPending;
  const actionError =
    cancelTx.isError ||
    applyHold.isError ||
    releaseHold.isError ||
    approveFlag.isError ||
    rejectFlag.isError;

  function done(message: string) {
    setAction(null);
    setResultMessage(message);
    onRefetch();
  }

  function confirmAction(reason?: string) {
    if (!action) return;
    if (action.kind === "cancel" && reason) {
      cancelTx.mutate({ id: tx.id, reason }, { onSuccess: () => done(t("success.cancelled")) });
    } else if (action.kind === "hold" && reason) {
      applyHold.mutate({ id: tx.id, reason }, { onSuccess: () => done(t("success.held")) });
    } else if (action.kind === "release" && reason) {
      releaseHold.mutate(
        { id: tx.id, action: action.releaseAction, note: reason },
        { onSuccess: () => done(t("success.released")) },
      );
    } else if (action.kind === "approveFlag" && pendingFlagId) {
      approveFlag.mutate(
        { id: pendingFlagId },
        { onSuccess: () => done(t("success.flagResolved")) },
      );
    } else if (action.kind === "rejectFlag" && pendingFlagId) {
      rejectFlag.mutate(
        { id: pendingFlagId },
        { onSuccess: () => done(t("success.flagResolved")) },
      );
    }
  }

  // ── Modal config per action ────────────────────────────────────────────
  let modalTitle = "";
  let modalDescription = "";
  let modalConfirm = "";
  let modalTone: FlagActionTone = "reject";
  let modalReason: FlagActionReasonConfig | undefined;
  let modalInfo: ReactNode = undefined;

  if (action?.kind === "cancel") {
    modalTitle = t("actions.cancelTx");
    modalDescription = t("confirm.cancelTx");
    modalConfirm = t("actions.cancelTx");
    modalTone = "reject";
    modalReason = {
      label: t("reason.cancelLabel"),
      placeholder: t("reason.cancelPlaceholder"),
      minLength: CANCEL_REASON_MIN,
      tooShort: t("reason.minChars", { count: CANCEL_REASON_MIN }),
    };
    // 04 §8.5 / 03 §8.7 — show what will be returned to whom before confirming.
    const itemEscrowed = ITEM_ESCROWED_STATES.has(tx.status);
    const paymentHeld = PAYMENT_HELD_STATES.has(tx.status);
    modalInfo = (
      <div className="rounded-md border border-amber-200 bg-amber-50 px-3 py-2 text-sm">
        <p className="mb-1 font-medium text-amber-800">{t("cancelRefund.title")}</p>
        <ul className="list-disc space-y-0.5 pl-4 text-amber-900">
          {itemEscrowed && <li>{t("cancelRefund.itemToSeller")}</li>}
          {paymentHeld && <li>{t("cancelRefund.paymentToBuyer")}</li>}
          {!itemEscrowed && <li>{t("cancelRefund.none")}</li>}
        </ul>
      </div>
    );
  } else if (action?.kind === "hold") {
    modalTitle = t("actions.emergencyHold");
    modalDescription = t("confirm.emergencyHold");
    modalConfirm = t("actions.emergencyHold");
    modalTone = "hold";
    modalReason = {
      label: t("reason.holdLabel"),
      placeholder: t("reason.holdPlaceholder"),
      minLength: HOLD_REASON_MIN,
      tooShort: t("reason.minChars", { count: HOLD_REASON_MIN }),
    };
  } else if (action?.kind === "release") {
    const resume = action.releaseAction === "RESUME";
    modalTitle = resume ? t("actions.releaseResume") : t("actions.releaseCancel");
    modalDescription = resume ? t("confirm.releaseResume") : t("confirm.releaseCancel");
    modalConfirm = modalTitle;
    modalTone = resume ? "approve" : "reject";
    modalReason = {
      label: t("reason.releaseLabel"),
      placeholder: t("reason.releasePlaceholder"),
      minLength: RELEASE_NOTE_MIN,
      tooShort: t("reason.minChars", { count: RELEASE_NOTE_MIN }),
    };
  } else if (action?.kind === "approveFlag") {
    modalTitle = t("actions.continueFlag");
    modalDescription = t("confirm.continueFlag");
    modalConfirm = t("actions.continueFlag");
    modalTone = "approve";
  } else if (action?.kind === "rejectFlag") {
    modalTitle = t("actions.cancelFlag");
    modalDescription = t("confirm.cancelFlag");
    modalConfirm = t("actions.cancelFlag");
    modalTone = "reject";
  }

  function renderParty(party: AdminTransactionParty | null) {
    if (!party) return <p className="text-sm text-gray-400">{td("noBuyer")}</p>;
    return (
      <Link
        href={`/${locale}/admin/users/${encodeURIComponent(party.steamId)}`}
        className="inline-flex items-center gap-2 hover:underline"
      >
        {party.avatarUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img src={party.avatarUrl} alt="" className="h-8 w-8 rounded-full bg-gray-100" />
        ) : (
          <span className="h-8 w-8 rounded-full bg-gray-200" aria-hidden="true" />
        )}
        <span className="flex flex-col">
          <span className="text-sm font-medium text-gray-900">{party.displayName}</span>
          <span className="font-mono text-xs text-gray-500">{party.steamId}</span>
        </span>
      </Link>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex flex-col gap-2">
        <Link
          href={`/${locale}/admin/transactions`}
          className="text-sm font-medium text-blue-600 hover:text-blue-700"
        >
          {td("back")}
        </Link>
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="font-mono text-xl font-semibold text-gray-900">#{tx.id.slice(0, 8)}</h1>
          <StatusBadge status={headerStatus} />
          <span className="text-sm text-gray-500">{formatDateTime(tx.createdAt, locale)}</span>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[2fr_1fr]">
        {/* Main column */}
        <div className="flex flex-col gap-6">
          {/* 1 — Transaction info */}
          <Section title={td("info")}>
            <div className="flex flex-col gap-3">
              <div className="flex items-center gap-3">
                {tx.itemImageUrl ? (
                  // eslint-disable-next-line @next/next/no-img-element
                  <img
                    src={tx.itemImageUrl}
                    alt=""
                    className="h-12 w-16 rounded bg-gray-100 object-cover"
                  />
                ) : (
                  <span className="h-12 w-16 rounded bg-gray-200" aria-hidden="true" />
                )}
                <div className="flex flex-col">
                  <span className="text-sm font-medium text-gray-900">{tx.itemName}</span>
                  {tx.itemExterior && (
                    <span className="text-xs text-gray-500">{tx.itemExterior}</span>
                  )}
                </div>
              </div>
              <dl>
                <DescRow label={td("price")} value={formatStablecoin(tx.price, sym)} />
                <DescRow
                  label={td("commission")}
                  value={formatStablecoin(tx.commissionAmount, sym)}
                />
                <DescRow label={td("total")} value={formatStablecoin(tx.totalAmount, sym)} />
                <DescRow label={td("stablecoin")} value={sym} />
                <DescRow
                  label={td("paymentTimeout")}
                  value={td("paymentTimeoutMinutes", { count: tx.paymentTimeoutMinutes })}
                />
              </dl>
            </div>
          </Section>

          {/* 2 — Status history timeline */}
          <Section title={td("statusHistory")}>
            {tx.statusHistory.length === 0 ? (
              <p className="text-sm text-gray-500">{td("noStatusHistory")}</p>
            ) : (
              <ol className="flex flex-col gap-2">
                {tx.statusHistory.map((h, i) => (
                  <li key={`${h.changedAt}-${i}`} className="flex flex-col gap-0.5 text-sm">
                    <div className="flex flex-wrap items-center gap-2">
                      <time dateTime={h.changedAt} className="tabular-nums text-xs text-gray-500">
                        {formatDateTime(h.changedAt, locale)}
                      </time>
                      <span className="text-gray-900">
                        {h.fromStatus ? `${h.fromStatus} → ${h.toStatus}` : h.toStatus}
                      </span>
                    </div>
                    <span className="text-xs text-gray-400">
                      {td("trigger")}: {h.trigger}
                    </span>
                  </li>
                ))}
              </ol>
            )}
          </Section>

          {/* 3 — Party details */}
          <Section title={td("parties")}>
            <div className="flex flex-col gap-4">
              <div>
                <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-500">
                  {td("seller")}
                </p>
                {renderParty(tx.seller)}
              </div>
              <div>
                <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-500">
                  {td("buyer")}
                </p>
                {renderParty(tx.buyer)}
              </div>
            </div>
          </Section>

          {/* 4 — Payment details */}
          {tx.paymentDetail && (
            <Section title={td("payment")}>
              <dl>
                {tx.paymentDetail.paymentAddress && (
                  <DescRow
                    label={td("paymentAddress")}
                    value={
                      <span className="break-all font-mono text-xs">
                        {tx.paymentDetail.paymentAddress}
                      </span>
                    }
                  />
                )}
                <DescRow
                  label={td("receivedAmount")}
                  value={formatStablecoin(tx.paymentDetail.receivedAmount, sym)}
                />
                {tx.paymentDetail.receivedTxHash && (
                  <DescRow
                    label={td("txHash")}
                    value={<TxHashLink hash={tx.paymentDetail.receivedTxHash} />}
                  />
                )}
                <DescRow label={td("confirmations")} value={tx.paymentDetail.blockConfirmations} />
                {tx.paymentDetail.confirmedAt && (
                  <DescRow
                    label={td("confirmedAt")}
                    value={formatDateTime(tx.paymentDetail.confirmedAt, locale)}
                  />
                )}
              </dl>
            </Section>
          )}

          {/* 5 — Seller payout details */}
          {tx.sellerPayoutDetail && (
            <Section title={td("payout")}>
              <dl>
                <DescRow
                  label={td("payoutGross")}
                  value={formatStablecoin(tx.sellerPayoutDetail.grossAmount, sym)}
                />
                <DescRow
                  label={td("payoutCommission")}
                  value={formatStablecoin(tx.sellerPayoutDetail.commission, sym)}
                />
                {tx.sellerPayoutDetail.gasFee !== null && (
                  <DescRow
                    label={td("payoutGasFee")}
                    value={formatStablecoin(tx.sellerPayoutDetail.gasFee, sym)}
                  />
                )}
                <DescRow
                  label={td("payoutNet")}
                  value={formatStablecoin(tx.sellerPayoutDetail.netAmount, sym)}
                />
                {tx.sellerPayoutDetail.txHash && (
                  <DescRow
                    label={td("txHash")}
                    value={<TxHashLink hash={tx.sellerPayoutDetail.txHash} />}
                  />
                )}
                {tx.sellerPayoutDetail.sentAt && (
                  <DescRow
                    label={td("payoutSentAt")}
                    value={formatDateTime(tx.sellerPayoutDetail.sentAt, locale)}
                  />
                )}
              </dl>
            </Section>
          )}

          {/* 6 — Refund details */}
          {tx.refundDetail && (
            <Section title={td("refund")}>
              <dl>
                <DescRow
                  label={td("refundOriginal")}
                  value={formatStablecoin(tx.refundDetail.originalAmount, sym)}
                />
                {tx.refundDetail.gasFee !== null && (
                  <DescRow
                    label={td("refundGasFee")}
                    value={formatStablecoin(tx.refundDetail.gasFee, sym)}
                  />
                )}
                <DescRow
                  label={td("refundNet")}
                  value={formatStablecoin(tx.refundDetail.netRefundAmount, sym)}
                />
                {tx.refundDetail.refundAddress && (
                  <DescRow
                    label={td("refundAddress")}
                    value={
                      <span className="break-all font-mono text-xs">
                        {tx.refundDetail.refundAddress}
                      </span>
                    }
                  />
                )}
                {tx.refundDetail.txHash && (
                  <DescRow
                    label={td("txHash")}
                    value={<TxHashLink hash={tx.refundDetail.txHash} />}
                  />
                )}
                {tx.refundDetail.refundedAt && (
                  <DescRow
                    label={td("refundedAt")}
                    value={formatDateTime(tx.refundDetail.refundedAt, locale)}
                  />
                )}
              </dl>
            </Section>
          )}

          {/* 7 — Notification history */}
          <Section title={td("notifications")}>
            {tx.notificationHistory.length === 0 ? (
              <p className="text-sm text-gray-500">{td("noNotifications")}</p>
            ) : (
              <ul className="flex flex-col gap-2">
                {tx.notificationHistory.map((n, i) => (
                  <li
                    key={`${n.type}-${i}`}
                    className="flex flex-col gap-0.5 rounded border border-gray-100 p-2 text-sm"
                  >
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-medium text-gray-900">{n.type}</span>
                      <time dateTime={n.sentAt} className="text-xs text-gray-500">
                        {formatDateTime(n.sentAt, locale)}
                      </time>
                    </div>
                    <span className="text-xs text-gray-600">
                      {td("recipient")}: {n.recipient}
                      {n.channels.length > 0 && ` · ${n.channels.join(", ")}`}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Section>

          {/* 8 — Dispute history */}
          {tx.disputeHistory.length > 0 && (
            <Section title={td("disputes")}>
              <ul className="flex flex-col gap-2">
                {tx.disputeHistory.map((d) => (
                  <li
                    key={d.id}
                    className="flex flex-col gap-0.5 rounded border border-gray-100 p-2 text-sm"
                  >
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium text-gray-900">{d.type}</span>
                      <span className="rounded bg-gray-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-gray-600">
                        {d.status}
                      </span>
                    </div>
                    {d.autoCheckResult && (
                      <span className="text-xs text-gray-600">
                        {td("autoCheck")}: {d.autoCheckResult}
                      </span>
                    )}
                    <span className="text-xs text-gray-400">
                      {formatDateTime(d.escalatedAt, locale)}
                      {d.closedAt && ` → ${formatDateTime(d.closedAt, locale)}`}
                    </span>
                  </li>
                ))}
              </ul>
            </Section>
          )}

          {/* 9 — Flag history */}
          {tx.flagHistory.length > 0 && (
            <Section title={td("flags")}>
              <ul className="flex flex-col gap-2">
                {tx.flagHistory.map((f) => (
                  <li
                    key={f.id}
                    className="flex flex-col gap-0.5 rounded border border-gray-100 p-2 text-sm"
                  >
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium text-gray-900">{f.type}</span>
                      <span className="rounded bg-gray-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-gray-600">
                        {f.reviewStatus}
                      </span>
                      {f.reviewedAt && (
                        <time dateTime={f.reviewedAt} className="text-xs text-gray-500">
                          {formatDateTime(f.reviewedAt, locale)}
                        </time>
                      )}
                    </div>
                    {f.adminNote && <span className="text-xs text-gray-600">{f.adminNote}</span>}
                  </li>
                ))}
              </ul>
            </Section>
          )}
        </div>

        {/* Action rail */}
        <div className="flex flex-col gap-6">
          {/* Emergency-hold info */}
          {tx.isOnHold && (
            <Section title={td("holdInfo")}>
              <dl>
                {isSanctionsHold && (
                  <p className="mb-2 rounded bg-red-100 px-2 py-1 text-xs font-semibold uppercase text-red-700">
                    {td("autoHoldSanctions")}
                  </p>
                )}
                {tx.emergencyHoldReason && (
                  <DescRow label={td("holdReason")} value={tx.emergencyHoldReason} />
                )}
                {tx.emergencyHoldAt && (
                  <DescRow
                    label={td("heldAt")}
                    value={formatDateTime(tx.emergencyHoldAt, locale)}
                  />
                )}
              </dl>
            </Section>
          )}

          {isTerminal ? (
            <Section title={td("actions")}>
              <p className="text-sm text-gray-500">{td("readOnly")}</p>
            </Section>
          ) : (
            <Section title={td("actions")}>
              <div className="flex flex-col gap-2">
                {/* FLAGGED — flag-resolution actions (S14 terminology), general cancel hidden */}
                {isFlagged && pendingFlagId && (
                  <>
                    <button
                      type="button"
                      onClick={() => setAction({ kind: "approveFlag" })}
                      disabled={actionPending}
                      className="rounded-md bg-emerald-600 px-3 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
                    >
                      {t("actions.continueFlag")}
                    </button>
                    <button
                      type="button"
                      onClick={() => setAction({ kind: "rejectFlag" })}
                      disabled={actionPending}
                      className="rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
                    >
                      {t("actions.cancelFlag")}
                    </button>
                  </>
                )}

                {/* General admin cancel — CREATED…TRADE_OFFER_SENT_TO_BUYER, not flagged/held */}
                {!isFlagged && !isDelivered && !tx.isOnHold && tx.adminActions.canCancel && (
                  <button
                    type="button"
                    onClick={() => setAction({ kind: "cancel" })}
                    disabled={actionPending}
                    className="rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
                  >
                    {t("actions.cancelTx")}
                  </button>
                )}

                {/* ITEM_DELIVERED — standard cancel impossible; exceptional resolution deferred */}
                {isDelivered && !tx.isOnHold && (
                  <button
                    type="button"
                    disabled
                    title={t("actions.exceptionalResolutionHint")}
                    className="inline-flex items-center justify-center gap-2 rounded-md bg-orange-500/60 px-3 py-2 text-sm font-medium text-white"
                  >
                    {t("actions.exceptionalResolution")}
                    <span className="rounded bg-white/30 px-1.5 py-0.5 text-[10px] font-semibold uppercase">
                      {t("actions.deferred")}
                    </span>
                  </button>
                )}

                {/* Emergency hold — any active state, not already held */}
                {!tx.isOnHold && (
                  <button
                    type="button"
                    onClick={() => setAction({ kind: "hold" })}
                    disabled={actionPending}
                    className="rounded-md bg-amber-500 px-3 py-2 text-sm font-medium text-white hover:bg-amber-600 disabled:opacity-50"
                  >
                    {t("actions.emergencyHold")}
                  </button>
                )}

                {/* Release hold — RESUME always; CANCEL deferred to exceptional resolution at ITEM_DELIVERED */}
                {tx.isOnHold && (
                  <>
                    <button
                      type="button"
                      onClick={() => setAction({ kind: "release", releaseAction: "RESUME" })}
                      disabled={actionPending}
                      className="rounded-md bg-emerald-600 px-3 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
                    >
                      {t("actions.releaseResume")}
                    </button>
                    {isDelivered ? (
                      <button
                        type="button"
                        disabled
                        title={t("actions.exceptionalResolutionHint")}
                        className="inline-flex items-center justify-center gap-2 rounded-md bg-orange-500/60 px-3 py-2 text-sm font-medium text-white"
                      >
                        {t("actions.releaseCancel")}
                        <span className="rounded bg-white/30 px-1.5 py-0.5 text-[10px] font-semibold uppercase">
                          {t("actions.deferred")}
                        </span>
                      </button>
                    ) : (
                      <button
                        type="button"
                        onClick={() => setAction({ kind: "release", releaseAction: "CANCEL" })}
                        disabled={actionPending}
                        className="rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
                      >
                        {t("actions.releaseCancel")}
                      </button>
                    )}
                  </>
                )}

                {actionError && <p className="text-sm text-red-600">{t("actionError")}</p>}
                {resultMessage && <p className="text-sm text-emerald-700">{resultMessage}</p>}
              </div>
            </Section>
          )}
        </div>
      </div>

      <FlagActionModal
        open={action !== null}
        title={modalTitle}
        description={modalDescription}
        confirmLabel={modalConfirm}
        cancelLabel={t("confirm.dismiss")}
        tone={modalTone}
        reason={modalReason}
        infoBlock={modalInfo}
        pending={actionPending}
        onConfirm={confirmAction}
        onClose={() => setAction(null)}
      />
    </div>
  );
}
