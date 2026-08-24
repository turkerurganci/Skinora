import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import messages from "@/i18n/messages/tr.json";
import SteamCallbackPage from "./page";

/**
 * F4b regresyon testi — `/tr/tr/dashboard`.
 *
 * F4a login sayfasına locale öğretti (`returnUrl=/tr/dashboard`) ama bu sayfa
 * kendi `sanitizeReturnUrl` KOPYASINI taşıyordu ve dönen değeri `localePath`
 * ile bir kez daha önekliyordu. Sonuç: Türkçe giriş `/tr/tr/dashboard`'a düştü
 * ve 404 aldı — üretimdeki gerçek bir girişin nginx log'unda ölçüldü.
 *
 * Kusur iki KOPYAdan doğdu, tek bir yanlış satırdan değil; düzeltme ikisini de
 * `@/lib/auth/returnUrl`e bağladı. Bu test o birleşmenin bekçisi: hedefin
 * TAM DEĞERİNİ okur, "içinde dashboard geçiyor mu" demez.
 */

const { replace } = vi.hoisted(() => ({ replace: vi.fn() }));
let params = new URLSearchParams();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), back: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => params,
  usePathname: () => "/tr/auth/callback",
  useParams: () => ({ locale: "tr" }),
  redirect: vi.fn(),
  permanentRedirect: vi.fn(),
  notFound: vi.fn(),
}));

const { refreshAccessToken } = vi.hoisted(() => ({ refreshAccessToken: vi.fn() }));
vi.mock("@/lib/api/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/client")>()),
  refreshAccessToken,
}));

function renderCallback(locale = "tr") {
  return render(
    <NextIntlClientProvider locale={locale} messages={messages}>
      <SteamCallbackPage />
    </NextIntlClientProvider>,
  );
}

beforeEach(() => {
  replace.mockClear();
  refreshAccessToken.mockResolvedValue("fake-access-token");
});

afterEach(cleanup);

describe("SteamCallbackPage — giriş sonrası hedef", () => {
  it("locale önekli returnUrl'i TEKRAR öneklemez (/tr/tr/dashboard regresyonu)", async () => {
    params = new URLSearchParams("status=success&returnUrl=%2Ftr%2Fdashboard");
    renderCallback("tr");
    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(replace).toHaveBeenCalledWith("/tr/dashboard");
  });

  it("locale'siz returnUrl'e bir KEZ önek ekler", async () => {
    params = new URLSearchParams("status=success&returnUrl=%2Fdashboard");
    renderCallback("tr");
    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(replace).toHaveBeenCalledWith("/tr/dashboard");
  });

  it("returnUrl yoksa dilli panele gider", async () => {
    params = new URLSearchParams("status=success");
    renderCallback("tr");
    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(replace).toHaveBeenCalledWith("/tr/dashboard");
  });

  it("başka bir locale taşıyan returnUrl korunur", async () => {
    params = new URLSearchParams("status=success&returnUrl=%2Fes%2Ftransactions%2Fnew");
    renderCallback("tr");
    await waitFor(() => expect(replace).toHaveBeenCalled());
    expect(replace).toHaveBeenCalledWith("/es/transactions/new");
  });

  it("token alınamazsa hiçbir yere yönlendirmez", async () => {
    refreshAccessToken.mockResolvedValue(null);
    params = new URLSearchParams("status=success&returnUrl=%2Ftr%2Fdashboard");
    renderCallback("tr");
    await waitFor(() => expect(refreshAccessToken).toHaveBeenCalled());
    expect(replace).not.toHaveBeenCalled();
  });
});

describe("SteamCallbackPage — politika reddi kendi ekranına gider (F4c)", () => {
  it.each([
    ["age_blocked", "/tr/auth/age-gate"],
    ["geo_blocked", "/tr/auth/geo-block"],
    ["sanctions_match", "/tr/auth/sanctions"],
  ])("%s → %s", async (code, target) => {
    params = new URLSearchParams(`error=${code}`);
    renderCallback("tr");
    await waitFor(() => expect(replace).toHaveBeenCalledWith(target));
  });

  it("politika reddinde jenerik hata kartı ÇİZİLMEZ — Tekrar Dene düğmesi yok", () => {
    params = new URLSearchParams("error=age_blocked");
    const { container } = renderCallback("tr");
    expect(container.textContent).not.toContain(messages.auth.callback.error.unknown.title);
    expect(screen.queryByText(messages.common.retry)).toBeNull();
  });

  it("tanınan bir hata kodu YÖNLENDİRİLMEZ, kartını gösterir", () => {
    params = new URLSearchParams("error=account_banned");
    renderCallback("tr");
    expect(replace).not.toHaveBeenCalled();
  });

  it("bilinmeyen bir kod da yönlendirilmez — sessiz yutulmaz", () => {
    params = new URLSearchParams("error=some_future_code");
    renderCallback("tr");
    expect(replace).not.toHaveBeenCalled();
  });
});
