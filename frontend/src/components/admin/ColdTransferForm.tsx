"use client";

import { useState, type FormEvent } from "react";
import { useTranslations } from "next-intl";
import { CopyButton } from "@/components/common";
import { TxHashLink } from "@/components/transactions/detail/TxHashLink";
import { walletErrorMessage } from "@/lib/admin/walletErrors";
import type { ColdTransferToken, HotToColdTransferResult, NetworkWallets } from "@/lib/api/admin";
import { useHotToColdTransfer } from "@/lib/hooks/useAdminWallets";
import { networkLabel } from "@/lib/utils/blockchain";
import { BlockchainNetwork } from "@/types/enums";
import { AdminDialog } from "./AdminDialog";

/** Ethereum accepts USDT only (T157); TRON keeps both stablecoins. */
const TOKENS_BY_NETWORK: Record<BlockchainNetwork, readonly ColdTransferToken[]> = {
  [BlockchainNetwork.TRON]: ["USDT", "USDC"],
  [BlockchainNetwork.ETHEREUM]: ["USDT"],
};

// Positive decimal, at most 6 fraction digits (the stablecoin precision).
const AMOUNT_SHAPE = /^\d+(\.\d{1,6})?$/;

function parseAmount(raw: string): number | null {
  const trimmed = raw.trim();
  if (!AMOUNT_SHAPE.test(trimmed)) return null;
  const value = Number(trimmed);
  return Number.isFinite(value) && value > 0 ? value : null;
}

export interface ColdTransferFormProps {
  data: NetworkWallets;
}

/**
 * AD20 hot→cold transfer for ONE network. The destination is fixed by
 * deployment config, so both addresses are read-only here. Money moves, so
 * nothing is sent from the form itself: "Review" opens a confirmation dialog
 * that names network, token, amount and both addresses.
 */
export function ColdTransferForm({ data }: ColdTransferFormProps) {
  const t = useTranslations("adminWallets");
  const tf = useTranslations("adminWallets.transfer");
  const network = data.network;
  const tokens = TOKENS_BY_NETWORK[network];
  const hot = data.wallets.find((w) => w.role === "HOT");
  const cold = data.wallets.find((w) => w.role === "COLD");
  const hotAddress = hot?.address ?? null;
  const coldAddress = cold?.address ?? null;
  const available =
    data.serviceConfigured && hotAddress !== null && coldAddress !== null && !hot?.addressMismatch;

  const [token, setToken] = useState<ColdTransferToken>(tokens[0]);
  const [amountText, setAmountText] = useState("");
  const [touched, setTouched] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [result, setResult] = useState<HotToColdTransferResult | null>(null);
  const transfer = useHotToColdTransfer();

  const amount = parseAmount(amountText);
  const amountInvalid = touched && amount === null;
  const idPrefix = `cold-${network}`;

  function handleReview(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setTouched(true);
    setResult(null);
    if (amount === null || !available) return;
    transfer.reset();
    setConfirming(true);
  }

  function handleConfirm() {
    if (amount === null) return;
    transfer.mutate(
      { amount, token, network },
      {
        onSuccess: (res) => {
          setConfirming(false);
          setResult(res);
          setAmountText("");
          setTouched(false);
        },
      },
    );
  }

  function closeDialog() {
    if (transfer.isPending) return;
    setConfirming(false);
  }

  return (
    <section
      aria-label={tf("heading")}
      data-testid={`cold-transfer-${network}`}
      className="flex flex-col gap-3 rounded-lg border border-gray-200 p-4"
    >
      <div>
        <h4 className="text-sm font-semibold text-gray-900">{tf("heading")}</h4>
        <p className="text-xs text-gray-500">
          {tf("description", { network: networkLabel(network) })}
        </p>
      </div>

      {!available ? (
        <p className="text-sm text-amber-700" data-testid={`cold-transfer-unavailable-${network}`}>
          {tf("unavailable")}
        </p>
      ) : (
        <form onSubmit={handleReview} className="flex flex-col gap-3" noValidate>
          <dl className="grid gap-2 text-sm md:grid-cols-2">
            <div>
              <dt className="text-xs font-medium text-gray-500">{tf("from")}</dt>
              <dd className="break-all font-mono text-xs text-gray-900">{hotAddress}</dd>
            </div>
            <div>
              <dt className="text-xs font-medium text-gray-500">{tf("to")}</dt>
              <dd className="break-all font-mono text-xs text-gray-900">{coldAddress}</dd>
            </div>
          </dl>

          <div className="flex flex-wrap items-start gap-3">
            <label htmlFor={`${idPrefix}-token`} className="flex flex-col gap-1 text-sm">
              <span className="font-medium text-gray-700">{tf("token")}</span>
              <select
                id={`${idPrefix}-token`}
                value={token}
                onChange={(e) => setToken(e.target.value as ColdTransferToken)}
                className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm"
              >
                {tokens.map((tk) => (
                  <option key={tk} value={tk}>
                    {tk}
                  </option>
                ))}
              </select>
            </label>

            <label htmlFor={`${idPrefix}-amount`} className="flex flex-col gap-1 text-sm">
              <span className="font-medium text-gray-700">{tf("amount")}</span>
              <input
                id={`${idPrefix}-amount`}
                type="text"
                inputMode="decimal"
                autoComplete="off"
                value={amountText}
                onChange={(e) => setAmountText(e.target.value)}
                onBlur={() => setTouched(true)}
                aria-invalid={amountInvalid}
                aria-describedby={amountInvalid ? `${idPrefix}-amount-error` : undefined}
                className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm tabular-nums"
              />
              {amountInvalid && (
                <span id={`${idPrefix}-amount-error`} className="text-xs text-red-600">
                  {tf("amountInvalid")}
                </span>
              )}
            </label>

            <button
              type="submit"
              className="mt-6 rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700"
            >
              {tf("review")}
            </button>
          </div>
        </form>
      )}

      {result && (
        <p
          role="status"
          className="text-sm text-emerald-700"
          data-testid={`cold-success-${network}`}
        >
          {tf("success", { amount: result.amount, token: result.token })}{" "}
          <span className="text-gray-600">{tf("successTx")}: </span>
          <TxHashLink txHash={result.txHash} network={result.network} />
        </p>
      )}

      <AdminDialog open={confirming} title={tf("confirmTitle")} onClose={closeDialog}>
        <p
          role="alert"
          className="rounded-md border border-red-300 bg-red-50 p-3 text-sm font-medium text-red-800"
        >
          {tf("confirmWarning")}
        </p>
        <dl className="flex flex-col gap-2 text-sm">
          <div>
            <dt className="text-xs font-medium text-gray-500">{tf("confirmNetwork")}</dt>
            <dd className="text-base font-semibold text-gray-900">{networkLabel(network)}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium text-gray-500">{tf("confirmToken")}</dt>
            <dd className="font-semibold text-gray-900">{token}</dd>
          </div>
          <div>
            <dt className="text-xs font-medium text-gray-500">{tf("confirmAmount")}</dt>
            <dd className="text-base font-semibold tabular-nums text-gray-900">
              {amount} {token}
            </dd>
          </div>
          <div>
            <dt className="text-xs font-medium text-gray-500">{tf("confirmFrom")}</dt>
            <dd className="break-all font-mono text-xs text-gray-900">
              {hotAddress} {hotAddress && <CopyButton value={hotAddress} />}
            </dd>
          </div>
          <div>
            <dt className="text-xs font-medium text-gray-500">{tf("confirmTo")}</dt>
            <dd className="break-all font-mono text-xs text-gray-900">
              {coldAddress} {coldAddress && <CopyButton value={coldAddress} />}
            </dd>
          </div>
        </dl>
        {transfer.isError && (
          <p role="alert" className="text-sm text-red-700">
            {walletErrorMessage(t, transfer.error)}
          </p>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={closeDialog}
            disabled={transfer.isPending}
            className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
          >
            {tf("cancel")}
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            disabled={transfer.isPending}
            className="rounded-md bg-red-600 px-3 py-2 text-sm font-semibold text-white hover:bg-red-700 disabled:opacity-50"
          >
            {transfer.isPending
              ? tf("sending")
              : tf("confirmSubmit", { amount: amount ?? 0, token })}
          </button>
        </div>
      </AdminDialog>
    </section>
  );
}
