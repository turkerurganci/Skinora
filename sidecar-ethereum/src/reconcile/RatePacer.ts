/**
 * Spreads a burst of RPC reads over time so it stays under the provider's
 * throughput limit (T151 — 08 §3.7.4 "Bakiye mutabakatı": Alchemy Free allows
 * 300 compute units per second; a reconciliation of ~300 deposits does not
 * fit in one second and must be spread — at least ~46 s at the full 300 CU/s).
 *
 * <para>
 * A leaky bucket in compute units: every {@link acquire} reserves its cost on
 * a single timeline and waits until its slot. The default budget is below the
 * provider's limit (200 of 300 CU/s) so the payment scanner's own calls keep
 * room while a reconciliation runs. Costs are Alchemy's published CU table as
 * T142 recorded it; a provider that bills per request (Chainstack) is slower
 * still, never faster.
 * </para>
 *
 * <para>
 * <b>One call, one reservation</b> (T158 düzeltme turu). Callers acquire per
 * RPC call, never a whole batch at once: a reservation is paid later by
 * whoever acquires next, so a batch reserved in one piece (a 500-block scan
 * range ≈ 6,000 CU ≈ 30 s) would make a concurrent balance request wait all of
 * it. Per call, concurrent callers interleave and each gets a fair share.
 * </para>
 */

/** Alchemy compute units per method (T142_REPORT §1.4). */
export const CU_COST = {
  eth_getLogs: 60,
  eth_call: 26,
  eth_getBalance: 20,
  eth_getBlockByNumber: 20,
} as const;

export interface RatePacerDeps {
  /** Compute units per second this pacer may spend. */
  cuPerSecond: number;
  now?: () => number;
  sleep?: (ms: number) => Promise<void>;
}

export class RatePacer {
  private nextFreeAt = 0;
  private readonly now: () => number;
  private readonly sleep: (ms: number) => Promise<void>;

  constructor(private readonly deps: RatePacerDeps) {
    if (!Number.isFinite(deps.cuPerSecond) || deps.cuPerSecond <= 0) {
      throw new Error(`RatePacer needs a positive budget, got ${deps.cuPerSecond}`);
    }
    this.now = deps.now ?? (() => Date.now());
    this.sleep =
      deps.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms).unref?.()));
  }

  get cuPerSecond(): number {
    return this.deps.cuPerSecond;
  }

  /** Waits until <c>cu</c> compute units may be spent, then returns. */
  async acquire(cu: number): Promise<void> {
    const now = this.now();
    const start = Math.max(now, this.nextFreeAt);
    this.nextFreeAt = start + (cu / this.deps.cuPerSecond) * 1000;
    const wait = start - now;
    if (wait > 0) await this.sleep(wait);
  }

  /** Lower bound of the time <c>cu</c> compute units take at this budget, in seconds. */
  static minimumSeconds(cu: number, cuPerSecond: number): number {
    return cu / cuPerSecond;
  }
}
