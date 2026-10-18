import { useTranslations, useLocale } from "next-intl";
import type { TransactionDetailResponse } from "@/lib/api/transactions";
import { formatDateTime, formatStablecoin } from "@/lib/utils/format";
import { networkLabel } from "@/lib/utils/blockchain";
import { BlockchainNetwork } from "@/types/enums";
import { buyerNetworkOptions, showsNetworkFeeLine } from "@/lib/transactions/networkChoice";
import { detailNetwork } from "./helpers";

export interface TransactionInfoPanelProps {
  detail: TransactionDetailResponse;
}

/**
 * 04 §7.3 sabit layout — sağ taraftaki "İşlem Bilgileri" kutusu.
 * Commission/total satırları sadece authenticated callers için döner
 * (public surface bunları null tutar — 07 §7.5).
 *
 * T156 — the network:
 *   - once accepted: the chosen network as an explicit label, and on TRON the
 *     network fee as its OWN line (K8); Ethereum has no such line. The total
 *     is the server's `totalAmount`, which already contains the line (T149).
 *   - before acceptance: the networks the listing accepts.
 * Every amount is the API string verbatim — nothing is added up here.
 */
export function TransactionInfoPanel({ detail }: TransactionInfoPanelProps) {
  const t = useTranslations("transactionDetail.info");
  const locale = useLocale();
  const network = detailNetwork(detail);
  const accepted = network
    ? []
    : buyerNetworkOptions(detail.acceptedNetworks, detail.networkQuotes).map((o) => o.network);
  const feeLine = showsNetworkFeeLine(network) ? (detail.networkFeeAmount ?? null) : null;
  return (
    <dl className="space-y-2 rounded-lg border border-gray-200 bg-white p-4 text-sm">
      <div className="flex justify-between gap-3">
        <dt className="text-gray-600">{t("price")}</dt>
        <dd className="font-semibold text-gray-900">
          {formatStablecoin(detail.price, detail.stablecoin)}
        </dd>
      </div>
      {detail.commissionAmount != null && (
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("commission")}</dt>
          <dd className="text-gray-900">
            {formatStablecoin(detail.commissionAmount, detail.stablecoin)}
          </dd>
        </div>
      )}
      {feeLine !== null && (
        <div className="flex justify-between gap-3" data-testid="info-network-fee">
          <dt className="text-gray-600">{t("tronNetworkFee")}</dt>
          <dd className="text-gray-900">{formatStablecoin(feeLine, detail.stablecoin)}</dd>
        </div>
      )}
      {detail.totalAmount != null && (
        <div className="flex justify-between gap-3 border-t border-gray-100 pt-2">
          <dt className="font-medium text-gray-700">
            {/* Before acceptance the server's total is price + commission; on
                TRON the buyer will also pay the fee line, so say so here
                rather than show a figure that is not what TRON costs. */}
            {!network && accepted.includes(BlockchainNetwork.TRON)
              ? t("totalExcludingTronFee")
              : t("total")}
          </dt>
          <dd className="font-semibold text-gray-900">
            {formatStablecoin(detail.totalAmount, detail.stablecoin)}
          </dd>
        </div>
      )}
      <div className="flex justify-between gap-3">
        <dt className="text-gray-600">{t("token")}</dt>
        <dd className="text-gray-900">{detail.stablecoin}</dd>
      </div>
      {network ? (
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("network")}</dt>
          <dd className="font-semibold text-gray-900" data-testid="info-network">
            {networkLabel(network)}
          </dd>
        </div>
      ) : (
        accepted.length > 0 && (
          <div className="flex justify-between gap-3">
            <dt className="text-gray-600">{t("acceptedNetworks")}</dt>
            <dd className="text-right text-gray-900" data-testid="info-accepted-networks">
              {accepted.map(networkLabel).join(", ")}
            </dd>
          </div>
        )
      )}
      {detail.createdAt && (
        <div className="flex justify-between gap-3">
          <dt className="text-gray-600">{t("createdAt")}</dt>
          <dd className="text-gray-900">{formatDateTime(detail.createdAt, locale)}</dd>
        </div>
      )}
    </dl>
  );
}
