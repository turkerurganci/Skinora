import { describe, it, expect, afterEach, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import en from "@/i18n/messages/en.json";
import tr from "@/i18n/messages/tr.json";
import es from "@/i18n/messages/es.json";
import zh from "@/i18n/messages/zh.json";
import { Step1ItemSelection } from "./Step1ItemSelection";
import { POST_ERROR_CODES } from "./NewTransactionForm";

/**
 * P2P-InventoryUnauthorizedMapping — Steam's 401 means the account has no CS2
 * inventory at all (measured 2026-10-02). It reaches the seller as 422
 * INVENTORY_NOT_FOUND and must read as its own, permanent condition: neither
 * the generic "try again" nor the private-profile instruction is true for it.
 *
 * Real locale files on purpose: a key the step references but a catalogue
 * lacks renders as its own dotted path, which these assertions would catch.
 */

const LOCALES = [
  { locale: "en", messages: en },
  { locale: "tr", messages: tr },
  { locale: "es", messages: es },
  { locale: "zh", messages: zh },
] as const;

function renderStep(
  errorCode: string | null,
  locale: (typeof LOCALES)[number]["locale"] = "en",
  messages: object = en,
) {
  return render(
    <NextIntlClientProvider locale={locale} messages={messages}>
      <Step1ItemSelection
        inventory={undefined}
        totalCount={undefined}
        tradeableCount={undefined}
        isLoading={false}
        isError
        errorCode={errorCode}
        selectedAssetId={null}
        onSelect={vi.fn()}
        onRetry={vi.fn()}
      />
    </NextIntlClientProvider>,
  );
}

afterEach(cleanup);

describe("Step1ItemSelection — inventory read failures", () => {
  it("names the missing CS2 inventory and offers no retry", () => {
    renderStep("INVENTORY_NOT_FOUND");

    expect(screen.getByText(en.newTransaction.step1.error.notFoundTitle)).toBeInTheDocument();
    expect(screen.getByText(en.newTransaction.step1.error.notFoundMessage)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: en.common.retry })).toBeNull();
  });

  it("keeps the retry button for a Steam outage — the only failure retrying fixes", () => {
    renderStep("STEAM_UNAVAILABLE");

    expect(screen.getByText(en.newTransaction.step1.error.message)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: en.common.retry })).toBeInTheDocument();
  });

  it.each(LOCALES)("carries the no-inventory copy in $locale", ({ locale, messages }) => {
    renderStep("INVENTORY_NOT_FOUND", locale, messages);

    const error = messages.newTransaction.step1.error;
    expect(screen.getByText(error.notFoundTitle)).toBeInTheDocument();
    expect(screen.getByText(error.notFoundMessage)).toBeInTheDocument();
  });

  it.each(LOCALES)(
    "has a step-4 and a confirm-ready message for INVENTORY_NOT_FOUND in $locale",
    ({ messages }) => {
      expect(POST_ERROR_CODES.has("INVENTORY_NOT_FOUND")).toBe(true);
      expect(messages.newTransaction.step4.errors.INVENTORY_NOT_FOUND).toBeTruthy();
      expect(
        messages.transactionDetail.actions.accepted.seller.errors.INVENTORY_NOT_FOUND,
      ).toBeTruthy();
    },
  );
});
