import { describe, it, expect } from "vitest";
import { BlockchainNetwork } from "@/types/enums";
import type { UserWalletEntry } from "@/lib/api/users";
import {
  buyerNetworkOptions,
  effectiveAcceptedNetworks,
  networkGateReason,
  sellerNetworkOptions,
  showsNetworkFeeLine,
} from "./networkChoice";

const { TRON, ETHEREUM } = BlockchainNetwork;
const TRON_ADDR = "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH";
const ETH_ADDR = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";

function wallet(network: BlockchainNetwork, type: "PAYOUT" | "REFUND", address: string) {
  return { network, type, address, changedAt: null } satisfies UserWalletEntry;
}

describe("sellerNetworkOptions — the wizard offers supportedNetworks only (11 T156)", () => {
  it("does NOT offer a network missing from supportedNetworks, even with a payout address", () => {
    const options = sellerNetworkOptions(
      ["TRON"],
      [wallet(TRON, "PAYOUT", TRON_ADDR), wallet(ETHEREUM, "PAYOUT", ETH_ADDR)],
    );
    expect(options.map((o) => o.network)).toEqual([TRON]);
  });

  it("offers an open network without a payout address, but not as selectable (02 §4.2)", () => {
    const options = sellerNetworkOptions(["TRON", "ETHEREUM"], [wallet(TRON, "PAYOUT", TRON_ADDR)]);
    expect(options).toEqual([
      { network: TRON, payoutAddress: TRON_ADDR, selectable: true },
      { network: ETHEREUM, payoutAddress: null, selectable: false },
    ]);
  });

  it("a REFUND address does not make a network selectable for the seller", () => {
    const options = sellerNetworkOptions(["ETHEREUM"], [wallet(ETHEREUM, "REFUND", ETH_ADDR)]);
    expect(options[0].selectable).toBe(false);
  });

  it("keeps enum order and drops unknown wire values", () => {
    const options = sellerNetworkOptions(
      ["ETHEREUM", "BSC", "TRON"],
      [wallet(TRON, "PAYOUT", TRON_ADDR), wallet(ETHEREUM, "PAYOUT", ETH_ADDR)],
    );
    expect(options.map((o) => o.network)).toEqual([TRON, ETHEREUM]);
  });
});

describe("effectiveAcceptedNetworks", () => {
  const both = sellerNetworkOptions(
    ["TRON", "ETHEREUM"],
    [wallet(TRON, "PAYOUT", TRON_ADDR), wallet(ETHEREUM, "PAYOUT", ETH_ADDR)],
  );

  it("untouched selection = every selectable network", () => {
    expect(effectiveAcceptedNetworks(null, both)).toEqual([TRON, ETHEREUM]);
  });

  it("a restored choice cannot carry a network that is no longer selectable", () => {
    const tronOnly = sellerNetworkOptions(["TRON"], [wallet(TRON, "PAYOUT", TRON_ADDR)]);
    expect(effectiveAcceptedNetworks([ETHEREUM], tronOnly)).toEqual([]);
    expect(effectiveAcceptedNetworks([TRON, ETHEREUM], tronOnly)).toEqual([TRON]);
  });

  it("an explicitly emptied choice stays empty (step 2 then blocks)", () => {
    expect(effectiveAcceptedNetworks([], both)).toEqual([]);
  });
});

describe("networkGateReason — eligible on paper, nothing to list on (T147 validation)", () => {
  it("no open network at all", () => {
    expect(networkGateReason(sellerNetworkOptions([], [wallet(TRON, "PAYOUT", TRON_ADDR)]))).toBe(
      "NO_OPEN_NETWORK",
    );
  });

  it("the only payout address is on a network that is not open", () => {
    // Eligibility says "eligible" (an address on ANY network — T147 decision iv).
    const options = sellerNetworkOptions(["TRON"], [wallet(ETHEREUM, "PAYOUT", ETH_ADDR)]);
    expect(networkGateReason(options)).toBe("NO_PAYOUT_ON_OPEN_NETWORK");
  });

  it("no gate when one open network has a payout address", () => {
    const options = sellerNetworkOptions(["TRON", "ETHEREUM"], [wallet(TRON, "PAYOUT", TRON_ADDR)]);
    expect(networkGateReason(options)).toBeNull();
  });
});

describe("buyerNetworkOptions", () => {
  it("pairs each accepted network with its quote, enum order", () => {
    const quotes = [
      { network: "ETHEREUM", networkFeeAmount: "0.00", totalAmount: "20.40" },
      { network: "TRON", networkFeeAmount: "2.90", totalAmount: "23.30" },
    ];
    expect(buyerNetworkOptions(["ETHEREUM", "TRON"], quotes)).toEqual([
      { network: TRON, quote: quotes[1] },
      { network: ETHEREUM, quote: quotes[0] },
    ]);
  });

  it("a network without a quote is still offered, with no amount invented", () => {
    expect(buyerNetworkOptions(["TRON"], null)).toEqual([{ network: TRON, quote: null }]);
  });
});

describe("showsNetworkFeeLine — K8: TRON has the line, Ethereum never", () => {
  it("is keyed on the network, not on the amount", () => {
    expect(showsNetworkFeeLine(TRON)).toBe(true);
    expect(showsNetworkFeeLine(ETHEREUM)).toBe(false);
    expect(showsNetworkFeeLine(null)).toBe(false);
  });
});
