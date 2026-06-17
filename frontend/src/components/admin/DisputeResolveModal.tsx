"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";
import { DisputeResolutionOutcome } from "@/types/enums";
import type { AdminDisputeListItem } from "@/lib/api/admin";
import { useAdminDisputeDetail } from "@/lib/hooks/useAdminDisputeDetail";
import { useAdminDisputeResolve } from "@/lib/hooks/useAdminDisputeResolve";

const MIN_NOTE = 1;
const MAX_NOTE = 2000;

export interface DisputeResolveModalProps {
  /** The dispute being resolved; `null` keeps the modal closed. */
  dispute: AdminDisputeListItem | null;
  onClose: () => void;
}

/**
 * WP5 — admin dispute resolve modal (AD28 detail + AD29 resolve, 07 §9.x).
 * Fetches the full dispute on open, lets the admin pick an outcome
 * (seller-favor / buyer-favor) + a required note, then resolves.
 */
export function DisputeResolveModal({ dispute, onClose }: DisputeResolveModalProps) {
  const open = dispute !== null;
  const dialogRef = useRef<HTMLDialogElement>(null);
  const t = useTranslations("adminDisputes");

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
      className="w-full max-w-lg rounded-lg p-0 backdrop:bg-black/50"
      aria-label={t("resolve.title")}
    >
      {/* Remount on open so the form resets between disputes. */}
      {open && <DisputeResolveForm dispute={dispute} onClose={onClose} />}
    </dialog>
  );
}

function DisputeResolveForm({
  dispute,
  onClose,
}: {
  dispute: AdminDisputeListItem;
  onClose: () => void;
}) {
  const t = useTranslations("adminDisputes");
  const tType = useTranslations("adminDisputes.type");
  const { data: detail, isLoading } = useAdminDisputeDetail(dispute.id);
  const resolve = useAdminDisputeResolve();

  const [outcome, setOutcome] = useState<DisputeResolutionOutcome | null>(null);
  const [note, setNote] = useState("");
  const [touched, setTouched] = useState(false);

  const trimmed = note.trim();
  const noteTooShort = trimmed.length < MIN_NOTE;
  const noteTooLong = trimmed.length > MAX_NOTE;

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setTouched(true);
    if (outcome === null || noteTooShort || noteTooLong) return;
    resolve.mutate({ id: dispute.id, outcome, adminNote: trimmed }, { onSuccess: () => onClose() });
  }

  const outcomes: DisputeResolutionOutcome[] = [
    DisputeResolutionOutcome.SELLER_FAVOR,
    DisputeResolutionOutcome.BUYER_FAVOR,
  ];

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-6">
      <h2 className="text-lg font-semibold text-gray-900">{t("resolve.title")}</h2>

      {/* Dispute summary */}
      <dl className="grid grid-cols-3 gap-1 rounded-md bg-gray-50 p-3 text-sm">
        <dt className="text-gray-500">{t("resolve.fields.type")}</dt>
        <dd className="col-span-2 text-gray-900">{tType(dispute.type)}</dd>
        <dt className="text-gray-500">{t("resolve.fields.item")}</dt>
        <dd className="col-span-2 text-gray-900">{dispute.itemName}</dd>
        <dt className="text-gray-500">{t("resolve.fields.openedBy")}</dt>
        <dd className="col-span-2 text-gray-900">{dispute.openedBy.displayName}</dd>
        {detail?.userDescription && (
          <>
            <dt className="text-gray-500">{t("resolve.fields.userDescription")}</dt>
            <dd className="col-span-2 whitespace-pre-wrap text-gray-900">
              {detail.userDescription}
            </dd>
          </>
        )}
        {detail?.systemCheckResult && (
          <>
            <dt className="text-gray-500">{t("resolve.fields.systemCheckResult")}</dt>
            <dd className="col-span-2 text-gray-700">{detail.systemCheckResult}</dd>
          </>
        )}
      </dl>

      {/* Outcome */}
      <fieldset className="flex flex-col gap-2">
        <legend className="mb-1 text-sm font-medium text-gray-700">
          {t("resolve.outcomeLabel")}
        </legend>
        {outcomes.map((value) => (
          <label
            key={value}
            className={cn(
              "flex cursor-pointer items-start gap-2 rounded-md border p-2 text-sm",
              outcome === value ? "border-blue-400 bg-blue-50" : "border-gray-200",
            )}
          >
            <input
              type="radio"
              name="dispute-outcome"
              value={value}
              checked={outcome === value}
              onChange={() => setOutcome(value)}
              className="mt-0.5"
            />
            <span>
              <span className="font-medium text-gray-900">{t(`resolve.outcome.${value}`)}</span>
              <span className="block text-xs text-gray-500">
                {t(`resolve.outcomeHint.${value}`)}
              </span>
            </span>
          </label>
        ))}
        {touched && outcome === null && (
          <span className="text-xs text-red-600">{t("resolve.outcomeRequired")}</span>
        )}
      </fieldset>

      {/* Note */}
      <label className="flex flex-col gap-1 text-sm">
        <span className="font-medium text-gray-700">{t("resolve.noteLabel")}</span>
        <textarea
          value={note}
          onChange={(e) => setNote(e.target.value)}
          onBlur={() => setTouched(true)}
          placeholder={t("resolve.notePlaceholder")}
          rows={3}
          maxLength={MAX_NOTE}
          required
          className={cn(
            "rounded-md border bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2",
            touched && (noteTooShort || noteTooLong)
              ? "border-red-300 focus:ring-red-200"
              : "border-gray-300 focus:ring-blue-200",
          )}
        />
        {touched && noteTooShort && (
          <span className="text-xs text-red-600">{t("resolve.noteRequired")}</span>
        )}
      </label>

      {resolve.isError && <p className="text-sm text-red-600">{t("resolve.error")}</p>}

      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onClose}
          className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          {t("resolve.cancel")}
        </button>
        <button
          type="submit"
          disabled={resolve.isPending || isLoading}
          className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {t("resolve.confirm")}
        </button>
      </div>
    </form>
  );
}
