import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { TransactionTimeline } from "@/components/common/TransactionTimeline";
import { TransactionStatus } from "@/types/enums";

// Mirrors the shape of the real `timeline` namespace (labels shortened to the
// step key so assertions read clearly).
const messages = {
  timeline: {
    ariaLabel: "Transaction flow",
    step: {
      CREATED: "Created",
      ACCEPTED: "Accepted",
      ITEM_ESCROWED: "Item escrowed",
      PAYMENT_RECEIVED: "Payment",
      PAYMENT_VERIFIED: "Payment verified",
      ITEM_DELIVERED: "Delivered",
      DELIVERY_VERIFIED: "Delivery verified",
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

const STEP_COUNT = 8;

/** Step markers only — the connector lines share the green/gray colours, so a
 *  bare `.bg-green-500` query would also match them. */
function markers(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>("span.rounded-full"));
}

function countWithClass(container: HTMLElement, className: string): number {
  return markers(container).filter((m) => m.classList.contains(className)).length;
}

describe("TransactionTimeline", () => {
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
    // PAYMENT_RECEIVED maps to index 4 → four earlier markers green, three later pending.
    expect(countWithClass(container, "bg-green-500")).toBe(4);
    expect(countWithClass(container, "bg-gray-200")).toBe(3);
    expect(screen.getByText("Completed")).toBeInTheDocument();
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
});
