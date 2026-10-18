import { canonicalAddress, sameAddress } from '../address/canonicalAddress.js';

/**
 * Wrong-token classification on Ethereum (T151 — 08 §3.7.4 "Yanlış token",
 * 11 §F8 T141 yönetici kararı "(d)" (öneriyle kaydedildi — ⚠)). The same rule
 * as the TRON service's `classifyToken`:
 *
 * - the contract the deposit was opened for → the payment;
 * - another contract on the allowlist (USDT, Circle USDC) → `wrong_token`,
 *   refunded automatically by the backend after its minimum-refund check;
 * - anything else → `spam_token`: recorded for the admin, never refunded.
 */

export type StablecoinSymbol = 'USDT' | 'USDC';

/** Both allowlisted Ethereum stablecoins have 6 decimals (08 §3.7.1, read from chain in T142). */
export const STABLECOIN_DECIMALS = 6;

/** Canonical contract per symbol; '' = not configured (a testnet before T159's USDT copy). */
export type StablecoinAllowlist = Readonly<Record<StablecoinSymbol, string>>;

export type TokenClassification =
  | { kind: 'expected' }
  | { kind: 'wrong_token'; symbol: StablecoinSymbol }
  | { kind: 'spam_token' };

export function classifyToken(opts: {
  contract: string;
  expectedContract: string;
  allowlist: StablecoinAllowlist;
}): TokenClassification {
  if (sameAddress(opts.contract, opts.expectedContract)) return { kind: 'expected' };
  for (const symbol of ['USDT', 'USDC'] as const) {
    const listed = opts.allowlist[symbol];
    if (listed !== '' && sameAddress(opts.contract, listed)) {
      return { kind: 'wrong_token', symbol };
    }
  }
  return { kind: 'spam_token' };
}

/** The allowlisted symbol of a contract, or null. */
export function allowlistedSymbol(
  contract: string,
  allowlist: StablecoinAllowlist,
): StablecoinSymbol | null {
  for (const symbol of ['USDT', 'USDC'] as const) {
    if (
      allowlist[symbol] !== '' &&
      canonicalAddress(allowlist[symbol]) === canonicalAddress(contract)
    ) {
      return symbol;
    }
  }
  return null;
}
