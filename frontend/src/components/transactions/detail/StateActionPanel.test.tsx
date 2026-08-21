import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import messages from "@/i18n/messages/en.json";
import { TransactionStatus } from "@/types/enums";
import type { TransactionDetailResponse } from "@/lib/api/transactions";
import { StateActionPanel } from "./StateActionPanel";

/**
 * T135 — S07 state × role action panel row behaviour (04 §7.3).
 *
 * The sibling `StateActionPanel.matrix.test.ts` proves every cell is CLASSIFIED;
 * this file proves the classified cells RENDER the thing the spec names — the
 * button, the link, the confirmation dialog, the asymmetry — and that the
 * server's `availableActions` flags, not local guesses, decide what is live.
 *
 * The real `en.json` is used on purpose rather than a hand-written stub: a key
 * this panel references but no locale carries would silently render as its own
 * dotted path, and asserting on the actual copy catches that.
 */

const { confirmReady, confirmReceipt, cancelTransaction } = vi.hoisted(() => ({
  confirmReady: vi.fn(),
  confirmReceipt: vi.fn(),
  cancelTransaction: vi.fn(),
}));

vi.mock("@/lib/api/transactions", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/transactions")>()),
  confirmReady,
  confirmReceipt,
  cancelTransaction,
}));

function baseDetail(overrides: Partial<TransactionDetailResponse>): TransactionDetailResponse {
  return {
    id: "11111111-1111-1111-1111-111111111111",
    status: TransactionStatus.CREATED,
    userRole: "seller",
    item: { name: "AK-47 | Redline", imageUrl: "https://cdn.example/ak.png" },
    price: "100.00",
    stablecoin: "USDT" as TransactionDetailResponse["stablecoin"],
    seller: { displayName: "Seller" },
    availableActions: { canAccept: false },
    ...overrides,
  };
}

function renderPanel(detail: TransactionDetailResponse, isSuspended = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale="en" messages={messages}>
        <StateActionPanel
          detail={detail}
          defaultRefundAddress={null}
          defaultSteamTradeUrl={null}
          isAuthenticated
          isSuspended={isSuspended}
          onRefetch={() => {}}
        />
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  confirmReady.mockReset();
  confirmReceipt.mockReset();
  cancelTransaction.mockReset();
});

afterEach(() => {
  vi.restoreAllMocks();
});

// ---------------------------------------------------------------------------
// ACCEPTED — "Göndermeye Hazırım" (03 §2.3, 07 §7.6a)
// ---------------------------------------------------------------------------

describe("ACCEPTED row", () => {
  const accepted = (role: "seller" | "buyer", canConfirmReady = true) =>
    baseDetail({
      status: TransactionStatus.ACCEPTED,
      userRole: role,
      availableActions: { canAccept: false, canConfirmReady, canCancel: true },
    });

  it("gives the seller the readiness button, not a 'platform is preparing' message", () => {
    renderPanel(accepted("seller"));

    expect(screen.getByTestId("confirm-ready-submit")).toBeEnabled();
    expect(screen.getByText("The buyer accepted. Are you ready to send the item?")).toBeVisible();
    // The v2 copy claimed the platform builds the trade offer. In v3.0 it never
    // does (02 §2.1) — the regression this row exists to close.
    expect(screen.queryByText(/platform is preparing/i)).toBeNull();
  });

  it("posts confirm-ready and refetches on success", async () => {
    confirmReady.mockResolvedValue({
      status: TransactionStatus.SELLER_CONFIRMED,
      sellerReadyConfirmedAt: "2026-08-21T10:00:00Z",
      paymentDeadline: "2026-08-22T10:00:00Z",
      buyerInventoryVisible: true,
    });
    const onRefetch = vi.fn();
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <NextIntlClientProvider locale="en" messages={messages}>
          <StateActionPanel
            detail={accepted("seller")}
            defaultRefundAddress={null}
            defaultSteamTradeUrl={null}
            isAuthenticated
            isSuspended={false}
            onRefetch={onRefetch}
          />
        </NextIntlClientProvider>
      </QueryClientProvider>,
    );

    fireEvent.click(screen.getByTestId("confirm-ready-submit"));

    await waitFor(() => expect(confirmReady).toHaveBeenCalledWith(accepted("seller").id));
    expect(onRefetch).toHaveBeenCalledTimes(1);
  });

  it("honours the server's canConfirmReady flag instead of re-deriving it", () => {
    renderPanel(accepted("seller", false));
    expect(screen.getByTestId("confirm-ready-submit")).toBeDisabled();
  });

  it("disables the button for a suspended session (04 §7.3 override)", () => {
    renderPanel(accepted("seller"), true);
    expect(screen.getByTestId("confirm-ready-submit")).toBeDisabled();
  });

  it("shows the buyer a waiting message and no readiness button", () => {
    renderPanel(accepted("buyer"));
    expect(screen.queryByTestId("confirm-ready-submit")).toBeNull();
    expect(
      screen.getByText("Waiting for the seller to confirm they are ready to send."),
    ).toBeVisible();
  });
});

// ---------------------------------------------------------------------------
// SELLER_CONFIRMED (03 §2.3 step 4)
// ---------------------------------------------------------------------------

describe("SELLER_CONFIRMED row", () => {
  it("tells the seller the payment is awaited — the branch that used to be missing", () => {
    renderPanel(
      baseDetail({
        status: TransactionStatus.SELLER_CONFIRMED,
        userRole: "seller",
        availableActions: { canAccept: false, canCancel: true },
      }),
    );
    expect(
      screen.getByText("You confirmed you are ready. Waiting for the buyer's payment."),
    ).toBeVisible();
  });

  it("points the buyer at the payment details PaymentInfoBlock renders", () => {
    renderPanel(
      baseDetail({
        status: TransactionStatus.SELLER_CONFIRMED,
        userRole: "buyer",
        availableActions: { canAccept: false, canCancel: true },
      }),
    );
    expect(screen.getByText(/The seller is ready\./)).toBeVisible();
  });
});

// ---------------------------------------------------------------------------
// PAYMENT_RECEIVED — the trade deep link and "Teslim Aldım" (03 §3.5)
// ---------------------------------------------------------------------------

describe("PAYMENT_RECEIVED row", () => {
  const TRADE_URL = "https://steamcommunity.com/tradeoffer/new/?partner=9988&token=xyz";

  const paymentReceived = (role: "seller" | "buyer", extra = {}) =>
    baseDetail({
      status: TransactionStatus.PAYMENT_RECEIVED,
      userRole: role,
      steamTradeOfferUrl: role === "seller" ? TRADE_URL : null,
      availableActions:
        role === "seller"
          ? { canAccept: false, canCancel: true }
          : { canAccept: false, canConfirmReceipt: true, canCancel: false },
      ...extra,
    });

  it("opens the buyer's trade URL for the seller, in a new tab", () => {
    renderPanel(paymentReceived("seller"));

    const cta = screen.getByTestId("seller-trade-cta");
    expect(cta).toHaveAttribute("href", TRADE_URL);
    expect(cta).toHaveAttribute("target", "_blank");
    // Opening a user-supplied URL with target=_blank without noopener hands the
    // opener reference to a page the counterparty controls.
    expect(cta).toHaveAttribute("rel", expect.stringContaining("noopener"));
  });

  it("repeats the item and the wrong-item warning next to the link", () => {
    renderPanel(paymentReceived("seller"));
    expect(screen.getByText("AK-47 | Redline")).toBeVisible();
    expect(screen.getByText(/Send only the item in this transaction/)).toBeVisible();
  });

  it("degrades to an explanation, not a dead link, when the trade URL is missing", () => {
    renderPanel(paymentReceived("seller", { steamTradeOfferUrl: null }));
    expect(screen.queryByTestId("seller-trade-cta")).toBeNull();
    expect(screen.getByTestId("seller-trade-cta-missing")).toBeVisible();
  });

  it("warns the seller inside the cancel modal that cancelling refunds the buyer (02 §7)", async () => {
    renderPanel(paymentReceived("seller"));

    fireEvent.click(screen.getByRole("button", { name: "Cancel transaction" }));

    expect(
      await screen.findByText(
        /If you cancel, the money is refunded to the buyer and your reputation is affected/,
      ),
    ).toBeVisible();
  });

  it("asks the buyer to confirm before sending an irreversible receipt", async () => {
    confirmReceipt.mockResolvedValue({
      status: TransactionStatus.ITEM_DELIVERED,
      deliveryVerifiedAt: "2026-08-21T10:00:00Z",
      evidence: ["BUYER_CONFIRMED"],
    });
    renderPanel(paymentReceived("buyer"));

    fireEvent.click(screen.getByTestId("confirm-receipt-open"));
    // 04 §7.3: the dialog must appear BEFORE anything is sent.
    expect(confirmReceipt).not.toHaveBeenCalled();
    expect(
      screen.getByText(/Once you confirm, the payment is released to the seller/),
    ).toBeVisible();

    fireEvent.click(screen.getByTestId("confirm-receipt-submit"));
    await waitFor(() => expect(confirmReceipt).toHaveBeenCalledTimes(1));
  });

  it("sends nothing when the buyer backs out of the dialog", async () => {
    renderPanel(paymentReceived("buyer"));

    fireEvent.click(screen.getByTestId("confirm-receipt-open"));
    fireEvent.click(screen.getByTestId("confirm-receipt-dismiss"));

    expect(confirmReceipt).not.toHaveBeenCalled();
  });

  it("states WHY the buyer cannot cancel instead of leaving a bare grey button", () => {
    renderPanel(paymentReceived("buyer"));

    expect(screen.getByRole("button", { name: "Cancel transaction" })).toBeDisabled();
    expect(screen.getByTestId("cancel-disabled-reason")).toHaveTextContent(
      "You cannot cancel — the payment has already been sent.",
    );
  });

  it("gives the seller no receipt button", () => {
    renderPanel(paymentReceived("seller"));
    expect(screen.queryByTestId("confirm-receipt-open")).toBeNull();
  });

  it("gives the buyer no trade link", () => {
    renderPanel(paymentReceived("buyer"));
    expect(screen.queryByTestId("seller-trade-cta")).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// buyerInventoryVisible — the standing obligation (07 §7.5, 02 §9.2)
// ---------------------------------------------------------------------------

describe("hidden-inventory notice", () => {
  it("warns both parties once the baseline could not be taken", () => {
    renderPanel(
      baseDetail({
        status: TransactionStatus.PAYMENT_RECEIVED,
        userRole: "seller",
        buyerInventoryVisible: false,
        steamTradeOfferUrl: "https://steamcommunity.com/tradeoffer/new/?partner=1&token=t",
        availableActions: { canAccept: false, canCancel: true },
      }),
    );
    expect(screen.getByTestId("inventory-hidden-seller")).toBeVisible();
    cleanup();

    renderPanel(
      baseDetail({
        status: TransactionStatus.PAYMENT_RECEIVED,
        userRole: "buyer",
        buyerInventoryVisible: false,
        availableActions: { canAccept: false, canConfirmReceipt: true, canCancel: false },
      }),
    );
    // The buyer is the one who then has to press the button, so their copy has
    // to say so (03 §3.5 note).
    expect(screen.getByTestId("inventory-hidden-buyer")).toHaveTextContent(/I received it/);
  });

  it("stays silent when the baseline was taken", () => {
    renderPanel(
      baseDetail({
        status: TransactionStatus.SELLER_CONFIRMED,
        userRole: "seller",
        buyerInventoryVisible: true,
        availableActions: { canAccept: false, canCancel: true },
      }),
    );
    expect(screen.queryByTestId("inventory-hidden-seller")).toBeNull();
  });

  it("stays silent while the answer is still unknown (field absent ≠ false)", () => {
    // Before the seller confirms readiness the buyer's inventory has never been
    // read. Treating `undefined` as `false` would warn about a hidden inventory
    // nobody has looked at.
    renderPanel(
      baseDetail({
        status: TransactionStatus.SELLER_CONFIRMED,
        userRole: "seller",
        availableActions: { canAccept: false, canCancel: true },
      }),
    );
    expect(screen.queryByTestId("inventory-hidden-seller")).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// ITEM_DELIVERED — the settlement window (02 §4.5.1)
// ---------------------------------------------------------------------------

describe("ITEM_DELIVERED row", () => {
  const settlementTimeout = {
    type: "settlement",
    expiresAt: "2026-09-01T12:00:00Z",
    remainingSeconds: 8 * 86400,
    warningThresholdPercent: 75,
    frozen: false,
  };

  const delivered = (role: "seller" | "buyer") =>
    baseDetail({
      status: TransactionStatus.ITEM_DELIVERED,
      userRole: role,
      timeout: settlementTimeout,
      availableActions: { canAccept: false, canDispute: true },
    });

  it("tells the seller the payout date and why the wait exists", () => {
    renderPanel(delivered("seller"));

    expect(screen.getByTestId("settlement-notice-seller")).toHaveTextContent(
      /Your payout will be sent on/,
    );
    expect(screen.getByText(/Steam keeps trades reversible for 7 days/)).toBeVisible();
  });

  it("gives the buyer a guarantee and NO countdown (04 §7.3)", () => {
    renderPanel(delivered("buyer"));

    expect(screen.getByTestId("settlement-notice-buyer")).toHaveTextContent(
      /your payment is refunded/,
    );
    // A ticking clock in the buyer's view reads as a deadline of their own.
    expect(screen.queryByRole("timer")).toBeNull();
  });

  it("does not draw the generic frame countdown on top of the settlement one", () => {
    renderPanel(delivered("seller"));
    // Exactly one timer: the labelled settlement countdown.
    expect(screen.getAllByRole("timer")).toHaveLength(1);
    expect(screen.queryByText("Remaining")).toBeNull();
  });

  it("drops the dated sentence when no settlement window is armed", () => {
    renderPanel(
      baseDetail({
        status: TransactionStatus.ITEM_DELIVERED,
        userRole: "seller",
        availableActions: { canAccept: false },
      }),
    );
    expect(screen.getByTestId("settlement-notice-seller")).toHaveTextContent(
      "Item delivered. Your payout is scheduled.",
    );
  });
});

// ---------------------------------------------------------------------------
// REFUNDED — the row that used to fall through (07 §7.5)
// ---------------------------------------------------------------------------

describe("REFUNDED row", () => {
  it("renders no action area and, above all, no dead buttons", () => {
    const { container } = renderPanel(
      baseDetail({
        status: TransactionStatus.REFUNDED,
        userRole: "buyer",
        // What the server actually sends for a refunded transaction: the flags
        // are present and false, which is what used to make the FE draw two
        // permanently disabled buttons.
        availableActions: { canAccept: false, canCancel: false, canDispute: false },
      }),
    );

    expect(container).toBeEmptyDOMElement();
    expect(screen.queryByRole("button")).toBeNull();
  });
});

// ---------------------------------------------------------------------------
// Rows that were already correct — kept as regression cover
// ---------------------------------------------------------------------------

describe("unchanged rows", () => {
  it("freezes every action under EMERGENCY_HOLD", () => {
    renderPanel(
      baseDetail({
        status: "EMERGENCY_HOLD",
        userRole: "seller",
        availableActions: { canAccept: false, canCancel: false, canDispute: false },
      }),
    );
    expect(
      screen.getByText("Actions are disabled while this transaction is under review."),
    ).toBeVisible();
    expect(screen.queryByRole("button")).toBeNull();
  });

  it("offers a public visitor the login CTA on CREATED only", () => {
    renderPanel(
      baseDetail({
        status: TransactionStatus.CREATED,
        userRole: null,
        availableActions: { canAccept: false, requiresLogin: true },
      }),
    );
    expect(screen.getByRole("link", { name: "Sign in and accept" })).toBeVisible();
    cleanup();

    const { container } = renderPanel(
      baseDetail({
        status: TransactionStatus.PAYMENT_RECEIVED,
        userRole: null,
        availableActions: { canAccept: false, requiresLogin: true },
      }),
    );
    expect(container).toBeEmptyDOMElement();
  });
});
