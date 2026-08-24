import { getRequestConfig } from "next-intl/server";
import type { Locale } from "next-intl";
import { routing } from "./routing";

export default getRequestConfig(async ({ requestLocale }) => {
  const requested = await requestLocale;

  // WP4 — narrow to the Locale union rather than carrying a `string`. The
  // AppConfig augmentation (src/types/next-intl.d.ts) types the locale, so this
  // guard is now the single place an unrecognised value is turned into the
  // default; downstream code can rely on the type instead of re-checking.
  const locale: Locale = routing.locales.includes(requested as Locale)
    ? (requested as Locale)
    : routing.defaultLocale;

  return {
    locale,
    messages: (await import(`./messages/${locale}.json`)).default,
  };
});
