"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { acceptTransaction } from "@/lib/api/transactions";
import { tDynamic } from "@/lib/i18n/dynamicKey";

export interface AcceptFormProps {
  transactionId: string;
  defaultRefundAddress: string | null;
  /**
   * T119a — trade URL saved on the buyer's profile (U17), used to prefill the
   * mandatory field. Null when they never saved one; the input then starts
   * empty and they paste it here.
   */
  defaultSteamTradeUrl: string | null;
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
 * T119a — v3.0'da forma ikinci bir zorunlu alan eklendi: **Steam trade URL**.
 * P2P modelinde item satıcıdan alıcıya doğrudan gider (02 §2.2 adım 6), yani
 * teslimat adresini alıcı burada verir. Refund adresinin aksine bu alan
 * profilde kayıtlı olsa bile düzenlenebilir kalır — alıcı Steam hesabının
 * trade URL'ini yenilemiş (token değişmiş) olabilir ve kabul anında
 * güncelini vermesi gerekir.
 *
 * Hata kodları (07 §7.6, TransactionErrorCodes):
 *   REFUND_ADDRESS_REQUIRED, INVALID_WALLET_ADDRESS, SANCTIONS_MATCH,
 *   WALLET_COOLDOWN_ACTIVE, STEAM_ID_MISMATCH, ALREADY_ACCEPTED,
 *   INVALID_STATE_TRANSITION, BUYER_NOT_FOUND,
 *   INVALID_TRADE_URL, MOBILE_AUTHENTICATOR_REQUIRED, STEAM_UNAVAILABLE.
 */
export function AcceptForm({
  transactionId,
  defaultRefundAddress,
  defaultSteamTradeUrl,
  disabled,
  disabledReason,
  onAccepted,
}: AcceptFormProps) {
  const t = useTranslations("transactionDetail.accept");
  const locale = useLocale();
  const [address, setAddress] = useState(defaultRefundAddress ?? "");
  const [tradeUrl, setTradeUrl] = useState(defaultSteamTradeUrl ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // T119a doğrulaması — 03 §3.2 adım 7, MA reddinde kullanıcının kurulum
  // rehberine yönlendirilmesini şart koşuyor. Kontrol tıklamadan sonra
  // sunucuda yapıldığı için tek yönlendirme noktası bu hata dalıdır; hangi
  // kodun geldiğini bilmek gerektiğinden mesajın yanında kodu da tutuyoruz.
  const [errorCode, setErrorCode] = useState<string | null>(null);
  const hasDefault = Boolean(defaultRefundAddress);
  const inputDisabled = hasDefault; // K4: per-tx override disabled

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (disabled) return;
    if (!address.trim()) {
      setErrorCode(null);
      setError(t("errors.REFUND_ADDRESS_REQUIRED"));
      return;
    }
    if (!tradeUrl.trim()) {
      setErrorCode(null);
      setError(t("errors.INVALID_TRADE_URL"));
      return;
    }
    setSubmitting(true);
    setError(null);
    setErrorCode(null);
    try {
      await acceptTransaction(transactionId, {
        refundWalletAddress: address.trim(),
        steamTradeUrl: tradeUrl.trim(),
      });
      onAccepted();
    } catch (err) {
      if (err instanceof ApiError) {
        const code = err.code;
        setErrorCode(code ?? null);
        setError(tDynamic(t, `errors.${code}`, t("errors.generic")));
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
          data-testid="accept-refund-input"
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
      <div className="space-y-1">
        <label htmlFor="steam-trade-url" className="text-sm font-medium text-gray-900">
          {t("tradeUrlLabel")}
        </label>
        <p className="text-xs text-gray-600">{t("tradeUrlHint")}</p>
        <input
          id="steam-trade-url"
          data-testid="accept-trade-url-input"
          type="text"
          value={tradeUrl}
          onChange={(e) => setTradeUrl(e.target.value)}
          disabled={submitting || disabled}
          autoComplete="off"
          placeholder="https://steamcommunity.com/tradeoffer/new/?partner=...&token=..."
          required
          className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 font-mono text-sm text-gray-900 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:bg-gray-100"
        />
        {defaultSteamTradeUrl && (
          <p className="text-xs text-gray-600">{t("tradeUrlUsingProfile")}</p>
        )}
      </div>
      {error && (
        <div
          className="space-y-1 rounded-md border border-red-200 bg-red-50 p-2 text-sm text-red-700"
          role="alert"
        >
          <p>{error}</p>
          {errorCode === "MOBILE_AUTHENTICATOR_REQUIRED" && (
            <Link
              href={`/${locale}/auth/mobile-authenticator`}
              data-testid="accept-ma-setup-link"
              className="inline-block font-medium underline"
            >
              {t("maSetupLink")}
            </Link>
          )}
        </div>
      )}
      {disabled && disabledReason && (
        <p className="rounded-md border border-gray-200 bg-gray-50 p-2 text-sm text-gray-700">
          {disabledReason}
        </p>
      )}
      <button
        type="submit"
        data-testid="accept-submit"
        disabled={submitting || disabled}
        className="w-full rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
      >
        {submitting ? t("submitting") : t("submit")}
      </button>
    </form>
  );
}
