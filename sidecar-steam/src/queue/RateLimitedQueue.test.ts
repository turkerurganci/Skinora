import { describe, it, expect, vi } from 'vitest';

vi.mock('../logger.js', () => ({
  logger: {
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
    debug: vi.fn(),
    child: vi.fn().mockReturnThis(),
  },
}));

import { RateLimitedQueue } from './RateLimitedQueue.js';

/**
 * The queue is driven by real `Date.now()` / `setTimeout` (no clock injection),
 * so these tests use deliberately tiny windows and assert only LOWER bounds on
 * elapsed time. An upper bound would be flaky on a loaded CI runner, while a
 * lower bound still proves throttling happened.
 */
describe('RateLimitedQueue (T120 — 08 §2.6)', () => {
  it('runs a task and returns its value', async () => {
    const queue = new RateLimitedQueue(5, 50);

    await expect(queue.enqueue(async () => 'ok')).resolves.toBe('ok');
  });

  it('propagates task rejection to the caller', async () => {
    const queue = new RateLimitedQueue(5, 50);

    await expect(queue.enqueue(async () => Promise.reject(new Error('boom')))).rejects.toThrow(
      'boom',
    );
  });

  it('keeps draining after a task rejects', async () => {
    const queue = new RateLimitedQueue(5, 50);

    await expect(queue.enqueue(async () => Promise.reject(new Error('boom')))).rejects.toThrow();
    await expect(queue.enqueue(async () => 'still alive')).resolves.toBe('still alive');
  });

  it('throttles once the window budget is spent', async () => {
    // 1 request per 60ms → three tasks cannot finish in under two windows.
    const queue = new RateLimitedQueue(1, 60);
    const started = Date.now();

    await Promise.all([
      queue.enqueue(async () => 1),
      queue.enqueue(async () => 2),
      queue.enqueue(async () => 3),
    ]);

    expect(Date.now() - started).toBeGreaterThanOrEqual(120);
  });

  it('dispatches in FIFO order', async () => {
    const queue = new RateLimitedQueue(1, 20);
    const order: number[] = [];

    await Promise.all(
      [1, 2, 3, 4].map((n) =>
        queue.enqueue(async () => {
          order.push(n);
        }),
      ),
    );

    expect(order).toEqual([1, 2, 3, 4]);
  });

  it('does not throttle while the budget is unspent', async () => {
    const queue = new RateLimitedQueue(10, 60_000);
    const started = Date.now();

    await Promise.all([1, 2, 3, 4, 5].map((n) => queue.enqueue(async () => n)));

    // Well under the 60s window — a shared/undersized budget would stall here.
    expect(Date.now() - started).toBeLessThan(1_000);
  });

  it('reports pending depth while tasks wait for the window', async () => {
    const queue = new RateLimitedQueue(1, 40);
    expect(queue.pendingCount).toBe(0);

    const inFlight = [
      queue.enqueue(async () => 1),
      queue.enqueue(async () => 2),
      queue.enqueue(async () => 3),
    ];
    // The first task is dispatched synchronously on enqueue; the rest wait.
    expect(queue.pendingCount).toBeGreaterThan(0);

    await Promise.all(inFlight);
    expect(queue.pendingCount).toBe(0);
  });

  it('notifies the depth observer on enqueue and on dispatch', async () => {
    const depths: number[] = [];
    const queue = new RateLimitedQueue(1, 20, (depth) => depths.push(depth));

    await Promise.all([1, 2, 3].map((n) => queue.enqueue(async () => n)));

    // Backlog must be observable while it exists, and must return to 0.
    expect(Math.max(...depths)).toBeGreaterThan(0);
    expect(depths.at(-1)).toBe(0);
  });

  it('keeps two queues independent — one saturated queue does not stall the other', async () => {
    // 08 §2.6: the whole point of a second queue. `slow` is saturated at
    // 1 req/80ms; `fast` must not inherit that ceiling.
    const slow = new RateLimitedQueue(1, 80);
    const fast = new RateLimitedQueue(50, 60_000);

    const slowWork = Promise.all([1, 2, 3].map((n) => slow.enqueue(async () => n)));

    const started = Date.now();
    await Promise.all([1, 2, 3].map((n) => fast.enqueue(async () => n)));
    const fastElapsed = Date.now() - started;

    await slowWork;
    expect(fastElapsed).toBeLessThan(80);
  });
});
