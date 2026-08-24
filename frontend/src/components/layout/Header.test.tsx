import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import messages from "@/i18n/messages/tr.json";
import { Header } from "./Header";

/**
 * F7a — `Session-NoSignOutInMainHeader`.
 *
 * Sıradan kullanıcı oturumunu arayüzden kapatamıyordu: `logout()`'un üç
 * çağıranı da bu başlığın DIŞINDAYDI (admin başlığı · askıya alınmış başlık ·
 * hesap kapatma sonrası). Altyapı hazırdı — `nav.signOut` dört dilde vardı —
 * eksik olan yalnız düğmeydi. Ortak bilgisayarda gerçek bir sorun.
 *
 * Testler düğmenin VARLIĞINI değil, TIKLANINCA NE OLDUĞUNU ölçüyor: signOut
 * çağrılıyor mu ve kullanıcı oturumdan çıkarılıp ana sayfaya gönderiliyor mu.
 */

const { replace } = vi.hoisted(() => ({ replace: vi.fn() }));
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn() }),
  usePathname: () => "/tr/dashboard",
  useSearchParams: () => new URLSearchParams(),
  useParams: () => ({ locale: "tr" }),
  redirect: vi.fn(),
  permanentRedirect: vi.fn(),
  notFound: vi.fn(),
}));

const { signOut } = vi.hoisted(() => ({ signOut: vi.fn() }));
vi.mock("@/lib/auth/signOut", () => ({ signOut }));

vi.mock("@/lib/stores/auth-store", () => ({
  useAuthStore: (selector: (s: Record<string, unknown>) => unknown) =>
    selector({ displayName: "tester", avatarUrl: null }),
}));

function renderHeader() {
  return render(
    <NextIntlClientProvider locale="tr" messages={messages}>
      <Header />
    </NextIntlClientProvider>,
  );
}

beforeEach(() => {
  replace.mockClear();
  signOut.mockReset();
  signOut.mockResolvedValue(undefined);
});

afterEach(cleanup);

describe("Header — çıkış", () => {
  it("sıradan kullanıcı için çıkış düğmesi vardır", () => {
    renderHeader();
    expect(screen.getByTestId("sign-out")).toBeInTheDocument();
  });

  it("düğme dört dilde çevrilmiş metni taşır (tr: Çıkış)", () => {
    renderHeader();
    expect(screen.getByTestId("sign-out")).toHaveAttribute("aria-label", messages.nav.signOut);
  });

  it("tıklanınca signOut çağrılır — yalnız yerel temizlik değil", async () => {
    renderHeader();
    fireEvent.click(screen.getByTestId("sign-out"));
    await waitFor(() => expect(signOut).toHaveBeenCalledTimes(1));
  });

  it("çıkıştan SONRA ana sayfaya gönderilir, oturum içi bir sayfada bırakılmaz", async () => {
    renderHeader();
    fireEvent.click(screen.getByTestId("sign-out"));
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/tr/"));
  });
});
