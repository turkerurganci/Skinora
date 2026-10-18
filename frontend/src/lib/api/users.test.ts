import { describe, it, expect } from "vitest";
import { BlockchainNetwork } from "@/types/enums";
import { findWallet, walletChangeNeedsReAuth, type UserWalletEntry } from "./users";

const entry = (
  network: BlockchainNetwork,
  type: "PAYOUT" | "REFUND",
  address = "addr",
): UserWalletEntry => ({ network, type, address, changedAt: null });

describe("walletChangeNeedsReAuth (07 §5.3 Ek Auth — T146 decision xv)", () => {
  it("the very first address of a type needs no re-auth", () => {
    expect(walletChangeNeedsReAuth([], "PAYOUT")).toBe(false);
    expect(walletChangeNeedsReAuth(undefined, "REFUND")).toBe(false);
  });

  it("a FIRST address on a NEW network needs re-auth when another network has one of the same type", () => {
    // A hijacked session must not add a second payout target by picking a
    // network the account has not used yet.
    expect(walletChangeNeedsReAuth([entry(BlockchainNetwork.TRON, "PAYOUT")], "PAYOUT")).toBe(true);
  });

  it("changing an existing address needs re-auth", () => {
    expect(walletChangeNeedsReAuth([entry(BlockchainNetwork.ETHEREUM, "REFUND")], "REFUND")).toBe(
      true,
    );
  });

  it("an address of the OTHER type does not count (T147 decision xi — per type)", () => {
    expect(walletChangeNeedsReAuth([entry(BlockchainNetwork.TRON, "REFUND")], "PAYOUT")).toBe(
      false,
    );
  });
});

describe("findWallet", () => {
  it("matches network AND type", () => {
    const wallets = [
      entry(BlockchainNetwork.TRON, "PAYOUT", "T-payout"),
      entry(BlockchainNetwork.ETHEREUM, "REFUND", "0x-refund"),
    ];
    expect(findWallet(wallets, BlockchainNetwork.TRON, "PAYOUT")).toBe("T-payout");
    expect(findWallet(wallets, BlockchainNetwork.ETHEREUM, "PAYOUT")).toBeNull();
    expect(findWallet(wallets, BlockchainNetwork.ETHEREUM, "REFUND")).toBe("0x-refund");
  });
});
