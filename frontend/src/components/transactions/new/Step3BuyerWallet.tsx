"use client";

import { useTranslations } from "next-intl";
import { WalletAddressInput } from "@/components/common";
import { BuyerIdentificationMethod } from "@/types/enums";
import { cn } from "@/lib/utils/cn";

export interface Step3BuyerWalletProps {
  method: BuyerIdentificationMethod;
  buyerSteamId: string;
  sellerWalletAddress: string;
  walletConfirmed: boolean;
  steamIdError: string | null;
  openLinkEnabled: boolean;
  onChangeMethod: (value: BuyerIdentificationMethod) => void;
  onChangeBuyerSteamId: (value: string) => void;
  onConfirmWallet: (address: string) => void;
  onResetWallet: () => void;
}

export function Step3BuyerWallet({
  method,
  buyerSteamId,
  sellerWalletAddress,
  walletConfirmed,
  steamIdError,
  openLinkEnabled,
  onChangeMethod,
  onChangeBuyerSteamId,
  onConfirmWallet,
  onResetWallet,
}: Step3BuyerWalletProps) {
  const t = useTranslations("newTransaction.step3");

  return (
    <div className="space-y-5">
      <h2 className="text-lg font-semibold text-gray-900">{t("title")}</h2>

      <fieldset className="space-y-3">
        <legend className="text-sm font-medium text-gray-700">{t("buyer.label")}</legend>

        <label className="flex items-start gap-3 rounded-md border border-gray-200 bg-white p-3 hover:border-gray-300">
          <input
            type="radio"
            name="buyerMethod"
            checked={method === BuyerIdentificationMethod.STEAM_ID}
            onChange={() => onChangeMethod(BuyerIdentificationMethod.STEAM_ID)}
            className="mt-1"
          />
          <div className="flex-1">
            <p className="text-sm font-medium text-gray-900">{t("buyer.steamId.title")}</p>
            <p className="text-xs text-gray-600">{t("buyer.steamId.description")}</p>
          </div>
        </label>

        <label
          className={cn(
            "flex items-start gap-3 rounded-md border bg-white p-3",
            openLinkEnabled
              ? "border-gray-200 hover:border-gray-300"
              : "cursor-not-allowed border-gray-200 opacity-60",
          )}
        >
          <input
            type="radio"
            name="buyerMethod"
            checked={method === BuyerIdentificationMethod.OPEN_LINK}
            disabled={!openLinkEnabled}
            onChange={() => onChangeMethod(BuyerIdentificationMethod.OPEN_LINK)}
            className="mt-1"
          />
          <div className="flex-1">
            <p className="text-sm font-medium text-gray-900">{t("buyer.openLink.title")}</p>
            <p className="text-xs text-gray-600">
              {openLinkEnabled
                ? t("buyer.openLink.description")
                : t("buyer.openLink.disabled")}
            </p>
          </div>
        </label>
      </fieldset>

      {method === BuyerIdentificationMethod.STEAM_ID && (
        <div className="space-y-1">
          <label htmlFor="buyerSteamId" className="text-sm font-medium text-gray-700">
            {t("buyer.steamId.inputLabel")}
          </label>
          <input
            id="buyerSteamId"
            type="text"
            inputMode="numeric"
            value={buyerSteamId}
            onChange={(e) => onChangeBuyerSteamId(e.target.value.trim())}
            placeholder="76561198XXXXXXXXX"
            autoComplete="off"
            spellCheck={false}
            className={cn(
              "w-full rounded-md border bg-white px-3 py-2 font-mono text-sm focus:outline-none focus:ring-2",
              steamIdError
                ? "border-red-300 focus:ring-red-200"
                : "border-gray-300 focus:ring-blue-200",
            )}
            aria-invalid={steamIdError ? "true" : undefined}
            aria-describedby="buyerSteamId-hint"
          />
          <p id="buyerSteamId-hint" className="text-xs text-gray-500">
            {t("buyer.steamId.hint")}
          </p>
          {steamIdError && <p className="text-xs text-red-600">{steamIdError}</p>}
        </div>
      )}

      <div className="space-y-2">
        <p className="text-sm font-medium text-gray-700">{t("wallet.label")}</p>
        <p className="text-xs text-gray-500">{t("wallet.description")}</p>
        {walletConfirmed ? (
          <div className="flex flex-col gap-3 rounded-lg border border-green-200 bg-green-50 p-4">
            <p className="text-sm font-medium text-green-900">
              {t("wallet.confirmed")}
            </p>
            <code className="break-all rounded-md bg-white px-3 py-2 text-sm">
              {sellerWalletAddress}
            </code>
            <button
              type="button"
              onClick={onResetWallet}
              className="self-start rounded-md border border-green-300 bg-white px-3 py-2 text-sm text-green-700 hover:bg-green-100"
            >
              {t("wallet.change")}
            </button>
          </div>
        ) : (
          <WalletAddressInput
            initialValue={sellerWalletAddress}
            onConfirm={onConfirmWallet}
          />
        )}
      </div>
    </div>
  );
}
