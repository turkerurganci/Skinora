/**
 * Terms that stay in English across all four locales — 04 §10.4.
 *
 * Translators and devs reference this list when adding new copy. The values
 * are emitted verbatim into the four locale JSON files (`en/zh/es/tr.json`)
 * with no translation. Adding a new term here means "this string must read
 * the same in every locale".
 */

export const UNTRANSLATABLE_TERMS = [
  // Stablecoins / blockchain (04 §10.4)
  "USDT",
  "USDC",
  "TRC-20",
  "Tron",
  // Steam (04 §10.4)
  "Steam",
  "Steam ID",
  "Mobile Authenticator",
  // Trading (04 §10.4)
  "Trade offer",
  "CS2",
  "Gas fee",
] as const;

export type UntranslatableTerm = (typeof UNTRANSLATABLE_TERMS)[number];

/**
 * Case-insensitive check: is `term` one of the untranslatable tokens?
 *
 * Lint/CI tooling can call this against translated strings to catch
 * accidental translations (e.g. a "Steam ID" rendered as "Steam Kimliği"
 * in `tr.json`).
 */
export function isUntranslatable(term: string): boolean {
  const lower = term.trim().toLowerCase();
  return UNTRANSLATABLE_TERMS.some((t) => t.toLowerCase() === lower);
}
