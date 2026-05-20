"use client";

import { useEffect, useId, useRef, useState } from "react";
import { useTranslations } from "next-intl";
import { cn } from "@/lib/utils/cn";

export interface TosModalProps {
  open: boolean;
  tosVersion: string;
  tosHref?: string;
  submitting?: boolean;
  errorMessage?: string | null;
  onAccept: (payload: { tosVersion: string; ageOver18: true }) => void;
  onAgeRejected?: () => void;
  className?: string;
}

export function TosModal({
  open,
  tosVersion,
  tosHref = "/terms",
  submitting = false,
  errorMessage,
  onAccept,
  onAgeRejected,
  className,
}: TosModalProps) {
  const t = useTranslations("auth.tos");
  const titleId = useId();
  const descId = useId();
  const [ageChecked, setAgeChecked] = useState(false);
  const [tosChecked, setTosChecked] = useState(false);
  const ageInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    if (open) {
      ageInputRef.current?.focus();
    }
  }, [open]);

  if (!open) return null;

  const canSubmit = ageChecked && tosChecked && !submitting;

  const summaryKeys = ["escrow", "commission", "crypto", "disputes", "kyc"] as const;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      aria-describedby={descId}
      className={cn(
        "fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4",
        className,
      )}
    >
      <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
        <h2 id={titleId} className="text-xl font-semibold text-gray-900">
          {t("title")}
        </h2>
        <p id={descId} className="mt-2 text-sm text-gray-600">
          {t("description")}
        </p>

        <div className="mt-4 rounded-md bg-gray-50 p-4">
          <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">
            {t("summaryHeading")}
          </p>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-gray-700">
            {summaryKeys.map((key) => (
              <li key={key}>{t(`summary.${key}`)}</li>
            ))}
          </ul>
        </div>

        <fieldset className="mt-5 space-y-3">
          <label className="flex items-start gap-3">
            <input
              ref={ageInputRef}
              type="checkbox"
              checked={ageChecked}
              onChange={(e) => setAgeChecked(e.target.checked)}
              className="mt-1 h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              data-testid="tos-age-checkbox"
            />
            <span className="text-sm text-gray-800">{t("ageCheckbox")}</span>
          </label>
          <label className="flex items-start gap-3">
            <input
              type="checkbox"
              checked={tosChecked}
              onChange={(e) => setTosChecked(e.target.checked)}
              className="mt-1 h-4 w-4 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
              data-testid="tos-accept-checkbox"
            />
            <span className="text-sm text-gray-800">
              {t.rich("tosCheckbox", {
                link: (chunks) => (
                  <a
                    href={tosHref}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="font-medium text-blue-600 underline-offset-2 hover:underline"
                  >
                    {chunks}
                  </a>
                ),
              })}
            </span>
          </label>
        </fieldset>

        {errorMessage && (
          <p className="mt-4 text-sm text-red-600" role="alert">
            {errorMessage}
          </p>
        )}

        <div className="mt-6 flex flex-col gap-2">
          <button
            type="button"
            disabled={!canSubmit}
            aria-disabled={!canSubmit}
            aria-busy={submitting || undefined}
            onClick={() => canSubmit && onAccept({ tosVersion, ageOver18: true })}
            className={cn(
              "inline-flex w-full items-center justify-center rounded-md px-4 py-2.5 text-sm font-semibold shadow-sm focus:outline-none focus:ring-2 focus:ring-offset-2",
              canSubmit
                ? "bg-blue-600 text-white hover:bg-blue-700 focus:ring-blue-500"
                : "cursor-not-allowed bg-gray-300 text-gray-600",
            )}
            data-testid="tos-submit"
          >
            {submitting ? t("submitting") : t("continue")}
          </button>
          {onAgeRejected && (
            <button
              type="button"
              onClick={onAgeRejected}
              className="text-xs text-gray-500 hover:text-gray-700 hover:underline"
            >
              {t("notEligible")}
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
