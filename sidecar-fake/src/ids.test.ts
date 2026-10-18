import { describe, it, expect } from 'vitest';
import {
  fakeTronAddress,
  fakeTxHash,
  fakeAssetId,
  fakeEthereumAddress,
  fakeEthereumTxHash,
  toChecksumAddress,
} from './ids.js';

describe('fakeTronAddress', () => {
  it('is a deterministic 34-char base58 string starting with T', () => {
    const a = fakeTronAddress(0);
    expect(a).toHaveLength(34);
    expect(a.startsWith('T')).toBe(true);
    expect(a).toMatch(/^T[1-9A-HJ-NP-Za-km-z]{33}$/);
    expect(fakeTronAddress(0)).toBe(a);
  });

  it('differs per index', () => {
    expect(fakeTronAddress(0)).not.toBe(fakeTronAddress(1));
  });
});

describe('fakeTxHash', () => {
  it('is a deterministic 64-hex string', () => {
    const h = fakeTxHash('tx-seed');
    expect(h).toMatch(/^[0-9a-f]{64}$/);
    expect(fakeTxHash('tx-seed')).toBe(h);
    expect(fakeTxHash('other')).not.toBe(h);
  });
});

describe('fakeAssetId', () => {
  it('is a deterministic numeric string', () => {
    expect(fakeAssetId('a')).toMatch(/^[0-9]+$/);
    expect(fakeAssetId('a')).toBe(fakeAssetId('a'));
    expect(fakeAssetId('b')).not.toBe(fakeAssetId('a'));
  });
});

describe('toChecksumAddress (EIP-55)', () => {
  // The four "all caps / all lower / normal" examples of the EIP-55 spec
  // (eips.ethereum.org/EIPS/eip-55) — an independent source, not this code.
  it.each([
    '0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed',
    '0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359',
    '0xdbF03B407c01E7cD3CBea99509d93f8DDDC8C6FB',
    '0xD1220A0cf47c7B9Be7A2E6BA89F429762e7b9aDb',
  ])('reproduces the EIP-55 spec example %s from its lower-case form', (expected) => {
    expect(toChecksumAddress(expected.toLowerCase())).toBe(expected);
    expect(toChecksumAddress(expected.toUpperCase().replace('0X', '0x'))).toBe(expected);
  });

  it('refuses anything that is not 20 bytes of hex', () => {
    expect(() => toChecksumAddress('0x1234')).toThrow();
    expect(() => toChecksumAddress('TKzxdSv2FZKQrEqkKVgp5DcwEXBEKMg2Ax')).toThrow();
  });
});

describe('fakeEthereumAddress', () => {
  // ethers 6.17.0 `getAddress` of the same 20 bytes (2026-10-18) — pins both
  // the derivation seed and the EIP-55 casing against an independent
  // implementation.
  it.each([
    [0, '0x2Ff3C209729708F6C1Df74A39A246204550a157A'],
    [1, '0x715a14e8505Ec2Cdf89bBa2bbDB9306D33D70102'],
    [999_001, '0xA5DfAA6e093408Af19263350166f2a30cCDa6d48'],
  ])('index %i derives %s', (index, expected) => {
    expect(fakeEthereumAddress(index)).toBe(expected);
  });

  it('differs per index', () => {
    expect(fakeEthereumAddress(0)).not.toBe(fakeEthereumAddress(1));
  });
});

describe('fakeEthereumTxHash', () => {
  it('is 0x + 64 lower-case hex, deterministic, distinct from the TRON hash', () => {
    const h = fakeEthereumTxHash('seed');
    expect(h).toMatch(/^0x[0-9a-f]{64}$/);
    expect(fakeEthereumTxHash('seed')).toBe(h);
    expect(h.slice(2)).not.toBe(fakeTxHash('seed'));
  });
});
