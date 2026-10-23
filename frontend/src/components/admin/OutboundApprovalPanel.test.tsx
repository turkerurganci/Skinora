import { describe, it, expect, vi, afterEach } from "vitest";
import { cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { ApiError } from "@/lib/api/client";
import type { DepositRefundHeldRow, OutboundApprovalRow } from "@/lib/api/admin";
import { OutboundApprovalPanel } from "./OutboundApprovalPanel";
import { TRON_HOT, renderWithProviders } from "./walletTestUtils";

/**
 * T169 — the admin approval of payouts and refunds while the launch gate
 * `outbound.auto_send_enabled` is closed (07 §9.35 AD40/AD41). ⚠ PARA: the
 * dialog must show what leaves (network, type, amount, recipient) and nothing
 * is sent before the admin confirms.
 */

const api = vi.hoisted(() => ({
  listOutboundApprovals: vi.fn(),
  approveOutboundTransfer: vi.fn(),
  releaseDepositRefundLimit: vi.fn(),
}));
vi.mock("@/lib/api/admin", () => api);

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

const BUYER = "TBuyerRefundAddress00000000000000001";

function row(overrides: Partial<OutboundApprovalRow> = {}): OutboundApprovalRow {
  return {
    blockchainTransactionId: "bt-1",
    transactionId: "tx-1",
    network: "TRON",
    type: "SELLER_PAYOUT",
    token: "USDT",
    chainToken: "USDT",
    amount: 98.5,
    gasFee: 2.25,
    toAddress: TRON_HOT,
    retryCount: 0,
    createdAt: "2026-10-22T10:00:00Z",
    ...overrides,
  };
}

describe("OutboundApprovalPanel", () => {
  it("says the gate is closed and lists the waiting rows", async () => {
    api.listOutboundApprovals.mockResolvedValue({
      autoSendEnabled: false,
      rows: [
        row(),
        row({ blockchainTransactionId: "bt-2", type: "BUYER_REFUND", toAddress: BUYER }),
      ],
    });
    renderWithProviders(<OutboundApprovalPanel />);

    expect(await screen.findByTestId("approval-gate-state")).toHaveTextContent(
      "Automatic sending is OFF",
    );
    const table = screen.getByRole("table", { name: "Outbound rows waiting for approval" });
    expect(within(table).getByText("Seller payout")).toBeInTheDocument();
    expect(within(table).getByText("Buyer refund")).toBeInTheDocument();
    expect(within(table).getByText(BUYER)).toBeInTheDocument();
    expect(within(table).getAllByText("Network fee: 2.25").length).toBe(2);
  });

  it("shows the empty state and the open gate", async () => {
    api.listOutboundApprovals.mockResolvedValue({ autoSendEnabled: true, rows: [] });
    renderWithProviders(<OutboundApprovalPanel />);

    expect(await screen.findByTestId("approval-gate-state")).toHaveTextContent(
      "Automatic sending is on",
    );
    expect(screen.getByText("No payout or refund is waiting for approval.")).toBeInTheDocument();
  });

  it("confirms first, then approves the row it showed", async () => {
    api.listOutboundApprovals.mockResolvedValue({ autoSendEnabled: false, rows: [row()] });
    api.approveOutboundTransfer.mockResolvedValue({
      blockchainTransactionId: "bt-1",
      network: "TRON",
      type: "SELLER_PAYOUT",
      amount: 98.5,
      toAddress: TRON_HOT,
      approvedAt: "2026-10-22T10:05:00Z",
      approvedByAdminId: "admin-me",
    });
    renderWithProviders(<OutboundApprovalPanel />);

    fireEvent.click(await screen.findByTestId("approve-bt-1"));
    const dialog = await screen.findByRole("dialog", { name: "Approve payment" });
    expect(within(dialog).getByRole("alert")).toHaveTextContent("cannot be undone");
    expect(within(dialog).getByText("98.5 USDT")).toBeInTheDocument();
    expect(within(dialog).getByText(TRON_HOT)).toBeInTheDocument();
    expect(api.approveOutboundTransfer).not.toHaveBeenCalled();

    fireEvent.click(within(dialog).getByTestId("approval-confirm"));
    await waitFor(() => expect(api.approveOutboundTransfer).toHaveBeenCalledTimes(1));
    expect(api.approveOutboundTransfer.mock.calls[0][0]).toBe("bt-1");
  });

  it("maps a backend error code to a message and keeps the dialog open", async () => {
    api.listOutboundApprovals.mockResolvedValue({ autoSendEnabled: false, rows: [row()] });
    api.approveOutboundTransfer.mockRejectedValue(
      new ApiError({ code: "ALREADY_APPROVED", message: "raw", details: null }, "trace", 409),
    );
    renderWithProviders(<OutboundApprovalPanel />);

    fireEvent.click(await screen.findByTestId("approve-bt-1"));
    const dialog = await screen.findByRole("dialog", { name: "Approve payment" });
    fireEvent.click(within(dialog).getByTestId("approval-confirm"));

    expect(await within(dialog).findByText("This row is already approved.")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  // ---- T169a — deposit refund daily limit (⚠ PARA) ----

  function heldRow(overrides: Partial<DepositRefundHeldRow> = {}): DepositRefundHeldRow {
    return {
      blockchainTransactionId: "bt-held",
      transactionId: "tx-held",
      network: "TRON",
      type: "BUYER_REFUND",
      chainToken: "USDT",
      amount: 137.25,
      gasFee: 2.8,
      toAddress: BUYER,
      heldAt: "2026-10-22T11:00:00Z",
      approvedAt: "2026-10-22T10:30:00Z",
      createdAt: "2026-10-22T10:00:00Z",
      ...overrides,
    };
  }

  it("shows each network's last 24 hours against its limit and the held refunds", async () => {
    api.listOutboundApprovals.mockResolvedValue({
      autoSendEnabled: false,
      rows: [],
      depositRefundLimits: [
        { network: "TRON", limitUsdt: 100, usedLast24hUsdt: 60 },
        { network: "ETHEREUM", limitUsdt: 100, usedLast24hUsdt: 0 },
      ],
      limitHeldRows: [heldRow()],
    });
    renderWithProviders(<OutboundApprovalPanel />);

    expect(await screen.findByTestId("limit-usage-TRON")).toHaveTextContent(
      "60 / 100 USDT refunded from deposits in the last 24 hours",
    );
    expect(screen.getByTestId("limit-usage-ETHEREUM")).toHaveTextContent("0 / 100 USDT");
    const table = screen.getByRole("table", { name: "Refunds held by the daily refund limit" });
    expect(within(table).getByText("Buyer refund")).toBeInTheDocument();
    expect(within(table).getByText("137.25 USDT")).toBeInTheDocument();
  });

  it("releases a held refund only after the warning is confirmed", async () => {
    api.listOutboundApprovals.mockResolvedValue({
      autoSendEnabled: true,
      rows: [],
      depositRefundLimits: [{ network: "TRON", limitUsdt: 100, usedLast24hUsdt: 60 }],
      limitHeldRows: [heldRow()],
    });
    api.releaseDepositRefundLimit.mockResolvedValue({
      blockchainTransactionId: "bt-held",
      network: "TRON",
      type: "BUYER_REFUND",
      amount: 137.25,
      toAddress: BUYER,
      limitUsdt: 100,
      usedLast24hUsdt: 60,
      releasedAt: "2026-10-22T11:05:00Z",
      releasedByAdminId: "admin-me",
    });
    renderWithProviders(<OutboundApprovalPanel />);

    fireEvent.click(await screen.findByTestId("limit-release-bt-held"));
    const dialog = await screen.findByRole("dialog", { name: "Send the refund past the limit" });
    expect(within(dialog).getByRole("alert")).toHaveTextContent("cannot be undone");
    expect(within(dialog).getByText(BUYER)).toBeInTheDocument();
    expect(api.releaseDepositRefundLimit).not.toHaveBeenCalled();

    fireEvent.click(within(dialog).getByTestId("limit-release-confirm"));
    await waitFor(() => expect(api.releaseDepositRefundLimit).toHaveBeenCalledTimes(1));
    expect(api.releaseDepositRefundLimit.mock.calls[0][0]).toBe("bt-held");
  });

  it("maps a release refusal to its message", async () => {
    api.listOutboundApprovals.mockResolvedValue({
      autoSendEnabled: true,
      rows: [],
      depositRefundLimits: [],
      limitHeldRows: [heldRow()],
    });
    api.releaseDepositRefundLimit.mockRejectedValue(
      new ApiError({ code: "ROW_NOT_HELD", message: "raw", details: null }, "trace", 409),
    );
    renderWithProviders(<OutboundApprovalPanel />);

    fireEvent.click(await screen.findByTestId("limit-release-bt-held"));
    const dialog = await screen.findByRole("dialog", { name: "Send the refund past the limit" });
    fireEvent.click(within(dialog).getByTestId("limit-release-confirm"));

    expect(
      await within(dialog).findByText(
        "This refund is not held by the daily refund limit; there is nothing to release.",
      ),
    ).toBeInTheDocument();
  });

  it("says when nothing is held", async () => {
    api.listOutboundApprovals.mockResolvedValue({ autoSendEnabled: true, rows: [] });
    renderWithProviders(<OutboundApprovalPanel />);

    expect(await screen.findByText("No refund is held by the limit.")).toBeInTheDocument();
  });
});
