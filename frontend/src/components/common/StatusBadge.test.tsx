import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { StatusBadge } from "@/components/common/StatusBadge";
import { TransactionStatus } from "@/types/enums";

// Minimal messages for the one status this test renders.
const messages = {
  status: {
    [TransactionStatus.COMPLETED]: "Completed",
  },
};

describe("StatusBadge", () => {
  it("renders the localized status label (jsdom + RTL + jest-dom + next-intl provider)", () => {
    render(
      <NextIntlClientProvider locale="en" messages={messages}>
        <StatusBadge status={TransactionStatus.COMPLETED} />
      </NextIntlClientProvider>,
    );

    const badge = screen.getByText("Completed");
    expect(badge).toBeInTheDocument();
    expect(badge.tagName).toBe("SPAN");
  });
});
