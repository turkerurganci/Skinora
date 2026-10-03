import { afterEach, describe, it, expect, vi } from 'vitest';
import { logger } from '../logger.js';
import type { Trc20Record, TransferLogEntry } from '../tron/TronGridClient.js';
import {
  catchLogsNotReady,
  createEventIndexBook,
  createEventIndexScan,
  eventKey,
  LOG_WAIT_MS,
  LogsNotReadyError,
  markReported,
  resolveEventIndex,
  type EventIndexBook,
  type EventIndexScan,
} from './EventIndexResolver.js';

const DEPOSIT = 'TDeposit1234567890DepositAddrFakeXX';
const USDT = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';
const USDC = 'TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8';
const T = Date.parse('2026-10-03T12:00:00Z');

function record(txHash: string, value = '5000000', contract = USDT): Trc20Record {
  return {
    transaction_id: txHash,
    token_info: { address: contract, decimals: 6, symbol: contract === USDT ? 'USDT' : 'USDC' },
    block_timestamp: 1_790_000_000_000,
    from: 'TFrom111111111111111111111111111111',
    to: DEPOSIT,
    type: 'Transfer',
    value,
  };
}

/**
 * The node's answers per lookup, in order; the last one repeats. null = it
 * does not know the tx. A function answers per contract instead.
 */
function harness(
  answers:
    | Array<TransferLogEntry[] | null>
    | ((contract: string, lookup: number) => TransferLogEntry[] | null),
) {
  const seenEvents = new Set<string>();
  const book: EventIndexBook = createEventIndexBook();
  const lookups: string[] = [];
  const client = {
    // eslint-disable-next-line @typescript-eslint/require-await
    resolveTransferEventIndices: async (txHash: string, contract: string) => {
      lookups.push(`${txHash}:${contract}`);
      if (typeof answers === 'function') return answers(contract, lookups.length - 1);
      return answers[Math.min(lookups.length - 1, answers.length - 1)];
    },
  };
  /** One scan by default: a fresh per-scan memo, as each phase of a poll uses. */
  const resolve = (rec: Trc20Record, now: number, scan: EventIndexScan = createEventIndexScan()) =>
    resolveEventIndex({
      client,
      record: rec,
      depositAddress: DEPOSIT,
      scan,
      seenEvents,
      book,
      now,
    });
  /** Resolve and, unless the key is taken, report — what a monitor's handle does. */
  const report = async (rec: Trc20Record, now: number, scan?: EventIndexScan) => {
    const resolution = await resolve(rec, now, scan);
    if (seenEvents.has(eventKey(rec.transaction_id, resolution.index))) return undefined;
    markReported(seenEvents, book, rec, resolution);
    return resolution;
  };
  /** Wait out the node for a record and report the index-0 guess. */
  const guess = async (rec: Trc20Record) => {
    await expect(resolve(rec, T)).rejects.toBeInstanceOf(LogsNotReadyError);
    return report(rec, T + LOG_WAIT_MS);
  };
  return { seenEvents, book, lookups, resolve, report, guess };
}

describe('resolveEventIndex', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('the wait is two minutes', () => {
    expect(LOG_WAIT_MS).toBe(120_000);
  });

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

  it('keeps a known answer: a lagging node answering "unknown" later does not turn the reported index into a 0 guess', async () => {
    // Validation finding 1: index 2 reported, then a node behind the load
    // balancer answers {} for two minutes — the re-read went to 0 and the
    // backend recorded a second payment.
    const h = harness([[{ index: 2, value: '5000000' }], null]);
    expect(await h.report(record('tx'), T)).toEqual({ index: 2, guessed: false });

    for (const now of [T + 1, T + LOG_WAIT_MS, T + 10 * LOG_WAIT_MS]) {
      await expect(h.resolve(record('tx'), now)).resolves.toEqual({ index: 2, guessed: false });
    }
    expect([...h.seenEvents]).toEqual([eventKey('tx', 2)]);
    expect(h.lookups).toEqual([`tx:${USDT}`]);
  });

  it('a known answer whose report was lost is resolved to the same index again — not to 0', async () => {
    // The backend wrote the payment but the response was lost (504): the
    // index is not in seenEvents, and the next answer is "unknown".
    const h = harness([[{ index: 2, value: '5000000' }], null]);
    await h.resolve(record('tx'), T); // webhook failed: markReported not called

    await expect(h.resolve(record('tx'), T + LOG_WAIT_MS + 1)).resolves.toEqual({
      index: 2,
      guessed: false,
    });
  });

  it('waits per transaction and token: a known token does not restart another token’s wait', async () => {
    // Validation finding 2: with the wait keyed by tx alone, phase 1's known
    // USDT answer cleared it and phase 2's unknown USDC restarted it — the
    // two minutes never ran out.
    const h = harness((contract) => (contract === USDT ? [{ index: 1, value: '5000000' }] : null));
    await expect(h.resolve(record('tx', '7', USDC), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    await h.report(record('tx'), T + LOG_WAIT_MS / 2);

    await expect(h.resolve(record('tx', '7', USDC), T + LOG_WAIT_MS)).resolves.toEqual({
      index: 0,
      guessed: true,
    });
  });

  it('asks the node once per scan for a transaction it does not know', async () => {
    const h = harness([null]);
    const scan = createEventIndexScan();
    await expect(h.resolve(record('tx'), T, scan)).rejects.toBeInstanceOf(LogsNotReadyError);
    await expect(h.resolve(record('tx', '7'), T, scan)).rejects.toBeInstanceOf(LogsNotReadyError);

    expect(h.lookups).toHaveLength(1);
    await expect(h.resolve(record('tx'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
    expect(h.lookups).toHaveLength(2);
  });

  it('remembers a reported guess, and only a guess', async () => {
    const h = harness([[{ index: 2, value: '5000000' }]]);
    await h.report(record('tx'), T);
    expect(h.book.guesses.size).toBe(0);

    const g = harness([null]);
    await g.guess(record('tx'));
    expect(g.book.guesses.get('tx')).toEqual({ contract: USDT, value: '5000000' });
  });

  it('ties a reported guess to the real log: the re-read finds that index already reported', async () => {
    const h = harness([null, null, [{ index: 2, value: '5000000' }]]);
    expect(await h.guess(record('tx'))).toEqual({ index: 0, guessed: true });

    const reread = await h.resolve(record('tx'), T + LOG_WAIT_MS + 1);

    expect(reread).toEqual({ index: 2, guessed: false });
    expect(h.seenEvents.has(eventKey('tx', 2))).toBe(true);
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
    await h.guess(record('tx'));
    const error = vi.spyOn(logger, 'error').mockImplementation(() => undefined as never);

    // The second equal transfer of the transaction is still reported, under 3.
    const reread = await h.resolve(record('tx'), T + LOG_WAIT_MS + 1);

    expect(reread).toEqual({ index: 3, guessed: false });
    expect([...h.seenEvents]).toEqual([eventKey('tx', 0)]);
    expect(error).not.toHaveBeenCalled();
  });

  it('keeps known answers per token of a transaction', async () => {
    const h = harness((contract) =>
      contract === USDT ? [{ index: 1, value: '5000000' }] : [{ index: 3, value: '7' }],
    );
    await h.report(record('tx'), T);

    await expect(h.resolve(record('tx', '7', USDC), T)).resolves.toEqual({
      index: 3,
      guessed: false,
    });
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
    await h.guess(record('tx'));

    const other = await h.resolve(record('tx', '7'), T + LOG_WAIT_MS + 1);

    expect(other).toEqual({ index: 1, guessed: false });
    expect(h.seenEvents.has(eventKey('tx', 2))).toBe(true);
  });

  it('keeps guesses per transaction', async () => {
    const h = harness([null, null, [{ index: 2, value: '5000000' }]]);
    await h.guess(record('tx-a'));

    const b = await h.resolve(record('tx-b'), T + LOG_WAIT_MS + 1);

    expect(b).toEqual({ index: 2, guessed: false });
    expect(h.seenEvents.has(eventKey('tx-b', 2))).toBe(false);
    expect(h.book.guesses.has('tx-a')).toBe(true);
  });

  describe('transfers the index-0 guess shuts out are logged (validation finding 5)', () => {
    function errorSpy() {
      return vi.spyOn(logger, 'error').mockImplementation(() => undefined as never);
    }

    it('a re-read of the guessed transfer logs nothing', async () => {
      const h = harness([null]);
      await h.guess(record('tx'));
      const error = errorSpy();

      expect(await h.report(record('tx'), T + LOG_WAIT_MS + 1)).toBeUndefined();
      expect(await h.report(record('tx'), T + LOG_WAIT_MS + 2)).toBeUndefined();

      expect(error).not.toHaveBeenCalled();
    });

    it('another value in the same transaction is not reported, and logged once', async () => {
      const h = harness([null]);
      await h.guess(record('tx'));
      const error = errorSpy();

      expect(await h.report(record('tx', '7'), T + LOG_WAIT_MS + 1)).toBeUndefined();
      expect(await h.report(record('tx', '7'), T + LOG_WAIT_MS + 2)).toBeUndefined();

      expect(error).toHaveBeenCalledTimes(1);
      expect(error.mock.calls[0][0]).toMatchObject({
        txHash: 'tx',
        value: '7',
        guessedValue: '5000000',
      });
    });

    it('an equal second transfer of the same token, met twice in one scan, is logged', async () => {
      const h = harness([null]);
      await expect(h.resolve(record('tx'), T)).rejects.toBeInstanceOf(LogsNotReadyError);
      const error = errorSpy();
      const scan = createEventIndexScan();

      expect(await h.report(record('tx'), T + LOG_WAIT_MS, scan)).toEqual({
        index: 0,
        guessed: true,
      });
      expect(await h.report(record('tx'), T + LOG_WAIT_MS, scan)).toBeUndefined();

      expect(error).toHaveBeenCalledTimes(1);
    });

    it('another token’s transfer in the same transaction is not reported, and logged', async () => {
      const h = harness([null]);
      await h.guess(record('tx'));
      await expect(h.resolve(record('tx', '7', USDC), T)).rejects.toBeInstanceOf(LogsNotReadyError);
      const error = errorSpy();

      expect(await h.report(record('tx', '7', USDC), T + LOG_WAIT_MS)).toBeUndefined();

      expect(error).toHaveBeenCalledTimes(1);
      expect(error.mock.calls[0][0]).toMatchObject({ contract: USDC, guessedContract: USDT });
    });

    it('another token’s transfer of the guessed value is not taken for a re-read of the guess', async () => {
      const h = harness([null]);
      await h.guess(record('tx'));
      await expect(h.resolve(record('tx', '5000000', USDC), T)).rejects.toBeInstanceOf(
        LogsNotReadyError,
      );
      const error = errorSpy();

      expect(await h.report(record('tx', '5000000', USDC), T + LOG_WAIT_MS)).toBeUndefined();

      expect(error).toHaveBeenCalledTimes(1);
      expect(error.mock.calls[0][0]).toMatchObject({ contract: USDC, guessedContract: USDT });
    });

    it('a transfer whose real index is 0 is logged when its logs arrive — the guess holds that key', async () => {
      // The guessed USDT transfer really sits at 2; the USDC one at 0.
      const h = harness((contract, lookup) => {
        if (lookup < 2) return null;
        return contract === USDT ? [{ index: 2, value: '5000000' }] : [{ index: 0, value: '7' }];
      });
      await h.guess(record('tx'));
      const error = errorSpy();

      expect(await h.report(record('tx'), T + LOG_WAIT_MS + 1)).toBeUndefined();
      expect(error).not.toHaveBeenCalled();
      expect(await h.report(record('tx', '7', USDC), T + LOG_WAIT_MS + 1)).toBeUndefined();

      expect(error).toHaveBeenCalledTimes(1);
      expect(error.mock.calls[0][0]).toMatchObject({ contract: USDC, value: '7' });
    });

    it('the guessed token’s other transfer at index 0 is logged when its logs arrive', async () => {
      const h = harness([
        null,
        null,
        [
          { index: 0, value: '7' },
          { index: 2, value: '5000000' },
        ],
      ]);
      await h.guess(record('tx'));
      const error = errorSpy();

      expect(await h.report(record('tx'), T + LOG_WAIT_MS + 1)).toBeUndefined();

      expect(error).toHaveBeenCalledTimes(1);
      expect(error.mock.calls[0][0]).toMatchObject({ contract: USDT, value: '7' });
    });
  });
});

describe('catchLogsNotReady', () => {
  it('returns the wait instead of throwing it, and lets every other error through', async () => {
    const notReady = new LogsNotReadyError('tx');
    await expect(catchLogsNotReady(() => Promise.reject(notReady))).resolves.toBe(notReady);
    await expect(catchLogsNotReady(() => Promise.resolve())).resolves.toBeUndefined();
    await expect(catchLogsNotReady(() => Promise.reject(new Error('503')))).rejects.toThrow('503');
  });
});
