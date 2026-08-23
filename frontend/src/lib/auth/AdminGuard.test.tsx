import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import messages from "@/i18n/messages/en.json";
import { ApiError } from "@/lib/api/client";
import { ACCESS_TOKEN_STORAGE_KEY } from "@/lib/stores/auth-store";
import { AdminGuard } from "./AdminGuard";

/**
 * F3c — "cevap alamadım" ile "yetkin yok" ayrımı.
 *
 * Bu dosyanın var oluş nedeni ölçülmüş bir davranış: rate limit kovası dolunca
 * `/auth/me` 429 dönüyordu, guard bunu `isError` olarak okuyup süper admini
 * hiçbir hata göstermeden kullanıcı paneline atıyordu. Ekran "admin değilsin"
 * diyordu, sistem ise "şu an bilmiyorum" demeliydi
 * (`UITour-AuthBucketIncludesSessionReads`).
 *
 * Asıl iddia yönlendirmenin YAPILMAMASI olduğu için testler `router.replace`
 * çağrılarını sayıyor — "ekranda bir şey var mı" demek yetmez, kullanıcının
 * dışarı atılmadığını göstermek gerekir.
 */

const { replace } = vi.hoisted(() => ({ replace: vi.fn() }));

// `next/navigation` bütünüyle taklit edilir: `@/components/common` barrel'ı
// LanguageSelector üzerinden next-intl'in navigation katmanını çekiyor ve o
// katman `redirect`/`permanentRedirect`'i modül yüklenirken sarmalıyor. Yalnız
// `useRouter` vermek modülü import anında düşürüyordu.
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), back: vi.fn(), refresh: vi.fn() }),
  usePathname: () => "/en/admin/dashboard",
  useSearchParams: () => new URLSearchParams(),
  useParams: () => ({ locale: "en" }),
  redirect: vi.fn(),
  permanentRedirect: vi.fn(),
  notFound: vi.fn(),
}));

const { getMe } = vi.hoisted(() => ({ getMe: vi.fn() }));
vi.mock("@/lib/api/auth", () => ({ getMe }));

function renderGuard() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale="en" messages={messages}>
        <AdminGuard>
          <div data-testid="admin-content">admin</div>
        </AdminGuard>
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

describe("AdminGuard (F3c)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    window.localStorage.setItem(ACCESS_TOKEN_STORAGE_KEY, "token");
  });
  afterEach(() => {
    cleanup();
    window.localStorage.clear();
  });

  it("429 → kullanıcı DIŞARI ATILMAZ, rate-limit mesajı gösterilir", async () => {
    getMe.mockRejectedValue(
      new ApiError({ code: "RATE_LIMIT_EXCEEDED", message: "slow", details: null }, "t", 429),
    );

    renderGuard();

    await waitFor(() =>
      expect(screen.getByText(messages.adminGuard.rateLimitedTitle)).toBeTruthy(),
    );
    // Asıl iddia: yönlendirme YOK.
    expect(replace).not.toHaveBeenCalled();
    expect(screen.queryByTestId("admin-content")).toBeNull();
  });

  it("500 → yine dışarı atılmaz, genel hata gösterilir", async () => {
    getMe.mockRejectedValue(
      new ApiError({ code: "INTERNAL_ERROR", message: "boom", details: null }, "t", 500),
    );

    renderGuard();

    await waitFor(() =>
      expect(screen.getByText(messages.adminGuard.unavailableTitle)).toBeTruthy(),
    );
    expect(replace).not.toHaveBeenCalled();
  });

  it("401 → oturum gerçekten yok, ana sayfaya yönlendirilir", async () => {
    getMe.mockRejectedValue(
      new ApiError({ code: "UNAUTHORIZED", message: "no", details: null }, "t", 401),
    );

    renderGuard();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/en"));
    expect(screen.queryByText(messages.adminGuard.rateLimitedTitle)).toBeNull();
  });

  it("admin değil → kullanıcı paneline yönlendirilir (eski davranış korunur)", async () => {
    getMe.mockResolvedValue({ role: "user", mobileAuthenticatorActive: true });

    renderGuard();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/en/dashboard"));
    expect(screen.queryByTestId("admin-content")).toBeNull();
  });

  it("admin → içerik render edilir, yönlendirme yok", async () => {
    getMe.mockResolvedValue({ role: "super_admin", mobileAuthenticatorActive: true });

    renderGuard();

    await waitFor(() => expect(screen.getByTestId("admin-content")).toBeTruthy());
    expect(replace).not.toHaveBeenCalled();
  });

  it("token yok → ana sayfaya, istek hiç yapılmaz", async () => {
    window.localStorage.clear();

    renderGuard();

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/en"));
    expect(getMe).not.toHaveBeenCalled();
  });
});
