"use client";

import Link from "next/link";
import { useLocale, useTranslations } from "next-intl";
import { ItemCard, type ItemCardItem } from "@/components/common";
import { BlockchainNetwork, StablecoinType } from "@/types/enums";
import type { SellerNetworkOption } from "@/lib/transactions/networkChoice";
import { networkLabel } from "@/lib/utils/blockchain";
import { maskAddress } from "@/components/transactions/detail/helpers";
import type { TransactionParamsResponse } from "@/lib/api/transactions";
import type { SteamInventoryItem } from "@/lib/api/steam";
import { cn } from "@/lib/utils/cn";
import { formatStablecoin } from "@/lib/utils/format";
import { calculateCommission } from "@/lib/utils/money";

export interface Step2DetailsProps {
  item: SteamInventoryItem;
  params: TransactionParamsResponse;
  stablecoin: StablecoinType;
  price: string;
  paymentTimeoutHours: number;
  priceError: string | null;
  /**
   * T156 — the networks on offer (`supportedNetworks` only — a closed network
   * is not in the list) and whether each can be ticked (payout address saved).
   */
  networkOptions: readonly SellerNetworkOption[];
  acceptedNetworks: readonly BlockchainNetwork[];
  onToggleNetwork: (network: BlockchainNetwork) => void;
  onChangeItem: () => void;
  onChangeStablecoin: (value: StablecoinType) => void;
  onChangePrice: (value: string) => void;
  onChangeTimeout: (value: number) => void;
}

function toCardItem(item: SteamInventoryItem): ItemCardItem {
  return {
    steamItemId: item.assetId,
    name: item.name,
    type: item.type,
    wear: item.wear,
    imageUrl: item.imageUrl,
    tradeable: item.tradeable,
  };
}

// T144 — same rule as the backend (truncated to the cent), so the preview
// is the commission the transaction will actually carry.
function commissionPreview(price: string, rate: number, stablecoin: StablecoinType): string | null {
  const commission = calculateCommission(price, rate);
  return commission === null ? null : formatStablecoin(commission, stablecoin);
}

export function Step2Details({
  item,
  params,
  stablecoin,
  price,
  paymentTimeoutHours,
  priceError,
  networkOptions,
  acceptedNetworks,
  onToggleNetwork,
  onChangeItem,
  onChangeStablecoin,
  onChangePrice,
  onChangeTimeout,
}: Step2DetailsProps) {
  const t = useTranslations("newTransaction.step2");
  const locale = useLocale();
  const commission = commissionPreview(price, params.commissionRate, stablecoin);
  const commissionPercent = (params.commissionRate * 100).toFixed(0);

  const timeoutOptions: number[] = [];
  for (let h = params.paymentTimeout.minHours; h <= params.paymentTimeout.maxHours; h += 1) {
    timeoutOptions.push(h);
  }

  return (
    <div className="space-y-5">
      <h2 className="text-lg font-semibold text-gray-900">{t("title")}</h2>

      <div className="space-y-2">
        <p className="text-sm font-medium text-gray-700">{t("selectedItem")}</p>
        <div className="flex items-center gap-3">
          <div className="min-w-0 flex-1">
            <ItemCard variant="compact" item={toCardItem(item)} />
          </div>
          <button
            type="button"
            onClick={onChangeItem}
            className="rounded-md border border-gray-300 bg-white px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
          >
            {t("changeItem")}
          </button>
        </div>
      </div>

      <fieldset className="space-y-2">
        <legend className="text-sm font-medium text-gray-700">{t("stablecoin.label")}</legend>
        <div className="flex gap-2" role="radiogroup">
          {params.supportedStablecoins.map((coin) => {
            const isActive = coin === stablecoin;
            return (
              <button
                key={coin}
                type="button"
                role="radio"
                aria-checked={isActive}
                onClick={() => onChangeStablecoin(coin)}
                className={cn(
                  "flex-1 rounded-md border-2 px-4 py-2 text-sm font-semibold transition-colors",
                  isActive
                    ? "border-blue-600 bg-blue-50 text-blue-700"
                    : "border-gray-300 bg-white text-gray-700 hover:border-gray-400",
                )}
              >
                {coin}
              </button>
            );
          })}
        </div>
      </fieldset>

      <fieldset className="space-y-2" aria-describedby="networks-hint">
        <legend className="text-sm font-medium text-gray-700">{t("networks.label")}</legend>
        <p id="networks-hint" className="text-xs text-gray-500">
          {t("networks.hint")}
        </p>
        <ul className="space-y-2">
          {networkOptions.map((option) => {
            const id = `network-${option.network}`;
            const checked = option.selectable && acceptedNetworks.includes(option.network);
            return (
              <li
                key={option.network}
                className={cn(
                  "rounded-md border p-3",
                  checked ? "border-blue-600 bg-blue-50" : "border-gray-300 bg-white",
                  !option.selectable && "bg-gray-50",
                )}
              >
                <div className="flex items-start gap-3">
                  <input
                    id={id}
                    type="checkbox"
                    checked={checked}
                    disabled={!option.selectable}
                    onChange={() => onToggleNetwork(option.network)}
                    aria-describedby={`${id}-note`}
                    className="mt-1"
                    data-testid={`network-option-${option.network}`}
                  />
                  <div className="min-w-0 flex-1">
                    <label htmlFor={id} className="text-sm font-semibold text-gray-900">
                      {networkLabel(option.network)}
                    </label>
                    <div id={`${id}-note`} className="space-y-0.5 text-xs text-gray-600">
                      {option.payoutAddress ? (
                        <p>
                          {t("networks.payoutAddress")}:{" "}
                          <span className="font-mono" title={option.payoutAddress}>
                            {maskAddress(option.payoutAddress)}
                          </span>
                        </p>
                      ) : (
                        <p className="text-red-700">
                          {t("networks.noPayoutAddress")}{" "}
                          <Link href={`/${locale}/profile`} className="underline">
                            {t("networks.addInProfile")}
                          </Link>
                        </p>
                      )}
                      <p>
                        {option.network === BlockchainNetwork.TRON
                          ? t("networks.tronFeeNote")
                          : t("networks.ethereumFeeNote")}
                      </p>
                    </div>
                  </div>
                </div>
              </li>
            );
          })}
        </ul>
        {acceptedNetworks.length === 0 && (
          <p role="alert" className="text-xs text-red-600">
            {t("networks.noneSelected")}
          </p>
        )}
      </fieldset>

      <div className="space-y-1">
        <label htmlFor="price" className="text-sm font-medium text-gray-700">
          {t("price.label")}
        </label>
        <div className="flex items-stretch gap-0 overflow-hidden rounded-md border border-gray-300 focus-within:border-blue-500 focus-within:ring-2 focus-within:ring-blue-200">
          <input
            id="price"
            type="number"
            inputMode="decimal"
            min={params.minPrice}
            max={params.maxPrice}
            step="0.01"
            value={price}
            onChange={(e) => onChangePrice(e.target.value)}
            className="min-w-0 flex-1 px-3 py-2 text-sm focus:outline-none"
            aria-describedby="price-hint"
            aria-invalid={priceError ? "true" : undefined}
          />
          <span className="flex items-center bg-gray-50 px-3 text-sm font-semibold text-gray-700">
            {stablecoin}
          </span>
        </div>
        <p id="price-hint" className="text-xs text-gray-500">
          {t("price.range", { min: params.minPrice, max: params.maxPrice })}
        </p>
        {priceError && <p className="text-xs text-red-600">{priceError}</p>}
      </div>

      <div className="space-y-1">
        <label htmlFor="paymentTimeout" className="text-sm font-medium text-gray-700">
          {t("timeout.label")}
        </label>
        <select
          id="paymentTimeout"
          value={paymentTimeoutHours}
          onChange={(e) => onChangeTimeout(Number(e.target.value))}
          className="w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200"
        >
          {timeoutOptions.map((h) => (
            <option key={h} value={h}>
              {t("timeout.hours", { hours: h })}
            </option>
          ))}
        </select>
        <p className="text-xs text-gray-500">{t("timeout.hint")}</p>
      </div>

      <div className="rounded-md border border-gray-200 bg-gray-50 p-3">
        <p className="text-xs font-medium text-gray-700">{t("commission.label")}</p>
        <p className="mt-1 text-sm text-gray-900">
          {commission
            ? t("commission.value", { percent: commissionPercent, amount: commission })
            : t("commission.placeholder", { percent: commissionPercent })}
        </p>
      </div>
    </div>
  );
}
