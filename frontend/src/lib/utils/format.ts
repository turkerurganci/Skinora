/**
 * Locale-aware display helpers for dates, times, numbers and stablecoin amounts.
 *
 * Source of truth: 04 §10.2–§10.4. Locale outputs are produced by
 * `Intl.DateTimeFormat` / `Intl.NumberFormat` which already match the spec
 * tables for the four supported locales (en, tr, es, zh).
 */

import { routing } from "@/i18n/routing";

export type SupportedLocale = (typeof routing.locales)[number];

function normalizeLocale(locale: string | undefined): SupportedLocale {
  const fallback = routing.defaultLocale;
  if (!locale) return fallback;
  return (routing.locales as readonly string[]).includes(locale)
    ? (locale as SupportedLocale)
    : fallback;
}

/**
 * Date only — 04 §10.2 row "Tarih Formatı". Produces "Mar 14, 2026" (en),
 * "14 Mar 2026" (tr/es), "2026年3月14日" (zh).
 */
export function formatDate(value: string | Date, locale?: string): string {
  return new Intl.DateTimeFormat(normalizeLocale(locale), {
    dateStyle: "medium",
  }).format(toDate(value));
}

/**
 * Time only — 04 §10.2 row "Saat Formatı". 12-hour with AM/PM for en,
 * 24-hour for tr/es/zh (Intl applies locale's hour-cycle automatically).
 */
export function formatTime(value: string | Date, locale?: string): string {
  return new Intl.DateTimeFormat(normalizeLocale(locale), {
    timeStyle: "short",
  }).format(toDate(value));
}

/**
 * Combined date + short time — used in lists, audit timestamps, modal headers.
 */
export function formatDateTime(value: string | Date, locale?: string): string {
  return new Intl.DateTimeFormat(normalizeLocale(locale), {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(toDate(value));
}

/**
 * Long date for headers/timeline labels. Intl `dateStyle:"long"` yields
 * "March 14, 2026" (en), "14 Mart 2026" (tr), "14 de marzo de 2026" (es),
 * "2026年3月14日" (zh).
 */
export function formatDateLong(value: string | Date, locale?: string): string {
  return new Intl.DateTimeFormat(normalizeLocale(locale), {
    dateStyle: "long",
  }).format(toDate(value));
}

/**
 * Relative recency label ("5 dakika önce", "2 saat önce") — backs the S18 bot
 * "Son Kontrol" field (04 §8.7). Computed against the current clock at render
 * time, so it is **non-deterministic across renders**: callers must be client
 * components (the S18 page loads via React Query, so the value only renders
 * client-side — no SSR hydration mismatch). For spans ≥ 24h the absolute
 * date+time is clearer than "3 gün önce", so it falls back to {@link formatDateTime}.
 * `now` is injectable for deterministic tests.
 */
export function formatRelativeTime(value: string | Date, locale?: string, now?: Date): string {
  const date = toDate(value);
  const ref = now ?? new Date();
  const diffSec = Math.round((date.getTime() - ref.getTime()) / 1000);
  const rtf = new Intl.RelativeTimeFormat(normalizeLocale(locale), { numeric: "auto" });
  if (Math.abs(diffSec) < 60) return rtf.format(diffSec, "second");
  const diffMin = Math.round(diffSec / 60);
  if (Math.abs(diffMin) < 60) return rtf.format(diffMin, "minute");
  const diffHour = Math.round(diffMin / 60);
  if (Math.abs(diffHour) < 24) return rtf.format(diffHour, "hour");
  return formatDateTime(date, locale);
}

/**
 * Locale-aware number formatting for **non-stablecoin** values — counters,
 * statistics, ranking. 04 §10.3 thousand/decimal separator table:
 *   en/zh → 1,234.56   tr/es → 1.234,56
 */
export function formatNumber(
  value: number,
  locale?: string,
  options?: Intl.NumberFormatOptions,
): string {
  return new Intl.NumberFormat(normalizeLocale(locale), options).format(value);
}

/**
 * Percentage display. Input is a percentage value (e.g. `99.5` → "99.5%"),
 * NOT a fraction. Decimal separator follows locale (04 §10.3).
 */
export function formatPercent(value: number, locale?: string, fractionDigits: number = 1): string {
  return `${formatNumber(value, locale, {
    minimumFractionDigits: 0,
    maximumFractionDigits: fractionDigits,
  })}%`;
}

/**
 * Stablecoin amount — **locale-invariant** per 04 §10.3 note:
 * "Stablecoin tutarları her zaman `.` (nokta) ile gösterilir — blockchain
 * standardı." Symbol stays English (USDT/USDC — 04 §10.4 untranslatable).
 *
 * Backend serializes amounts as decimal strings (e.g. "100.50000000"); those
 * are passed through verbatim. Client-computed numbers are formatted with a
 * fixed locale (`en-US`) so the decimal separator is always `.`.
 */
export function formatStablecoin(
  amount: string | number,
  symbol: string,
  options?: { fractionDigits?: number },
): string {
  if (typeof amount === "string") {
    return `${amount} ${symbol}`;
  }
  const fixed = new Intl.NumberFormat("en-US", {
    minimumFractionDigits: options?.fractionDigits ?? 2,
    maximumFractionDigits: options?.fractionDigits ?? 2,
    useGrouping: false,
  }).format(amount);
  return `${fixed} ${symbol}`;
}

function toDate(value: string | Date): Date {
  return value instanceof Date ? value : new Date(value);
}
