import { describe, it, expect, afterEach } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import en from "@/i18n/messages/en.json";
import tr from "@/i18n/messages/tr.json";
import es from "@/i18n/messages/es.json";
import zh from "@/i18n/messages/zh.json";
import type { EligibilityResponse } from "@/lib/api/transactions";
import { EligibilityGate } from "./EligibilityGate";
import { POST_ERROR_CODES } from "./NewTransactionForm";

/**
 * 08 §2.2a — the seller-facing half of the Steam trade eligibility gate.
 *
 * Two things had no coverage at all before this file. First, the gate itself:
 * there was no test for this component, so every reason banner was a claim
 * nobody measured. Second, and the reason the round exists, the remaining-day
 * count: the number is computed on the server and now travels in the
 * eligibility envelope, and the only proof it reaches a screen is rendering
 * the banner and reading it back.
 *
 * The real locale files are used rather than stubs on purpose: a key the gate
 * references but no catalogue carries renders as its own dotted path, and
 * asserting on the actual copy catches that. next-intl does NOT type-check
 * interpolation values against JSON messages, so a forgotten `{days}` is
 * invisible to `tsc` — only a render can see it.
 */

const LOCALES = [
  { locale: "en", messages: en },
  { locale: "tr", messages: tr },
  { locale: "es", messages: es },
  { locale: "zh", messages: zh },
] as const;

function eligibility(overrides: Partial<EligibilityResponse> = {}): EligibilityResponse {
  return {
    eligible: false,
    mobileAuthenticatorActive: true,
    concurrentLimit: { current: 0, max: 5 },
    cancelCooldown: { active: false, expiresAt: null },
    newAccountLimit: { isNewAccount: false, current: null, max: null },
    ...overrides,
  };
}

function renderGate(
  value: EligibilityResponse,
  locale: (typeof LOCALES)[number]["locale"] = "en",
  messages: object = en,
) {
  return render(
    <NextIntlClientProvider locale={locale} messages={messages}>
      <EligibilityGate eligibility={value} />
    </NextIntlClientProvider>,
  );
}

afterEach(cleanup);

describe("EligibilityGate — Steam trade eligibility reasons", () => {
  it("shows the remaining day count when the account is inside the 15-day wait", () => {
    renderGate(eligibility({ reasons: ["STEAM_ACCOUNT_TOO_NEW"], steamAccountRemainingDays: 4 }));

    expect(screen.getByText(/Time left: 4 days/)).toBeInTheDocument();
  });

  it("uses the singular form on the last day", () => {
    // The server rounds up and floors at 1, so "1" is a real value the seller
    // will see — and "1 days" is the shape a plain `{days}` interpolation
    // would have produced.
    renderGate(eligibility({ reasons: ["STEAM_ACCOUNT_TOO_NEW"], steamAccountRemainingDays: 1 }));

    expect(screen.getByText(/Time left: 1 day\./)).toBeInTheDocument();
  });

  it("falls back to the countless text when the server sends no number", () => {
    renderGate(eligibility({ reasons: ["STEAM_ACCOUNT_TOO_NEW"] }));

    expect(screen.getByText(/first 15 days/)).toBeInTheDocument();
    expect(screen.queryByText(/Time left/)).not.toBeInTheDocument();
  });

  it("renders the day count in every locale", () => {
    // Not a translation check — a wiring check. A locale whose value lost the
    // placeholder would render the number nowhere, and a malformed ICU plural
    // would throw here rather than in production.
    for (const { locale, messages } of LOCALES) {
      const { unmount } = renderGate(
        eligibility({ reasons: ["STEAM_ACCOUNT_TOO_NEW"], steamAccountRemainingDays: 3 }),
        locale,
        messages,
      );

      expect(screen.getByText(/3/)).toBeInTheDocument();
      unmount();
    }
  });

  it("attaches no day count to the limited-account reason", () => {
    // The limited restriction lifts by spending, not by waiting. A number here
    // would name a deadline Steam never gave.
    renderGate(eligibility({ reasons: ["STEAM_ACCOUNT_LIMITED"] }));

    expect(screen.getByText(/US\$5/)).toBeInTheDocument();
    expect(screen.queryByText(/Time left/)).not.toBeInTheDocument();
  });

  it("shows the unreadable-answer reason as temporary, not as a refusal", () => {
    renderGate(eligibility({ reasons: ["STEAM_UNAVAILABLE"] }));

    expect(screen.getByText(/temporary/i)).toBeInTheDocument();
  });

  it("renders every blocking reason, not just the first", () => {
    // The endpoint's contract is the complete reason list, and the backend
    // deliberately keeps asking Steam even when a cheaper rule already blocks.
    // If the gate rendered only one banner, that decision would buy nothing.
    renderGate(
      eligibility({
        reasons: ["ACCOUNT_FLAGGED", "STEAM_ACCOUNT_TOO_NEW"],
        steamAccountRemainingDays: 2,
      }),
    );

    expect(screen.getByText("Account under review")).toBeInTheDocument();
    expect(screen.getByText(/Time left: 2 days/)).toBeInTheDocument();
  });

  it("renders nothing when the seller is eligible", () => {
    const { container } = renderGate(eligibility({ eligible: true }));

    expect(container).toBeEmptyDOMElement();
  });
});

describe("POST /transactions error codes", () => {
  const stepErrors = (messages: typeof en) =>
    Object.keys(messages.newTransaction.step4.errors).filter((key) => key !== "generic");

  it("recognises both Steam trade restrictions", () => {
    // These two shipped as catalogue strings with no code behind them: the set
    // omitted them, so a real 403 rendered the generic "something went wrong".
    expect(POST_ERROR_CODES.has("STEAM_ACCOUNT_LIMITED")).toBe(true);
    expect(POST_ERROR_CODES.has("STEAM_ACCOUNT_TOO_NEW")).toBe(true);
  });

  it("keeps the recognised codes and the catalogue strings symmetric", () => {
    // Both directions, because both failures are silent: a code with no string
    // renders its own dotted path, and a string with no code is dead weight the
    // seller never sees. Only the second one was caught here by a human.
    expect([...POST_ERROR_CODES].sort()).toEqual(stepErrors(en).sort());
  });

  it("carries every recognised code in all four locales", () => {
    for (const { locale, messages } of LOCALES) {
      expect(
        stepErrors(messages as typeof en).sort(),
        `locale ${locale} is missing a step-4 error string`,
      ).toEqual([...POST_ERROR_CODES].sort());
    }
  });
});
