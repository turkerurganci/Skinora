"use client";

import { useTranslations } from "next-intl";
import { Spinner } from "@/components/common/LoadingState";
import { BlockchainNetwork, BuyerIdentificationMethod, StablecoinType } from "@/types/enums";
import { networkLabel } from "@/lib/utils/blockchain";
import type { SellerNetworkOption } from "@/lib/transactions/networkChoice";
import { formatStablecoin } from "@/lib/utils/format";
import { calculateCommission } from "@/lib/utils/money";
import type { SteamInventoryItem } from "@/lib/api/steam";

export interface Step4SummaryProps {
  item: SteamInventoryItem;
  stablecoin: StablecoinType;
  price: string;
  commissionRate: number;
  paymentTimeoutHours: number;
  buyerMethod: BuyerIdentificationMethod;
  buyerSteamId: string;
  /** T156 — the networks the listing is created with (step 2). */
  acceptedNetworks: readonly BlockchainNetwork[];
  /** Carries each network's payout address (06 §3.5b snapshot source). */
  networkOptions: readonly SellerNetworkOption[];
  /** Error panel; `network` is the label of `error.details.network` when present. */
  submitError: { message: string; network?: string } | null;
  isSubmitting: boolean;
  onBack: () => void;
  onSubmit: () => void;
}

function maskAddress(address: string): string {
  if (address.length <= 12) return address;
  return `${address.slice(0, 6)}…${address.slice(-4)}`;
}

export function Step4Summary({
  item,
  stablecoin,
  price,
  commissionRate,
  paymentTimeoutHours,
  buyerMethod,
  buyerSteamId,
  acceptedNetworks,
  networkOptions,
  submitError,
  isSubmitting,
  onBack,
  onSubmit,
}: Step4SummaryProps) {
  const t = useTranslations("newTransaction.step4");
  // T144 — same rule as the backend (truncated to the cent), so the preview
  // is the commission the transaction will actually carry.
  const commission = calculateCommission(price, commissionRate);
  const commissionDisplay =
    commission !== null ? formatStablecoin(commission, stablecoin) : `— ${stablecoin}`;

  return (
    <div className="space-y-5">
      <h2 className="text-lg font-semibold text-gray-900">{t("title")}</h2>

      <dl className="divide-y divide-gray-200 rounded-lg border border-gray-200 bg-white">
        <Row label={t("rows.item")}>
          <div className="flex items-center gap-3">
            {item.imageUrl && (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={item.imageUrl} alt={item.name} className="h-10 w-14 rounded object-cover" />
            )}
            <div className="min-w-0">
              <p className="truncate text-sm font-medium text-gray-900">{item.name}</p>
              {item.wear && <p className="truncate text-xs text-gray-500">{item.wear}</p>}
            </div>
          </div>
        </Row>
        <Row label={t("rows.price")}>
          <span className="text-sm text-gray-900">{formatStablecoin(price, stablecoin)}</span>
        </Row>
        <Row label={t("rows.commission")}>
          <span className="text-sm text-gray-600">
            {commissionDisplay} <span className="text-xs">({t("rows.commissionPayer")})</span>
          </span>
        </Row>
        <Row label={t("rows.networks")}>
          <ul className="space-y-0.5 text-sm text-gray-900" data-testid="summary-networks">
            {acceptedNetworks.map((network) => (
              <li key={network}>
                {stablecoin} · {networkLabel(network)}
              </li>
            ))}
          </ul>
        </Row>
        <Row label={t("rows.timeout")}>
          <span className="text-sm text-gray-900">
            {t("rows.timeoutValue", { hours: paymentTimeoutHours })}
          </span>
        </Row>
        <Row label={t("rows.buyer")}>
          {buyerMethod === BuyerIdentificationMethod.STEAM_ID ? (
            <span className="font-mono text-sm text-gray-900">
              {t("rows.buyerSteamId", { steamId: buyerSteamId })}
            </span>
          ) : (
            <span className="text-sm text-gray-900">{t("rows.buyerOpenLink")}</span>
          )}
        </Row>
        <Row label={t("rows.wallet")}>
          <ul className="space-y-0.5 text-sm text-gray-900">
            {acceptedNetworks.map((network) => {
              const address =
                networkOptions.find((o) => o.network === network)?.payoutAddress ?? "";
              return (
                <li key={network}>
                  <span className="text-xs text-gray-600">{networkLabel(network)}:</span>{" "}
                  <span className="font-mono" title={address}>
                    {maskAddress(address)}
                  </span>
                </li>
              );
            })}
          </ul>
        </Row>
      </dl>

      {submitError && (
        <div
          role="alert"
          className="rounded-md border border-red-200 bg-red-50 p-3 text-sm text-red-900"
        >
          <p>{submitError.message}</p>
          {submitError.network && (
            <p className="mt-1 text-xs">{t("errorNetwork", { network: submitError.network })}</p>
          )}
        </div>
      )}

      <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-between">
        <button
          type="button"
          onClick={onBack}
          disabled={isSubmitting}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50"
        >
          {t("back")}
        </button>
        <button
          type="button"
          onClick={onSubmit}
          disabled={isSubmitting}
          className="inline-flex items-center justify-center gap-2 rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50"
        >
          {isSubmitting && <Spinner size="sm" />}
          {isSubmitting ? t("submitting") : t("submit")}
        </button>
      </div>
    </div>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1 px-4 py-3 sm:flex-row sm:items-center sm:gap-6">
      <dt className="w-32 flex-shrink-0 text-xs font-medium uppercase tracking-wide text-gray-500">
        {label}
      </dt>
      <dd className="flex-1 min-w-0">{children}</dd>
    </div>
  );
}
