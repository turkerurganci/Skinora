import { describe, expect, it } from 'vitest';
import {
  feeCapPerGasWei,
  formatCents,
  formatScaled,
  medianOf,
  parseScaled,
  realizedFeeWei,
  weiToUsdtCentsCeil,
} from './feeMath.js';

describe('parseScaled / formatScaled', () => {
  it.each([
    ['2414.57999137', 6, 2414579991n], // 8 digits → rounded half up at the 7th
    ['2414.5799984', 6, 2414579998n], // 7th digit 4 → not rounded up
    ['2414.5799985', 6, 2414579999n], // 7th digit 5 → rounded up
    ['2400', 6, 2400000000n],
    ['0.5', 6, 500000n],
    ['0.000001', 6, 1n],
  ])('%s at scale %i → %s', (text, scale, expected) => {
    expect(parseScaled(text, scale)).toBe(expected);
  });

  it.each(['', '-1', '1e3', '1,5', '1.', '.5', ' 1', '0x10', 'NaN', 'Infinity'])(
    'refuses %j (never read as zero)',
    (text) => {
      expect(parseScaled(text, 6)).toBeNull();
    },
  );

  it('formats with exactly the scale digits', () => {
    expect(formatScaled(2414579991n, 6)).toBe('2414.579991');
    expect(formatScaled(1n, 6)).toBe('0.000001');
    expect(formatScaled(0n, 2)).toBe('0.00');
    expect(formatCents(2175n)).toBe('21.75');
  });
});

describe('weiToUsdtCentsCeil — exact, rounded up', () => {
  it('prices a fee beyond 2^53 wei without losing a digit', () => {
    // 2^53 + 1 wei is not representable as a double; 0.009007… ETH × 2414.579991.
    expect(weiToUsdtCentsCeil(9_007_199_254_740_993n, 2_414_579_991n)).toBe(2175n);
  });

  it('keeps the wei that a double would drop — and the cent it is worth', () => {
    // 9,010,000,000,000,001 wei at 1000 USDT is 9.01 USDT plus a fraction → 9.02.
    // Through a double the last wei is gone and the charge is 9.01.
    expect(Number(9_010_000_000_000_001n)).toBe(9_010_000_000_000_000);
    expect(weiToUsdtCentsCeil(9_010_000_000_000_001n, 1_000_000_000n)).toBe(902n);
  });

  it('rounds any remainder up to the next cent', () => {
    // 1 wei at any price is a fraction of a cent → 1 cent.
    expect(weiToUsdtCentsCeil(1n, 2_414_579_991n)).toBe(1n);
    // Exactly 1 cent: 10^22 / price … choose a price that divides cleanly.
    // 0.00001 ETH at 1000 USDT = 0.01 USDT exactly.
    expect(weiToUsdtCentsCeil(10_000_000_000_000n, 1_000_000_000n)).toBe(1n);
    expect(weiToUsdtCentsCeil(10_000_000_000_001n, 1_000_000_000n)).toBe(2n);
  });

  it('is 0 for 0 wei and refuses a non-positive price', () => {
    expect(weiToUsdtCentsCeil(0n, 1n)).toBe(0n);
    expect(() => weiToUsdtCentsCeil(1n, 0n)).toThrow(RangeError);
    expect(() => weiToUsdtCentsCeil(-1n, 1n)).toThrow(RangeError);
  });

  it('T142 measurement: 125,482 gas at 0.217 gwei and 2,364.48 $ is 0.07 $ (0.064 rounded up)', () => {
    const wei = 125_482n * 217_000_000n;
    expect(formatCents(weiToUsdtCentsCeil(wei, 2_364_480_000n))).toBe('0.07');
  });
});

describe('feeCapPerGasWei — 08 §3.7.7 cap in wei per gas, rounded down', () => {
  it('0.50 $ over the sweep (67,201 gas) at 2,364.48 $ is ~3.15 gwei', () => {
    const cap = feeCapPerGasWei(500_000n, 67_201n, 2_364_480_000n);
    expect(cap).toBe(3_146_723_791n);
    // Never above the cap: the rounded-down price × gas costs ≤ 0.50 $.
    expect(weiToUsdtCentsCeil(cap * 67_201n, 2_364_480_000n)).toBeLessThanOrEqual(50n);
  });

  it('0.50 $ over the payout (64,031 gas) is ~3.3 gwei', () => {
    expect(feeCapPerGasWei(500_000n, 64_031n, 2_364_480_000n)).toBe(3_302_509_495n);
  });

  it('refuses a missing price (no price → no cap → the transfer waits)', () => {
    expect(() => feeCapPerGasWei(500_000n, 67_201n, 0n)).toThrow(RangeError);
    expect(() => feeCapPerGasWei(500_000n, 0n, 1n)).toThrow(RangeError);
  });
});

describe('realizedFeeWei — receipt gasUsed × effectiveGasPrice', () => {
  it('is exact beyond 2^53 (the transfer-status contract example)', () => {
    const fee = realizedFeeWei({
      gasUsed: '0x' + (63_209).toString(16),
      effectiveGasPrice: '0x' + (142_500_000_123).toString(16),
    });
    expect(fee.toString()).toBe('9007282507774707');
    // A double would have reported …708.
    expect(Number(fee).toString()).not.toBe(fee.toString());
  });

  it.each([
    [{ gasUsed: undefined, effectiveGasPrice: '0x1' }],
    [{ gasUsed: '0x1', effectiveGasPrice: 7 }],
    [{ gasUsed: '12', effectiveGasPrice: '0x1' }],
  ])('refuses a receipt without hex quantities %j', (receipt) => {
    expect(() => realizedFeeWei(receipt)).toThrow(RangeError);
  });
});

describe('medianOf', () => {
  it('takes the middle, the lower middle for an even count', () => {
    expect(medianOf([5n, 1n, 3n])).toBe(3n);
    expect(medianOf([4n, 1n, 3n, 2n])).toBe(2n);
    expect(medianOf([7n])).toBe(7n);
    expect(() => medianOf([])).toThrow(RangeError);
  });
});
