import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import messages from "@/i18n/messages/en.json";
import AdminTransactionsPage from "./page";

/**
 * T157 — the network filter on the admin transaction list: URL-synced like the
 * stablecoin filter, and passed to AD6 as `network`.
 */

const { replace } = vi.hoisted(() => ({ replace: vi.fn() }));
let currentParams = new URLSearchParams();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), back: vi.fn(), refresh: vi.fn() }),
  usePathname: () => "/en/admin/transactions",
  useSearchParams: () => currentParams,
  useParams: () => ({ locale: "en" }),
  redirect: vi.fn(),
  permanentRedirect: vi.fn(),
  notFound: vi.fn(),
}));

const { useAdminTransactionList } = vi.hoisted(() => ({ useAdminTransactionList: vi.fn() }));
vi.mock("@/lib/hooks/useAdminTransactionList", () => ({ useAdminTransactionList }));

beforeEach(() => {
  currentParams = new URLSearchParams();
  useAdminTransactionList.mockReturnValue({
    data: { items: [], totalCount: 0, page: 1, pageSize: 20 },
    isLoading: false,
    isError: false,
    refetch: vi.fn(),
  });
});
afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

function renderPage() {
  return render(
    <NextIntlClientProvider locale="en" messages={messages}>
      <AdminTransactionsPage />
    </NextIntlClientProvider>,
  );
}

describe("AdminTransactionsPage — network filter", () => {
  it("offers both networks and writes the choice to the URL", () => {
    renderPage();

    const select = screen.getByLabelText("Network");
    const options = Array.from((select as HTMLSelectElement).options).map((o) => o.textContent);
    expect(options).toEqual(["All", "TRON (TRC-20)", "Ethereum (ERC-20)"]);

    fireEvent.change(select, { target: { value: "ETHEREUM" } });
    fireEvent.click(screen.getByRole("button", { name: "Apply" }));

    expect(replace).toHaveBeenCalledTimes(1);
    expect(replace.mock.calls[0][0]).toContain("network=ETHEREUM");
  });

  it("passes the URL's network to the list query and preselects it", () => {
    currentParams = new URLSearchParams("network=ETHEREUM");
    renderPage();

    expect(useAdminTransactionList).toHaveBeenCalledWith(
      expect.objectContaining({ network: "ETHEREUM" }),
    );
    expect(screen.getByLabelText("Network")).toHaveValue("ETHEREUM");
  });

  it("ignores an unknown network value instead of forwarding it", () => {
    currentParams = new URLSearchParams("network=SOLANA");
    renderPage();

    expect(useAdminTransactionList).toHaveBeenCalledWith(
      expect.objectContaining({ network: undefined }),
    );
  });
});
