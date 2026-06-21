/**
 * Widen a bare `yyyy-mm-dd` end-date to the last instant of that day.
 *
 * A native `<input type="date">` emits `yyyy-mm-dd`, which binds to 00:00. The
 * admin list backends filter `CreatedAt <= dateTo` inclusively of the exact
 * instant, so an un-widened `dateTo` excludes every event on the selected end
 * day (04 §8.10 date-range filter). Returning `${value}T23:59:59.999` (local,
 * no `Z` — matching how `dateFrom`'s bare date binds to local 00:00) makes the
 * upper bound inclusive of the whole day.
 *
 * Falsy input returns `undefined` so it matches the API client's
 * `if (query.dateTo)` truthy guard (an empty string must not become a bound).
 */
export function toEndOfDay(value: string | undefined): string | undefined {
  return value ? `${value}T23:59:59.999` : undefined;
}
