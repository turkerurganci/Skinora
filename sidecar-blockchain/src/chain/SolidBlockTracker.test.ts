import { describe, expect, it } from 'vitest';
import { SolidBlockTracker } from './SolidBlockTracker.js';
import {
  solidBlockAgeSeconds,
  solidBlockNumber,
  solidBlockReadFailuresTotal,
  solidBlockStallThresholdSeconds,
} from '../metrics.js';
import { positiveInt } from '../config/index.js';

async function gaugeValue(metric: { get(): Promise<{ values: { value: number }[] }> }) {
  const { values } = await metric.get();
  return values[0]?.value;
}

describe('SolidBlockTracker (T170 — tron-solid-block-stalled)', () => {
  it('publishes the solid block number, the stall threshold, and its age from the block timestamp', async () => {
    let now = 1_800_000_000_000;
    const tracker = new SolidBlockTracker({
      client: {
        getNowSolidBlockHeader: async () => ({ number: 70_000_000, timestampMs: now - 57_000 }),
      },
      pollIntervalMs: 60_000,
      stallThresholdSeconds: 300,
      now: () => now,
    });

    await tracker.pollOnce();

    expect(await gaugeValue(solidBlockNumber)).toBe(70_000_000);
    expect(await gaugeValue(solidBlockStallThresholdSeconds)).toBe(300);
    expect(await gaugeValue(solidBlockAgeSeconds)).toBe(57);

    // No new read: the age keeps growing at scrape time.
    now += 600_000;
    expect(await gaugeValue(solidBlockAgeSeconds)).toBe(657);
  });

  it('keeps the last block on a failed read, counts the failure, and the age grows past the threshold', async () => {
    let now = 1_800_000_000_000;
    let fail = false;
    const tracker = new SolidBlockTracker({
      client: {
        getNowSolidBlockHeader: async () => {
          if (fail) throw new Error('ECONNREFUSED');
          return { number: 10, timestampMs: now };
        },
      },
      pollIntervalMs: 60_000,
      stallThresholdSeconds: 300,
      now: () => now,
    });
    await tracker.pollOnce();
    const failuresBefore = (await solidBlockReadFailuresTotal.get()).values[0]?.value ?? 0;

    fail = true;
    now += 301_000;
    await tracker.pollOnce();

    expect(tracker.snapshot()?.number).toBe(10);
    expect((await solidBlockReadFailuresTotal.get()).values[0]?.value).toBe(failuresBefore + 1);
    expect(tracker.ageSeconds()).toBe(301);
    expect(tracker.ageSeconds() / 300).toBeGreaterThan(1);
  });

  it('counts the age from start-up until the first successful read (never a silent series)', async () => {
    let now = 1_800_000_000_000;
    const tracker = new SolidBlockTracker({
      client: {
        getNowSolidBlockHeader: async () => {
          throw new Error('getaddrinfo ENOTFOUND');
        },
      },
      pollIntervalMs: 60_000,
      stallThresholdSeconds: 300,
      now: () => now,
    });
    await tracker.pollOnce();
    now += 400_000;

    expect(tracker.snapshot()).toBeNull();
    expect(tracker.ageSeconds()).toBe(400);
    expect(await gaugeValue(solidBlockAgeSeconds)).toBe(400);
  });

  it('does not step back to an older block from a lagging node', async () => {
    const answers = [
      { number: 100, timestampMs: 1_000_000 },
      { number: 99, timestampMs: 997_000 },
    ];
    const tracker = new SolidBlockTracker({
      client: { getNowSolidBlockHeader: async () => answers.shift()! },
      pollIntervalMs: 60_000,
      stallThresholdSeconds: 300,
      now: () => 1_010_000,
    });
    await tracker.pollOnce();
    await tracker.pollOnce();
    expect(tracker.snapshot()?.number).toBe(100);
  });

  it('falls back to the read time when the node gives no timestamp', async () => {
    const tracker = new SolidBlockTracker({
      client: { getNowSolidBlockHeader: async () => ({ number: 5, timestampMs: null }) },
      pollIntervalMs: 60_000,
      stallThresholdSeconds: 300,
      now: () => 2_000_000,
    });
    await tracker.pollOnce();
    expect(tracker.snapshot()?.timestampMs).toBe(2_000_000);
  });
});

describe('positiveInt (T170 config)', () => {
  it.each([
    [undefined, 300],
    ['', 300],
    ['abc', 300],
    ['0', 300],
    ['-5', 300],
    ['1.5', 300],
    ['600', 600],
  ])('%s → %s', (raw, expected) => {
    expect(positiveInt(raw, 300)).toBe(expected);
  });
});
