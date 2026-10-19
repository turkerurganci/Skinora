import { describe, expect, it } from 'vitest';
import { FuelLedger } from '../reconcile/FuelLedger.js';
import { Reconciler } from '../reconcile/Reconciler.js';
import { BUYER, T0, USDT, deposit, keysOf, monitorHarness } from '../testing/monitorHarness.js';
import { HISTORY_TICK_SHARE, LIVE_TICK_SHARE, type PaymentScanner } from './PaymentScanner.js';

/**
 * T158 düzeltme turu — two lanes (yönetici kararı; 08 §3.7.4 "İki şerit").
 *
 * After a restart the backend re-arms every open deposit from the block it was
 * derived at (T158 h). The validation measured the single-lane read: a fresh
 * payment waited until the oldest deposit's history was read — ~29 min behind
 * an 8-day HOLDING deposit, ~1.8 h behind a 30-day post-cancel one (twice that
 * at the rate budget) → payment timeout → wrong cancel.
 *
 * The stories below run the service's default tempo: a 15-s tick, 500
 * catch-up blocks per tick, the 7,200-block read-back, the 5-min re-arm
 * window; "paced" adds the real rate budget (200 CU/s, a 10-block provider
 * range) with its waits moving the clock. The chain follows the wall clock
 * (one block per 12 s) and is finalized at once: the finality lag is the same
 * before and after the fix and is left out of the delays measured here.
 *
 * Pre-T158 tempo for a fresh payment (the yardstick): reported by the first
 * tick after its block — at most one tick plus one block, ≤ 30 s (measured
 * with the pre-T158 rules in the same stories: 15 s, 30 s while a restart's
 * 7,200-block read-back is caught up at the rate budget). Measured with the
 * single lane (HEAD 22c75eb6): 6,180 s / 12,660 s paced behind the 30-day
 * deposit, 1,425 s / 3,135 s paced behind the 8-day one. With two lanes: 15 s
 * in every story.
 */

const DAY_BLOCKS = 7_200n;
const BLOCK_MS = 12_000;
const TICK_MS = 15_000;
const PRE_T158_FRESH_PAYMENT_MS = 30_000;
/** One chunk of a lane is about a second of the rate budget (2 × 60 CU at 200 CU/s), plus slack. */
const CHUNK_MS = 1_000;
const DERIVED_AT = 1_000n;

type Harness = ReturnType<typeof monitorHarness>;

/** One `setInterval` tick: a tick that ran over skips the ticks it overlapped. */
async function tick(h: Harness, scanner: PaymentScanner, blockAt: (t: number) => bigint) {
  const start = h.clock.now;
  const target = blockAt(start);
  if (target > h.chain.latest) h.chain.mineTo(target);
  if (h.chain.latest > h.chain.finalized) h.chain.finalize(h.chain.latest);
  await scanner.tick();
  const spent = h.clock.now - start;
  h.clock.now = start + TICK_MS * Math.max(1, Math.ceil(spent / TICK_MS));
}

interface StoryOptions {
  /** Age of the old deposit's start block. */
  oldAgeDays: number;
  oldKind: 'post_cancel' | 'holding';
  /** When the fresh deposit is derived, after the restart. */
  freshAfterMs: number;
  paced: boolean;
  /**
   * Who the buyer pays: a deposit derived after the restart (default), or a
   * PAYMENT deposit derived 2 h before it and re-armed with the old one — its
   * start block (600 blocks back) is more than one tick's catch-up budget.
   */
  payTo?: 'fresh' | 'rearmed_payment';
}

async function story(o: StoryOptions) {
  const h = monitorHarness({
    restartLookbackBlocks: 7_200,
    catchUpBlocksPerTick: 500,
    rearmWindowMs: 300_000,
    providerRange: o.paced ? 10 : 1_000,
    cuPerSecond: o.paced ? 200 : undefined,
    tickIntervalMs: TICK_MS,
  });
  // The old deposit: derived at DERIVED_AT, a late transfer in its first day
  // and one just before the restart's read-back (the end of its history).
  const old = deposit(70);
  h.chain.mineTo(DERIVED_AT + 5n);
  const oldPay = h.chain.mine([{ contract: USDT, from: BUYER, to: old, value: 7_000_000n }]);
  const restartBlock = DERIVED_AT + DAY_BLOCKS * BigInt(o.oldAgeDays);
  h.chain.mineTo(restartBlock - 7_210n);
  const oldEdge = h.chain.mine([{ contract: USDT, from: BUYER, to: old, value: 3_000_000n }]);
  h.chain.mineTo(restartBlock);
  h.chain.finalize(restartBlock);
  h.clock.now = T0 + o.oldAgeDays * 86_400_000;
  const restartAt = h.clock.now;
  const blockAt = (t: number) => restartBlock + BigInt(Math.floor((t - restartAt) / BLOCK_MS));

  // Restart: a new process boots at the finalized head; the backend re-arms
  // the old deposit with its persisted start block within a minute.
  const scanner = h.makeScanner();
  await tick(h, scanner, blockAt);
  const oldStart = {
    address: old,
    paymentAddressId: 'pa-old',
    transactionId: 'tx-old',
    expectedContract: USDT,
    expectedSymbol: 'USDT' as const,
    watchFromBlock: DERIVED_AT,
  };
  if (o.oldKind === 'post_cancel') {
    h.registry.startPostCancel({
      ...oldStart,
      cancelledAt: new Date(restartAt - (o.oldAgeDays - 1) * 86_400_000),
    });
  } else {
    h.registry.startActive({ ...oldStart, cadence: 'HOLDING' });
  }
  const fresh = deposit(71);
  const freshStart = {
    address: fresh,
    paymentAddressId: 'pa-fresh',
    transactionId: 'tx-fresh',
    expectedContract: USDT,
    expectedSymbol: 'USDT' as const,
    cadence: 'PAYMENT' as const,
  };
  if (o.payTo === 'rearmed_payment') {
    h.registry.startActive({ ...freshStart, watchFromBlock: restartBlock - 600n });
  }

  while (h.clock.now < restartAt + o.freshAfterMs) await tick(h, scanner, blockAt);

  if (o.payTo !== 'rearmed_payment') {
    // A fresh deposit: derived a minute ago (its start block a few blocks
    // behind the monitor's cursor), armed now.
    h.registry.startActive({ ...freshStart, watchFromBlock: h.chain.finalized - 5n });
  }
  await tick(h, scanner, blockAt);
  const pay = h.chain.mine([{ contract: USDT, from: BUYER, to: fresh, value: 20_400_000n }]);
  const paidAt = h.clock.now;
  const detected = `payment.detected:${pay.txHashes[0]}:0`;
  let ticks = 0;
  while (!keysOf(h.sent).includes(detected) && ticks < 2_000) {
    await tick(h, scanner, blockAt);
    ticks++;
  }
  return {
    h,
    scanner,
    blockAt,
    old,
    oldPay,
    oldEdge,
    fresh,
    pay,
    delayMs: h.clock.now - paidAt,
    oldEntry: () => h.registry.entries().find((e) => e.address === old)!,
  };
}

describe('T158 düzeltme turu — two lanes: a long history never delays a fresh payment', () => {
  it.each([
    { oldAgeDays: 30, oldKind: 'post_cancel' as const, paced: false },
    { oldAgeDays: 30, oldKind: 'post_cancel' as const, paced: true },
    { oldAgeDays: 8, oldKind: 'holding' as const, paced: false },
    { oldAgeDays: 8, oldKind: 'holding' as const, paced: true },
  ])(
    '$oldAgeDays-day $oldKind deposit re-armed, fresh payment 1 h after the restart (paced: $paced) — reported at pre-T158 tempo',
    async (o) => {
      const s = await story({ ...o, freshAfterMs: 3_600_000 });
      expect(s.delayMs).toBeLessThanOrEqual(PRE_T158_FRESH_PAYMENT_MS);
      expect(keysOf(s.h.sent).filter((k) => k.includes(s.pay.txHashes[0]))).toEqual([
        `payment.detected:${s.pay.txHashes[0]}:0`,
        `payment.confirmed:${s.pay.txHashes[0]}:0`,
      ]);
    },
  );

  it.each([
    { oldAgeDays: 30, oldKind: 'post_cancel' as const, paced: false },
    { oldAgeDays: 30, oldKind: 'post_cancel' as const, paced: true },
  ])(
    'a PAYMENT deposit derived 2 h before the restart, re-armed with a $oldAgeDays-day $oldKind one; the buyer pays 1 h after the restart (paced: $paced) — reported at pre-T158 tempo',
    async (o) => {
      const s = await story({ ...o, freshAfterMs: 3_600_000, payTo: 'rearmed_payment' });
      expect(s.delayMs).toBeLessThanOrEqual(PRE_T158_FRESH_PAYMENT_MS);
      expect(s.oldEntry().scan.history).not.toBeNull(); // the old history is still being read
    },
  );

  it('fresh payment right after the re-arm window, while the re-armed deposit is still catching up its read-back day (paced) — the short gap goes first', async () => {
    const s = await story({
      oldAgeDays: 8,
      oldKind: 'holding',
      paced: true,
      freshAfterMs: 300_000 + TICK_MS,
    });
    // The old deposit's live lane is still behind the cursor: the fresh
    // deposit did not queue behind it.
    expect(s.oldEntry().scan.scannedThrough!).toBeLessThan(s.scanner.scannedBlock()!);
    expect(s.delayMs).toBeLessThanOrEqual(PRE_T158_FRESH_PAYMENT_MS);
  });

  it('separation: when the fresh payment is reported the 30-day history is still being read — the delay a single lane would have added — and it finishes later, reporting the old transfer once', async () => {
    const s = await story({
      oldAgeDays: 30,
      oldKind: 'post_cancel',
      paced: false,
      freshAfterMs: 300_000 + TICK_MS,
    });
    const history = s.oldEntry().scan.history;
    expect(history).not.toBeNull();
    // A single lane reads oldest-first: the fresh payment would have waited
    // for every block still in front of it — ≥ 1.5 h more at 500 blocks a tick.
    const blocksAhead = Number(history!.through - history!.next + 1n);
    expect((blocksAhead / 500) * TICK_MS).toBeGreaterThan(1.5 * 3_600_000);

    let ticks = 0;
    while (s.oldEntry().scan.history !== null && ticks < 1_000) {
      await tick(s.h, s.scanner, s.blockAt);
      ticks++;
    }
    expect(s.oldEntry().scan.history).toBeNull();
    const keys = keysOf(s.h.sent);
    expect(keys.filter((k) => k.includes(s.oldPay.txHashes[0]))).toEqual([
      `payment.late_detected:${s.oldPay.txHashes[0]}:0`,
    ]);
    // …and the one at the very end of the history, next to the live lane.
    expect(keys.filter((k) => k.includes(s.oldEdge.txHashes[0]))).toEqual([
      `payment.late_detected:${s.oldEdge.txHashes[0]}:0`,
    ]);
    expect(keys.filter((k) => k.includes(s.pay.txHashes[0]))).toHaveLength(2); // never again
  });

  it('the history lane stops at its share of the tick (paced): the next head read never waits behind it', async () => {
    const s = await story({
      oldAgeDays: 30,
      oldKind: 'post_cancel',
      paced: true,
      freshAfterMs: 3_600_000,
    });
    expect(s.oldEntry().scan.history).not.toBeNull();
    for (let i = 0; i < 5; i++) {
      const start = s.h.clock.now;
      await tick(s.h, s.scanner, s.blockAt);
      expect(s.h.clock.now - start).toBe(TICK_MS); // no tick skipped
    }
    const start = s.h.clock.now;
    await s.scanner.tick();
    expect(s.h.clock.now - start).toBeLessThanOrEqual(HISTORY_TICK_SHARE * TICK_MS + CHUNK_MS);
  });

  it('a restart read-back caught up at the rate budget ends every tick before the next is due (paced): no tick is skipped', async () => {
    const h = monitorHarness({
      restartLookbackBlocks: 7_200,
      catchUpBlocksPerTick: 500,
      providerRange: 10,
      cuPerSecond: 200,
      tickIntervalMs: TICK_MS,
    });
    h.chain.mineTo(20_000n);
    h.chain.finalize(20_000n);
    const scanner = h.makeScanner();
    await scanner.tick();
    h.arm(deposit(72), { cadence: 'HOLDING' }); // re-armed, no start block: a day of read-back
    for (let i = 0; i < 5; i++) {
      const start = h.clock.now;
      await scanner.tick();
      expect(h.clock.now - start).toBeLessThanOrEqual(LIVE_TICK_SHARE * TICK_MS + CHUNK_MS);
      expect(h.clock.now - start).toBeLessThan(TICK_MS);
      h.clock.now = start + TICK_MS;
    }
    expect(h.registry.entries()[0].scan.scannedThrough!).toBeLessThan(scanner.scannedBlock()!);
  });

  it('the reconciliation leaves a deposit out while its history is unread and compares it from the zero baseline once read', async () => {
    const s = await story({
      oldAgeDays: 30,
      oldKind: 'post_cancel',
      paced: false,
      freshAfterMs: 3_600_000,
    });
    const reconciler = new Reconciler({
      registry: s.h.registry,
      finality: s.h.finality,
      scannedBlock: () => s.scanner.scannedBlock(),
      reader: s.h.reader,
      fuel: new FuelLedger(),
      intervalMs: 3_600_000,
      maxFinalizedAgeSeconds: 1500,
      now: () => s.h.clock.now,
    });
    // Only the fresh deposit: comparing the old one now would miss the
    // transfer its unread history holds — a false mismatch.
    expect(await reconciler.runOnce()).toMatchObject({
      kind: 'completed',
      deposits: 1,
      mismatches: 0,
    });
    let ticks = 0;
    while (s.oldEntry().scan.history !== null && ticks < 1_000) {
      await tick(s.h, s.scanner, s.blockAt);
      ticks++;
    }
    expect(await reconciler.runOnce()).toMatchObject({
      kind: 'completed',
      deposits: 2,
      mismatches: 0,
    });
  });

  it('a tick whose time is already used up (slow head reads) still moves each lane by one chunk — neither stalls for good', async () => {
    const h = monitorHarness({
      restartLookbackBlocks: 7_200,
      catchUpBlocksPerTick: 500,
      providerRange: 10,
      cuPerSecond: 200,
      tickIntervalMs: 1, // every deadline is past after the first call
    });
    h.chain.mineTo(20_000n);
    h.chain.finalize(20_000n);
    const scanner = h.makeScanner();
    // Each tick a new finalized block: the head read spends pacer time first.
    const slowTick = async () => {
      h.chain.mineTo(h.chain.latest + 1n);
      h.chain.finalize(h.chain.latest);
      await scanner.tick();
    };
    await slowTick();
    h.arm(deposit(73), { cadence: 'HOLDING', watchFromBlock: 1_000n }); // re-armed: live + history
    // A deposit in the head lane: the head read sends requests and spends the tick.
    h.arm(deposit(74), { cadence: 'PAYMENT', watchFromBlock: h.chain.finalized });
    await slowTick();
    const scan = h.registry.entries().find((e) => e.address === deposit(73))!.scan;
    const live0 = scan.scannedThrough!;
    for (let i = 0; i < 3; i++) await slowTick();
    expect(scan.scannedThrough!).toBeGreaterThan(live0);
    // Once the live lane has caught up, the history moves too.
    for (let i = 0; i < 2_000 && scan.scannedThrough! < scanner.scannedBlock()!; i++) {
      await slowTick();
    }
    expect(scan.scannedThrough!).toBe(scanner.scannedBlock()!);
    const history0 = scan.history!.next;
    for (let i = 0; i < 3; i++) await slowTick();
    expect(scan.history!.next).toBeGreaterThan(history0);
  });
});
