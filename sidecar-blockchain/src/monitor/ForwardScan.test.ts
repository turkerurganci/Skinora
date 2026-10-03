import { describe, it, expect } from 'vitest';
import {
  MAX_PAGES_PER_POLL,
  pickEventIndex,
  RecordNotReadyError,
  scanForward,
  type ForwardCursor,
} from './ForwardScan.js';
import { FakeTronGridLedger, type LedgerRecord } from '../testing/FakeTronGridLedger.js';
import type { Trc20Record } from '../tron/TronGridClient.js';

const ADDRESS = 'TDeposit1234567890DepositAddrFakeXX';
const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const SPAM = 'TSpam111111111111111111111111111111';

function rec(txHash: string, blockTimestamp: number, token = USDT): LedgerRecord {
  return {
    transaction_id: txHash,
    token_info: { address: token, decimals: 6 },
    block_timestamp: blockTimestamp,
    from: 'TFrom111111111111111111111111111111',
    to: ADDRESS,
    type: 'Transfer',
    value: '1000000',
  };
}

/** n records three seconds apart (one per Tron block), oldest first. */
function series(prefix: string, n: number, firstTs: number, token = USDT): LedgerRecord[] {
  return Array.from({ length: n }, (_, i) => rec(`${prefix}-${i}`, firstTs + i * 3000, token));
}

const T0 = 1_790_000_000_000;

describe('FakeTronGridLedger reproduces the Nile probes it stands in for', () => {
  // The five records the probe address held on Nile, 2026-10-03.
  const NILE = [1787948814000, 1788372564000, 1788375435000, 1790111046000, 1790112792000];

  function nileLedger(): FakeTronGridLedger {
    const ledger = new FakeTronGridLedger();
    NILE.forEach((ts, i) => ledger.add(rec(`nile-${i}`, ts)));
    return ledger;
  }

  it('default order: page 1 is the newest, its fingerprint leads to OLDER records, the last page has none', async () => {
    const ledger = nileLedger();
    const p1 = await ledger.client.listTrc20({ address: ADDRESS, limit: 3 });
    const p2 = await ledger.client.listTrc20({
      address: ADDRESS,
      limit: 3,
      fingerprint: p1.fingerprint!,
    });

    expect(p1.records.map((r) => r.block_timestamp)).toEqual([NILE[4], NILE[3], NILE[2]]);
    expect(p2.records.map((r) => r.block_timestamp)).toEqual([NILE[1], NILE[0]]);
    expect(p2.fingerprint).toBeNull();
  });

  it('asc order: oldest first, the fingerprint continues forward', async () => {
    const ledger = nileLedger();
    const p1 = await ledger.client.listTrc20({ address: ADDRESS, limit: 3, order: 'asc' });
    const p2 = await ledger.client.listTrc20({
      address: ADDRESS,
      limit: 3,
      order: 'asc',
      fingerprint: p1.fingerprint!,
    });

    expect(p1.records.map((r) => r.block_timestamp)).toEqual([NILE[0], NILE[1], NILE[2]]);
    expect(p2.records.map((r) => r.block_timestamp)).toEqual([NILE[3], NILE[4]]);
  });

  it('min_timestamp is inclusive and floored to the second (+999 still returns T, +1000 does not)', async () => {
    const ledger = nileLedger();
    const at = async (min: number) =>
      (
        await ledger.client.listTrc20({ address: ADDRESS, order: 'asc', minTimestamp: min })
      ).records.map((r) => r.block_timestamp);

    expect(await at(NILE[3] + 999)).toEqual([NILE[3], NILE[4]]);
    expect(await at(NILE[3] + 1000)).toEqual([NILE[4]]);
  });

  it('the last page carries no fingerprint even when it is exactly limit long', async () => {
    const ledger = nileLedger();
    const page = await ledger.client.listTrc20({
      address: ADDRESS,
      limit: 1,
      order: 'asc',
      minTimestamp: NILE[4],
    });
    expect(page.records).toHaveLength(1);
    expect(page.fingerprint).toBeNull();
  });
});

describe('scanForward', () => {
  async function poll(
    ledger: FakeTronGridLedger,
    cursor: ForwardCursor,
    seen: string[],
  ): Promise<void> {
    await scanForward({
      client: ledger.client,
      address: ADDRESS,
      pageLimit: 20,
      cursor,
      handle: async (r: Trc20Record) => {
        seen.push(r.transaction_id);
      },
    });
  }

  it('sees a new transfer once the address holds 25 records (more than one page)', async () => {
    const ledger = new FakeTronGridLedger();
    ledger.add(...series('spam', 25, T0, SPAM));
    const cursor: ForwardCursor = {};
    const seen: string[] = [];

    await poll(ledger, cursor, seen);
    ledger.add(rec('late', T0 + 25 * 3000));
    await poll(ledger, cursor, seen);

    expect(seen.filter((h) => h === 'late')).toHaveLength(1);
    expect(new Set(seen).size).toBe(26);
  });

  it('reads oldest first and leaves the watermark on the newest record', async () => {
    const ledger = new FakeTronGridLedger();
    ledger.add(...series('tx', 3, T0));
    const cursor: ForwardCursor = {};
    const seen: string[] = [];

    await poll(ledger, cursor, seen);

    expect(seen).toEqual(['tx-0', 'tx-1', 'tx-2']);
    expect(cursor).toEqual({ minTimestamp: T0 + 6000, fingerprint: undefined });
    expect(ledger.listCalls[0]).toMatchObject({ order: 'asc', minTimestamp: undefined });
  });

  it('re-reads only the watermark block on a quiet poll', async () => {
    const ledger = new FakeTronGridLedger();
    ledger.add(...series('tx', 30, T0));
    const cursor: ForwardCursor = {};
    const first: string[] = [];
    await poll(ledger, cursor, first);

    const second: string[] = [];
    await poll(ledger, cursor, second);

    expect(second).toEqual(['tx-29']);
    expect(ledger.listCalls[ledger.listCalls.length - 1].minTimestamp).toBe(T0 + 29 * 3000);
  });

  it('a throw halfway through a page leaves that page to the next poll', async () => {
    const ledger = new FakeTronGridLedger();
    ledger.add(...series('tx', 3, T0));
    const cursor: ForwardCursor = {};
    const handled: string[] = [];
    let failOn: string | undefined = 'tx-1';

    await expect(
      scanForward({
        client: ledger.client,
        address: ADDRESS,
        pageLimit: 20,
        cursor,
        handle: async (r) => {
          if (r.transaction_id === failOn) throw new Error('webhook 503');
          handled.push(r.transaction_id);
        },
      }),
    ).rejects.toThrow('webhook 503');
    expect(cursor).toEqual({});

    failOn = undefined;
    await poll(ledger, cursor, handled);
    expect(handled).toEqual(['tx-0', 'tx-0', 'tx-1', 'tx-2']);
  });

  it('a record that is not ready lets the rest of its page through, then leaves that page to the next poll', async () => {
    const ledger = new FakeTronGridLedger();
    ledger.add(...series('tx', 3, T0));
    const cursor: ForwardCursor = {};
    const handled: string[] = [];
    let waitOn: string | undefined = 'tx-0';
    const handle = async (r: Trc20Record) => {
      if (r.transaction_id === waitOn) throw new RecordNotReadyError('node lags');
      handled.push(r.transaction_id);
    };

    await expect(
      scanForward({ client: ledger.client, address: ADDRESS, pageLimit: 20, cursor, handle }),
    ).rejects.toBeInstanceOf(RecordNotReadyError);
    expect(handled).toEqual(['tx-1', 'tx-2']);
    expect(cursor).toEqual({});

    waitOn = undefined;
    await scanForward({ client: ledger.client, address: ADDRESS, pageLimit: 20, cursor, handle });
    expect(handled).toEqual(['tx-1', 'tx-2', 'tx-0', 'tx-1', 'tx-2']);
    expect(cursor.minTimestamp).toBe(T0 + 6000);
  });

  it('a record that is not ready stops the scan at the end of its page — later pages wait', async () => {
    const ledger = new FakeTronGridLedger();
    ledger.add(...series('tx', 30, T0));
    const cursor: ForwardCursor = {};
    const handled: string[] = [];

    await expect(
      scanForward({
        client: ledger.client,
        address: ADDRESS,
        pageLimit: 20,
        cursor,
        handle: async (r) => {
          if (r.transaction_id === 'tx-25') throw new RecordNotReadyError('node lags');
          handled.push(r.transaction_id);
        },
      }),
    ).rejects.toBeInstanceOf(RecordNotReadyError);

    expect(handled).toHaveLength(29);
    expect(cursor.minTimestamp).toBe(T0 + 19 * 3000);
  });

  it('keeps the pages it finished when a later page fails', async () => {
    const ledger = new FakeTronGridLedger();
    ledger.add(...series('tx', 30, T0));
    const cursor: ForwardCursor = {};

    await expect(
      scanForward({
        client: ledger.client,
        address: ADDRESS,
        pageLimit: 20,
        cursor,
        handle: async (r) => {
          if (r.transaction_id === 'tx-25') throw new Error('webhook 503');
        },
      }),
    ).rejects.toThrow();

    expect(cursor.minTimestamp).toBe(T0 + 19 * 3000);
  });

  it(`stops at ${MAX_PAGES_PER_POLL} pages and continues on the next poll`, async () => {
    const ledger = new FakeTronGridLedger();
    ledger.add(...series('tx', 120, T0));
    const cursor: ForwardCursor = {};
    const seen: string[] = [];

    await poll(ledger, cursor, seen);
    expect(ledger.listCalls).toHaveLength(MAX_PAGES_PER_POLL);
    expect(new Set(seen).size).toBe(100);

    await poll(ledger, cursor, seen);
    expect(new Set(seen).size).toBe(120);
  });

  it('carries the fingerprint when a capped poll never left the watermark second', async () => {
    const ledger = new FakeTronGridLedger();
    // 120 transfers in one block — more than a capped poll reads.
    ledger.add(...Array.from({ length: 120 }, (_, i) => rec(`same-${i}`, T0)));
    ledger.add(rec('after', T0 + 3000));
    const cursor: ForwardCursor = { minTimestamp: T0 };
    const seen: string[] = [];

    await poll(ledger, cursor, seen);
    expect(cursor.minTimestamp).toBe(T0);
    expect(cursor.fingerprint).toBeDefined();

    await poll(ledger, cursor, seen);
    expect(new Set(seen).size).toBe(121);
    expect(seen).toHaveLength(121);
    expect(cursor).toEqual({ minTimestamp: T0 + 3000, fingerprint: undefined });
  });

  it('an empty address leaves the cursor at the start', async () => {
    const ledger = new FakeTronGridLedger();
    const cursor: ForwardCursor = {};
    await poll(ledger, cursor, []);
    expect(cursor).toEqual({ minTimestamp: undefined, fingerprint: undefined });
  });
});

describe('pickEventIndex', () => {
  const none = () => false;

  it('returns the log index matching the value', () => {
    expect(pickEventIndex([{ index: 2, value: '5' }], '5', none)).toBe(2);
  });

  it('gives two equal transfers in one transaction two indices', () => {
    const entries = [
      { index: 1, value: '5' },
      { index: 2, value: '5' },
    ];
    expect(pickEventIndex(entries, '5', (i) => i === 1)).toBe(2);
  });

  it('returns the already-reported index on a re-read — not 0', () => {
    expect(pickEventIndex([{ index: 2, value: '5' }], '5', (i) => i === 2)).toBe(2);
  });

  it('falls back to 0 when no log of the known transaction matches', () => {
    expect(pickEventIndex([], '5', none)).toBe(0);
    expect(pickEventIndex([{ index: 3, value: '7' }], '5', none)).toBe(0);
  });
});
