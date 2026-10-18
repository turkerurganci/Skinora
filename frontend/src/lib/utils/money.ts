/**
 * Client-side preview of the payable amounts (T144 — 02 §5, 06 §8.3,
 * 09 §7.2/§14.3).
 *
 * The backend is the source of truth (`FinancialCalculator`). This module
 * mirrors its rule so the form never previews a figure the server will not
 * charge:
 *
 *   commission = ROUND(price × rate, 2, toward zero)
 *   total      = price + commission
 *
 * Price is a whole-cent amount (07 §7.2) and the commission is truncated to
 * the cent, so the total is always 2-decimal — the most a Turkish exchange
 * (BtcTurk, Paribu) sends. Arithmetic is done in integer cents: `price × rate`
 * in floating point drifts (10.25 × 0.02 = 0.20500000000000002) and the
 * display rounding of `Intl.NumberFormat` is half-up, so the old preview
 * could show a cent the backend never charged.
 */

const PRICE_PATTERN = /^\d+(\.\d{1,2})?$/;

/** Commission rates are `decimal(5,4)` (06 §3.5): units of 1/10 000. */
const RATE_SCALE = 10_000;

/** Parses a whole-cent decimal string ("10.55", "10.5", "10") into cents. */
export function toCents(value: string): number | null {
  const trimmed = value.trim();
  if (!PRICE_PATTERN.test(trimmed)) return null;
  const [whole, fraction = ""] = trimmed.split(".");
  const cents = Number(whole) * 100 + Number(fraction.padEnd(2, "0"));
  return Number.isSafeInteger(cents) ? cents : null;
}

/** Formats integer cents as the 2-decimal wire form ("10.76"). */
export function centsToString(cents: number): string {
  const whole = Math.trunc(cents / 100);
  const fraction = cents % 100;
  return `${whole}.${String(fraction).padStart(2, "0")}`;
}

function rateUnits(rate: number): number | null {
  if (!Number.isFinite(rate) || rate < 0) return null;
  return Math.round(rate * RATE_SCALE);
}

function commissionCents(priceCents: number, rate: number): number | null {
  const units = rateUnits(rate);
  if (units === null) return null;
  const product = priceCents * units;
  if (!Number.isSafeInteger(product)) return null;
  // Integer division toward zero — the backend's MidpointRounding.ToZero.
  return Math.floor(product / RATE_SCALE);
}

/**
 * Commission the buyer pays, as a 2-decimal string. `null` when the price
 * is not a positive whole-cent amount (the form shows a placeholder).
 */
export function calculateCommission(price: string, rate: number): string | null {
  const cents = toCents(price);
  if (cents === null || cents <= 0) return null;
  const commission = commissionCents(cents, rate);
  return commission === null ? null : centsToString(commission);
}

/** Total the buyer sends (price + commission), as a 2-decimal string. */
export function calculateTotal(price: string, rate: number): string | null {
  const cents = toCents(price);
  if (cents === null || cents <= 0) return null;
  const commission = commissionCents(cents, rate);
  return commission === null ? null : centsToString(cents + commission);
}
