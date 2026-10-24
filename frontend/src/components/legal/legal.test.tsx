import { describe, it, expect, vi, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import en from "@/i18n/messages/en.json";
import tr from "@/i18n/messages/tr.json";
import es from "@/i18n/messages/es.json";
import zh from "@/i18n/messages/zh.json";
import {
  OPERATOR_INFO,
  REQUIRED_OPERATOR_FIELDS,
  SUPPORT_EMAIL,
  isOperatorInfoComplete,
  operatorMessageValues,
  type OperatorInfo,
} from "@/lib/legal/operator";
import { PRIVACY_SECTIONS, SUPPORT_SECTIONS, TERMS_SECTIONS } from "@/lib/legal/sections";
import { LegalBody } from "./LegalBody";
import { LegalPage } from "./LegalPage";

/**
 * T168 — legal texts (11 §F9 T168, owner decision 2026-10-21: no company yet;
 * texts complete, operator fields empty and marked, DRAFT label stays until
 * the operator's details are filled in).
 */

vi.mock("@/components/layout/Footer", () => ({ Footer: () => null }));

type Messages = typeof en;
const LOCALES = { tr, en, es, zh } as const satisfies Record<string, Messages>;
const PAGES = {
  terms: TERMS_SECTIONS,
  privacy: PRIVACY_SECTIONS,
  support: SUPPORT_SECTIONS,
} as const;

function wrap(
  node: React.ReactNode,
  messages: Messages = tr,
  locale: "tr" | "en" | "es" | "zh" = "tr",
) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale={locale} messages={messages} timeZone="UTC">
        {node}
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

afterEach(cleanup);

const FILLED: OperatorInfo = {
  legalName: "Örnek Teknoloji A.Ş.",
  address: "Örnek Mah. 1. Sok. No: 1, İstanbul",
  taxOffice: "Kadıköy",
  taxId: "1234567890",
  mersisNo: "",
  kepAddress: "",
  courtVenue: "İstanbul (Merkez)",
};

describe("operator record (single source)", () => {
  it("is empty today — owner decision 2026-10-21, no company yet", () => {
    for (const field of REQUIRED_OPERATOR_FIELDS) expect(OPERATOR_INFO[field]).toBe("");
    expect(isOperatorInfoComplete()).toBe(false);
  });

  it("is complete only when every required field is filled", () => {
    expect(isOperatorInfoComplete(FILLED)).toBe(true);
    expect(isOperatorInfoComplete({ ...FILLED, courtVenue: "  " })).toBe(false);
    // MERSİS and KEP are optional (a sole trader has neither).
    expect(isOperatorInfoComplete({ ...FILLED, mersisNo: "", kepAddress: "" })).toBe(true);
  });

  it("empty fields become the pending marker, filled ones pass through", () => {
    const pending = operatorMessageValues("[P]");
    expect(pending.legalName).toBe("[P]");
    expect(pending.courtVenue).toBe("[P]");
    expect(pending.supportEmail).toBe("support@getskinora.com");
    const filled = operatorMessageValues("[P]", FILLED);
    expect(filled.legalName).toBe(FILLED.legalName);
    expect(filled.mersisNo).toBe("[P]");
  });

  it("one support domain: getskinora.com", () => {
    expect(SUPPORT_EMAIL).toBe("support@getskinora.com");
    for (const messages of Object.values(LOCALES)) {
      expect(JSON.stringify(messages)).not.toMatch(/skinora\.app/);
    }
  });
});

describe("LegalBody", () => {
  it("renders paragraphs, a lead-in line with its bullet list, and a numbered list", () => {
    render(
      <LegalBody
        text={"First paragraph.\n\nLead-in:\n- one\n- two\n\n1. step a\n2. step b\n\nLast."}
      />,
    );
    expect(screen.getByText("First paragraph.").tagName).toBe("P");
    expect(screen.getByText("Lead-in:").tagName).toBe("P");
    const bullets = screen.getByText("one").closest("ul");
    expect(bullets?.querySelectorAll("li")).toHaveLength(2);
    const steps = screen.getByText("step a").closest("ol");
    expect(steps?.querySelectorAll("li")).toHaveLength(2);
    expect(screen.getByText("step b").textContent).toBe("step b");
    expect(screen.getByText("Last.").tagName).toBe("P");
  });
});

describe("legal pages while the operator is unknown", () => {
  it("terms: DRAFT notice, pending marker in the intro and in the jurisdiction clause, operator block", () => {
    wrap(<LegalPage namespace="legal.terms" sectionKeys={TERMS_SECTIONS} showContents />);
    expect(screen.getByTestId("legal-draft-notice")).toHaveTextContent("TASLAKTIR");
    expect(screen.getByTestId("legal-last-updated")).toHaveTextContent("2026");
    const pending = tr.legal.operator.pending;
    expect(document.querySelector("main")).toHaveTextContent(`İşletmeci: ${pending}`);
    const law = document.getElementById("governingLaw");
    expect(law).toHaveTextContent("Türkiye Cumhuriyeti hukuku");
    expect(law).toHaveTextContent(`${pending} mahkemeleri ve icra daireleri yetkilidir`);
    for (const field of ["legalName", "address", "taxOffice", "taxId", "courtVenue"]) {
      expect(screen.getByTestId(`operator-${field}`)).toHaveTextContent(pending);
    }
    expect(screen.getByTestId("legal-operator-block")).toHaveTextContent("support@getskinora.com");
    // Contents list links every section.
    expect(
      screen.getByRole("navigation", { name: "İçindekiler" }).querySelectorAll("a"),
    ).toHaveLength(TERMS_SECTIONS.length);
  });

  it("privacy: data controller is the operator (pending) and the KVKK rights are listed", () => {
    wrap(<LegalPage namespace="legal.privacy" sectionKeys={PRIVACY_SECTIONS} showContents />);
    expect(screen.getByTestId("legal-draft-notice")).toBeInTheDocument();
    const controller = document.getElementById("controller");
    expect(controller).toHaveTextContent(tr.legal.operator.pending);
    expect(controller).toHaveTextContent("support@getskinora.com");
    const rights = document.getElementById("yourRights");
    expect(rights?.querySelectorAll("li").length).toBeGreaterThanOrEqual(9);
    expect(rights).toHaveTextContent("Şartları kabul etmiyorum");
  });

  it("never renders a raw placeholder or a missing-key path", () => {
    for (const [page, keys] of Object.entries(PAGES)) {
      const { unmount } = wrap(
        <LegalPage namespace={`legal.${page}` as "legal.terms"} sectionKeys={keys} />,
      );
      const text = document.querySelector("main")?.textContent ?? "";
      expect(text).not.toMatch(/\{[a-zA-Z]+\}/);
      expect(text).not.toMatch(/sections\.[a-zA-Z]+\.(heading|body)/);
      unmount();
    }
  });
});

describe("catalogue — every locale carries the same legal structure", () => {
  const placeholders = (s: string) => [...s.matchAll(/\{(\w+)\}/g)].map((m) => m[1]).sort();
  const shape = (s: string) => ({
    bullets: s.split("\n").filter((l) => l.startsWith("- ")).length,
    steps: s.split("\n").filter((l) => /^\d+\.\s/.test(l)).length,
    placeholders: placeholders(s),
  });

  it.each(Object.entries(PAGES))(
    "%s: every listed section exists in all four locales",
    (page, keys) => {
      for (const messages of Object.values(LOCALES)) {
        const sections = (
          messages.legal as unknown as Record<string, { sections: Record<string, unknown> }>
        )[page].sections;
        expect(Object.keys(sections).sort()).toEqual([...keys].sort());
      }
    },
  );

  it.each(Object.entries(PAGES))(
    "%s: translations keep the Turkish list items and placeholders (TR is authoritative)",
    (page, keys) => {
      const get = (m: Messages) =>
        (
          m.legal as unknown as Record<
            string,
            { intro: string; sections: Record<string, { body: string }> }
          >
        )[page];
      for (const [loc, messages] of Object.entries(LOCALES)) {
        expect(shape(get(messages).intro), `${loc} ${page}.intro`).toEqual(shape(get(tr).intro));
        for (const key of keys) {
          expect(shape(get(messages).sections[key].body), `${loc} ${page}.${key}`).toEqual(
            shape(get(tr).sections[key].body),
          );
        }
      }
    },
  );
});

describe("terms — 02 §22 P2P clauses and the network rules (TR, authoritative)", () => {
  const s = tr.legal.terms.sections;
  it("no custody of the item; the platform guarantees the refund, not the delivery (02 §20.1)", () => {
    expect(s.serviceDescription.body).toMatch(/eşya hiçbir aşamada Skinora'ya geçmez/);
    expect(s.serviceDescription.body).toMatch(/garanti edemez/);
    expect(s.liability.body).toMatch(/alıcının parasının iadesidir/);
  });
  it("trade reversal: protected within the settlement window, Steam's own actions are the user's risk (02 §20.2, §4.5.1)", () => {
    expect(s.settlement.body).toMatch(/7 gün/);
    expect(s.settlement.body).toMatch(/8 gün/);
    expect(s.liability.body).toMatch(/Steam'in kendi kararıyla yaptığı geri almalar/);
  });
  it("the parties see each other's Steam profile and the seller sees the buyer's trade URL (02 §22)", () => {
    expect(s.visibility.body).toMatch(/Steam profilini/);
    expect(s.visibility.body).toMatch(/trade URL/);
  });
  it("delivery window 12 hours (owner decision 2026-10-04)", () => {
    expect(s.delivery.body).toMatch(/12 saati vardır/);
  });
  it("networks: TRON + Ethereum, USDT only, wrong EVM network not auto-refunded", () => {
    expect(s.paymentAndNetworks.body).toMatch(/TRC-20/);
    expect(s.paymentAndNetworks.body).toMatch(/ERC-20/);
    expect(s.paymentAndNetworks.body).toMatch(/Başka token ve başka ağ kabul edilmez/);
    expect(s.paymentAndNetworks.body).toMatch(/otomatik iade edilmez/);
  });
  it("network fee deductions: TRON fee line, refund gas deducted from every refund (T149, T159a, T160b)", () => {
    expect(s.fees.body).toMatch(/TRON ağ ücreti/);
    expect(s.fees.body).toMatch(/Ethereum'da satıcı fiyatın tamamını alır/);
    expect(s.refunds.body).toMatch(/iade edilen tutar − iadenin ağ ücreti/);
    expect(s.refunds.body).toMatch(/Reddedilen ödemelerin/);
    expect(s.refunds.body).toMatch(/son ölçülen ücretten küçük olanı/);
  });
  it("MASAK note without a licence claim (T167)", () => {
    expect(s.masak.body).toMatch(/Bu kuralları borsanız uygular, Skinora değil/);
    expect(s.masak.body).toMatch(/hukuki tavsiye değildir/);
    for (const messages of Object.values(LOCALES)) {
      expect(JSON.stringify(messages.legal)).not.toMatch(/lisanslı|licensed|licenciad|持牌/i);
    }
  });
  it("governing law + competent court clause, venue is an operator field", () => {
    for (const messages of Object.values(LOCALES)) {
      expect(messages.legal.terms.sections.governingLaw.body).toContain("{courtVenue}");
    }
  });
  it("T168 fix S4-1/S4-2 — a single seller cancellation lowers reputation only (the ban needs repeated cancellations); the refund fee is shown on the transaction page", () => {
    // CancelCooldownEvaluator: the cooldown starts only past cancel_limit_count.
    expect(s.cancellation.body).not.toMatch(/yeni işlem açma yasağı/);
    expect(s.cancellation.body).toMatch(/Kısa sürede çok sayıda iptal/);
    // PAYMENT_REFUNDED carries no fee; the transaction page does.
    expect(s.refunds.body).toMatch(/Düşülen ücret işlem sayfasında/);
  });
  it("T169a — the refund fee is the platform's whole outlay (both networks) and deposit refunds have a daily limit", () => {
    expect(s.refunds.body).toMatch(/blok zinciri harcamasının tamamıdır/);
    expect(s.refunds.body).toMatch(/TRON'da ödeme adresinin açılışı/);
    expect(s.refunds.body).toMatch(/Ethereum'da ödeme adresine gönderilen ETH/);
    expect(s.refunds.body).toMatch(/iade tutarını aşamaz/);
    expect(s.refunds.body).toMatch(/herhangi bir 24 saatte/);
    expect(s.refunds.body).toMatch(/ağ başına 100 USDT/);
    expect(s.refunds.body).toMatch(/Bekleme iadenin tutarını değiştirmez/);
    for (const messages of Object.values(LOCALES)) {
      expect(messages.legal.terms.sections.refunds.body).toMatch(/100 USDT/);
    }
  });
  it("T168 fix B3 — the decline path is promised for an updated version too", () => {
    expect(s.suspension.body).toMatch(/güncellenmiş bir sürümünü kabul etmeden de/);
  });
  it("no hedging words in the Turkish legal texts (GUARDRAILS §5)", () => {
    expect(JSON.stringify(tr.legal)).not.toMatch(/muhtemelen|belki/i);
  });
});

describe("privacy — retention matches AccountLifecycleService + the login-log retention job (T168 fix B2)", () => {
  const p = tr.legal.privacy.sections;
  it("account deletion clears the device/IP data of the SESSION records (RefreshToken), not every log", () => {
    expect(p.dataRetention.body).toMatch(/oturum kayıtlarınızdaki cihaz ve IP bilgisi silinir/);
    expect(p.dataRetention.body).not.toMatch(/oturum kayıtlarınız \(cihaz ve IP bilgisi dahil\)/);
  });
  it("sign-in records (UserLoginLog) stay 1 year (retention.user_login_log_days = 365), also after deletion", () => {
    expect(p.dataRetention.body).toMatch(/Giriş kayıtları/);
    expect(p.dataRetention.body).toMatch(/1 yıl saklanır, sonra kalıcı olarak silinir/);
    expect(p.dataRetention.body).toMatch(/hemen silinmez/);
    // DeviceFingerprint is never written by LoginAuditService — not promised.
    expect(p.dataRetention.body).not.toMatch(/parmak izi/);
  });
});
