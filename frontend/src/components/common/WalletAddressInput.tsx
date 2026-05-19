"use client";

import { FormEvent, useState } from "react";
import { useTranslations } from "next-intl";
import { Spinner } from "./LoadingState";
import { cn } from "@/lib/utils/cn";

export type WalletValidationOutcome =
  | { status: "ok" }
  | { status: "sanctioned" }
  | { status: "error"; messageKey?: string };

export interface WalletAddressInputProps {
  initialValue?: string;
  onValidate?: (address: string) => Promise<WalletValidationOutcome>;
  onConfirm: (address: string) => void;
  className?: string;
}

type Phase = "input" | "validating" | "confirm";

const TRC20_REGEX = /^T[1-9A-HJ-NP-Za-km-z]{33}$/;

export function WalletAddressInput({
  initialValue = "",
  onValidate,
  onConfirm,
  className,
}: WalletAddressInputProps) {
  const t = useTranslations("walletAddress");
  const [value, setValue] = useState(initialValue);
  const [phase, setPhase] = useState<Phase>("input");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setError(null);
    if (!TRC20_REGEX.test(value)) {
      setError(t("invalidFormat"));
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
          setError(t(result.messageKey ?? "validationError"));
          return;
        }
      } catch {
        setPhase("input");
        setError(t("validationError"));
        return;
      }
    }
    setPhase("confirm");
  }

  function handleConfirm() {
    onConfirm(value);
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
      >
        <p className="text-sm font-medium text-blue-900">{t("confirmTitle")}</p>
        <code className="break-all rounded-md bg-white px-3 py-2 text-sm">{value}</code>
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
            className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700"
          >
            {t("confirm")}
          </button>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit} className={cn("flex flex-col gap-2", className)}>
      <label className="flex flex-col gap-1 text-sm">
        <span className="font-medium text-gray-700">{t("label")}</span>
        <input
          type="text"
          value={value}
          onChange={(e) => setValue(e.target.value.trim())}
          placeholder="T..."
          inputMode="text"
          autoComplete="off"
          spellCheck={false}
          disabled={phase === "validating"}
          className={cn(
            "rounded-md border bg-white px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2",
            error ? "border-red-300 focus:ring-red-200" : "border-gray-300 focus:ring-blue-200",
            phase === "validating" && "opacity-60",
          )}
        />
        <span className="text-xs text-gray-500">{t("hint")}</span>
        {error && <span className="text-xs text-red-600">{error}</span>}
      </label>
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
