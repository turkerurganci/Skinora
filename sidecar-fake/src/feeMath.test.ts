import { describe, it, expect } from 'vitest';
import { parsePriceMicro, weiToUsdtCentsCeil } from './feeMath.js';

describe('weiToUsdtCentsCeil', () => {
  it('rounds UP to cents with integer math (the backend refuses any other pair)', () => {
    // 2·10^13 wei = 0.00002 ETH × 2414.579991 = 0.04829… → 0.05
    expect(weiToUsdtCentsCeil(20_000_000_000_000n, '2414.579991')).toBe('0.05');
    // An exact cent stays put.
    expect(weiToUsdtCentsCeil(10n ** 16n, '1')).toBe('0.01');
    expect(weiToUsdtCentsCeil(10n ** 16n + 1n, '1')).toBe('0.02');
    expect(weiToUsdtCentsCeil(0n, '2000')).toBe('0.00');
  });

  it('keeps precision past 2^53 wei', () => {
    // 9007199254740993 = 2^53 + 1 — a JS number would read it as 2^53.
    // 0.009007199254740993 ETH × 2414.579991 = 21.7487… → 21.75 (the figure
    // of the Ethereum service's estimate-fee example, T154).
    expect(weiToUsdtCentsCeil(9_007_199_254_740_993n, '2414.579991')).toBe('21.75');
  });

  it('refuses a price with more than 6 fraction digits or a non-positive price', () => {
    expect(() => parsePriceMicro('1.0000001')).toThrow();
    expect(() => parsePriceMicro('0')).toThrow();
    expect(() => parsePriceMicro('-1')).toThrow();
    expect(parsePriceMicro('2414.579991')).toBe(2_414_579_991n);
  });
});
