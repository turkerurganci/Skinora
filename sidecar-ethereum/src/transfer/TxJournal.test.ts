import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { FeeWaitTracker, STALE_AFTER_MS } from './FeeWaitTracker.js';
import { capShareWei, feeCeilingPerGas } from './FeePolicy.js';
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

describe('fee cap arithmetic (08 §3.7.7; T159a — shared by estimated gas)', () => {
  const WHOLE_CAP = 211_462_985_519_014n; // 0.50 $ ÷ 2,364.48 $/ETH, wei (T142's price)

  it('a transaction with no fuel leg gets the whole cap, whatever its gas', () => {
    for (const gas of [21_000n, 50_822n, 70_434n, 168_300n, 1_100_000n]) {
      expect(capShareWei(500_000n, 2_364_480_000n, gas, gas)).toBe(WHOLE_CAP);
    }
  });

  it('a missing price, cap or gas gives a zero share (nothing passes)', () => {
    expect(capShareWei(500_000n, 0n, 50_822n, 50_822n)).toBe(0n);
    expect(capShareWei(0n, 2_364_480_000n, 50_822n, 50_822n)).toBe(0n);
    expect(capShareWei(500_000n, 2_364_480_000n, 0n, 50_822n)).toBe(0n);
    // A flow cannot be smaller than one of its own transactions.
    expect(capShareWei(500_000n, 2_364_480_000n, 50_822n, 50_821n)).toBe(0n);
  });

  it.each([
    // Mainnet tariff: sweep 46,201 → limit 50,822, fuel to a new account 21,000.
    ['mainnet sweep', 50_822n, 21_000n],
    // Mainnet refund to a first-time recipient: 64,031 → 70,435 + fuel 21,000.
    ['mainnet first-time refund', 70_435n, 21_000n],
    // Sepolia 2026-10-19: sweep ~48,000 → 52,800; fuel to a new account 207,391.
    ['Sepolia sweep', 52_800n, 207_391n],
    // Sepolia: first USDT receipt 153,000 → 168,300; fuel 207,391.
    ['Sepolia first-time refund', 168_300n, 207_391n],
  ])(
    '%s: token and fuel shares are the one cap split by their estimated gas, never more than it',
    (_label, token, fuel) => {
      const tokenShare = capShareWei(500_000n, 2_364_480_000n, token, token + fuel);
      const fuelShare = capShareWei(500_000n, 2_364_480_000n, fuel, token + fuel);
      expect(tokenShare + fuelShare).toBeLessThanOrEqual(WHOLE_CAP);
      // Both priced at the flow's one ceiling per gas, rounded down.
      const perGas = WHOLE_CAP / (token + fuel);
      expect(feeCeilingPerGas(tokenShare, token)).toBeGreaterThanOrEqual(perGas - 1n);
      expect(feeCeilingPerGas(fuelShare, fuel)).toBeGreaterThanOrEqual(perGas - 1n);
      expect(token * feeCeilingPerGas(tokenShare, token)).toBeLessThanOrEqual(tokenShare);
      expect(fuel * feeCeilingPerGas(fuelShare, fuel)).toBeLessThanOrEqual(fuelShare);
    },
  );

  it('the worked example: 0.50 $ over a mainnet sweep flow (50,822 + 21,000 gas) at 2,364.48 $ is ~2.94 gwei per gas', () => {
    const tokenShare = capShareWei(500_000n, 2_364_480_000n, 50_822n, 71_822n);
    expect(tokenShare).toBe(149_633_424_995_786n);
    expect(feeCeilingPerGas(tokenShare, 50_822n)).toBe(2_944_264_786n);
  });

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
