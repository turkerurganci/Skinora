"use client";

import { FormEvent, useEffect, useRef, useState } from "react";

import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export interface CancelModalProps {
  open: boolean;
  refundDescription?: string;
  onConfirm: (reason: string) => void;
  onClose: () => void;
  minReasonLength?: number;
  className?: string;
}

interface CancelModalFormProps {
  refundDescription?: string;
  minReasonLength: number;
  onConfirm: (reason: string) => void;
  onClose: () => void;
}

function CancelModalForm({
  refundDescription,
  minReasonLength,
  onConfirm,
  onClose,
}: CancelModalFormProps) {
  const t = useTranslations("cancelModal");
  const [reason, setReason] = useState("");
  const [touched, setTouched] = useState(false);

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setTouched(true);
    if (reason.trim().length < minReasonLength) return;
    onConfirm(reason.trim());
  }

  const tooShort = touched && reason.trim().length < minReasonLength;

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-6">
      <h2 id="cancel-modal-title" className="text-lg font-semibold text-gray-900">
        {t("title")}
      </h2>
      <label className="flex flex-col gap-1 text-sm">
        <span className="font-medium text-gray-700">
          {t("reasonLabel")}{" "}
          <span className="text-gray-500">({t("minChars", { count: minReasonLength })})</span>
        </span>
        <textarea
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          onBlur={() => setTouched(true)}
          minLength={minReasonLength}
          required
          rows={4}
          className={cn(
            "rounded-md border bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2",
            tooShort ? "border-red-300 focus:ring-red-200" : "border-gray-300 focus:ring-blue-200",
          )}
        />
        {tooShort && <span className="text-xs text-red-600">{t("tooShort")}</span>}
      </label>
      {refundDescription && (
        <p className="rounded-md bg-blue-50 px-3 py-2 text-sm text-blue-800">{refundDescription}</p>
      )}
      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          {t("dismiss")}
        </button>
        <button
          type="submit"
          className="rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50"
          disabled={reason.trim().length < minReasonLength}
        >
          {t("confirm")}
        </button>
      </div>
    </form>
  );
}

export function CancelModal({
  open,
  refundDescription,
  onConfirm,
  onClose,
  minReasonLength = 10,
  className,
}: CancelModalProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);

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

  return (
    <dialog
      ref={dialogRef}
      className={cn("rounded-lg p-0 backdrop:bg-black/50 w-full max-w-md", className)}
      aria-labelledby="cancel-modal-title"
    >
      {open && (
        <CancelModalForm
          refundDescription={refundDescription}
          minReasonLength={minReasonLength}
          onConfirm={onConfirm}
          onClose={onClose}
        />
      )}
    </dialog>
  );
}
