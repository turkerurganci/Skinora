import { logger } from '../logger.js';

interface QueuedTask<T> {
  fn: () => Promise<T>;
  resolve: (value: T) => void;
  reject: (reason: unknown) => void;
}

/**
 * Minimal queue surface consumers depend on. Declared separately from the
 * class so services can accept a pass-through double in tests without
 * constructing a real timer-driven queue (T120).
 */
export interface TaskQueue {
  enqueue<T>(fn: () => Promise<T>): Promise<T>;
}

/**
 * Simple rate-limited request queue.
 * Ensures at most `maxRequests` are dispatched per `windowMs`.
 *
 * One instance per upstream (T120 — 08 §2.6): Steam's Web API and Community
 * endpoints have different, independently enforced limits, so they must not
 * share a queue. Sharing one would park delivery-verification inventory reads
 * behind trade-hold calls that are governed by an unrelated budget.
 *
 * Scope note: the queue is per-process and in-memory (T67 K7 — single replica
 * assumption). Horizontal scaling would need a shared limiter.
 */
export class RateLimitedQueue implements TaskQueue {
  private queue: QueuedTask<unknown>[] = [];
  private timestamps: number[] = [];
  private processing = false;

  /**
   * @param onDepthChange Optional pending-depth observer, called on every
   *   enqueue and dispatch. `index.ts` wires it to the
   *   `skinora_steam_queue_depth` gauge; the queue itself stays free of the
   *   Prometheus import so it carries no module-load side effects.
   */
  constructor(
    private readonly maxRequests: number,
    private readonly windowMs: number,
    private readonly onDepthChange?: (depth: number) => void,
  ) {}

  async enqueue<T>(fn: () => Promise<T>): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      this.queue.push({ fn, resolve, reject } as QueuedTask<unknown>);
      this.onDepthChange?.(this.queue.length);
      this.process();
    });
  }

  private async process(): Promise<void> {
    if (this.processing) return;
    this.processing = true;

    while (this.queue.length > 0) {
      const now = Date.now();
      // Remove timestamps outside the current window
      this.timestamps = this.timestamps.filter((t) => now - t < this.windowMs);

      if (this.timestamps.length >= this.maxRequests) {
        const oldestInWindow = this.timestamps[0];
        const waitMs = this.windowMs - (now - oldestInWindow) + 10;
        logger.debug({ waitMs, queueLength: this.queue.length }, 'Rate limit reached, waiting');
        await this.sleep(waitMs);
        continue;
      }

      const task = this.queue.shift()!;
      this.onDepthChange?.(this.queue.length);
      this.timestamps.push(Date.now());

      try {
        const result = await task.fn();
        task.resolve(result);
      } catch (err) {
        task.reject(err);
      }
    }

    this.processing = false;
  }

  private sleep(ms: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  get pendingCount(): number {
    return this.queue.length;
  }
}
