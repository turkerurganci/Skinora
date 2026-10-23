"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { ErrorState, Skeleton } from "@/components/common";
import type {
  DepositRefundHeldRow,
  DepositRefundLimit,
  OutboundApprovalRow,
} from "@/lib/api/admin";
import { walletErrorMessage } from "@/lib/admin/walletErrors";
import { tDynamic } from "@/lib/i18n/dynamicKey";
import {
  useApproveOutboundTransfer,
  useOutboundApprovals,
  useReleaseDepositRefundLimit,
} from "@/lib/hooks/useOutboundApprovals";
import { networkLabel, parseNetwork } from "@/lib/utils/blockchain";
import { formatDateTime } from "@/lib/utils/format";
import { AdminDialog } from "./AdminDialog";
import { ChainTokenNote, movedToken } from "./ClosureModals";

const SECONDARY_BUTTON =
  "rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50";
const PRIMARY_BUTTON =
  "rounded-md bg-red-600 px-3 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-50";

function rowNetwork(row: { network: string }): string {
  const network = parseNetwork(row.network);
  return network ? networkLabel(network) : row.network;
}

function ApproveDialog({ row, onClose }: { row: OutboundApprovalRow | null; onClose: () => void }) {
  const t = useTranslations("adminWallets");
  const ta = useTranslations("adminWallets.approvals.confirm");
  const tType = useTranslations("adminWallets.outboundType");
  const approve = useApproveOutboundTransfer();

  return (
    <AdminDialog open={row !== null} title={ta("title")} onClose={onClose}>
      {row && (
        <div className="flex flex-col gap-4">
          <p
            role="alert"
            className="rounded-md border border-red-300 bg-red-50 p-3 text-sm font-medium text-red-800"
          >
            {ta("warning")}
          </p>
          <dl className="flex flex-col gap-1 rounded-md bg-gray-50 p-3 text-sm">
            <div>
              <dt className="text-xs text-gray-500">{ta("network")}</dt>
              <dd className="text-gray-900">{rowNetwork(row)}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{ta("type")}</dt>
              <dd className="text-gray-900">{tDynamic(tType, row.type, row.type)}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{ta("amount")}</dt>
              <dd className="font-semibold tabular-nums text-gray-900">
                {row.amount} {movedToken(row)}
              </dd>
              <ChainTokenNote row={row} />
            </div>
            <div>
              <dt className="text-xs text-gray-500">{ta("to")}</dt>
              <dd className="break-all font-mono text-xs text-gray-900">{row.toAddress}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{ta("transaction")}</dt>
              <dd className="break-all font-mono text-xs text-gray-900">{row.transactionId}</dd>
            </div>
          </dl>
          {approve.isError && (
            <p role="alert" className="text-sm text-red-700">
              {walletErrorMessage(t, approve.error)}
            </p>
          )}
          <div className="flex justify-end gap-2">
            <button type="button" onClick={onClose} className={SECONDARY_BUTTON}>
              {ta("cancel")}
            </button>
            <button
              type="button"
              disabled={approve.isPending}
              onClick={() => approve.mutate(row.blockchainTransactionId, { onSuccess: onClose })}
              className={PRIMARY_BUTTON}
              data-testid="approval-confirm"
            >
              {ta("submit")}
            </button>
          </div>
        </div>
      )}
    </AdminDialog>
  );
}

function ReleaseDialog({
  row,
  onClose,
}: {
  row: DepositRefundHeldRow | null;
  onClose: () => void;
}) {
  const t = useTranslations("adminWallets");
  const tr = useTranslations("adminWallets.approvals.limit.confirm");
  const tType = useTranslations("adminWallets.outboundType");
  const release = useReleaseDepositRefundLimit();

  return (
    <AdminDialog open={row !== null} title={tr("title")} onClose={onClose}>
      {row && (
        <div className="flex flex-col gap-4">
          <p
            role="alert"
            className="rounded-md border border-red-300 bg-red-50 p-3 text-sm font-medium text-red-800"
          >
            {tr("warning")}
          </p>
          <dl className="flex flex-col gap-1 rounded-md bg-gray-50 p-3 text-sm">
            <div>
              <dt className="text-xs text-gray-500">{tr("network")}</dt>
              <dd className="text-gray-900">{rowNetwork(row)}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{tr("type")}</dt>
              <dd className="text-gray-900">{tDynamic(tType, row.type, row.type)}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{tr("amount")}</dt>
              <dd className="font-semibold tabular-nums text-gray-900">
                {row.amount} {row.chainToken ?? "?"}
              </dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{tr("to")}</dt>
              <dd className="break-all font-mono text-xs text-gray-900">{row.toAddress}</dd>
            </div>
            <div>
              <dt className="text-xs text-gray-500">{tr("transaction")}</dt>
              <dd className="break-all font-mono text-xs text-gray-900">{row.transactionId}</dd>
            </div>
          </dl>
          {release.isError && (
            <p role="alert" className="text-sm text-red-700">
              {walletErrorMessage(t, release.error)}
            </p>
          )}
          <div className="flex justify-end gap-2">
            <button type="button" onClick={onClose} className={SECONDARY_BUTTON}>
              {tr("cancel")}
            </button>
            <button
              type="button"
              disabled={release.isPending}
              onClick={() => release.mutate(row.blockchainTransactionId, { onSuccess: onClose })}
              className={PRIMARY_BUTTON}
              data-testid="limit-release-confirm"
            >
              {tr("submit")}
            </button>
          </div>
        </div>
      )}
    </AdminDialog>
  );
}

/**
 * T169a — the deposit refund daily limit (08 §3.3c): every network's last 24
 * hours against its limit, and the refunds it holds. A held refund goes out on
 * its own when the window has room; AD42 lets one go now (audited).
 */
function DepositRefundLimitSection({
  limits,
  held,
}: {
  limits: DepositRefundLimit[];
  held: DepositRefundHeldRow[];
}) {
  const t = useTranslations("adminWallets.approvals.limit");
  const tType = useTranslations("adminWallets.outboundType");
  const locale = useLocale();
  const [releasing, setReleasing] = useState<DepositRefundHeldRow | null>(null);

  return (
    <div className="flex flex-col gap-2 border-t border-red-200 pt-3" data-testid="limit-section">
      <h3 className="text-sm font-semibold text-gray-900">{t("heading")}</h3>
      <ul className="flex flex-col gap-0.5 text-sm text-gray-700">
        {limits.map((limit) => (
          <li key={limit.network} data-testid={`limit-usage-${limit.network}`}>
            {t("usage", {
              network: rowNetwork(limit),
              used: limit.usedLast24hUsdt,
              limit: limit.limitUsdt,
            })}
          </li>
        ))}
      </ul>
      <p className="text-xs text-gray-600">{t("intro")}</p>
      {held.length === 0 ? (
        <p className="text-sm text-gray-500">{t("empty")}</p>
      ) : (
        <div className="overflow-x-auto">
          <table
            className="w-full table-auto border-collapse text-left text-sm"
            aria-label={t("ariaLabel")}
          >
            <thead>
              <tr className="border-b border-gray-200 bg-gray-50 text-xs font-medium uppercase tracking-wide text-gray-500">
                <th scope="col" className="px-3 py-2">
                  {t("columns.network")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.type")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.amount")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.to")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.held")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.action")}
                </th>
              </tr>
            </thead>
            <tbody>
              {held.map((row) => (
                <tr
                  key={row.blockchainTransactionId}
                  className="border-b border-gray-100 last:border-b-0"
                >
                  <td className="px-3 py-2">{rowNetwork(row)}</td>
                  <td className="px-3 py-2">{tDynamic(tType, row.type, row.type)}</td>
                  <td className="px-3 py-2 tabular-nums">
                    {row.amount} {row.chainToken ?? "?"}
                  </td>
                  <td className="break-all px-3 py-2 font-mono text-xs">{row.toAddress}</td>
                  <td className="px-3 py-2">
                    <time dateTime={row.heldAt}>{formatDateTime(row.heldAt, locale)}</time>
                  </td>
                  <td className="px-3 py-2">
                    <button
                      type="button"
                      onClick={() => setReleasing(row)}
                      className="rounded-md border border-red-300 bg-white px-2 py-1 text-xs font-medium text-red-700 hover:bg-red-50"
                      data-testid={`limit-release-${row.blockchainTransactionId}`}
                    >
                      {t("release")}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <ReleaseDialog row={releasing} onClose={() => setReleasing(null)} />
    </div>
  );
}

/**
 * T169 — payouts and refunds waiting for an admin approval while the launch
 * gate `outbound.auto_send_enabled` is closed (07 §9.35 AD40/AD41). One admin
 * approves; the dispatcher then sends the row exactly as it was created.
 */
export function OutboundApprovalPanel() {
  const t = useTranslations("adminWallets.approvals");
  const tAll = useTranslations("adminWallets");
  const tType = useTranslations("adminWallets.outboundType");
  const locale = useLocale();
  const [approving, setApproving] = useState<OutboundApprovalRow | null>(null);
  const queue = useOutboundApprovals();

  return (
    <section
      aria-labelledby="approval-panel-heading"
      data-testid="approval-panel"
      className="flex flex-col gap-3 rounded-lg border border-red-200 bg-red-50/30 p-4"
    >
      <div>
        <h2 id="approval-panel-heading" className="text-base font-semibold text-gray-900">
          {t("heading")}
        </h2>
        {queue.data && (
          <p
            className={`text-sm ${queue.data.autoSendEnabled ? "text-gray-700" : "font-medium text-red-800"}`}
            data-testid="approval-gate-state"
          >
            {queue.data.autoSendEnabled ? t("gateOpen") : t("gateClosed")}
          </p>
        )}
        <p className="text-xs text-gray-600">{t("intro")}</p>
      </div>

      {queue.isError ? (
        <ErrorState message={tAll("loadError")} onRetry={() => queue.refetch()} />
      ) : queue.isLoading || !queue.data ? (
        <Skeleton className="h-12" />
      ) : queue.data.rows.length === 0 ? (
        <p className="text-sm text-gray-500">{t("empty")}</p>
      ) : (
        <div className="overflow-x-auto">
          <table
            className="w-full table-auto border-collapse text-left text-sm"
            aria-label={t("ariaLabel")}
          >
            <thead>
              <tr className="border-b border-gray-200 bg-gray-50 text-xs font-medium uppercase tracking-wide text-gray-500">
                <th scope="col" className="px-3 py-2">
                  {t("columns.network")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.type")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.amount")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.to")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.created")}
                </th>
                <th scope="col" className="px-3 py-2">
                  {t("columns.action")}
                </th>
              </tr>
            </thead>
            <tbody>
              {queue.data.rows.map((row) => (
                <tr
                  key={row.blockchainTransactionId}
                  className="border-b border-gray-100 last:border-b-0"
                >
                  <td className="px-3 py-2">{rowNetwork(row)}</td>
                  <td className="px-3 py-2">{tDynamic(tType, row.type, row.type)}</td>
                  <td className="px-3 py-2 tabular-nums">
                    {row.amount} {movedToken(row)}
                    {row.gasFee !== null && (
                      <span className="block text-xs text-gray-500">
                        {t("gasFee", { amount: row.gasFee })}
                      </span>
                    )}
                  </td>
                  <td className="break-all px-3 py-2 font-mono text-xs">{row.toAddress}</td>
                  <td className="px-3 py-2">
                    <time dateTime={row.createdAt}>{formatDateTime(row.createdAt, locale)}</time>
                  </td>
                  <td className="px-3 py-2">
                    <button
                      type="button"
                      onClick={() => setApproving(row)}
                      className="rounded-md border border-red-300 bg-white px-2 py-1 text-xs font-medium text-red-700 hover:bg-red-50"
                      data-testid={`approve-${row.blockchainTransactionId}`}
                    >
                      {t("approve")}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <ApproveDialog row={approving} onClose={() => setApproving(null)} />

      {queue.data && (
        <DepositRefundLimitSection
          limits={queue.data.depositRefundLimits ?? []}
          held={queue.data.limitHeldRows ?? []}
        />
      )}
    </section>
  );
}
