import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import messages from "@/i18n/messages/tr.json";
import SteamLoginPage from "./page";
import type { Locale } from "next-intl";

/**
 * F4a — `UITour-SignupLanguageHardcodedEn`'in ana yolu.
 *
 * Bu dosyanın tek bir iddiası var ve o iddia bir dizgi değil, bir ZİNCİR:
 * A1'e giden `returnUrl` bir locale segmenti taşımazsa backend yeni
 * kullanıcının dilini türetemez ve `en`'e düşer — çünkü tek ipucu bu değerin
 * ilk segmentidir (`SupportedLanguages.FromPathPrefix`). F4 backend yarısını
 * doğru kurdu ama ürünün en çok kullanılan girişi (açılış sayfasının kayıt
 * CTA'sı) `returnUrl` hiç geçirmiyordu ve sayfa da locale'siz "/dashboard"a
 * düşüyordu; ölçüldü: `?returnUrl=%2Fdashboard`.
 *
 * Testler bu yüzden butona TIKLAYIP gerçekten gidilen A1 URL'ini okuyor —
 * `sanitizeReturnUrl`'ü doğrudan çağırmak, bileşenin onu locale ile besleyip
 * beslemediğini ölçmez ve kusur tam olarak oradaydı.
 */

let params = new URLSearchParams();
vi.mock("next/navigation", () => ({
  useSearchParams: () => params,
  useRouter: () => ({ replace: vi.fn(), push: vi.fn() }),
  usePathname: () => "/tr/auth/login",
  useParams: () => ({ locale: "tr" }),
  redirect: vi.fn(),
  permanentRedirect: vi.fn(),
  notFound: vi.fn(),
}));

const assign = vi.fn();

function renderLogin(locale: Locale = "tr") {
  return render(
    <NextIntlClientProvider locale={locale} messages={messages}>
      <SteamLoginPage />
    </NextIntlClientProvider>,
  );
}

/** Butona basıp gerçekten gidilen A1 URL'ini döndürür. */
function clickAndReadA1(): string {
  fireEvent.click(screen.getByTestId("steam-login-button"));
  expect(assign).toHaveBeenCalledTimes(1);
  return assign.mock.calls[0][0] as string;
}

beforeEach(() => {
  assign.mockClear();
  params = new URLSearchParams();
  Object.defineProperty(window, "location", {
    value: { ...window.location, assign },
    writable: true,
    configurable: true,
  });
});

afterEach(cleanup);

describe("SteamLoginPage — A1'e giden returnUrl", () => {
  it("returnUrl verilmediğinde arayüz dilini taşır (açılış sayfası CTA'sının yolu)", () => {
    renderLogin("tr");
    expect(clickAndReadA1()).toBe("/api/v1/auth/steam?returnUrl=%2Ftr%2Fdashboard");
  });

  it("locale'siz bir returnUrl'e arayüz dilini önekler", () => {
    params = new URLSearchParams("returnUrl=%2Ftransactions%2Fnew");
    renderLogin("tr");
    expect(clickAndReadA1()).toBe("/api/v1/auth/steam?returnUrl=%2Ftr%2Ftransactions%2Fnew");
  });

  it("zaten locale taşıyan returnUrl'e DOKUNMAZ — açık hedef kazanır", () => {
    params = new URLSearchParams("returnUrl=%2Fes%2Fdashboard");
    renderLogin("tr");
    expect(clickAndReadA1()).toBe("/api/v1/auth/steam?returnUrl=%2Fes%2Fdashboard");
  });

  it("uygulama-dışı bir returnUrl'i atar ve dilli varsayılana döner", () => {
    params = new URLSearchParams("returnUrl=https%3A%2F%2Fevil.example%2Fx");
    renderLogin("tr");
    expect(clickAndReadA1()).toBe("/api/v1/auth/steam?returnUrl=%2Ftr%2Fdashboard");
  });

  it("protokolsüz//host biçimini de atar (açık yönlendirme yüzeyi)", () => {
    params = new URLSearchParams("returnUrl=%2F%2Fevil.example%2Fx");
    renderLogin("tr");
    expect(clickAndReadA1()).toBe("/api/v1/auth/steam?returnUrl=%2Ftr%2Fdashboard");
  });

  it("dil değişince varsayılan da değişir — sabitlenmiş bir 'tr' değil", () => {
    renderLogin("es");
    expect(clickAndReadA1()).toBe("/api/v1/auth/steam?returnUrl=%2Fes%2Fdashboard");
  });
});
