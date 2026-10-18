import { SidecarError } from '../errors/SidecarError.js';

/**
 * Decimal ↔ raw-unit conversion for the Ethereum service's sends (T152).
 * Bigint only — never `parseFloat` (09 §14.3 financial math invariants).
 *
 * USDT and USDC on Ethereum both have **6 decimals** (08 §3.7.1, read from the
 * chain in T142); USD amounts (the fee cap) are carried in micro-dollars so the
 * cap → gwei conversion stays integer.
 */

/** USDT / USDC ERC-20 decimals on Ethereum (08 §3.7.1). */
export const TOKEN_DECIMALS = 6;
const TOKEN_POWER = 10n ** BigInt(TOKEN_DECIMALS);

/** Wei per ETH. */
export const WEI_PER_ETH = 10n ** 18n;

const DECIMAL = /^\d+(?:\.\d+)?$/;

function scaled(value: string, decimals: number): bigint | null {
  if (!DECIMAL.test(value)) return null;
  const [whole, fraction = ''] = value.split('.');
  if (fraction.length > decimals) return null;
  return BigInt(whole) * 10n ** BigInt(decimals) + BigInt(fraction.padEnd(decimals, '0') || '0');
}

/**
 * A positive token amount ("100.5") → raw units (100500000n). More than six
 * fraction digits, a sign, an exponent or zero is refused — the backend sends
 * at most six (05 §3.3a "Tutar ve ücret alanları").
 */
export function toTokenUnits(amount: string): bigint {
  const units = scaled(amount, TOKEN_DECIMALS);
  if (units === null) {
    throw new SidecarError(
      `Invalid amount "${amount}" — expected a positive decimal with at most ${TOKEN_DECIMALS} fraction digits.`,
      'INVALID_TRANSFER_AMOUNT',
      false,
    );
  }
  if (units <= 0n) {
    throw new SidecarError('Transfer amount must be positive.', 'INVALID_TRANSFER_AMOUNT', false);
  }
  return units;
}

/** Raw units → decimal string ("100500000" → "100.5"), for logs. */
export function formatTokenUnits(units: bigint): string {
  const whole = units / TOKEN_POWER;
  const fraction = (units % TOKEN_POWER)
    .toString()
    .padStart(TOKEN_DECIMALS, '0')
    .replace(/0+$/, '');
  return fraction ? `${whole}.${fraction}` : whole.toString();
}

/** A positive USD amount ("0.50") → micro-dollars (500000n); null when malformed or zero. */
export function usdToMicros(value: string): bigint | null {
  const micros = scaled(value.trim(), 6);
  return micros !== null && micros > 0n ? micros : null;
}

/**
 * A configured limit ("5000", "0.05") → raw units of the given decimals, or
 * null for unset / malformed / zero — the caller treats null as "limit not
 * configured" and refuses to sign (fail closed, the TRON service's rule).
 */
export function parseLimit(raw: string | undefined, decimals: number): bigint | null {
  const units = scaled((raw ?? '').trim(), decimals);
  return units !== null && units > 0n ? units : null;
}
