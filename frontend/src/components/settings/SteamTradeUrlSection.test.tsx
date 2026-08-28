import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import messages from "@/i18n/messages/en.json";
import { ApiError } from "@/lib/api/client";
import { SteamTradeUrlSection } from "./SteamTradeUrlSection";

/**
 * F1 — U17'nin tek UI çağıranı (`UITour-NoUiPathToVerifyMobileAuthenticator`).
 *
 * Buradaki asıl iddia **üç sonucun ayrıldığı**: backend "MA kapalı" ile
 * "Steam'e ulaşılamadı"yı aynı `mobileAuthenticatorActive: false` değeriyle
 * döndürüyor ve ikisini ayıran tek şey `setupGuideUrl`'ün varlığı — çünkü
 * pending dalı HTTP 503 olmasına rağmen `success: true` zarfıyla geliyor ve
 * `apiClient` durum kodunu yukarı taşımıyor. Bu ayrımı canlı Steam'i bozarak
 * denemek kırılgan olurdu; ayrım istemci mantığıdır ve burada ölçülür.
 *
 * Gerçek `en.json` bilerek kullanılıyor: bileşenin adres verdiği ama hiçbir
 * dilin taşımadığı bir anahtar sessizce kendi noktalı yolunu render eder ve
 * gerçek metne assert etmek bunu yakalar.
 */

const { updateSteamTradeUrl, getMyProfile } = vi.hoisted(() => ({
  updateSteamTradeUrl: vi.fn(),
  getMyProfile: vi.fn(),
}));

vi.mock("@/lib/api/users", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/api/users")>()),
  updateSteamTradeUrl,
  getMyProfile,
}));

vi.mock("@/lib/stores/auth-store", () => ({
  useAuthStore: (selector: (s: { isAuthenticated: boolean }) => unknown) =>
    selector({ isAuthenticated: true }),
}));

const PROFILE = {
  id: "u1",
  steamId: "765611990000000",
  displayName: "tester",
  avatarUrl: null,
  accountAge: "1 gün",
  createdAt: new Date(0).toISOString(),
  reputationScore: null,
  completedTransactionCount: 0,
  successfulTransactionRate: null,
  cancelRate: null,
  sellerWalletAddress: null,
  refundWalletAddress: null,
  mobileAuthenticatorActive: false,
  steamTradeUrl: null,
};

const VALID = "https://steamcommunity.com/tradeoffer/new/?partner=1&token=abc";

function renderSection() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale="en" messages={messages}>
        <SteamTradeUrlSection />
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

async function submit(url = VALID) {
  const input = await screen.findByTestId("trade-url-input");
  fireEvent.change(input, { target: { value: url } });
  fireEvent.click(screen.getByTestId("trade-url-save"));
}

describe("SteamTradeUrlSection (F1)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    getMyProfile.mockResolvedValue(PROFILE);
  });
  afterEach(() => cleanup());

  it("MA aktif → başarı mesajı, U17 gövdesi tradeUrl alanıyla gider", async () => {
    updateSteamTradeUrl.mockResolvedValue({
      tradeUrl: VALID,
      mobileAuthenticatorActive: true,
      setupGuideUrl: null,
    });

    renderSection();
    await submit();

    await waitFor(() =>
      expect(screen.getByText(messages.settings.steamTradeUrl.resultActive)).toBeTruthy(),
    );
    expect(updateSteamTradeUrl).toHaveBeenCalledWith(VALID);
  });

  it("MA kapalı (setupGuideUrl DOLU) → kurulum rehberi linkiyle uyarı", async () => {
    updateSteamTradeUrl.mockResolvedValue({
      tradeUrl: VALID,
      mobileAuthenticatorActive: false,
      setupGuideUrl: "https://help.steampowered.com/guide",
    });

    renderSection();
    await submit();

    await waitFor(() =>
      expect(screen.getByText(messages.settings.steamTradeUrl.resultInactive)).toBeTruthy(),
    );
    // MaMessageConflatesAbsentWithYoungAuthenticator — Steam, "MA yok" ile
    // "MA 7 günden genç" durumlarını AYNI bekletme sayısıyla bildiriyor, yani kod
    // ikisini ayıramıyor. Metin bu yüzden ikisini de adlandırmak zorunda: yalnız
    // "etkinleştir" diyen bir uyarı, MA'si zaten aktif olan kullanıcıya
    // yapamayacağı bir şeyi söyler (2026-08-28'de gerçek bir hesapta ölçüldü).
    const inactiveCopy = messages.settings.steamTradeUrl.resultInactive;
    expect(inactiveCopy).toMatch(/not enabled/i);
    expect(inactiveCopy).toMatch(/less than 7 days/i);
    const guide = screen.getByText(messages.settings.steamTradeUrl.setupGuide);
    expect(guide.closest("a")?.getAttribute("href")).toBe("https://help.steampowered.com/guide");
  });

  it("Steam erişilemez (setupGuideUrl NULL) → pending mesajı, sessiz başarı DEĞİL", async () => {
    // 503 + success:true zarfı — apiClient fırlatmaz, gövde "MA kapalı" ile
    // birebir aynı görünür; ayrımı yapan tek şey setupGuideUrl'ün null olması.
    updateSteamTradeUrl.mockResolvedValue({
      tradeUrl: VALID,
      mobileAuthenticatorActive: false,
      setupGuideUrl: null,
    });

    renderSection();
    await submit();

    await waitFor(() =>
      expect(screen.getByText(messages.settings.steamTradeUrl.resultPending)).toBeTruthy(),
    );
    // Başarı mesajı GÖSTERİLMEMELİ — kullanıcı doğrulanmadığını bilmeli.
    expect(screen.queryByText(messages.settings.steamTradeUrl.resultActive)).toBeNull();
    // "MA kapalı" mesajı da gösterilmemeli: Steam bunu söylemedi, susmuş olabilir.
    expect(screen.queryByText(/does not appear to be active/)).toBeNull();
  });

  it("INVALID_TRADE_URL → alan işaretlenir ve hata satır içinde gösterilir", async () => {
    updateSteamTradeUrl.mockRejectedValue(
      new ApiError({ code: "INVALID_TRADE_URL", message: "bad", details: null }, "trace", 422),
    );

    renderSection();
    await submit("https://steamcommunity.com/tradeoffer/new/?partner=bozuk");

    await waitFor(() =>
      expect(screen.getByRole("alert").textContent).toContain("Invalid trade URL"),
    );
    expect(screen.getByTestId("trade-url-input").getAttribute("aria-invalid")).toBe("true");
  });

  it("beklenmeyen hata INVALID ile karıştırılmaz", async () => {
    updateSteamTradeUrl.mockRejectedValue(
      new ApiError({ code: "INTERNAL_ERROR", message: "boom", details: null }, "trace", 500),
    );

    renderSection();
    await submit();

    await waitFor(() =>
      expect(screen.getByText(messages.settings.steamTradeUrl.resultError)).toBeTruthy(),
    );
    expect(screen.getByTestId("trade-url-input").getAttribute("aria-invalid")).toBeNull();
  });

  it("kayıtlı URL alanı doldurur ve MA rozeti profilden gelir", async () => {
    getMyProfile.mockResolvedValue({
      ...PROFILE,
      steamTradeUrl: VALID,
      mobileAuthenticatorActive: true,
    });

    renderSection();

    await waitFor(() =>
      expect((screen.getByTestId("trade-url-input") as HTMLInputElement).value).toBe(VALID),
    );
    expect(screen.getByTestId("ma-status").getAttribute("data-active")).toBe("true");
  });
});
