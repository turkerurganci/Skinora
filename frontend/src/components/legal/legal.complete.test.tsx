import { describe, it, expect, vi, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import tr from "@/i18n/messages/tr.json";
import { TERMS_SECTIONS } from "@/lib/legal/sections";
import { BuyerExchangeNotice } from "@/components/masak/BuyerExchangeNotice";
import { LegalPage } from "./LegalPage";

/**
 * T168 — the other side of the owner decision: once the operator's details are
 * filled in (`lib/legal/operator.ts`), the DRAFT notice disappears, the texts
 * name the operator and no pending marker is left. Mocks the single operator
 * record with a filled one.
 */

const { FILLED } = vi.hoisted(() => ({
  FILLED: {
    legalName: "Örnek Teknoloji A.Ş.",
    address: "Örnek Mah. 1. Sok. No: 1, İstanbul",
    taxOffice: "Kadıköy",
    taxId: "1234567890",
    mersisNo: "0123456789012345",
    kepAddress: "ornek@hs01.kep.tr",
    courtVenue: "İstanbul (Merkez)",
  },
}));

vi.mock("@/lib/legal/operator", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/lib/legal/operator")>();
  return {
    ...actual,
    OPERATOR_INFO: FILLED,
    isOperatorInfoComplete: (info = FILLED) => actual.isOperatorInfoComplete(info),
    operatorMessageValues: (pending: string, info = FILLED) =>
      actual.operatorMessageValues(pending, info),
  };
});
vi.mock("@/components/layout/Footer", () => ({ Footer: () => null }));

function wrap(node: React.ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale="tr" messages={tr} timeZone="UTC">
        {node}
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

afterEach(cleanup);

describe("legal pages once the operator is known", () => {
  it("no DRAFT notice, the operator is named and the court venue is filled in", () => {
    wrap(<LegalPage namespace="legal.terms" sectionKeys={TERMS_SECTIONS} />);
    expect(screen.queryByTestId("legal-draft-notice")).toBeNull();
    const main = document.querySelector("main");
    expect(main).toHaveTextContent(`İşletmeci: ${FILLED.legalName}`);
    expect(document.getElementById("governingLaw")).toHaveTextContent(
      "İstanbul (Merkez) mahkemeleri ve icra daireleri yetkilidir",
    );
    expect(main).not.toHaveTextContent(tr.legal.operator.pending);
    expect(screen.getByTestId("operator-mersisNo")).toHaveTextContent(FILLED.mersisNo);
  });

  it("MASAK declaration carries the trade name and address (Yönetmelik 24/A-6)", () => {
    wrap(<BuyerExchangeNotice variant="full" />);
    const entity = screen.getByTestId("masak-buyer-entity");
    expect(entity).toHaveTextContent(FILLED.legalName);
    expect(entity).toHaveTextContent(FILLED.address);
  });
});
