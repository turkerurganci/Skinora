import {
  closeSync,
  fsyncSync,
  mkdirSync,
  openSync,
  readFileSync,
  readdirSync,
  renameSync,
  rmSync,
  writeSync,
} from 'node:fs';
import { join } from 'node:path';
import { canonicalAddress } from '../address/canonicalAddress.js';
import { ConfigError } from '../config/index.js';

/**
 * The durable record of every transaction the service signs (08 §3.7.6 "Çift
 * ödeme imkânsız", T152).
 *
 * <para>
 * <b>Why it exists.</b> On Ethereum two signatures for the same payout with
 * two different nonces are two payouts. The rule that makes a double payment
 * impossible is: a request id gets <i>one</i> nonce, ever, and the signed bytes
 * are written to disk <b>before</b> they are broadcast. A retry of the same id
 * (backend timeout, restart, a second dispatcher tick) finds the record and
 * re-broadcasts the SAME bytes; a fee bump is a new signature at the SAME
 * nonce, so at most one of them can enter the chain. A restart resumes from
 * these files (FK8-5).
 * </para>
 *
 * <para>
 * <b>Storage.</b> One JSON file per record in a directory on a Docker volume
 * (`ETHEREUM_TX_JOURNAL_DIR`), written as temp file → fsync → rename, so a
 * crash leaves either the old or the new version, never half of one. The
 * service refuses to start when the directory cannot be written or a record
 * cannot be read: a signer that cannot remember what it signed must not sign.
 * </para>
 */

export type TransferPurpose = 'PAYOUT' | 'SWEEP' | 'REFUND' | 'COLD' | 'FUEL';
export type SignerRole = 'HOT' | 'DEPOSIT' | 'FUEL';

export interface JournalAttempt {
  readonly hash: string;
  readonly raw: string;
  readonly maxFeePerGas: string;
  readonly maxPriorityFeePerGas: string;
  readonly signedAt: number;
}

export interface MinedState {
  readonly hash: string;
  readonly blockNumber: string;
  readonly blockHash: string;
  readonly status: number | null;
  /** In a block at or below `finalized`, re-read canonical — never re-checked. */
  readonly final: boolean;
}

export interface JournalRecord {
  /** `transfer:<BlockchainTransaction.Id>` / `cold:<ColdWalletTransfer.Id>` / `fuel:<id>`. */
  readonly id: string;
  readonly purpose: TransferPurpose;
  readonly refundType: string | null;
  readonly signer: SignerRole;
  /** Canonical (lowercase) sender. */
  readonly from: string;
  readonly depositIndex: number | null;
  /** Canonical token contract; null for a fuel (ETH) send. */
  readonly contract: string | null;
  readonly token: 'USDT' | 'USDC' | null;
  /** Canonical recipient (token recipient, or the fuelled deposit). */
  readonly recipient: string;
  /** Raw token units, or wei for fuel. */
  readonly amountUnits: string;
  readonly nonce: number;
  readonly gasLimit: string;
  /**
   * Capped flows: the most the transaction may cost in fees, wei —
   * `gasLimit × maxFeePerGas` of every attempt stays at or under it (08 §3.7.7,
   * per transaction since T152 düzeltme turu 2); null when uncapped.
   */
  readonly feeCapWei?: string | null;
  /** Records signed before T152 düzeltme turu 2: the cap per gas. Read as `× gasLimit`. */
  readonly feeCapWeiPerGas?: string | null;
  readonly attempts: readonly JournalAttempt[];
  readonly createdAt: number;
  readonly lastBroadcastAt: number | null;
  readonly lastBroadcastError: string | null;
  readonly mined: MinedState | null;
  /** First time the sender's nonce was seen past this record with none of its attempts mined. */
  readonly conflictSuspectedAt: number | null;
  /** Confirmed: the nonce was consumed by a transaction this journal does not know. */
  readonly conflict: { readonly detectedAt: number; readonly chainNonce: number } | null;
  /**
   * T153 — a fuel record only: the identity of the request it was sent for
   * (bulgu e). While the request's own `transfer:<id>` is not signed yet, the
   * same id asked with another identity is IDEMPOTENCY_CONFLICT, as it would
   * be against the transfer record.
   */
  readonly fuelFor?: FuelRequestIdentity | null;
}

/** What a fuel record remembers of the request it was sent for (T153). */
export interface FuelRequestIdentity {
  readonly purpose: TransferPurpose;
  readonly token: 'USDT' | 'USDC';
  /** Canonical token recipient. */
  readonly recipient: string;
  readonly amountUnits: string;
  readonly refundType: string | null;
}

/** A record's fee budget in wei (legacy per-gas records: per gas × gas limit), or null. */
export function recordFeeCapWei(record: JournalRecord): bigint | null {
  if (record.feeCapWei !== undefined && record.feeCapWei !== null) return BigInt(record.feeCapWei);
  if (record.feeCapWeiPerGas !== undefined && record.feeCapWeiPerGas !== null) {
    return BigInt(record.feeCapWeiPerGas) * BigInt(record.gasLimit);
  }
  return null;
}

const ID_PATTERN =
  /^(transfer|cold|fuel):[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/;

/** Whether an id is one this journal can store (prefix + GUID — safe as a file name). */
export function isJournalId(id: string): boolean {
  return ID_PATTERN.test(id);
}

function fileName(id: string): string {
  return id.toLowerCase().replace(':', '-') + '.json';
}

/** Writes a file so that a crash leaves either the previous or the new content. */
export function writeFileDurably(dir: string, name: string, content: string): void {
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

export class TxJournal {
  private readonly records = new Map<string, JournalRecord>();
  private readonly byHash = new Map<string, string>();

  constructor(readonly dir: string) {
    try {
      mkdirSync(dir, { recursive: true });
      // Prove the directory is writable now, not at the first payout.
      writeFileDurably(dir, '.write-probe', String(Date.now()));
      rmSync(join(dir, '.write-probe'), { force: true });
    } catch (err) {
      throw new ConfigError(
        'TX_JOURNAL_UNAVAILABLE',
        `The transaction journal directory ${dir} is not writable (${(err as NodeJS.ErrnoException).code ?? 'error'}) — the service does not sign without it (08 §3.7.6).`,
      );
    }
    for (const name of readdirSync(dir)) {
      if (!name.endsWith('.json') || name.startsWith('.') || name === 'fee-waits.json') continue;
      let record: JournalRecord;
      try {
        record = JSON.parse(readFileSync(join(dir, name), 'utf8')) as JournalRecord;
      } catch {
        throw new ConfigError(
          'TX_JOURNAL_CORRUPT',
          `Journal record ${name} cannot be read — refusing to start rather than forget a signed transaction.`,
        );
      }
      if (!record || typeof record.id !== 'string' || !isJournalId(record.id)) {
        throw new ConfigError('TX_JOURNAL_CORRUPT', `Journal record ${name} has no valid id.`);
      }
      this.index(record);
    }
  }

  get size(): number {
    return this.records.size;
  }

  get(id: string): JournalRecord | undefined {
    return this.records.get(id.toLowerCase());
  }

  findByHash(hash: string): JournalRecord | undefined {
    const id = this.byHash.get(hash.toLowerCase());
    return id ? this.records.get(id) : undefined;
  }

  all(): JournalRecord[] {
    return [...this.records.values()];
  }

  bySender(from: string): JournalRecord[] {
    const sender = canonicalAddress(from);
    return this.all().filter((r) => r.from === sender);
  }

  /** Records that still need the keeper: not final on chain, not in confirmed conflict. */
  unsettled(): JournalRecord[] {
    return this.all().filter((r) => !(r.mined?.final ?? false) && r.conflict === null);
  }

  /** Durable first, memory second: what the process believes is always on disk. */
  put(record: JournalRecord): void {
    if (!isJournalId(record.id)) {
      throw new Error(`Journal id ${record.id} is not a prefix:GUID id.`);
    }
    const normalized: JournalRecord = { ...record, id: record.id.toLowerCase() };
    writeFileDurably(this.dir, fileName(normalized.id), JSON.stringify(normalized, null, 2));
    this.index(normalized);
  }

  private index(record: JournalRecord): void {
    this.records.set(record.id.toLowerCase(), record);
    for (const attempt of record.attempts) {
      this.byHash.set(attempt.hash.toLowerCase(), record.id.toLowerCase());
    }
  }
}
