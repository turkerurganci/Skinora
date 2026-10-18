import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { FeeWaitTracker, STALE_AFTER_MS } from './FeeWaitTracker.js';
import {
  CAP_REFERENCE_GAS,
  capWeiPerGas,
  capWeiPerTransaction,
  feeCeilingPerGas,
} from './FeePolicy.js';
import { TxJournal, isJournalId, recordFeeCapWei, type JournalRecord } from './TxJournal.js';
import { formatTokenUnits, parseLimit, toTokenUnits, usdToMicros } from './units.js';

let dir: string;
beforeEach(() => {
  dir = mkdtempSync(join(tmpdir(), 'eth-journal-'));
});
afterEach(() => rmSync(dir, { recursive: true, force: true }));

const RECORD: JournalRecord = {
  id: 'transfer:00000000-0000-0000-0000-000000000001',
  purpose: 'PAYOUT',
  refundType: null,
  signer: 'HOT',
  from: '0xaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa',
  depositIndex: null,
  contract: '0xdac17f958d2ee523a2206206994597c13d831ec7',
  token: 'USDT',
  recipient: '0x1111111111111111111111111111111111111111',
  amountUnits: '12345678',
  nonce: 4,
  gasLimit: '50822',
  feeCapWeiPerGas: '3302593213',
  attempts: [
    {
      hash: '0x' + 'ab'.repeat(32),
      raw: '0x02f8',
      maxFeePerGas: '1',
      maxPriorityFeePerGas: '1',
      signedAt: 1,
    },
  ],
  createdAt: 1,
  lastBroadcastAt: null,
  lastBroadcastError: null,
  mined: null,
  conflictSuspectedAt: null,
  conflict: null,
};

describe('TxJournal (08 §3.7.6 — bytes on disk before broadcast)', () => {
  it('survives a restart: the record, its hash index and its nonce come back', () => {
    new TxJournal(dir).put(RECORD);
    const reopened = new TxJournal(dir);
    expect(reopened.get(RECORD.id)).toEqual(RECORD);
    expect(reopened.findByHash('0x' + 'AB'.repeat(32))?.id).toBe(RECORD.id);
    expect(reopened.bySender('0xAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA')).toHaveLength(1);
  });

  it('writes the file whole (no temp file left behind)', () => {
    new TxJournal(dir).put(RECORD);
    const content = readFileSync(
      join(dir, 'transfer-00000000-0000-0000-0000-000000000001.json'),
      'utf8',
    );
    expect(JSON.parse(content)).toEqual(RECORD);
  });

  it('refuses to start on an unreadable record (TX_JOURNAL_CORRUPT)', () => {
    writeFileSync(
      join(dir, 'transfer-00000000-0000-0000-0000-000000000002.json'),
      '{"id": "transfer:00000000',
    );
    expect(() => new TxJournal(dir)).toThrow(
      expect.objectContaining({ code: 'TX_JOURNAL_CORRUPT' }),
    );
  });

  it('refuses to start when the directory cannot be written (TX_JOURNAL_UNAVAILABLE)', () => {
    const file = join(dir, 'not-a-dir');
    writeFileSync(file, 'x');
    expect(() => new TxJournal(join(file, 'journal'))).toThrow(
      expect.objectContaining({ code: 'TX_JOURNAL_UNAVAILABLE' }),
    );
  });

  it('only prefix:GUID ids (safe file names)', () => {
    expect(isJournalId('transfer:00000000-0000-0000-0000-000000000001')).toBe(true);
    expect(isJournalId('cold:00000000-0000-0000-0000-000000000001')).toBe(true);
    expect(isJournalId('transfer:../../etc/passwd')).toBe(false);
    expect(isJournalId('00000000-0000-0000-0000-000000000001')).toBe(false);
  });
});

describe('FeeWaitTracker (3-hour wait clock, 08 §3.7.7)', () => {
  it('keeps the first refusal time across refusals and restarts, stops on clear', () => {
    let now = 1_000_000;
    const tracker = new FeeWaitTracker(dir, () => now);
    expect(tracker.deferred('a', 'FEE_ABOVE_CAP')).toBe(1_000_000);
    now += 60_000;
    expect(new FeeWaitTracker(dir, () => now).deferred('a', 'ETH_PRICE_UNAVAILABLE')).toBe(
      1_000_000,
    );
    expect(
      new FeeWaitTracker(dir, () => now).longestWaitSeconds([
        'FEE_ABOVE_CAP',
        'ETH_PRICE_UNAVAILABLE',
      ]),
    ).toBe(60);
    tracker.cleared('a');
    expect(tracker.longestWaitSeconds(['FEE_ABOVE_CAP'])).toBe(0);
  });

  it('a request nobody asks about for 15 min no longer counts; asked again, its clock restarts', () => {
    let now = 0;
    const tracker = new FeeWaitTracker(dir, () => now);
    tracker.deferred('a', 'FEE_ABOVE_CAP');
    now += STALE_AFTER_MS + 1;
    expect(tracker.longestWaitSeconds(['FEE_ABOVE_CAP'])).toBe(0);
    expect(tracker.deferred('a', 'FEE_ABOVE_CAP')).toBe(now);
  });

  it('a fee wait after a gas wait starts its own clock', () => {
    let now = 0;
    const tracker = new FeeWaitTracker(dir, () => now);
    tracker.deferred('a', 'FUEL_REQUIRED');
    now += 60_000;
    expect(tracker.deferred('a', 'FEE_ABOVE_CAP')).toBe(60_000);
  });
});

describe('fee cap arithmetic (08 §3.7.7)', () => {
  // T142's worked example: 0.50 $, ETH 2,364.48 $ → sweep ~3.1 gwei, payout ~3.3 gwei.
  it.each([
    ['SWEEP', 3_146_723_791n],
    ['PAYOUT', 3_302_509_495n],
  ] as const)('%s cap at 0.50 $ and 2,364.48 $/ETH = %s wei/gas', (flow, expected) => {
    expect(capWeiPerGas(500_000n, CAP_REFERENCE_GAS[flow], 2_364_480_000n)).toBe(expected);
  });

  it('a missing price or cap gives a zero cap (nothing passes)', () => {
    expect(capWeiPerGas(500_000n, 64_031n, 0n)).toBe(0n);
    expect(capWeiPerGas(0n, 64_031n, 2_364_480_000n)).toBe(0n);
    expect(capWeiPerTransaction(500_000n, 'PAYOUT', 0n)).toBe(0n);
    expect(capWeiPerTransaction(0n, 'PAYOUT', 2_364_480_000n)).toBe(0n);
  });

  // T152 düzeltme turu 2 — per transaction (yönetici kararı): 0.50 $ ÷ 2,364.48 $,
  // the deposit flows keep the fuel send's share of the reference gas.
  it.each([
    ['PAYOUT', 211_462_985_519_014n],
    ['COLD', 211_462_985_519_014n],
    ['SWEEP', 145_381_785_895_507n],
    ['REFUND', 159_238_235_770_107n],
  ] as const)(
    '%s budget at 0.50 $ and 2,364.48 $/ETH = %s wei per transaction',
    (flow, expected) => {
      expect(capWeiPerTransaction(500_000n, flow, 2_364_480_000n)).toBe(expected);
    },
  );

  it('the per-gas ceiling is the budget divided by the gas limit, rounded down', () => {
    expect(feeCeilingPerGas(211_462_985_519_014n, 50_822n)).toBe(4_160_855_250n);
    expect(50_822n * 4_160_855_250n).toBeLessThanOrEqual(211_462_985_519_014n);
    expect(50_822n * 4_160_855_251n).toBeGreaterThan(211_462_985_519_014n);
    expect(feeCeilingPerGas(1n, 0n)).toBe(0n);
  });

  it('a record keeps its budget; a record signed before the change is read as per gas × gas limit', () => {
    expect(recordFeeCapWei({ ...RECORD, feeCapWeiPerGas: null, feeCapWei: '123' })).toBe(123n);
    expect(recordFeeCapWei({ ...RECORD, feeCapWeiPerGas: '3302593213' })).toBe(
      3_302_593_213n * 50_822n,
    );
    expect(recordFeeCapWei({ ...RECORD, feeCapWeiPerGas: null, feeCapWei: null })).toBeNull();
  });
});

describe('units (6 decimals, bigint only)', () => {
  it('converts both ways without float', () => {
    expect(toTokenUnits('9007199254740993.000001')).toBe(9_007_199_254_740_993_000_001n);
    expect(formatTokenUnits(12_345_678n)).toBe('12.345678');
    expect(formatTokenUnits(1_000_000n)).toBe('1');
  });

  it('parses USD and limits fail-closed', () => {
    expect(usdToMicros('0.50')).toBe(500_000n);
    expect(usdToMicros('0')).toBeNull();
    expect(usdToMicros('abc')).toBeNull();
    expect(parseLimit('5000', 6)).toBe(5_000_000_000n);
    expect(parseLimit('', 6)).toBeNull();
    expect(parseLimit('0', 6)).toBeNull();
    expect(parseLimit('1.0000001', 6)).toBeNull();
  });
});
