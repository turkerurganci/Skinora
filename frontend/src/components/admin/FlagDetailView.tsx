"use client";

import Link from "next/link";
import { useState, type ReactNode } from "react";
import { useLocale, useTranslations } from "next-intl";
import { StatusBadge, UserCard } from "@/components/common";
import type { UserCardUser } from "@/components/common";
import { formatDateTime, formatStablecoin } from "@/lib/utils/format";
import type {
  AbnormalBehaviorFlagDetail,
  AdminFlagDetail,
  AdminFlagPartyDetail,
  DeliveryReversedFlagDetail,
  HighVolumeFlagDetail,
  MultiAccountFlagDetail,
  PriceDeviationFlagDetail,
} from "@/lib/api/admin";
import {
  useApproveFlag,
  useHoldUserTransactions,
  useRejectFlag,
} from "@/lib/hooks/useAdminFlagMutations";
import {
  FlagActionModal,
  type FlagActionReasonConfig,
  type FlagActionTone,
} from "./FlagActionModal";
import { FlagReviewStatusBadge } from "./FlagReviewStatusBadge";
import { tDynamicOrKey } from "@/lib/i18n/dynamicKey";

const HOLD_REASON_MIN_LENGTH = 10;

type ActionKind = "approve" | "reject" | "hold";

function toUserCardUser(party: AdminFlagPartyDetail): UserCardUser {
  return {
    steamId: party.steamId,
    username: party.displayName,
    avatarUrl: party.avatarUrl ?? undefined,
    reputationScore: party.reputationScore,
    completedTransactions: party.completedTransactionCount,
    accountAgeText: party.accountAge,
  };
}

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

export interface FlagDetailViewProps {
  flag: AdminFlagDetail;
}

/** S14 flag-detail view — transaction-flag + account-flag variants (04 §8.3). */
export function FlagDetailView({ flag }: FlagDetailViewProps) {
  const t = useTranslations("adminFlags");
  const tType = useTranslations("adminFlags.type");
  const locale = useLocale();

  const approve = useApproveFlag();
  const reject = useRejectFlag();
  const hold = useHoldUserTransactions();

  const [adminNote, setAdminNote] = useState("");
  const [action, setAction] = useState<ActionKind | null>(null);
  const [holdMessage, setHoldMessage] = useState<string | null>(null);

  const isAccount = flag.scope === "ACCOUNT_LEVEL";
  const isPending = flag.reviewStatus === "PENDING";
  const note = adminNote.trim().length > 0 ? adminNote.trim() : undefined;
  const actionPending = approve.isPending || reject.isPending || hold.isPending;
  const actionError = approve.isError || reject.isError || hold.isError;

  function confirmAction(reason?: string) {
    if (action === "approve") {
      approve.mutate({ id: flag.id, note }, { onSuccess: () => setAction(null) });
    } else if (action === "reject") {
      reject.mutate({ id: flag.id, note }, { onSuccess: () => setAction(null) });
    } else if (action === "hold" && reason) {
      hold.mutate(
        { userId: flag.userId, reason },
        {
          onSuccess: (res) => {
            setAction(null);
            setHoldMessage(t("hold.success", { count: res.heldCount }));
          },
        },
      );
    }
  }

  // ── Modal config per action ────────────────────────────────────────────
  const approveLabel = isAccount ? t("actions.removeFlag") : t("actions.continue");
  let modalTitle = "";
  let modalDescription = "";
  let modalConfirm = "";
  let modalTone: FlagActionTone = "approve";
  let modalReason: FlagActionReasonConfig | undefined;

  if (action === "approve") {
    modalTitle = approveLabel;
    modalDescription = isAccount ? t("confirm.removeFlag") : t("confirm.continue");
    modalConfirm = approveLabel;
    modalTone = "approve";
  } else if (action === "reject") {
    modalTitle = t("actions.cancelTx");
    modalDescription = t("confirm.cancelTx");
    modalConfirm = t("actions.cancelTx");
    modalTone = "reject";
  } else if (action === "hold") {
    modalTitle = t("actions.hold");
    modalDescription = t("confirm.hold");
    modalConfirm = t("actions.hold");
    modalTone = "hold";
    modalReason = {
      label: t("hold.reasonLabel"),
      placeholder: t("hold.reasonPlaceholder"),
      minLength: HOLD_REASON_MIN_LENGTH,
      tooShort: t("hold.reasonTooShort", { count: HOLD_REASON_MIN_LENGTH }),
      hint: t("hold.reasonHint"),
    };
  }

  // ── flagDetail rendering (07 §9.3) ─────────────────────────────────────
  function renderFlagDetail() {
    const d = flag.flagDetail;
    if (!d) return <p className="text-sm text-gray-500">{t("detail.noSignal")}</p>;

    if (flag.type === "PRICE_DEVIATION") {
      const p = d as PriceDeviationFlagDetail;
      const sym = flag.transaction?.stablecoin ?? "USDT";
      return (
        <dl>
          <DescRow label={t("detail.inputPrice")} value={formatStablecoin(p.inputPrice, sym)} />
          <DescRow label={t("detail.marketPrice")} value={formatStablecoin(p.marketPrice, sym)} />
          <DescRow label={t("detail.deviationPercent")} value={`%${p.deviationPercent}`} />
        </dl>
      );
    }
    if (flag.type === "HIGH_VOLUME") {
      const p = d as HighVolumeFlagDetail;
      return (
        <dl>
          <DescRow label={t("detail.periodHours")} value={`${p.periodHours}`} />
          <DescRow label={t("detail.transactionCount")} value={`${p.transactionCount}`} />
          <DescRow
            label={t("detail.totalVolume")}
            value={formatStablecoin(p.totalVolume, "USDT")}
          />
        </dl>
      );
    }
    if (flag.type === "ABNORMAL_BEHAVIOR") {
      const p = d as AbnormalBehaviorFlagDetail;
      return (
        <dl>
          <DescRow label={t("detail.pattern")} value={p.pattern} />
          <DescRow label={t("detail.description")} value={p.description} />
        </dl>
      );
    }
    if (flag.type === "MULTI_ACCOUNT") {
      const p = d as MultiAccountFlagDetail;
      return (
        <div className="flex flex-col gap-3">
          <dl>
            <DescRow label={t("detail.matchType")} value={p.matchType} />
            <DescRow
              label={t("detail.matchValue")}
              value={<span className="break-all font-mono text-xs">{p.matchValue}</span>}
            />
          </dl>
          {p.linkedAccounts.length > 0 && (
            <div>
              <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-500">
                {t("detail.linkedAccounts")}
              </p>
              <ul className="flex flex-col gap-1">
                {p.linkedAccounts.map((a) => (
                  <li key={a.steamId} className="text-sm text-gray-900">
                    {a.displayName}{" "}
                    <span className="font-mono text-xs text-gray-500">({a.steamId})</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {p.supportingSignals && p.supportingSignals.length > 0 && (
            <div>
              <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-500">
                {t("detail.supportingSignals")}
              </p>
              <ul className="flex flex-col gap-2">
                {p.supportingSignals.map((s, i) => (
                  <li
                    key={`${s.type}-${i}`}
                    className="rounded border border-gray-100 bg-gray-50 p-2"
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs font-semibold text-gray-700">
                        {tDynamicOrKey(t, `signalType.${s.type}`)}
                      </span>
                      <span className="break-all font-mono text-xs text-gray-500">{s.value}</span>
                    </div>
                    {s.linkedAccounts.length > 0 && (
                      <ul className="mt-1 flex flex-col gap-0.5">
                        {s.linkedAccounts.map((a) => (
                          <li key={a.steamId} className="text-xs text-gray-700">
                            {a.displayName}{" "}
                            <span className="font-mono text-gray-400">({a.steamId})</span>
                          </li>
                        ))}
                      </ul>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      );
    }
    if (flag.type === "DELIVERY_REVERSED") {
      const p = d as DeliveryReversedFlagDetail;
      // Account-level flag, so the reversed transaction lives in the payload
      // (06 §3.12) — link it so a repeat offender can be traced (02 §14.2).
      const counts =
        typeof p.observedClassCount === "number" && typeof p.expectedClassCount === "number"
          ? `${p.observedClassCount} / ${p.expectedClassCount}`
          : null;
      return (
        <div className="flex flex-col gap-3">
          <dl>
            <DescRow
              label={t("detail.reversedTransaction")}
              value={
                <Link
                  href={`/${locale}/admin/transactions/${p.transactionId}`}
                  className="break-all font-mono text-xs text-blue-600 hover:text-blue-700"
                >
                  {p.transactionId}
                </Link>
              }
            />
            <DescRow label={t("detail.item")} value={p.itemName ?? "—"} />
            <DescRow
              label={t("detail.deliveredAt")}
              value={p.itemDeliveredAt ? formatDateTime(p.itemDeliveredAt, locale) : "—"}
            />
            <DescRow label={t("detail.detectedAt")} value={formatDateTime(p.detectedAt, locale)} />
            <DescRow label={t("detail.buyerVisibility")} value={p.buyerVisibility ?? "—"} />
            <DescRow label={t("detail.sellerVisibility")} value={p.sellerVisibility ?? "—"} />
            {counts && <DescRow label={t("detail.classCount")} value={counts} />}
          </dl>
          {p.detail && (
            <div>
              <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-500">
                {t("detail.description")}
              </p>
              <p className="text-sm text-gray-900">{p.detail}</p>
            </div>
          )}
        </div>
      );
    }
    // SANCTIONS_MATCH (and any future type) — AD3 does not project a typed
    // payload; surface a generic note pointing the admin to the audit trail.
    return <p className="text-sm text-gray-500">{t("detail.sanctionsNote")}</p>;
  }

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex flex-col gap-2">
        <Link
          href={`/${locale}/admin/flags`}
          className="text-sm font-medium text-blue-600 hover:text-blue-700"
        >
          {t("detail.back")}
        </Link>
        <div className="flex flex-wrap items-center gap-2">
          <h1 className="text-xl font-semibold text-gray-900">{tType(flag.type)}</h1>
          <FlagReviewStatusBadge status={flag.reviewStatus} />
          <span className="text-sm text-gray-500">{formatDateTime(flag.createdAt, locale)}</span>
        </div>
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-[2fr_1fr]">
        <div className="flex flex-col gap-6">
          {/* Flag / signal info */}
          <Section title={t("detail.flagInfo")}>{renderFlagDetail()}</Section>

          {/* Transaction details — transaction-flag variant */}
          {flag.transaction && (
            <Section title={t("detail.transactionInfo")}>
              <div className="flex flex-col gap-3">
                <div className="flex items-center gap-3">
                  {flag.transaction.itemImageUrl ? (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={flag.transaction.itemImageUrl}
                      alt=""
                      className="h-12 w-16 rounded bg-gray-100 object-cover"
                    />
                  ) : (
                    <span className="h-12 w-16 rounded bg-gray-200" aria-hidden="true" />
                  )}
                  <span className="text-sm font-medium text-gray-900">
                    {flag.transaction.itemName}
                  </span>
                </div>
                <dl>
                  <DescRow
                    label={t("detail.amount")}
                    value={formatStablecoin(flag.transaction.price, flag.transaction.stablecoin)}
                  />
                  <DescRow
                    label={t("detail.status")}
                    value={<StatusBadge status={flag.transaction.status} />}
                  />
                  <DescRow
                    label={t("detail.timeout")}
                    value={t("detail.timeoutHours", {
                      count: flag.transaction.paymentTimeoutHours,
                    })}
                  />
                  <DescRow
                    label={t("detail.createdAt")}
                    value={formatDateTime(flag.transaction.createdAt, locale)}
                  />
                </dl>
              </div>
            </Section>
          )}

          {/* Parties */}
          <Section title={t("detail.parties")}>
            <div className="flex flex-col gap-4">
              <div>
                <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-500">
                  {isAccount ? t("detail.user") : t("detail.seller")}
                </p>
                {flag.seller ? (
                  <UserCard user={toUserCardUser(flag.seller)} variant="detailed" />
                ) : (
                  <p className="text-sm text-gray-400">—</p>
                )}
              </div>
              {flag.buyer && (
                <div>
                  <p className="mb-1 text-xs font-medium uppercase tracking-wide text-gray-500">
                    {t("detail.buyer")}
                  </p>
                  <UserCard user={toUserCardUser(flag.buyer)} variant="detailed" />
                </div>
              )}
              <DescRow
                label={t("detail.historicalCount")}
                value={flag.historicalTransactionCount}
              />
            </div>
          </Section>

          {/* Active transactions — account-flag variant (04 §8.3 madde 4) */}
          {isAccount && (
            <Section
              title={t("detail.activeTransactions", {
                count: flag.activeTransactions.length,
              })}
            >
              {flag.activeTransactions.length === 0 ? (
                <p className="text-sm text-gray-500">{t("detail.noActiveTransactions")}</p>
              ) : (
                <ul className="flex flex-col gap-2">
                  {flag.activeTransactions.map((tx) => (
                    <li
                      key={tx.id}
                      className="flex flex-col gap-1 rounded border border-gray-100 p-2 sm:flex-row sm:items-center sm:justify-between"
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="text-sm font-medium text-gray-900">{tx.itemName}</span>
                        <span className="rounded bg-gray-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-gray-600">
                          {t(`role.${tx.role}`)}
                        </span>
                        {tx.isOnHold && (
                          <span className="rounded bg-red-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase text-red-700">
                            {t("detail.onHold")}
                          </span>
                        )}
                      </div>
                      <div className="flex flex-wrap items-center gap-3 text-xs text-gray-600">
                        <StatusBadge status={tx.status} />
                        <span className="tabular-nums">
                          {formatStablecoin(tx.price, tx.stablecoin)}
                        </span>
                        <time dateTime={tx.createdAt}>{formatDateTime(tx.createdAt, locale)}</time>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </Section>
          )}
        </div>

        {/* Action rail */}
        <div className="flex flex-col gap-6">
          {isPending ? (
            <Section title={t("detail.actions")}>
              <div className="flex flex-col gap-3">
                <label className="flex flex-col gap-1 text-sm">
                  <span className="font-medium text-gray-700">{t("adminNote.label")}</span>
                  <textarea
                    value={adminNote}
                    onChange={(e) => setAdminNote(e.target.value)}
                    placeholder={t("adminNote.placeholder")}
                    rows={3}
                    className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-200"
                  />
                </label>

                {!isAccount && (
                  <div className="flex flex-col gap-2">
                    <button
                      type="button"
                      onClick={() => setAction("approve")}
                      disabled={actionPending}
                      className="rounded-md bg-emerald-600 px-3 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
                    >
                      {t("actions.continue")}
                    </button>
                    <button
                      type="button"
                      onClick={() => setAction("reject")}
                      disabled={actionPending}
                      className="rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
                    >
                      {t("actions.cancelTx")}
                    </button>
                  </div>
                )}

                {isAccount && (
                  <div className="flex flex-col gap-2">
                    <button
                      type="button"
                      onClick={() => setAction("approve")}
                      disabled={actionPending}
                      className="rounded-md bg-emerald-600 px-3 py-2 text-sm font-medium text-white hover:bg-emerald-700 disabled:opacity-50"
                    >
                      {t("actions.removeFlag")}
                    </button>
                    {/* Askıya Al — deferred to the dedicated account-suspension task */}
                    <button
                      type="button"
                      disabled
                      title={t("actions.suspendDeferredHint")}
                      className="inline-flex items-center justify-center gap-2 rounded-md bg-amber-500/60 px-3 py-2 text-sm font-medium text-white"
                    >
                      {t("actions.suspend")}
                      <span className="rounded bg-white/30 px-1.5 py-0.5 text-[10px] font-semibold uppercase">
                        {t("actions.suspendDeferred")}
                      </span>
                    </button>
                    <button
                      type="button"
                      onClick={() => setAction("hold")}
                      disabled={actionPending}
                      className="rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
                    >
                      {t("actions.hold")}
                    </button>
                  </div>
                )}

                {actionError && <p className="text-sm text-red-600">{t("actionError")}</p>}
                {holdMessage && <p className="text-sm text-emerald-700">{holdMessage}</p>}
              </div>
            </Section>
          ) : (
            <Section title={t("detail.reviewInfo")}>
              <dl>
                <DescRow
                  label={t("detail.decision")}
                  value={<FlagReviewStatusBadge status={flag.reviewStatus} />}
                />
                {flag.reviewedAt && (
                  <DescRow
                    label={t("detail.reviewedAt")}
                    value={formatDateTime(flag.reviewedAt, locale)}
                  />
                )}
                {flag.adminNote && <DescRow label={t("adminNote.label")} value={flag.adminNote} />}
              </dl>
              {holdMessage && <p className="mt-2 text-sm text-emerald-700">{holdMessage}</p>}
            </Section>
          )}

          {/* Hold can still be applied after review (account-flag enforcement). */}
          {!isPending && isAccount && (
            <Section title={t("detail.enforcement")}>
              <button
                type="button"
                onClick={() => setAction("hold")}
                disabled={actionPending}
                className="w-full rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
              >
                {t("actions.hold")}
              </button>
              {actionError && <p className="mt-2 text-sm text-red-600">{t("actionError")}</p>}
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
        pending={actionPending}
        onConfirm={confirmAction}
        onClose={() => setAction(null)}
      />
    </div>
  );
}
