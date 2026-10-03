import { logger } from '../logger.js';
import type { Trc20Record, TransferLogEntry } from '../tron/TronGridClient.js';
import { pickEventIndex } from './ForwardScan.js';

/**
 * How long a monitor waits for the solidity node to know a transaction the
 * list already shows as confirmed (backlog `EventIndexFallbackDoubleReport`,
 * owner decision 2026-10-03). On Nile the node knew every probed transaction
 * 3–7 s before the confirmed list showed it, so a wait at all means a lagging
 * node; two minutes absorbs a lag without letting a node that never answers
 * stall the address for good.
 */
export const LOG_WAIT_MS = 2 * 60 * 1000;

/**
 * Thrown while the solidity node does not know a listed transaction yet. It
 * aborts the scan before the record is reported, so the cursor stays on its
 * page and the next poll reads it again.
 */
export class LogsNotReadyError extends Error {
  constructor(readonly txHash: string) {
    super(`Solidity node does not know transaction ${txHash} yet`);
    this.name = 'LogsNotReadyError';
  }
}

/** Per-address memory of the waits and guesses below. Lives as long as the monitor. */
export interface EventIndexBook {
  /** txHash → epoch ms the node was first found not knowing it. */
  waitingSince: Map<string, number>;
  /**
   * `${txHash}:${contract}` → values reported under a guessed index 0 after
   * the wait ran out. When the node later yields the logs, each guess is tied
   * to its real log so the transfer is not reported a second time.
   */
  guessedZero: Map<string, string[]>;
}

export function createEventIndexBook(): EventIndexBook {
  return { waitingSince: new Map(), guessedZero: new Map() };
}

export interface EventIndexResolution {
  index: number;
  /** True when the index is the index-0 guess made after the wait ran out. */
  guessed: boolean;
}

export interface ResolveEventIndexArgs {
  client: {
    resolveTransferEventIndices(
      txHash: string,
      contractAddress: string,
      toAddress: string,
    ): Promise<TransferLogEntry[] | null>;
  };
  record: Trc20Record;
  depositAddress: string;
  /** Per-poll cache keyed by `${txHash}:${contract}`; null = node does not know the tx. */
  logCache: Map<string, TransferLogEntry[] | null>;
  seenEvents: Set<string>;
  book: EventIndexBook;
  now: number;
}

/** Composite dedup key (08 §3.4 — WP10). */
export function eventKey(txHash: string, eventIndex: number): string {
  return `${txHash}:${eventIndex}`;
}

/**
 * Resolve the on-chain log index for a list record (08 §3.4).
 *
 * <para>
 * The solidity node answers a transaction it does not know with an empty
 * object, and a known one with its id and every log at once (Nile probe
 * 2026-10-03). Only the known answer is final, so only it may fall back to
 * index 0 when no log matches — a standard-breaking spam token, say, gets
 * the same 0 on every re-read. Until 2026-10-03 the unknown answer fell back
 * to 0 as well; the next poll found the real index (say 2), and the same
 * transfer was reported again under it — a second payment in the backend.
 * </para>
 *
 * <para>
 * Now an unknown transaction aborts the scan ({@link LogsNotReadyError})
 * for up to {@link LOG_WAIT_MS}. After that the index is guessed as 0 so a
 * node that never answers cannot stall the address; once the caller reports
 * the guess ({@link markReported}) it is remembered and tied to the real log
 * when the logs arrive.
 * </para>
 */
export async function resolveEventIndex(
  args: ResolveEventIndexArgs,
): Promise<EventIndexResolution> {
  const { client, record, depositAddress, logCache, seenEvents, book, now } = args;
  const txHash = record.transaction_id;
  const cacheKey = `${txHash}:${record.token_info.address}`;
  let entries = logCache.get(cacheKey);
  if (entries === undefined) {
    entries = await client.resolveTransferEventIndices(
      txHash,
      record.token_info.address,
      depositAddress,
    );
    logCache.set(cacheKey, entries);
  }
  const isReported = (index: number) => seenEvents.has(eventKey(txHash, index));

  if (entries === null) {
    const since = book.waitingSince.get(txHash);
    if (since === undefined) {
      book.waitingSince.set(txHash, now);
      throw new LogsNotReadyError(txHash);
    }
    if (now - since < LOG_WAIT_MS) {
      throw new LogsNotReadyError(txHash);
    }
    const index = pickEventIndex([], record.value, isReported);
    if (!isReported(index)) {
      logger.warn(
        { txHash, contract: record.token_info.address, waitedMs: now - since },
        'Solidity node still does not know the transaction — reporting under index 0',
      );
    }
    return { index, guessed: true };
  }

  book.waitingSince.delete(txHash);
  tieGuessesToLogs(book, cacheKey, txHash, entries, seenEvents);
  return { index: pickEventIndex(entries, record.value, isReported), guessed: false };
}

/**
 * Record a reported event. Called only after the webhook went out, so a guess
 * whose report failed is not remembered — it is resolved afresh next poll.
 */
export function markReported(
  seenEvents: Set<string>,
  book: EventIndexBook,
  record: Trc20Record,
  resolution: EventIndexResolution,
): void {
  seenEvents.add(eventKey(record.transaction_id, resolution.index));
  if (!resolution.guessed) return;
  const cacheKey = `${record.transaction_id}:${record.token_info.address}`;
  const values = book.guessedZero.get(cacheKey) ?? [];
  values.push(record.value);
  book.guessedZero.set(cacheKey, values);
}

/**
 * Tie each index-0 guess of a transaction to its real log once the logs are
 * known: a guess whose value has a log at index 0 was right; otherwise the
 * first unreported log with its value is marked reported, so the re-read does
 * not report the same transfer again under its real index.
 */
function tieGuessesToLogs(
  book: EventIndexBook,
  cacheKey: string,
  txHash: string,
  entries: ReadonlyArray<TransferLogEntry>,
  seenEvents: Set<string>,
): void {
  const guesses = book.guessedZero.get(cacheKey);
  if (!guesses) return;
  book.guessedZero.delete(cacheKey);
  for (const value of guesses) {
    if (entries.some((e) => e.index === 0 && e.value === value)) continue;
    const real = entries.find(
      (e) => e.value === value && !seenEvents.has(eventKey(txHash, e.index)),
    );
    if (real) {
      seenEvents.add(eventKey(txHash, real.index));
      logger.warn(
        { txHash, guessedIndex: 0, realIndex: real.index },
        'Index-0 guess tied to its real log — the transfer stays reported once, under index 0',
      );
    }
    const atZero = entries.find((e) => e.index === 0);
    if (atZero && atZero.value !== value) {
      // The guess took the key of a different transfer; the backend's
      // (TxHash, EventIndex) UNIQUE would refuse that one too.
      logger.error(
        { txHash, guessedValue: value, realValueAtZero: atZero.value },
        'Index-0 guess collides with another transfer at index 0 — that transfer is not reported',
      );
    }
  }
}
