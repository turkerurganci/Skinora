import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { TransactionTimeline, TIMELINE_STEPS } from "@/components/common/TransactionTimeline";
import { TransactionStatus } from "@/types/enums";

// Mirrors the shape of the real `timeline` namespace (labels shortened to the
// step key so assertions read clearly).
const messages = {
  timeline: {
    ariaLabel: "Transaction flow",
    step: {
      CREATED: "Created",
      ACCEPTED: "Accepted",
      SELLER_CONFIRMED: "Seller ready",
      PAYMENT_RECEIVED: "Payment received",
      ITEM_DELIVERED: "Delivered",
      COMPLETED: "Completed",
    },
  },
};

function renderTimeline(props: React.ComponentProps<typeof TransactionTimeline>) {
  return render(
    <NextIntlClientProvider locale="en" messages={messages}>
      <TransactionTimeline {...props} />
    </NextIntlClientProvider>,
  );
}

// 04 §C05 (v3.0) — six steps: "Item Emanet" and the two verification steps are
// gone. The count is asserted so a step silently reappearing fails here.
const STEP_COUNT = 6;

/** Step markers only — the connector lines share the green/gray colours, so a
 *  bare `.bg-green-500` query would also match them. */
function markers(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>("span.rounded-full"));
}

function countWithClass(container: HTMLElement, className: string): number {
  return markers(container).filter((m) => m.classList.contains(className)).length;
}

describe("TransactionTimeline", () => {
  it("renders exactly the six v3.0 steps (04 §C05)", () => {
    const { container } = renderTimeline({ status: TransactionStatus.CREATED });

    expect(markers(container)).toHaveLength(STEP_COUNT);
    for (const label of Object.values(messages.timeline.step)) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it("marks every step done — including the last — when the transaction is COMPLETED", () => {
    const { container } = renderTimeline({ status: TransactionStatus.COMPLETED });

    // 04 §C05: finished steps are green. A COMPLETED transaction has no step
    // left "in progress", so no marker may carry the active (blue/pulsing)
    // treatment or aria-current.
    expect(markers(container)).toHaveLength(STEP_COUNT);
    expect(countWithClass(container, "bg-green-500")).toBe(STEP_COUNT);
    expect(countWithClass(container, "bg-blue-500")).toBe(0);
    expect(countWithClass(container, "bg-gray-200")).toBe(0);
    expect(container.querySelector('[aria-current="step"]')).toBeNull();
  });

  it("highlights exactly one active step mid-flow and leaves later steps pending", () => {
    const { container } = renderTimeline({ status: TransactionStatus.PAYMENT_RECEIVED });

    const active = container.querySelectorAll('[aria-current="step"]');
    expect(active).toHaveLength(1);
    expect(active[0]).toHaveClass("bg-blue-500");
    // PAYMENT_RECEIVED is step 4 of 6 → three earlier markers green, two later pending.
    expect(countWithClass(container, "bg-green-500")).toBe(3);
    expect(countWithClass(container, "bg-gray-200")).toBe(2);
    expect(screen.getByText("Completed")).toBeInTheDocument();
  });

  it("puts SELLER_CONFIRMED on the timeline as step 3 (v3.0 — replaces ITEM_ESCROWED)", () => {
    const { container } = renderTimeline({ status: TransactionStatus.SELLER_CONFIRMED });

    const active = container.querySelectorAll('[aria-current="step"]');
    expect(active).toHaveLength(1);
    expect(countWithClass(container, "bg-green-500")).toBe(2);
    expect(countWithClass(container, "bg-gray-200")).toBe(3);
  });

  it("renders a cancelled transaction with the red terminal marker, not a pulsing step", () => {
    const { container } = renderTimeline({ status: TransactionStatus.CANCELLED_BUYER });

    expect(countWithClass(container, "bg-red-500")).toBe(1);
    expect(countWithClass(container, "bg-blue-500")).toBe(0);
  });

  it("treats REFUNDED as terminal (WP5 dispute unwind), not as step 1 in progress", () => {
    const { container } = renderTimeline({ status: TransactionStatus.REFUNDED });

    expect(countWithClass(container, "bg-red-500")).toBe(1);
    expect(countWithClass(container, "bg-blue-500")).toBe(0);
  });

  it("renders the flagged transaction with the orange marker", () => {
    const { container } = renderTimeline({ status: TransactionStatus.FLAGGED });

    expect(countWithClass(container, "bg-orange-500")).toBe(1);
    expect(countWithClass(container, "bg-blue-500")).toBe(0);
  });

  // ---------- WP2c: red X lands on the step the flow stopped at ----------

  it("puts the red marker on the step the flow stopped at, not on step 1", () => {
    // The defect this closes: indexForStatus returns -1 for every off-timeline
    // status, so the clamp put the X on step 1 and every cancellation looked
    // like it died at creation.
    const { container } = renderTimeline({
      status: TransactionStatus.CANCELLED_SELLER,
      stoppedAtStatus: TransactionStatus.PAYMENT_RECEIVED,
    });

    const red = markers(container).findIndex((m) => m.classList.contains("bg-red-500"));
    expect(red).toBe(TIMELINE_STEPS.indexOf(TransactionStatus.PAYMENT_RECEIVED));
    // Everything before it stays green — the flow really did get that far.
    expect(countWithClass(container, "bg-green-500")).toBe(red);
  });

  it("falls back to step 1 when no stop status is known", () => {
    // A record with no history row (pre-WP2c data) keeps the old rendering.
    const { container } = renderTimeline({ status: TransactionStatus.CANCELLED_BUYER });

    const red = markers(container).findIndex((m) => m.classList.contains("bg-red-500"));
    expect(red).toBe(0);
  });

  it("falls back to step 1 when the stop status is itself off-timeline", () => {
    // FLAGGED -> CANCELLED_ADMIN reports FLAGGED as the previous status, and
    // FLAGGED has no timeline position of its own.
    const { container } = renderTimeline({
      status: TransactionStatus.CANCELLED_ADMIN,
      stoppedAtStatus: TransactionStatus.FLAGGED,
    });

    const red = markers(container).findIndex((m) => m.classList.contains("bg-red-500"));
    expect(red).toBe(0);
  });

  it("positions a REFUNDED transaction the same way", () => {
    const { container } = renderTimeline({
      status: TransactionStatus.REFUNDED,
      stoppedAtStatus: TransactionStatus.ITEM_DELIVERED,
    });

    const red = markers(container).findIndex((m) => m.classList.contains("bg-red-500"));
    expect(red).toBe(TIMELINE_STEPS.indexOf(TransactionStatus.ITEM_DELIVERED));
  });

  it("ignores the stop status for a live transaction", () => {
    // Only the off-timeline terminal states consult it; a running flow keeps
    // marking its own status.
    const { container } = renderTimeline({
      status: TransactionStatus.ACCEPTED,
      stoppedAtStatus: TransactionStatus.ITEM_DELIVERED,
    });

    const blue = markers(container).findIndex((m) => m.classList.contains("bg-blue-500"));
    expect(blue).toBe(TIMELINE_STEPS.indexOf(TransactionStatus.ACCEPTED));
  });
});
