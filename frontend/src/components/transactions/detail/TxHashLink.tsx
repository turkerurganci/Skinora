import { useTranslations } from "next-intl";
import { CopyButton } from "@/components/common";
import { tronscanTxUrl } from "@/lib/utils/blockchain";
import { maskAddress } from "./helpers";

export interface TxHashLinkProps {
  txHash: string;
  /** Mask widths; default 8/6 matches the existing detail-block layout. */
  lead?: number;
  tail?: number;
}

/**
 * Renders an on-chain transaction hash as a masked Tronscan explorer link plus
 * a copy button (WP13 — the payout / refund / payment-event blocks previously
 * showed inert masked text). Inline fragment so it drops into the existing
 * <dd>/<p> layouts without changing their structure.
 */
export function TxHashLink({ txHash, lead = 8, tail = 6 }: TxHashLinkProps) {
  const t = useTranslations("transactionDetail");
  return (
    <>
      <a
        href={tronscanTxUrl(txHash)}
        target="_blank"
        rel="noopener noreferrer"
        title={t("viewOnTronscan")}
        className="text-blue-600 hover:text-blue-700 hover:underline"
      >
        {maskAddress(txHash, lead, tail)}
      </a>
      <CopyButton value={txHash} />
    </>
  );
}
