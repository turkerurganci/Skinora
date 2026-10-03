import { describe, it, expect } from 'vitest';
import type { Trc20Record, TransferLogEntry } from '../tron/TronGridClient.js';
import {
  createEventIndexBook,
  eventKey,
  LOG_WAIT_MS,
  LogsNotReadyError,
  markReported,
  resolveEventIndex,
  type EventIndexBook,
} from './EventIndexResolver.js';

const DEPOSIT = 'TDeposit1234567890DepositAddrFakeXX';
const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const T = Date.parse('2026-10-03T12:00:00Z');

function record(txHash: string, value = '5000000'): Trc20Record {
  return {
    transaction_id: txHash,
    token_info: { address: USDT, decimals: 6, symbol: 'USDT' },
    block_timestamp: 1_790_000_000_000,
    from: 'TFrom111111111111111111111111111111',
    to: DEPOSIT,
    type: 'Transfer',
    value,
  };
}

/** The node's answers per lookup, in order; the last one repeats. null = it does not know the tx. */
function harness(answers: Array<TransferLogEntry[] | null>) {
  const seenEvents = new Set<string>();
  const book: EventIndexBook = createEventIndexBook();
  let lookups = 0;
  const client = {
    // eslint-disable-next-line @typescript-eslint/require-await
    resolveTransferEventIndices: async () => {
      const answer = answers[Math.min(lookups, answers.length - 1)];
      lookups += 1;
      return answer;
    },
  };
  /** One poll: a fresh per-poll cache, as the monitors use. */
  const resolve = (rec: Trc20Record, now: number) =>
    resolveEventIndex({
      client,
      record: rec,
      depositAddress: DEPOSIT,
      logCache: new Map(),
      seenEvents,
      book,
      now,
    });
  return { seenEvents, book, resolve };
}

describe('resolveEventIndex', () => {
  it('waits while the node does not know the transaction, for up to LOG_WAIT_MS', async () => {
    const h = harness([null]);
    await expect(h.resolve(record('tx'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    await expect(h.resolve(record('tx'), T + LOG_WAIT_MS - 1)).rejects.toBeInstanceOf(
      LogsNotReadyError,
    );
    await expect(h.resolve(record('tx'), T + LOG_WAIT_MS)).resolves.toEqual({
      index: 0,
      guessed: true,
    });
  });

  it('measures the wait from the first "unknown" answer, not from the latest lookup', async () => {
    const h = harness([null]);
    await expect(h.resolve(record('tx'), T + 10 * LOG_WAIT_MS)).rejects.toBeInstanceOf(
      LogsNotReadyError,
    );
    await expect(h.resolve(record('tx'), T + 11 * LOG_WAIT_MS - 1)).rejects.toBeInstanceOf(
      LogsNotReadyError,
    );
  });

  it('a known transaction with no matching log falls back to 0 at once — its answer is final', async () => {
    const h = harness([[]]);
    await expect(h.resolve(record('tx'), T)).resolves.toEqual({ index: 0, guessed: false });
  });

  it('a known transaction clears the wait — a later unknown answer starts a fresh one', async () => {
    const h = harness([null, [{ index: 2, value: '5000000' }], null]);
    await expect(h.resolve(record('tx'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    await expect(h.resolve(record('tx'), T + 1)).resolves.toEqual({ index: 2, guessed: false });
    expect(h.book.waitingSince.size).toBe(0);
    await expect(h.resolve(record('tx'), T + LOG_WAIT_MS)).rejects.toBeInstanceOf(
      LogsNotReadyError,
    );
  });

  it('ties a reported guess to the real log: the re-read finds that index already reported', async () => {
    const h = harness([null, null, [{ index: 2, value: '5000000' }]]);
    await expect(h.resolve(record('tx'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    const guess = await h.resolve(record('tx'), T + LOG_WAIT_MS);
    markReported(h.seenEvents, h.book, record('tx'), guess);

    const reread = await h.resolve(record('tx'), T + LOG_WAIT_MS + 1);

    expect(reread).toEqual({ index: 2, guessed: false });
    expect(h.seenEvents.has(eventKey('tx', 2))).toBe(true);
    expect(h.book.guessedZero.size).toBe(0);
  });

  it('a guess whose report failed is not remembered — the real index is reported when it arrives', async () => {
    const h = harness([null, null, [{ index: 2, value: '5000000' }]]);
    await expect(h.resolve(record('tx'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    await h.resolve(record('tx'), T + LOG_WAIT_MS); // webhook failed: markReported not called

    const reread = await h.resolve(record('tx'), T + LOG_WAIT_MS + 1);

    expect(reread).toEqual({ index: 2, guessed: false });
    expect(h.seenEvents.has(eventKey('tx', 2))).toBe(false);
  });

  it('a guess that was right (the log is at index 0) marks nothing else', async () => {
    const h = harness([
      null,
      null,
      [
        { index: 0, value: '5000000' },
        { index: 3, value: '5000000' },
      ],
    ]);
    await expect(h.resolve(record('tx'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    const guess = await h.resolve(record('tx'), T + LOG_WAIT_MS);
    markReported(h.seenEvents, h.book, record('tx'), guess);

    // The second equal transfer of the transaction is still reported, under 3.
    const reread = await h.resolve(record('tx'), T + LOG_WAIT_MS + 1);

    expect(reread).toEqual({ index: 3, guessed: false });
    expect([...h.seenEvents]).toEqual([eventKey('tx', 0)]);
  });

  it('ties a guess only to a log of its own value', async () => {
    const h = harness([
      null,
      null,
      [
        { index: 1, value: '7' },
        { index: 2, value: '5000000' },
      ],
    ]);
    await expect(h.resolve(record('tx'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    const guess = await h.resolve(record('tx'), T + LOG_WAIT_MS);
    markReported(h.seenEvents, h.book, record('tx'), guess);

    const other = await h.resolve(record('tx', '7'), T + LOG_WAIT_MS + 1);

    expect(other).toEqual({ index: 1, guessed: false });
    expect(h.seenEvents.has(eventKey('tx', 2))).toBe(true);
  });

  it('keeps guesses per transaction', async () => {
    const h = harness([null, null, [{ index: 2, value: '5000000' }]]);
    await expect(h.resolve(record('tx-a'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    const guess = await h.resolve(record('tx-a'), T + LOG_WAIT_MS);
    markReported(h.seenEvents, h.book, record('tx-a'), guess);

    const b = await h.resolve(record('tx-b'), T + LOG_WAIT_MS + 1);

    expect(b).toEqual({ index: 2, guessed: false });
    expect(h.seenEvents.has(eventKey('tx-b', 2))).toBe(false);
    expect(h.book.guessedZero.has(`tx-a:${USDT}`)).toBe(true);
  });
});
