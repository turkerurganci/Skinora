"use client";

import { useId } from "react";
import { useLocale, useTranslations } from "next-intl";
import { CopyButton } from "@/components/common";
import { TxHashLink } from "@/components/transactions/detail/TxHashLink";
import type { NetworkWallets, PlatformWallet } from "@/lib/api/admin";
import { tDynamic } from "@/lib/i18n/dynamicKey";
import { networkLabel } from "@/lib/utils/blockchain";
import { formatDateTime } from "@/lib/utils/format";
import { BlockchainNetwork } from "@/types/enums";
import { ColdTransferForm } from "./ColdTransferForm";
import { OutboundClosurePanel } from "./OutboundClosurePanel";

const TH = "px-3 py-2";
const HEAD_ROW =
  "border-b border-gray-200 bg-gray-50 text-xs font-medium uppercase tracking-wide text-gray-500";

function WalletRow({ wallet, data }: { wallet: PlatformWallet; data: NetworkWallets }) {
  const t = useTranslations("adminWallets");
  const address = wallet.address ?? wallet.configuredAddress;
  const status = data.balancesStatus;

  let balances: React.ReactNode;
  if (wallet.balances !== null && status === "OK") {
    balances = (
      <ul className="flex flex-col gap-0.5">
        {wallet.balances.map((b) => (
          <li key={b.symbol} className="tabular-nums">
            {b.amount} {b.symbol}
          </li>
        ))}
      </ul>
    );
  } else {
    // Never render a missing balance as zero.
    balances = (
      <span className="text-amber-700">
        {status === "NOT_CONFIGURED" ? t("balanceNotConfigured") : t("balanceUnavailable")}
      </span>
    );
  }

  return (
    <tr className="border-b border-gray-100 align-top last:border-b-0">
      <td className="px-3 py-2 font-medium">{t(`roles.${wallet.role}`)}</td>
      <td className="px-3 py-2">
        {address ? (
          <span className="inline-flex flex-wrap items-center gap-2">
            <span className="break-all font-mono text-xs">{address}</span>
            <CopyButton value={address} />
          </span>
        ) : (
          <span className="text-gray-500">{t("addressNotConfigured")}</span>
        )}
      </td>
      <td className="px-3 py-2">{balances}</td>
      <td className="px-3 py-2">
        {wallet.nativeFloor ? (
          <div className="flex flex-col gap-1">
            <span className="tabular-nums">
              {t("floor.value", { amount: wallet.nativeFloor, asset: data.nativeAsset })}
            </span>
            {wallet.belowNativeFloor === true && (
              <span
                role="alert"
                data-testid={`below-floor-${data.network}-${wallet.role}`}
                className="rounded bg-red-50 px-2 py-1 text-xs font-medium text-red-700"
              >
                {t("floor.below", { asset: data.nativeAsset })}
              </span>
            )}
          </div>
        ) : (
          <span className="text-gray-400">{t("floor.none")}</span>
        )}
      </td>
    </tr>
  );
}

export interface NetworkWalletsCardProps {
  data: NetworkWallets;
}

/**
 * One network's wallet section (AD38): service state, wallets with balances,
 * outbound queue, the hot→cold transfer form and recent activity. Every figure
 * on it belongs to this network only; the heading names the network.
 */
export function NetworkWalletsCard({ data }: NetworkWalletsCardProps) {
  const t = useTranslations("adminWallets");
  const tType = useTranslations("adminWallets.outboundType");
  const locale = useLocale();
  const headingId = useId();
  const label = networkLabel(data.network);
  const { outbound } = data;
  const mismatched = data.wallets.filter((w) => w.addressMismatch);

  return (
    <section
      aria-labelledby={headingId}
      data-testid={`network-${data.network}`}
      className="flex flex-col gap-4 rounded-lg border border-gray-200 bg-white p-4 shadow-sm"
    >
      <h2 id={headingId} className="text-lg font-semibold text-gray-900">
        {label}
      </h2>

      {!data.serviceConfigured && (
        <p role="status" className="rounded-md bg-amber-50 p-3 text-sm text-amber-900">
          {t("state.notConfigured")}
        </p>
      )}
      {data.serviceConfigured && data.degraded && (
        <p role="status" className="rounded-md bg-amber-50 p-3 text-sm text-amber-900">
          {t("state.degraded")}
        </p>
      )}
      {data.balancesStatus !== "OK" && (
        <p
          role="status"
          data-testid={`balances-status-${data.network}`}
          className="rounded-md bg-amber-50 p-3 text-sm text-amber-900"
        >
          {t(`balancesStatus.${data.balancesStatus}`)}
        </p>
      )}
      {mismatched.map((w) => (
        <p
          key={w.role}
          role="alert"
          data-testid={`address-mismatch-${data.network}-${w.role}`}
          className="rounded-md border border-red-300 bg-red-50 p-3 text-sm font-medium text-red-800"
        >
          {t("addressMismatch", {
            configured: w.configuredAddress ?? "—",
            actual: w.address ?? "—",
          })}
        </p>
      ))}

      <div className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-gray-900">{t("walletsHeading")}</h3>
        <div className="overflow-x-auto">
          <table
            className="w-full table-auto border-collapse text-left text-sm"
            aria-label={t("tableAriaLabel", { network: label })}
          >
            <thead>
              <tr className={HEAD_ROW}>
                <th scope="col" className={TH}>
                  {t("columns.role")}
                </th>
                <th scope="col" className={TH}>
                  {t("columns.address")}
                </th>
                <th scope="col" className={TH}>
                  {t("columns.balances")}
                </th>
                <th scope="col" className={TH}>
                  {t("columns.nativeFloor", { asset: data.nativeAsset })}
                </th>
              </tr>
            </thead>
            <tbody>
              {data.wallets.map((w) => (
                <WalletRow key={w.role} wallet={w} data={data} />
              ))}
            </tbody>
          </table>
        </div>
        {data.balancesStatus === "OK" && data.balancesBlock !== null && (
          <p className="text-xs text-gray-500">
            {t("balancesAtBlock", { block: data.balancesBlock })}
          </p>
        )}
      </div>

      <div className="flex flex-col gap-1">
        <h3 className="text-sm font-semibold text-gray-900">{t("outbound.heading")}</h3>
        <ul className="flex flex-wrap gap-x-6 gap-y-1 text-sm text-gray-700">
          <li>{t("outbound.pending", { count: outbound.pendingCount })}</li>
          <li>
            {outbound.oldestPendingAt
              ? t("outbound.oldestPending", {
                  date: formatDateTime(outbound.oldestPendingAt, locale),
                })
              : t("outbound.noneOldest")}
          </li>
          <li>{t("outbound.inFlight", { count: outbound.inFlightCount })}</li>
        </ul>
      </div>

      <ColdTransferForm data={data} />

      <div className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-gray-900">{t("coldHistory.heading")}</h3>
        {data.recentColdTransfers.length === 0 ? (
          <p className="text-sm text-gray-500">{t("coldHistory.empty")}</p>
        ) : (
          <div className="overflow-x-auto">
            <table
              className="w-full table-auto border-collapse text-left text-sm"
              aria-label={t("coldHistory.ariaLabel", { network: label })}
            >
              <thead>
                <tr className={HEAD_ROW}>
                  <th scope="col" className={TH}>
                    {t("coldHistory.columns.date")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("coldHistory.columns.amount")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("coldHistory.columns.from")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("coldHistory.columns.to")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("coldHistory.columns.tx")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("coldHistory.columns.by")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.recentColdTransfers.map((c) => (
                  <tr key={c.id} className="border-b border-gray-100 last:border-b-0">
                    <td className="px-3 py-2">
                      <time dateTime={c.createdAt}>{formatDateTime(c.createdAt, locale)}</time>
                    </td>
                    <td className="px-3 py-2 tabular-nums">
                      {c.amount} {c.token}
                    </td>
                    <td className="break-all px-3 py-2 font-mono text-xs">{c.fromAddress}</td>
                    <td className="break-all px-3 py-2 font-mono text-xs">{c.toAddress}</td>
                    <td className="px-3 py-2 font-mono text-xs">
                      <TxHashLink txHash={c.txHash} network={data.network} />
                    </td>
                    <td className="px-3 py-2">{c.initiatedBy}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="flex flex-col gap-2">
        <h3 className="text-sm font-semibold text-gray-900">{t("outboundRows.heading")}</h3>
        {data.recentOutbound.length === 0 ? (
          <p className="text-sm text-gray-500">{t("outboundRows.empty")}</p>
        ) : (
          <div className="overflow-x-auto">
            <table
              className="w-full table-auto border-collapse text-left text-sm"
              aria-label={t("outboundRows.ariaLabel", { network: label })}
            >
              <thead>
                <tr className={HEAD_ROW}>
                  <th scope="col" className={TH}>
                    {t("outboundRows.columns.type")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("outboundRows.columns.status")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("outboundRows.columns.amount")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("outboundRows.columns.to")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("outboundRows.columns.tx")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("outboundRows.columns.retries")}
                  </th>
                  <th scope="col" className={TH}>
                    {t("outboundRows.columns.created")}
                  </th>
                </tr>
              </thead>
              <tbody>
                {data.recentOutbound.map((r) => (
                  <tr key={r.id} className="border-b border-gray-100 last:border-b-0">
                    <td className="px-3 py-2">{tDynamic(tType, r.type, r.type)}</td>
                    <td className="px-3 py-2">{t(`outboundStatus.${r.status}`)}</td>
                    <td className="px-3 py-2 tabular-nums">
                      {r.amount} {r.token}
                    </td>
                    <td className="break-all px-3 py-2 font-mono text-xs">{r.toAddress}</td>
                    <td className="px-3 py-2 font-mono text-xs">
                      {r.txHash ? (
                        <TxHashLink txHash={r.txHash} network={data.network} />
                      ) : (
                        <span className="text-gray-400">{t("noHash")}</span>
                      )}
                    </td>
                    <td className="px-3 py-2 tabular-nums">{r.retryCount}</td>
                    <td className="px-3 py-2">
                      <time dateTime={r.createdAt}>{formatDateTime(r.createdAt, locale)}</time>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {data.network === BlockchainNetwork.ETHEREUM && <OutboundClosurePanel />}
    </section>
  );
}
