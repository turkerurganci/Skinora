"use client";

import { useState } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useTranslations } from "next-intl";
import { WalletAddressInput } from "@/components/common";
import { ApiError } from "@/lib/api/client";
import { initiateSteamReVerify } from "@/lib/api/auth";
import {
  updateRefundWallet,
  updateSellerWallet,
  type UpdateWalletResponse,
} from "@/lib/api/users";
import { maskWalletAddress } from "./helpers";

export type WalletRole = "seller" | "refund";

export interface WalletSectionProps {
  role: WalletRole;
  currentAddress: string | null;
  /** Active when the page returned from re-auth and the token matches this role. */
  activeReAuthToken: string | null;
  activeRoleFromCallback: WalletRole | null;
  onChangeCancelled: () => void;
  onSavedSuccessfully: () => void;
}

type Mode = "view" | "input";

/**
 * 04 §7.4 cüzdan bölümü. Mevcut adres maskeli gösterilir + toggle ile tam
 * görünür. "Adresi Değiştir" akışı (4 §7.4 step 1–7):
 *
 *   1. Frontend POST /auth/steam/re-verify → steamAuthUrl.
 *   2. window.location = steamAuthUrl → Steam authenticates.
 *   3. Backend callback redirects to `returnUrl` with `?reAuthToken=...`.
 *   4. Page captures token, opens C11 input panel for that role.
 *   5. User submits → PUT /users/me/wallet/{role} with X-ReAuth-Token.
 *   6. Success → cache invalidates + activeTransactionsUsingOldAddress
 *      uyarısı gösterilir.
 *
 * The sister section (other role) closes its input panel when this one
 * opens — only one role can hold the consumed token at a time (single
 * use, 5 min TTL). Token leakage via browser history is mitigated at the
 * page layer with router.replace.
 */
export function WalletSection({
  role,
  currentAddress,
  activeReAuthToken,
  activeRoleFromCallback,
  onChangeCancelled,
  onSavedSuccessfully,
}: WalletSectionProps) {
  const t = useTranslations("profile.wallet");
  const tErr = useTranslations("profile.wallet.errors");
  const queryClient = useQueryClient();
  const [mode, setMode] = useState<Mode>("view");
  const [showFull, setShowFull] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [savedResult, setSavedResult] = useState<UpdateWalletResponse | null>(null);

  // Re-auth callback landed and matches this role → drop straight into
  // the input panel without requiring another button click (would
  // otherwise consume the token via state mismatch).
  const reAuthActiveForThisRole =
    activeReAuthToken !== null && activeRoleFromCallback === role;
  const effectiveMode: Mode =
    mode === "input" || reAuthActiveForThisRole ? "input" : "view";

  async function handleChangeAddress() {
    if (!currentAddress) {
      // First-time wallet creation: no re-auth required by backend
      // (06 §3.1 / WalletAddressService — gating on `previous != null`).
      setMode("input");
      return;
    }
    try {
      setSubmitting(true);
      setServerError(null);
      const returnUrl = `/profile?walletChange=${role}`;
      const { steamAuthUrl } = await initiateSteamReVerify("wallet_change", returnUrl);
      window.location.href = steamAuthUrl;
    } catch (err) {
      setSubmitting(false);
      if (err instanceof ApiError) {
        setServerError(tErr(mapErrorCode(err.code) ?? "generic"));
      } else {
        setServerError(tErr("generic"));
      }
    }
  }

  async function handleConfirm(address: string) {
    setSubmitting(true);
    setServerError(null);
    try {
      const tokenForUpdate = currentAddress ? activeReAuthToken : null;
      const result =
        role === "seller"
          ? await updateSellerWallet(address, tokenForUpdate)
          : await updateRefundWallet(address, tokenForUpdate);
      setSavedResult(result);
      setMode("view");
      setShowFull(false);
      await queryClient.invalidateQueries({ queryKey: ["users", "me"] });
      onSavedSuccessfully();
    } catch (err) {
      if (err instanceof ApiError) {
        setServerError(tErr(mapErrorCode(err.code) ?? "generic"));
      } else {
        setServerError(tErr("generic"));
      }
    } finally {
      setSubmitting(false);
    }
  }

  function handleCancelChange() {
    setMode("view");
    setServerError(null);
    onChangeCancelled();
  }

  const titleKey = role === "seller" ? "sellerTitle" : "refundTitle";
  const noteKey = role === "seller" ? "sellerNote" : "refundNote";
  const changeButtonKey = currentAddress ? "changeButton" : "addButton";

  return (
    <section className="rounded-lg border border-gray-200 bg-white p-6">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="text-lg font-semibold text-gray-900">{t(titleKey)}</h2>
          <p className="mt-1 text-sm text-gray-600">{t(noteKey)}</p>
        </div>
      </div>

      {effectiveMode === "view" && (
        <div className="mt-4 flex flex-col gap-3">
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
              className="inline-flex items-center justify-center rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {submitting ? t("redirecting") : t(changeButtonKey)}
            </button>
          </div>

          {savedResult && savedResult.activeTransactionsUsingOldAddress > 0 && (
            <p className="rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-900">
              {t("activeTransactionsNotice", {
                count: savedResult.activeTransactionsUsingOldAddress,
              })}
            </p>
          )}

          {serverError && (
            <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
              {serverError}
            </p>
          )}
        </div>
      )}

      {effectiveMode === "input" && (
        <div className="mt-4 flex flex-col gap-3">
          <WalletAddressInput
            initialValue=""
            onConfirm={handleConfirm}
          />
          {serverError && (
            <p className="rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
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
    </section>
  );
}

function mapErrorCode(code: string): string | null {
  switch (code) {
    case "INVALID_WALLET_ADDRESS":
      return "invalidAddress";
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
