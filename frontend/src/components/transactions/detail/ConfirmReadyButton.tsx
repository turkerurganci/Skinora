"use client";

import { useState } from "react";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { confirmReady } from "@/lib/api/transactions";

export interface ConfirmReadyButtonProps {
  transactionId: string;
  /** Server-derived `availableActions.canConfirmReady` (07 §7.5). */
  canConfirmReady: boolean;
  isSuspended: boolean;
  onConfirmed: () => void;
}

/**
 * 04 §7.3 ACCEPTED × satıcı — **[Göndermeye Hazırım]**.
 *
 * The seller asserts the sale can still happen; the platform then runs the
 * three 03 §2.3 checks itself (item still in the inventory and tradeable, the
 * buyer's Mobile Authenticator still active, the buyer's delivery baseline).
 * Nothing is submitted, so this is a button rather than a form — the whole
 * point of the step is that the *platform* verifies, not the seller.
 *
 * Failure copy is per error code because the codes prescribe different actions
 * (07 §7.6a):
 *   • ITEM_NO_LONGER_AVAILABLE — a positive finding: the item really is gone or
 *     untradeable. Paired with the cancel hint 04 §7.3 asks for.
 *   • INVENTORY_PRIVATE — an absence of information about the SELLER's own
 *     profile. The instruction is "open your profile", not "find your item".
 *   • BUYER_MOBILE_AUTHENTICATOR_INACTIVE — the fix belongs to the other party,
 *     so the message must not read as "enable your authenticator".
 *   • STEAM_UNAVAILABLE — retryable; everything else here is not.
 *
 * On success the caller refetches. The "buyer's inventory is hidden" warning is
 * deliberately NOT raised here: it is a standing condition for the rest of the
 * transaction, so it is read off the detail envelope's `buyerInventoryVisible`
 * (07 §7.5) in the states that follow — where a reload keeps it and where the
 * buyer, who carries the resulting obligation, can see it too.
 */
export function ConfirmReadyButton({
  transactionId,
  canConfirmReady,
  isSuspended,
  onConfirmed,
}: ConfirmReadyButtonProps) {
  const t = useTranslations("transactionDetail.actions.accepted.seller");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [errorCode, setErrorCode] = useState<string | null>(null);

  const disabled = !canConfirmReady || isSuspended || submitting;

  async function handleClick() {
    if (disabled) return;
    setSubmitting(true);
    setError(null);
    setErrorCode(null);
    try {
      await confirmReady(transactionId);
      onConfirmed();
    } catch (err) {
      if (err instanceof ApiError) {
        const code = err.code;
        setErrorCode(code ?? null);
        setError(t.has(`errors.${code}`) ? t(`errors.${code}`) : t("errors.generic"));
      } else {
        setError(t("errors.generic"));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="space-y-3 rounded-md border border-blue-200 bg-blue-50 p-3">
      <p className="text-sm text-blue-900">{t("title")}</p>
      <button
        type="button"
        data-testid="confirm-ready-submit"
        disabled={disabled}
        onClick={handleClick}
        className="w-full rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50 sm:w-auto"
      >
        {submitting ? t("submitting") : t("cta")}
      </button>
      {error && (
        <div
          className="space-y-1 rounded-md border border-red-200 bg-red-50 p-2 text-sm text-red-700"
          role="alert"
          data-testid="confirm-ready-error"
        >
          <p>{error}</p>
          {errorCode === "ITEM_NO_LONGER_AVAILABLE" && (
            <p className="text-xs text-red-600">{t("itemGoneHint")}</p>
          )}
        </div>
      )}
      {isSuspended && <p className="text-xs text-gray-600">{t("suspendedHint")}</p>}
    </div>
  );
}
