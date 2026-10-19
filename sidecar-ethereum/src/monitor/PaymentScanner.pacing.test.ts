import { describe, expect, it } from 'vitest';
import { CU_COST, RatePacer } from '../reconcile/RatePacer.js';
import { deposit, monitorHarness } from '../testing/monitorHarness.js';

/**
 * T158 düzeltme turu (i) — the scanner paces per RPC call, so a balance
 * request (the backend's ledger reconciliation, `POST /api/wallet/balances`)
 * sharing the rate budget is never queued behind a whole scan range.
 *
 * A reservation in the pacer is paid by whoever acquires next: a 500-block
 * range at a 10-block provider range reserved in one piece is 100 calls ×
 * 60 CU = 6,000 CU = 30 s at 200 CU/s, and a balance request arriving behind
 * it waited all of it — past the backend's client timeout. Per call, the two
 * interleave call by call: each of the balance read's 30 calls (10 addresses ×
 * eth_getBalance + 2 eth_call; 3.6 s alone) waits at most one 60-CU scanner
 * call (0.3 s) — ≤ 12.6 s, inside the backend's 30-s chain-read budget for
 * its 10-address chunk (with the hourly reconciliation's calls too: ≤ 16.5 s).
 */

function virtualTime() {
  let now = 0;
  const timers: Array<{ at: number; resolve: () => void }> = [];
  return {
    now: () => now,
    sleep: (ms: number) => new Promise<void>((resolve) => timers.push({ at: now + ms, resolve })),
    async run(done: () => boolean): Promise<void> {
      for (let i = 0; i < 1_000_000 && !done(); i++) {
        await new Promise((r) => setImmediate(r));
        if (timers.length === 0) continue;
        timers.sort((a, b) => a.at - b.at);
        const t = timers.shift()!;
        now = Math.max(now, t.at);
        t.resolve();
      }
    },
  };
}

const ADDRESSES = 10;
const CALLS = ADDRESSES * 3;
const SOLO_MS = ((ADDRESSES * (CU_COST.eth_getBalance + 2 * CU_COST.eth_call)) / 200) * 1000;
const ONE_SCANNER_CALL_MS = (CU_COST.eth_getLogs / 200) * 1000;

describe('T158 düzeltme turu (i) — a balance request is not queued behind a scan range', () => {
  it('a 10-address balance read started with a 500-block scan range waits at most one scanner call per call, not the whole range', async () => {
    const vt = virtualTime();
    const pacer = new RatePacer({ cuPerSecond: 200, now: vt.now, sleep: vt.sleep });
    const h = monitorHarness({ providerRange: 10, pacer });
    h.chain.mineTo(1_000n);
    h.chain.finalize(1_000n);
    h.arm(deposit(80), { cadence: 'HOLDING', watchFromBlock: 0n });

    let scanEndedAt: number | null = null;
    let balancesEndedAt: number | null = null;
    void h.scanner
      .scanRange(401n, 900n, h.registry.entries(), 1_000n)
      .then(() => (scanEndedAt = vt.now()));
    void (async () => {
      for (let i = 0; i < ADDRESSES; i++) await h.reader.read(deposit(90 + i), 1_000n);
      balancesEndedAt = vt.now();
    })();
    await vt.run(() => scanEndedAt !== null && balancesEndedAt !== null);

    expect(SOLO_MS).toBe(3_600);
    expect(balancesEndedAt).not.toBeNull();
    expect(balancesEndedAt!).toBeLessThanOrEqual(SOLO_MS + CALLS * ONE_SCANNER_CALL_MS + 100);
    expect(balancesEndedAt!).toBeLessThan(30_000); // the backend's chain-read budget
    // The range itself still spends its whole budget.
    expect(scanEndedAt!).toBeGreaterThanOrEqual(29_000);
  });
});
