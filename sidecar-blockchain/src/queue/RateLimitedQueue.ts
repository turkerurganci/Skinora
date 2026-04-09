import { logger } from '../logger.js';

interface QueuedTask<T> {
  fn: () => Promise<T>;
  resolve: (value: T) => void;
  reject: (reason: unknown) => void;
}

/**
 * Simple rate-limited request queue.
 * Ensures at most `maxRequests` are dispatched per `windowMs`.
 */
export class RateLimitedQueue {
  private queue: QueuedTask<unknown>[] = [];
  private timestamps: number[] = [];
  private processing = false;

  constructor(
    private readonly maxRequests: number,
    private readonly windowMs: number,
  ) {}

  async enqueue<T>(fn: () => Promise<T>): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      this.queue.push({ fn, resolve, reject } as QueuedTask<unknown>);
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
