"use client";

import { FormEvent, useState } from "react";
import { useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { acceptTransaction } from "@/lib/api/transactions";

export interface AcceptFormProps {
  transactionId: string;
  defaultRefundAddress: string | null;
  disabled?: boolean;
  disabledReason?: string;
  onAccepted: () => void;
}

/**
 * 04 §7.3 — CREATED state, alıcı görünümü. Profilde DefaultRefundAddress
 * varsa input prefilled + "Değiştir" linki (T-future K4 — disabled);
 * yoksa input boş + required. Accept submit edilince
 * POST /transactions/:id/accept; başarılıysa onAccepted callback → page
 * refresh / refetch.
 *
 * Hata kodları (07 §7.6, TransactionErrorCodes):
 *   REFUND_ADDRESS_REQUIRED, INVALID_WALLET_ADDRESS, SANCTIONS_MATCH,
 *   WALLET_COOLDOWN_ACTIVE, STEAM_ID_MISMATCH, ALREADY_ACCEPTED,
 *   INVALID_STATE_TRANSITION, BUYER_NOT_FOUND.
 */
export function AcceptForm({
  transactionId,
  defaultRefundAddress,
  disabled,
  disabledReason,
  onAccepted,
}: AcceptFormProps) {
  const t = useTranslations("transactionDetail.accept");
  const [address, setAddress] = useState(defaultRefundAddress ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const hasDefault = Boolean(defaultRefundAddress);
  const inputDisabled = hasDefault; // K4: per-tx override disabled

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (disabled) return;
    if (!address.trim()) {
      setError(t("errors.REFUND_ADDRESS_REQUIRED"));
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await acceptTransaction(transactionId, { refundWalletAddress: address.trim() });
      onAccepted();
    } catch (err) {
      if (err instanceof ApiError) {
        const code = err.code;
        setError(t.has(`errors.${code}`) ? t(`errors.${code}`) : t("errors.generic"));
      } else {
        setError(t("errors.generic"));
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form
      onSubmit={handleSubmit}
      className="space-y-3 rounded-lg border border-blue-300 bg-blue-50 p-4"
    >
      <div className="space-y-1">
        <label htmlFor="refund-address" className="text-sm font-medium text-gray-900">
          {t("addressLabel")}
        </label>
        <p className="text-xs text-gray-600">{t("addressHint")}</p>
        <input
          id="refund-address"
          type="text"
          value={address}
          onChange={(e) => setAddress(e.target.value)}
          disabled={inputDisabled || submitting || disabled}
          autoComplete="off"
          placeholder="TXyz..."
          required
          className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 font-mono text-sm text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:bg-gray-100"
        />
        {hasDefault && (
          <div className="flex items-center justify-between text-xs">
            <span className="text-gray-600">{t("usingDefault")}</span>
            <button
              type="button"
              disabled
              title={t("changeUnavailable")}
              className="text-blue-700 underline disabled:cursor-not-allowed disabled:text-gray-400 disabled:no-underline"
            >
              {t("change")}
            </button>
          </div>
        )}
      </div>
      {error && (
        <p
          className="rounded-md border border-red-200 bg-red-50 p-2 text-sm text-red-700"
          role="alert"
        >
          {error}
        </p>
      )}
      {disabled && disabledReason && (
        <p className="rounded-md border border-gray-200 bg-gray-50 p-2 text-sm text-gray-700">
          {disabledReason}
        </p>
      )}
      <button
        type="submit"
        disabled={submitting || disabled}
        className="w-full rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
      >
        {submitting ? t("submitting") : t("submit")}
      </button>
    </form>
  );
}
