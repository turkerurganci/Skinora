import { logger } from '../logger.js';
import {
  setSolidBlockAgeSource,
  solidBlockNumber,
  solidBlockReadFailuresTotal,
  solidBlockStallThresholdSeconds,
} from '../metrics.js';

/**
 * T170 — is the TRON chain progressing, as this service sees it?
 *
 * <para>
 * Reads the current solid (irreversible) block on a fixed cadence
 * (`TRON_SOLID_BLOCK_POLL_INTERVAL_MS`, 60 s) and publishes its number and,
 * at scrape time, its age from the block's own timestamp — the TRON
 * counterpart of the Ethereum service's finality tracker (T150). Payments are
 * confirmed against the solid head (20 confirmations, 08 §3.4), so a head
 * that stops advancing means no TRON payment is confirmed; a TronGrid that
 * stops answering looks the same here (the age keeps growing from the last
 * block read). Before T170 the only TRON signals were HTTP error counts and
 * the backend's /health probe, which reached the admin panel only.
 * </para>
 *
 * <para>
 * Until the first successful read the age is counted from the tracker's
 * start, so a service that never reaches the chain still crosses the
 * threshold instead of publishing nothing (an absent series reads as a quiet
 * 0 in the rule — the dead-watchman failure). Cost: one TronGrid request per
 * interval — 1,440 a day at 60 s, ~1.4 % of the 100,000 free-plan budget the
 * `tron-quota-projection` rule watches (the call is counted there).
 * </para>
 */

export interface SolidBlockHeaderReader {
  getNowSolidBlockHeader(): Promise<{ number: number; timestampMs: number | null }>;
}

export interface SolidBlockSnapshot {
  readonly number: number;
  /** Block production time, epoch ms (the read time when the node gave none). */
  readonly timestampMs: number;
  /** When the service read it, epoch ms. */
  readonly readAt: number;
}

export interface SolidBlockTrackerDeps {
  client: SolidBlockHeaderReader;
  pollIntervalMs: number;
  stallThresholdSeconds: number;
  now?: () => number;
}

export class SolidBlockTracker {
  private snapshotValue: SolidBlockSnapshot | null = null;
  private timer?: NodeJS.Timeout;
  private polling = false;
  private readonly now: () => number;
  private readonly startedAt: number;

  constructor(private readonly deps: SolidBlockTrackerDeps) {
    this.now = deps.now ?? (() => Date.now());
    this.startedAt = this.now();
    solidBlockStallThresholdSeconds.set(deps.stallThresholdSeconds);
    setSolidBlockAgeSource(() => this.ageSeconds());
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

  /** One read of the solid head. Never throws — a failure keeps the last block. */
  async pollOnce(): Promise<void> {
    if (this.polling) return;
    this.polling = true;
    try {
      const header = await this.deps.client.getNowSolidBlockHeader();
      const readAt = this.now();
      const previous = this.snapshotValue;
      // A block number that goes backwards (load-balanced nodes a block apart)
      // does not replace a newer one.
      if (!previous || header.number >= previous.number) {
        this.snapshotValue = {
          number: header.number,
          timestampMs: header.timestampMs ?? readAt,
          readAt,
        };
        solidBlockNumber.set(header.number);
      }
    } catch (err) {
      solidBlockReadFailuresTotal.inc();
      logger.warn({ reason: (err as Error).message }, 'TRON solid block could not be read');
    } finally {
      this.polling = false;
    }
  }

  snapshot(): SolidBlockSnapshot | null {
    return this.snapshotValue;
  }

  /** Seconds since the last solid block was produced; since start-up while none was read. */
  ageSeconds(): number {
    const reference = this.snapshotValue?.timestampMs ?? this.startedAt;
    return Math.max(0, Math.floor((this.now() - reference) / 1000));
  }
}
