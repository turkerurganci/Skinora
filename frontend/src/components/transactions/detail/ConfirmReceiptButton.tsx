"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { confirmReceipt } from "@/lib/api/transactions";

export interface ConfirmReceiptButtonProps {
  transactionId: string;
  /** Server-derived `availableActions.canConfirmReceipt` (07 §7.5). */
  canConfirmReceipt: boolean;
  isSuspended: boolean;
  onConfirmed: () => void;
}

/**
 * 04 §7.3 PAYMENT_RECEIVED × alıcı — **[Teslim Aldım]**.
 *
 * The confirmation is against the buyer's own interest — once given, the payout
 * is released to the seller — which is exactly why it counts as sufficient
 * proof on its own (06 §2.24, 03 §3.5) and why 04 §7.3 requires an explicit
 * confirmation dialog before it is sent: it cannot be taken back.
 *
 * It is also not the only route to ITEM_DELIVERED. When the buyer's inventory
 * is readable the evidence engine can verify delivery without them (02 §9.2),
 * so this button is an accelerator in the normal case and the ONLY route when
 * the baseline could not be taken — which is what the sibling
 * `InventoryHiddenNotice` tells the buyer.
 *
 * The endpoint is idempotent (07 §7.6b): a repeat on an already-delivered
 * transaction answers 200 with the current state, so a double submit or a retry
 * after a dropped response is not an error path.
 */
export function ConfirmReceiptButton({
  transactionId,
  canConfirmReceipt,
  isSuspended,
  onConfirmed,
}: ConfirmReceiptButtonProps) {
  const t = useTranslations("transactionDetail.actions.paymentReceived.buyer");
  const [open, setOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const dialogRef = useRef<HTMLDialogElement>(null);

  const disabled = !canConfirmReceipt || isSuspended || submitting;

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    else if (!open && dialog.open) dialog.close();
  }, [open]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    const handleCancel = (e: Event) => {
      e.preventDefault();
      if (!submitting) setOpen(false);
    };
    dialog.addEventListener("cancel", handleCancel);
    return () => dialog.removeEventListener("cancel", handleCancel);
  }, [submitting]);

  async function handleConfirm() {
    setSubmitting(true);
    setError(null);
    try {
      await confirmReceipt(transactionId);
      setOpen(false);
      onConfirmed();
    } catch (err) {
      if (err instanceof ApiError) {
        const code = err.code;
        setError(t.has(`errors.${code}`) ? t(`errors.${code}`) : t("errors.generic"));
      } else {
        setError(t("errors.generic"));
      }
      setOpen(false);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="space-y-3 rounded-md border border-yellow-300 bg-yellow-50 p-3">
      <p className="text-sm text-yellow-900">{t("title")}</p>
      <button
        type="button"
        data-testid="confirm-receipt-open"
        disabled={disabled}
        onClick={() => setOpen(true)}
        className="w-full rounded-md bg-green-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-green-700 disabled:cursor-not-allowed disabled:opacity-50 sm:w-auto"
      >
        {t("cta")}
      </button>
      {error && (
        <p
          className="rounded-md border border-red-200 bg-red-50 p-2 text-sm text-red-700"
          role="alert"
          data-testid="confirm-receipt-error"
        >
          {error}
        </p>
      )}

      <dialog
        ref={dialogRef}
        className="w-full max-w-md rounded-lg p-0 backdrop:bg-black/50"
        aria-labelledby="confirm-receipt-title"
      >
        {open && (
          <div className="flex flex-col gap-4 p-6">
            <h2 id="confirm-receipt-title" className="text-lg font-semibold text-gray-900">
              {t("confirmModal.title")}
            </h2>
            <p className="text-sm text-gray-700">{t("confirmModal.body")}</p>
            <div className="flex justify-end gap-2">
              <button
                type="button"
                data-testid="confirm-receipt-dismiss"
                onClick={() => setOpen(false)}
                disabled={submitting}
                className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
              >
                {t("confirmModal.dismiss")}
              </button>
              <button
                type="button"
                data-testid="confirm-receipt-submit"
                onClick={handleConfirm}
                disabled={submitting}
                className="rounded-md bg-green-600 px-3 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50"
              >
                {submitting ? t("submitting") : t("confirmModal.confirm")}
              </button>
            </div>
          </div>
        )}
      </dialog>
    </div>
  );
}
