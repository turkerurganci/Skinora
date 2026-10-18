import crypto from 'node:crypto';
import { canonicalAddress } from '../address/canonicalAddress.js';
import { logger } from '../logger.js';
import { watchedDeposits } from '../metrics.js';
import type { StablecoinSymbol } from './tokenRules.js';
import {
  PostCancelMonitorStates,
  type PostCancelMonitorState,
  type PostCancelMonitorStateChangedData,
} from '../webhook/WebhookPayloads.js';

/**
 * The deposits the Ethereum service watches (T151 — 08 §3.4 "Aktif izlemenin
 * yaşam döngüsü", §3.7.4). Same contract as the TRON service's two
 * registries — `start`/`stop` for the active payment window,
 * `post-cancel-start`/`post-cancel-stop` for the 30-day late-payment window —
 * with one difference that comes from the chain: there is no per-address
 * polling. One `eth_getLogs` per block range reads every watched deposit at
 * once (PaymentScanner), so the cadence the backend sends (PAYMENT / HOLDING)
 * is accepted and echoed but changes nothing, and the post-cancel states only
 * drive the state-change webhooks and the 30-day stop.
 *
 * <para>
 * The registry is memory: the backend's database is the source of truth and
 * re-arms every open address every minute (`EnsurePaymentMonitorJob`), which
 * is how a restart is recovered. What the scanner needs to recover without
 * gaps — from which block an address must be (re)read — is decided when it
 * picks the entry up ({@link WatchEntry.scannedThrough} `null`).
 * </para>
 */

export type MonitorCadence = 'PAYMENT' | 'HOLDING';
export type WatchKind = 'active' | 'post_cancel';

export interface Flow {
  readonly key: string;
  readonly block: bigint;
  readonly symbol: StablecoinSymbol;
  /** Signed base units: incoming positive, outgoing negative. */
  readonly delta: bigint;
}

export interface Baseline {
  readonly block: bigint;
  readonly eth: bigint;
  readonly tokens: Partial<Record<StablecoinSymbol, bigint>>;
}

/** Scan and reconciliation state of one address — carried over when the address changes registry. */
export interface ScanState {
  /** Highest block whose logs for this address are processed; null = not picked up yet. */
  scannedThrough: bigint | null;
  /** A fresh deposit: its whole history is inside the scanned range (balance known to start at 0). */
  assumeEmptyStart: boolean;
  /** `${txHash}:${logIndex}` delivered to the backend. */
  readonly reported: Set<string>;
  /** Keys of the transfers already counted in {@link flows}. */
  readonly flowKeys: Set<string>;
  flows: Flow[];
  baseline: Baseline | null;
}

export interface WatchEntry {
  readonly key: string;
  readonly address: string;
  readonly kind: WatchKind;
  readonly paymentAddressId: string;
  readonly transactionId: string;
  /** Canonical expected contract. */
  readonly expectedContract: string;
  readonly expectedSymbol: StablecoinSymbol;
  readonly correlationId: string;
  /** Epoch ms the entry was registered. */
  readonly armedAt: number;
  /**
   * T158 (h) — the block the backend persisted for this deposit
   * (`PaymentAddress.MonitorFromBlock`: the finalized head when the address
   * was derived). Nothing can have reached the address at or before it, so
   * the deposit is read from the next block on with a zero balance baseline,
   * however long the service was down. Null for a deposit allocated before
   * T158 (the restart read-back rules apply).
   */
  readonly watchFromBlock: bigint | null;
  cadence: MonitorCadence;
  /** Post-cancel only. */
  postCancel?: {
    state: PostCancelMonitorState;
    stateExpiresAt: Date | null;
    readonly cancelledAt: Date;
  };
  readonly scan: ScanState;
}

export interface ActiveStartOptions {
  address: string;
  paymentAddressId: string;
  transactionId: string;
  expectedContract: string;
  expectedSymbol: StablecoinSymbol;
  cadence?: MonitorCadence;
  /** T158 (h) — see {@link WatchEntry.watchFromBlock}. */
  watchFromBlock?: bigint | null;
}

export interface PostCancelStartOptions extends Omit<ActiveStartOptions, 'cadence'> {
  cancelledAt: Date;
  initialState?: PostCancelMonitorState;
  initialStateExpiresAt?: Date | null;
}

/** Elapsed-since-cancel cutoffs of the post-cancel states (05 §3.3, 08 §3.4). */
export const POST_CANCEL_WINDOWS_MS = {
  POST_CANCEL_24H: 24 * 3600_000,
  POST_CANCEL_7D: 7 * 24 * 3600_000,
  POST_CANCEL_30D: 30 * 24 * 3600_000,
} as const;

/** How long a stopped address's scan state is kept for a hand-over (active → post-cancel). */
export const RETIRED_KEEP_MS = 10 * 60_000;

export type StateChangeSender = (
  data: PostCancelMonitorStateChangedData,
  correlationId: string,
) => Promise<void>;

export interface DepositRegistryDeps {
  clock?: () => Date;
}

function newScanState(): ScanState {
  return {
    scannedThrough: null,
    assumeEmptyStart: false,
    reported: new Set(),
    flowKeys: new Set(),
    flows: [],
    baseline: null,
  };
}

export class DepositRegistry {
  private readonly active = new Map<string, WatchEntry>();
  private readonly postCancel = new Map<string, WatchEntry>();
  private readonly retired = new Map<string, { scan: ScanState; until: number }>();
  private readonly clock: () => Date;

  constructor(deps: DepositRegistryDeps = {}) {
    this.clock = deps.clock ?? (() => new Date());
  }

  /** `POST /api/monitor/start`. Idempotent; a re-start only updates the cadence. */
  startActive(options: ActiveStartOptions): { started: boolean; cadence: MonitorCadence } {
    const key = canonicalAddress(options.address);
    const cadence = options.cadence ?? 'PAYMENT';
    const existing = this.active.get(key);
    if (existing) {
      existing.cadence = cadence;
      return { started: false, cadence };
    }
    const entry = this.newEntry('active', options, cadence, this.takeOver(key));
    this.active.set(key, entry);
    this.report();
    logger.info(
      { address: options.address, transactionId: options.transactionId, cadence },
      'Ethereum monitor started',
    );
    return { started: true, cadence };
  }

  /** `POST /api/monitor/stop`. */
  stopActive(address: string): { stopped: boolean } {
    return this.remove(this.active, address);
  }

  /** `POST /api/monitor/post-cancel-start`. Idempotent on the address. */
  startPostCancel(options: PostCancelStartOptions): {
    started: boolean;
    state: PostCancelMonitorState;
  } {
    const key = canonicalAddress(options.address);
    const existing = this.postCancel.get(key);
    if (existing?.postCancel) return { started: false, state: existing.postCancel.state };

    const now = this.clock();
    const state = options.initialState ?? stateFromElapsed(now, options.cancelledAt);
    if (state === PostCancelMonitorStates.Stopped) {
      logger.warn(
        { address: options.address, transactionId: options.transactionId },
        'Post-cancel start refused — already past the 30-day window',
      );
      return { started: false, state };
    }
    // A hand-over from the active window keeps what the active entry has
    // already read: the backend stops the active monitor first (08 §3.4
    // "Durdurma — devir"), so its state is in `retired`; a drifted backend
    // that starts post-cancel while active is still held hands it over too.
    const inherited = this.active.get(key)?.scan ?? this.takeOver(key);
    const entry = this.newEntry('post_cancel', options, 'HOLDING', inherited);
    entry.postCancel = {
      state,
      stateExpiresAt: options.initialStateExpiresAt ?? stateExpiresAt(state, options.cancelledAt),
      cancelledAt: options.cancelledAt,
    };
    this.postCancel.set(key, entry);
    this.report();
    logger.info(
      { address: options.address, transactionId: options.transactionId, state },
      'Ethereum post-cancel monitor started',
    );
    return { started: true, state };
  }

  /** `POST /api/monitor/post-cancel-stop`. */
  stopPostCancel(address: string): { stopped: boolean } {
    return this.remove(this.postCancel, address);
  }

  /**
   * Every watched address once. A post-cancel entry wins over an active one
   * for the same address (the transaction is cancelled: a transfer there is a
   * late payment, 08 §3.4) — the backend stops the active entry before it
   * starts the post-cancel one, so both at once only happens on drift.
   */
  entries(): WatchEntry[] {
    const byKey = new Map<string, WatchEntry>(this.active);
    for (const [key, entry] of this.postCancel) byKey.set(key, entry);
    return [...byKey.values()];
  }

  size(): number {
    return this.entries().length;
  }

  /**
   * Advances post-cancel states whose window has ended (24h → 7d → 30d →
   * STOPPED) and tells the backend. A failed webhook leaves the state as it
   * was so the next run sends it again.
   */
  async advancePostCancelStates(send: StateChangeSender): Promise<void> {
    const now = this.clock();
    for (const entry of [...this.postCancel.values()]) {
      const pc = entry.postCancel;
      if (!pc) continue;
      while (pc.stateExpiresAt !== null && now.getTime() >= pc.stateExpiresAt.getTime()) {
        const previous = pc.state;
        const next = nextState(previous);
        const nextExpires = next === 'STOPPED' ? null : stateExpiresAt(next, pc.cancelledAt);
        try {
          await send(
            {
              paymentAddressId: entry.paymentAddressId,
              transactionId: entry.transactionId,
              address: entry.address,
              previousState: previous,
              newState: next,
              newStateExpiresAt: nextExpires?.toISOString() ?? null,
              cancelledAt: pc.cancelledAt.toISOString(),
              changedAt: now.toISOString(),
            },
            entry.correlationId,
          );
        } catch (err) {
          logger.warn(
            { address: entry.address, previous, next, reason: (err as Error).message },
            'Post-cancel state change not delivered — will retry',
          );
          break;
        }
        pc.state = next;
        pc.stateExpiresAt = nextExpires;
        if (next === 'STOPPED') {
          this.postCancel.delete(entry.key);
          this.report();
          logger.warn(
            { address: entry.address, transactionId: entry.transactionId },
            'Post-cancel window ended (30 days) — address no longer watched',
          );
          break;
        }
      }
    }
  }

  private newEntry(
    kind: WatchKind,
    options: ActiveStartOptions,
    cadence: MonitorCadence,
    inherited: ScanState | undefined,
  ): WatchEntry {
    return {
      key: canonicalAddress(options.address),
      address: options.address,
      kind,
      paymentAddressId: options.paymentAddressId,
      transactionId: options.transactionId,
      expectedContract: canonicalAddress(options.expectedContract),
      expectedSymbol: options.expectedSymbol,
      correlationId: crypto.randomUUID(),
      armedAt: this.clock().getTime(),
      watchFromBlock: options.watchFromBlock ?? null,
      cadence,
      scan: inherited ?? newScanState(),
    };
  }

  private takeOver(key: string): ScanState | undefined {
    const now = this.clock().getTime();
    for (const [k, r] of this.retired) if (r.until < now) this.retired.delete(k);
    const kept = this.retired.get(key);
    this.retired.delete(key);
    return kept?.scan;
  }

  private remove(map: Map<string, WatchEntry>, address: string): { stopped: boolean } {
    const key = canonicalAddress(address);
    const entry = map.get(key);
    if (!entry) return { stopped: false };
    map.delete(key);
    if (entry.scan.scannedThrough !== null) {
      this.retired.set(key, { scan: entry.scan, until: this.clock().getTime() + RETIRED_KEEP_MS });
    }
    this.report();
    logger.info({ address, kind: entry.kind }, 'Ethereum monitor stopped');
    return { stopped: true };
  }

  private report(): void {
    watchedDeposits.set({ kind: 'active' }, this.active.size);
    watchedDeposits.set({ kind: 'post_cancel' }, this.postCancel.size);
  }
}

function stateFromElapsed(now: Date, cancelledAt: Date): PostCancelMonitorState {
  const elapsed = now.getTime() - cancelledAt.getTime();
  if (elapsed < POST_CANCEL_WINDOWS_MS.POST_CANCEL_24H) return 'POST_CANCEL_24H';
  if (elapsed < POST_CANCEL_WINDOWS_MS.POST_CANCEL_7D) return 'POST_CANCEL_7D';
  if (elapsed < POST_CANCEL_WINDOWS_MS.POST_CANCEL_30D) return 'POST_CANCEL_30D';
  return 'STOPPED';
}

function nextState(state: PostCancelMonitorState): PostCancelMonitorState {
  switch (state) {
    case 'POST_CANCEL_24H':
      return 'POST_CANCEL_7D';
    case 'POST_CANCEL_7D':
      return 'POST_CANCEL_30D';
    default:
      return 'STOPPED';
  }
}

function stateExpiresAt(state: PostCancelMonitorState, cancelledAt: Date): Date | null {
  if (state === 'STOPPED') return null;
  return new Date(cancelledAt.getTime() + POST_CANCEL_WINDOWS_MS[state]);
}
