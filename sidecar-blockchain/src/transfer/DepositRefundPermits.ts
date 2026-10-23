import { randomUUID } from 'node:crypto';
import {
  closeSync,
  existsSync,
  fsyncSync,
  mkdirSync,
  openSync,
  readFileSync,
  readdirSync,
  renameSync,
  writeSync,
} from 'node:fs';
import { join } from 'node:path';
import { canonicalAddress, sameAddress } from '../address/canonicalAddress.js';
import { logger } from '../logger.js';

/**
 * T172b — one-time operator permits past the signer's 24-hour deposit refund
 * cap (proje sahibi kararı 2026-10-23, modal: "sunucuda tek seferlik izin").
 *
 * <para>
 * A refund larger than the cap — or one an admin released past the backend's
 * limit (AD42) — can never pass the signer's cap on its own. The owner's rule:
 * an operator on the server writes a permit for exactly that refund with a
 * command (`docker exec … node dist/cli/depositRefundPermit.js create …`). The
 * permit lives ONLY in this service's own volume; there is no HTTP endpoint,
 * so neither the backend nor anything on the network can create one — the
 * point of the cap is that a compromised backend cannot lift it.
 * </para>
 *
 * <para>
 * <b>What a permit binds</b> (all must match): the network, the refund's
 * <c>BlockchainTransaction.Id</c>, the recipient address, the amount in token
 * units — <b>exactly</b> (öneriyle kaydedildi — ⚠ PARA: the refund's amount
 * is known to the unit from the admin screen, so an upper bound would only add
 * room a substituted request could use; a typo makes the refund wait, the
 * safe direction) — and a validity window (default and maximum 24 hours).
 * It is single-use: the first signing that uses it consumes it durably; a
 * second signing finds it consumed. The permitted refund still counts toward
 * the 24-hour total, so the cap is not opened for any other refund.
 * </para>
 *
 * <para>
 * <b>Storage.</b> One JSON file per permit in its own directory
 * (<c>deposit-refund-permits/</c> under the service's volume), each written
 * temp → fsync → rename. The CLI (another process) only ever creates new
 * files and the service only rewrites a permit's own file, so the two never
 * lose each other's writes. The service re-reads the directory at every use.
 * </para>
 *
 * This file is byte-identical in sidecar-blockchain and sidecar-ethereum
 * (network-agnostic: the network and the address check are parameters).
 */

export const PERMIT_DIR_NAME = 'deposit-refund-permits';
/** Default and longest validity of a permit. */
export const MAX_PERMIT_VALIDITY_MS = 24 * 60 * 60 * 1000;

export type PermitEvent = 'created' | 'seen' | 'used' | 'restored' | 'expired' | 'revoked';

export interface DepositRefundPermit {
  readonly id: string;
  /** `TRON` | `ETHEREUM` — a permit is honoured only by its own network's service. */
  readonly network: string;
  /** Lowercase `BlockchainTransaction.Id` of the refund row. */
  readonly blockchainTransactionId: string;
  /** Canonical recipient address. */
  readonly recipient: string;
  /** Exact amount, raw token units (6 decimals), decimal string. */
  readonly amountUnits: string;
  readonly createdAt: number;
  readonly expiresAt: number;
  readonly createdBy: string;
  readonly reason: string;
  /** Set when a signing used it; null while unused. */
  readonly consumedAt: number | null;
  /** The signing attempt that used it (ledger key / journal id). */
  readonly consumedBy: string | null;
  readonly revokedAt: number | null;
  /** Set once the service has reported the permit expired unused. */
  readonly expiredReportedAt: number | null;
}

export interface PermitRequest {
  readonly blockchainTransactionId: string;
  readonly recipient: string;
  readonly amountUnits: bigint;
}

export interface DepositRefundPermitStoreOptions {
  /** The service's volume directory; permits live in its `deposit-refund-permits/`. */
  readonly baseDir: string;
  readonly network: 'TRON' | 'ETHEREUM';
  /** The network's address format check (the CLI refuses anything else). */
  readonly isValidAddress: (address: string) => boolean;
  readonly now?: () => number;
  /** Metric hook (the service's counter); the CLI passes none. */
  readonly onEvent?: (event: PermitEvent) => void;
}

const ID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/;
const FILE = /^permit-([0-9a-f-]{36})\.json$/;

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
    const dirFd = openSync(dir, 'r');
    try {
      fsyncSync(dirFd);
    } finally {
      closeSync(dirFd);
    }
  } catch {
    // Best effort (Windows cannot open a directory).
  }
}

/** Parses a decimal token amount (≤ 6 decimals, > 0) into raw units, or null. */
export function parsePermitAmount(raw: string): bigint | null {
  const trimmed = raw.trim();
  if (!/^\d+(?:\.\d{1,6})?$/.test(trimmed)) return null;
  const [whole, fraction = ''] = trimmed.split('.');
  const units = BigInt(whole) * 1_000_000n + BigInt(fraction.padEnd(6, '0'));
  return units > 0n ? units : null;
}

export class DepositRefundPermitStore {
  readonly dir: string;
  private readonly network: 'TRON' | 'ETHEREUM';
  private readonly isValidAddress: (address: string) => boolean;
  private readonly now: () => number;
  private readonly onEvent: (event: PermitEvent) => void;
  private readonly seen = new Set<string>();

  constructor(options: DepositRefundPermitStoreOptions) {
    this.dir = join(options.baseDir, PERMIT_DIR_NAME);
    this.network = options.network;
    this.isValidAddress = options.isValidAddress;
    this.now = options.now ?? (() => Date.now());
    this.onEvent = options.onEvent ?? (() => undefined);
    mkdirSync(this.dir, { recursive: true });
  }

  /** CLI — writes a new permit. Throws on anything malformed. */
  create(input: {
    blockchainTransactionId: string;
    recipient: string;
    amount: string;
    validForMs?: number;
    createdBy: string;
    reason: string;
  }): DepositRefundPermit {
    const btid = input.blockchainTransactionId.trim().toLowerCase();
    if (!ID.test(btid)) {
      throw new Error('--tx must be the refund row id (BlockchainTransaction.Id, a GUID).');
    }
    const recipient = input.recipient.trim();
    if (!this.isValidAddress(recipient)) {
      throw new Error(`--to is not a valid ${this.network} address: ${recipient}`);
    }
    const units = parsePermitAmount(input.amount);
    if (units === null) {
      throw new Error('--amount must be a positive decimal with at most 6 fraction digits.');
    }
    const validFor = input.validForMs ?? MAX_PERMIT_VALIDITY_MS;
    if (!Number.isInteger(validFor) || validFor < 60_000 || validFor > MAX_PERMIT_VALIDITY_MS) {
      throw new Error('Validity must be between 1 minute and 24 hours.');
    }
    if (!input.createdBy.trim() || !input.reason.trim()) {
      throw new Error('--by and --reason are required (who writes the permit, and why).');
    }
    const now = this.now();
    const permit: DepositRefundPermit = {
      id: randomUUID(),
      network: this.network,
      blockchainTransactionId: btid,
      recipient: canonicalAddress(recipient),
      amountUnits: units.toString(),
      createdAt: now,
      expiresAt: now + validFor,
      createdBy: input.createdBy.trim(),
      reason: input.reason.trim(),
      consumedAt: null,
      consumedBy: null,
      revokedAt: null,
      expiredReportedAt: null,
    };
    this.write(permit);
    logger.warn({ permit }, 'Deposit refund permit CREATED (operator, on the server)');
    this.onEvent('created');
    return permit;
  }

  /** Every permit on disk (unreadable files are skipped and logged — they never match). */
  list(): DepositRefundPermit[] {
    if (!existsSync(this.dir)) return [];
    const permits: DepositRefundPermit[] = [];
    for (const name of readdirSync(this.dir)) {
      if (!FILE.test(name)) continue;
      try {
        const permit = JSON.parse(
          readFileSync(join(this.dir, name), 'utf8'),
        ) as DepositRefundPermit;
        if (permit && typeof permit.id === 'string' && `permit-${permit.id}.json` === name) {
          permits.push(permit);
        }
      } catch (err) {
        logger.error(
          { file: name, reason: (err as Error).message },
          'Deposit refund permit file unreadable — ignored (it permits nothing)',
        );
      }
    }
    return permits.sort((a, b) => a.createdAt - b.createdAt);
  }

  /**
   * The oldest live permit for exactly this refund, or null. Also reports
   * permits that expired unused (log + metric, once).
   */
  findUsable(request: PermitRequest): DepositRefundPermit | null {
    const now = this.now();
    const btid = request.blockchainTransactionId.toLowerCase();
    let found: DepositRefundPermit | null = null;
    for (const permit of this.list()) {
      if (!this.seen.has(permit.id)) {
        this.seen.add(permit.id);
        if (permit.consumedAt === null && permit.revokedAt === null && permit.expiresAt > now) {
          logger.warn({ permitId: permit.id }, 'Deposit refund permit seen by the service');
          this.onEvent('seen');
        }
      }
      if (permit.consumedAt !== null || permit.revokedAt !== null) continue;
      if (permit.expiresAt <= now) {
        this.reportExpired(permit, now);
        continue;
      }
      if (found !== null) continue;
      if (
        permit.network === this.network &&
        permit.blockchainTransactionId === btid &&
        sameAddress(permit.recipient, request.recipient) &&
        permit.amountUnits === request.amountUnits.toString()
      ) {
        found = permit;
      }
    }
    return found;
  }

  /** Marks the permit used — durably, before the signing it allows. */
  consume(permitId: string, usedBy: string): void {
    const permit = this.read(permitId);
    if (permit.consumedAt !== null || permit.revokedAt !== null) {
      throw new Error(`Deposit refund permit ${permitId} is no longer usable.`);
    }
    this.write({ ...permit, consumedAt: this.now(), consumedBy: usedBy });
    logger.warn(
      { permitId, usedBy, blockchainTransactionId: permit.blockchainTransactionId },
      'Deposit refund permit USED — this refund passes the 24-hour cap once',
    );
    this.onEvent('used');
  }

  /** The signing it was consumed for certainly never left: usable again (until it expires). */
  restore(permitId: string): void {
    const permit = this.read(permitId);
    this.write({ ...permit, consumedAt: null, consumedBy: null });
    logger.warn({ permitId }, 'Deposit refund permit restored — its transfer never left');
    this.onEvent('restored');
  }

  /** CLI — withdraws an unused permit. */
  revoke(permitId: string): DepositRefundPermit {
    const permit = this.read(permitId);
    const revoked = { ...permit, revokedAt: this.now() };
    this.write(revoked);
    logger.warn({ permitId }, 'Deposit refund permit revoked');
    this.onEvent('revoked');
    return revoked;
  }

  /** Reports every permit that expired unused (the service runs this periodically). */
  reportExpiredAll(): void {
    const now = this.now();
    for (const permit of this.list()) {
      if (permit.consumedAt === null && permit.revokedAt === null && permit.expiresAt <= now) {
        this.reportExpired(permit, now);
      }
    }
  }

  private reportExpired(permit: DepositRefundPermit, now: number): void {
    if (permit.expiredReportedAt !== null) return;
    try {
      this.write({ ...permit, expiredReportedAt: now });
    } catch {
      // Reported again next time; nothing depends on the mark.
    }
    logger.warn(
      { permitId: permit.id, blockchainTransactionId: permit.blockchainTransactionId },
      'Deposit refund permit EXPIRED unused',
    );
    this.onEvent('expired');
  }

  private read(permitId: string): DepositRefundPermit {
    if (!ID.test(permitId)) throw new Error(`Not a permit id: ${permitId}`);
    return JSON.parse(
      readFileSync(join(this.dir, `permit-${permitId}.json`), 'utf8'),
    ) as DepositRefundPermit;
  }

  private write(permit: DepositRefundPermit): void {
    writeFileDurably(this.dir, `permit-${permit.id}.json`, JSON.stringify(permit, null, 2));
  }
}
