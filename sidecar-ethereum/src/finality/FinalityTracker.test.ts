import { describe, expect, it } from 'vitest';
import client from 'prom-client';
import { FinalityTracker } from './FinalityTracker.js';
import type { BlockHeader } from '../rpc/RpcClient.js';

const POLL_MS = 30_000;
const STALL_S = 1800;
const T0 = 1_800_000_000_000; // epoch ms

function tracker(read: () => Promise<BlockHeader>, clock: { now: number }) {
  return new FinalityTracker({
    rpc: { getBlockHeader: async () => read() },
    primaryProvider: 'alchemy',
    pollIntervalMs: POLL_MS,
    stallThresholdSeconds: STALL_S,
    now: () => clock.now,
  });
}

/** A finalized block produced <ageSeconds> before the clock. */
function head(clock: { now: number }, ageSeconds: number, provider = 'alchemy', number = 100n) {
  return { number, hash: '0x', timestamp: Math.floor(clock.now / 1000) - ageSeconds, provider };
}

async function metric(name: string): Promise<number | undefined> {
  const m = client.register.getSingleMetric(name);
  const values = (await m?.get())?.values ?? [];
  return values[0]?.value;
}

describe('FinalityTracker states (08 §3.7.4, §3.7.8)', () => {
  it('is starting before the first read finishes', () => {
    const clock = { now: T0 };
    expect(tracker(async () => head(clock, 900), clock).status().state).toBe('starting');
  });

  it('is ok with a fresh head from the primary (normal lag 12.8–18.8 min)', async () => {
    const clock = { now: T0 };
    const t = tracker(async () => head(clock, 1128), clock);
    await t.pollOnce();
    expect(t.status()).toMatchObject({ state: 'ok', ageSeconds: 1128 });
  });

  it('is backup when the last read came from the backup provider', async () => {
    const clock = { now: T0 };
    const t = tracker(async () => head(clock, 900, 'chainstack'), clock);
    await t.pollOnce();
    expect(t.status().state).toBe('backup');
  });

  it('is stalled once the finalized block is older than the threshold — even if reads succeed', async () => {
    const clock = { now: T0 };
    const t = tracker(async () => head(clock, STALL_S + 1), clock);
    await t.pollOnce();
    expect(t.status().state).toBe('stalled');
  });

  it('turns stalled by the clock alone when the chain stops finalizing', async () => {
    const clock = { now: T0 };
    const frozen = head(clock, 900);
    const t = tracker(async () => frozen, clock);
    await t.pollOnce();
    expect(t.status().state).toBe('ok');
    clock.now += (STALL_S - 900 + 1) * 1000;
    await t.pollOnce(); // the same block again
    expect(t.status().state).toBe('stalled');
  });

  it('is unreadable when no provider answers for three poll intervals', async () => {
    const clock = { now: T0 };
    let up = true;
    const t = tracker(async () => {
      if (!up) throw new Error('RPC_ALL_PROVIDERS_FAILED');
      return head(clock, 900);
    }, clock);
    await t.pollOnce();
    up = false;
    clock.now += 2 * POLL_MS;
    await t.pollOnce();
    expect(t.status().state).toBe('ok'); // one missed read is not an outage
    clock.now += 2 * POLL_MS;
    await t.pollOnce();
    expect(t.status()).toMatchObject({
      state: 'unreadable',
      lastError: 'RPC_ALL_PROVIDERS_FAILED',
    });
  });

  it('is unreadable when the very first read fails', async () => {
    const clock = { now: T0 };
    const t = tracker(async () => {
      throw new Error('down');
    }, clock);
    await t.pollOnce();
    expect(t.status().state).toBe('unreadable');
  });
});

describe('FinalityTracker metrics — the finality-lag signal', () => {
  it('publishes the block number, the stall threshold, and an age computed at scrape time', async () => {
    const clock = { now: T0 };
    const t = tracker(async () => head(clock, 600, 'alchemy', 26_214_908n), clock);
    await t.pollOnce();

    expect(await metric('skinora_ethereum_finalized_block_number')).toBe(26_214_908);
    expect(await metric('skinora_ethereum_finalized_stall_threshold_seconds')).toBe(STALL_S);
    expect(await metric('skinora_ethereum_finalized_block_age_seconds')).toBe(600);

    // No further read: the age keeps growing with the clock.
    clock.now += 120_000;
    expect(await metric('skinora_ethereum_finalized_block_age_seconds')).toBe(720);
  });
});
