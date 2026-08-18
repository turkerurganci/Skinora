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

// T131 — mirrors AdminDisputeService.MinOverrideReasonLength / Max… (03 §6.4).
// The server is the authority; these only keep the admin from submitting into a
// rejection they could have been told about while typing.
const MIN_OVERRIDE_REASON = 20;
const MAX_OVERRIDE_REASON = 2000;

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
  const [overrideReason, setOverrideReason] = useState("");
  const [touched, setTouched] = useState(false);

  const trimmed = note.trim();
  const noteTooShort = trimmed.length < MIN_NOTE;
  const noteTooLong = trimmed.length > MAX_NOTE;

  // T131 — the transaction's delivery is already established, so ruling for the
  // buyer reverses the platform's own finding and hands the loss to a seller
  // who cannot get the item back (03 §6.4). Whether that applies is decided by
  // the server (`buyerFavorRequiresOverride`), never re-derived here.
  const overrideRequired =
    outcome === DisputeResolutionOutcome.BUYER_FAVOR && detail?.buyerFavorRequiresOverride === true;
  const trimmedOverride = overrideReason.trim();
  const overrideTooShort = trimmedOverride.length < MIN_OVERRIDE_REASON;
  const overrideTooLong = trimmedOverride.length > MAX_OVERRIDE_REASON;
  const overrideInvalid = overrideRequired && (overrideTooShort || overrideTooLong);

  // The expected side of the comparison is read from the DETAIL, so both halves
  // come from one fetch and cannot disagree; the list row is only the
  // placeholder until it lands.
  const expectedItemName = detail?.transaction.itemName ?? dispute.itemName;

  function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setTouched(true);
    if (outcome === null || noteTooShort || noteTooLong || overrideInvalid) return;
    resolve.mutate(
      {
        id: dispute.id,
        outcome,
        adminNote: trimmed,
        overrideReason: overrideRequired ? trimmedOverride : undefined,
      },
      { onSuccess: () => onClose() },
    );
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
        <dd className="col-span-2 text-gray-900">{expectedItemName}</dd>
        {/*
          T130 evidence, surfaced by T131. Rendered directly beneath the
          expected item so the admin reads the comparison instead of making it
          (03 §6.3 Sonuç B). Absent means "not a wrong-item case" — not
          "unknown" — so the row is omitted rather than shown empty.
        */}
        {detail?.deliveredItemName && (
          <>
            <dt className="text-gray-500">{t("resolve.fields.deliveredItemName")}</dt>
            <dd className="col-span-2 font-medium text-amber-700">{detail.deliveredItemName}</dd>
          </>
        )}
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
        {detail?.resolutionOverrideReason && (
          <>
            <dt className="text-gray-500">{t("resolve.fields.overrideReason")}</dt>
            <dd className="col-span-2 whitespace-pre-wrap text-gray-700">
              {detail.resolutionOverrideReason}
            </dd>
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

      {/*
        T131 — the override gate (03 §6.4, 02 §10.4). Only drawn once the admin
        has actually chosen the buyer, so the ordinary ruling is not cluttered
        by a field it never needs.
      */}
      {overrideRequired && (
        <label className="flex flex-col gap-1 rounded-md border border-amber-300 bg-amber-50 p-3 text-sm">
          <span className="font-medium text-amber-900">{t("resolve.overrideLabel")}</span>
          <span className="text-xs text-amber-800">{t("resolve.overrideHint")}</span>
          <textarea
            value={overrideReason}
            onChange={(e) => setOverrideReason(e.target.value)}
            onBlur={() => setTouched(true)}
            placeholder={t("resolve.overridePlaceholder")}
            rows={3}
            minLength={MIN_OVERRIDE_REASON}
            maxLength={MAX_OVERRIDE_REASON}
            required
            className={cn(
              "rounded-md border bg-white px-3 py-2 text-sm text-gray-900 focus:outline-none focus:ring-2",
              touched && (overrideTooShort || overrideTooLong)
                ? "border-red-300 focus:ring-red-200"
                : "border-amber-300 focus:ring-amber-200",
            )}
          />
          {touched && overrideTooShort && (
            <span className="text-xs text-red-600">
              {t("resolve.overrideRequired", { min: MIN_OVERRIDE_REASON })}
            </span>
          )}
        </label>
      )}

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
