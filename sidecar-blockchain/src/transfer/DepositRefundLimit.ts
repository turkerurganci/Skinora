import { randomUUID } from 'node:crypto';
import {
  closeSync,
  existsSync,
  fsyncSync,
  mkdirSync,
  openSync,
  readFileSync,
  renameSync,
  rmSync,
  writeSync,
} from 'node:fs';
import { join } from 'node:path';
import { TransferDeferredError } from '../errors/SidecarError.js';
import { logger } from '../logger.js';
import {
  depositRefundDailyLimitUsdt,
  depositRefundLimitRefusalsTotal,
  depositRefundWindowUsdt,
} from '../metrics.js';
import type { DepositRefundPermitStore } from './DepositRefundPermits.js';

/**
 * T172b — the signer's own 24-hour cap on refunds that leave a DEPOSIT
 * address (proje sahibi kararı 2026-10-23; the backend's twin is T169a's
 * <c>blockchain.tron.deposit_refund_daily_limit_usdt</c>).
 *
 * <para>
 * <b>Why the signer.</b> The hot wallet's outflow is capped here already
 * (<c>MAX_DAILY_OUTFLOW_USDT</c>), but a refund leaves the deposit it was paid
 * into, so before T172b only the backend limited how much refunds could send
 * in a day — and a compromised or broken backend is exactly what the signer
 * must still be able to refuse (05 §3.3 "Transfer limitleri").
 * </para>
 *
 * <para>
 * <b>What counts — decided by the signer's own knowledge.</b> On TRON the only
 * transfers the service signs with a deposit key are the sweep (destination
 * pinned to its own hot wallet, so the money stays on the platform) and the
 * refund family (<c>POST /api/transfer/refund</c>, destination chosen by the
 * caller). Every refund is therefore a deposit refund: the key is derived from
 * the HD seed at the requested index and checked against the requested
 * address. No request field (refund type, source) decides whether the cap
 * applies. Amounts are token units; USDT and USDC count 1:1 (6 decimals each).
 * </para>
 *
 * <para>
 * <b>Window and storage.</b> Rolling 24 hours (as the hot wallet cap — no
 * midnight where twice the cap could go out). The chain cannot answer "what
 * left all deposits today" without scanning every deposit, so the total lives
 * in a ledger file on the service's own Docker volume
 * (<c>DEPOSIT_REFUND_LEDGER_DIR</c>), written temp → fsync → rename BEFORE the
 * broadcast: a restart cannot reset it, a crash leaves the old or the new file.
 * </para>
 *
 * <para>
 * <b>Concurrency.</b> {@link DepositRefundLimit.reserve} reads the total,
 * compares and writes the entry synchronously — Node runs it to the end
 * without yielding — so two requests in flight can never both read the same
 * total. An entry is removed only when the transfer was certainly not
 * broadcast (the build failed, or the code never reached the broadcast);
 * every other failure keeps it counted (fail closed).
 * </para>
 *
 * <para>
 * <b>Fail closed, and wait.</b> Over the cap, an unset or malformed cap, or an
 * unreadable ledger: nothing is signed, no TRX is spent, and the refusal is a
 * {@link TransferDeferredError} — the backend keeps the row PENDING without
 * spending a retry (never FAILED, never re-opened under a new id), the alarm
 * <c>tron-deposit-refund-limit-refused</c> fires, and the refund goes out
 * when the window has room again or an operator raises the cap.
 * </para>
 */

/** Rolling window of the cap. */
export const DEPOSIT_REFUND_WINDOW_MS = 24 * 60 * 60 * 1000;

const LEDGER_FILE = 'deposit-refunds.json';

export type DepositRefundRefusal =
  | 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED'
  | 'DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED'
  | 'DEPOSIT_REFUND_LEDGER_UNAVAILABLE';

export interface DepositRefundEntry {
  /** Unique per signing attempt — a retried id is a new attempt and counts again. */
  readonly key: string;
  readonly blockchainTransactionId: string;
  /** Raw token units (6 decimals), decimal string. */
  readonly units: string;
  /** When the attempt was counted (ms since epoch). */
  readonly at: number;
  /** Set once the broadcast returned a hash; null while in flight or when unknown. */
  readonly txHash: string | null;
  /** The operator permit that let this refund past the cap (T172b, sahip kararı); absent/null otherwise. */
  readonly permitId?: string | null;
}

/** Writes a file so that a crash leaves either the previous or the new content. */
function writeFileDurably(dir: string, name: string, content: string): void {
  const target = join(dir, name);
  const temp = join(dir, `.${name}.${process.pid}.tmp`);
  const fd = openSync(temp, 'w');
  try {
    writeSync(fd, content);
    fsyncSync(fd);
  } finally {
    closeSync(fd);
  }
  renameSync(temp, target);
  try {
    // Persist the rename itself (POSIX); Windows cannot open a directory.
    const dirFd = openSync(dir, 'r');
    try {
      fsyncSync(dirFd);
    } finally {
      closeSync(dirFd);
    }
  } catch {
    // Best effort.
  }
}

function isEntry(value: unknown): value is DepositRefundEntry {
  if (!value || typeof value !== 'object') return false;
  const e = value as Record<string, unknown>;
  return (
    typeof e.key === 'string' &&
    typeof e.blockchainTransactionId === 'string' &&
    typeof e.units === 'string' &&
    /^\d+$/.test(e.units) &&
    typeof e.at === 'number' &&
    Number.isFinite(e.at) &&
    (e.txHash === null || typeof e.txHash === 'string') &&
    (e.permitId === undefined || e.permitId === null || typeof e.permitId === 'string')
  );
}

/**
 * The durable list of counted deposit refunds. Every change is written to
 * disk first and only then believed in memory.
 */
export class DepositRefundLedger {
  private entries: DepositRefundEntry[] = [];

  /** Throws when the directory cannot be written or the file cannot be read. */
  constructor(readonly dir: string) {
    mkdirSync(dir, { recursive: true });
    // Prove the directory is writable now, not at the first refund.
    writeFileDurably(dir, '.write-probe', String(Date.now()));
    rmSync(join(dir, '.write-probe'), { force: true });
    const path = join(dir, LEDGER_FILE);
    if (!existsSync(path)) return;
    const parsed: unknown = JSON.parse(readFileSync(path, 'utf8'));
    if (!Array.isArray(parsed) || !parsed.every(isEntry)) {
      throw new Error(`${LEDGER_FILE} is not a list of deposit refund entries`);
    }
    this.entries = parsed;
  }

  list(): readonly DepositRefundEntry[] {
    return this.entries;
  }

  /** Durable first, memory second. */
  replace(next: DepositRefundEntry[]): void {
    writeFileDurably(this.dir, LEDGER_FILE, JSON.stringify(next, null, 2));
    this.entries = next;
  }
}

/**
 * Opens the ledger, or reports why it cannot: the service still starts (the
 * payment monitor and sweeps do not depend on it), but every deposit refund
 * waits until an operator fixes the volume (DEPLOY_RUNBOOK §D).
 */
export function openDepositRefundLedger(dir: string): {
  ledger: DepositRefundLedger | null;
  error: string | null;
} {
  if (!dir) {
    return { ledger: null, error: 'DEPOSIT_REFUND_LEDGER_DIR is not set' };
  }
  try {
    return { ledger: new DepositRefundLedger(dir), error: null };
  } catch (err) {
    const error = `${dir}: ${(err as NodeJS.ErrnoException).code ?? (err as Error).message}`;
    logger.error(
      { dir, reason: (err as Error).message },
      'Deposit refund ledger is unavailable — every deposit refund waits (fail closed) until it is fixed',
    );
    return { ledger: null, error };
  }
}

export interface DepositRefundLimitDeps {
  /** Raw token units; null when unset or malformed → every deposit refund waits. */
  maxDailyUnits: bigint | null;
  ledger: DepositRefundLedger | null;
  /** Why the ledger is null (logged and returned with each refusal). */
  ledgerError?: string | null;
  /** 10^decimals of the tokens (6 for USDT and USDC) — for the USDT gauges. */
  decimalsPower: bigint;
  /**
   * One-time operator permits past the cap (sahip kararı 2026-10-23, modal);
   * null/absent = no refund ever passes over the cap.
   */
  permits?: DepositRefundPermitStore | null;
  now?: () => number;
}

export class DepositRefundLimit {
  private readonly maxDailyUnits: bigint | null;
  private readonly ledger: DepositRefundLedger | null;
  private readonly ledgerError: string;
  private readonly decimalsPower: bigint;
  private readonly permits: DepositRefundPermitStore | null;
  private readonly now: () => number;

  constructor(deps: DepositRefundLimitDeps) {
    this.maxDailyUnits = deps.maxDailyUnits;
    this.ledger = deps.ledger;
    this.ledgerError = deps.ledgerError ?? 'not opened';
    this.decimalsPower = deps.decimalsPower;
    this.permits = deps.permits ?? null;
    this.now = deps.now ?? (() => Date.now());
    if (this.maxDailyUnits !== null) {
      depositRefundDailyLimitUsdt.set(this.toUsdt(this.maxDailyUnits));
    }
    if (this.ledger) depositRefundWindowUsdt.set(this.toUsdt(this.usedUnits()));
  }

  /** What the deposit refunds counted in the last 24 hours add up to (raw units). */
  usedUnits(): bigint {
    if (!this.ledger) return 0n;
    const since = this.now() - DEPOSIT_REFUND_WINDOW_MS;
    let used = 0n;
    for (const entry of this.ledger.list()) {
      if (entry.at < since) continue;
      used += BigInt(entry.units);
    }
    return used;
  }

  /**
   * Counts this refund against the window, or refuses it. Synchronous from
   * the read of the total to the durable write of the entry, so no other
   * request can interleave. Returns the entry's key for
   * {@link settle} / {@link release}.
   */
  reserve(blockchainTransactionId: string, units: bigint, recipient: string): string {
    if (this.maxDailyUnits === null) {
      throw this.refuse(
        'DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED',
        'Deposit refund daily limit is not configured (DEPOSIT_REFUND_DAILY_LIMIT_USDT) — the refund waits, nothing signed.',
        blockchainTransactionId,
        units,
      );
    }
    if (!this.ledger) {
      throw this.refuse(
        'DEPOSIT_REFUND_LEDGER_UNAVAILABLE',
        `Deposit refund ledger is unavailable (${this.ledgerError}) — the refund waits, nothing signed.`,
        blockchainTransactionId,
        units,
      );
    }
    const now = this.now();
    const since = now - DEPOSIT_REFUND_WINDOW_MS;
    const live = this.ledger.list().filter((entry) => entry.at >= since);
    let used = 0n;
    for (const entry of live) used += BigInt(entry.units);
    const projected = used + units;
    depositRefundWindowUsdt.set(this.toUsdt(used));
    const key = randomUUID();
    let permitId: string | null = null;
    if (projected > this.maxDailyUnits) {
      // Sahip kararı 2026-10-23 — an operator's one-time permit for exactly
      // this refund lets it past the cap; it still counts toward the window.
      const permit = this.findPermit(blockchainTransactionId, recipient, units);
      if (permit !== null) {
        try {
          this.permits!.consume(permit, key);
          permitId = permit;
        } catch (err) {
          logger.error(
            { permitId: permit, reason: (err as Error).message },
            'Deposit refund permit could not be consumed — the refund waits',
          );
        }
      }
    }
    if (projected > this.maxDailyUnits && permitId === null) {
      throw this.refuse(
        'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
        `Deposit refunds in 24 h would reach ${projected} (counted ${used} + ${units}), above the configured limit ${this.maxDailyUnits} — the refund waits, nothing signed.`,
        blockchainTransactionId,
        units,
      );
    }
    const entry: DepositRefundEntry = {
      key,
      blockchainTransactionId,
      units: units.toString(),
      at: now,
      txHash: null,
      permitId,
    };
    try {
      // Entries that left the window are dropped on the same write.
      this.ledger.replace([...live, entry]);
    } catch (err) {
      if (permitId !== null) this.restorePermit(permitId);
      throw this.refuse(
        'DEPOSIT_REFUND_LEDGER_UNAVAILABLE',
        `Deposit refund ledger could not be written (${(err as Error).message}) — the refund waits, nothing signed.`,
        blockchainTransactionId,
        units,
      );
    }
    depositRefundWindowUsdt.set(this.toUsdt(projected));
    return entry.key;
  }

  /** The broadcast returned a hash: kept counted, with the hash for the operator. */
  settle(key: string, txHash: string): void {
    if (!this.ledger) return;
    try {
      this.ledger.replace(
        this.ledger.list().map((entry) => (entry.key === key ? { ...entry, txHash } : entry)),
      );
    } catch (err) {
      // The entry is already counted; only the hash annotation is lost.
      logger.warn(
        { key, txHash, reason: (err as Error).message },
        'Deposit refund ledger: hash not recorded (entry stays counted)',
      );
    }
  }

  /**
   * The transfer was certainly NOT broadcast: the entry stops counting. Any
   * failure that may have reached the network keeps it (fail closed).
   */
  release(key: string): void {
    if (!this.ledger) return;
    try {
      const released = this.ledger.list().find((entry) => entry.key === key);
      this.ledger.replace(this.ledger.list().filter((entry) => entry.key !== key));
      // Its permit is usable again: the transfer it allowed never left.
      if (released?.permitId) this.restorePermit(released.permitId);
      depositRefundWindowUsdt.set(this.toUsdt(this.usedUnits()));
    } catch (err) {
      logger.warn(
        { key, reason: (err as Error).message },
        'Deposit refund ledger: release not recorded (entry stays counted — fail closed)',
      );
    }
  }

  private findPermit(
    blockchainTransactionId: string,
    recipient: string,
    units: bigint,
  ): string | null {
    if (!this.permits) return null;
    try {
      return (
        this.permits.findUsable({ blockchainTransactionId, recipient, amountUnits: units })?.id ??
        null
      );
    } catch (err) {
      logger.error(
        { reason: (err as Error).message },
        'Deposit refund permits unreadable — no permit applies',
      );
      return null;
    }
  }

  private restorePermit(permitId: string): void {
    try {
      this.permits?.restore(permitId);
    } catch (err) {
      logger.error(
        { permitId, reason: (err as Error).message },
        'Deposit refund permit could not be restored (it stays used — write a new one)',
      );
    }
  }

  private refuse(
    reason: DepositRefundRefusal,
    message: string,
    blockchainTransactionId: string,
    units: bigint,
  ): TransferDeferredError {
    depositRefundLimitRefusalsTotal.inc({ reason });
    logger.error(
      {
        blockchainTransactionId,
        reason,
        units: units.toString(),
        limitUnits: this.maxDailyUnits?.toString() ?? null,
      },
      message,
    );
    return new TransferDeferredError(message, reason);
  }

  private toUsdt(units: bigint): number {
    return Number(units) / Number(this.decimalsPower);
  }
}
