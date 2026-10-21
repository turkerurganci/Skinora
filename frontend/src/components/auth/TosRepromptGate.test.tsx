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

const { getMe, acceptTos } = vi.hoisted(() => ({ getMe: vi.fn(), acceptTos: vi.fn() }));
vi.mock("@/lib/api/auth", () => ({ getMe, acceptTos }));

function me(tosAccepted: boolean, tosAcceptedVersion: string | null) {
  return { tosAccepted, tosAcceptedVersion };
}

function renderGate() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <NextIntlClientProvider locale="en" messages={messages}>
        <TosRepromptGate />
      </NextIntlClientProvider>
    </QueryClientProvider>,
  );
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

  it("renders nothing for an account on the current version", async () => {
    getMe.mockResolvedValue(me(true, "1.0"));

    renderGate();

    await waitFor(() => expect(getMe).toHaveBeenCalled());
    expect(screen.queryByTestId("tos-submit")).toBeNull();
  });
});
