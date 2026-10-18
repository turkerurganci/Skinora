import { getAddress } from 'ethers';

const EVM_ADDRESS = /^0x[0-9a-fA-F]{40}$/;

/**
 * Validates a configured Ethereum address the way the backend does (06 §1.4,
 * T145 decision iv, `EvmAddress`): `0x` + 40 hex digits; a mixed-case value
 * must carry a valid EIP-55 checksum; an all-lowercase or all-uppercase value
 * carries no checksum and is accepted as written.
 *
 * Returns the EIP-55 display form, or `null` when the value is not an address.
 * The canonical (stored, compared) form is `canonicalAddress()` — lowercase.
 */
export function checksummedEvmAddress(value: string): string | null {
  if (!EVM_ADDRESS.test(value)) return null;
  const hex = value.slice(2);
  const mixedCase = hex !== hex.toLowerCase() && hex !== hex.toUpperCase();
  try {
    // ethers throws on a mixed-case value whose checksum does not match, and
    // checksums an all-lowercase one.
    return getAddress(mixedCase ? value : value.toLowerCase());
  } catch {
    return null;
  }
}
