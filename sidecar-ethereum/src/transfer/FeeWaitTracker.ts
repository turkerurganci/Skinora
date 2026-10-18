import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { logger } from '../logger.js';
import type { DeferralReason } from './FeePolicy.js';
import { writeFileDurably } from './TxJournal.js';

/**
 * How long each send has been waiting without being signed (08 §3.7.7 "Bekle"
 * eşiği, T142: 3 hours, öneriyle kaydedildi — ⚠).
 *
 * <para>
 * A sweep, payout or cold transfer whose fee is above the cap — or whose cap
 * cannot be converted because the ETH price is missing — is not signed; the
 * backend keeps the row PENDING and asks again every minute. The first refusal
 * starts the clock, each later one keeps it running, a signature stops it. A
 * request the backend stops asking about (row cancelled, moved to another
 * path) drops out after {@link STALE_AFTER_MS}: the wait is "uninterrupted"
 * only while someone is still waiting.
 * </para>
 *
 * <para>
 * The clocks are written to the journal directory so a restart does not reset
 * a three-hour wait to zero.
 * </para>
 */

export type WaitReason = DeferralReason;

/** Reasons the 3-hour fee-cap alarm counts (08 §3.7.7); gas shortages have their own balance alarms. */
export const FEE_CAP_WAIT_REASONS: readonly WaitReason[] = [
  'FEE_ABOVE_CAP',
  'ETH_PRICE_UNAVAILABLE',
];

/**
 * Gas shortages (`kind="gas"`): the sender has no ETH for the fee. They are
 * not the fee-cap alarm's; the fuel and hot wallet balance alarms
 * (`ethereum-wallet-eth-low`, T153) and the backend's
 * `outbound-transfer-stuck` see them.
 */
export const GAS_WAIT_REASONS: readonly WaitReason[] = [
  'FUEL_REQUIRED',
  'FUEL_PENDING',
  'FUEL_LIMIT_REACHED',
  'FUEL_WALLET_LOW',
  'HOT_WALLET_GAS_LOW',
];

interface WaitEntry {
  reason: WaitReason;
  firstAt: number;
  lastAt: number;
}

/** A request not asked about for this long is no longer waiting (the backend asks every minute). */
export const STALE_AFTER_MS = 15 * 60 * 1000;

const FILE = 'fee-waits.json';

export class FeeWaitTracker {
  private readonly entries = new Map<string, WaitEntry>();

  constructor(
    private readonly dir: string,
    private readonly now: () => number = () => Date.now(),
  ) {
    const path = join(dir, FILE);
    if (!existsSync(path)) return;
    try {
      const parsed = JSON.parse(readFileSync(path, 'utf8')) as Record<string, WaitEntry>;
      for (const [id, entry] of Object.entries(parsed)) this.entries.set(id, entry);
    } catch {
      // Losing the clocks restarts them; it never sends anything.
      logger.warn('fee-waits.json is unreadable — wait clocks restart from now');
    }
  }

  /** Records a refusal; returns when this uninterrupted wait began. */
  deferred(id: string, reason: WaitReason): number {
    const now = this.now();
    const existing = this.entries.get(id);
    // A wait continues only within one kind: a fee wait that follows a gas
    // shortage starts its own clock (each has its own alarm).
    const continuing =
      existing !== undefined &&
      now - existing.lastAt <= STALE_AFTER_MS &&
      FEE_CAP_WAIT_REASONS.includes(existing.reason) === FEE_CAP_WAIT_REASONS.includes(reason);
    const entry: WaitEntry = {
      reason,
      firstAt: continuing ? existing.firstAt : now,
      lastAt: now,
    };
    this.entries.set(id, entry);
    this.persist();
    return entry.firstAt;
  }

  /** The send was signed (or refused for good) — its clock stops. */
  cleared(id: string): void {
    if (this.entries.delete(id)) this.persist();
  }

  /** Longest live wait in seconds among the given reasons (0 when none). */
  longestWaitSeconds(reasons: readonly WaitReason[]): number {
    const now = this.now();
    let longest = 0;
    for (const entry of this.entries.values()) {
      if (!reasons.includes(entry.reason)) continue;
      if (now - entry.lastAt > STALE_AFTER_MS) continue;
      longest = Math.max(longest, Math.floor((now - entry.firstAt) / 1000));
    }
    return longest;
  }

  private persist(): void {
    const now = this.now();
    for (const [id, entry] of this.entries) {
      if (now - entry.lastAt > STALE_AFTER_MS) this.entries.delete(id);
    }
    try {
      writeFileDurably(this.dir, FILE, JSON.stringify(Object.fromEntries(this.entries)));
    } catch (err) {
      logger.warn({ reason: (err as Error).message }, 'fee-waits.json could not be written');
    }
  }
}
