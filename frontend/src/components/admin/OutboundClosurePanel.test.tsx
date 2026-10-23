import { describe, it, expect, vi, afterEach } from "vitest";
import { cleanup, fireEvent, screen, waitFor, within } from "@testing-library/react";
import { ApiError } from "@/lib/api/client";
import { OutboundClosurePanel } from "./OutboundClosurePanel";
import { BlockchainNetwork } from "@/types/enums";
import { HASH_A, HASH_B, candidate, closure, renderWithProviders } from "./walletTestUtils";

/**
 * T157 — the two-person closure tool. What must hold: the proposer is never
 * offered Approve, the approver cannot submit until the hash they TYPED equals
 * the proposal (and that typed value is what is sent), and the proposal form
 * rejects a malformed hash or a thin reason before calling the server.
 */

const api = vi.hoisted(() => ({
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

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function setup({
  candidates = [],
  closures = [],
  network,
}: {
  candidates?: ReturnType<typeof candidate>[];
  closures?: ReturnType<typeof closure>[];
  network?: BlockchainNetwork;
} = {}) {
  api.listOutboundClosureCandidates.mockResolvedValue(candidates);
  api.listOutboundClosures.mockResolvedValue(closures);
  renderWithProviders(
    network ? <OutboundClosurePanel network={network} /> : <OutboundClosurePanel />,
  );
}

// T172a — a TRON transaction id: 64 hex, no 0x.
const TRON_ID = "c".repeat(64);
const TRON_SELLER = "TNPeeaaFB7K9cmo4uQpcU32zGK8G1NYqeL";

describe("OutboundClosurePanel — TRON and the restore case (T172a)", () => {
  it("asks for its own network's rows and names the network", async () => {
    setup({ network: BlockchainNetwork.TRON });

    expect(await screen.findByText("No open outbound rows on TRON.")).toBeInTheDocument();
    expect(api.listOutboundClosureCandidates).toHaveBeenCalledWith("TRON");
    expect(api.listOutboundClosures).toHaveBeenCalledWith("PROPOSED", "TRON");
    expect(
      screen.getByRole("heading", { name: "Outbound row closure on TRON (two-person)" }),
    ).toBeInTheDocument();
  });

  it("takes a TRON id (no 0x), refuses an Ethereum-shaped hash, and links Tronscan", async () => {
    api.proposeOutboundClosure.mockResolvedValue(closure({ network: "TRON", txHash: TRON_ID }));
    setup({
      network: BlockchainNetwork.TRON,
      candidates: [candidate({ network: "TRON", fromAddress: "", toAddress: TRON_SELLER })],
    });

    fireEvent.click(await screen.findByRole("button", { name: "Propose closure" }));
    const dialog = await screen.findByRole("dialog", { name: "Propose closure" });
    expect(within(dialog).getByText(/64 hexadecimal characters, no 0x/)).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText("On-chain transaction hash"), {
      target: { value: HASH_A },
    });
    const reason = "Hot wallet sent this payout after the backup; Tronscan shows it.";
    fireEvent.change(within(dialog).getByLabelText("Reason"), { target: { value: reason } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Propose closure" }));
    expect(within(dialog).getByText(/Enter a valid TRON transaction id/)).toBeInTheDocument();
    expect(api.proposeOutboundClosure).not.toHaveBeenCalled();

    fireEvent.change(within(dialog).getByLabelText("On-chain transaction hash"), {
      target: { value: TRON_ID },
    });
    expect(within(dialog).getByRole("link")).toHaveAttribute(
      "href",
      expect.stringContaining(`tronscan.org/#/transaction/${TRON_ID}`),
    );
    fireEvent.click(within(dialog).getByRole("button", { name: "Propose closure" }));
    await waitFor(() =>
      expect(api.proposeOutboundClosure).toHaveBeenCalledWith({
        blockchainTransactionId: "row-1",
        txHash: TRON_ID,
        reason,
      }),
    );
  });

  it("marks a row closed by hand after a restore (FAILED POST_RESTORE_DUPLICATE)", async () => {
    setup({
      network: BlockchainNetwork.TRON,
      candidates: [
        candidate({ network: "TRON", rowStatus: "FAILED", toAddress: TRON_SELLER }),
        candidate({ blockchainTransactionId: "row-2", network: "TRON", toAddress: TRON_SELLER }),
      ],
    });

    expect(await screen.findByTestId("restored-failed-row-1")).toHaveTextContent(
      /POST_RESTORE_DUPLICATE/,
    );
    expect(screen.queryByTestId("restored-failed-row-2")).toBeNull();

    fireEvent.click(screen.getAllByRole("button", { name: "Propose closure" })[0]);
    const dialog = await screen.findByRole("dialog", { name: "Propose closure" });
    expect(within(dialog).getByTestId("restored-failed-note")).toHaveTextContent(
      /same row back to the real payment/,
    );
  });

  it("approving a TRON closure links its id on Tronscan", async () => {
    setup({
      network: BlockchainNetwork.TRON,
      closures: [closure({ network: "TRON", txHash: TRON_ID, toAddress: TRON_SELLER })],
    });

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));
    const dialog = await screen.findByRole("dialog", { name: "Approve closure" });
    expect(within(dialog).getByRole("link")).toHaveAttribute(
      "href",
      expect.stringContaining(`tronscan.org/#/transaction/${TRON_ID}`),
    );
  });
});

describe("OutboundClosurePanel — propose", () => {
  it("lists open rows and marks one that already has a live closure", async () => {
    setup({
      candidates: [
        candidate(),
        candidate({ blockchainTransactionId: "row-2", hasLiveClosure: true }),
      ],
    });

    expect(await screen.findAllByRole("button", { name: "Propose closure" })).toHaveLength(1);
    expect(screen.getByText("Closure in progress")).toBeInTheDocument();
  });

  it("states that no money is sent and validates hash format and reason length", async () => {
    api.proposeOutboundClosure.mockResolvedValue(closure());
    setup({ candidates: [candidate()] });

    fireEvent.click(await screen.findByRole("button", { name: "Propose closure" }));
    const dialog = await screen.findByRole("dialog", { name: "Propose closure" });
    expect(within(dialog).getByRole("alert")).toHaveTextContent(/sends no money/i);
    expect(within(dialog).getByRole("alert")).toHaveTextContent(/already paid on-chain/i);

    // Bad hash + short reason: nothing is sent, both errors show.
    fireEvent.change(within(dialog).getByLabelText("On-chain transaction hash"), {
      target: { value: "0x1234" },
    });
    fireEvent.change(within(dialog).getByLabelText("Reason"), { target: { value: "too short" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Propose closure" }));
    expect(within(dialog).getByText(/Enter a valid transaction hash/)).toBeInTheDocument();
    expect(within(dialog).getByText(/at least 20 characters\.$/)).toBeInTheDocument();
    expect(api.proposeOutboundClosure).not.toHaveBeenCalled();
    expect(within(dialog).queryByRole("link")).toBeNull();

    // A well-formed hash shows an Etherscan link for it, and the form submits.
    fireEvent.change(within(dialog).getByLabelText("On-chain transaction hash"), {
      target: { value: HASH_A },
    });
    expect(within(dialog).getByRole("link")).toHaveAttribute(
      "href",
      expect.stringContaining(`etherscan.io/tx/${HASH_A}`),
    );
    const reason = "Nonce 12 of the hot account; amount and recipient match.";
    fireEvent.change(within(dialog).getByLabelText("Reason"), { target: { value: reason } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Propose closure" }));

    await waitFor(() =>
      expect(api.proposeOutboundClosure).toHaveBeenCalledWith({
        blockchainTransactionId: "row-1",
        txHash: HASH_A,
        reason,
      }),
    );
  });

  it("shows a localized message for a backend error code", async () => {
    api.proposeOutboundClosure.mockRejectedValue(
      new ApiError({ code: "HASH_ALREADY_BOUND", message: "raw", details: null }, "t", 409),
    );
    setup({ candidates: [candidate()] });

    fireEvent.click(await screen.findByRole("button", { name: "Propose closure" }));
    const dialog = await screen.findByRole("dialog", { name: "Propose closure" });
    fireEvent.change(within(dialog).getByLabelText("On-chain transaction hash"), {
      target: { value: HASH_A },
    });
    fireEvent.change(within(dialog).getByLabelText("Reason"), {
      target: { value: "x".repeat(25) },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Propose closure" }));

    expect(
      await within(dialog).findByText("That hash is already bound to another row."),
    ).toBeInTheDocument();
  });
});

describe("OutboundClosurePanel — the token a row moves on chain (T157 düzeltme turu)", () => {
  it("shows the token a wrong-token refund returns, not the one the transaction expected", async () => {
    setup({
      candidates: [candidate({ type: "WRONG_TOKEN_REFUND", token: "USDT", chainToken: "USDC" })],
      closures: [closure({ rowType: "WRONG_TOKEN_REFUND", token: "USDT", chainToken: "USDC" })],
    });

    const propose = await screen.findByRole("button", { name: "Propose closure" });
    const row = propose.closest("tr")!;
    expect(within(row).getByText("42.5 USDC")).toBeInTheDocument();
    expect(within(row).queryByText("42.5 USDT")).toBeNull();
    expect(within(row).getByTestId("chain-token-note")).toHaveTextContent(
      "The transaction expected USDT; this row moves USDC on chain",
    );

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));
    const dialog = await screen.findByRole("dialog", { name: "Approve closure" });
    expect(within(dialog).getByText("42.5 USDC")).toBeInTheDocument();
    expect(within(dialog).getByTestId("chain-token-note")).toHaveTextContent("moves USDC");
  });

  it("says so when the token cannot be resolved, and shows no note when it is the expected one", async () => {
    setup({
      candidates: [
        candidate({ type: "WRONG_TOKEN_REFUND", chainToken: null }),
        candidate({ blockchainTransactionId: "row-2" }),
      ],
    });

    await screen.findAllByRole("button", { name: "Propose closure" });
    const notes = screen.getAllByTestId("chain-token-note");
    expect(notes).toHaveLength(1);
    expect(notes[0]).toHaveTextContent(/cannot be determined/i);
    expect(screen.getByText("42.5 ?")).toBeInTheDocument();
  });

  it("maps AMBIGUOUS_ROW_MATCH and ROW_TRANSFER_UNRESOLVED to the manual path", async () => {
    api.proposeOutboundClosure
      .mockRejectedValueOnce(
        new ApiError({ code: "AMBIGUOUS_ROW_MATCH", message: "raw", details: null }, "t", 409),
      )
      .mockRejectedValueOnce(
        new ApiError({ code: "ROW_TRANSFER_UNRESOLVED", message: "raw", details: null }, "t", 422),
      );
    setup({ candidates: [candidate()] });

    fireEvent.click(await screen.findByRole("button", { name: "Propose closure" }));
    const dialog = await screen.findByRole("dialog", { name: "Propose closure" });
    fireEvent.change(within(dialog).getByLabelText("On-chain transaction hash"), {
      target: { value: HASH_A },
    });
    fireEvent.change(within(dialog).getByLabelText("Reason"), {
      target: { value: "x".repeat(25) },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Propose closure" }));
    expect(
      await within(dialog).findByText(/same recipient, token and amount/i),
    ).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Propose closure" }));
    expect(await within(dialog).findByText(/cannot be determined/i)).toBeInTheDocument();
    expect(within(dialog).queryByText("raw")).toBeNull();
  });
});

describe("OutboundClosurePanel — approve", () => {
  it("never offers Approve on the current admin's own proposal", async () => {
    setup({
      closures: [
        closure({ id: "mine", proposedByAdminId: "admin-me", proposedByName: "Me" }),
        closure({ id: "theirs", proposedByAdminId: "admin-other" }),
      ],
    });

    const mine = await screen.findByTestId("closure-mine");
    const theirs = screen.getByTestId("closure-theirs");
    await waitFor(() =>
      expect(within(theirs).getByRole("button", { name: "Approve" })).toBeInTheDocument(),
    );
    expect(within(mine).queryByRole("button", { name: "Approve" })).toBeNull();
    expect(within(mine).getByRole("button", { name: "Withdraw" })).toBeInTheDocument();
    expect(within(mine).getByText(/different admin must approve/i)).toBeInTheDocument();
    // The default filter is PROPOSED.
    expect(api.listOutboundClosures).toHaveBeenCalledWith("PROPOSED", "ETHEREUM");
  });

  it("refuses to submit until the typed hash equals the proposal, then sends the typed hash", async () => {
    api.approveOutboundClosure.mockResolvedValue(closure({ status: "APPROVED" }));
    setup({ closures: [closure()] });

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));
    const dialog = await screen.findByRole("dialog", { name: "Approve closure" });
    const submit = within(dialog).getByRole("button", { name: "Approve closure" });
    const input = within(dialog).getByLabelText("Type the transaction hash again");

    // The row's recipient/amount are shown for comparison; the hash is NOT prefilled.
    expect(within(dialog).getByText("42.5 USDT")).toBeInTheDocument();
    expect(within(dialog).getByText(/irreversible in effect/i)).toBeInTheDocument();
    expect(input).toHaveValue("");
    expect(submit).toBeDisabled();

    fireEvent.change(input, { target: { value: HASH_B } });
    expect(within(dialog).getByText(/does not match the proposal/i)).toBeInTheDocument();
    expect(submit).toBeDisabled();
    fireEvent.submit(input.closest("form")!);
    expect(api.approveOutboundClosure).not.toHaveBeenCalled();

    fireEvent.change(input, { target: { value: HASH_A } });
    expect(submit).toBeEnabled();
    fireEvent.click(submit);

    await waitFor(() => expect(api.approveOutboundClosure).toHaveBeenCalledWith("c1", HASH_A));
  });

  it("maps SELF_APPROVAL_FORBIDDEN and HASH_NOT_FINAL to localized messages", async () => {
    api.approveOutboundClosure
      .mockRejectedValueOnce(
        new ApiError({ code: "HASH_NOT_FINAL", message: "raw", details: null }, "t", 409),
      )
      .mockRejectedValueOnce(
        new ApiError({ code: "SELF_APPROVAL_FORBIDDEN", message: "raw", details: null }, "t", 403),
      );
    setup({ closures: [closure()] });

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));
    const dialog = await screen.findByRole("dialog", { name: "Approve closure" });
    fireEvent.change(within(dialog).getByLabelText("Type the transaction hash again"), {
      target: { value: HASH_A },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Approve closure" }));
    expect(await within(dialog).findByText(/not final yet/i)).toBeInTheDocument();

    fireEvent.click(within(dialog).getByRole("button", { name: "Approve closure" }));
    expect(
      await within(dialog).findByText(/cannot approve your own proposal/i),
    ).toBeInTheDocument();
  });

  it("maps HASH_DOES_NOT_MATCH_ROW and explains the system check", async () => {
    api.approveOutboundClosure.mockRejectedValue(
      new ApiError({ code: "HASH_DOES_NOT_MATCH_ROW", message: "raw", details: null }, "t", 422),
    );
    setup({ closures: [closure()] });

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));
    const dialog = await screen.findByRole("dialog", { name: "Approve closure" });
    expect(within(dialog).getByText(/still compare it yourself on the explorer/i)).toBeVisible();
    fireEvent.change(within(dialog).getByLabelText("Type the transaction hash again"), {
      target: { value: HASH_A },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Approve closure" }));

    expect(
      await within(dialog).findByText(/does not pay this row \(wrong recipient/i),
    ).toBeInTheDocument();
    expect(within(dialog).queryByText("raw")).toBeNull();
  });
});

describe("OutboundClosurePanel — reject and decided closures", () => {
  it("requires a note to reject", async () => {
    api.rejectOutboundClosure.mockResolvedValue(closure({ status: "REJECTED" }));
    setup({ closures: [closure()] });

    fireEvent.click(await screen.findByRole("button", { name: "Reject" }));
    const dialog = await screen.findByRole("dialog", { name: "Reject closure" });
    fireEvent.click(within(dialog).getByRole("button", { name: "Reject closure" }));
    expect(within(dialog).getByText("A note is required.")).toBeInTheDocument();
    expect(api.rejectOutboundClosure).not.toHaveBeenCalled();

    fireEvent.change(within(dialog).getByLabelText(/^Note/), {
      target: { value: "Hash belongs to a different payment." },
    });
    fireEvent.click(within(dialog).getByRole("button", { name: "Reject closure" }));
    await waitFor(() =>
      expect(api.rejectOutboundClosure).toHaveBeenCalledWith(
        "c1",
        "Hash belongs to a different payment.",
      ),
    );
  });

  it("shows decided closures read-only", async () => {
    setup({
      closures: [
        closure({
          id: "done",
          status: "APPROVED",
          decidedByAdminId: "admin-other",
          decidedByName: "Carol Checker",
          decidedAt: "2026-10-07T10:00:00Z",
        }),
      ],
    });

    const card = await screen.findByTestId("closure-done");
    expect(within(card).getByText("Approved")).toBeInTheDocument();
    expect(within(card).getByText(/Decided by Carol Checker/)).toBeInTheDocument();
    expect(within(card).queryByRole("button")).toBeNull();
  });
});
