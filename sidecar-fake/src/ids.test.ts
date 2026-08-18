import { describe, it, expect } from 'vitest';
import { fakeTronAddress, fakeTxHash, fakeAssetId } from './ids.js';

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
