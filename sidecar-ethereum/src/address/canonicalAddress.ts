/**
 * The single canonical form of an on-chain identifier and the comparison the
 * service uses on one (06 §1.4 "Kanonik adres biçimi", T145). Same rule as the
 * backend's `BlockchainAddress` helper and the TRON service's copy of this
 * file, so all three agree on what "the same address" means.
 *
 * - **EVM** (`0x` followed by hex digits — address, contract, tx hash): hex is
 *   case-insensitive and EIP-55 uses the letter case only as a checksum, so
 *   the canonical form is lowercase and two spellings compare equal.
 * - **Anything else** is returned unchanged (TRON base58 is case-significant).
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
