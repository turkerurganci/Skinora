import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import messages from "@/i18n/messages/en.json";
import type { AdminUserListItem, AdminUserListResponse } from "@/lib/api/admin";
import AdminUsersPage from "./page";

/**
 * F5 — `UITour-AdminUsersPageIsStub`.
 *
 * The load-bearing assertion in this file is the SCOPE HINT, not the table.
 * `AdminUserService.ListAsync` answers two different questions depending on
 * whether a search term is present: with no term it returns only users holding
 * an active admin-role assignment, and with one it broadens to every
 * non-deactivated user. An admin who does not know that reads the default list
 * as "the platform has four users" — a wrong conclusion drawn from a correct
 * screen. The hint is the only thing standing between those two readings, and
 * it is a one-line ternary that a refactor could drop without breaking
 * anything visible.
 */

const { replace } = vi.hoisted(() => ({ replace: vi.fn() }));
let currentParams = new URLSearchParams();

// See AdminGuard.test.tsx: the `@/components/common` barrel pulls next-intl's
// navigation layer through LanguageSelector, and that layer wraps
// redirect/permanentRedirect at module load — a partial mock drops the module.
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), back: vi.fn(), refresh: vi.fn() }),
  usePathname: () => "/en/admin/users",
  useSearchParams: () => currentParams,
  useParams: () => ({ locale: "en" }),
  redirect: vi.fn(),
  permanentRedirect: vi.fn(),
  notFound: vi.fn(),
}));

const { useAdminUsers, useAdminRoles } = vi.hoisted(() => ({
  useAdminUsers: vi.fn(),
  useAdminRoles: vi.fn(),
}));
vi.mock("@/lib/hooks/useAdminUsers", () => ({ useAdminUsers }));
vi.mock("@/lib/hooks/useAdminRoles", () => ({ useAdminRoles }));

// `AdminUsersDirectoryPermissionMismatch` — the page now reads the caller's own
// permissions, because AD15 (this list) accepts VIEW_USERS or MANAGE_ROLES while
// AD11 (the role list feeding the role filter) still demands MANAGE_ROLES.
const { getMe } = vi.hoisted(() => ({ getMe: vi.fn() }));
vi.mock("@/lib/api/auth", () => ({ getMe }));

function setMe(permissions: string[]) {
  getMe.mockResolvedValue({ role: "admin", permissions });
}

const USER: AdminUserListItem = {
  id: "11111111-1111-1111-1111-111111111111",
  steamId: "76561199053273410",
  displayName: "moderator one",
  avatarUrl: null,
  role: { id: "role-1", name: "Moderator" },
};

function page(items: AdminUserListItem[], totalCount = items.length): AdminUserListResponse {
  return { items, totalCount, page: 1, pageSize: 20 };
}

function setList(data: AdminUserListResponse | undefined, extra: Record<string, unknown> = {}) {
  useAdminUsers.mockReturnValue({
    data,
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
    ...extra,
  });
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <NextIntlClientProvider locale="en" messages={messages}>
        <AdminUsersPage />
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  replace.mockClear();
  getMe.mockReset();
  useAdminRoles.mockClear();
  setMe(["MANAGE_ROLES"]);
  currentParams = new URLSearchParams();
  setList(page([USER]));
  useAdminRoles.mockReturnValue({
    data: { roles: [{ id: "role-1", name: "Moderator" }], availablePermissions: [] },
  });
});

afterEach(cleanup);

describe("AdminUsersPage — kapsam ipucu", () => {
  it("arama yokken listenin YALNIZ rol atanmış kullanıcıları gösterdiğini söyler", () => {
    renderPage();

    expect(screen.getByText(messages.adminUsers.scope.browse)).toBeInTheDocument();
    expect(screen.queryByText(messages.adminUsers.scope.search)).not.toBeInTheDocument();
  });

  it("arama varken kapsamın tüm kullanıcılara açıldığını söyler", () => {
    currentParams = new URLSearchParams("search=moder");
    renderPage();

    expect(screen.getByText(messages.adminUsers.scope.search)).toBeInTheDocument();
    expect(screen.queryByText(messages.adminUsers.scope.browse)).not.toBeInTheDocument();
  });

  it("boş sonuçta gerekçeyi kipe göre ayırır", () => {
    setList(page([]));
    const { unmount } = renderPage();
    expect(screen.getByText(messages.adminUsers.empty.browseDescription)).toBeInTheDocument();
    unmount();

    currentParams = new URLSearchParams("search=yok");
    renderPage();
    expect(screen.getByText(messages.adminUsers.empty.searchDescription)).toBeInTheDocument();
  });
});

describe("AdminUsersPage — sorgu ve URL", () => {
  it("okunan search/roleId/page değerlerini AD15 sorgusuna geçirir", () => {
    currentParams = new URLSearchParams("search=moder&roleId=role-1&page=3");
    renderPage();

    expect(useAdminUsers).toHaveBeenCalledWith({
      search: "moder",
      roleId: "role-1",
      page: 3,
      pageSize: 20,
    });
  });

  it("geçersiz page değerinde 1'e döner — sunucuya çöp gitmez", () => {
    currentParams = new URLSearchParams("page=-4");
    renderPage();

    expect(useAdminUsers).toHaveBeenCalledWith({
      search: undefined,
      roleId: undefined,
      page: 1,
      pageSize: 20,
    });
  });

  it("filtre uygulanınca URL'e yazar ve sayfayı 1'e sıfırlar", () => {
    currentParams = new URLSearchParams("page=5");
    renderPage();

    fireEvent.change(screen.getByRole("textbox"), { target: { value: "moder" } });
    fireEvent.click(screen.getByRole("button", { name: messages.filterBar.apply }));

    expect(replace).toHaveBeenCalledWith("/en/admin/users?search=moder");
  });

  it("rol filtresinin seçeneklerini AD11'den doldurur", async () => {
    renderPage();
    // `findBy` because the filter waits for the caller's own permissions — it
    // is rendered only once we know AD11 is readable for them.
    expect(await screen.findByRole("option", { name: "Moderator" })).toBeInTheDocument();
  });
});

describe("AdminUsersPage — iki uç, iki yetki", () => {
  /** Waits for the `me` query to resolve and re-render the page. */
  async function settle() {
    await waitFor(() => expect(useAdminRoles.mock.calls.length).toBeGreaterThan(1));
  }

  it("MANAGE_ROLES taşıyan admin için AD11 sorgusunu açar ve filtreyi gösterir", async () => {
    renderPage();

    expect(await screen.findByRole("option", { name: "Moderator" })).toBeInTheDocument();
    expect(useAdminRoles).toHaveBeenLastCalledWith({ enabled: true });
  });

  it("yalnız VIEW_USERS taşıyan admin'e rol filtresini göstermez, AD11'i hiç açmaz", async () => {
    // `AdminUsersDirectoryPermissionMismatch`: this admin legitimately reaches
    // the directory now, but AD11 still demands MANAGE_ROLES. A select with
    // permanently empty options would read as "there are no roles" — a wrong
    // conclusion drawn from a correct screen — and firing the request anyway
    // would put a red herring in the network log and the server's audit trail.
    setMe(["VIEW_USERS"]);
    renderPage();
    await settle();

    expect(useAdminRoles).toHaveBeenLastCalledWith({ enabled: false });
    expect(screen.queryByRole("option", { name: "Moderator" })).not.toBeInTheDocument();
    // The list itself still renders for them — that is the whole point of the
    // widened policy.
    expect(screen.getAllByText(USER.displayName).length).toBeGreaterThan(0);
  });
});

describe("AdminUsersPage — rol yönetimine köprü", () => {
  it("rol atamanın burada değil S19'da olduğunu bağlantıyla gösterir", () => {
    renderPage();
    expect(screen.getByTestId("manage-roles-link")).toHaveAttribute("href", "/en/admin/roles");
  });
});
