import { canonicalAddress, sameAddress } from '../address/canonicalAddress.js';
import { SidecarError } from '../errors/SidecarError.js';
import { logger } from '../logger.js';
import {
  depositRefundDailyLimitUsdt,
  depositRefundLimitRefusalsTotal,
  depositRefundWindowUsdt,
} from '../metrics.js';
import type { EthereumRpcClient } from '../rpc/RpcClient.js';
import { TRANSFER_TOPIC, addressTopic } from './erc20.js';
import { TransferDeferredError } from './FeePolicy.js';
import type { DepositRefundPermitStore } from './DepositRefundPermits.js';
import type { JournalRecord } from './TxJournal.js';

/**
 * Value-safety guards of the Ethereum signer — the Ethereum twin of the TRON
 * service's `TransferGuard` (05 §3.3 "Transfer limitleri", 08 §3.3a, §3.7.6
 * "Sabit hedefler ve tavanlar"). The backend decides what to send; if the
 * backend, its database or an admin account is compromised, only the signer
 * can still say no.
 *
 * <list type="bullet">
 *   <item><b>Pinned destinations:</b> a sweep may only credit this service's
 *     own hot wallet, a consolidation only its own cold wallet.</item>
 *   <item><b>Single-transfer cap</b> on customer-facing sends (payout,
 *     refund): `ETHEREUM_MAX_SINGLE_TRANSFER_USDT`.</item>
 *   <item><b>24-hour cap</b> on hot-wallet payouts:
 *     `ETHEREUM_MAX_DAILY_OUTFLOW_USDT`. The total is read <b>from the chain</b>
 *     (allowlisted-token `Transfer` logs from the hot wallet over the last
 *     7,200 blocks ≈ 24 h, cold-wallet consolidation excluded) plus what the
 *     journal has signed that the chain does not show yet — so neither a
 *     restart nor a lost journal resets it. The scan is incremental: only
 *     blocks not read before are asked for.</item>
 *   <item><b>T172b — 24-hour cap on deposit refunds</b>:
 *     `ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT` (proje sahibi kararı
 *     2026-10-23; the backend's twin is T169a). A refund the deposit key signs
 *     leaves a deposit, so the hot wallet cap never saw it. Counted from the
 *     journal — every REFUND record the DEPOSIT key signed in the last 24 h
 *     (by `createdAt`), written to disk before its broadcast, so a restart
 *     cannot reset it; reverted and nonce-conflicted records moved nothing and
 *     do not count. Over the cap, or with the cap unset, the refund waits
 *     (`TransferDeferredError` → 503 deferred) — nothing signed, no fuel.</item>
 * </list>
 * Every cap is fail-closed: unset or malformed refuses the send.
 */

/** ~24 h of 12-second slots. Missed slots make it longer in time — the conservative side. */
export const OUTFLOW_WINDOW_BLOCKS = 7_200n;
export const OUTFLOW_WINDOW_MS = 24 * 60 * 60 * 1000;

export interface TransferGuardDeps {
  rpc: Pick<EthereumRpcClient, 'getLogs'>;
  /** Canonical; null when not configured. */
  hotWalletAddress: string | null;
  coldWalletAddress: string | null;
  /** Canonical allowlisted token contracts (USDT, USDC) — outflow of anything else is not ours. */
  tokenContracts: readonly string[];
  maxSingleTransferUnits: bigint | null;
  maxDailyOutflowUnits: bigint | null;
  /** T172b — raw token units; null when unset → every deposit refund waits (fail closed). */
  maxDailyDepositRefundUnits: bigint | null;
  /**
   * T172b — one-time operator permits past the deposit refund cap (proje
   * sahibi kararı 2026-10-23, modal), written on the server by
   * `node dist/cli/depositRefundPermit.js`; absent = none ever applies.
   */
  depositRefundPermits?: DepositRefundPermitStore | null;
  now?: () => number;
}

/** USDT and USDC both carry 6 decimals (08 §3.7.3). */
const TOKEN_UNIT = 1_000_000;

interface OutflowEntry {
  readonly block: bigint;
  readonly txHash: string;
  readonly units: bigint;
}

export class TransferGuard {
  private readonly entries = new Map<string, OutflowEntry>();
  private scannedTo: bigint | null = null;
  private readonly now: () => number;

  constructor(private readonly deps: TransferGuardDeps) {
    this.now = deps.now ?? (() => Date.now());
    if (deps.maxDailyDepositRefundUnits !== null) {
      depositRefundDailyLimitUsdt.set(Number(deps.maxDailyDepositRefundUnits) / TOKEN_UNIT);
    }
  }

  /**
   * T172b — a refund the DEPOSIT key is about to sign: the deposit refunds
   * the journal holds for the last 24 hours + this amount must stay within
   * the cap (equal passes). The caller holds the service's single deposit
   * refund lock from this check until the record is on disk, so two refunds
   * from two deposits cannot both read the same total.
   */
  assertDepositRefundDailyLimit(
    units: bigint,
    journal: readonly JournalRecord[],
    refund: { readonly blockchainTransactionId: string; readonly recipient: string },
  ): string | null {
    const max = this.deps.maxDailyDepositRefundUnits;
    if (max === null) {
      depositRefundLimitRefusalsTotal.inc({ reason: 'DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED' });
      throw new TransferDeferredError(
        'Deposit refund daily limit is not configured (ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT) — the refund waits, nothing signed.',
        'DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED',
      );
    }
    const used = this.depositRefundsLast24h(journal);
    depositRefundWindowUsdt.set(Number(used) / TOKEN_UNIT);
    const projected = used + units;
    if (projected > max) {
      // Sahip kararı 2026-10-23 — an operator's one-time permit for exactly
      // this refund lets it past the cap (still counted: it is journalled).
      // Returned, not consumed: the caller consumes it right before the
      // signature is journalled, so a wait (fuel, fee) does not burn it.
      const permitId = this.findPermit(units, refund);
      if (permitId !== null) return permitId;
      depositRefundLimitRefusalsTotal.inc({ reason: 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED' });
      throw new TransferDeferredError(
        `Deposit refunds in 24 h would reach ${projected} (journal ${used} + ${units}), above the configured limit ${max} — the refund waits, nothing signed.`,
        'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED',
      );
    }
    return null;
  }

  /** Marks a permit used, durably — called right before the signed refund is journalled. */
  consumeDepositRefundPermit(permitId: string, usedBy: string): void {
    try {
      if (!this.deps.depositRefundPermits) throw new Error('no permit store');
      this.deps.depositRefundPermits.consume(permitId, usedBy);
    } catch (err) {
      throw new SidecarError(
        `Deposit refund permit ${permitId} could not be consumed (${(err as Error).message}) — nothing signed.`,
        'DEPOSIT_REFUND_PERMIT_UNAVAILABLE',
        true,
      );
    }
  }

  /** The signature it was consumed for was never journalled: usable again. */
  restoreDepositRefundPermit(permitId: string): void {
    try {
      this.deps.depositRefundPermits?.restore(permitId);
    } catch (err) {
      logger.error(
        { permitId, reason: (err as Error).message },
        'Deposit refund permit could not be restored (it stays used — write a new one)',
      );
    }
  }

  private findPermit(
    units: bigint,
    refund: { readonly blockchainTransactionId: string; readonly recipient: string },
  ): string | null {
    const store = this.deps.depositRefundPermits;
    if (!store) return null;
    try {
      return (
        store.findUsable({
          blockchainTransactionId: refund.blockchainTransactionId,
          recipient: refund.recipient,
          amountUnits: units,
        })?.id ?? null
      );
    } catch (err) {
      logger.error(
        { reason: (err as Error).message },
        'Deposit refund permits unreadable — no permit applies',
      );
      return null;
    }
  }

  /** Raw units of the REFUND records the deposit key signed in the last 24 hours. */
  depositRefundsLast24h(journal: readonly JournalRecord[]): bigint {
    const since = this.now() - OUTFLOW_WINDOW_MS;
    let sum = 0n;
    for (const record of journal) {
      if (record.purpose !== 'REFUND' || record.signer !== 'DEPOSIT') continue;
      if (record.createdAt < since) continue;
      // Moved nothing: its nonce went to a transaction the journal does not
      // know, or it reverted on chain.
      if (record.conflict !== null) continue;
      if (record.mined?.status === 0) continue;
      sum += BigInt(record.amountUnits);
    }
    return sum;
  }

  assertSweepDestination(requested: string): void {
    if (!this.deps.hotWalletAddress) {
      throw new SidecarError(
        'ETHEREUM_HOT_WALLET_ADDRESS is not configured — no sweep destination.',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    if (!sameAddress(requested, this.deps.hotWalletAddress)) {
      throw new SidecarError(
        `Sweep destination ${requested} is not the configured hot wallet — refusing to sign.`,
        'DESTINATION_NOT_ALLOWED',
        false,
      );
    }
  }

  assertColdDestination(requested: string): void {
    if (!this.deps.coldWalletAddress) {
      throw new SidecarError(
        'ETHEREUM_COLD_WALLET_ADDRESS is not configured.',
        'COLD_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    if (!sameAddress(requested, this.deps.coldWalletAddress)) {
      throw new SidecarError(
        `Cold transfer destination ${requested} is not the configured cold wallet — refusing to sign.`,
        'DESTINATION_NOT_ALLOWED',
        false,
      );
    }
  }

  assertSingleTransferLimit(units: bigint): void {
    const max = this.deps.maxSingleTransferUnits;
    if (max === null) {
      throw new SidecarError(
        'Single-transfer limit is not configured (ETHEREUM_MAX_SINGLE_TRANSFER_USDT) — refusing to sign.',
        'TRANSFER_LIMIT_NOT_CONFIGURED',
        false,
      );
    }
    if (units > max) {
      throw new SidecarError(
        `Transfer amount ${units} exceeds the single-transfer limit ${max}.`,
        'TRANSFER_AMOUNT_ABOVE_LIMIT',
        false,
      );
    }
  }

  /**
   * Hot-wallet payouts and (T158 (f)) refunds after the sweep: chain outflow of the last 7,200 blocks up to
   * <c>headNumber</c> + journal payouts the chain does not show yet + this
   * amount must stay within the daily cap.
   */
  async assertHotWalletDailyLimit(
    units: bigint,
    headNumber: bigint,
    journal: readonly JournalRecord[],
  ): Promise<void> {
    const max = this.deps.maxDailyOutflowUnits;
    if (max === null) {
      throw new SidecarError(
        'Daily outflow limit is not configured (ETHEREUM_MAX_DAILY_OUTFLOW_USDT) — refusing to sign.',
        'TRANSFER_LIMIT_NOT_CONFIGURED',
        false,
      );
    }
    const { total, seen } = await this.chainOutflow(headNumber);
    const inFlight = this.journalOutflow(journal, seen);
    const projected = total + inFlight + units;
    if (projected > max) {
      throw new SidecarError(
        `Hot wallet 24h outflow would reach ${projected} (chain ${total} + signed-not-seen ${inFlight} + ${units}), above the configured limit ${max}.`,
        'DAILY_OUTFLOW_LIMIT_EXCEEDED',
        false,
      );
    }
  }

  private journalOutflow(journal: readonly JournalRecord[], seen: Set<string>): bigint {
    const since = this.now() - OUTFLOW_WINDOW_MS;
    let sum = 0n;
    for (const record of journal) {
      // Hot-wallet outflow signed but not yet seen on chain: payouts and
      // (T158 (f)) refunds the hot key signed after a sweep.
      const hotOutflow =
        record.purpose === 'PAYOUT' || (record.purpose === 'REFUND' && record.signer === 'HOT');
      if (!hotOutflow || record.createdAt < since) continue;
      if (record.conflict !== null) continue;
      if (record.mined?.status === 0) continue;
      if (record.attempts.some((a) => seen.has(a.hash.toLowerCase()))) continue;
      sum += BigInt(record.amountUnits);
    }
    return sum;
  }

  private async chainOutflow(head: bigint): Promise<{ total: bigint; seen: Set<string> }> {
    const hot = this.deps.hotWalletAddress;
    if (!hot) {
      throw new SidecarError(
        'ETHEREUM_HOT_WALLET_ADDRESS is not configured.',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    const windowStart = head >= OUTFLOW_WINDOW_BLOCKS ? head - OUTFLOW_WINDOW_BLOCKS + 1n : 0n;
    const from =
      this.scannedTo === null || this.scannedTo < windowStart - 1n
        ? windowStart
        : this.scannedTo + 1n;
    if (from <= head && this.deps.tokenContracts.length > 0) {
      let logs;
      try {
        logs = await this.deps.rpc.getLogs({
          address: [...this.deps.tokenContracts],
          topics: [TRANSFER_TOPIC, addressTopic(hot)],
          fromBlock: from,
          toBlock: head,
        });
      } catch (err) {
        throw new SidecarError(
          `Hot wallet outflow history is unreadable — cannot evaluate the daily limit: ${(err as Error).message}`,
          'OUTFLOW_HISTORY_UNAVAILABLE',
          true,
        );
      }
      for (const log of logs) {
        if (log.removed) continue;
        const recipient = canonicalAddress('0x' + (log.topics[2] ?? '').slice(26));
        // Consolidation to the pinned cold wallet is platform-internal movement.
        if (this.deps.coldWalletAddress && sameAddress(recipient, this.deps.coldWalletAddress))
          continue;
        this.entries.set(`${log.transactionHash.toLowerCase()}:${log.logIndex}`, {
          block: log.blockNumber,
          txHash: log.transactionHash.toLowerCase(),
          units: BigInt(log.data === '0x' ? 0 : log.data),
        });
      }
      this.scannedTo = head;
      logger.debug(
        { from: String(from), to: String(head), entries: this.entries.size },
        'Hot wallet outflow scanned',
      );
    }
    let total = 0n;
    const seen = new Set<string>();
    for (const [key, entry] of this.entries) {
      if (entry.block < windowStart) {
        this.entries.delete(key);
        continue;
      }
      total += entry.units;
      seen.add(entry.txHash);
    }
    return { total, seen };
  }
}
