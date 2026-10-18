import { describe, it, expect, afterEach, vi } from "vitest";
import { cleanup, render } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import en from "@/i18n/messages/en.json";
import { BuyerIdentificationMethod, StablecoinType } from "@/types/enums";
import { Step4Summary } from "./Step4Summary";

/**
 * T144 — the summary shows the commission the transaction will carry
 * (integer-cent truncation). A revert to float math would change these.
 */

function renderStep(price: string) {
  return render(
    <NextIntlClientProvider locale="en" messages={en}>
      <Step4Summary
        item={{ assetId: "1", name: "AK-47 | Redline", tradeable: true }}
        stablecoin={StablecoinType.USDT}
        price={price}
        commissionRate={0.02}
        paymentTimeoutHours={24}
        buyerMethod={BuyerIdentificationMethod.OPEN_LINK}
        buyerSteamId=""
        sellerWalletAddress="TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH"
        submitError={null}
        isSubmitting={false}
        onBack={vi.fn()}
        onSubmit={vi.fn()}
      />
    </NextIntlClientProvider>,
  );
}

afterEach(cleanup);

describe("Step4Summary commission (T144)", () => {
  it("10.25 -> commission 0.20 (truncated, not rounded up)", () => {
    const { container } = renderStep("10.25");
    expect(container.textContent).toContain("0.20 USDT");
    expect(container.textContent).not.toContain("0.21 USDT");
  });

  it("10.55 -> commission 0.21", () => {
    const { container } = renderStep("10.55");
    expect(container.textContent).toContain("0.21 USDT");
  });
});
