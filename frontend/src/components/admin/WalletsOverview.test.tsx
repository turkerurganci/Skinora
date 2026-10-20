import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { ApiError } from "@/lib/api/client";
import { WalletsOverview } from "./WalletsOverview";
import {
  ETH_COLD,
  ETH_HOT,
  TRON_COLD,
  TRON_HOT,
  ethWallets,
  renderWithProviders,
  tronWallets,
  wallet,
} from "./walletTestUtils";

/**
 * T157 — the wallets overview. The point of the screen is that two networks
 * never blur into one: each has its own section, its own balances and its own
 * transfer form. A missing balance must read as "unavailable", never as zero,
 * and the cold transfer must be confirmed before anything is sent.
 */

const api = vi.hoisted(() => ({
  getAdminWallets: vi.fn(),
  hotToColdTransfer: vi.fn(),
  listOutboundClosureCandidates: vi.fn(),
  listOutboundClosures: vi.fn(),
  proposeOutboundClosure: vi.fn(),
  approveOutboundClosure: vi.fn(),
  rejectOutboundClosure: vi.fn(),
}));
vi.mock("@/lib/api/admin", () => api);
vi.mock("@/lib/api/auth", () => ({
  getMe: vi.fn().mockResolvedValue({ id: "admin-me", role: "admin", permissions: [] }),
}));

beforeEach(() => {
  api.listOutboundClosureCandidates.mockResolvedValue([]);
  api.listOutboundClosures.mockResolvedValue([]);
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

async function renderOverview(networks = [tronWallets(), ethWallets()]) {
  api.getAdminWallets.mockResolvedValue({ networks });
  renderWithProviders(<WalletsOverview />);
  await screen.findByTestId("network-TRON");
}

const tron = () => within(screen.getByTestId("network-TRON"));
const eth = () => within(screen.getByTestId("network-ETHEREUM"));

describe("WalletsOverview — one section per network", () => {
  it("shows every network in its own section, with its own balances", async () => {
    await renderOverview();

    expect(tron().getByRole("heading", { level: 2 })).toHaveTextContent("TRON (TRC-20)");
    expect(eth().getByRole("heading", { level: 2 })).toHaveTextContent("Ethereum (ERC-20)");

    // TRON figures live in the TRON section only…
    expect(tron().getByText("501.25 USDT")).toBeInTheDocument();
    expect(tron().getByText("7 USDC")).toBeInTheDocument();
    expect(tron().getAllByText(TRON_HOT).length).toBeGreaterThan(0);
    expect(tron().queryByText("33.3 USDT")).toBeNull();
    // …and Ethereum's in the Ethereum section only, with its FUEL wallet.
    expect(eth().getByText("33.3 USDT")).toBeInTheDocument();
    expect(eth().getByText("Fuel wallet")).toBeInTheDocument();
    expect(eth().queryByText("501.25 USDT")).toBeNull();
    expect(tron().queryByText("Fuel wallet")).toBeNull();

    // The closure tool belongs to the Ethereum section only.
    expect(eth().getByTestId("closure-panel")).toBeInTheDocument();
    expect(tron().queryByTestId("closure-panel")).toBeNull();
  });

  it("shows UNAVAILABLE explicitly and never as a zero balance", async () => {
    await renderOverview([
      tronWallets(),
      ethWallets({
        balancesStatus: "UNAVAILABLE",
        balancesBlock: null,
        wallets: [
          wallet({ role: "HOT", address: ETH_HOT, balances: null }),
          wallet({ role: "COLD", address: ETH_COLD, balances: null }),
        ],
      }),
    ]);

    expect(eth().getByTestId("balances-status-ETHEREUM")).toHaveTextContent(
      "Balances are unavailable",
    );
    expect(eth().getAllByText("Unavailable").length).toBeGreaterThan(0);
    expect(eth().queryByText(/^0(\.0+)?\s/)).toBeNull();
    // TRON is unaffected.
    expect(tron().queryByTestId("balances-status-TRON")).toBeNull();
    expect(tron().getByText("501.25 USDT")).toBeInTheDocument();
  });

  it("shows NOT_CONFIGURED explicitly", async () => {
    await renderOverview([
      tronWallets(),
      ethWallets({
        serviceConfigured: false,
        balancesStatus: "NOT_CONFIGURED",
        balancesBlock: null,
        wallets: [wallet({ role: "HOT" }), wallet({ role: "COLD" })],
      }),
    ]);

    expect(eth().getByTestId("balances-status-ETHEREUM")).toHaveTextContent(
      "not configured for this network",
    );
    expect(eth().getByText(/signing service for this network is not configured/i)).toBeVisible();
    expect(eth().getAllByText("Not configured").length).toBeGreaterThan(0);
    // No transfer is possible on an unconfigured network.
    expect(eth().getByTestId("cold-transfer-unavailable-ETHEREUM")).toBeInTheDocument();
    expect(eth().queryByRole("button", { name: "Review transfer" })).toBeNull();
  });

  it("flags a hot or fuel wallet below its native floor", async () => {
    await renderOverview([
      tronWallets(),
      ethWallets({
        wallets: ethWallets().wallets.map((w) =>
          w.role === "FUEL" ? { ...w, belowNativeFloor: true } : w,
        ),
      }),
    ]);

    expect(eth().getByTestId("below-floor-ETHEREUM-FUEL")).toHaveTextContent(
      "Below the minimum ETH balance",
    );
    expect(eth().queryByTestId("below-floor-ETHEREUM-HOT")).toBeNull();
    expect(tron().queryByTestId("below-floor-TRON-HOT")).toBeNull();
  });

  it("warns on an address mismatch and refuses to offer the transfer", async () => {
    await renderOverview([
      tronWallets(),
      ethWallets({
        wallets: ethWallets().wallets.map((w) =>
          w.role === "HOT"
            ? { ...w, addressMismatch: true, configuredAddress: "0xCONFIGUREDADDRESS" }
            : w,
        ),
      }),
    ]);

    const warning = eth().getByTestId("address-mismatch-ETHEREUM-HOT");
    expect(warning).toHaveTextContent("0xCONFIGUREDADDRESS");
    expect(warning).toHaveTextContent(ETH_HOT);
    expect(eth().queryByRole("button", { name: "Review transfer" })).toBeNull();
    // TRON can still transfer.
    expect(tron().getByRole("button", { name: "Review transfer" })).toBeInTheDocument();
  });

  it("links recent hashes to the explorer of their own network", async () => {
    await renderOverview([
      tronWallets(),
      ethWallets({
        recentOutbound: [
          {
            id: "o9",
            transactionId: "t9",
            type: "BUYER_REFUND",
            status: "CONFIRMED",
            token: "USDT",
            amount: 5,
            toAddress: ETH_COLD,
            txHash: `0x${"d".repeat(64)}`,
            retryCount: 0,
            createdAt: "2026-10-04T10:00:00Z",
          },
        ],
      }),
    ]);

    const tronLinks = tron()
      .getAllByRole("link")
      .map((a) => a.getAttribute("href"));
    expect(tronLinks.every((h) => h?.includes("tronscan"))).toBe(true);
    const ethLink = eth()
      .getAllByRole("link")
      .find((a) => a.getAttribute("href")?.includes(`0x${"d".repeat(64)}`));
    expect(ethLink?.getAttribute("href")).toContain("etherscan.io/tx/");
  });
});

describe("ColdTransferForm — confirmation before money moves", () => {
  it("offers USDT only on Ethereum, USDT and USDC on TRON", async () => {
    await renderOverview();

    const ethOptions = within(eth().getByLabelText("Token")).getAllByRole("option");
    expect(ethOptions.map((o) => o.textContent)).toEqual(["USDT"]);
    const tronOptions = within(tron().getByLabelText("Token")).getAllByRole("option");
    expect(tronOptions.map((o) => o.textContent)).toEqual(["USDT", "USDC"]);
  });

  it("confirms first, then sends the network with the request", async () => {
    api.hotToColdTransfer.mockResolvedValue({
      coldTransferId: 5,
      txHash: `0x${"e".repeat(64)}`,
      amount: "25",
      token: "USDT",
      fromAddress: ETH_HOT,
      toAddress: ETH_COLD,
      network: "ETHEREUM",
    });
    await renderOverview();

    fireEvent.change(eth().getByLabelText("Amount"), { target: { value: "25" } });
    fireEvent.click(eth().getByRole("button", { name: "Review transfer" }));

    // Nothing sent yet; the dialog names network, token, amount and both addresses.
    expect(api.hotToColdTransfer).not.toHaveBeenCalled();
    const dialog = await screen.findByRole("dialog", { name: "Confirm hot → cold transfer" });
    expect(within(dialog).getByText("Ethereum (ERC-20)")).toBeInTheDocument();
    expect(within(dialog).getByText("25 USDT")).toBeInTheDocument();
    expect(within(dialog).getByText(new RegExp(ETH_HOT))).toBeInTheDocument();
    expect(within(dialog).getByText(new RegExp(ETH_COLD))).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Send 25 USDT" }));

    await waitFor(() =>
      expect(api.hotToColdTransfer).toHaveBeenCalledWith({
        amount: 25,
        token: "USDT",
        network: "ETHEREUM",
      }),
    );
    // The overview is refetched so the new balances show.
    await waitFor(() => expect(api.getAdminWallets).toHaveBeenCalledTimes(2));
    expect(await eth().findByTestId("cold-success-ETHEREUM")).toBeInTheDocument();
  });

  it("does not open the confirmation for an invalid amount", async () => {
    await renderOverview();

    fireEvent.change(tron().getByLabelText("Amount"), { target: { value: "0" } });
    fireEvent.click(tron().getByRole("button", { name: "Review transfer" }));

    expect(tron().getByText(/greater than zero/i)).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: "Confirm hot → cold transfer" })).toBeNull();
    expect(api.hotToColdTransfer).not.toHaveBeenCalled();
  });

  it("maps a backend error code to a localized message and keeps the dialog open", async () => {
    api.hotToColdTransfer.mockRejectedValue(
      new ApiError({ code: "TRANSFER_DEFERRED", message: "raw", details: null }, "trace", 503),
    );
    await renderOverview();

    fireEvent.change(tron().getByLabelText("Amount"), { target: { value: "10.5" } });
    fireEvent.click(tron().getByRole("button", { name: "Review transfer" }));
    const dialog = await screen.findByRole("dialog", { name: "Confirm hot → cold transfer" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Send 10.5 USDT" }));

    expect(await within(dialog).findByText(/transfer was deferred/i)).toBeInTheDocument();
    expect(within(dialog).queryByText("raw")).toBeNull();
    expect(api.hotToColdTransfer).toHaveBeenCalledWith({
      amount: 10.5,
      token: "USDT",
      network: "TRON",
    });
    // The addresses it would have used.
    expect(within(dialog).getByText(new RegExp(TRON_HOT))).toBeInTheDocument();
    expect(within(dialog).getByText(new RegExp(TRON_COLD))).toBeInTheDocument();
  });

  it("SIDECAR_UNAVAILABLE: may have been sent, check the explorer (T157 düzeltme turu)", async () => {
    // A timeout or an unreadable 200 reaches the screen as SIDECAR_UNAVAILABLE —
    // the service may have signed and broadcast the transfer (07 §9.34).
    api.hotToColdTransfer.mockRejectedValue(
      new ApiError({ code: "SIDECAR_UNAVAILABLE", message: "raw", details: null }, "trace", 502),
    );
    await renderOverview();

    fireEvent.change(eth().getByLabelText("Amount"), { target: { value: "10" } });
    fireEvent.click(eth().getByRole("button", { name: "Review transfer" }));
    const dialog = await screen.findByRole("dialog", { name: "Confirm hot → cold transfer" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Send 10 USDT" }));

    const message = await within(dialog).findByText(/may have been signed and sent/i);
    expect(message).toHaveTextContent(/before trying again, check/i);
    expect(within(dialog).queryByText(/nothing was sent/i)).toBeNull();
  });
});
