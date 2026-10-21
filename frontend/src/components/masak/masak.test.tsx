import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import en from "@/i18n/messages/en.json";
import tr from "@/i18n/messages/tr.json";
import es from "@/i18n/messages/es.json";
import zh from "@/i18n/messages/zh.json";
import { BlockchainNetwork } from "@/types/enums";
import { LegalPage } from "@/components/legal";
import { BuyerExchangeNotice } from "./BuyerExchangeNotice";
import { PayoutSourceNotice } from "./PayoutSourceNotice";
import { MasakHelpSection } from "./MasakHelpSection";

/**
 * T167 — MASAK on the payment screens and the help page (11 T167; MASAK Genel
 * Tebliği Sıra No: 29 m. 4, Tedbirler Yönetmeliği m. 24/A-6, read against the
 * Resmî Gazete text — T167_REPORT "Resmî metin karşılaştırması").
 */

const { getPayoutSources } = vi.hoisted(() => ({ getPayoutSources: vi.fn() }));
vi.mock("@/lib/api/platform", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/platform")>()),
  getPayoutSources,
}));

// The legal shell's footer carries the language selector, which needs the
// Next app router; the section under test does not.
vi.mock("@/components/layout/Footer", () => ({ Footer: () => null }));

const TRON_HOT = "TJRabPrwbZy45sbavfcjinPJC18kjpRTv8";
const ETH_HOT = "0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed";

function wrap(
  node: React.ReactNode,
  messages: Record<string, unknown> = en,
  locale: "en" | "tr" | "es" | "zh" = "en",
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale={locale} messages={messages}>
        {node}
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  getPayoutSources.mockReset();
  getPayoutSources.mockResolvedValue({
    sources: [
      { network: BlockchainNetwork.TRON, address: TRON_HOT },
      { network: BlockchainNetwork.ETHEREUM, address: ETH_HOT },
    ],
  });
});

afterEach(cleanup);

describe("BuyerExchangeNotice", () => {
  it("full: the wait (48 h / 72 h first withdrawal), the 3,000 $ stablecoin limit, the declaration and the 20-character description", () => {
    wrap(<BuyerExchangeNotice variant="full" />);
    expect(screen.getByTestId("masak-buyer-hold")).toHaveTextContent("48 hours");
    expect(screen.getByTestId("masak-buyer-hold")).toHaveTextContent("72 hours");
    expect(screen.getByTestId("masak-buyer-limit")).toHaveTextContent("3,000 $ a day");
    expect(screen.getByTestId("masak-buyer-limit")).toHaveTextContent("50,000 $ a month");
    // T141(b) — the deposit is per transaction, so a saved address is never reused.
    expect(screen.getByTestId("masak-buyer-declare")).toHaveTextContent(
      "Every transaction has a different payment address",
    );
    expect(screen.getByTestId("masak-buyer-full")).toHaveTextContent("at least 20 characters");
    expect(screen.getByTestId("masak-buyer-full")).toHaveTextContent("not legal advice");
    expect(screen.getByRole("link", { name: "Details" })).toHaveAttribute(
      "href",
      "/en/support#masak",
    );
  });

  it("short (accept form): hold USDT in advance, with the link to the help page", () => {
    wrap(<BuyerExchangeNotice variant="short" />, tr, "tr");
    const note = screen.getByTestId("masak-buyer-short");
    expect(note).toHaveTextContent("USDT'yi şimdiden alın");
    expect(note).toHaveTextContent("48 saat");
    expect(note).toHaveTextContent("72 saat");
    expect(note).toHaveTextContent("3.000 $");
    expect(screen.getByRole("link", { name: "Ayrıntılar" })).toHaveAttribute(
      "href",
      "/tr/support#masak",
    );
  });
});

describe("PayoutSourceNotice", () => {
  it("with a network: only that network's fixed payout sender, copyable", async () => {
    wrap(<PayoutSourceNotice network={BlockchainNetwork.ETHEREUM} />);
    expect(await screen.findByTestId("payout-source-ETHEREUM")).toHaveTextContent(ETH_HOT);
    expect(screen.queryByTestId("payout-source-TRON")).toBeNull();
    expect(screen.getByTestId("masak-seller")).toHaveTextContent("the sender is Skinora");
    expect(screen.getByTestId("masak-seller")).toHaveTextContent(
      "always leave from the same address on each network",
    );
  });

  it("without a network (profile / help): every configured network", async () => {
    wrap(<PayoutSourceNotice />);
    expect(await screen.findByTestId("payout-source-TRON")).toHaveTextContent(TRON_HOT);
    expect(screen.getByTestId("payout-source-ETHEREUM")).toHaveTextContent(ETH_HOT);
  });

  it("a network without a configured sender: says so instead of showing a blank address", async () => {
    getPayoutSources.mockResolvedValue({
      sources: [{ network: BlockchainNetwork.TRON, address: TRON_HOT }],
    });
    wrap(<PayoutSourceNotice network={BlockchainNetwork.ETHEREUM} />);
    expect(await screen.findByTestId("payout-source-unavailable")).toBeInTheDocument();
    expect(screen.queryByTestId("payout-source-TRON")).toBeNull();
  });

  it("the endpoint fails: the declaration is still explained", async () => {
    getPayoutSources.mockRejectedValue(new Error("down"));
    wrap(<PayoutSourceNotice network={BlockchainNetwork.TRON} />);
    expect(await screen.findByTestId("payout-source-unavailable")).toBeInTheDocument();
    expect(screen.getByTestId("masak-seller")).toHaveTextContent("the sender is Skinora");
  });
});

describe("support page — MASAK section", () => {
  it("is reachable at #masak and carries the buyer's rules, the seller's addresses and the official sources", async () => {
    wrap(
      <LegalPage
        namespace="legal.support"
        sectionKeys={["contact", "masak", "faq"]}
        sectionExtras={{ masak: <MasakHelpSection /> }}
      />,
    );
    const section = document.getElementById("masak");
    expect(section).not.toBeNull();
    expect(section).toHaveTextContent("Exchanges in Türkiye and MASAK rules");
    expect(section).toHaveTextContent("48 hours");
    expect(section).toHaveTextContent("3,000 $ a day");
    expect(section).toHaveTextContent("daily and monthly TOTAL");
    expect(section).toHaveTextContent("Refunds also come from a Skinora address");
    expect(await screen.findByTestId("payout-source-TRON")).toHaveTextContent(TRON_HOT);
    expect(screen.getByTestId("masak-help-source")).toHaveTextContent(
      "Official Gazette of Türkiye 28.06.2025, issue 32940, article 4",
    );
  });
});

describe("MASAK copy — the official figures in every locale", () => {
  // Tebliğ 29 m. 4/3 (48 h, 72 h first withdrawal), m. 4/4 (3,000 $ / 50,000 $),
  // m. 4/5 (20 characters). A translation that drops or changes a figure fails.
  const locales = { en, tr, es, zh } as const;
  it.each(Object.entries(locales))("%s", (_loc, messages) => {
    const buyer = (messages as typeof en).masak.buyer;
    expect(buyer.holdInAdvance).toMatch(/48/);
    expect(buyer.holdInAdvance).toMatch(/72/);
    expect(buyer.short).toMatch(/48/);
    expect(buyer.short).toMatch(/72/);
    expect(buyer.short).toMatch(/3[.,]000 \$/);
    expect(buyer.dailyLimit).toMatch(/3[.,]000 \$/);
    expect(buyer.dailyLimit).toMatch(/50[.,]000 \$/);
    expect(buyer.description).toMatch(/20/);
    expect((messages as typeof en).masak.help.refund).toMatch(/Skinora/);
    expect((messages as typeof en).masak.help.sources).toMatch(/32940/);
  });
});
