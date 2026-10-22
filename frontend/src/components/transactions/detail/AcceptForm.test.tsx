import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import messages from "@/i18n/messages/en.json";
import { BlockchainNetwork } from "@/types/enums";
import type { UserWalletEntry } from "@/lib/api/users";
import { AcceptForm } from "./AcceptForm";

/**
 * T156 — the accept form's network choice (03 §3.2 step 4, 02 §4.2/§12.2, K8).
 */

const { acceptTransaction } = vi.hoisted(() => ({ acceptTransaction: vi.fn() }));
vi.mock("@/lib/api/transactions", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/transactions")>()),
  acceptTransaction,
}));

const TRON_ADDR = "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH";
const ETH_ADDR = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";
const TRADE_URL = "https://steamcommunity.com/tradeoffer/new/?partner=1&token=abc";

// 02 §5 worked example: 20 USDT sale, 2% commission — TRON 23.30, Ethereum 20.40.
const QUOTES = [
  { network: "TRON", networkFeeAmount: "2.90", totalAmount: "23.30" },
  { network: "ETHEREUM", networkFeeAmount: "0.00", totalAmount: "20.40" },
];

function renderForm(
  props: Partial<{
    acceptedNetworks: string[] | null;
    profileWallets: UserWalletEntry[];
    networkQuotes: typeof QUOTES;
  }> = {},
) {
  return render(
    <NextIntlClientProvider locale="en" messages={messages}>
      <AcceptForm
        transactionId="tx-1"
        acceptedNetworks={
          props.acceptedNetworks === undefined ? ["TRON", "ETHEREUM"] : props.acceptedNetworks
        }
        networkQuotes={props.networkQuotes ?? QUOTES}
        stablecoin="USDT"
        profileWallets={props.profileWallets ?? []}
        defaultSteamTradeUrl={TRADE_URL}
        onAccepted={vi.fn()}
      />
    </NextIntlClientProvider>,
  );
}

beforeEach(() => {
  acceptTransaction.mockReset();
  acceptTransaction.mockResolvedValue({ status: "ACCEPTED", acceptedAt: "2026-10-18T10:00:00Z" });
});
afterEach(cleanup);

describe("AcceptForm — network choice (T156)", () => {
  it("with several networks, nothing is preselected and accept stays disabled", () => {
    renderForm();
    expect(screen.getByTestId("accept-network-TRON")).not.toBeChecked();
    expect(screen.getByTestId("accept-network-ETHEREUM")).not.toBeChecked();
    expect(screen.getByTestId("accept-submit")).toBeDisabled();
  });

  it("with a single network it is preselected", () => {
    renderForm({ acceptedNetworks: ["TRON"] });
    expect(screen.getByTestId("accept-network-TRON")).toBeChecked();
    expect(screen.queryByTestId("accept-network-ETHEREUM")).toBeNull();
  });

  it("shows each network's total verbatim; only TRON names a network fee part (K8)", () => {
    renderForm();
    const tron = screen.getByTestId("accept-quote-TRON");
    expect(tron).toHaveTextContent("You send: 23.30 USDT");
    expect(tron).toHaveTextContent("includes TRON network fee: 2.90 USDT");
    const eth = screen.getByTestId("accept-quote-ETHEREUM");
    expect(eth).toHaveTextContent("You send: 20.40 USDT");
    expect(eth).not.toHaveTextContent("network fee");
  });

  it("sends the chosen network with the refund address of that network", async () => {
    renderForm({
      profileWallets: [
        { network: BlockchainNetwork.ETHEREUM, type: "REFUND", address: ETH_ADDR, changedAt: null },
      ],
    });
    fireEvent.click(screen.getByTestId("accept-network-ETHEREUM"));
    const input = screen.getByTestId("accept-refund-input");
    expect(input).toHaveValue(ETH_ADDR);
    expect(input).toBeDisabled(); // K4 — profile address, per-tx override disabled
    fireEvent.click(screen.getByTestId("accept-submit"));
    await waitFor(() =>
      expect(acceptTransaction).toHaveBeenCalledWith("tx-1", {
        refundWalletAddress: ETH_ADDR,
        steamTradeUrl: TRADE_URL,
        network: BlockchainNetwork.ETHEREUM,
      }),
    );
  });

  it("the profile's TRON refund address is NOT used for an Ethereum choice", () => {
    renderForm({
      profileWallets: [
        { network: BlockchainNetwork.TRON, type: "REFUND", address: TRON_ADDR, changedAt: null },
      ],
    });
    fireEvent.click(screen.getByTestId("accept-network-ETHEREUM"));
    const input = screen.getByTestId("accept-refund-input");
    expect(input).toHaveValue("");
    expect(input).toBeEnabled();
    expect(input).toHaveAttribute("placeholder", "0x…");
  });

  it("rejects a TRON address typed for Ethereum before calling the server", async () => {
    renderForm();
    fireEvent.click(screen.getByTestId("accept-network-ETHEREUM"));
    fireEvent.change(screen.getByTestId("accept-refund-input"), { target: { value: TRON_ADDR } });
    fireEvent.click(screen.getByTestId("accept-submit"));
    expect(await screen.findByText("Enter a valid Ethereum (ERC-20) address.")).toBeVisible();
    expect(acceptTransaction).not.toHaveBeenCalled();
  });

  it("an Ethereum address without checksum needs the explicit confirmation (02 §12.3)", async () => {
    renderForm();
    fireEvent.click(screen.getByTestId("accept-network-ETHEREUM"));
    fireEvent.change(screen.getByTestId("accept-refund-input"), {
      target: { value: ETH_ADDR.toLowerCase() },
    });
    fireEvent.click(screen.getByTestId("accept-submit"));
    expect(
      await screen.findByText("Confirm that you checked the address without a checksum."),
    ).toBeVisible();
    expect(acceptTransaction).not.toHaveBeenCalled();

    fireEvent.click(screen.getByTestId("accept-checksum-confirm"));
    fireEvent.click(screen.getByTestId("accept-submit"));
    await waitFor(() =>
      expect(acceptTransaction).toHaveBeenCalledWith(
        "tx-1",
        expect.objectContaining({
          refundWalletAddress: ETH_ADDR.toLowerCase(),
          network: BlockchainNetwork.ETHEREUM,
          confirmLowercase: true,
        }),
      ),
    );
  });

  it("a checksummed Ethereum address sends no confirmLowercase", async () => {
    renderForm();
    fireEvent.click(screen.getByTestId("accept-network-ETHEREUM"));
    fireEvent.change(screen.getByTestId("accept-refund-input"), { target: { value: ETH_ADDR } });
    fireEvent.click(screen.getByTestId("accept-submit"));
    await waitFor(() => expect(acceptTransaction).toHaveBeenCalled());
    expect(acceptTransaction.mock.calls[0][1]).not.toHaveProperty("confirmLowercase");
  });

  it("a TRON address sends no confirmLowercase", async () => {
    renderForm();
    fireEvent.click(screen.getByTestId("accept-network-TRON"));
    fireEvent.change(screen.getByTestId("accept-refund-input"), { target: { value: TRON_ADDR } });
    fireEvent.click(screen.getByTestId("accept-submit"));
    await waitFor(() => expect(acceptTransaction).toHaveBeenCalled());
    expect(acceptTransaction.mock.calls[0][1]).not.toHaveProperty("confirmLowercase");
  });

  it("a server 400 CHECKSUM_CONFIRMATION_REQUIRED brings up the confirmation box", async () => {
    const { ApiError } = await import("@/lib/api/client");
    acceptTransaction.mockRejectedValueOnce(
      new ApiError(
        {
          code: "INVALID_WALLET_ADDRESS",
          message: "x",
          details: { network: "ETHEREUM", reason: "CHECKSUM_CONFIRMATION_REQUIRED" },
        } as never,
        "trace",
        400,
      ),
    );
    renderForm();
    fireEvent.click(screen.getByTestId("accept-network-ETHEREUM"));
    // A checksummed address needs no box locally, so only the server's answer
    // can bring it up.
    fireEvent.change(screen.getByTestId("accept-refund-input"), { target: { value: ETH_ADDR } });
    fireEvent.click(screen.getByTestId("accept-submit"));
    expect(
      await screen.findByText("Confirm that you checked the address without a checksum."),
    ).toBeVisible();
    fireEvent.click(await screen.findByTestId("accept-checksum-confirm"));
    fireEvent.click(screen.getByTestId("accept-submit"));
    await waitFor(() => expect(acceptTransaction).toHaveBeenCalledTimes(2));
    expect(acceptTransaction.mock.calls[1][1]).toMatchObject({ confirmLowercase: true });
  });

  it("switching network clears an address typed for the previous one", () => {
    renderForm();
    fireEvent.click(screen.getByTestId("accept-network-TRON"));
    fireEvent.change(screen.getByTestId("accept-refund-input"), { target: { value: TRON_ADDR } });
    fireEvent.click(screen.getByTestId("accept-network-ETHEREUM"));
    expect(screen.getByTestId("accept-refund-input")).toHaveValue("");
  });

  it("names the network in a NETWORK_UNAVAILABLE rejection", async () => {
    const { ApiError } = await import("@/lib/api/client");
    acceptTransaction.mockRejectedValue(
      new ApiError({ code: "NETWORK_UNAVAILABLE", message: "x", details: null }, "trace", 503),
    );
    renderForm({ acceptedNetworks: ["TRON"] });
    fireEvent.change(screen.getByTestId("accept-refund-input"), { target: { value: TRON_ADDR } });
    fireEvent.click(screen.getByTestId("accept-submit"));
    expect(
      await screen.findByText(
        "TRON (TRC-20) is temporarily unavailable. Choose another network or try again later.",
      ),
    ).toBeVisible();
  });
});

describe("AcceptForm — a network below its own minimum (T169)", () => {
  // 20 USDT sale: TRON is below its 50 USDT minimum (owner decision 2026-10-21).
  const BELOW = [
    {
      network: "TRON",
      networkFeeAmount: "2.90",
      totalAmount: "23.30",
      unavailableReason: "NETWORK_MIN_AMOUNT_NOT_MET",
      minAmount: "50.00",
    },
    { network: "ETHEREUM", networkFeeAmount: "0.00", totalAmount: "20.40" },
  ];

  it("shows TRON disabled with the reason and preselects the only choosable network", () => {
    renderForm({ networkQuotes: BELOW });
    expect(screen.getByTestId("accept-network-TRON")).toBeDisabled();
    expect(screen.getByTestId("accept-below-min-TRON")).toHaveTextContent("at least 50.00 USDT");
    expect(screen.queryByTestId("accept-quote-TRON")).toBeNull();
    expect(screen.getByTestId("accept-network-ETHEREUM")).toBeChecked();
  });
});
