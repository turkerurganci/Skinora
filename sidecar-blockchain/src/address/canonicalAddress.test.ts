import { describe, expect, it } from 'vitest';
import { canonicalAddress, sameAddress } from './canonicalAddress.js';

// T145 (06 §1.4) — real addresses of both shapes. The checksummed form is
// Tether's Ethereum USDT contract as ethers v6 `getAddress` prints it.
const EVM_CHECKSUMMED = '0xdAC17F958D2ee523a2206206994597C13D831ec7';
const EVM_LOWER = '0xdac17f958d2ee523a2206206994597c13d831ec7';
const TRON = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
// Synthetic TRON transaction id (64 hex, no prefix), built at run time.
const TRON_TXID = '4f1e0bd2'.repeat(8);

describe('canonicalAddress', () => {
  it('lowercases an EVM address in any letter case', () => {
    expect(canonicalAddress(EVM_CHECKSUMMED)).toBe(EVM_LOWER);
    expect(canonicalAddress(EVM_LOWER.toUpperCase().replace('0X', '0x'))).toBe(EVM_LOWER);
  });

  it('lowercases an EVM transaction hash', () => {
    const hash = '0xAbCdEf' + '0'.repeat(58);
    expect(canonicalAddress(hash)).toBe(hash.toLowerCase());
  });

  it('never touches a TRON value — base58 is case-significant', () => {
    expect(canonicalAddress(TRON)).toBe(TRON);
    expect(canonicalAddress(TRON.toLowerCase())).toBe(TRON.toLowerCase());
    expect(canonicalAddress(TRON_TXID)).toBe(TRON_TXID);
  });

  it('leaves a non-hex 0x string unchanged', () => {
    expect(canonicalAddress('0xZZ12')).toBe('0xZZ12');
    expect(canonicalAddress('0x')).toBe('0x');
  });
});

describe('sameAddress', () => {
  it('matches EVM spellings case-insensitively', () => {
    expect(sameAddress(EVM_CHECKSUMMED, EVM_LOWER)).toBe(true);
  });

  it('compares TRON exactly', () => {
    expect(sameAddress(TRON, TRON)).toBe(true);
    expect(sameAddress(TRON, TRON.toLowerCase())).toBe(false);
  });

  it('never matches an absent value', () => {
    expect(sameAddress(undefined, undefined)).toBe(false);
    expect(sameAddress(null, TRON)).toBe(false);
    expect(sameAddress(EVM_LOWER, undefined)).toBe(false);
  });
});
