"use client";

import { FormEvent, useId, useState } from "react";
import { useTranslations } from "next-intl";
import { Spinner } from "./LoadingState";
import { cn } from "@/lib/utils/cn";
import { tDynamic } from "@/lib/i18n/dynamicKey";
import { BlockchainNetwork } from "@/types/enums";
import { checkAddressShape, networkLabel, networkMeta } from "@/lib/utils/blockchain";

export type WalletValidationOutcome =
  | { status: "ok" }
  | { status: "sanctioned" }
  | { status: "error"; messageKey?: string };

/**
 * What the user confirmed together with the address. `confirmLowercase` is the
 * second confirmation 02 §12.3 / 07 §5.3 require for an Ethereum address that
 * carries no EIP-55 checksum (all one letter case); it is `false` everywhere
 * else.
 */
export interface WalletAddressConfirmation {
  confirmLowercase: boolean;
}

export interface WalletAddressInputProps {
  /** C11 F8 (04 §5): the network whose address shape is checked. */
  network: BlockchainNetwork;
  initialValue?: string;
  onValidate?: (address: string) => Promise<WalletValidationOutcome>;
  onConfirm: (address: string, confirmation: WalletAddressConfirmation) => void;
  className?: string;
}

type Phase = "input" | "validating" | "confirm";

/**
 * C11 — Wallet Address Input (04 §5), per network (T156).
 *
 * The shape is checked per network (TRON base58 `T…` 34 chars; Ethereum `0x` +
 * 40 hex). The confirm step shows the full address next to the network label,
 * because the same `0x` address is valid on many EVM networks and the network
 * is what the user must check. An Ethereum address without a checksum cannot be
 * confirmed until the user ticks the extra box. The server re-validates
 * everything (checksums included) — this is the early, explanatory half.
 */
export function WalletAddressInput({
  network,
  initialValue = "",
  onValidate,
  onConfirm,
  className,
}: WalletAddressInputProps) {
  const t = useTranslations("walletAddress");
  const inputId = useId();
  const [value, setValue] = useState(initialValue);
  const [phase, setPhase] = useState<Phase>("input");
  const [error, setError] = useState<string | null>(null);
  const [needsChecksumConfirm, setNeedsChecksumConfirm] = useState(false);
  const [checksumConfirmed, setChecksumConfirmed] = useState(false);
  const meta = networkMeta(network);
  const label = networkLabel(network);

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    const shape = checkAddressShape(network, value);
    if (shape === "INVALID_FORMAT") {
      setError(t("invalidFormat", { network: label }));
      return;
    }
    if (onValidate) {
      setPhase("validating");
      try {
        const result = await onValidate(value);
        if (result.status === "sanctioned") {
          setPhase("input");
          setError(t("sanctioned"));
          return;
        }
        if (result.status === "error") {
          setPhase("input");
          setError(tDynamic(t, result.messageKey ?? "validationError", t("validationError")));
          return;
        }
      } catch {
        setPhase("input");
        setError(t("validationError"));
        return;
      }
    }
    setNeedsChecksumConfirm(shape === "NO_CHECKSUM");
    setChecksumConfirmed(false);
    setPhase("confirm");
  }

  function handleConfirm() {
    if (needsChecksumConfirm && !checksumConfirmed) return;
    onConfirm(value, { confirmLowercase: needsChecksumConfirm });
  }

  function handleEdit() {
    setPhase("input");
  }

  if (phase === "confirm") {
    return (
      <div
        className={cn(
          "flex flex-col gap-3 rounded-lg border border-blue-200 bg-blue-50 p-4",
          className,
        )}
        data-testid="wallet-address-confirm"
      >
        <p className="text-sm font-medium text-blue-900">{t("confirmTitle")}</p>
        <p className="text-sm text-blue-900">
          <span className="font-medium">{t("networkLabel")}:</span>{" "}
          <span className="font-semibold" data-testid="wallet-address-confirm-network">
            {label}
          </span>
        </p>
        <code className="break-all rounded-md bg-white px-3 py-2 text-sm">{value}</code>
        {network === BlockchainNetwork.ETHEREUM && (
          <p className="text-xs text-blue-900">{t("evmSameAddressNote")}</p>
        )}
        {needsChecksumConfirm && (
          <label className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 p-2 text-sm text-amber-900">
            <input
              type="checkbox"
              checked={checksumConfirmed}
              onChange={(e) => setChecksumConfirmed(e.target.checked)}
              className="mt-0.5"
              data-testid="wallet-address-checksum-confirm"
            />
            <span>{t("noChecksumConfirm")}</span>
          </label>
        )}
        <div className="flex justify-end gap-2">
          <button
            type="button"
            onClick={handleEdit}
            className="rounded-md border border-blue-300 bg-white px-3 py-2 text-sm text-blue-700 hover:bg-blue-100"
          >
            {t("edit")}
          </button>
          <button
            type="button"
            onClick={handleConfirm}
            disabled={needsChecksumConfirm && !checksumConfirmed}
            className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
          >
            {t("confirm")}
          </button>
        </div>
      </div>
    );
  }

  const errorId = `${inputId}-error`;
  const hintId = `${inputId}-hint`;
  return (
    <form onSubmit={handleSubmit} className={cn("flex flex-col gap-2", className)}>
      <label htmlFor={inputId} className="text-sm font-medium text-gray-700">
        {t("label", { network: label })}
      </label>
      <input
        id={inputId}
        type="text"
        value={value}
        onChange={(e) => setValue(e.target.value.trim())}
        placeholder={meta.addressPlaceholder}
        inputMode="text"
        autoComplete="off"
        spellCheck={false}
        disabled={phase === "validating"}
        aria-invalid={error ? "true" : undefined}
        aria-describedby={error ? `${hintId} ${errorId}` : hintId}
        className={cn(
          "rounded-md border bg-white px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2",
          error ? "border-red-300 focus:ring-red-200" : "border-gray-300 focus:ring-blue-200",
          phase === "validating" && "opacity-60",
        )}
      />
      <span id={hintId} className="text-xs text-gray-500">
        {network === BlockchainNetwork.ETHEREUM ? t("hintEthereum") : t("hintTron")}
      </span>
      {error && (
        <span id={errorId} role="alert" className="text-xs text-red-600">
          {error}
        </span>
      )}
      <button
        type="submit"
        disabled={phase === "validating"}
        className="inline-flex items-center justify-center gap-2 self-start rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
      >
        {phase === "validating" ? (
          <>
            <Spinner size="sm" /> {t("validating")}
          </>
        ) : (
          t("continue")
        )}
      </button>
    </form>
  );
}
