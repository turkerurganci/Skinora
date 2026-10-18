"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ApiError } from "@/lib/api/client";
import { acceptTransaction, type TransactionNetworkQuote } from "@/lib/api/transactions";
import { findWallet, type UserWalletEntry } from "@/lib/api/users";
import { buyerNetworkOptions, showsNetworkFeeLine } from "@/lib/transactions/networkChoice";
import { checkAddressShape, networkLabel, networkMeta } from "@/lib/utils/blockchain";
import { formatStablecoin } from "@/lib/utils/format";
import { tDynamic } from "@/lib/i18n/dynamicKey";
import { BlockchainNetwork } from "@/types/enums";

export interface AcceptFormProps {
  transactionId: string;
  /**
   * T156 — the listing's `acceptedNetworks` and per-network quotes (07 §7.5).
   * Absent only from a server that predates T147, where every listing was
   * TRON-only; the form then offers TRON alone, which is exactly what that
   * server would have done with a body that names no network.
   */
  acceptedNetworks?: readonly string[] | null;
  networkQuotes?: readonly TransactionNetworkQuote[] | null;
  stablecoin: string;
  /** The buyer's saved addresses (07 §5.1 `wallets[]`) — refund address prefill per network. */
  profileWallets: readonly UserWalletEntry[];
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
 * 04 §7.3 — CREATED state, alıcı görünümü (03 §3.2 adım 4–6).
 *
 * T156 — the buyer picks ONE of the listing's networks first (02 §4.2). Each
 * choice shows what it costs, verbatim from `networkQuotes` (TRON: total with
 * the TRON network fee as a named part, K8; Ethereum: no fee part). With a
 * single network it is preselected; with several nothing is, so the buyer
 * makes the choice that decides where the money moves.
 *
 * The refund address belongs to the chosen network (02 §12.2, K9 — every
 * refund of this transaction, rejected payments included, goes there): the
 * profile's address for that network prefills the input (K4 — the per-tx
 * override stays disabled); without one the buyer types it, its shape is
 * checked for that network, and an Ethereum address without an EIP-55
 * checksum needs an explicit confirmation before submit (02 §12.3). The server
 * enforces it too (T156 verification, security): the accept body carries
 * `confirmLowercase: true` once the box is ticked, and a 400
 * `INVALID_WALLET_ADDRESS` with `details.reason = "CHECKSUM_CONFIRMATION_REQUIRED"`
 * brings the box up even when the local shape check did not (07 §7.6). This
 * covers a profile default too: the box is shown for it as well.
 *
 * T119a — **Steam trade URL** is mandatory (02 §2.2 adım 6) and stays editable
 * even when prefilled from the profile.
 */
export function AcceptForm({
  transactionId,
  acceptedNetworks,
  networkQuotes,
  stablecoin,
  profileWallets,
  defaultSteamTradeUrl,
  disabled,
  disabledReason,
  onAccepted,
}: AcceptFormProps) {
  const t = useTranslations("transactionDetail.accept");
  const locale = useLocale();
  const options = buyerNetworkOptions(acceptedNetworks ?? [BlockchainNetwork.TRON], networkQuotes);
  const [network, setNetwork] = useState<BlockchainNetwork | null>(
    options.length === 1 ? options[0].network : null,
  );
  const [typedAddress, setTypedAddress] = useState("");
  const [checksumConfirmed, setChecksumConfirmed] = useState(false);
  // The server asked for the second confirmation (400 CHECKSUM_CONFIRMATION_REQUIRED).
  const [serverWantsConfirm, setServerWantsConfirm] = useState(false);
  const [tradeUrl, setTradeUrl] = useState(defaultSteamTradeUrl ?? "");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // T119a doğrulaması — 03 §3.2 adım 7, MA reddinde kurulum rehberine yönlendirme.
  const [errorCode, setErrorCode] = useState<string | null>(null);

  const profileAddress = network ? findWallet(profileWallets, network, "REFUND") : null;
  const hasDefault = profileAddress !== null;
  const address = hasDefault ? profileAddress : typedAddress;
  const shape = network && address ? checkAddressShape(network, address) : null;
  const needsChecksumConfirm = shape === "NO_CHECKSUM" || serverWantsConfirm;

  function chooseNetwork(next: BlockchainNetwork) {
    if (next === network) return;
    setNetwork(next);
    // An address typed for one network is never carried over to another —
    // the same `0x` string would be accepted on Ethereum for a TRON choice
    // only by accident, and a TRON address is never valid on Ethereum.
    setTypedAddress("");
    setChecksumConfirmed(false);
    setServerWantsConfirm(false);
    setError(null);
    setErrorCode(null);
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (disabled) return;
    setErrorCode(null);
    if (!network) {
      setError(t("errors.NETWORK_REQUIRED"));
      return;
    }
    if (!address.trim()) {
      setError(t("errors.REFUND_ADDRESS_REQUIRED"));
      return;
    }
    if (!hasDefault && checkAddressShape(network, address.trim()) === "INVALID_FORMAT") {
      setError(t("errors.addressFormat", { network: networkLabel(network) }));
      return;
    }
    if (needsChecksumConfirm && !checksumConfirmed) {
      setError(t("errors.CHECKSUM_CONFIRMATION_REQUIRED"));
      return;
    }
    if (!tradeUrl.trim()) {
      setError(t("errors.INVALID_TRADE_URL"));
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await acceptTransaction(transactionId, {
        refundWalletAddress: address.trim(),
        steamTradeUrl: tradeUrl.trim(),
        network,
        ...(needsChecksumConfirm && checksumConfirmed ? { confirmLowercase: true } : {}),
      });
      onAccepted();
    } catch (err) {
      if (err instanceof ApiError) {
        const code = err.code;
        const reason = (err.details as Record<string, unknown> | null)?.reason;
        if (code === "INVALID_WALLET_ADDRESS" && reason === "CHECKSUM_CONFIRMATION_REQUIRED") {
          // The server wants the second confirmation the local check missed.
          setServerWantsConfirm(true);
          setChecksumConfirmed(false);
          setErrorCode(code);
          setError(t("errors.CHECKSUM_CONFIRMATION_REQUIRED"));
          return;
        }
        setErrorCode(code ?? null);
        setError(
          tDynamic(t, `errors.${code}`, t("errors.generic"), { network: networkLabel(network) }),
        );
      } else {
        setError(t("errors.generic"));
      }
    } finally {
      setSubmitting(false);
    }
  }

  const fieldsDisabled = submitting || disabled;

  return (
    <form
      onSubmit={handleSubmit}
      className="space-y-3 rounded-lg border border-blue-300 bg-blue-50 p-4"
    >
      <fieldset className="space-y-2" aria-describedby="accept-network-hint">
        <legend className="text-sm font-medium text-gray-900">{t("networkLabel")}</legend>
        <p id="accept-network-hint" className="text-xs text-gray-600">
          {t("networkHint")}
        </p>
        <div className="space-y-2">
          {options.map((option) => {
            const id = `accept-network-${option.network}`;
            const checked = option.network === network;
            const quote = option.quote;
            return (
              <label
                key={option.network}
                htmlFor={id}
                className={`flex cursor-pointer items-start gap-3 rounded-md border bg-white p-3 ${
                  checked ? "border-blue-600 ring-2 ring-blue-200" : "border-gray-300"
                }`}
              >
                <input
                  id={id}
                  type="radio"
                  name="accept-network"
                  value={option.network}
                  checked={checked}
                  disabled={fieldsDisabled}
                  onChange={() => chooseNetwork(option.network)}
                  className="mt-1"
                  data-testid={`accept-network-${option.network}`}
                />
                <span className="min-w-0 flex-1 text-sm">
                  <span className="block font-semibold text-gray-900">
                    {networkLabel(option.network)}
                  </span>
                  {quote ? (
                    <span
                      className="block text-gray-800"
                      data-testid={`accept-quote-${option.network}`}
                    >
                      {t("quote.total", {
                        amount: formatStablecoin(quote.totalAmount, stablecoin),
                      })}
                      {showsNetworkFeeLine(option.network) && (
                        <span className="block text-xs text-gray-600">
                          {t("quote.tronFee", {
                            amount: formatStablecoin(quote.networkFeeAmount, stablecoin),
                          })}
                        </span>
                      )}
                    </span>
                  ) : (
                    <span className="block text-xs text-gray-600">{t("quote.unavailable")}</span>
                  )}
                </span>
              </label>
            );
          })}
        </div>
      </fieldset>

      {network && (
        <div className="space-y-1">
          <label htmlFor="refund-address" className="text-sm font-medium text-gray-900">
            {t("addressLabel", { network: networkLabel(network) })}
          </label>
          <p id="refund-address-hint" className="text-xs text-gray-600">
            {t("addressHint", { network: networkLabel(network) })}
          </p>
          <input
            id="refund-address"
            data-testid="accept-refund-input"
            type="text"
            value={address}
            onChange={(e) => {
              setTypedAddress(e.target.value);
              setChecksumConfirmed(false);
              setServerWantsConfirm(false);
            }}
            disabled={hasDefault || fieldsDisabled}
            autoComplete="off"
            spellCheck={false}
            placeholder={networkMeta(network).addressPlaceholder}
            aria-describedby="refund-address-hint"
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
          {needsChecksumConfirm && (
            <label className="flex items-start gap-2 rounded-md border border-amber-300 bg-amber-50 p-2 text-xs text-amber-900">
              <input
                type="checkbox"
                checked={checksumConfirmed}
                onChange={(e) => setChecksumConfirmed(e.target.checked)}
                disabled={fieldsDisabled}
                className="mt-0.5"
                data-testid="accept-checksum-confirm"
              />
              <span>{t("noChecksumConfirm")}</span>
            </label>
          )}
        </div>
      )}

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
          disabled={fieldsDisabled}
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
        disabled={fieldsDisabled || network === null}
        className="w-full rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:opacity-50"
      >
        {submitting ? t("submitting") : t("submit")}
      </button>
    </form>
  );
}
