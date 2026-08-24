/**
 * WP4 (T136-NoRouteOrMessageCompileGuard) — the single, deliberate escape
 * hatch from the compile-time message-key check.
 *
 * `src/types/next-intl.d.ts` types `t()` against the real catalogue, so a typo
 * is now a build error. That check can only see keys known at compile time,
 * and a handful of call sites legitimately build the key at runtime — an audit
 * action name, an API error code, a settings group id, a dispute type. Those
 * would otherwise each need their own inline cast, which is exactly how a
 * guard turns into 30 invisible holes.
 *
 * Routing them through one named helper keeps the hatch **countable**: a single
 * `grep tDynamic` lists every place the catalogue is consulted without the
 * compiler's help, and each of those places is forced to name its fallback.
 *
 * The fallback is not optional on purpose. next-intl's default behaviour for a
 * missing key is to render the key path as if it were copy, which is precisely
 * the silent failure this whole guard exists to remove; here the caller has to
 * decide what the user sees instead.
 */

/**
 * The dynamic-key shape of a next-intl translator. Deliberately structural and
 * local — the typed `Translator` cannot express a `string` key, and that is the
 * point of the check we are stepping around.
 */
type DynamicTranslator = {
  (key: string): string;
  has(key: string): boolean;
};

/**
 * Looks up a runtime-computed message key, returning `fallback` when the
 * catalogue has no entry for it.
 *
 * @param t A translator from `useTranslations(...)`.
 * @param key The runtime key, relative to that translator's namespace.
 * @param fallback Rendered when the key is absent — a translated string, or the
 *   raw value when showing the value itself is more useful than a generic
 *   message (an unmapped audit action, for instance).
 */
export function tDynamic(t: unknown, key: string, fallback: string): string {
  const dynamic = t as DynamicTranslator;
  return dynamic.has(key) ? dynamic(key) : fallback;
}

/**
 * Same lookup, but for the sites that render a key the catalogue is expected to
 * always contain (an enum member, a fixed section list). Missing entries fall
 * back to the key path — next-intl's own default — because these paths have no
 * meaningful alternative text to show.
 *
 * Prefer {@link tDynamic}: reach for this only when there is genuinely nothing
 * better to render than the key itself.
 */
export function tDynamicOrKey(t: unknown, key: string): string {
  return tDynamic(t, key, key);
}
