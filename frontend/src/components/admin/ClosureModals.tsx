"use client";

import { useId, useState, type FormEvent } from "react";
import { useTranslations } from "next-intl";
import { walletErrorMessage } from "@/lib/admin/walletErrors";
import { tDynamic } from "@/lib/i18n/dynamicKey";
import type { OutboundClosure, OutboundClosureCandidate } from "@/lib/api/admin";
import {
  useApproveOutboundClosure,
  useProposeOutboundClosure,
  useRejectOutboundClosure,
} from "@/lib/hooks/useOutboundClosures";
import { explorerTxUrl, networkMeta } from "@/lib/utils/blockchain";
import { BlockchainNetwork } from "@/types/enums";
import { AdminDialog } from "./AdminDialog";

// T172a — the tool takes both networks; each row/closure names its own.
export const TX_HASH_SHAPE = /^0x[0-9a-fA-F]{64}$/;
/** A TRON transaction id as TronScan shows it: 64 hex digits, no prefix. */
export const TRON_TX_ID_SHAPE = /^[0-9a-fA-F]{64}$/;

/** The row's network as the enum; an unknown value reads as Ethereum (the pre-T172a tool). */
export function closureNetwork(network: string | undefined): BlockchainNetwork {
  return network === BlockchainNetwork.TRON ? BlockchainNetwork.TRON : BlockchainNetwork.ETHEREUM;
}

export function hashShapeFor(network: BlockchainNetwork): RegExp {
  return network === BlockchainNetwork.TRON ? TRON_TX_ID_SHAPE : TX_HASH_SHAPE;
}
export const MIN_REASON_LENGTH = 20;
const MAX_REASON_LENGTH = 2000;

const SECONDARY_BUTTON =
  "rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50";
const PRIMARY_BUTTON = "rounded-md px-3 py-2 text-sm font-semibold text-white disabled:opacity-50";
const FIELD =
  "rounded-md border bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2";

function ExplorerLink({
  hash,
  label,
  network,
}: {
  hash: string;
  label: string;
  network: BlockchainNetwork;
}) {
  return (
    <a
      href={explorerTxUrl(network, hash)}
      target="_blank"
      rel="noopener noreferrer"
      className="text-xs text-blue-600 hover:underline"
    >
      {label}
    </a>
  );
}

interface TokenedRow {
  token: string;
  chainToken: string | null;
}

/**
 * The token a row moves on chain (T157 düzeltme turu) — for a wrong-token
 * refund the token that arrived, not the one the transaction expected. "?"
 * when it cannot be resolved (the backend then refuses the row).
 */
export function movedToken(row: TokenedRow): string {
  return row.chainToken ?? "?";
}

/** Says so when the row moves another token than the transaction expected, or none resolves. */
export function ChainTokenNote({ row }: { row: TokenedRow }) {
  const tr = useTranslations("adminWallets.closures.row");
  if (row.chainToken === row.token) return null;
  return (
    <p data-testid="chain-token-note" className="text-xs font-medium text-amber-800">
      {row.chainToken === null
        ? tr("tokenUnresolved")
        : tr("tokenDiffers", { expected: row.token, actual: row.chainToken })}
    </p>
  );
}

// ── Propose ─────────────────────────────────────────────────────────────────

export interface ProposeClosureModalProps {
  candidate: OutboundClosureCandidate | null;
  onClose: () => void;
}

function ProposeForm({
  candidate,
  onClose,
}: {
  candidate: OutboundClosureCandidate;
  onClose: () => void;
}) {
  const t = useTranslations("adminWallets");
  const tp = useTranslations("adminWallets.closures.propose");
  const tt = useTranslations("adminWallets.outboundType");
  const uid = useId();
  const [hash, setHash] = useState("");
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);
  const propose = useProposeOutboundClosure();
  const network = closureNetwork(candidate.network);
  const explorer = networkMeta(network).explorerName;
  const tron = network === BlockchainNetwork.TRON;

  const trimmedHash = hash.trim();
  const trimmedReason = reason.trim();
  const hashValid = hashShapeFor(network).test(trimmedHash);
  const reasonValid =
    trimmedReason.length >= MIN_REASON_LENGTH && trimmedReason.length <= MAX_REASON_LENGTH;

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setTouched(true);
    if (!hashValid || !reasonValid) return;
    propose.mutate(
      {
        blockchainTransactionId: candidate.blockchainTransactionId,
        txHash: trimmedHash,
        reason: trimmedReason,
      },
      { onSuccess: onClose },
    );
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <p
        role="alert"
        className="rounded-md border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900"
      >
        {tp("warning")}
      </p>
      <p className="text-xs text-gray-600">
        {tp("row", {
          type: tDynamic(tt, candidate.type, candidate.type),
          amount: candidate.amount,
          token: movedToken(candidate),
          to: candidate.toAddress,
        })}
      </p>
      <ChainTokenNote row={candidate} />
      {candidate.rowStatus === "FAILED" && (
        <p data-testid="restored-failed-note" className="text-xs font-medium text-amber-800">
          {tp("restoredFailed")}
        </p>
      )}

      <div className="flex flex-col gap-1 text-sm">
        <label htmlFor={`${uid}-hash`} className="font-medium text-gray-700">
          {tp("hash")}
        </label>
        <input
          id={`${uid}-hash`}
          type="text"
          value={hash}
          onChange={(e) => setHash(e.target.value)}
          autoComplete="off"
          spellCheck={false}
          aria-invalid={touched && !hashValid}
          className={`${FIELD} font-mono text-xs ${touched && !hashValid ? "border-red-300 focus:ring-red-200" : "border-gray-300 focus:ring-blue-200"}`}
        />
        <span className="text-xs text-gray-500">{tron ? tp("hashHelpTron") : tp("hashHelp")}</span>
        {touched && !hashValid && (
          <span className="text-xs text-red-600">
            {tron ? tp("hashInvalidTron") : tp("hashInvalid")}
          </span>
        )}
      </div>
      {hashValid && (
        <ExplorerLink hash={trimmedHash} network={network} label={tp("explorer", { explorer })} />
      )}

      <div className="flex flex-col gap-1 text-sm">
        <label htmlFor={`${uid}-reason`} className="font-medium text-gray-700">
          {tp("reason")}
        </label>
        <textarea
          id={`${uid}-reason`}
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={4}
          maxLength={MAX_REASON_LENGTH}
          aria-invalid={touched && !reasonValid}
          className={`${FIELD} ${touched && !reasonValid ? "border-red-300 focus:ring-red-200" : "border-gray-300 focus:ring-blue-200"}`}
        />
        <span className="text-xs text-gray-500">{tp("reasonHelp")}</span>
        {touched && !reasonValid && (
          <span className="text-xs text-red-600">{tp("reasonTooShort")}</span>
        )}
      </div>

      {propose.isError && (
        <p role="alert" className="text-sm text-red-700">
          {walletErrorMessage(t, propose.error)}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <button type="button" onClick={onClose} className={SECONDARY_BUTTON}>
          {tp("cancel")}
        </button>
        <button
          type="submit"
          disabled={propose.isPending}
          className={`${PRIMARY_BUTTON} bg-blue-600 hover:bg-blue-700`}
        >
          {tp("submit")}
        </button>
      </div>
    </form>
  );
}

export function ProposeClosureModal({ candidate, onClose }: ProposeClosureModalProps) {
  const tp = useTranslations("adminWallets.closures.propose");
  return (
    <AdminDialog open={candidate !== null} title={tp("title")} onClose={onClose}>
      {candidate && <ProposeForm candidate={candidate} onClose={onClose} />}
    </AdminDialog>
  );
}

// ── Approve ─────────────────────────────────────────────────────────────────

export interface ApproveClosureModalProps {
  closure: OutboundClosure | null;
  onClose: () => void;
}

function ApproveForm({ closure, onClose }: { closure: OutboundClosure; onClose: () => void }) {
  const t = useTranslations("adminWallets");
  const ta = useTranslations("adminWallets.closures.approve");
  const tr = useTranslations("adminWallets.closures.row");
  const uid = useId();
  const [typed, setTyped] = useState("");
  const approve = useApproveOutboundClosure();
  const network = closureNetwork(closure.network);
  const explorer = networkMeta(network).explorerName;

  const typedTrimmed = typed.trim();
  // Hex is case-insensitive; the point of retyping is a human comparison.
  const matches = typedTrimmed.toLowerCase() === closure.txHash.toLowerCase();
  const showMismatch = typedTrimmed.length > 0 && !matches;

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!matches) return;
    approve.mutate({ id: closure.id, txHash: typedTrimmed }, { onSuccess: onClose });
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <p
        role="alert"
        className="rounded-md border border-red-300 bg-red-50 p-3 text-sm font-medium text-red-800"
      >
        {ta("warning")}
      </p>
      <p className="text-xs text-gray-700">{ta("systemCheck")}</p>

      <div className="flex flex-col gap-2 rounded-md bg-gray-50 p-3 text-sm">
        <p className="text-xs font-medium text-gray-700">{ta("compare", { explorer })}</p>
        <dl className="flex flex-col gap-1">
          <div>
            <dt className="text-xs text-gray-500">{tr("recipient")}</dt>
            <dd className="break-all font-mono text-xs text-gray-900">{closure.toAddress}</dd>
          </div>
          <div>
            <dt className="text-xs text-gray-500">{tr("amount")}</dt>
            <dd className="font-semibold tabular-nums text-gray-900">
              {closure.amount} {movedToken(closure)}
            </dd>
            <ChainTokenNote row={closure} />
          </div>
          <div>
            <dt className="text-xs text-gray-500">{ta("hashProposed")}</dt>
            <dd className="break-all font-mono text-xs text-gray-900">{closure.txHash}</dd>
          </div>
        </dl>
        <ExplorerLink
          hash={closure.txHash}
          network={network}
          label={ta("explorer", { explorer })}
        />
        <p className="text-xs text-gray-600">
          {ta("proposedBy", { name: closure.proposedByName })}
        </p>
        <p className="text-xs text-gray-600">{closure.reason}</p>
      </div>

      <div className="flex flex-col gap-1 text-sm">
        <label htmlFor={`${uid}-typed`} className="font-medium text-gray-700">
          {ta("typeHash")}
        </label>
        <input
          id={`${uid}-typed`}
          type="text"
          value={typed}
          onChange={(e) => setTyped(e.target.value)}
          autoComplete="off"
          spellCheck={false}
          aria-invalid={showMismatch}
          className={`${FIELD} font-mono text-xs ${showMismatch ? "border-red-300 focus:ring-red-200" : "border-gray-300 focus:ring-blue-200"}`}
        />
        <span className="text-xs text-gray-500">{ta("typeHashHelp")}</span>
        {showMismatch && <span className="text-xs text-red-600">{ta("mismatch")}</span>}
      </div>

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
          type="submit"
          disabled={!matches || approve.isPending}
          className={`${PRIMARY_BUTTON} bg-red-600 hover:bg-red-700`}
        >
          {ta("submit")}
        </button>
      </div>
    </form>
  );
}

export function ApproveClosureModal({ closure, onClose }: ApproveClosureModalProps) {
  const ta = useTranslations("adminWallets.closures.approve");
  return (
    <AdminDialog open={closure !== null} title={ta("title")} onClose={onClose}>
      {closure && <ApproveForm closure={closure} onClose={onClose} />}
    </AdminDialog>
  );
}

// ── Reject / withdraw ───────────────────────────────────────────────────────

export interface RejectClosureModalProps {
  closure: OutboundClosure | null;
  /** True when the proposer withdraws their own proposal. */
  withdraw: boolean;
  onClose: () => void;
}

function RejectForm({
  closure,
  withdraw,
  onClose,
}: {
  closure: OutboundClosure;
  withdraw: boolean;
  onClose: () => void;
}) {
  const t = useTranslations("adminWallets");
  const trj = useTranslations("adminWallets.closures.reject");
  const uid = useId();
  const [note, setNote] = useState("");
  const [touched, setTouched] = useState(false);
  const reject = useRejectOutboundClosure();
  const trimmed = note.trim();
  const valid = trimmed.length > 0;

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setTouched(true);
    if (!valid) return;
    reject.mutate({ id: closure.id, note: trimmed }, { onSuccess: onClose });
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4" noValidate>
      <div className="flex flex-col gap-1 text-sm">
        <label htmlFor={`${uid}-note`} className="font-medium text-gray-700">
          {trj("note")}
        </label>
        <textarea
          id={`${uid}-note`}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          rows={3}
          aria-invalid={touched && !valid}
          className={`${FIELD} ${touched && !valid ? "border-red-300 focus:ring-red-200" : "border-gray-300 focus:ring-blue-200"}`}
        />
        <span className="text-xs text-gray-500">{trj("noteHelp")}</span>
        {touched && !valid && <span className="text-xs text-red-600">{trj("noteRequired")}</span>}
      </div>

      {reject.isError && (
        <p role="alert" className="text-sm text-red-700">
          {walletErrorMessage(t, reject.error)}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <button type="button" onClick={onClose} className={SECONDARY_BUTTON}>
          {trj("cancel")}
        </button>
        <button
          type="submit"
          disabled={reject.isPending}
          className={`${PRIMARY_BUTTON} bg-red-600 hover:bg-red-700`}
        >
          {withdraw ? trj("submitWithdraw") : trj("submitReject")}
        </button>
      </div>
    </form>
  );
}

export function RejectClosureModal({ closure, withdraw, onClose }: RejectClosureModalProps) {
  const trj = useTranslations("adminWallets.closures.reject");
  return (
    <AdminDialog
      open={closure !== null}
      title={withdraw ? trj("titleWithdraw") : trj("titleReject")}
      onClose={onClose}
    >
      {closure && <RejectForm closure={closure} withdraw={withdraw} onClose={onClose} />}
    </AdminDialog>
  );
}
