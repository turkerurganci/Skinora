import { describe, expect, it } from 'vitest';
import {
  TRANSFER_TOPIC,
  addressFromWord,
  addressWord,
  balanceOfCall,
  decodeTransferLog,
  decodeUint,
  formatTokenAmount,
} from './erc20.js';
import { classifyToken } from './tokenRules.js';
import type { RpcLog } from '../rpc/RpcClient.js';

const USDT = '0xdac17f958d2ee523a2206206994597c13d831ec7';
const USDC = '0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48';
const A = '0x1111111111111111111111111111111111111111';
const B = '0x2222222222222222222222222222222222222222';

function log(overrides: Partial<RpcLog> = {}): RpcLog {
  return {
    address: '0xdAC17F958D2ee523a2206206994597C13D831ec7',
    topics: [TRANSFER_TOPIC, addressWord(A), addressWord(B)],
    data: '0x' + 20_400_000n.toString(16).padStart(64, '0'),
    blockNumber: 7n,
    blockHash: '0xbb',
    transactionHash: '0xABCDEF',
    logIndex: 3,
    removed: false,
    ...overrides,
  };
}

describe('ERC-20 Transfer decoding (08 §3.7.4)', () => {
  it('decodes an ERC-20 Transfer and keeps the chain log index; contract and hash canonical', () => {
    expect(decodeTransferLog(log())).toEqual({
      contract: USDT,
      from: A,
      to: B,
      value: 20_400_000n,
      txHash: '0xabcdef',
      logIndex: 3,
      blockNumber: 7n,
      blockHash: '0xbb',
    });
  });

  it('drops an ERC-721 Transfer (fourth indexed topic, empty data)', () => {
    expect(
      decodeTransferLog(log({ topics: [...log().topics, addressWord(A)], data: '0x' })),
    ).toBeNull();
  });

  it('drops a four-topic Transfer even when it carries a 32-byte data word (not ERC-20)', () => {
    expect(decodeTransferLog(log({ topics: [...log().topics, addressWord(A)] }))).toBeNull();
  });

  it('drops another event and a topic that is not an address', () => {
    expect(
      decodeTransferLog(log({ topics: ['0x' + '0'.repeat(64), ...log().topics.slice(1)] })),
    ).toBeNull();
    expect(addressFromWord('0x' + 'f'.repeat(64))).toBeNull();
  });

  it('address word round trip and balanceOf call data', () => {
    expect(addressFromWord(addressWord(A.toUpperCase().replace('0X', '0x')))).toBe(A);
    expect(balanceOfCall(B)).toBe('0x70a08231' + '0'.repeat(24) + B.slice(2));
  });

  it('refuses an empty return value instead of reading it as zero', () => {
    expect(() => decodeUint('0x')).toThrow();
    expect(decodeUint('0x' + '0'.repeat(63) + '5')).toBe(5n);
  });

  it.each([
    [20_400_000n, 6, '20.400000'],
    [1n, 6, '0.000001'],
    [0n, 6, '0.000000'],
    [10n ** 18n, 18, '1.000000000000000000'],
  ])('formatTokenAmount(%s, %i) = %s', (raw, decimals, out) => {
    expect(formatTokenAmount(raw, decimals)).toBe(out);
  });
});

describe('classifyToken (izin listesi — öneriyle kaydedildi ⚠)', () => {
  const allowlist = { USDT, USDC };
  it.each([
    [USDT, { kind: 'expected' }],
    ['0xDAC17F958D2EE523A2206206994597C13D831EC7'.replace('0X', '0x'), { kind: 'expected' }],
    [USDC, { kind: 'wrong_token', symbol: 'USDC' }],
    ['0x' + '9'.repeat(40), { kind: 'spam_token' }],
  ])('%s → %o', (contract, expected) => {
    expect(classifyToken({ contract, expectedContract: USDT, allowlist })).toEqual(expected);
  });

  it('a symbol without a configured contract never matches (empty string is not a wildcard)', () => {
    expect(
      classifyToken({ contract: '', expectedContract: USDT, allowlist: { USDT, USDC: '' } }),
    ).toEqual({
      kind: 'spam_token',
    });
  });
});
