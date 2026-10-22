"use client";

import { useTranslations } from "next-intl";
import { CopyButton } from "@/components/common";
import { useOperatorValues } from "@/components/legal/useOperatorValues";
import { payoutSourceFor, usePayoutSources } from "@/lib/hooks/usePayoutSources";
import { ALL_NETWORKS, networkLabel } from "@/lib/utils/blockchain";
import type { BlockchainNetwork } from "@/types/enums";

export interface PayoutSourceNoticeProps {
  /**
   * The network the payout moves on. Given (transaction screens) → only that
   * network's sender. Omitted (profile, help page) → every configured network.
   */
  network?: BlockchainNetwork | null;
  /** `compact` drops the title — for use inside an existing panel. */
  compact?: boolean;
}

/**
 * T167 — the seller's side of MASAK (Tedbirler Yönetmeliği m. 24/A-6): a
 * Turkish exchange asks its customer to declare the sender of a deposit that
 * comes from a wallet not registered with any crypto-asset service provider.
 * Every Skinora payout on a network leaves from that network's one hot wallet
 * (05 §3.3; measured in T167 — the payout endpoint takes no deposit index and
 * signs with the hot key only), so the seller can declare one address once.
 * The address comes from P3 (07 §10.3); while it is unknown the notice still
 * explains the declaration and says where the sender can be seen.
 */
export function PayoutSourceNotice({ network, compact }: PayoutSourceNoticeProps) {
  const t = useTranslations("masak.seller");
  const tMasak = useTranslations("masak");
  const operator = useOperatorValues();
  const { data, isError } = usePayoutSources();
  const networks = network ? [network] : ALL_NETWORKS;
  const rows = networks
    .map((n) => ({ network: n, address: payoutSourceFor(data, n) }))
    .filter((r): r is { network: BlockchainNetwork; address: string } => r.address !== null);
  const unavailable = isError || (data !== undefined && rows.length === 0);

  return (
    <div
      className="space-y-2 rounded-md border border-blue-200 bg-blue-50 p-3 text-sm text-blue-900"
      role="note"
      data-testid="masak-seller"
    >
      {!compact && <p className="font-medium">{t("title")}</p>}
      <p>{t("declare")}</p>
      {/* T168 — Yönetmelik 24/A-6 trade name + address of the sender. */}
      <p data-testid="masak-seller-entity">{tMasak("entityDetails", operator)}</p>
      {rows.map((row) => (
        <div key={row.network} className="space-y-1" data-testid={`payout-source-${row.network}`}>
          <p className="text-xs font-medium uppercase text-blue-800">
            {t("addressLabel", { network: networkLabel(row.network) })}
          </p>
          <div className="flex items-center gap-2 rounded-md border border-blue-200 bg-white px-3 py-2 font-mono text-xs text-gray-900 break-all">
            <span className="flex-1">{row.address}</span>
            <CopyButton value={row.address} />
          </div>
        </div>
      ))}
      {unavailable && <p data-testid="payout-source-unavailable">{t("addressUnavailable")}</p>}
      {rows.length > 0 && <p className="text-xs text-blue-800">{t("changeNote")}</p>}
    </div>
  );
}
