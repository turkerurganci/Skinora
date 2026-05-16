import type { Trc20Record } from '../tron/TronGridClient.js';

/**
 * Pure decision helpers used by the active monitor (08 §3.4). Keeping them
 * stateless and free of side effects lets the registry test the wiring with
 * fakes while the rules themselves are covered by table-driven unit tests.
 */

/**
 * 08 §3.4 — only `Transfer` records advance through monitoring.
 * `Approval`, `Authorization`, and TRC-721 records are dropped at debug level.
 */
export function isTransferRecord(type: string): boolean {
  return type === 'Transfer';
}

export type StablecoinSymbol = 'USDT' | 'USDC';

/**
 * Allowlist mapping of supported stablecoin contracts. Anything not in this
 * list is treated as spam (08 §3.4 wrong-token table).
 */
export type StablecoinAllowlist = Record<StablecoinSymbol, string>;

export type TokenClassification =
  | { kind: 'expected' }
  | { kind: 'wrong_token'; symbol: StablecoinSymbol }
  | { kind: 'spam_token' };

/**
 * Classify a contract address against the expected token + the allowlist of
 * platform-supported stablecoins.
 * - Match expected → `expected` (record belongs to phase 1's stream).
 * - Match allowlist but ≠ expected → `wrong_token` (auto-refund target).
 * - No match at all → `spam_token` (ignore + log; no refund attempt).
 */
export function classifyToken(opts: {
  contractAddress: string;
  expectedContract: string;
  allowlist: StablecoinAllowlist;
}): TokenClassification {
  if (opts.contractAddress === opts.expectedContract) {
    return { kind: 'expected' };
  }
  for (const symbol of Object.keys(opts.allowlist) as StablecoinSymbol[]) {
    if (opts.allowlist[symbol] && opts.contractAddress === opts.allowlist[symbol]) {
      return { kind: 'wrong_token', symbol };
    }
  }
  return { kind: 'spam_token' };
}

export interface FinalityProbe {
  currentSolidBlock: number;
  txBlock: number;
  minConfirmations: number;
}

/**
 * 08 §3.4 / 05 §3.3 — `currentSolidBlock - txBlock >= 20` flips PENDING → CONFIRMED.
 * The 20-block threshold matches the canonical Tron finality recommendation.
 */
export function isFinalized(probe: FinalityProbe): boolean {
  return probe.currentSolidBlock - probe.txBlock >= probe.minConfirmations;
}

export function confirmationCount(
  probe: Pick<FinalityProbe, 'currentSolidBlock' | 'txBlock'>,
): number {
  return Math.max(0, probe.currentSolidBlock - probe.txBlock);
}

/**
 * Convert a raw TRC-20 `value` (uint string in base units) to a fixed-decimal
 * string. USDT/USDC both use 6 decimals; the same helper supports arbitrary
 * decimals to keep tests easy.
 *
 * <para>
 * The output is intentionally a string so the .NET side can `decimal.Parse`
 * without float rounding (09 §14.3 — `MidpointRounding.ToZero`, scale 6,
 * no tolerance).
 * </para>
 */
export function formatTokenAmount(rawValue: string, decimals: number): string {
  if (!/^\d+$/.test(rawValue)) {
    throw new Error(`Invalid TRC-20 raw value: ${rawValue}`);
  }
  if (!Number.isInteger(decimals) || decimals < 0 || decimals > 18) {
    throw new Error(`Invalid decimals: ${decimals}`);
  }
  if (decimals === 0) {
    return rawValue;
  }
  const padded = rawValue.padStart(decimals + 1, '0');
  const intPart = padded.slice(0, padded.length - decimals).replace(/^0+(?=\d)/, '');
  const fracPart = padded.slice(padded.length - decimals);
  return `${intPart}.${fracPart}`;
}

/**
 * Defensive check used by the monitor before emitting any event — TronGrid's
 * v1 endpoint will occasionally surface outbound transfers from the same
 * address (the `from` matches the deposit address). Those records are
 * irrelevant for payment monitoring and would otherwise produce spurious
 * webhook traffic.
 */
export function isIncomingFor(record: Trc20Record, depositAddress: string): boolean {
  return record.to === depositAddress;
}
