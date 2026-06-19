import { createNavigation } from "next-intl/navigation";
import { routing } from "./routing";

/**
 * Locale-aware navigation APIs (next-intl v4). Using these instead of raw
 * `next/navigation` keeps the locale segment correct and lets `useRouter`
 * switch locale with a soft navigation (`router.replace(path, { locale })`)
 * rather than a full page reload.
 */
export const { Link, redirect, usePathname, useRouter, getPathname } = createNavigation(routing);

export const LOCALE_COOKIE = "NEXT_LOCALE";

/**
 * Persist the chosen locale in the next-intl `NEXT_LOCALE` cookie (WP13 —
 * replaces the legacy localStorage `preferredLocale`, which had no readers).
 * The middleware honours this cookie on non-prefixed entry points and future
 * visits. 1-year, lax — mirrors next-intl's own cookie defaults.
 */
export function setLocaleCookie(locale: string): void {
  if (typeof document === "undefined") return;
  document.cookie = `${LOCALE_COOKIE}=${locale}; path=/; max-age=31536000; samesite=lax`;
}
