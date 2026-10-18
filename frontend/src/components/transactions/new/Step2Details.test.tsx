import { describe, it, expect, afterEach, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import en from "@/i18n/messages/en.json";
import { BlockchainNetwork, StablecoinType } from "@/types/enums";
import type { TransactionParamsResponse } from "@/lib/api/transactions";
import { Step2Details } from "./Step2Details";

/**
 * T144 — the commission preview must be the commission the backend will carry:
 * truncated to the cent with integer math. A revert to float math (10.25 x 0.02
 * = 0.20500000000000002, 10.55 x 0.02 = 0.21100000000000002) or to rounding
 * would change these strings.
 */

const params: TransactionParamsResponse = {
  minPrice: "1.00",
  maxPrice: "10000.00",
  commissionRate: 0.02,
  paymentTimeout: { minHours: 1, maxHours: 72, defaultHours: 24 },
  openLinkEnabled: true,
  supportedStablecoins: [StablecoinType.USDT, StablecoinType.USDC],
  supportedNetworks: [BlockchainNetwork.TRON],
};

function renderStep(price: string) {
  return render(
    <NextIntlClientProvider locale="en" messages={en}>
      <Step2Details
        item={{ assetId: "1", name: "AK-47 | Redline", tradeable: true }}
        params={params}
        stablecoin={StablecoinType.USDT}
        price={price}
        paymentTimeoutHours={24}
        priceError={null}
        networkOptions={[
          {
            network: BlockchainNetwork.TRON,
            payoutAddress: "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH",
            selectable: true,
          },
        ]}
        acceptedNetworks={[BlockchainNetwork.TRON]}
        onToggleNetwork={vi.fn()}
        onChangeItem={vi.fn()}
        onChangeStablecoin={vi.fn()}
        onChangePrice={vi.fn()}
        onChangeTimeout={vi.fn()}
      />
    </NextIntlClientProvider>,
  );
}

afterEach(cleanup);

describe("Step2Details commission preview (T144)", () => {
  it("10.25 -> commission 0.20 (truncated, not rounded up)", () => {
    renderStep("10.25");
    expect(screen.getByText("Buyer pays 2% commission: 0.20 USDT")).toBeTruthy();
  });

  it("10.55 -> commission 0.21", () => {
    renderStep("10.55");
    expect(screen.getByText("Buyer pays 2% commission: 0.21 USDT")).toBeTruthy();
  });
});
