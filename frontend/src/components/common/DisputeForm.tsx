"use client";

import { FormEvent, useState } from "react";
import { useTranslations } from "next-intl";
import { DisputeType } from "@/types/enums";
import { Spinner } from "./LoadingState";
import { cn } from "@/lib/utils/cn";
import { tDynamic } from "@/lib/i18n/dynamicKey";

/**
 * Outcome of the initial auto-check. Includes the human-readable message
 * plus action-availability flags so the form can decide whether to render
 * the TX-hash retry sub-step (PAYMENT) or jump straight to the escalation
 * step. Mirrors `DisputeAutoCheckResult` from `lib/api/disputes.ts`.
 */
export interface DisputeAutoCheckPayload {
  resolved: boolean;
  message: string;
  canSubmitTxHash: boolean;
  canEscalate: boolean;
  /**
   * Dispute id returned by the open-dispute endpoint. Required for the
   * TX-hash retry and escalation API calls. Omitted only in the existing-
   * dispute resume flow (DisputeBlock already has the id).
   */
  disputeId: string;
  type: DisputeType;
}

/**
 * Outcome of the TX hash re-check. Backend (07 §7.9) returns only the
 * resolution boolean + message; escalation availability is unchanged from
 * the original auto-check, so the form keeps using the existing flag.
 */
export interface DisputeTxHashPayload {
  resolved: boolean;
  message: string;
}

/**
 * Existing-dispute mode payload — used when the form is re-entered from
 * `DisputeBlock` for an already-open dispute. Skips the type-selection and
 * initial auto-check steps; starts at the result step (or escalation step
 * if the dispute is past auto-check). Auto-check messages are the verbatim
 * string surfaced in `TransactionDetailDispute.autoCheckResult` (07 §7.5);
 * see [[T92_REPORT]] for the locale-handoff decision.
 */
export interface ExistingDisputeContext {
  disputeId: string;
  type: DisputeType;
  autoCheckMessage: string | null;
  canSubmitTxHash: boolean;
  canEscalate: boolean;
}

export interface DisputeFormProps {
  /**
   * Open a new dispute. Returns the structured auto-check + dispute id so
   * the form can sequence TX-hash / escalation sub-steps. Required in
   * `open` mode, ignored in `existing` mode.
   */
  onOpen?: (type: DisputeType) => Promise<DisputeAutoCheckPayload>;
  /**
   * Re-submit a TX hash for an open PAYMENT dispute. Called from the
   * "Submit TX hash" sub-step. Required if any input enables it.
   */
  onSubmitTxHash?: (disputeId: string, txHash: string) => Promise<DisputeTxHashPayload>;
  /** Escalate the dispute to admin review. */
  onEscalate: (disputeId: string, type: DisputeType, detail: string) => Promise<void>;
  /**
   * WP6a (T135-FeDisputeTypeChoices) — the types the server will accept for
   * this transaction right now (`availableActions.disputableTypes`, 07 §7.5).
   * The form used to offer all three unconditionally, so a buyer could pick
   * one the API would reject and only learn on submit.
   *
   * Undefined means "the caller did not supply it" (an older response, or a
   * context with no transaction) and all three are offered, exactly as
   * before. An EMPTY array is different and is respected: the server said
   * none are open.
   */
  disputableTypes?: readonly DisputeType[];
  /** Optional close handler — renders the cancel/close buttons when set. */
  onClose?: () => void;
  /**
   * When provided, the form skips the type/checking steps and resumes from
   * the result step. Used by `DisputeBlock` to re-enter the same wizard
   * for already-open disputes.
   */
  existingDispute?: ExistingDisputeContext;
  className?: string;
}

type Step = "type" | "checking" | "result" | "txhash" | "txhashChecking" | "escalation" | "done";

/**
 * C07 — Dispute Form (04 §5, T92). Three-step user-facing wizard:
 *
 *   1. Type selection (PAYMENT / DELIVERY / WRONG_ITEM)
 *   2. Auto-check (server runs blockchain or Steam-side check, returns
 *      verbatim Turkish message; PAYMENT type may unlock a TX-hash retry
 *      sub-step when the auto-check is unresolved + `canSubmitTxHash`)
 *   3. Escalation (textarea + admin handoff; available when the auto-check
 *      result reports `canEscalate` true)
 *
 * In `existingDispute` mode the wizard starts at step 2 with the result
 * pre-populated — `DisputeBlock` uses this to keep the same UX for
 * follow-up actions on already-open disputes.
 */
export function DisputeForm({
  onOpen,
  onSubmitTxHash,
  onEscalate,
  onClose,
  existingDispute,
  className,
  disputableTypes,
}: DisputeFormProps) {
  const t = useTranslations("disputeForm");
  const tErr = useTranslations("disputeForm.errors");

  const [step, setStep] = useState<Step>(existingDispute ? "result" : "type");
  const [type, setType] = useState<DisputeType | null>(existingDispute?.type ?? null);

  // WP6a — the server's list when it supplied one, otherwise all three. The
  // ?? (not ||) matters: an empty array is a real answer ("none disputable")
  // and must not fall through to the full set.
  const offeredTypes: readonly DisputeType[] = disputableTypes ?? [
    DisputeType.PAYMENT,
    DisputeType.DELIVERY,
    DisputeType.WRONG_ITEM,
  ];
  const [disputeId, setDisputeId] = useState<string | null>(existingDispute?.disputeId ?? null);
  const [resolved, setResolved] = useState<boolean>(false);
  const [message, setMessage] = useState<string | null>(existingDispute?.autoCheckMessage ?? null);
  const [canSubmitTxHash, setCanSubmitTxHash] = useState<boolean>(
    existingDispute?.canSubmitTxHash ?? false,
  );
  const [canEscalate, setCanEscalate] = useState<boolean>(existingDispute?.canEscalate ?? false);
  const [txHash, setTxHash] = useState("");
  const [detail, setDetail] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [errorKey, setErrorKey] = useState<string | null>(null);

  // 04 §7.3 + 02 §10.2 — existing dispute starts at result without going
  // through auto-check; outcome wording is muted because we already know
  // the verdict from the parent's `autoCheckMessage` snapshot.
  const isExistingFlow = existingDispute != null;

  async function handleTypeSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!type || !onOpen) return;
    setErrorKey(null);
    setStep("checking");
    try {
      const result = await onOpen(type);
      setDisputeId(result.disputeId);
      setResolved(result.resolved);
      setMessage(result.message);
      setCanSubmitTxHash(result.canSubmitTxHash);
      setCanEscalate(result.canEscalate);
      setStep("result");
    } catch (err) {
      setErrorKey(extractErrorKey(err));
      setStep("type");
    }
  }

  async function handleTxHashSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!disputeId || !onSubmitTxHash || txHash.trim().length === 0) return;
    setErrorKey(null);
    setStep("txhashChecking");
    try {
      const result = await onSubmitTxHash(disputeId, txHash.trim());
      setResolved(result.resolved);
      setMessage(result.message);
      // 07 §7.9 — submit-txhash response carries only resolved + message.
      // Re-check eligibility stays unchanged unless backend resolved (then
      // both flags drop to false to mirror the original 07 §7.8 contract).
      if (result.resolved) {
        setCanSubmitTxHash(false);
        setCanEscalate(false);
      }
      setStep("result");
    } catch (err) {
      setErrorKey(extractErrorKey(err));
      setStep("txhash");
    }
  }

  async function handleEscalate(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!type || !disputeId || detail.trim().length < 10) return;
    setErrorKey(null);
    setSubmitting(true);
    try {
      await onEscalate(disputeId, type, detail.trim());
      setStep("done");
    } catch (err) {
      setErrorKey(extractErrorKey(err));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div
      className={cn(
        "flex flex-col gap-4 rounded-lg border border-gray-200 bg-white p-6",
        className,
      )}
    >
      <header className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">{t("title")}</h2>
        <span className="text-xs text-gray-500">{t(`stepLabel.${step}`)}</span>
      </header>

      {errorKey && (
        <p
          className="rounded-md border border-red-200 bg-red-50 p-2 text-sm text-red-700"
          role="alert"
        >
          {tDynamic(tErr, errorKey, tErr("generic"))}
        </p>
      )}

      {step === "type" && (
        <form onSubmit={handleTypeSubmit} className="space-y-3">
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium text-gray-700">{t("typeLegend")}</legend>
            {offeredTypes.map((option) => (
              <label
                key={option}
                className={cn(
                  "flex cursor-pointer items-start gap-3 rounded-md border p-3 text-sm",
                  type === option
                    ? "border-blue-500 bg-blue-50"
                    : "border-gray-300 hover:border-gray-400",
                )}
              >
                <input
                  type="radio"
                  name="dispute-type"
                  value={option}
                  checked={type === option}
                  onChange={() => setType(option)}
                  className="mt-1"
                  required
                />
                <span>
                  <span className="block font-medium">{t(`type.${option}.title`)}</span>
                  <span className="block text-xs text-gray-600">
                    {t(`type.${option}.description`)}
                  </span>
                </span>
              </label>
            ))}
          </fieldset>
          <div className="flex justify-end gap-2">
            {onClose && (
              <button
                type="button"
                onClick={onClose}
                className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 hover:bg-gray-50"
              >
                {t("cancel")}
              </button>
            )}
            <button
              type="submit"
              disabled={!type}
              className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {t("continue")}
            </button>
          </div>
        </form>
      )}

      {(step === "checking" || step === "txhashChecking") && (
        <div className="flex flex-col items-center gap-3 py-8" role="status">
          <Spinner size="lg" label={t("checking")} />
          <p className="text-sm text-gray-600">{t("checking")}</p>
        </div>
      )}

      {step === "result" && (
        <div
          className={cn(
            "rounded-md p-4 text-sm",
            resolved ? "bg-green-50 text-green-900" : "bg-yellow-50 text-yellow-900",
          )}
        >
          <p className="font-medium">
            {resolved
              ? t("result.resolved.title")
              : isExistingFlow
                ? t("result.existing.title")
                : t("result.unresolved.title")}
          </p>
          {message && <p className="mt-1 whitespace-pre-line">{message}</p>}
          <div className="mt-3 flex flex-wrap justify-end gap-2">
            {!resolved && canSubmitTxHash && (
              <button
                type="button"
                onClick={() => {
                  setErrorKey(null);
                  setTxHash("");
                  setStep("txhash");
                }}
                className="rounded-md border border-blue-300 bg-white px-3 py-2 text-xs font-medium text-blue-700 hover:bg-blue-50"
              >
                {t("submitTxHash")}
              </button>
            )}
            {!resolved && canEscalate && (
              <button
                type="button"
                onClick={() => {
                  setErrorKey(null);
                  setStep("escalation");
                }}
                className="rounded-md bg-blue-600 px-3 py-2 text-xs font-medium text-white hover:bg-blue-700"
              >
                {t("escalate")}
              </button>
            )}
            {onClose && (
              <button
                type="button"
                onClick={onClose}
                className="rounded-md border border-gray-300 bg-white px-3 py-2 text-xs font-medium text-gray-700 hover:bg-gray-50"
              >
                {t("close")}
              </button>
            )}
          </div>
        </div>
      )}

      {step === "txhash" && (
        <form onSubmit={handleTxHashSubmit} className="space-y-3">
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-gray-700">{t("txHashLabel")}</span>
            <input
              type="text"
              value={txHash}
              onChange={(e) => setTxHash(e.target.value)}
              required
              autoComplete="off"
              spellCheck={false}
              className="rounded-md border border-gray-300 bg-white px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2 focus:ring-blue-200"
              placeholder={t("txHashPlaceholder")}
            />
            <span className="text-xs text-gray-500">{t("txHashHint")}</span>
          </label>
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => {
                setErrorKey(null);
                setStep("result");
              }}
              className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 hover:bg-gray-50"
            >
              {t("back")}
            </button>
            <button
              type="submit"
              disabled={txHash.trim().length === 0}
              className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {t("submitTxHashConfirm")}
            </button>
          </div>
        </form>
      )}

      {step === "escalation" && (
        <form onSubmit={handleEscalate} className="space-y-3">
          <label className="flex flex-col gap-1 text-sm">
            <span className="font-medium text-gray-700">{t("detailLabel")}</span>
            <textarea
              value={detail}
              onChange={(e) => setDetail(e.target.value)}
              rows={5}
              minLength={10}
              required
              className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-200"
            />
            <span className="text-xs text-gray-500">{t("detailHint")}</span>
          </label>
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => {
                setErrorKey(null);
                setStep("result");
              }}
              className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm text-gray-700 hover:bg-gray-50"
            >
              {t("back")}
            </button>
            <button
              type="submit"
              disabled={submitting || detail.trim().length < 10}
              className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {submitting ? <Spinner size="sm" /> : t("submit")}
            </button>
          </div>
        </form>
      )}

      {step === "done" && (
        <div className="rounded-md bg-green-50 p-4 text-sm text-green-900" role="status">
          <p className="font-medium">{t("submittedTitle")}</p>
          <p className="mt-1">{t("submittedDescription")}</p>
          {onClose && (
            <div className="mt-3 flex justify-end">
              <button
                type="button"
                onClick={onClose}
                className="rounded-md border border-green-200 bg-white px-3 py-2 text-xs font-medium text-green-800"
              >
                {t("close")}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * Map an `ApiError`-shaped thrown value to a translation key. Defers to
 * the structured `code` field when present; falls back to `generic` for
 * non-API throws (network errors, parse failures, etc.).
 */
function extractErrorKey(err: unknown): string {
  if (
    err &&
    typeof err === "object" &&
    "code" in err &&
    typeof (err as { code: unknown }).code === "string"
  ) {
    return (err as { code: string }).code;
  }
  return "generic";
}
