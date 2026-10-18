import { describe, it, expect } from 'vitest';
import { parseStakePermissionId, parseStakingEnabled } from './index.js';

/**
 * #325 validation: the read of STAKE_ACCOUNT_PERMISSION_ID had no test — its
 * default could change to 3 unnoticed — and `parseInt(env ?? '2')` turned a
 * blank value (a `.env` line outside compose, which applies its own `:-2`)
 * into NaN, after which every delegation was rejected and quietly burned.
 */
describe('parseStakePermissionId', () => {
  it.each([
    { raw: undefined, expected: 2 },
    { raw: '', expected: 2 },
    { raw: '   ', expected: 2 },
    { raw: '2', expected: 2 },
    { raw: '5', expected: 5 },
    { raw: ' 3 ', expected: 3 },
  ])('reads $raw as $expected', ({ raw, expected }) => {
    expect(parseStakePermissionId(raw)).toBe(expected);
  });

  // NaN is refused where the stake account is configured
  // (EnergyDelegationService); parseInt would have read '2abc' as 2.
  it.each(['abc', '2abc', '2.5', '-2', '0x2'])('reads %s as NaN, never as a number', (raw) => {
    expect(parseStakePermissionId(raw)).toBeNaN();
  });
});

/** T149 — the dormant stake switch: default OFF, only the literal words. */
describe('parseStakingEnabled', () => {
  it.each([
    { raw: undefined, expected: false },
    { raw: '', expected: false },
    { raw: '  ', expected: false },
    { raw: 'false', expected: false },
    { raw: 'FALSE', expected: false },
    { raw: 'true', expected: true },
    { raw: ' True ', expected: true },
  ])('reads $raw as $expected', ({ raw, expected }) => {
    expect(parseStakingEnabled(raw)).toBe(expected);
  });

  it.each(['1', '0', 'yes', 'no', 'ture', 'on'])('refuses %s instead of guessing', (raw) => {
    expect(() => parseStakingEnabled(raw)).toThrow(/ENERGY_STAKING_ENABLED/);
  });
});
