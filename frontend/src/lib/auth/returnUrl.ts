import { routing } from "@/i18n/routing";

/** App-relative path: starts with a single "/", no scheme, no protocol-relative "//host". */
const RELATIVE_PATH_RE = /^\/(?!\/)[^?#]*(\?[^#]*)?(#.*)?$/;

/** First path segment of an app-relative URL ("/tr/dashboard?x=1" → "tr"). */
function firstSegment(path: string): string {
  const withoutLeadingSlash = path.startsWith("/") ? path.slice(1) : path;
  return withoutLeadingSlash.split(/[/?#]/)[0];
}

/** Does `path` already start with one of the four supported locales? */
export function hasLocalePrefix(path: string): boolean {
  return (routing.locales as readonly string[]).includes(firstSegment(path));
}

/**
 * Normalises a post-login return URL to a **locale-prefixed, app-relative**
 * path. The result is always ready to navigate to as-is — callers must not
 * prefix a locale again.
 *
 * F4a made this locale-aware because the value is not just a destination: A1
 * stores it in the `skinora_oid_rt` cookie and the Steam callback derives a
 * brand-new user's `PreferredLanguage` from its first segment
 * (`SupportedLanguages.FromPathPrefix`), which decides the language of every
 * notification that user will ever receive.
 *
 * F4b moved it here from two private copies. The login page and the callback
 * page each had their own; when F4a taught one of them about locales the other
 * kept unconditionally prefixing its own, and a Turkish login landed on
 * `/tr/tr/dashboard` → a 404. One function, one rule, one place.
 *
 * - no value / not app-relative → `/{locale}/dashboard`
 * - app-relative without a locale → current locale prefixed
 * - app-relative with a locale → returned untouched (explicit destination wins)
 */
export function sanitizeReturnUrl(raw: string | null | undefined, locale: string): string {
  const fallback = `/${locale}/dashboard`;
  if (!raw || !RELATIVE_PATH_RE.test(raw)) return fallback;
  if (hasLocalePrefix(raw)) return raw;
  return `/${locale}${raw}`;
}
