import { describe, it, expect } from 'vitest';
import {
  classifyToken,
  confirmationCount,
  formatTokenAmount,
  isFinalized,
  isIncomingFor,
  isTransferRecord,
  type StablecoinAllowlist,
} from './PaymentMonitorRules.js';

const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const RANDOM_TOKEN = 'TXXXXSpamtokenContractAddressXXXXX';

const allowlist: StablecoinAllowlist = { USDT, USDC };

describe('isTransferRecord()', () => {
  it.each([
    ['Transfer', true],
    ['Approval', false],
    ['Authorization', false],
    ['transfer', false],
    ['', false],
  ] as const)('type=%s => %s', (type, expected) => {
    expect(isTransferRecord(type)).toBe(expected);
  });
});

describe('classifyToken()', () => {
  it('returns expected when contract matches expected', () => {
    expect(classifyToken({ contractAddress: USDT, expectedContract: USDT, allowlist })).toEqual({
      kind: 'expected',
    });
  });

  it('returns wrong_token for USDC when expected is USDT', () => {
    expect(classifyToken({ contractAddress: USDC, expectedContract: USDT, allowlist })).toEqual({
      kind: 'wrong_token',
      symbol: 'USDC',
    });
  });

  it('returns wrong_token for USDT when expected is USDC', () => {
    expect(classifyToken({ contractAddress: USDT, expectedContract: USDC, allowlist })).toEqual({
      kind: 'wrong_token',
      symbol: 'USDT',
    });
  });

  it('returns spam_token for unknown contract', () => {
    expect(
      classifyToken({ contractAddress: RANDOM_TOKEN, expectedContract: USDT, allowlist }),
    ).toEqual({ kind: 'spam_token' });
  });

  it('ignores allowlist entries with empty contract address', () => {
    const partialAllowlist: StablecoinAllowlist = { USDT, USDC: '' };
    // The contract is the same as the empty USDC slot — must not be classified as wrong_token USDC.
    expect(
      classifyToken({
        contractAddress: RANDOM_TOKEN,
        expectedContract: USDT,
        allowlist: partialAllowlist,
      }),
    ).toEqual({ kind: 'spam_token' });
  });
});

describe('isFinalized() and confirmationCount()', () => {
  it('returns true when delta meets minConfirmations', () => {
    expect(isFinalized({ currentSolidBlock: 120, txBlock: 100, minConfirmations: 20 })).toBe(true);
    expect(isFinalized({ currentSolidBlock: 121, txBlock: 100, minConfirmations: 20 })).toBe(true);
  });

  it('returns false when delta is one short', () => {
    expect(isFinalized({ currentSolidBlock: 119, txBlock: 100, minConfirmations: 20 })).toBe(false);
  });

  it('returns 0 confirmation count for negative deltas (defensive)', () => {
    expect(confirmationCount({ currentSolidBlock: 50, txBlock: 100 })).toBe(0);
  });

  it('confirmationCount equals delta when positive', () => {
    expect(confirmationCount({ currentSolidBlock: 130, txBlock: 100 })).toBe(30);
  });
});

describe('formatTokenAmount()', () => {
  it.each([
    ['100500000', 6, '100.500000'],
    ['1', 6, '0.000001'],
    ['0', 6, '0.000000'],
    ['50000000', 6, '50.000000'],
    ['1000000000000', 6, '1000000.000000'],
    ['7', 2, '0.07'],
    ['7', 0, '7'],
  ])('raw=%s decimals=%d => %s', (raw, decimals, expected) => {
    expect(formatTokenAmount(raw, decimals)).toBe(expected);
  });

  it('throws on non-numeric value', () => {
    expect(() => formatTokenAmount('abc', 6)).toThrow();
  });

  it('throws on negative decimals', () => {
    expect(() => formatTokenAmount('1', -1)).toThrow();
  });
});

describe('isIncomingFor()', () => {
  it('returns true when record.to matches the deposit address', () => {
    const record = {
      transaction_id: 'a',
      from: 'TQ8fxJKG9RbGaq9HkonU4gu1S2eWJM1PM2',
      to: 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t',
      type: 'Transfer',
      value: '1',
      block_timestamp: 0,
      token_info: { address: USDT },
    };
    expect(isIncomingFor(record, 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t')).toBe(true);
    expect(isIncomingFor(record, 'TQ8fxJKG9RbGaq9HkonU4gu1S2eWJM1PM2')).toBe(false);
  });
});
