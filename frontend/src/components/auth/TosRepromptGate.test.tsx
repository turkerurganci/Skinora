import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import messages from "@/i18n/messages/en.json";
import { useAuthStore } from "@/lib/stores/auth-store";
import { TosRepromptGate, tosGateMode } from "./TosRepromptGate";

/**
 * T166 — the backend now rejects every gated endpoint with 403
 * `TOS_NOT_ACCEPTED` until the account accepts. The global gate must therefore
 * show the modal for a NOT-YET-ACCEPTED account too (before T166 it only
 * re-prompted on a version bump), or such an account is stuck: the callback
 * reports a returning user as `success` and never shows the modal again.
 */

const { replace, pathname } = vi.hoisted(() => ({
  replace: vi.fn(),
  pathname: { current: "/en/dashboard" },
}));

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace, push: vi.fn(), back: vi.fn(), refresh: vi.fn() }),
  usePathname: () => pathname.current,
  useSearchParams: () => new URLSearchParams(),
  useParams: () => ({ locale: "en" }),
  redirect: vi.fn(),
  permanentRedirect: vi.fn(),
  notFound: vi.fn(),
}));

// `logout` is the server half of the REAL signOut (lib/auth/signOut is not
// mocked): the decline path must go through the actual sign-out, which clears
// the auth store — T168 B5.
const { getMe, acceptTos, logoutRequest } = vi.hoisted(() => ({
  getMe: vi.fn(),
  acceptTos: vi.fn(),
  logoutRequest: vi.fn(),
}));
vi.mock("@/lib/api/auth", () => ({ getMe, acceptTos, logout: logoutRequest }));

// T168 — the decline path reuses the settings account-management section.
const { deleteAccount, deactivateAccount } = vi.hoisted(() => ({
  deleteAccount: vi.fn(),
  deactivateAccount: vi.fn(),
}));
vi.mock("@/lib/api/settings", () => ({
  DELETE_ACCOUNT_CONFIRMATION: "SİL",
  deleteAccount,
  deactivateAccount,
}));

function me(tosAccepted: boolean, tosAcceptedVersion: string | null) {
  return { tosAccepted, tosAcceptedVersion };
}

// jsdom has no modal dialog; the confirm modal only needs `open` to flip.
function installDialogPolyfill() {
  if (!HTMLDialogElement.prototype.showModal) {
    HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
      this.open = true;
    };
    HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
      this.open = false;
    };
  }
}

function gateTree(client: QueryClient) {
  return (
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale="en" messages={messages}>
        <TosRepromptGate />
      </NextIntlClientProvider>
    </QueryClientProvider>
  );
}

function renderGate(client = new QueryClient({ defaultOptions: { queries: { retry: false } } })) {
  const result = render(gateTree(client));
  return { ...result, client };
}

describe("tosGateMode", () => {
  it("not accepted → first", () => {
    expect(tosGateMode(me(false, null), "1.0")).toBe("first");
    // A ToS stamp without the 18+ stamp is reported by /auth/me as not accepted.
    expect(tosGateMode(me(false, "1.0"), "1.0")).toBe("first");
  });
  it("accepted on an older version → reprompt", () => {
    expect(tosGateMode(me(true, "0.9"), "1.0")).toBe("reprompt");
  });
  it("accepted on the current version → nothing", () => {
    expect(tosGateMode(me(true, "1.0"), "1.0")).toBeNull();
  });
  it("no session data → nothing", () => {
    expect(tosGateMode(undefined, "1.0")).toBeNull();
  });
});

describe("TosRepromptGate (T166)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    replace.mockReset();
    pathname.current = "/en/dashboard";
    useAuthStore.getState().setAccessToken("token");
  });
  afterEach(() => {
    cleanup();
    useAuthStore.getState().logout();
  });

  it("shows the first-time modal for an account that has not accepted", async () => {
    getMe.mockResolvedValue(me(false, null));

    renderGate();

    await waitFor(() => expect(screen.getByText(messages.auth.tos.title)).toBeTruthy());
    // First-time prompt offers the "not eligible" exit; the age-gate route is the target.
    fireEvent.click(screen.getByText(messages.auth.tos.notEligible));
    expect(replace).toHaveBeenCalledWith("/en/auth/age-gate");
  });

  it("accepting calls the API with the current version", async () => {
    getMe.mockResolvedValue(me(false, null));
    acceptTos.mockResolvedValue({ accepted: true, acceptedAt: "2026-10-21T00:00:00Z" });

    renderGate();

    await waitFor(() => expect(screen.getByTestId("tos-submit")).toBeTruthy());
    fireEvent.click(screen.getByTestId("tos-age-checkbox"));
    fireEvent.click(screen.getByTestId("tos-accept-checkbox"));
    fireEvent.click(screen.getByTestId("tos-submit"));

    await waitFor(() => expect(acceptTos).toHaveBeenCalledWith("1.0"));
  });

  it("stays dormant on the callback page (it shows its own modal)", async () => {
    pathname.current = "/en/auth/callback";
    getMe.mockResolvedValue(me(false, null));

    renderGate();

    await waitFor(() => expect(getMe).toHaveBeenCalled());
    expect(screen.queryByTestId("tos-submit")).toBeNull();
  });

  it("stays dormant on the terms page so the terms stay readable", async () => {
    pathname.current = "/tr/terms";
    getMe.mockResolvedValue(me(false, null));

    renderGate();

    await waitFor(() => expect(getMe).toHaveBeenCalled());
    expect(screen.queryByTestId("tos-submit")).toBeNull();
  });

  it("T168 — first-time: declining the terms offers account closure and deletion", async () => {
    installDialogPolyfill();
    getMe.mockResolvedValue(me(false, null));
    deleteAccount.mockResolvedValue({ deletedAt: "2026-10-21T00:00:00Z", message: "ok" });

    renderGate();

    await waitFor(() => expect(screen.getByTestId("tos-decline-toggle")).toBeTruthy());
    expect(screen.queryByTestId("tos-decline-panel")).toBeNull();
    fireEvent.click(screen.getByTestId("tos-decline-toggle"));

    const panel = screen.getByTestId("tos-decline-panel");
    expect(panel).toHaveTextContent(messages.auth.tos.decline.description);
    expect(panel).toHaveTextContent(messages.settings.accountManagement.deactivate.button);

    fireEvent.click(
      screen.getAllByText(messages.settings.accountManagement.delete.button)[0] as HTMLElement,
    );
    fireEvent.change(screen.getByPlaceholderText("SİL"), { target: { value: "SİL" } });
    // The opener and the confirm button share the label; confirm is the dialog's submit.
    fireEvent.click(document.querySelector("dialog button[type=submit]") as HTMLButtonElement);

    await waitFor(() => expect(deleteAccount).toHaveBeenCalledWith("SİL"));
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/en"));
    expect(acceptTos).not.toHaveBeenCalled();
  });

  it("T168 — re-prompt (new version): the modal blocks /settings, so declining offers closure and deletion here too", async () => {
    installDialogPolyfill();
    // The re-prompt covers every non-dormant route, the settings page included.
    pathname.current = "/en/settings";
    getMe.mockResolvedValue(me(true, "0.9"));
    deactivateAccount.mockResolvedValue({ deactivatedAt: "2026-10-22T00:00:00Z", message: "ok" });

    renderGate();

    await waitFor(() => expect(screen.getByText(messages.auth.tos.reprompt.title)).toBeTruthy());
    fireEvent.click(screen.getByTestId("tos-decline-toggle"));

    const panel = screen.getByTestId("tos-decline-panel");
    expect(panel).toHaveTextContent(messages.auth.tos.decline.description);
    expect(panel).toHaveTextContent(messages.settings.accountManagement.delete.button);

    fireEvent.click(screen.getByText(messages.settings.accountManagement.deactivate.button));
    fireEvent.click(document.querySelector("dialog button[type=submit]") as HTMLButtonElement);

    await waitFor(() => expect(deactivateAccount).toHaveBeenCalled());
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/en"));
    expect(acceptTos).not.toHaveBeenCalled();
  });

  /*
   * T168 B5 — after closing / deleting the account from the modal the session
   * is gone (real signOut → store token null) but the `["auth","me"]` cache
   * still holds the old answer (not accepted / old version). The gate lives in
   * the locale layout, so it survives the `/settings` → `/en` navigation; it
   * must close with the session, not with a page reload.
   */
  it.each([
    { label: "first-time", meValue: me(false, null), action: "delete" as const },
    { label: "re-prompt", meValue: me(true, "0.9"), action: "deactivate" as const },
  ])(
    "T168 B5 — $label: after $action + sign-out the modal closes and no 'accept the terms' prompt is left",
    async ({ meValue, action }) => {
      installDialogPolyfill();
      pathname.current = "/en/settings";
      getMe.mockResolvedValue(meValue);
      logoutRequest.mockResolvedValue(undefined);
      deleteAccount.mockResolvedValue({ deletedAt: "2026-10-22T00:00:00Z", message: "ok" });
      deactivateAccount.mockResolvedValue({ deactivatedAt: "2026-10-22T00:00:00Z", message: "ok" });
      replace.mockImplementation((to: string) => {
        pathname.current = to;
      });

      const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
      // The stale answer AuthInitializer left in the cache before the modal opened.
      client.setQueryData(["auth", "me"], meValue);
      const { rerender } = renderGate(client);

      await waitFor(() => expect(screen.getByTestId("tos-submit")).toBeTruthy());
      fireEvent.click(screen.getByTestId("tos-decline-toggle"));
      if (action === "delete") {
        fireEvent.click(
          screen.getAllByText(messages.settings.accountManagement.delete.button)[0] as HTMLElement,
        );
        fireEvent.change(screen.getByPlaceholderText("SİL"), { target: { value: "SİL" } });
      } else {
        fireEvent.click(screen.getByText(messages.settings.accountManagement.deactivate.button));
      }
      fireEvent.click(document.querySelector("dialog button[type=submit]") as HTMLButtonElement);

      // The real sign-out ran: server revoke + local token cleared, then home.
      await waitFor(() => expect(logoutRequest).toHaveBeenCalled());
      await waitFor(() => expect(replace).toHaveBeenCalledWith("/en"));
      expect(useAuthStore.getState().accessToken).toBeNull();

      rerender(gateTree(client));

      await waitFor(() => expect(screen.queryByTestId("tos-submit")).toBeNull());
      expect(screen.queryByText(messages.auth.tos.title)).toBeNull();
      expect(screen.queryByText(messages.auth.tos.reprompt.title)).toBeNull();
      expect(screen.queryByTestId("tos-decline-toggle")).toBeNull();
      // The stale answer is dropped too: the next sign-in fetches its own.
      expect(client.getQueryData(["auth", "me"])).toBeUndefined();
      expect(acceptTos).not.toHaveBeenCalled();
    },
  );

  it("renders nothing for an account on the current version", async () => {
    getMe.mockResolvedValue(me(true, "1.0"));

    renderGate();

    await waitFor(() => expect(getMe).toHaveBeenCalled());
    expect(screen.queryByTestId("tos-submit")).toBeNull();
  });
});
