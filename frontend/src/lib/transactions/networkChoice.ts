import { BlockchainNetwork } from "@/types/enums";
import { ALL_NETWORKS, parseNetwork } from "@/lib/utils/blockchain";
import { findWallet, type UserWalletEntry } from "@/lib/api/users";
import type { NetworkMinPrice, TransactionNetworkQuote } from "@/lib/api/transactions";

/**
 * T156 — the network rules of the listing wizard (S06 step 2, 02 §4.2, 03 §2.2
 * step 9) and of the accept form (S07 CREATED, 03 §3.2 step 4), as pure
 * functions so the rules are tested apart from the markup.
 */

/** A network the seller is offered in step 2. */
export interface SellerNetworkOption {
  network: BlockchainNetwork;
  /** The payout address the listing would fix for this network (06 §3.5b). */
  payoutAddress: string | null;
  /** Only networks with a saved payout address can be ticked (02 §4.2). */
  selectable: boolean;
  /**
   * T169 — the network's own minimum price (TRON 50.00) when the entered price
   * is below it: the network cannot be ticked for this price. Absent otherwise.
   */
  belowMinPrice?: string;
}

/**
 * Networks offered to the seller: exactly the ones `GET /transactions/params`
 * says are open (`supportedNetworks`, 07 §7.4), in enum order. A network that
 * is not open is NOT shown — a listing on it would be rejected with
 * `NETWORK_UNAVAILABLE` (07 §7.2) and no buyer could pay on it. An open network
 * without a payout address is shown but cannot be ticked.
 *
 * Unknown wire values are dropped rather than guessed.
 */
export function sellerNetworkOptions(
  supportedNetworks: readonly string[] | null | undefined,
  wallets: readonly UserWalletEntry[] | null | undefined,
): SellerNetworkOption[] {
  const open = new Set(
    (supportedNetworks ?? []).map(parseNetwork).filter((n): n is BlockchainNetwork => n !== null),
  );
  return ALL_NETWORKS.filter((n) => open.has(n)).map((network) => {
    const payoutAddress = findWallet(wallets, network, "PAYOUT");
    return { network, payoutAddress, selectable: payoutAddress !== null };
  });
}

/** T169 — `network`'s own minimum price from `networkMinPrices`, or null. */
export function networkMinPrice(
  network: BlockchainNetwork,
  minPrices: readonly NetworkMinPrice[] | null | undefined,
): string | null {
  return minPrices?.find((m) => m.network === network)?.minPrice ?? null;
}

/**
 * T169 — the step-2 options for the entered `price`: a network whose own
 * minimum the price misses (no TRON below 50 USDT) cannot be ticked and says
 * why. An empty or non-numeric price changes nothing (the price field reports
 * its own error). The price-free options stay the input of
 * {@link networkGateReason}: that gate answers "can this seller list at all".
 */
export function withPriceMinimums(
  options: readonly SellerNetworkOption[],
  price: string,
  minPrices: readonly NetworkMinPrice[] | null | undefined,
): SellerNetworkOption[] {
  const value = price.trim() === "" ? Number.NaN : Number(price);
  return options.map((option) => {
    const min = networkMinPrice(option.network, minPrices);
    if (min === null || !Number.isFinite(value) || value >= Number(min)) return option;
    return { ...option, selectable: false, belowMinPrice: min };
  });
}

/**
 * The networks the listing will be created with. `chosen === null` means the
 * seller has not touched the selection yet: every selectable network is
 * accepted (the widest reach for buyers). Whatever was chosen is intersected
 * with today's selectable set, so a restored draft cannot carry a network that
 * has since closed or lost its payout address.
 */
export function effectiveAcceptedNetworks(
  chosen: readonly BlockchainNetwork[] | null,
  options: readonly SellerNetworkOption[],
): BlockchainNetwork[] {
  const selectable = options.filter((o) => o.selectable).map((o) => o.network);
  if (chosen === null) return selectable;
  return selectable.filter((n) => chosen.includes(n));
}

/**
 * Why the wizard cannot be used even though `GET /transactions/eligibility`
 * said "eligible" (11 T156, from the T147 validation). Eligibility answers
 * "can this seller list on ANY network" and ignores availability (T147
 * decision iv), so a seller whose only payout address is on a network that is
 * not open right now passes it and would otherwise meet an empty step 2.
 */
export type NetworkGateReason = "NO_OPEN_NETWORK" | "NO_PAYOUT_ON_OPEN_NETWORK";

export function networkGateReason(
  options: readonly SellerNetworkOption[],
): NetworkGateReason | null {
  if (options.length === 0) return "NO_OPEN_NETWORK";
  if (!options.some((o) => o.selectable)) return "NO_PAYOUT_ON_OPEN_NETWORK";
  return null;
}

/** A network the buyer can pick on the accept form, with what it costs. */
export interface BuyerNetworkOption {
  network: BlockchainNetwork;
  /** `networkQuotes[]` entry for it; null when the server sent none. */
  quote: TransactionNetworkQuote | null;
  /**
   * T169 — the network's own minimum the price misses (from the quote's
   * `unavailableReason`/`minAmount`): the option is shown but cannot be chosen.
   */
  belowMinPrice?: string;
}

/**
 * The buyer's choices: the listing's `acceptedNetworks` (07 §7.5), enum order,
 * each with its quote. Unknown values are dropped.
 */
export function buyerNetworkOptions(
  acceptedNetworks: readonly string[] | null | undefined,
  quotes: readonly TransactionNetworkQuote[] | null | undefined,
): BuyerNetworkOption[] {
  const accepted = new Set(
    (acceptedNetworks ?? []).map(parseNetwork).filter((n): n is BlockchainNetwork => n !== null),
  );
  return ALL_NETWORKS.filter((n) => accepted.has(n)).map((network) => {
    const quote = quotes?.find((q) => q.network === network) ?? null;
    return quote?.unavailableReason === "NETWORK_MIN_AMOUNT_NOT_MET" && quote.minAmount
      ? { network, quote, belowMinPrice: quote.minAmount }
      : { network, quote };
  });
}

/**
 * K8 (11 §F8): the TRON network fee is its own visible line; Ethereum has NO
 * such line (the platform carries Ethereum's fee). The rule is keyed on the
 * network, not on the amount — a `"0.00"` TRON line (a transaction accepted
 * before T149) is still a TRON line, and an Ethereum response never shows one
 * whatever it carries.
 */
export function showsNetworkFeeLine(network: BlockchainNetwork | null): boolean {
  return network === BlockchainNetwork.TRON;
}
