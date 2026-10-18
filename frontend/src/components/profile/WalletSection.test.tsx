import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import messages from "@/i18n/messages/en.json";
import { BlockchainNetwork } from "@/types/enums";
import type { UserWalletEntry } from "@/lib/api/users";
import { WalletSection } from "./WalletSection";

/**
 * T156 — S08 wallet addresses per network (02 §12.1–§12.3, 03 §9.1, 07 §5.3).
 */

const { initiateSteamReVerify, updateSellerWallet, updateRefundWallet } = vi.hoisted(() => ({
  initiateSteamReVerify: vi.fn(),
  updateSellerWallet: vi.fn(),
  updateRefundWallet: vi.fn(),
}));
vi.mock("@/lib/api/auth", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/auth")>()),
  initiateSteamReVerify,
}));
vi.mock("@/lib/api/users", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/users")>()),
  updateSellerWallet,
  updateRefundWallet,
}));

const TRON_ADDR = "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH";
const ETH_ADDR = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";

function renderSection(
  wallets: UserWalletEntry[],
  callback: { token: string | null; network: BlockchainNetwork | null } = {
    token: null,
    network: null,
  },
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale="en" messages={messages}>
        <WalletSection
          role="seller"
          wallets={wallets}
          activeReAuthToken={callback.token}
          activeRoleFromCallback={callback.token ? "seller" : null}
          activeNetworkFromCallback={callback.network}
          onChangeCancelled={vi.fn()}
          onSavedSuccessfully={vi.fn()}
        />
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  initiateSteamReVerify.mockReset();
  initiateSteamReVerify.mockResolvedValue({ steamAuthUrl: "https://steamcommunity.com/openid" });
  updateSellerWallet.mockReset();
  updateSellerWallet.mockResolvedValue({
    walletAddress: ETH_ADDR,
    updatedAt: "2026-10-18T10:00:00Z",
    activeTransactionsUsingOldAddress: 0,
  });
  updateRefundWallet.mockReset();
});
afterEach(cleanup);

describe("WalletSection — one row per network", () => {
  it("shows each network's own address, labelled", () => {
    renderSection([
      { network: BlockchainNetwork.TRON, type: "PAYOUT", address: TRON_ADDR, changedAt: null },
    ]);
    expect(screen.getByTestId("wallet-row-seller-TRON")).toHaveTextContent("TRON (TRC-20)");
    expect(screen.getByTestId("wallet-row-seller-TRON")).toHaveTextContent("TUEZSd...WYdH");
    expect(screen.getByTestId("wallet-row-seller-ETHEREUM")).toHaveTextContent("Ethereum (ERC-20)");
    expect(screen.getByTestId("wallet-row-seller-ETHEREUM")).toHaveTextContent(
      "No address has been set yet.",
    );
  });
});

describe("WalletSection — Steam re-auth trigger (T146 decision xv)", () => {
  it("the very first payout address opens the input without re-auth", () => {
    renderSection([]);
    fireEvent.click(screen.getByTestId("wallet-change-seller-ETHEREUM"));
    expect(initiateSteamReVerify).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Ethereum (ERC-20) wallet address")).toBeVisible();
  });

  it("a FIRST address on a NEW network re-auths when another network already has one", async () => {
    renderSection([
      { network: BlockchainNetwork.TRON, type: "PAYOUT", address: TRON_ADDR, changedAt: null },
    ]);
    fireEvent.click(screen.getByTestId("wallet-change-seller-ETHEREUM"));
    await waitFor(() =>
      expect(initiateSteamReVerify).toHaveBeenCalledWith(
        "wallet_change",
        "/profile?walletChange=seller&walletNetwork=ETHEREUM",
      ),
    );
  });

  it("a refund address on another network does not trigger payout re-auth", () => {
    renderSection([
      { network: BlockchainNetwork.TRON, type: "REFUND", address: TRON_ADDR, changedAt: null },
    ]);
    fireEvent.click(screen.getByTestId("wallet-change-seller-TRON"));
    expect(initiateSteamReVerify).not.toHaveBeenCalled();
  });

  it("back from re-auth: the started network's row opens and the PUT carries network + token", async () => {
    renderSection(
      [{ network: BlockchainNetwork.TRON, type: "PAYOUT", address: TRON_ADDR, changedAt: null }],
      { token: "reauth-123", network: BlockchainNetwork.ETHEREUM },
    );
    const input = screen.getByLabelText("Ethereum (ERC-20) wallet address");
    expect(screen.queryByLabelText("TRON (TRC-20) wallet address")).toBeNull();
    fireEvent.change(input, { target: { value: ETH_ADDR } });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(screen.getByTestId("wallet-address-confirm-network")).toHaveTextContent(
      "Ethereum (ERC-20)",
    );
    fireEvent.click(screen.getByRole("button", { name: "Confirm" }));
    await waitFor(() =>
      expect(updateSellerWallet).toHaveBeenCalledWith(
        { walletAddress: ETH_ADDR, network: BlockchainNetwork.ETHEREUM },
        "reauth-123",
      ),
    );
  });
});

describe("WalletSection — EIP-55 second confirmation (07 §5.3 confirmLowercase)", () => {
  it("an all-lowercase Ethereum address is saved only after the extra confirmation, with confirmLowercase", async () => {
    renderSection([]);
    fireEvent.click(screen.getByTestId("wallet-change-seller-ETHEREUM"));
    fireEvent.change(screen.getByLabelText("Ethereum (ERC-20) wallet address"), {
      target: { value: ETH_ADDR.toLowerCase() },
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    const confirm = screen.getByRole("button", { name: "Confirm" });
    expect(confirm).toBeDisabled();
    fireEvent.click(screen.getByTestId("wallet-address-checksum-confirm"));
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    await waitFor(() =>
      expect(updateSellerWallet).toHaveBeenCalledWith(
        {
          walletAddress: ETH_ADDR.toLowerCase(),
          network: BlockchainNetwork.ETHEREUM,
          confirmLowercase: true,
        },
        null,
      ),
    );
  });

  it("a TRON address cannot be entered on the Ethereum row", () => {
    renderSection([]);
    fireEvent.click(screen.getByTestId("wallet-change-seller-ETHEREUM"));
    fireEvent.change(screen.getByLabelText("Ethereum (ERC-20) wallet address"), {
      target: { value: TRON_ADDR },
    });
    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(screen.getByRole("alert")).toHaveTextContent("Enter a valid Ethereum (ERC-20) address.");
    expect(updateSellerWallet).not.toHaveBeenCalled();
  });
});
