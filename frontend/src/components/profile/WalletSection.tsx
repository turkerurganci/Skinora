"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { WalletAddressInput, type WalletAddressConfirmation } from "@/components/common";
import { ApiError } from "@/lib/api/client";
import { initiateSteamReVerify } from "@/lib/api/auth";
import {
  findWallet,
  updateRefundWallet,
  updateSellerWallet,
  walletChangeNeedsReAuth,
  type UpdateWalletResponse,
  type UserWalletEntry,
  type WalletType,
} from "@/lib/api/users";
import { ALL_NETWORKS, networkLabel } from "@/lib/utils/blockchain";
import { BlockchainNetwork } from "@/types/enums";
import { maskWalletAddress } from "./helpers";
import { tDynamic } from "@/lib/i18n/dynamicKey";
import { PayoutSourceNotice } from "@/components/masak";

export type WalletRole = "seller" | "refund";

const TYPE_BY_ROLE: Record<WalletRole, WalletType> = { seller: "PAYOUT", refund: "REFUND" };

export interface WalletSectionProps {
  role: WalletRole;
  /** Every saved address (07 §5.1 `wallets[]`); the section reads its own type. */
  wallets: readonly UserWalletEntry[];
  /** Active when the page returned from re-auth and the token matches this role. */
  activeReAuthToken: string | null;
  activeRoleFromCallback: WalletRole | null;
  /** The network the re-auth was started for (`walletNetwork` callback param). */
  activeNetworkFromCallback: BlockchainNetwork | null;
  onChangeCancelled: () => void;
  onSavedSuccessfully: () => void;
}

/**
 * 04 §7.4 cüzdan bölümü — T156: ağ başına (02 §12.1–§12.2, 03 §9.1).
 *
 * One row per network (TRON, Ethereum), each with its own address, masked by
 * default with a "show full" toggle, and its own add / change button. The
 * address is the server's display form (EIP-55 on Ethereum — 06 §1.4).
 *
 * Re-auth (07 §5.3 "Ek Auth", T146 decision xv): required as soon as the user
 * has ANY address of this type — changing an address, and adding a first one
 * on a new network alike ({@link walletChangeNeedsReAuth}). Only the very first
 * address of a type is saved without it. The flow:
 *
 *   1. POST /auth/steam/re-verify, returnUrl
 *      `/profile?walletChange={role}&walletNetwork={network}`.
 *   2. Steam authenticates → backend callback appends `reAuthToken`.
 *   3. The page captures role + network + token; that row opens its C11 input.
 *   4. PUT /users/me/wallet/{role} `{ walletAddress, network, confirmLowercase? }`
 *      with X-ReAuth-Token.
 *
 * Only one row can hold the consumed token at a time (single use, 5 min TTL).
 */
export function WalletSection({
  role,
  wallets,
  activeReAuthToken,
  activeRoleFromCallback,
  activeNetworkFromCallback,
  onChangeCancelled,
  onSavedSuccessfully,
}: WalletSectionProps) {
  const t = useTranslations("profile.wallet");
  const type = TYPE_BY_ROLE[role];
  const needsReAuth = walletChangeNeedsReAuth(wallets, type);
  const titleKey = role === "seller" ? "sellerTitle" : "refundTitle";
  const noteKey = role === "seller" ? "sellerNote" : "refundNote";

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <h2 className="text-lg font-semibold text-gray-900">{t(titleKey)}</h2>
      <p className="mt-1 text-sm text-gray-600">{t(noteKey)}</p>
      <p className="mt-1 text-xs text-gray-500">{t("perNetworkNote")}</p>
      {/* T167 — where the seller's payouts come from, to declare to an exchange
          before the first one lands (Tedbirler Yönetmeliği m. 24/A-6). */}
      {role === "seller" && (
        <div className="mt-3">
          <PayoutSourceNotice />
        </div>
      )}

      <ul className="mt-4 divide-y divide-gray-100">
        {ALL_NETWORKS.map((network) => (
          <WalletNetworkRow
            key={network}
            role={role}
            network={network}
            currentAddress={findWallet(wallets, network, type)}
            needsReAuth={needsReAuth}
            callbackToken={
              activeReAuthToken !== null &&
              activeRoleFromCallback === role &&
              (activeNetworkFromCallback ?? BlockchainNetwork.TRON) === network
                ? activeReAuthToken
                : null
            }
            onChangeCancelled={onChangeCancelled}
            onSavedSuccessfully={onSavedSuccessfully}
          />
        ))}
      </ul>
    </section>
  );
}

interface WalletNetworkRowProps {
  role: WalletRole;
  network: BlockchainNetwork;
  currentAddress: string | null;
  needsReAuth: boolean;
  callbackToken: string | null;
  onChangeCancelled: () => void;
  onSavedSuccessfully: () => void;
}

type Mode = "view" | "input";

function WalletNetworkRow({
  role,
  network,
  currentAddress,
  needsReAuth,
  callbackToken,
  onChangeCancelled,
  onSavedSuccessfully,
}: WalletNetworkRowProps) {
  const t = useTranslations("profile.wallet");
  const tErr = useTranslations("profile.wallet.errors");
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<Mode>("view");
  const [showFull, setShowFull] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [savedResult, setSavedResult] = useState<UpdateWalletResponse | null>(null);
  const label = networkLabel(network);

  // Re-auth callback landed for THIS role and network → straight into the
  // input panel (another click would waste the single-use token).
  const effectiveMode: Mode = mode === "input" || callbackToken !== null ? "input" : "view";

  function errorMessage(err: unknown): string {
    if (!(err instanceof ApiError)) return tErr("generic");
    const key = mapErrorCode(err);
    return key ? tDynamic(tErr, key, tErr("generic")) : tErr("generic");
  }

  async function handleChangeAddress() {
    if (!needsReAuth) {
      // The user's first address of this type on any network — no re-auth
      // (07 §5.3: "o türde hiç adresi olmayan kullanıcının ilk kaydı onaysızdır").
      setMode("input");
      return;
    }
    try {
      setSubmitting(true);
      setServerError(null);
      const returnUrl = `/profile?walletChange=${role}&walletNetwork=${network}`;
      const { steamAuthUrl } = await initiateSteamReVerify("wallet_change", returnUrl);
      window.location.href = steamAuthUrl;
    } catch (err) {
      setSubmitting(false);
      setServerError(errorMessage(err));
    }
  }

  async function handleConfirm(address: string, confirmation: WalletAddressConfirmation) {
    setSubmitting(true);
    setServerError(null);
    try {
      const body = {
        walletAddress: address,
        network,
        ...(confirmation.confirmLowercase ? { confirmLowercase: true } : {}),
      };
      const token = needsReAuth ? callbackToken : null;
      const result =
        role === "seller"
          ? await updateSellerWallet(body, token)
          : await updateRefundWallet(body, token);
      setSavedResult(result);
      setMode("view");
      setShowFull(false);
      await queryClient.invalidateQueries({ queryKey: ["users", "me"] });
      onSavedSuccessfully();
    } catch (err) {
      setServerError(errorMessage(err));
    } finally {
      setSubmitting(false);
    }
  }

  function handleCancelChange() {
    setMode("view");
    setServerError(null);
    onChangeCancelled();
  }

  return (
    <li className="py-3" data-testid={`wallet-row-${role}-${network}`}>
      <p className="text-sm font-semibold text-gray-900">
        <span className="rounded bg-gray-100 px-2 py-0.5 text-xs font-semibold text-gray-800">
          {label}
        </span>
      </p>

      {effectiveMode === "view" && (
        <div className="mt-2 flex flex-col gap-3">
          {currentAddress ? (
            <div className="flex flex-wrap items-center gap-2">
              <code className="break-all rounded-md bg-gray-100 px-3 py-2 font-mono text-sm text-gray-900">
                {showFull ? currentAddress : maskWalletAddress(currentAddress)}
              </code>
              <button
                type="button"
                onClick={() => setShowFull((v) => !v)}
                className="rounded-md border border-gray-300 bg-white px-2 py-1 text-xs text-gray-700 hover:bg-gray-50"
              >
                {showFull ? t("hideFull") : t("showFull")}
              </button>
            </div>
          ) : (
            <p className="text-sm italic text-gray-500">{t("notSet")}</p>
          )}

          <div>
            <button
              type="button"
              disabled={submitting}
              onClick={handleChangeAddress}
              data-testid={`wallet-change-${role}-${network}`}
              aria-label={`${t(currentAddress ? "changeButton" : "addButton")} — ${label}`}
              className="inline-flex items-center justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {submitting ? t("redirecting") : t(currentAddress ? "changeButton" : "addButton")}
            </button>
            {needsReAuth && !submitting && (
              <p className="mt-1 text-xs text-gray-500">{t("reAuthNote")}</p>
            )}
          </div>

          {savedResult && savedResult.activeTransactionsUsingOldAddress > 0 && (
            <p className="rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-900">
              {t("activeTransactionsNotice", {
                count: savedResult.activeTransactionsUsingOldAddress,
              })}
            </p>
          )}

          {serverError && (
            <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
              {serverError}
            </p>
          )}
        </div>
      )}

      {effectiveMode === "input" && (
        <div className="mt-2 flex flex-col gap-3">
          <WalletAddressInput network={network} initialValue="" onConfirm={handleConfirm} />
          {serverError && (
            <p role="alert" className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
              {serverError}
            </p>
          )}
          <button
            type="button"
            onClick={handleCancelChange}
            className="self-start text-sm text-gray-600 hover:text-gray-900"
            disabled={submitting}
          >
            {t("cancel")}
          </button>
        </div>
      )}
    </li>
  );
}

function detailString(err: ApiError, key: string): string | null {
  const details = err.error.details as unknown as Record<string, unknown> | null;
  const value = details?.[key];
  return typeof value === "string" ? value : null;
}

function mapErrorCode(err: ApiError): string | null {
  switch (err.code) {
    case "INVALID_WALLET_ADDRESS":
      // T146 — `details.reason` says whether the shape or the checksum failed.
      switch (detailString(err, "reason")) {
        case "CHECKSUM_MISMATCH":
          return "checksumMismatch";
        case "CHECKSUM_CONFIRMATION_REQUIRED":
          return "checksumConfirmationRequired";
        default:
          return "invalidAddress";
      }
    case "SANCTIONS_MATCH":
      return "sanctioned";
    case "RE_AUTH_REQUIRED":
      return "reAuthRequired";
    case "RE_AUTH_TOKEN_INVALID":
      return "reAuthTokenInvalid";
    case "VALIDATION_ERROR":
      return "validation";
    default:
      return null;
  }
}
