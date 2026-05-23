"use client";

import { useEffect, useRef } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";

import { DisputeForm, type ExistingDisputeContext } from "@/components/common";
import { openDispute, submitDisputeTxHash, escalateDispute } from "@/lib/api/disputes";
import { cn } from "@/lib/utils/cn";

export interface DisputeModalProps {
  open: boolean;
  transactionId: string;
  /**
   * When set, the modal opens directly into the result-step of an existing
   * dispute (DisputeBlock entry point). Omitting it puts the modal into
   * the new-dispute creation flow (StateActionPanel "İtiraz Et" button).
   */
  existingDispute?: ExistingDisputeContext;
  onClose: () => void;
  className?: string;
}

/**
 * Dialog wrapper for the C07 dispute form (04 §5, T92). Mirrors the
 * `CancelModal` pattern — uses the native `<dialog>` element so the
 * browser handles focus trapping and ESC key handling.
 *
 * On any successful mutation the transaction detail query is invalidated;
 * the parent page refetches the dispute block + availableActions flags so
 * the UI matches the server's view of dispute state without a SignalR
 * push (T96 wires the live channel later).
 */
export function DisputeModal({
  open,
  transactionId,
  existingDispute,
  onClose,
  className,
}: DisputeModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const queryClient = useQueryClient();
  const t = useTranslations("transactionDetail.dispute");

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    if (open && !dialog.open) {
      dialog.showModal();
    } else if (!open && dialog.open) {
      dialog.close();
    }
  }, [open]);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) return;
    const handleCancel = (e: Event) => {
      e.preventDefault();
      onClose();
    };
    dialog.addEventListener("cancel", handleCancel);
    return () => dialog.removeEventListener("cancel", handleCancel);
  }, [onClose]);

  function invalidateDetail() {
    queryClient.invalidateQueries({ queryKey: ["transactions", "detail", transactionId] });
  }

  return (
    <dialog
      ref={dialogRef}
      className={cn("rounded-lg p-0 backdrop:bg-black/50 w-full max-w-lg", className)}
      aria-labelledby="dispute-modal-title"
    >
      {open && (
        <div className="p-2">
          <h2 id="dispute-modal-title" className="sr-only">
            {t("title")}
          </h2>
          <DisputeForm
            existingDispute={existingDispute}
            onOpen={async (type) => {
              const response = await openDispute(transactionId, { type });
              invalidateDetail();
              return {
                disputeId: response.id,
                type: response.type,
                resolved: response.autoCheckResult.resolved,
                message: response.autoCheckResult.message,
                canSubmitTxHash: response.autoCheckResult.canSubmitTxHash,
                canEscalate: response.autoCheckResult.canEscalate,
              };
            }}
            onSubmitTxHash={async (disputeId, txHash) => {
              const response = await submitDisputeTxHash(transactionId, disputeId, { txHash });
              invalidateDetail();
              return {
                resolved: response.checkResult.resolved,
                message: response.checkResult.message,
              };
            }}
            onEscalate={async (disputeId, _type, detail) => {
              await escalateDispute(transactionId, disputeId, { detail });
              invalidateDetail();
            }}
            onClose={onClose}
          />
        </div>
      )}
    </dialog>
  );
}
