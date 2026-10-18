import { useTranslations } from "next-intl";
import { CopyButton } from "@/components/common";
import { explorerTxUrl, networkMeta } from "@/lib/utils/blockchain";
import { BlockchainNetwork } from "@/types/enums";
import { maskAddress } from "./helpers";

export interface TxHashLinkProps {
  txHash: string;
  /**
   * T156 — the network the hash lives on; picks Tronscan or Etherscan
   * (11 T156 "Tronscan/Etherscan bağlantıları ağa göre"). `null` only for a
   * record without a network (every pre-F8 row is TRON — 06 §1.4, T148
   * decision vi), so it links to Tronscan.
   */
  network: BlockchainNetwork | null;
  /** Mask widths; default 8/6 matches the existing detail-block layout. */
  lead?: number;
  tail?: number;
}

/**
 * Renders an on-chain transaction hash as a masked explorer link plus a copy
 * button (WP13). Inline fragment so it drops into the existing <dd>/<p>
 * layouts without changing their structure.
 */
export function TxHashLink({ txHash, network, lead = 8, tail = 6 }: TxHashLinkProps) {
  const t = useTranslations("transactionDetail");
  const effective = network ?? BlockchainNetwork.TRON;
  return (
    <>
      <a
        href={explorerTxUrl(effective, txHash)}
        target="_blank"
        rel="noopener noreferrer"
        title={t("viewOnExplorer", { explorer: networkMeta(effective).explorerName })}
        className="text-blue-600 hover:text-blue-700 hover:underline"
      >
        {maskAddress(txHash, lead, tail)}
      </a>
      <CopyButton value={txHash} />
    </>
  );
}
