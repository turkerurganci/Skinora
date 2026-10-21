import { describe, it, expect, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import messages from "@/i18n/messages/en.json";
import { BlockchainNetwork, StablecoinType, TransactionStatus } from "@/types/enums";
import type { TransactionDetailPayment, TransactionDetailResponse } from "@/lib/api/transactions";
import { PaymentInfoBlock } from "./PaymentInfoBlock";
import { TransactionInfoPanel } from "./TransactionInfoPanel";
import { TxHashLink } from "./TxHashLink";

/**
 * T156 — how the chosen network shows on S07 (04 §7.3, 02 §5, K8, K9).
 * Amounts are the API strings; the tests feed values a client-side sum would
 * not reproduce (e.g. a total that is not price + commission + fee) to prove
 * nothing is recomputed.
 */

function wrap(node: React.ReactNode) {
  return render(
    <NextIntlClientProvider locale="en" messages={messages}>
      {node}
    </NextIntlClientProvider>,
  );
}

afterEach(cleanup);

const payment = (network: string, address: string): TransactionDetailPayment => ({
  address,
  expectedAmount: "23.30",
  stablecoin: StablecoinType.USDT,
  network,
});

describe("PaymentInfoBlock", () => {
  it("TRON: explicit label, the TRON fee as its own line, amount verbatim, network warning, no exchange warning", () => {
    wrap(
      <PaymentInfoBlock
        payment={payment("TRON", "TPaymentAddr1234567890abcdef1234")}
        network={BlockchainNetwork.TRON}
        price="20.00"
        commissionAmount="0.40"
        networkFeeAmount="2.90"
      />,
    );
    expect(screen.getByTestId("payment-network")).toHaveTextContent("TRON (TRC-20)");
    expect(screen.getByTestId("payment-amount")).toHaveTextContent("23.30 USDT");
    expect(screen.getByTestId("payment-network-fee")).toHaveTextContent(
      "TRON network fee: 2.90 USDT",
    );
    expect(screen.getByTestId("payment-network-warning")).toHaveTextContent(
      "Only send USDT on the TRON (TRC-20) network.",
    );
    expect(screen.queryByTestId("payment-evm-warning")).toBeNull();
    // K9 — the old "do not send from an exchange" ban is gone from the
    // warnings. T167 — paying from an exchange is the expected path, so the
    // block now explains what a Turkish exchange does instead (MASAK).
    expect(screen.getByRole("note", { name: "Important" }).textContent).not.toMatch(/exchange/i);
    expect(screen.getByTestId("masak-buyer-full")).toBeInTheDocument();
  });

  it("Ethereum: no fee line even if one were sent; the same-0x-address warning is shown", () => {
    wrap(
      <PaymentInfoBlock
        payment={{
          ...payment("ETHEREUM", "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed"),
          expectedAmount: "20.40",
        }}
        network={BlockchainNetwork.ETHEREUM}
        price="20.00"
        commissionAmount="0.40"
        networkFeeAmount="9.99"
      />,
    );
    expect(screen.getByTestId("payment-network")).toHaveTextContent("Ethereum (ERC-20)");
    expect(screen.getByTestId("payment-amount")).toHaveTextContent("20.40 USDT");
    expect(screen.queryByTestId("payment-network-fee")).toBeNull();
    expect(screen.getByTestId("payment-address")).toHaveTextContent(
      "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed",
    );
    expect(screen.getByTestId("payment-evm-warning")).toHaveTextContent("other EVM networks");
  });

  it("an unknown network withholds the address instead of guessing", () => {
    wrap(<PaymentInfoBlock payment={payment("BSC", "0xabc")} network={null} price="20.00" />);
    expect(screen.queryByTestId("payment-address")).toBeNull();
    expect(screen.getByRole("alert")).toHaveTextContent("Do not send anything");
  });
});

function detail(overrides: Partial<TransactionDetailResponse>): TransactionDetailResponse {
  return {
    id: "tx",
    status: TransactionStatus.ACCEPTED,
    userRole: "buyer",
    item: { name: "AK-47 | Redline" },
    price: "20.00",
    stablecoin: StablecoinType.USDT,
    commissionAmount: "0.40",
    totalAmount: "23.30",
    seller: { displayName: "S" },
    availableActions: { canAccept: false },
    ...overrides,
  };
}

describe("TransactionInfoPanel", () => {
  it("accepted on TRON: network label + TRON fee line + server total", () => {
    wrap(<TransactionInfoPanel detail={detail({ network: "TRON", networkFeeAmount: "2.90" })} />);
    expect(screen.getByTestId("info-network")).toHaveTextContent("TRON (TRC-20)");
    expect(screen.getByTestId("info-network-fee")).toHaveTextContent("2.90 USDT");
    expect(document.body.textContent).toContain("23.30 USDT");
  });

  it("accepted on Ethereum: no fee line whatever the response carries", () => {
    wrap(
      <TransactionInfoPanel
        detail={detail({ network: "ETHEREUM", networkFeeAmount: "1.00", totalAmount: "20.40" })}
      />,
    );
    expect(screen.getByTestId("info-network")).toHaveTextContent("Ethereum (ERC-20)");
    expect(screen.queryByTestId("info-network-fee")).toBeNull();
  });

  it("before acceptance: the accepted networks, and the total says the TRON fee is extra", () => {
    wrap(
      <TransactionInfoPanel
        detail={detail({
          status: TransactionStatus.CREATED,
          totalAmount: "20.40",
          acceptedNetworks: ["TRON", "ETHEREUM"],
        })}
      />,
    );
    expect(screen.getByTestId("info-accepted-networks")).toHaveTextContent(
      "TRON (TRC-20), Ethereum (ERC-20)",
    );
    expect(screen.getByText("Total (excl. TRON network fee)")).toBeVisible();
    expect(screen.queryByTestId("info-network")).toBeNull();
  });
});

describe("TxHashLink", () => {
  it("links to the explorer of the transaction's network", () => {
    wrap(
      <>
        <TxHashLink txHash="0xabcdef0123456789" network={BlockchainNetwork.ETHEREUM} />
        <TxHashLink txHash="tronhash0123456789" network={BlockchainNetwork.TRON} />
      </>,
    );
    const links = screen.getAllByRole("link");
    expect(links[0]).toHaveAttribute("href", "https://etherscan.io/tx/0xabcdef0123456789");
    expect(links[0]).toHaveAttribute("title", "View on Etherscan");
    expect(links[1]).toHaveAttribute(
      "href",
      "https://tronscan.org/#/transaction/tronhash0123456789",
    );
  });
});
