import type en from "@/i18n/messages/en.json";
import type { routing } from "@/i18n/routing";

/**
 * WP4 (T136-NoRouteOrMessageCompileGuard) — make a missing i18n key a
 * COMPILE error instead of a silent one.
 *
 * next-intl's default `getMessageFallback` prints the key path as plain text
 * and does not throw, so `t("nav.singOut")` used to ship a literal
 * "nav.singOut" to the user and no build, lint or test would say a word. The
 * existing `i18n:check` guard is a different guard: it compares the four
 * locale catalogues against each other, so it catches a key missing from `tr`
 * but never a key that exists in none of them because the code invented it.
 *
 * Declaring the catalogue on next-intl's `AppConfig` closes exactly that gap —
 * `useTranslations()` namespaces and `t()` keys are checked against the real
 * catalogue shape by `tsc`, which CI already runs as a blocking step. `en` is
 * the right source: `i18n:check` proves the other three carry an identical key
 * set, so typing against one types against all four.
 *
 * Augments `AppConfig` (next-intl v4), NOT a global `IntlMessages` interface —
 * that was the v3 mechanism and declaring it here compiles cleanly while
 * guarding nothing. Measured: with the global-interface version a deliberate
 * typo passed `tsc` silently; with this one it fails.
 */
declare module "next-intl" {
  interface AppConfig {
    Messages: typeof en;
    Locale: (typeof routing.locales)[number];
  }
}

export {};
