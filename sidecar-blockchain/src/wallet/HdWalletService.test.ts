import { describe, it, expect } from 'vitest';
import {
  HdWalletNotConfiguredError,
  HdWalletService,
  InvalidDerivationIndexError,
  derivationPath,
} from './HdWalletService.js';

// BIP-39 Trezor reference mnemonic (12-word "abandon... about"). The
// derived Tron addresses below are deterministic for any BIP-44 /
// BIP-32 compliant implementation and were cross-verified with
// iancoleman.io/bip39 (Tron coin type 195) — known-answer test vectors.
const TREZOR_MNEMONIC =
  'abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about';

const EXPECTED_ADDRESSES: ReadonlyArray<{ readonly index: number; readonly address: string }> = [
  { index: 0, address: 'TV54FrPiVbUqxAuDMH6nKT32DPWzNwcpUu' },
  { index: 1, address: 'TZ8eYHbxQ6r7FV83eEb7Vbzfnu9PnUGwd9' },
  { index: 2, address: 'TVy9diqZQBupok1DhXUfYESKXp3sbWiqgC' },
  { index: 3, address: 'TDvXo6VKEsbqDyHcaCN3nJcPp7zxYwYAwv' },
  { index: 4, address: 'THzcV7kp3kXdGPa7xZapCp3wUP1RavyTxQ' },
];

describe('derivationPath()', () => {
  it("uses Tron BIP-44 path m/44'/195'/0'/0/{index}", () => {
    expect(derivationPath(0)).toBe("m/44'/195'/0'/0/0");
    expect(derivationPath(42)).toBe("m/44'/195'/0'/0/42");
    expect(derivationPath(999_999)).toBe("m/44'/195'/0'/0/999999");
  });
});

describe('HdWalletService', () => {
  describe('configured', () => {
    const service = new HdWalletService(TREZOR_MNEMONIC);

    it('reports configured when a mnemonic is present', () => {
      expect(service.isConfigured()).toBe(true);
    });

    it.each(EXPECTED_ADDRESSES)(
      'derives the known Tron address for the Trezor reference mnemonic at index $index',
      ({ index, address }) => {
        const result = service.derive(index);
        expect(result.address).toBe(address);
        expect(result.derivationPath).toBe(`m/44'/195'/0'/0/${index}`);
        expect(result.index).toBe(index);
      },
    );

    it('returns a Tron base58 address (T-prefix, 34 chars)', () => {
      const result = service.derive(0);
      expect(result.address).toMatch(/^T[1-9A-HJ-NP-Za-km-z]{33}$/);
    });

    it('produces deterministic output for the same index across calls', () => {
      const first = service.derive(7);
      const second = service.derive(7);
      expect(first.address).toBe(second.address);
    });

    it('caches the derived address per index (WP10 — returns the same instance)', () => {
      const fresh = new HdWalletService(TREZOR_MNEMONIC);
      const first = fresh.derive(42);
      const second = fresh.derive(42);
      // Same object reference proves the cache hit (no re-derivation).
      expect(second).toBe(first);
      // A different index is still derived independently.
      expect(fresh.derive(43)).not.toBe(first);
    });

    it('produces distinct addresses for adjacent indices', () => {
      const a = service.derive(100);
      const b = service.derive(101);
      expect(a.address).not.toBe(b.address);
    });

    it('rejects negative indices', () => {
      expect(() => service.derive(-1)).toThrow(InvalidDerivationIndexError);
    });

    it('rejects non-integer indices', () => {
      expect(() => service.derive(1.5)).toThrow(InvalidDerivationIndexError);
      expect(() => service.derive(Number.NaN)).toThrow(InvalidDerivationIndexError);
    });
  });

  describe('not configured', () => {
    it('throws HdWalletNotConfiguredError when the mnemonic is empty', () => {
      const service = new HdWalletService('');
      expect(service.isConfigured()).toBe(false);
      expect(() => service.derive(0)).toThrow(HdWalletNotConfiguredError);
    });

    it('throws HdWalletNotConfiguredError when the mnemonic is whitespace only', () => {
      const service = new HdWalletService('   \n  ');
      expect(service.isConfigured()).toBe(false);
      expect(() => service.derive(0)).toThrow(HdWalletNotConfiguredError);
    });
  });

  describe('invalid mnemonic', () => {
    it('rejects a phrase that fails BIP-39 checksum on first derive', () => {
      // 12 valid wordlist words but invalid checksum (changed last word).
      const service = new HdWalletService(
        'abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon',
      );
      expect(() => service.derive(0)).toThrow(/INVALID_MASTER_MNEMONIC|invalid|checksum/i);
    });
  });
});
