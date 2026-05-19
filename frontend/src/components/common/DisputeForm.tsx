"use client";

import { FormEvent, useState } from "react";
import { useTranslations } from "next-intl";
import { DisputeType } from "@/types/enums";
import { Spinner } from "./LoadingState";
import { cn } from "@/lib/utils/cn";

export type DisputeAutoCheckOutcome = "resolved" | "unresolved";

export interface DisputeFormProps {
  onAutoCheck: (type: DisputeType) => Promise<DisputeAutoCheckOutcome>;
  onEscalate: (type: DisputeType, detail: string) => Promise<void>;
  onClose?: () => void;
  className?: string;
}

type Step = "type" | "checking" | "result" | "escalation" | "done";

export function DisputeForm({ onAutoCheck, onEscalate, onClose, className }: DisputeFormProps) {
  const t = useTranslations("disputeForm");
  const [step, setStep] = useState<Step>("type");
  const [type, setType] = useState<DisputeType | null>(null);
  const [outcome, setOutcome] = useState<DisputeAutoCheckOutcome | null>(null);
  const [detail, setDetail] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function handleTypeSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!type) return;
    setStep("checking");
    try {
      const result = await onAutoCheck(type);
      setOutcome(result);
      setStep("result");
    } catch {
      setOutcome("unresolved");
      setStep("result");
    }
  }

  async function handleEscalate(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!type || detail.trim().length < 10) return;
    setSubmitting(true);
    try {
      await onEscalate(type, detail.trim());
      setStep("done");
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

      {step === "type" && (
        <form onSubmit={handleTypeSubmit} className="space-y-3">
          <fieldset className="space-y-2">
            <legend className="text-sm font-medium text-gray-700">{t("typeLegend")}</legend>
            {([DisputeType.PAYMENT, DisputeType.DELIVERY, DisputeType.WRONG_ITEM] as const).map(
              (option) => (
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
              ),
            )}
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

      {step === "checking" && (
        <div className="flex flex-col items-center gap-3 py-8" role="status">
          <Spinner size="lg" label={t("checking")} />
          <p className="text-sm text-gray-600">{t("checking")}</p>
        </div>
      )}

      {step === "result" && outcome && (
        <div
          className={cn(
            "rounded-md p-4 text-sm",
            outcome === "resolved" ? "bg-green-50 text-green-900" : "bg-yellow-50 text-yellow-900",
          )}
        >
          <p className="font-medium">{t(`result.${outcome}.title`)}</p>
          <p className="mt-1">{t(`result.${outcome}.description`)}</p>
          <div className="mt-3 flex justify-end gap-2">
            {outcome === "unresolved" && (
              <button
                type="button"
                onClick={() => setStep("escalation")}
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
          </label>
          <div className="flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setStep("result")}
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
