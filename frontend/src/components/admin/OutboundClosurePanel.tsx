"use client";

import { useState } from "react";
import { useLocale, useTranslations } from "next-intl";
import { useQuery } from "@tanstack/react-query";
import { ErrorState, Skeleton } from "@/components/common";
import { getMe } from "@/lib/api/auth";
import type {
  OutboundClosure,
  OutboundClosureCandidate,
  OutboundClosureStatus,
} from "@/lib/api/admin";
import { tDynamic } from "@/lib/i18n/dynamicKey";
import { useOutboundClosureCandidates, useOutboundClosures } from "@/lib/hooks/useOutboundClosures";
import { explorerTxUrl, networkMeta } from "@/lib/utils/blockchain";
import { formatDateTime } from "@/lib/utils/format";
import { BlockchainNetwork } from "@/types/enums";
import {
  ApproveClosureModal,
  ChainTokenNote,
  ProposeClosureModal,
  RejectClosureModal,
  closureNetwork,
  movedToken,
} from "./ClosureModals";

const STATUSES: readonly OutboundClosureStatus[] = ["PROPOSED", "APPROVED", "REJECTED"];

const STATUS_TONE: Record<OutboundClosureStatus, string> = {
  PROPOSED: "bg-amber-100 text-amber-800",
  APPROVED: "bg-emerald-100 text-emerald-800",
  REJECTED: "bg-gray-200 text-gray-700",
};

function CandidatesTable({
  candidates,
  onPropose,
  networkName,
}: {
  candidates: readonly OutboundClosureCandidate[];
  onPropose: (c: OutboundClosureCandidate) => void;
  networkName: string;
}) {
  const t = useTranslations("adminWallets.closures.candidates");
  const tr = useTranslations("adminWallets.closures.row");
  const tType = useTranslations("adminWallets.outboundType");
  const locale = useLocale();

  if (candidates.length === 0) {
    return <p className="text-sm text-gray-500">{t("empty", { network: networkName })}</p>;
  }
  return (
    <div className="overflow-x-auto">
      <table
        className="w-full table-auto border-collapse text-left text-sm"
        aria-label={t("ariaLabel", { network: networkName })}
      >
        <thead>
          <tr className="border-b border-gray-200 bg-gray-50 text-xs font-medium uppercase tracking-wide text-gray-500">
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
              {t("columns.retries")}
            </th>
            <th scope="col" className="px-3 py-2">
              {t("columns.action")}
            </th>
          </tr>
        </thead>
        <tbody>
          {candidates.map((c) => (
            <tr
              key={c.blockchainTransactionId}
              className="border-b border-gray-100 last:border-b-0"
            >
              <td className="px-3 py-2">
                {tDynamic(tType, c.type, c.type)}
                {c.rowStatus === "FAILED" && (
                  <p
                    data-testid={`restored-failed-${c.blockchainTransactionId}`}
                    className="text-xs font-medium text-amber-800"
                  >
                    {tr("restoredFailed")}
                  </p>
                )}
              </td>
              <td className="px-3 py-2 tabular-nums">
                {c.amount} {movedToken(c)}
                <ChainTokenNote row={c} />
              </td>
              <td className="break-all px-3 py-2 font-mono text-xs">{c.toAddress}</td>
              <td className="px-3 py-2">
                <time dateTime={c.createdAt}>{formatDateTime(c.createdAt, locale)}</time>
              </td>
              <td className="px-3 py-2 tabular-nums">{c.retryCount}</td>
              <td className="px-3 py-2">
                {c.hasLiveClosure ? (
                  <span className="text-xs text-gray-500">{t("live")}</span>
                ) : (
                  <button
                    type="button"
                    onClick={() => onPropose(c)}
                    className="rounded-md border border-blue-300 bg-white px-2 py-1 text-xs font-medium text-blue-700 hover:bg-blue-50"
                  >
                    {t("propose")}
                  </button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function ClosureCard({
  closure,
  currentAdminId,
  onApprove,
  onReject,
}: {
  closure: OutboundClosure;
  currentAdminId: string | undefined;
  onApprove: (c: OutboundClosure) => void;
  onReject: (c: OutboundClosure, withdraw: boolean) => void;
}) {
  const t = useTranslations("adminWallets.closures");
  const tType = useTranslations("adminWallets.outboundType");
  const locale = useLocale();
  const network = closureNetwork(closure.network);
  const explorer = networkMeta(network).explorerName;
  const open = closure.status === "PROPOSED";
  // Until the current admin is known, treat the proposal as theirs: showing an
  // Approve button that the server will refuse is worse than a brief delay.
  const own = currentAdminId === undefined || closure.proposedByAdminId === currentAdminId;

  return (
    <li
      className="flex flex-col gap-2 rounded-lg border border-gray-200 bg-white p-3"
      data-testid={`closure-${closure.id}`}
    >
      <div className="flex flex-wrap items-center gap-2">
        <span
          className={`rounded-full px-2 py-0.5 text-xs font-medium ${STATUS_TONE[closure.status]}`}
        >
          {t(`status.${closure.status}`)}
        </span>
        <span className="text-sm font-medium text-gray-900">
          {tDynamic(tType, closure.rowType, closure.rowType)}
        </span>
        <span className="text-sm tabular-nums text-gray-900">
          {closure.amount} {movedToken(closure)}
        </span>
      </div>

      <dl className="grid gap-1 text-xs md:grid-cols-2">
        <div>
          <dt className="text-gray-500">{t("row.recipient")}</dt>
          <dd className="break-all font-mono text-gray-900">{closure.toAddress}</dd>
        </div>
        <div>
          <dt className="text-gray-500">{t("row.proposedHash")}</dt>
          <dd className="break-all font-mono text-gray-900">
            {closure.txHash}{" "}
            <a
              href={explorerTxUrl(network, closure.txHash)}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={t("approve.explorer", { explorer })}
              className="font-sans text-blue-600 hover:underline"
            >
              {explorer}
            </a>
          </dd>
        </div>
        <div className="md:col-span-2">
          <dt className="text-gray-500">{t("row.reason")}</dt>
          <dd className="text-gray-900">{closure.reason}</dd>
        </div>
      </dl>

      <p className="text-xs text-gray-600">
        {t("row.proposedBy", {
          name: closure.proposedByName,
          date: formatDateTime(closure.proposedAt, locale),
        })}
      </p>
      {closure.decidedAt && (
        <p className="text-xs text-gray-600">
          {t("row.decidedBy", {
            name: closure.decidedByName ?? "—",
            date: formatDateTime(closure.decidedAt, locale),
          })}
          {closure.decisionNote ? ` — ${closure.decisionNote}` : ""}
        </p>
      )}

      {open && (
        <div className="flex flex-wrap items-center gap-2">
          {own ? (
            <>
              <p className="text-xs text-gray-500">{t("row.ownProposal")}</p>
              <button
                type="button"
                onClick={() => onReject(closure, true)}
                className="rounded-md border border-gray-300 bg-white px-2 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50"
              >
                {t("actions.withdraw")}
              </button>
            </>
          ) : (
            <>
              <button
                type="button"
                onClick={() => onApprove(closure)}
                className="rounded-md bg-red-600 px-2 py-1 text-xs font-semibold text-white hover:bg-red-700"
              >
                {t("actions.approve")}
              </button>
              <button
                type="button"
                onClick={() => onReject(closure, false)}
                className="rounded-md border border-gray-300 bg-white px-2 py-1 text-xs font-medium text-gray-700 hover:bg-gray-50"
              >
                {t("actions.reject")}
              </button>
            </>
          )}
        </div>
      )}
    </li>
  );
}

/**
 * Two-person closure tool for one network's outbound rows that already went
 * out on chain (T157 — Ethereum journal loss; T172a — TRON, and the row a
 * database restore left behind, DEPLOY_RUNBOOK §N.8). It never sends money:
 * it only binds an on-chain hash to a row once a second admin has confirmed it
 * by retyping the hash.
 */
export function OutboundClosurePanel({
  network = BlockchainNetwork.ETHEREUM,
}: {
  network?: BlockchainNetwork;
}) {
  const t = useTranslations("adminWallets.closures");
  const tAll = useTranslations("adminWallets");
  const [status, setStatus] = useState<OutboundClosureStatus | "">("PROPOSED");
  const [proposing, setProposing] = useState<OutboundClosureCandidate | null>(null);
  const [approving, setApproving] = useState<OutboundClosure | null>(null);
  const [rejecting, setRejecting] = useState<{
    closure: OutboundClosure;
    withdraw: boolean;
  } | null>(null);

  const networkName = networkMeta(network).name;
  const headingId = `closure-panel-heading-${network}`;
  const candidates = useOutboundClosureCandidates(network);
  const closures = useOutboundClosures(network, status === "" ? undefined : status);
  // Shared ["auth","me"] query (AdminGuard / sidebar prime it): no extra request.
  const { data: me } = useQuery({ queryKey: ["auth", "me"], queryFn: getMe });

  return (
    <section
      aria-labelledby={headingId}
      data-testid="closure-panel"
      data-network={network}
      className="flex flex-col gap-4 rounded-lg border border-amber-300 bg-amber-50/40 p-4"
    >
      <div>
        <h4 id={headingId} className="text-sm font-semibold text-gray-900">
          {t("heading", { network: networkName })}
        </h4>
        <p className="text-xs text-gray-600">{t("intro")}</p>
        <p className="mt-1 text-xs font-semibold text-amber-800">{t("noMoney")}</p>
      </div>

      <div className="flex flex-col gap-2">
        <h5 className="text-sm font-medium text-gray-800">
          {t("candidates.heading", { network: networkName })}
        </h5>
        {candidates.isError ? (
          <ErrorState message={tAll("loadError")} onRetry={() => candidates.refetch()} />
        ) : candidates.isLoading ? (
          <Skeleton className="h-12" />
        ) : (
          <CandidatesTable
            candidates={candidates.data ?? []}
            onPropose={setProposing}
            networkName={networkName}
          />
        )}
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h5 className="text-sm font-medium text-gray-800">{t("list.heading")}</h5>
          <label className="flex items-center gap-2 text-xs text-gray-700">
            <span>{t("list.statusFilter")}</span>
            <select
              value={status}
              onChange={(e) => setStatus(e.target.value as OutboundClosureStatus | "")}
              className="rounded-md border border-gray-300 bg-white px-2 py-1 text-xs"
            >
              <option value="">{t("list.all")}</option>
              {STATUSES.map((s) => (
                <option key={s} value={s}>
                  {t(`status.${s}`)}
                </option>
              ))}
            </select>
          </label>
        </div>
        {closures.isError ? (
          <ErrorState message={tAll("loadError")} onRetry={() => closures.refetch()} />
        ) : closures.isLoading ? (
          <Skeleton className="h-16" />
        ) : (closures.data ?? []).length === 0 ? (
          <p className="text-sm text-gray-500">{t("list.empty")}</p>
        ) : (
          <ul aria-label={t("list.ariaLabel")} className="flex flex-col gap-2">
            {(closures.data ?? []).map((c) => (
              <ClosureCard
                key={c.id}
                closure={c}
                currentAdminId={me?.id}
                onApprove={setApproving}
                onReject={(closure, withdraw) => setRejecting({ closure, withdraw })}
              />
            ))}
          </ul>
        )}
      </div>

      <ProposeClosureModal candidate={proposing} onClose={() => setProposing(null)} />
      <ApproveClosureModal closure={approving} onClose={() => setApproving(null)} />
      <RejectClosureModal
        closure={rejecting?.closure ?? null}
        withdraw={rejecting?.withdraw ?? false}
        onClose={() => setRejecting(null)}
      />
    </section>
  );
}
