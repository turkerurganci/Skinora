import { describe, expect, it } from 'vitest';
import { DepositRegistry } from './DepositRegistry.js';
import { Reconciler } from '../reconcile/Reconciler.js';
import { FuelLedger } from '../reconcile/FuelLedger.js';
import { BUYER, T0, USDT, deposit, keysOf, monitorHarness } from '../testing/monitorHarness.js';

/**
 * T158 (h) — an outage longer than the restart read-back (08 §3.7.4 "İmleç",
 * 11 §F8 T151 kararları (xii)(a), yönetici kararı: kalıcı çözüm T158).
 *
 * The deposit is derived at block 1,000 (the backend persists that block as
 * `PaymentAddress.MonitorFromBlock`), the buyer pays at ~1,010, and the
 * service is down for ~10,000 blocks — longer than the default 7,200-block
 * read-back. After the restart the backend re-arms the deposit within a
 * minute. With the persisted block the payment is found by the log path and
 * a transfer the provider left out of `eth_getLogs` is caught by the first
 * reconciliation (zero baseline at the persisted block). The controls run the
 * same story without the block — the pre-T158 behaviour — and see neither.
 */

const LOOKBACK = 7_200;
const DERIVED_AT = 1_000n;
const OUTAGE_BLOCKS = 10_000n;

async function outageStory(opts: { persisted: boolean; hidden?: boolean }) {
  const h = monitorHarness({ restartLookbackBlocks: LOOKBACK, providerRange: 100 });
  h.chain.mineTo(DERIVED_AT);
  h.chain.finalize(DERIVED_AT);
  await h.scanner.tick(); // the first process: boots at 1,000
  const d = deposit(42);
  const startBody = {
    address: d,
    paymentAddressId: 'pa-outage',
    transactionId: 'tx-outage',
    expectedContract: USDT,
    expectedSymbol: 'USDT' as const,
    watchFromBlock: opts.persisted ? DERIVED_AT : null,
  };
  h.registry.startActive({ ...startBody, cadence: 'PAYMENT' });

  // The buyer pays; the service goes down before the block is finalized.
  h.chain.mineTo(DERIVED_AT + 9n);
  const pay = h.chain.mine([
    {
      contract: USDT,
      from: BUYER,
      to: d,
      value: 20_400_000n,
      hiddenFromLogs: opts.hidden === true,
    },
  ]);
  h.chain.mineTo(pay.block + OUTAGE_BLOCKS);
  h.chain.finalize(pay.block + OUTAGE_BLOCKS - 64n);
  h.clock.now += 36 * 3600_000; // ~33 h of blocks, the wall clock follows

  // Restart: memory is gone; the new process boots at the finalized head and
  // the backend's EnsurePaymentMonitorJob re-arms the deposit (now HOLDING if
  // the backend had seen the payment, PAYMENT otherwise — PAYMENT here).
  const registry = new DepositRegistry({ clock: () => new Date(h.clock.now) });
  const restarted = h.makeScanner(registry);
  await restarted.tick();
  registry.startActive({ ...startBody, cadence: 'PAYMENT' });
  h.sent.length = 0;
  for (let i = 0; i < 5; i++) await restarted.tick();
  return { h, d, pay, registry, restarted };
}

describe('T158 (h) — outage longer than the 7,200-block read-back', () => {
  it('with the persisted start block the payment made during the outage is reported, once, under its chain key', async () => {
    const { h, pay, registry } = await outageStory({ persisted: true });
    expect(pay.block).toBeLessThan(h.chain.finalized - BigInt(LOOKBACK));
    expect(keysOf(h.sent)).toEqual([
      `payment.detected:${pay.txHashes[0]}:0`,
      `payment.confirmed:${pay.txHashes[0]}:0`,
    ]);
    // The deposit is read from the block after the persisted one, not from the read-back.
    expect(registry.entries()[0].scan.scannedThrough).toBeGreaterThan(pay.block);
  });

  it('control (pre-T158, no persisted block): the restart read-back starts past the payment and the log path never sees it', async () => {
    const { h, pay } = await outageStory({ persisted: false });
    expect(pay.block).toBeLessThan(h.chain.finalized - BigInt(LOOKBACK));
    expect(h.sent).toEqual([]);
  });

  it('the reconciliation compares from a zero balance at the persisted block: a transfer the provider hid is a mismatch on the first reading', async () => {
    const { h, registry, restarted } = await outageStory({ persisted: true, hidden: true });
    expect(h.sent).toEqual([]); // the provider left it out of eth_getLogs
    const reconciler = new Reconciler({
      registry,
      finality: h.finality,
      scannedBlock: () => restarted.scannedBlock(),
      reader: h.reader,
      fuel: new FuelLedger(),
      intervalMs: 3_600_000,
      maxFinalizedAgeSeconds: 1500,
      now: () => h.clock.now,
    });
    expect(await reconciler.runOnce()).toMatchObject({
      kind: 'completed',
      deposits: 1,
      mismatches: 1,
    });
  });

  it('control (pre-T158): the same hidden transfer becomes the baseline of the re-armed deposit and is never reported', async () => {
    const { h, registry, restarted } = await outageStory({ persisted: false, hidden: true });
    const reconciler = new Reconciler({
      registry,
      finality: h.finality,
      scannedBlock: () => restarted.scannedBlock(),
      reader: h.reader,
      fuel: new FuelLedger(),
      intervalMs: 3_600_000,
      maxFinalizedAgeSeconds: 1500,
      now: () => h.clock.now,
    });
    expect(await reconciler.runOnce()).toMatchObject({ kind: 'completed', mismatches: 0 });
    expect(await reconciler.runOnce()).toMatchObject({ kind: 'completed', mismatches: 0 });
  });

  it('a post-cancel re-arm with the persisted block reports the payment as late (the backend refunds it), not lost', async () => {
    const h = monitorHarness({ restartLookbackBlocks: LOOKBACK, providerRange: 100 });
    h.chain.mineTo(DERIVED_AT);
    h.chain.finalize(DERIVED_AT);
    await h.scanner.tick();
    const d = deposit(43);
    const pay = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 5_000_000n }]);
    h.chain.mineTo(pay.block + OUTAGE_BLOCKS);
    h.chain.finalize(pay.block + OUTAGE_BLOCKS - 64n);
    const registry = new DepositRegistry({ clock: () => new Date(h.clock.now) });
    const restarted = h.makeScanner(registry);
    await restarted.tick();
    registry.startPostCancel({
      address: d,
      paymentAddressId: 'pa-pc',
      transactionId: 'tx-pc',
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      cancelledAt: new Date(h.clock.now - 3600_000),
      watchFromBlock: DERIVED_AT,
    });
    for (let i = 0; i < 5; i++) await restarted.tick();
    expect(keysOf(h.sent)).toEqual([`payment.late_detected:${pay.txHashes[0]}:0`]);
  });

  it('a persisted block above a lagging head is clamped to the head (never skips blocks the head has not reached)', async () => {
    const h = monitorHarness({ restartLookbackBlocks: LOOKBACK, providerRange: 100 });
    h.chain.mineTo(500n);
    h.chain.finalize(500n);
    await h.scanner.tick();
    h.registry.startActive({
      address: deposit(44),
      paymentAddressId: 'pa-lag',
      transactionId: 'tx-lag',
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      watchFromBlock: 900n,
    });
    await h.scanner.tick();
    expect(h.registry.entries()[0].scan.scannedThrough).toBe(500n);
  });
});

describe('T158 düzeltme turu — a re-start brings the start block the first arm lacked', () => {
  const startBody = (watchFromBlock: bigint | null) => ({
    address: deposit(45),
    paymentAddressId: 'pa-late-block',
    transactionId: 'tx-late-block',
    expectedContract: USDT,
    expectedSymbol: 'USDT' as const,
    watchFromBlock,
  });

  it('the registry takes the first start block and any earlier one, never a later one', () => {
    const registry = new DepositRegistry();
    registry.startActive(startBody(null));
    expect(registry.entries()[0].watchFromBlock).toBeNull();
    registry.startActive(startBody(900n));
    expect(registry.entries()[0].watchFromBlock).toBe(900n);
    registry.startActive(startBody(950n)); // later: would skip blocks
    expect(registry.entries()[0].watchFromBlock).toBe(900n);
    registry.startActive(startBody(null)); // a re-start without one keeps it
    expect(registry.entries()[0].watchFromBlock).toBe(900n);
    registry.startActive(startBody(800n));
    expect(registry.entries()[0].watchFromBlock).toBe(800n);
  });

  it('the same for a post-cancel entry', () => {
    const registry = new DepositRegistry({ clock: () => new Date(T0) });
    const pc = { ...startBody(null), cancelledAt: new Date(T0 - 3600_000) };
    registry.startPostCancel(pc);
    registry.startPostCancel({ ...pc, watchFromBlock: 700n });
    expect(registry.entries()[0].watchFromBlock).toBe(700n);
    registry.startPostCancel({ ...pc, watchFromBlock: 750n });
    expect(registry.entries()[0].watchFromBlock).toBe(700n);
  });

  async function lateBlockStory(withBlockOnRestart: boolean) {
    const h = monitorHarness({ restartLookbackBlocks: LOOKBACK, providerRange: 100 });
    h.chain.mineTo(DERIVED_AT);
    h.chain.finalize(DERIVED_AT);
    await h.scanner.tick();
    const d = deposit(45);
    h.chain.mineTo(DERIVED_AT + 9n);
    const pay = h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 9_000_000n }]);
    h.chain.mineTo(pay.block + OUTAGE_BLOCKS);
    h.chain.finalize(pay.block + OUTAGE_BLOCKS - 64n);
    h.clock.now += 36 * 3600_000;
    const registry = new DepositRegistry({ clock: () => new Date(h.clock.now) });
    const restarted = h.makeScanner(registry);
    await restarted.tick();
    // First arm without the block (e.g. an outbox row written before T158): the read-back applies.
    registry.startActive({ ...startBody(null), cadence: 'PAYMENT' });
    for (let i = 0; i < 3; i++) await restarted.tick();
    expect(h.sent).toEqual([]); // the payment is older than the read-back
    // The minute re-arm brings the persisted block.
    registry.startActive({
      ...startBody(withBlockOnRestart ? DERIVED_AT : null),
      cadence: 'PAYMENT',
    });
    for (let i = 0; i < 5; i++) await restarted.tick();
    return { h, pay, registry };
  }

  it('the scanner reads the address down to the new start block: the payment older than the read-back is reported once', async () => {
    const { h, pay, registry } = await lateBlockStory(true);
    expect(keysOf(h.sent)).toEqual([
      `payment.detected:${pay.txHashes[0]}:0`,
      `payment.confirmed:${pay.txHashes[0]}:0`,
    ]);
    const scan = registry.entries()[0].scan;
    expect(scan.coveredFrom).toBe(DERIVED_AT);
    expect(scan.history).toBeNull();
    expect(scan.baseline).toMatchObject({ block: DERIVED_AT, eth: 0n });
  });

  it('control: a re-start without the block leaves the payment unseen by the log path', async () => {
    const { h } = await lateBlockStory(false);
    expect(h.sent).toEqual([]);
  });
});
