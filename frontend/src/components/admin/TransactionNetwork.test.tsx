import { describe, it, expect, afterEach } from "vitest";
import { cleanup, screen, within } from "@testing-library/react";
import type { AdminTransactionDetail, AdminTransactionListItem } from "@/lib/api/admin";
import { BlockchainNetwork, StablecoinType, TransactionStatus } from "@/types/enums";
import { TransactionDetailView } from "./TransactionDetailView";
import { TransactionListTable } from "./TransactionListTable";
import { renderWithProviders } from "./walletTestUtils";

/**
 * T157 — admin transaction screens know the network. The list shows it (a dash
 * before the buyer accepts), and the detail links every hash to the explorer of
 * ITS network: an Ethereum hash opened on Tronscan is a dead link that looks
 * like a missing payment.
 */

afterEach(cleanup);

const party = { steamId: "7656", displayName: "Seller", avatarUrl: null };

function listItem(id: string, network: AdminTransactionListItem["network"]) {
  const item: AdminTransactionListItem = {
    id,
    itemName: `Item ${id}`,
    itemImageUrl: null,
    price: 10,
    stablecoin: StablecoinType.USDT,
    network,
    status: TransactionStatus.PAYMENT_RECEIVED,
    seller: party,
    buyer: null,
    createdAt: "2026-10-01T10:00:00Z",
    completedAt: null,
    cancelledAt: null,
  };
  return item;
}

describe("TransactionListTable — network column", () => {
  it("shows the explicit network label per row and a dash when none is chosen yet", () => {
    renderWithProviders(
      <TransactionListTable
        transactions={[
          listItem("aaaaaaaa-1", BlockchainNetwork.TRON),
          listItem("bbbbbbbb-2", BlockchainNetwork.ETHEREUM),
          listItem("cccccccc-3", null),
        ]}
      />,
    );

    expect(screen.getAllByRole("columnheader", { name: "Network" })).toHaveLength(1);
    const table = screen.getByRole("table");
    const rows = within(table).getAllByRole("row").slice(1);
    expect(within(rows[0]).getByText("TRON (TRC-20)")).toBeInTheDocument();
    expect(within(rows[1]).getByText("Ethereum (ERC-20)")).toBeInTheDocument();
    expect(within(rows[2]).queryByText(/TRON|Ethereum/)).toBeNull();
    expect(within(rows[2]).getAllByText("—").length).toBeGreaterThan(0);
  });
});

const PAY_HASH = "pay-hash-1111111111111111111111111111111111111111111111111111111";
const PAYOUT_HASH = "payout-hash-2222222222222222222222222222222222222222222222222222222";

function detail(network: AdminTransactionDetail["network"]): AdminTransactionDetail {
  return {
    id: "tx-detail-1",
    status: TransactionStatus.COMPLETED,
    itemName: "AK-47",
    itemImageUrl: null,
    itemExterior: null,
    itemInspectLink: null,
    price: 10,
    stablecoin: StablecoinType.USDT,
    network,
    commissionRate: 0.02,
    commissionAmount: 0.2,
    totalAmount: 10.2,
    paymentTimeoutMinutes: 60,
    seller: party,
    buyer: null,
    createdAt: "2026-10-01T10:00:00Z",
    acceptedAt: null,
    paymentReceivedAt: null,
    itemDeliveredAt: null,
    completedAt: null,
    cancelledAt: null,
    cancelReason: null,
    isOnHold: false,
    emergencyHoldAt: null,
    emergencyHoldReason: null,
    statusHistory: [],
    paymentDetail: {
      paymentAddress: null,
      receivedAmount: 10.2,
      receivedTxHash: PAY_HASH,
      blockConfirmations: 20,
      confirmedAt: null,
    },
    sellerPayoutDetail: {
      grossAmount: 10,
      commission: 0.2,
      gasFee: null,
      gasFeeFromCommission: 0,
      gasFeeFromSeller: 0,
      netAmount: 9.8,
      txHash: PAYOUT_HASH,
      sentAt: null,
    },
    refundDetail: null,
    notificationHistory: [],
    disputeHistory: [],
    flagHistory: [],
    adminActions: { canApproveFlag: false, canRejectFlag: false, canCancel: false },
  };
}

function hashLink(hash: string) {
  return screen.getAllByRole("link").find((a) => a.textContent === hash) as HTMLAnchorElement;
}

describe("TransactionDetailView — network-aware links", () => {
  it("links an Ethereum payment and payout hash to Etherscan and names it in the title", () => {
    renderWithProviders(
      <TransactionDetailView
        transaction={detail(BlockchainNetwork.ETHEREUM)}
        onRefetch={() => {}}
      />,
    );

    for (const hash of [PAY_HASH, PAYOUT_HASH]) {
      const link = hashLink(hash);
      expect(link.href).toBe(`https://etherscan.io/tx/${hash}`);
      expect(link.title).toBe("View on Etherscan");
    }
    expect(screen.getByText("Ethereum (ERC-20)")).toBeInTheDocument();
  });

  it("links a TRON hash to Tronscan", () => {
    renderWithProviders(
      <TransactionDetailView transaction={detail(BlockchainNetwork.TRON)} onRefetch={() => {}} />,
    );

    const link = hashLink(PAY_HASH);
    expect(link.href).toContain("tronscan.org");
    expect(link.title).toBe("View on Tronscan");
    expect(screen.getByText("TRON (TRC-20)")).toBeInTheDocument();
  });

  it("reads a null network as TRON for the link but does not invent a network label", () => {
    renderWithProviders(<TransactionDetailView transaction={detail(null)} onRefetch={() => {}} />);

    expect(hashLink(PAY_HASH).href).toContain("tronscan.org");
    expect(screen.queryByText("TRON (TRC-20)")).toBeNull();
  });
});
