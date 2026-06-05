"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils/cn";

export type FlagActionTone = "approve" | "reject" | "hold";

const TONE_CLASS: Record<FlagActionTone, string> = {
  approve: "bg-emerald-600 hover:bg-emerald-700",
  reject: "bg-red-600 hover:bg-red-700",
  hold: "bg-red-600 hover:bg-red-700",
};

export interface FlagActionReasonConfig {
  label: string;
  placeholder?: string;
  minLength: number;
  tooShort: string;
  hint?: string;
}

export interface FlagActionModalProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel: string;
  cancelLabel: string;
  tone: FlagActionTone;
  /**
   * When supplied, a required reason textarea (≥ `minLength`) is rendered and
   * its trimmed value is passed to `onConfirm`. When omitted the modal is a
   * simple yes/no confirmation and `onConfirm` receives `undefined` — the
   * approve/reject note then comes from the page-level "Admin Notu" field
   * (04 §8.3).
   */
  reason?: FlagActionReasonConfig;
  pending?: boolean;
  onConfirm: (reason?: string) => void;
  onClose: () => void;
  className?: string;
}

interface FlagActionFormProps {
  description: string;
  confirmLabel: string;
  cancelLabel: string;
  tone: FlagActionTone;
  reason?: FlagActionReasonConfig;
  pending?: boolean;
  onConfirm: (reason?: string) => void;
  onClose: () => void;
}

function FlagActionForm({
  description,
  confirmLabel,
  cancelLabel,
  tone,
  reason,
  pending,
  onConfirm,
  onClose,
}: FlagActionFormProps) {
  const [value, setValue] = useState("");
  const [touched, setTouched] = useState(false);

  const trimmed = value.trim();
  const tooShort = reason !== undefined && trimmed.length < reason.minLength;

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (reason !== undefined) {
      setTouched(true);
      if (tooShort) return;
      onConfirm(trimmed);
      return;
    }
    onConfirm(undefined);
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-6">
      <h2 id="flag-action-modal-title" className="text-lg font-semibold text-gray-900">
        {description}
      </h2>

      {reason && (
        <label className="flex flex-col gap-1 text-sm">
          <span className="font-medium text-gray-700">
            {reason.label} <span className="text-gray-500">({reason.tooShort})</span>
          </span>
          <textarea
            value={value}
            onChange={(e) => setValue(e.target.value)}
            onBlur={() => setTouched(true)}
            placeholder={reason.placeholder}
            minLength={reason.minLength}
            required
            rows={4}
            className={cn(
              "rounded-md border bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2",
              touched && tooShort
                ? "border-red-300 focus:ring-red-200"
                : "border-gray-300 focus:ring-blue-200",
            )}
          />
          {reason.hint && <span className="text-xs text-gray-500">{reason.hint}</span>}
          {touched && tooShort && <span className="text-xs text-red-600">{reason.tooShort}</span>}
        </label>
      )}

      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          {cancelLabel}
        </button>
        <button
          type="submit"
          disabled={pending || (reason !== undefined && tooShort)}
          className={cn(
            "rounded-md px-3 py-2 text-sm font-medium text-white disabled:opacity-50",
            TONE_CLASS[tone],
          )}
        >
          {confirmLabel}
        </button>
      </div>
    </form>
  );
}

/**
 * Confirmation modal for the S14 flag-review actions (approve / reject / hold).
 * Mirrors the `<dialog>` mechanics of {@link import("@/components/common").CancelModal}
 * but adds tone theming and an optional required-reason textarea so a single
 * component covers the "emin misiniz?" confirm (approve/reject) and the
 * reason-bearing Hold action (03 §8.8, reason ≥ 10).
 */
export function FlagActionModal({
  open,
  title,
  description,
  confirmLabel,
  cancelLabel,
  tone,
  reason,
  pending,
  onConfirm,
  onClose,
  className,
}: FlagActionModalProps) {
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
      className={cn("w-full max-w-md rounded-lg p-0 backdrop:bg-black/50", className)}
      aria-labelledby="flag-action-modal-title"
      aria-label={title}
    >
      {open && (
        // Remount on open so the reason field resets between actions.
        <FlagActionForm
          description={description}
          confirmLabel={confirmLabel}
          cancelLabel={cancelLabel}
          tone={tone}
          reason={reason}
          pending={pending}
          onConfirm={onConfirm}
          onClose={onClose}
        />
      )}
    </dialog>
  );
}
