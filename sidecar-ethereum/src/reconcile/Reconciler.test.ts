import { describe, expect, it } from 'vitest';
import client from 'prom-client';
import { Reconciler } from './Reconciler.js';
import { FuelLedger } from './FuelLedger.js';
import { RatePacer } from './RatePacer.js';
import { BalanceReader } from './BalanceReader.js';
import { BUYER, HOT, USDT, deposit, monitorHarness } from '../testing/monitorHarness.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';

/**
 * Hourly balance reconciliation (T151 — 08 §3.7.4 "Bakiye mutabakatı",
 * "Yerel ETH", §3.7.8 "Sağlayıcı hata vermeden eksik log döndürür").
 */

async function counter(name: string, labels: Record<string, string> = {}): Promise<number> {
  const metric = client.register.getSingleMetric(name);
  const values = (await metric?.get())?.values ?? [];
  return values
    .filter((v) => Object.entries(labels).every(([k, val]) => v.labels[k] === val))
    .reduce((s, v) => s + v.value, 0);
}

/** A monitor with one fresh deposit, scanned up to a finalized head past its first payment. */
async function setup(opts: { hiddenPayment?: boolean; fresh?: boolean } = {}) {
  const h = monitorHarness();
  h.chain.mineTo(100n);
  h.chain.finalize(100n);
  await h.scanner.tick();
  h.clock.now += 5 * 60_000 + 1;
  const d = deposit(1);
  h.arm(d, opts.fresh === false ? { cadence: 'HOLDING' } : {});
  await h.scanner.tick();
  const fuel = new FuelLedger();
  const finalityAge = { seconds: 900 };
  const finality = (): FinalityStatus => ({ ...h.finality(), ageSeconds: finalityAge.seconds });
  const reconciler = new Reconciler({
    registry: h.registry,
    finality,
    scannedBlock: () => h.scanner.scannedBlock(),
    reader: h.reader,
    fuel,
    intervalMs: 3_600_000,
    maxFinalizedAgeSeconds: 1500,
    now: () => h.clock.now,
  });
  return { h, d, fuel, reconciler, finalityAge };
}

async function advance(h: ReturnType<typeof monitorHarness>, blocks: bigint) {
  h.chain.mineTo(h.chain.latest + blocks);
  h.chain.finalize(h.chain.latest - 5n);
  await h.scanner.tick();
}

describe('token balance ↔ observed transfers', () => {
  it('a deposit whose balance moved exactly by the seen transfers is balanced', async () => {
    const { h, d, reconciler } = await setup();
    h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 20_000_000n }]);
    h.chain.mine([{ contract: USDT, from: d, to: HOT, value: 5_000_000n }]);
    await advance(h, 20n);
    const before = await counter('skinora_ethereum_reconciliation_mismatch_total');
    const out = await reconciler.runOnce();
    expect(out).toMatchObject({ kind: 'completed', deposits: 1, mismatches: 0, nativeEth: 0 });
    expect(await counter('skinora_ethereum_reconciliation_mismatch_total')).toBe(before);
  });

  it('a payment the provider left out of eth_getLogs is caught on the first reading of a fresh deposit', async () => {
    const { h, d, reconciler } = await setup();
    h.chain.mine([
      { contract: USDT, from: BUYER, to: d, value: 20_000_000n, hiddenFromLogs: true },
    ]);
    await advance(h, 20n);
    expect(h.sent).toEqual([]); // the log path did not see it
    const before = await counter('skinora_ethereum_reconciliation_mismatch_total', {
      asset: 'USDT',
    });
    const out = await reconciler.runOnce();
    expect(out).toMatchObject({ kind: 'completed', mismatches: 1 });
    expect(await counter('skinora_ethereum_reconciliation_mismatch_total', { asset: 'USDT' })).toBe(
      before + 1,
    );
  });

  it('between two readings, a hidden transfer is a mismatch once (the next round re-baselines)', async () => {
    const { h, d, reconciler } = await setup({ fresh: false });
    await advance(h, 10n);
    expect(await reconciler.runOnce()).toMatchObject({ kind: 'completed', mismatches: 0 }); // baseline
    h.chain.mine([{ contract: USDT, from: BUYER, to: d, value: 1n, hiddenFromLogs: true }]);
    await advance(h, 20n);
    expect(await reconciler.runOnce()).toMatchObject({ mismatches: 1 });
    await advance(h, 20n);
    expect(await reconciler.runOnce()).toMatchObject({ mismatches: 0 });
  });

  it('a deposit still being read back is left for the next round', async () => {
    const h = monitorHarness({ catchUpBlocksPerTick: 1 });
    h.chain.mineTo(300n);
    h.chain.finalize(300n);
    await h.scanner.tick();
    h.arm(deposit(2), { cadence: 'HOLDING' });
    await h.scanner.tick();
    const reconciler = new Reconciler({
      registry: h.registry,
      finality: h.finality,
      scannedBlock: () => h.scanner.scannedBlock(),
      reader: h.reader,
      fuel: new FuelLedger(),
      intervalMs: 3_600_000,
      maxFinalizedAgeSeconds: 1500,
    });
    expect(await reconciler.runOnce()).toMatchObject({ kind: 'completed', deposits: 0 });
  });
});

describe('native ETH on a deposit (no Transfer log, no automatic refund)', () => {
  it('ETH that is not recorded fuel is an alarm', async () => {
    const { h, d, reconciler } = await setup();
    h.chain.mine([], [{ to: d, wei: 10n ** 16n }]);
    await advance(h, 20n);
    const before = await counter('skinora_ethereum_native_eth_received_total');
    expect(await reconciler.runOnce()).toMatchObject({ nativeEth: 1, mismatches: 0 });
    expect(await counter('skinora_ethereum_native_eth_received_total')).toBe(before + 1);
    expect(h.sent).toEqual([]); // nothing reported, nothing refunded
  });

  it('fuel the fuel wallet sent (recorded with its block) is not an alarm', async () => {
    const { h, d, reconciler, fuel } = await setup();
    const { block } = h.chain.mine([], [{ to: d, wei: 2_000_000_000_000n }]);
    fuel.record(d, 2_000_000_000_000n, block);
    await advance(h, 20n);
    expect(await reconciler.runOnce()).toMatchObject({ nativeEth: 0 });
  });

  it('fuel recorded for other blocks does not excuse an ETH payment', async () => {
    const { h, d, reconciler, fuel } = await setup();
    const { block } = h.chain.mine([], [{ to: d, wei: 5n }]);
    fuel.record(d, 5n, block + 1000n);
    await advance(h, 20n);
    expect(await reconciler.runOnce()).toMatchObject({ nativeEth: 1 });
  });
});

describe('never silent when it cannot reconcile', () => {
  it('skips a round when the finalized head is older than 25 min (archive depth) and counts it', async () => {
    const { reconciler, finalityAge } = await setup();
    finalityAge.seconds = 1501;
    const before = await counter('skinora_ethereum_reconciliation_skipped_total', {
      reason: 'finalized_stale',
    });
    expect(await reconciler.runOnce()).toEqual({ kind: 'skipped', reason: 'finalized_stale' });
    expect(
      await counter('skinora_ethereum_reconciliation_skipped_total', { reason: 'finalized_stale' }),
    ).toBe(before + 1);
  });

  it('a failing read (an archive refusal, a dead provider) skips the round and leaves the last success as it was', async () => {
    const { h, reconciler } = await setup();
    await advance(h, 10n);
    const last = await counter('skinora_ethereum_reconciliation_last_success_timestamp_seconds');
    h.clock.now += 3_600_000;
    h.chain.down = true;
    expect(await reconciler.runOnce()).toEqual({ kind: 'skipped', reason: 'read_failed' });
    expect(await counter('skinora_ethereum_reconciliation_last_success_timestamp_seconds')).toBe(
      last,
    );
  });
});

describe('spread under the provider rate limit (Alchemy Free 300 CU/s)', () => {
  it('300 deposits read USDT + ETH (46 CU each) take at least ~46 s at 300 CU/s', async () => {
    let now = 0;
    let slept = 0;
    const pacer = new RatePacer({
      cuPerSecond: 300,
      now: () => now,
      sleep: async (ms) => {
        slept += ms;
        now += ms;
      },
    });
    const rpc = {
      getBalance: async () => 0n,
      call: async () => '0x' + '0'.repeat(64),
    };
    const reader = new BalanceReader(rpc, { USDT, USDC: '' }, pacer);
    expect(reader.costPerAddress()).toBe(46);
    for (let i = 0; i < 300; i++) await reader.read(deposit(i), 1n);
    expect(slept / 1000).toBeGreaterThanOrEqual(45.9);
  });

  it('the default budget (200 CU/s) with USDT + USDC + ETH spreads 300 deposits over ~108 s', async () => {
    expect(RatePacer.minimumSeconds(300 * (20 + 26 + 26), 200)).toBe(108);
  });
});
