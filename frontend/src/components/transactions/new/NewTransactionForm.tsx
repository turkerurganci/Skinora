"use client";

import { useMemo, useState } from "react";
import { useTranslations, useLocale } from "next-intl";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMutation } from "@tanstack/react-query";
import {
  BuyerIdentificationMethod,
  StablecoinType,
} from "@/types/enums";
import { ApiError } from "@/lib/api/client";
import {
  createTransaction,
  type EligibilityResponse,
  type TransactionParamsResponse,
} from "@/lib/api/transactions";
import { Skeleton } from "@/components/common";
import { useSteamInventory } from "@/lib/hooks/useSteamInventory";
import type { SteamInventoryItem } from "@/lib/api/steam";
import { EligibilityGate, getBlockingReasons } from "./EligibilityGate";
import { StepIndicator } from "./StepIndicator";
import { Step1ItemSelection } from "./Step1ItemSelection";
import { Step2Details } from "./Step2Details";
import { Step3BuyerWallet } from "./Step3BuyerWallet";
import { Step4Summary } from "./Step4Summary";

type StepNumber = 1 | 2 | 3 | 4;

const STEAM_ID_REGEX = /^\d{17}$/;

export interface NewTransactionFormProps {
  eligibility: EligibilityResponse;
  params: TransactionParamsResponse;
}

export function NewTransactionForm({ eligibility, params }: NewTransactionFormProps) {
  const t = useTranslations("newTransaction");
  const locale = useLocale();
  const router = useRouter();

  const gateReasons = useMemo(() => getBlockingReasons(eligibility), [eligibility]);
  const isGated = gateReasons.length > 0;

  const [step, setStep] = useState<StepNumber>(1);
  const [item, setItem] = useState<SteamInventoryItem | null>(null);
  const [stablecoin, setStablecoin] = useState<StablecoinType>(
    params.supportedStablecoins[0] ?? StablecoinType.USDT,
  );
  const [price, setPrice] = useState("");
  const [paymentTimeoutHours, setPaymentTimeoutHours] = useState(
    params.paymentTimeout.defaultHours,
  );
  const [method, setMethod] = useState<BuyerIdentificationMethod>(
    BuyerIdentificationMethod.STEAM_ID,
  );
  const [buyerSteamId, setBuyerSteamId] = useState("");
  const [sellerWalletAddress, setSellerWalletAddress] = useState("");
  const [walletConfirmed, setWalletConfirmed] = useState(false);

  const inventory = useSteamInventory(!isGated);

  const inventoryErrorCode =
    inventory.error instanceof ApiError ? inventory.error.code : null;

  const priceError = useMemo(() => {
    if (price === "") return null;
    const n = Number(price);
    if (!Number.isFinite(n)) return t("step2.price.errors.notNumber");
    const min = Number(params.minPrice);
    const max = Number(params.maxPrice);
    if (n < min) return t("step2.price.errors.belowMin", { min: params.minPrice });
    if (n > max) return t("step2.price.errors.aboveMax", { max: params.maxPrice });
    return null;
  }, [price, params.minPrice, params.maxPrice, t]);

  const steamIdError = useMemo(() => {
    if (method !== BuyerIdentificationMethod.STEAM_ID) return null;
    if (buyerSteamId === "") return null;
    if (!STEAM_ID_REGEX.test(buyerSteamId)) return t("step3.buyer.steamId.errors.format");
    return null;
  }, [method, buyerSteamId, t]);

  const isStep1Valid = item !== null && item.tradeable;
  const isStep2Valid =
    isStep1Valid &&
    price !== "" &&
    priceError === null &&
    paymentTimeoutHours >= params.paymentTimeout.minHours &&
    paymentTimeoutHours <= params.paymentTimeout.maxHours;
  const isStep3Valid =
    isStep2Valid &&
    walletConfirmed &&
    (method === BuyerIdentificationMethod.OPEN_LINK
      ? params.openLinkEnabled
      : STEAM_ID_REGEX.test(buyerSteamId));

  const mutation = useMutation({
    mutationFn: () => {
      if (!item) throw new Error("item required");
      return createTransaction({
        itemAssetId: item.assetId,
        stablecoin,
        price,
        paymentTimeoutHours,
        buyerIdentificationMethod: method,
        buyerSteamId:
          method === BuyerIdentificationMethod.STEAM_ID ? buyerSteamId : undefined,
        sellerWalletAddress,
      });
    },
    onSuccess: (data) => {
      router.push(`/${locale}/transactions/${data.id}`);
    },
  });

  if (isGated) {
    return (
      <div className="space-y-4">
        <EligibilityGate eligibility={eligibility} />
        <Link
          href={`/${locale}/dashboard`}
          className="inline-block text-sm text-blue-600 hover:underline"
        >
          {t("gate.backToDashboard")}
        </Link>
      </div>
    );
  }

  const submitErrorMessage = (() => {
    if (!mutation.error) return null;
    if (!(mutation.error instanceof ApiError)) return t("step4.errors.generic");
    return resolveErrorMessage(mutation.error.code, (k) => t(k as never));
  })();

  return (
    <div className="space-y-6">
      <StepIndicator current={step} />

      {step === 1 && (
        <>
          <Step1ItemSelection
            inventory={inventory.data?.items}
            totalCount={inventory.data?.totalCount}
            tradeableCount={inventory.data?.tradeableCount}
            isLoading={inventory.isLoading}
            isError={inventory.isError}
            errorCode={inventoryErrorCode}
            selectedAssetId={item?.assetId ?? null}
            onSelect={setItem}
            onRetry={() => inventory.refetch()}
          />
          <NavButtons
            backDisabled
            nextDisabled={!isStep1Valid}
            onNext={() => setStep(2)}
            backHref={`/${locale}/dashboard`}
          />
        </>
      )}

      {step === 2 && item && (
        <>
          <Step2Details
            item={item}
            params={params}
            stablecoin={stablecoin}
            price={price}
            paymentTimeoutHours={paymentTimeoutHours}
            priceError={priceError}
            onChangeItem={() => setStep(1)}
            onChangeStablecoin={setStablecoin}
            onChangePrice={setPrice}
            onChangeTimeout={setPaymentTimeoutHours}
          />
          <NavButtons
            onBack={() => setStep(1)}
            onNext={() => setStep(3)}
            nextDisabled={!isStep2Valid}
          />
        </>
      )}

      {step === 3 && item && (
        <>
          <Step3BuyerWallet
            method={method}
            buyerSteamId={buyerSteamId}
            sellerWalletAddress={sellerWalletAddress}
            walletConfirmed={walletConfirmed}
            steamIdError={steamIdError}
            openLinkEnabled={params.openLinkEnabled}
            onChangeMethod={setMethod}
            onChangeBuyerSteamId={setBuyerSteamId}
            onConfirmWallet={(address) => {
              setSellerWalletAddress(address);
              setWalletConfirmed(true);
            }}
            onResetWallet={() => {
              setWalletConfirmed(false);
            }}
          />
          <NavButtons
            onBack={() => setStep(2)}
            onNext={() => setStep(4)}
            nextDisabled={!isStep3Valid}
          />
        </>
      )}

      {step === 4 && item && (
        <Step4Summary
          item={item}
          stablecoin={stablecoin}
          price={price}
          commissionRate={params.commissionRate}
          paymentTimeoutHours={paymentTimeoutHours}
          buyerMethod={method}
          buyerSteamId={buyerSteamId}
          sellerWalletAddress={sellerWalletAddress}
          submitError={submitErrorMessage}
          isSubmitting={mutation.isPending}
          onBack={() => setStep(3)}
          onSubmit={() => mutation.mutate()}
        />
      )}

      {/* Fallback skeleton when item was unset between renders. */}
      {step > 1 && !item && (
        <>
          <Skeleton className="h-48 w-full" />
          <NavButtons onBack={() => setStep(1)} nextDisabled />
        </>
      )}
    </div>
  );
}

interface NavButtonsProps {
  onBack?: () => void;
  onNext?: () => void;
  backDisabled?: boolean;
  nextDisabled?: boolean;
  backHref?: string;
}

function NavButtons({
  onBack,
  onNext,
  backDisabled,
  nextDisabled,
  backHref,
}: NavButtonsProps) {
  const t = useTranslations("newTransaction.nav");
  return (
    <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-between">
      {backHref ? (
        <Link
          href={backHref}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-center text-sm font-medium text-gray-700 hover:bg-gray-50"
        >
          {t("cancel")}
        </Link>
      ) : (
        <button
          type="button"
          onClick={onBack}
          disabled={backDisabled}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
        >
          {t("back")}
        </button>
      )}
      {onNext && (
        <button
          type="button"
          onClick={onNext}
          disabled={nextDisabled}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {t("next")}
        </button>
      )}
    </div>
  );
}

// Backend `POST /transactions` error codes that the step 4 error panel
// recognises (07 §7.2 + TransactionErrorCodes). Codes outside this set fall
// through to the generic message.
const POST_ERROR_CODES = new Set([
  "VALIDATION_ERROR",
  "INVALID_WALLET_ADDRESS",
  "SANCTIONS_MATCH",
  "CONCURRENT_LIMIT_REACHED",
  "CANCEL_COOLDOWN_ACTIVE",
  "NEW_ACCOUNT_LIMIT_REACHED",
  "MOBILE_AUTHENTICATOR_REQUIRED",
  "ACCOUNT_FLAGGED",
  "ITEM_NOT_TRADEABLE",
  "ITEM_NOT_IN_INVENTORY",
  "PRICE_OUT_OF_RANGE",
  "TIMEOUT_OUT_OF_RANGE",
  "OPEN_LINK_DISABLED",
  "BUYER_STEAM_ID_NOT_FOUND",
  "PAYOUT_ADDRESS_COOLDOWN_ACTIVE",
  "SELLER_WALLET_ADDRESS_MISSING",
]);

function resolveErrorMessage(code: string, t: (key: string) => string): string {
  if (POST_ERROR_CODES.has(code)) return t(`step4.errors.${code}`);
  return t("step4.errors.generic");
}
