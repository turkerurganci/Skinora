import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import messages from "@/i18n/messages/tr.json";
import { TosRepromptGate } from "@/components/auth/TosRepromptGate";
import { useAuthStore } from "@/lib/stores/auth-store";
import SteamCallbackPage from "./page";
import type { Locale } from "next-intl";

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

const { replace, pathname } = vi.hoisted(() => ({
  replace: vi.fn(),
  pathname: { current: "/tr/auth/callback" },
}));
let params = new URLSearchParams();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), back: vi.fn(), refresh: vi.fn() }),
  useSearchParams: () => params,
  usePathname: () => pathname.current,
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

// T168 — the first-time ToS modal's decline path reuses the settings
// account-management section; its API calls are mocked here.
const { deleteAccount, deactivateAccount } = vi.hoisted(() => ({
  deleteAccount: vi.fn(),
  deactivateAccount: vi.fn(),
}));
vi.mock("@/lib/api/settings", () => ({
  DELETE_ACCOUNT_CONFIRMATION: "SİL",
  deleteAccount,
  deactivateAccount,
}));
// T168 B5 — the REAL signOut runs (lib/auth/signOut is not mocked); only its
// server call and the global gate's `/auth/me` are stubbed.
const { logoutRequest, getMe } = vi.hoisted(() => ({ logoutRequest: vi.fn(), getMe: vi.fn() }));
vi.mock("@/lib/api/auth", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/auth")>()),
  logout: logoutRequest,
  getMe,
}));

function renderCallback(locale: Locale = "tr") {
  return render(
    <NextIntlClientProvider locale={locale} messages={messages}>
      <SteamCallbackPage />
    </NextIntlClientProvider>,
  );
}

beforeEach(() => {
  replace.mockReset();
  pathname.current = "/tr/auth/callback";
  refreshAccessToken.mockResolvedValue("fake-access-token");
  logoutRequest.mockResolvedValue(undefined);
});

afterEach(() => {
  cleanup();
  useAuthStore.getState().logout();
});

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

  // AgeGateMessageDescribesWrongRule — /auth/age-gate'e iki alakasız sebepten
  // gelinir (Steam hesabı çok yeni / kullanıcı 18+ kutusunu reddetti) ve sayfa
  // ikisini ancak bu iki sayı kendisine ulaşırsa ayırt edebilir.
  it("age_blocked → hesap yaşı sayıları age-gate'e taşınır", async () => {
    params = new URLSearchParams("error=age_blocked&accountAgeDays=4&requiredDays=30");
    renderCallback("tr");
    await waitFor(() =>
      expect(replace).toHaveBeenCalledWith("/tr/auth/age-gate?accountAgeDays=4&requiredDays=30"),
    );
  });

  it("sayılar yoksa sorgu dizesi EKLENMEZ — 18+ metni varsayılan kalır", async () => {
    params = new URLSearchParams("error=age_blocked");
    renderCallback("tr");
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/tr/auth/age-gate"));
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

describe("SteamCallbackPage — ilk kabul modalında reddetme yolu (T168)", () => {
  it("yeni kullanıcı şartları kabul etmeden hesabını silebilir (KVKK silme hakkı)", async () => {
    // jsdom'da modal dialog yok; onay penceresi yalnız `open`ın dönmesini bekler.
    if (!HTMLDialogElement.prototype.showModal) {
      HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
        this.open = true;
      };
      HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
        this.open = false;
      };
    }
    deleteAccount.mockResolvedValue({ deletedAt: "2026-10-22T00:00:00Z", message: "ok" });
    params = new URLSearchParams("status=new_user&returnUrl=%2Ftr%2Fdashboard");

    renderCallback("tr");

    await waitFor(() => expect(screen.getByTestId("tos-decline-toggle")).toBeTruthy());
    fireEvent.click(screen.getByTestId("tos-decline-toggle"));
    const panel = screen.getByTestId("tos-decline-panel");
    expect(panel).toHaveTextContent(messages.auth.tos.decline.description);
    expect(panel).toHaveTextContent(messages.settings.accountManagement.deactivate.button);

    fireEvent.click(
      screen.getAllByText(messages.settings.accountManagement.delete.button)[0] as HTMLElement,
    );
    fireEvent.change(screen.getByPlaceholderText("SİL"), { target: { value: "SİL" } });
    fireEvent.click(document.querySelector("dialog button[type=submit]") as HTMLButtonElement);

    await waitFor(() => expect(deleteAccount).toHaveBeenCalledWith("SİL"));
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/tr"));
    // Kabul edilmedi; panele de gidilmedi.
    expect(replace).not.toHaveBeenCalledWith("/tr/dashboard");
  });
});

describe("SteamCallbackPage — reddedip silince pencere oturumla kapanır (T168 B5)", () => {
  it("sil + çıkış sonrası ne callback penceresi ne genel kapı 'şartları kabul edin' gösterir", async () => {
    if (!HTMLDialogElement.prototype.showModal) {
      HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
        this.open = true;
      };
      HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
        this.open = false;
      };
    }
    deleteAccount.mockResolvedValue({ deletedAt: "2026-10-22T00:00:00Z", message: "ok" });
    const notAccepted = { tosAccepted: false, tosAcceptedVersion: null };
    getMe.mockResolvedValue(notAccepted);
    replace.mockImplementation((to: string) => {
      pathname.current = to;
    });
    params = new URLSearchParams("status=new_user&returnUrl=%2Ftr%2Fdashboard");

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    // AuthInitializer'ın pencere açılmadan önce önbelleğe bıraktığı cevap.
    client.setQueryData(["auth", "me"], notAccepted);
    // Genel kapı [locale] layout'unda yaşar; callback'te uyur, /tr'de uyanır.
    const tree = () => (
      <QueryClientProvider client={client}>
        <NextIntlClientProvider locale="tr" messages={messages}>
          <SteamCallbackPage />
          <TosRepromptGate />
        </NextIntlClientProvider>
      </QueryClientProvider>
    );
    const { rerender } = render(tree());

    await waitFor(() => expect(screen.getByTestId("tos-decline-toggle")).toBeTruthy());
    fireEvent.click(screen.getByTestId("tos-decline-toggle"));
    fireEvent.click(
      screen.getAllByText(messages.settings.accountManagement.delete.button)[0] as HTMLElement,
    );
    fireEvent.change(screen.getByPlaceholderText("SİL"), { target: { value: "SİL" } });
    fireEvent.click(document.querySelector("dialog button[type=submit]") as HTMLButtonElement);

    await waitFor(() => expect(deleteAccount).toHaveBeenCalledWith("SİL"));
    await waitFor(() => expect(logoutRequest).toHaveBeenCalled());
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/tr"));
    expect(useAuthStore.getState().accessToken).toBeNull();

    rerender(tree());

    await waitFor(() => expect(screen.queryByTestId("tos-submit")).toBeNull());
    expect(screen.queryByText(messages.auth.tos.title)).toBeNull();
    expect(screen.queryByTestId("tos-decline-toggle")).toBeNull();
  });
});
