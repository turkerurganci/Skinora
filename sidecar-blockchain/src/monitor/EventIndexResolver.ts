import { logger } from '../logger.js';
import type { Trc20Record, TransferLogEntry } from '../tron/TronGridClient.js';
import { pickEventIndex, RecordNotReadyError } from './ForwardScan.js';

/**
 * How long a monitor waits for the solidity node to know a transaction the
 * list already shows as confirmed (backlog `EventIndexFallbackDoubleReport`,
 * owner decision 2026-10-03). On Nile the node knew every probed transaction
 * 3–7 s before the confirmed list showed it, so a wait at all means a lagging
 * node; two minutes absorbs a lag without letting a node that never answers
 * stall the record for good.
 */
export const LOG_WAIT_MS = 2 * 60 * 1000;

/**
 * Thrown while the solidity node does not know a listed transaction yet. The
 * scan finishes the rest of its page, then leaves the cursor before that page
 * ({@link RecordNotReadyError}), so the next poll reads it again; the other
 * records of the page are reported meanwhile.
 */
export class LogsNotReadyError extends RecordNotReadyError {
  constructor(readonly txHash: string) {
    super(`Solidity node does not know transaction ${txHash} yet`);
    this.name = 'LogsNotReadyError';
  }
}

/** Per-address memory of the answers, waits and guesses below. Lives as long as the monitor. */
export interface EventIndexBook {
  /**
   * `${txHash}:${contract}` → the transfer logs of a transaction the node
   * knew. That answer is final (the solidity node only serves solidified
   * blocks), so it is kept and never asked for again: a lagging node behind a
   * load balancer answering the next re-read with "unknown" cannot turn a
   * transfer reported under its real index into an index-0 guess.
   */
  knownLogs: Map<string, TransferLogEntry[]>;
  /** `${txHash}:${contract}` → epoch ms the node was first found not knowing it. */
  waitingSince: Map<string, number>;
  /**
   * txHash → the index-0 guess reported for it after a wait ran out. At most
   * one per transaction: once `${txHash}:0` is reported, every later guess in
   * that transaction finds the key taken. Kept for the monitor's lifetime —
   * when the node learns the transaction, each token's logs are checked
   * against it.
   */
  guesses: Map<string, { contract: string; value: string }>;
  /** Transfers already logged as not reported, so a re-read does not log them again. */
  droppedLogged: Set<string>;
}

export function createEventIndexBook(): EventIndexBook {
  return {
    knownLogs: new Map(),
    waitingSince: new Map(),
    guesses: new Map(),
    droppedLogged: new Set(),
  };
}

/** Memory of one scan (one phase of one poll). */
export interface EventIndexScan {
  /** `${txHash}:${contract}` the node did not know during this scan — not asked again within it. */
  unknown: Set<string>;
  /**
   * `${txHash}:${contract}:${value}` → list records met on the guess path in
   * this scan. A re-read meets the guessed transfer once per scan; a second
   * meeting is another transfer of the same token and value.
   */
  guessReads: Map<string, number>;
}

export function createEventIndexScan(): EventIndexScan {
  return { unknown: new Set(), guessReads: new Map() };
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
  scan: EventIndexScan;
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
 * the same 0 on every re-read. It is kept ({@link EventIndexBook.knownLogs}):
 * a later "unknown" from a lagging node must not undo it.
 * </para>
 *
 * <para>
 * An unknown transaction is waited for, up to {@link LOG_WAIT_MS} per
 * transaction and token ({@link LogsNotReadyError}). After that the index is
 * guessed as 0 so a node that never answers cannot stall the record; once
 * the caller reports the guess ({@link markReported}) it is remembered and
 * tied to the real log when the logs arrive. Every other transfer of that
 * transaction met while the node still does not know it finds index 0
 * taken: it is not reported then, and an error is logged — it is reported
 * under its real index if the node answers while its block is still re-read.
 * </para>
 */
export async function resolveEventIndex(
  args: ResolveEventIndexArgs,
): Promise<EventIndexResolution> {
  const { client, record, depositAddress, scan, seenEvents, book, now } = args;
  const txHash = record.transaction_id;
  const contract = record.token_info.address;
  const key = `${txHash}:${contract}`;
  const isReported = (index: number) => seenEvents.has(eventKey(txHash, index));

  let entries = book.knownLogs.get(key);
  if (entries === undefined && !scan.unknown.has(key)) {
    const answer = await client.resolveTransferEventIndices(txHash, contract, depositAddress);
    if (answer === null) {
      scan.unknown.add(key);
    } else {
      entries = answer;
      book.knownLogs.set(key, answer);
      book.waitingSince.delete(key);
      checkGuessAgainstLogs(book, txHash, contract, answer, seenEvents);
    }
  }
  if (entries !== undefined) {
    return { index: pickEventIndex(entries, record.value, isReported), guessed: false };
  }

  const since = book.waitingSince.get(key);
  if (since === undefined) {
    book.waitingSince.set(key, now);
    throw new LogsNotReadyError(txHash);
  }
  if (now - since < LOG_WAIT_MS) {
    throw new LogsNotReadyError(txHash);
  }

  const readKey = `${key}:${record.value}`;
  const reads = (scan.guessReads.get(readKey) ?? 0) + 1;
  scan.guessReads.set(readKey, reads);
  if (!isReported(0)) {
    logger.warn(
      { txHash, contract, waitedMs: now - since },
      'Solidity node still does not know the transaction — reporting under index 0',
    );
    return { index: 0, guessed: true };
  }
  const guess = book.guesses.get(txHash);
  const isTheGuess = reads === 1 && guess?.contract === contract && guess.value === record.value;
  if (!isTheGuess) {
    logDropped(
      book,
      `${readKey}#${reads}`,
      {
        txHash,
        contract,
        value: record.value,
        guessedContract: guess?.contract,
        guessedValue: guess?.value,
      },
      'Transfer not reported — the solidity node still does not know its transaction and index 0 is taken; ' +
        'it is reported under its real index if the node answers while its block is still re-read',
    );
  }
  return { index: 0, guessed: true };
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
  book.guesses.set(record.transaction_id, {
    contract: record.token_info.address,
    value: record.value,
  });
}

/**
 * Run one scan of a poll; a {@link LogsNotReadyError} it ends with is
 * returned instead of thrown, so the poll goes on to its next phase and to
 * its finality checks.
 */
export async function catchLogsNotReady(
  scan: () => Promise<void>,
): Promise<LogsNotReadyError | undefined> {
  try {
    await scan();
    return undefined;
  } catch (err) {
    if (err instanceof LogsNotReadyError) return err;
    throw err;
  }
}

/**
 * Check a transaction's index-0 guess against one token's logs, the first time
 * the node knows them. For the guessed token: a guess whose value has a log
 * at index 0 was right; otherwise the first log with its value is marked
 * reported, so the re-read does not report the same transfer again under its
 * real index. For any token: a transfer whose real index is 0 while the
 * guess took that key is not reported — the backend's (TxHash, EventIndex)
 * UNIQUE would refuse it too — and is logged.
 */
function checkGuessAgainstLogs(
  book: EventIndexBook,
  txHash: string,
  contract: string,
  entries: ReadonlyArray<TransferLogEntry>,
  seenEvents: Set<string>,
): void {
  const guess = book.guesses.get(txHash);
  if (!guess) return;
  const atZero = entries.find((e) => e.index === 0);
  if (guess.contract === contract) {
    if (atZero?.value === guess.value) return;
    const real = entries.find((e) => e.value === guess.value);
    if (real) {
      seenEvents.add(eventKey(txHash, real.index));
      logger.warn(
        { txHash, guessedIndex: 0, realIndex: real.index },
        'Index-0 guess tied to its real log — the transfer stays reported once, under index 0',
      );
    }
  }
  if (atZero) {
    logDropped(
      book,
      `${txHash}:${contract}:${atZero.value}#atZero`,
      {
        txHash,
        contract,
        value: atZero.value,
        guessedContract: guess.contract,
        guessedValue: guess.value,
      },
      'Transfer not reported — its real index is 0, which the index-0 guess of another transfer in its transaction holds',
    );
  }
}

function logDropped(
  book: EventIndexBook,
  logKey: string,
  details: {
    txHash: string;
    contract: string;
    value: string;
    guessedContract?: string;
    guessedValue?: string;
  },
  message: string,
): void {
  if (book.droppedLogged.has(logKey)) return;
  book.droppedLogged.add(logKey);
  logger.error(details, message);
}
