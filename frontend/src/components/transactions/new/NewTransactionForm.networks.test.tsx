import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import en from "@/i18n/messages/en.json";
import tr from "@/i18n/messages/tr.json";
import es from "@/i18n/messages/es.json";
import zh from "@/i18n/messages/zh.json";
import { BlockchainNetwork, BuyerIdentificationMethod, StablecoinType } from "@/types/enums";
import type { EligibilityResponse, TransactionParamsResponse } from "@/lib/api/transactions";
import type { UserWalletEntry } from "@/lib/api/users";
import { WIZARD_DRAFT_STORAGE_KEY } from "@/lib/transactions/wizardDraft";
import { NewTransactionForm } from "./NewTransactionForm";

/**
 * T156 — the wizard's network rules end to end through the real form
 * (02 §4.2, 03 §2.2 step 9, 07 §7.2/§7.4, 11 T156 + T147 validation):
 *   - the choice is filtered by `supportedNetworks` (a closed network is not
 *     offered even when the seller has a payout address on it);
 *   - an eligible seller with no payout address on any OPEN network is told why;
 *   - the POST body carries the networks the seller kept.
 */

const { createTransaction, profileState, searchParamsState } = vi.hoisted(() => ({
  createTransaction: vi.fn(),
  profileState: { wallets: [] as UserWalletEntry[] },
  searchParamsState: { value: "step=2" },
}));

vi.mock("@/lib/api/transactions", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/transactions")>()),
  createTransaction,
}));
vi.mock("@/lib/hooks/useMyProfile", () => ({
  useMyProfile: () => ({
    isLoading: false,
    isError: false,
    data: { wallets: profileState.wallets },
    refetch: vi.fn(),
  }),
}));
vi.mock("@/lib/hooks/useSteamInventory", () => ({
  useSteamInventory: () => ({
    data: undefined,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  }),
}));
vi.mock("next/navigation", async (importOriginal) => ({
  ...(await importOriginal<typeof import("next/navigation")>()),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
  usePathname: () => "/en/transactions/new",
  useSearchParams: () => new URLSearchParams(searchParamsState.value),
}));

const TRON_ADDR = "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH";
const ETH_ADDR = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";
const payout = (network: BlockchainNetwork, address: string): UserWalletEntry => ({
  network,
  type: "PAYOUT",
  address,
  changedAt: null,
});

const ELIGIBLE: EligibilityResponse = {
  eligible: true,
  mobileAuthenticatorActive: true,
  concurrentLimit: { current: 0, max: 5 },
  cancelCooldown: { active: false, expiresAt: null },
  newAccountLimit: { isNewAccount: false, current: null, max: null },
};

function params(
  supportedNetworks: BlockchainNetwork[],
  networkMinPrices?: TransactionParamsResponse["networkMinPrices"],
): TransactionParamsResponse {
  return {
    minPrice: "1.00",
    maxPrice: "10000.00",
    commissionRate: 0.02,
    paymentTimeout: { minHours: 1, maxHours: 72, defaultHours: 24 },
    openLinkEnabled: true,
    supportedStablecoins: [StablecoinType.USDT],
    supportedNetworks,
    networkMinPrices,
  };
}

function seedDraft() {
  window.sessionStorage.setItem(
    WIZARD_DRAFT_STORAGE_KEY,
    JSON.stringify({
      item: { assetId: "1", name: "AK-47 | Redline", tradeable: true },
      stablecoin: "USDT",
      price: "20.00",
      paymentTimeoutHours: 24,
      method: BuyerIdentificationMethod.OPEN_LINK,
      buyerSteamId: "",
    }),
  );
}

function renderForm(
  supported: BlockchainNetwork[],
  messages: object = en,
  locale: "en" | "tr" | "es" | "zh" = "en",
  networkMinPrices?: TransactionParamsResponse["networkMinPrices"],
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale={locale} messages={messages}>
        <NewTransactionForm eligibility={ELIGIBLE} params={params(supported, networkMinPrices)} />
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  createTransaction.mockReset();
  createTransaction.mockResolvedValue({
    id: "new",
    status: "CREATED",
    inviteUrl: "/x",
    createdAt: "",
  });
  window.sessionStorage.clear();
  seedDraft();
  searchParamsState.value = "step=2";
});
afterEach(cleanup);

describe("NewTransactionForm — network choice (T156)", () => {
  it("offers only supportedNetworks: a closed network is not shown even with a payout address", () => {
    profileState.wallets = [
      payout(BlockchainNetwork.TRON, TRON_ADDR),
      payout(BlockchainNetwork.ETHEREUM, ETH_ADDR),
    ];
    renderForm([BlockchainNetwork.TRON]);
    expect(screen.getByTestId("network-option-TRON")).toBeChecked();
    expect(screen.queryByTestId("network-option-ETHEREUM")).toBeNull();
  });

  it("an open network without a payout address is shown but cannot be ticked", () => {
    profileState.wallets = [payout(BlockchainNetwork.TRON, TRON_ADDR)];
    renderForm([BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM]);
    const eth = screen.getByTestId("network-option-ETHEREUM");
    expect(eth).toBeDisabled();
    expect(eth).not.toBeChecked();
    expect(screen.getByText("You have no payout address on this network.")).toBeVisible();
  });

  it("unticking every network blocks the step", () => {
    profileState.wallets = [payout(BlockchainNetwork.TRON, TRON_ADDR)];
    renderForm([BlockchainNetwork.TRON]);
    fireEvent.click(screen.getByTestId("network-option-TRON"));
    expect(screen.getByRole("alert")).toHaveTextContent("Select at least one network.");
    expect(screen.getByRole("button", { name: "Next" })).toBeDisabled();
  });

  it.each([
    { locale: "en", messages: en, text: "No payout address on an open network" },
    { locale: "tr", messages: tr, text: "Açık ağlarda ödeme adresiniz yok" },
    { locale: "es", messages: es, text: "No tienes dirección de cobro en una red abierta" },
    { locale: "zh", messages: zh, text: "开放网络上没有收款地址" },
  ] as const)(
    "$locale: eligible seller whose only payout address is on a closed network is told why",
    ({ locale, messages, text }) => {
      profileState.wallets = [payout(BlockchainNetwork.ETHEREUM, ETH_ADDR)];
      renderForm([BlockchainNetwork.TRON], messages, locale);
      expect(screen.getByText(text)).toBeVisible();
      expect(document.body.textContent).toContain("TRON (TRC-20)");
      expect(screen.queryByTestId("network-option-TRON")).toBeNull();
    },
  );

  it("no open network at all: says so instead of an empty step", () => {
    profileState.wallets = [payout(BlockchainNetwork.TRON, TRON_ADDR)];
    renderForm([]);
    expect(screen.getByText("No network is open for new transactions")).toBeVisible();
  });

  it("POST /transactions carries the accepted networks the seller kept", async () => {
    profileState.wallets = [
      payout(BlockchainNetwork.TRON, TRON_ADDR),
      payout(BlockchainNetwork.ETHEREUM, ETH_ADDR),
    ];
    searchParamsState.value = "step=4";
    renderForm([BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM]);
    expect(screen.getByTestId("summary-networks")).toHaveTextContent(
      "USDT · TRON (TRC-20)USDT · Ethereum (ERC-20)",
    );
    fireEvent.click(screen.getByRole("button", { name: "Start transaction" }));
    await waitFor(() =>
      expect(createTransaction).toHaveBeenCalledWith(
        expect.objectContaining({
          acceptedNetworks: [BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM],
        }),
      ),
    );
  });
});

describe("NewTransactionForm — no TRON below its own minimum (T169)", () => {
  // The seeded draft prices the item at 20.00; TRON's minimum is 50.00
  // (owner decision 2026-10-21).
  const MINS = [{ network: "TRON", minPrice: "50.00" }];

  it.each([
    { locale: "en", messages: en, text: "under 50.00 USDT" },
    { locale: "tr", messages: tr, text: "50.00 USDT altındaki" },
    { locale: "es", messages: es, text: "menos de 50.00 USDT" },
    { locale: "zh", messages: zh, text: "50.00" },
  ] as const)(
    "$locale: TRON is shown disabled with the reason; Ethereum stays ticked",
    ({ locale, messages, text }) => {
      profileState.wallets = [
        payout(BlockchainNetwork.TRON, TRON_ADDR),
        payout(BlockchainNetwork.ETHEREUM, ETH_ADDR),
      ];
      renderForm([BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM], messages, locale, MINS);
      const tron = screen.getByTestId("network-option-TRON");
      expect(tron).toBeDisabled();
      expect(tron).not.toBeChecked();
      expect(screen.getByTestId("network-below-min-TRON")).toHaveTextContent(text);
      expect(screen.getByTestId("network-option-ETHEREUM")).toBeChecked();
    },
  );

  it("POST /transactions leaves TRON out below its minimum", async () => {
    profileState.wallets = [
      payout(BlockchainNetwork.TRON, TRON_ADDR),
      payout(BlockchainNetwork.ETHEREUM, ETH_ADDR),
    ];
    searchParamsState.value = "step=4";
    renderForm([BlockchainNetwork.TRON, BlockchainNetwork.ETHEREUM], en, "en", MINS);
    fireEvent.click(screen.getByRole("button", { name: "Start transaction" }));
    await waitFor(() =>
      expect(createTransaction).toHaveBeenCalledWith(
        expect.objectContaining({ acceptedNetworks: [BlockchainNetwork.ETHEREUM] }),
      ),
    );
  });
});
