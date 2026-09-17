import TronWeb from 'tronweb';
import { SidecarError } from '../errors/SidecarError.js';
import { logger } from '../logger.js';
import type { TokenContractMap } from './TransferService.js';

/**
 * Value-safety guards applied by the signer itself (05 §3.3 "Transfer
 * limitleri", owner decisions 2026-09-16/17).
 *
 * <para>
 * Two families live here, and both exist because the sidecar is the last
 * component that can refuse. The backend decides <i>what</i> to send; if the
 * backend, its database or an admin account is compromised, only the signer
 * can still say no.
 * </para>
 *
 * <para>
 * <b>Destination pinning.</b> The two platform-owned destinations — the hot
 * wallet a sweep credits and the cold wallet an operational consolidation
 * credits — are configuration of <i>this</i> service, not values the caller
 * may choose. They used to arrive in the request body, sourced from the
 * <c>reconciliation.*_wallet_address</c> SystemSettings, which any holder of
 * <c>MANAGE_SETTINGS</c> could rewrite with no format check, no cooldown and
 * no second approval — the same defect family as
 * <c>Prova-SellerPayoutAddressBypassesCooldown</c> (#312): the protected value
 * was not the value the money followed. The request field is kept so a drifted
 * backend fails loudly here instead of silently sending elsewhere.
 * </para>
 *
 * <para>
 * <b>Amount limits.</b> A single transfer above
 * <c>MAX_SINGLE_TRANSFER_USDT</c> is refused (a decimal-shift bug cannot pass),
 * and a hot-wallet payout that would push the last 24 hours of hot-wallet
 * outflow above <c>MAX_DAILY_OUTFLOW_USDT</c> is refused (a compromised
 * backend cannot drain the wallet in many small steps). The 24-hour total is
 * read from the chain rather than kept in memory, so a sidecar restart cannot
 * reset it. Cold-wallet consolidation is exempt from both: its destination is
 * pinned, so the money cannot leave the platform that way.
 * </para>
 *
 * <para>
 * Every limit is <b>fail-closed</b>: an unset or malformed limit refuses the
 * transfer rather than allowing it. A limit that cannot be evaluated because
 * the chain history is unreadable is retryable; a limit that is genuinely
 * exceeded is not, so the dispatcher stops after the first attempt and the
 * existing FAILED alert reaches an operator.
 * </para>
 */
export interface OutflowHistoryRecord {
  transaction_id: string;
  from: string;
  to: string;
  value: string;
  block_timestamp: number;
  token_info: { address: string };
}

export interface OutflowHistoryPage {
  records: OutflowHistoryRecord[];
  fingerprint: string | null;
}

/** Narrow view of <c>TronGridClient.listTrc20</c> (keeps the guard testable). */
export interface OutflowHistoryReader {
  listTrc20(options: {
    address: string;
    contractAddress?: string;
    fingerprint?: string;
    limit?: number;
    onlyFrom?: boolean;
    minTimestamp?: number;
  }): Promise<OutflowHistoryPage>;
}

export interface TransferGuardDeps {
  /** Pinned sweep destination — the sidecar's own <c>HOT_WALLET_ADDRESS</c>. */
  hotWalletAddress: string;
  /** Pinned consolidation destination — <c>COLD_WALLET_ADDRESS</c>, '' when unset. */
  coldWalletAddress: string;
  /** Raw token units; <c>null</c> when unset or unparseable → fail closed. */
  maxSingleTransferUnits: bigint | null;
  /** Raw token units; <c>null</c> when unset or unparseable → fail closed. */
  maxDailyOutflowUnits: bigint | null;
  /** Allowlisted stablecoin contracts — outflow of anything else is not ours. */
  tokenContracts: TokenContractMap;
  history: OutflowHistoryReader;
  now?: () => number;
  /** Paging budget for the 24-hour window; 200 records per page. */
  maxHistoryPages?: number;
}

/** Rolling window the daily outflow limit is measured over. */
export const OUTFLOW_WINDOW_MS = 24 * 60 * 60 * 1000;
const HISTORY_PAGE_LIMIT = 200;
const DEFAULT_MAX_HISTORY_PAGES = 10;

interface InFlightOutflow {
  txHash: string;
  units: bigint;
  at: number;
}

/**
 * Parse a decimal limit from configuration into raw token units.
 * Returns <c>null</c> for an unset or malformed value — the caller treats
 * that as "limit not configured", which refuses transfers rather than
 * allowing unlimited ones.
 */
export function parseLimitUnits(raw: string | undefined, decimalsPower: bigint): bigint | null {
  const trimmed = (raw ?? '').trim();
  if (trimmed.length === 0) return null;
  if (!/^\d+(?:\.\d+)?$/.test(trimmed)) {
    logger.error(
      { value: trimmed },
      'Transfer limit is not a positive decimal — treating as unset (fail closed)',
    );
    return null;
  }
  const decimals = decimalsPower.toString().length - 1;
  const [whole, fraction = ''] = trimmed.split('.');
  if (fraction.length > decimals) {
    logger.error(
      { value: trimmed, decimals },
      'Transfer limit has more fraction digits than the token — treating as unset (fail closed)',
    );
    return null;
  }
  const units = BigInt(whole) * decimalsPower + BigInt(fraction.padEnd(decimals, '0') || '0');
  if (units <= 0n) {
    logger.error({ value: trimmed }, 'Transfer limit is zero — treating as unset (fail closed)');
    return null;
  }
  return units;
}

export class TransferGuard {
  private readonly hotWalletAddress: string;
  private readonly coldWalletAddress: string;
  private readonly maxSingleTransferUnits: bigint | null;
  private readonly maxDailyOutflowUnits: bigint | null;
  private readonly allowedContracts: Set<string>;
  private readonly history: OutflowHistoryReader;
  private readonly now: () => number;
  private readonly maxHistoryPages: number;
  private inFlight: InFlightOutflow[] = [];

  constructor(deps: TransferGuardDeps) {
    // Eager-fail on a malformed address, mirroring WalletManager's stance on a
    // malformed mnemonic: a signer that cannot trust its own pinned
    // destinations must not advertise itself as ready.
    assertConfiguredAddress(deps.hotWalletAddress, 'HOT_WALLET_ADDRESS');
    assertConfiguredAddress(deps.coldWalletAddress, 'COLD_WALLET_ADDRESS');

    this.hotWalletAddress = deps.hotWalletAddress;
    this.coldWalletAddress = deps.coldWalletAddress;
    this.maxSingleTransferUnits = deps.maxSingleTransferUnits;
    this.maxDailyOutflowUnits = deps.maxDailyOutflowUnits;
    this.allowedContracts = new Set(
      Object.values(deps.tokenContracts).filter((address): address is string => !!address),
    );
    this.history = deps.history;
    this.now = deps.now ?? (() => Date.now());
    this.maxHistoryPages = deps.maxHistoryPages ?? DEFAULT_MAX_HISTORY_PAGES;
  }

  /**
   * A sweep may only credit this sidecar's own hot wallet. The backend still
   * sends the address it recorded on the ledger row, so a mismatch means the
   * two have drifted (or the backend was tampered with) and the transfer is
   * refused instead of being redirected.
   */
  assertSweepDestination(requested: string): void {
    if (requested !== this.hotWalletAddress) {
      throw new SidecarError(
        `Sweep destination ${requested} is not the configured hot wallet — refusing to sign.`,
        'DESTINATION_NOT_ALLOWED',
        false,
      );
    }
  }

  /** Cold consolidation may only credit the configured cold wallet. */
  assertColdDestination(requested: string): void {
    if (!this.coldWalletAddress) {
      throw new SidecarError(
        'Cold wallet destination is not configured (COLD_WALLET_ADDRESS).',
        'COLD_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    if (requested !== this.coldWalletAddress) {
      throw new SidecarError(
        `Cold transfer destination ${requested} is not the configured cold wallet — refusing to sign.`,
        'DESTINATION_NOT_ALLOWED',
        false,
      );
    }
  }

  /** Applies to customer-facing transfers: payouts and refunds. */
  assertSingleTransferLimit(amountUnits: bigint): void {
    if (this.maxSingleTransferUnits === null) {
      throw new SidecarError(
        'Single-transfer limit is not configured (MAX_SINGLE_TRANSFER_USDT) — refusing to sign.',
        'TRANSFER_LIMIT_NOT_CONFIGURED',
        false,
      );
    }
    if (amountUnits > this.maxSingleTransferUnits) {
      throw new SidecarError(
        `Transfer amount ${amountUnits} exceeds the single-transfer limit ${this.maxSingleTransferUnits}.`,
        'TRANSFER_AMOUNT_ABOVE_LIMIT',
        false,
      );
    }
  }

  /**
   * Applies to hot-wallet outflow only (payouts). The already-spent total is
   * the chain's own answer for the last 24 hours plus anything this process
   * broadcast that the history has not caught up with yet — a restart drops
   * only the second half, and the chain still carries the first.
   */
  async assertHotWalletDailyLimit(amountUnits: bigint): Promise<void> {
    if (this.maxDailyOutflowUnits === null) {
      throw new SidecarError(
        'Daily outflow limit is not configured (MAX_DAILY_OUTFLOW_USDT) — refusing to sign.',
        'TRANSFER_LIMIT_NOT_CONFIGURED',
        false,
      );
    }
    const since = this.now() - OUTFLOW_WINDOW_MS;
    const { total, seen } = await this.readChainOutflow(since);
    const pending = this.pendingOutflow(since, seen);
    const projected = total + pending + amountUnits;
    if (projected > this.maxDailyOutflowUnits) {
      throw new SidecarError(
        `Hot wallet 24h outflow would reach ${projected} (chain ${total} + in-flight ${pending} + ${amountUnits}), ` +
          `above the configured limit ${this.maxDailyOutflowUnits}.`,
        'DAILY_OUTFLOW_LIMIT_EXCEEDED',
        false,
      );
    }
  }

  /** Called after a hot-wallet payout broadcast lands a tx hash. */
  recordHotWalletOutflow(txHash: string, amountUnits: bigint): void {
    const at = this.now();
    this.inFlight = this.inFlight.filter((entry) => entry.at >= at - OUTFLOW_WINDOW_MS);
    this.inFlight.push({ txHash, units: amountUnits, at });
  }

  private pendingOutflow(since: number, seen: Set<string>): bigint {
    let pending = 0n;
    for (const entry of this.inFlight) {
      if (entry.at < since) continue;
      if (seen.has(entry.txHash)) continue;
      pending += entry.units;
    }
    return pending;
  }

  private async readChainOutflow(since: number): Promise<{ total: bigint; seen: Set<string> }> {
    let total = 0n;
    const seen = new Set<string>();
    let fingerprint: string | undefined;
    let pages = 0;

    for (;;) {
      let page: OutflowHistoryPage;
      try {
        page = await this.history.listTrc20({
          address: this.hotWalletAddress,
          onlyFrom: true,
          minTimestamp: since,
          limit: HISTORY_PAGE_LIMIT,
          fingerprint,
        });
      } catch (err) {
        throw new SidecarError(
          `Hot wallet outflow history is unreadable — cannot evaluate the daily limit: ${
            (err as Error).message
          }`,
          'OUTFLOW_HISTORY_UNAVAILABLE',
          true,
        );
      }

      for (const record of page.records) {
        if (record.block_timestamp < since) continue;
        if (record.from !== this.hotWalletAddress) continue;
        if (!this.allowedContracts.has(record.token_info?.address)) continue;
        // Consolidation to the pinned cold wallet is platform-internal
        // movement, not outflow (owner decision 2026-09-17).
        if (this.coldWalletAddress && record.to === this.coldWalletAddress) continue;
        if (seen.has(record.transaction_id)) continue;
        seen.add(record.transaction_id);
        total += BigInt(record.value);
      }

      pages++;
      fingerprint = page.fingerprint ?? undefined;
      if (!fingerprint || page.records.length < HISTORY_PAGE_LIMIT) break;
      if (pages >= this.maxHistoryPages) {
        // Retrying cannot shorten the history; an operator has to widen the
        // budget or lower the window. Refusing is the fail-closed direction.
        throw new SidecarError(
          `Hot wallet outflow history exceeded ${this.maxHistoryPages} pages — daily limit cannot be evaluated.`,
          'OUTFLOW_HISTORY_INCOMPLETE',
          false,
        );
      }
    }

    return { total, seen };
  }
}

function assertConfiguredAddress(address: string, envName: string): void {
  if (!address) return;
  if (!TronWeb.isAddress(address)) {
    throw new Error(`${envName} is not a valid Tron address: ${address}`);
  }
}
