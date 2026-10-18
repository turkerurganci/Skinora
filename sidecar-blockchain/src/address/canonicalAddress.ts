/**
 * The single canonical form of an on-chain identifier and the comparison the
 * sidecar uses on one (06 §1.4 "Kanonik adres biçimi", T145). Mirrors the
 * backend's `BlockchainAddress` helper so both sides agree on what "the same
 * address" means.
 *
 * - **EVM** (`0x` followed by hex digits — address, contract, tx hash): hex is
 *   case-insensitive and EIP-55 uses the letter case only as a checksum, so
 *   the canonical form is lowercase and two spellings compare equal.
 * - **Anything else** (TRON base58check, a TRON transaction id) is returned
 *   **unchanged**. Base58 is case-significant — lowercasing a TRON address
 *   yields a different, usually invalid, address. Base58 has no `0` digit, so
 *   a TRON address can never start with `0x`.
 *
 * Today this service only ever sees TRON values, for which both helpers are
 * an identity / exact comparison; routing every address comparison through
 * them keeps the rule in one place for the EVM service that shares this code
 * shape (T150).
 */

const ZERO_X_HEX = /^0[xX][0-9a-fA-F]+$/;

export function canonicalAddress(value: string): string {
  return ZERO_X_HEX.test(value) ? value.toLowerCase() : value;
}

/**
 * Canonical equality. `undefined` / `null` never match anything — an absent
 * address is not "equal" to another absent one.
 */
export function sameAddress(a: string | null | undefined, b: string | null | undefined): boolean {
  if (a == null || b == null) return false;
  return canonicalAddress(a) === canonicalAddress(b);
}
