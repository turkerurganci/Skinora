import { logger } from '../logger.js';
import {
  finalizedBlockNumber,
  finalizedReadFailuresTotal,
  finalizedStallThresholdSeconds,
  setFinalizedAgeSource,
} from '../metrics.js';
import type { BlockHeader, EthereumRpcClient } from '../rpc/RpcClient.js';

/**
 * Reads the `finalized` head on a fixed cadence (08 §3.7.4 "Yoklama": every
 * 30 s) and keeps the last answer — the one place the service learns how far
 * Ethereum has finalized (K5). /health and the finality-lag metric read it
 * instead of probing the chain themselves, so neither costs RPC quota; the
 * payment monitor (T151) will read the same snapshot.
 *
 * <para>
 * <b>States</b> (what /health reports, 08 §3.7.4 "Dondurma", §3.7.8):
 * <list type="bullet">
 *   <item><c>starting</c> — no read has finished yet.</item>
 *   <item><c>unreadable</c> — no provider has answered for three poll
 *     intervals (both providers down, or the only one).</item>
 *   <item><c>stalled</c> — the last finalized block is older than the stall
 *     threshold (normal 12.8–18.8 min, threshold 30 min — T142).</item>
 *   <item><c>backup</c> — finality is fresh but the last read came from a
 *     provider other than the primary (the primary is failing).</item>
 *   <item><c>ok</c>.</item>
 * </list>
 * </para>
 */

export type FinalityState = 'starting' | 'unreadable' | 'stalled' | 'backup' | 'ok';

export interface FinalitySnapshot {
  readonly number: bigint;
  /** Block timestamp, unix seconds. */
  readonly timestamp: number;
  /** When the service read it, epoch ms. */
  readonly readAt: number;
  readonly provider: string;
}

export interface FinalityStatus {
  readonly state: FinalityState;
  readonly snapshot: FinalitySnapshot | null;
  readonly ageSeconds: number | null;
  readonly lastError: string | null;
}

export interface FinalityTrackerDeps {
  rpc: Pick<EthereumRpcClient, 'getBlockHeader'>;
  primaryProvider: string;
  pollIntervalMs: number;
  stallThresholdSeconds: number;
  now?: () => number;
}

export class FinalityTracker {
  private snapshotValue: FinalitySnapshot | null = null;
  private lastErrorValue: string | null = null;
  private attempted = false;
  private timer?: NodeJS.Timeout;
  private polling = false;
  private readonly now: () => number;

  constructor(private readonly deps: FinalityTrackerDeps) {
    this.now = deps.now ?? (() => Date.now());
    finalizedStallThresholdSeconds.set(deps.stallThresholdSeconds);
    // The age gauge is computed at scrape time from the block's own timestamp:
    // a service that can no longer read the head shows a growing age, not the
    // last value it managed to write.
    setFinalizedAgeSource(() => this.ageSeconds());
  }

  start(): void {
    void this.pollOnce();
    this.timer = setInterval(() => void this.pollOnce(), this.deps.pollIntervalMs);
    this.timer.unref();
  }

  stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = undefined;
  }

  /** One read of the finalized head. Never throws — a failure is state. */
  async pollOnce(): Promise<void> {
    if (this.polling) return;
    this.polling = true;
    try {
      const head: BlockHeader = await this.deps.rpc.getBlockHeader('finalized');
      const previous = this.snapshotValue;
      this.snapshotValue = {
        number: head.number,
        timestamp: head.timestamp,
        readAt: this.now(),
        provider: head.provider,
      };
      this.lastErrorValue = null;
      finalizedBlockNumber.set(Number(head.number));
      if (!previous || previous.number !== head.number) {
        logger.debug(
          { finalized: String(head.number), provider: head.provider },
          'Finalized head advanced',
        );
      }
    } catch (err) {
      this.lastErrorValue = (err as Error).message;
      finalizedReadFailuresTotal.inc();
      logger.warn({ reason: this.lastErrorValue }, 'Finalized head could not be read');
    } finally {
      this.attempted = true;
      this.polling = false;
    }
  }

  ageSeconds(): number | null {
    if (!this.snapshotValue) return null;
    return Math.max(0, Math.floor(this.now() / 1000) - this.snapshotValue.timestamp);
  }

  status(): FinalityStatus {
    const snapshot = this.snapshotValue;
    const ageSeconds = this.ageSeconds();
    let state: FinalityState;
    if (!this.attempted) {
      state = 'starting';
    } else if (!snapshot || this.now() - snapshot.readAt > 3 * this.deps.pollIntervalMs) {
      state = 'unreadable';
    } else if (ageSeconds !== null && ageSeconds > this.deps.stallThresholdSeconds) {
      state = 'stalled';
    } else if (snapshot.provider !== this.deps.primaryProvider) {
      state = 'backup';
    } else {
      state = 'ok';
    }
    return { state, snapshot, ageSeconds, lastError: this.lastErrorValue };
  }
}
