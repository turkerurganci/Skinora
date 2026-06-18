import crypto from 'crypto';
import { logger } from '../logger.js';
import { activeMonitors, transfersTotal } from '../metrics.js';
import type { Trc20Record, TransferLogEntry, TronGridClient } from '../tron/TronGridClient.js';
import { sendCallback, WebhookDeliveryError } from '../webhook/WebhookClient.js';
import type {
  AnyBlockchainWebhookPayload,
  BlockchainWebhookEvent,
  BlockchainWebhookEnvelope,
  LatePaymentDetectedData,
  PostCancelMonitorState,
  PostCancelMonitorStateChangedData,
  SpamTokenIncomingData,
  WrongTokenIncomingData,
} from '../webhook/WebhookPayloads.js';
import { BlockchainWebhookEvents, PostCancelMonitorStates } from '../webhook/WebhookPayloads.js';
import {
  classifyToken,
  formatTokenAmount,
  isIncomingFor,
  isTransferRecord,
  type StablecoinAllowlist,
  type StablecoinSymbol,
} from './PaymentMonitorRules.js';

export interface PostCancelMonitorStartOptions {
  address: string;
  paymentAddressId: string;
  transactionId: string;
  expectedContract: string;
  expectedSymbol: StablecoinSymbol;
  /** Wall-clock moment the transaction was cancelled. Anchors the
   * 24h/7d/30d windows so a sidecar restart resumes at the same boundary. */
  cancelledAt: Date;
  /** Recovery override — backend re-registers with the state observed in DB. */
  initialState?: PostCancelMonitorState;
  /** Recovery override — explicit window end. When omitted the state's
   * default window (anchored at <c>cancelledAt</c>) is used. */
  initialStateExpiresAt?: Date | null;
}

export interface PostCancelMonitorRegistryDeps {
  client: TronGridClient;
  allowlist: StablecoinAllowlist;
  /** Shared timer interval. Defaults to 30 s (08 §3.4 POST_CANCEL_24H
   * cadence). Acts as the polling-eligibility tick — slower cadences are
   * enforced by per-entry <c>nextPollAt</c>. */
  tickIntervalMs?: number;
  pageLimit: number;
  webhookEndpoints: {
    latePaymentDetected: string;
    postCancelMonitorStateChanged: string;
    wrongTokenIncoming: string;
    spamTokenIncoming: string;
  };
  cadences?: PostCancelCadences;
  windows?: PostCancelWindows;
  clock?: () => Date;
  webhookSender?: typeof sendCallback;
}

export interface PostCancelCadences {
  /** Poll interval (ms) while inside POST_CANCEL_24H. Default 30 s. */
  POST_CANCEL_24H: number;
  /** Poll interval (ms) while inside POST_CANCEL_7D. Default 5 min. */
  POST_CANCEL_7D: number;
  /** Poll interval (ms) while inside POST_CANCEL_30D. Default 1 h. */
  POST_CANCEL_30D: number;
}

export interface PostCancelWindows {
  /** Elapsed-since-cancel cutoff for POST_CANCEL_24H. Default 24 h. */
  POST_CANCEL_24H: number;
  /** Elapsed-since-cancel cutoff for POST_CANCEL_7D. Default 7 d (total). */
  POST_CANCEL_7D: number;
  /** Elapsed-since-cancel cutoff for POST_CANCEL_30D. Default 30 d (total). */
  POST_CANCEL_30D: number;
}

export const DEFAULT_POST_CANCEL_CADENCES: PostCancelCadences = {
  POST_CANCEL_24H: 30 * 1000,
  POST_CANCEL_7D: 5 * 60 * 1000,
  POST_CANCEL_30D: 60 * 60 * 1000,
};

export const DEFAULT_POST_CANCEL_WINDOWS: PostCancelWindows = {
  POST_CANCEL_24H: 24 * 60 * 60 * 1000,
  POST_CANCEL_7D: 7 * 24 * 60 * 60 * 1000,
  POST_CANCEL_30D: 30 * 24 * 60 * 60 * 1000,
};

interface PostCancelMonitorEntry {
  options: PostCancelMonitorStartOptions;
  correlationId: string;
  state: PostCancelMonitorState;
  /** Wall-clock at which the current state's window ends. Null only for
   * <c>STOPPED</c>, but a <c>STOPPED</c> entry is removed from the registry
   * immediately, so this value is non-null for any entry actually held. */
  stateExpiresAt: Date | null;
  nextPollAt: Date;
  phase1Fingerprint?: string;
  phase2Fingerprint?: string;
  /** `${txHash}:${eventIndex}` keys already emitted — per-event dedup (08 §3.4, WP10). */
  seenEvents: Set<string>;
}

/** Composite dedup key (08 §3.4 — WP10). */
function eventKey(txHash: string, eventIndex: number): string {
  return `${txHash}:${eventIndex}`;
}

/**
 * Post-cancel deposit-address monitor (T75 — 02 §4.4, 05 §3.3, 08 §3.4).
 *
 * <para>
 * When a transaction is cancelled (timeout, user-cancel, admin-cancel) the
 * platform keeps watching its deposit address for late buyer transfers and
 * refunds anything that arrives back to the buyer (net of gas fee). Polling
 * cadence degrades with elapsed time:
 *
 * <list type="bullet">
 *   <item>0–24h since cancel → POST_CANCEL_24H, poll every 30 s</item>
 *   <item>24h–7d → POST_CANCEL_7D, poll every 5 min</item>
 *   <item>7d–30d → POST_CANCEL_30D, poll every 1 h</item>
 *   <item>30d+ → STOPPED, drop the monitor + admin alert</item>
 * </list>
 * </para>
 *
 * <para>
 * State transitions are <b>sidecar-clocked</b> (decision A in the T75 scope
 * review). On every tick a transition check happens before polling — when
 * the window expires the entry advances and a
 * <c>PostCancelMonitorStateChanged</c> webhook informs the backend so
 * <c>PaymentAddress.MonitoringStatus</c> stays in sync. The backend is the
 * persistent recovery source: on sidecar restart it re-registers every
 * <c>POST_CANCEL_*</c> address with the state and window endpoint observed
 * in DB.
 * </para>
 *
 * <para>
 * One shared <c>setInterval(tickIntervalMs)</c> drives every entry. The
 * default 30 s tick matches POST_CANCEL_24H exactly; slower cadences are
 * enforced by per-entry <c>nextPollAt</c>, so a POST_CANCEL_7D entry only
 * triggers a TronGrid call every 10th tick (5 min / 30 s). Phase 1 and 2
 * fingerprint pagination mirror T71 to keep idempotency and spam handling
 * identical to the active path.
 * </para>
 */
export class PostCancelMonitorRegistry {
  private readonly monitors = new Map<string, PostCancelMonitorEntry>();
  private timer?: NodeJS.Timeout;
  private polling = false;
  private stopped = false;
  private readonly clock: () => Date;
  private readonly webhookSender: typeof sendCallback;
  private readonly cadences: PostCancelCadences;
  private readonly windows: PostCancelWindows;
  private readonly tickIntervalMs: number;

  constructor(private readonly deps: PostCancelMonitorRegistryDeps) {
    this.clock = deps.clock ?? (() => new Date());
    this.webhookSender = deps.webhookSender ?? sendCallback;
    this.cadences = deps.cadences ?? DEFAULT_POST_CANCEL_CADENCES;
    this.windows = deps.windows ?? DEFAULT_POST_CANCEL_WINDOWS;
    this.tickIntervalMs = deps.tickIntervalMs ?? this.cadences.POST_CANCEL_24H;
  }

  size(): number {
    return this.monitors.size;
  }

  /**
   * Register a deposit address for post-cancel monitoring. Idempotent on
   * <c>address</c> — restart returns <c>started=false</c> without touching
   * cursor/dedup state. Both fresh starts (from the cancel-time stamper) and
   * recovery starts (with <c>initialState</c>) use this entry point.
   */
  start(options: PostCancelMonitorStartOptions): {
    started: boolean;
    state: PostCancelMonitorState | null;
  } {
    if (this.stopped) {
      throw new Error('PostCancelMonitorRegistry has been shut down');
    }
    if (this.monitors.has(options.address)) {
      const existing = this.monitors.get(options.address)!;
      logger.info(
        {
          address: options.address,
          transactionId: options.transactionId,
          state: existing.state,
        },
        'Post-cancel monitor already active for address — no-op restart',
      );
      return { started: false, state: existing.state };
    }

    const now = this.clock();
    const initialState =
      options.initialState ?? this.deriveStateFromElapsed(now, options.cancelledAt);

    if (initialState === PostCancelMonitorStates.Stopped) {
      logger.warn(
        {
          address: options.address,
          transactionId: options.transactionId,
          cancelledAt: options.cancelledAt.toISOString(),
        },
        'Post-cancel start refused — wall-clock already past 30-day window',
      );
      return { started: false, state: PostCancelMonitorStates.Stopped };
    }

    const stateExpiresAt =
      options.initialStateExpiresAt ??
      this.computeStateExpiresAt(initialState, options.cancelledAt);

    const entry: PostCancelMonitorEntry = {
      options,
      correlationId: crypto.randomUUID(),
      state: initialState,
      stateExpiresAt,
      nextPollAt: now,
      seenEvents: new Set(),
    };
    this.monitors.set(options.address, entry);
    activeMonitors.set(this.monitors.size);
    logger.info(
      {
        address: options.address,
        transactionId: options.transactionId,
        state: entry.state,
        stateExpiresAt: entry.stateExpiresAt?.toISOString(),
      },
      'Post-cancel monitor started',
    );
    this.ensureTimer();
    return { started: true, state: entry.state };
  }

  /**
   * Stop monitoring the given address. Returns <c>stopped=true</c> if an
   * entry existed (admin manual stop or successful late refund); the caller
   * does not need to know either way — both are idempotent on the backend.
   */
  stop(address: string): { stopped: boolean } {
    const had = this.monitors.delete(address);
    activeMonitors.set(this.monitors.size);
    if (had) {
      logger.info({ address }, 'Post-cancel monitor stopped');
    }
    if (this.monitors.size === 0) {
      this.clearTimer();
    }
    return { stopped: had };
  }

  async shutdown(): Promise<void> {
    this.stopped = true;
    this.clearTimer();
    this.monitors.clear();
    activeMonitors.set(0);
  }

  /** Public for tests — runs a single tick deterministically. */
  async tick(): Promise<void> {
    if (this.polling || this.stopped) return;
    this.polling = true;
    try {
      const entries = [...this.monitors.values()];
      for (const entry of entries) {
        await this.processEntry(entry);
      }
    } finally {
      this.polling = false;
    }
  }

  private async processEntry(entry: PostCancelMonitorEntry): Promise<void> {
    try {
      // Transition first — a window expiry must change cadence before any
      // polling decision based on the new state.
      await this.maybeAdvanceState(entry);
      if (!this.monitors.has(entry.options.address)) {
        // Stopped during transition (30d → STOPPED).
        return;
      }
      const now = this.clock();
      if (now.getTime() < entry.nextPollAt.getTime()) return;
      // Per-tick cache of resolved transfer-log entries keyed by
      // `${txHash}:${contract}` (08 §3.4 — WP10), shared across both phases.
      const logCache = new Map<string, TransferLogEntry[]>();
      await this.pollPhase1(entry, logCache);
      await this.pollPhase2(entry, logCache);
      entry.nextPollAt = new Date(now.getTime() + this.cadenceFor(entry.state));
    } catch (err) {
      logger.error(
        {
          err: (err as Error).message,
          address: entry.options.address,
          correlationId: entry.correlationId,
        },
        'Post-cancel tick failed — will retry next interval',
      );
    }
  }

  private ensureTimer(): void {
    if (this.timer || this.stopped) return;
    this.timer = setInterval(() => {
      void this.tick().catch((err) => {
        logger.error({ err: (err as Error).message }, 'Post-cancel timer tick crashed');
      });
    }, this.tickIntervalMs);
    this.timer.unref?.();
  }

  private clearTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
  }

  private async maybeAdvanceState(entry: PostCancelMonitorEntry): Promise<void> {
    const now = this.clock();
    while (
      entry.state !== PostCancelMonitorStates.Stopped &&
      entry.stateExpiresAt !== null &&
      now.getTime() >= entry.stateExpiresAt.getTime()
    ) {
      const previousState = entry.state;
      const nextState = this.nextState(entry.state);
      const nextExpiresAt =
        nextState === PostCancelMonitorStates.Stopped
          ? null
          : this.computeStateExpiresAt(nextState, entry.options.cancelledAt);

      entry.state = nextState;
      entry.stateExpiresAt = nextExpiresAt;

      await this.emitStateChanged(entry, previousState, nextState, nextExpiresAt);

      if (nextState === PostCancelMonitorStates.Stopped) {
        // Drop the entry — no further polling after 30-day terminal.
        this.monitors.delete(entry.options.address);
        activeMonitors.set(this.monitors.size);
        if (this.monitors.size === 0) {
          this.clearTimer();
        }
        return;
      }
    }
  }

  private cadenceFor(state: PostCancelMonitorState): number {
    // STOPPED entries are removed from <c>monitors</c> before this is reached;
    // guard with the 24h cadence as a defensive fallback so a buggy caller
    // does not pin <c>nextPollAt</c> to NaN.
    switch (state) {
      case PostCancelMonitorStates.PostCancel24h:
        return this.cadences.POST_CANCEL_24H;
      case PostCancelMonitorStates.PostCancel7d:
        return this.cadences.POST_CANCEL_7D;
      case PostCancelMonitorStates.PostCancel30d:
        return this.cadences.POST_CANCEL_30D;
      default:
        return this.cadences.POST_CANCEL_24H;
    }
  }

  private nextState(state: PostCancelMonitorState): PostCancelMonitorState {
    switch (state) {
      case PostCancelMonitorStates.PostCancel24h:
        return PostCancelMonitorStates.PostCancel7d;
      case PostCancelMonitorStates.PostCancel7d:
        return PostCancelMonitorStates.PostCancel30d;
      case PostCancelMonitorStates.PostCancel30d:
        return PostCancelMonitorStates.Stopped;
      default:
        return PostCancelMonitorStates.Stopped;
    }
  }

  private computeStateExpiresAt(state: PostCancelMonitorState, cancelledAt: Date): Date | null {
    switch (state) {
      case PostCancelMonitorStates.PostCancel24h:
        return new Date(cancelledAt.getTime() + this.windows.POST_CANCEL_24H);
      case PostCancelMonitorStates.PostCancel7d:
        return new Date(cancelledAt.getTime() + this.windows.POST_CANCEL_7D);
      case PostCancelMonitorStates.PostCancel30d:
        return new Date(cancelledAt.getTime() + this.windows.POST_CANCEL_30D);
      default:
        return null;
    }
  }

  private deriveStateFromElapsed(now: Date, cancelledAt: Date): PostCancelMonitorState {
    const elapsed = now.getTime() - cancelledAt.getTime();
    if (elapsed < this.windows.POST_CANCEL_24H) return PostCancelMonitorStates.PostCancel24h;
    if (elapsed < this.windows.POST_CANCEL_7D) return PostCancelMonitorStates.PostCancel7d;
    if (elapsed < this.windows.POST_CANCEL_30D) return PostCancelMonitorStates.PostCancel30d;
    return PostCancelMonitorStates.Stopped;
  }

  private async pollPhase1(
    entry: PostCancelMonitorEntry,
    logCache: Map<string, TransferLogEntry[]>,
  ): Promise<void> {
    const response = await this.deps.client.listTrc20({
      address: entry.options.address,
      contractAddress: entry.options.expectedContract,
      fingerprint: entry.phase1Fingerprint,
      limit: this.deps.pageLimit,
    });
    if (response.fingerprint) {
      entry.phase1Fingerprint = response.fingerprint;
    }
    for (const record of response.records) {
      if (!this.shouldEmit(record, entry)) continue;
      if (record.token_info.address !== entry.options.expectedContract) {
        logger.debug(
          { txHash: record.transaction_id, contract: record.token_info.address },
          'Post-cancel phase 1 returned a non-expected contract row — skipping',
        );
        continue;
      }
      const eventIndex = await this.resolveEventIndex(entry, record, logCache);
      const key = eventKey(record.transaction_id, eventIndex);
      if (entry.seenEvents.has(key)) continue;
      await this.emitLatePaymentDetected(entry, record, eventIndex);
      entry.seenEvents.add(key);
    }
  }

  private async pollPhase2(
    entry: PostCancelMonitorEntry,
    logCache: Map<string, TransferLogEntry[]>,
  ): Promise<void> {
    const response = await this.deps.client.listTrc20({
      address: entry.options.address,
      fingerprint: entry.phase2Fingerprint,
      limit: this.deps.pageLimit,
    });
    if (response.fingerprint) {
      entry.phase2Fingerprint = response.fingerprint;
    }
    for (const record of response.records) {
      if (!this.shouldEmit(record, entry)) continue;
      const classification = classifyToken({
        contractAddress: record.token_info.address,
        expectedContract: entry.options.expectedContract,
        allowlist: this.deps.allowlist,
      });
      const eventIndex = await this.resolveEventIndex(entry, record, logCache);
      const key = eventKey(record.transaction_id, eventIndex);
      if (entry.seenEvents.has(key)) continue;
      if (classification.kind === 'expected') {
        // Late catch — phase 1's cursor passed this row. Treat as detected.
        await this.emitLatePaymentDetected(entry, record, eventIndex);
      } else if (classification.kind === 'wrong_token') {
        await this.emitWrongTokenIncoming(entry, record, classification.symbol, eventIndex);
      } else {
        await this.emitSpamTokenIncoming(entry, record, eventIndex);
      }
      entry.seenEvents.add(key);
    }
  }

  /**
   * Resolve the on-chain log index for a record (08 §3.4 — WP10), correlating
   * by transfer value and falling back to index 0 when logs are unavailable.
   * Mirrors <c>MonitorRegistry.resolveEventIndex</c>.
   */
  private async resolveEventIndex(
    entry: PostCancelMonitorEntry,
    record: Trc20Record,
    logCache: Map<string, TransferLogEntry[]>,
  ): Promise<number> {
    const cacheKey = `${record.transaction_id}:${record.token_info.address}`;
    let entries = logCache.get(cacheKey);
    if (entries === undefined) {
      entries = await this.deps.client.resolveTransferEventIndices(
        record.transaction_id,
        record.token_info.address,
        entry.options.address,
      );
      logCache.set(cacheKey, entries);
    }
    for (const logEntry of entries) {
      if (logEntry.value !== record.value) continue;
      if (entry.seenEvents.has(eventKey(record.transaction_id, logEntry.index))) continue;
      return logEntry.index;
    }
    return 0;
  }

  private shouldEmit(record: Trc20Record, entry: PostCancelMonitorEntry): boolean {
    if (!isTransferRecord(record.type)) {
      logger.debug(
        { type: record.type, txHash: record.transaction_id },
        'Skipping non-Transfer record in post-cancel sweep',
      );
      return false;
    }
    if (!isIncomingFor(record, entry.options.address)) return false;
    // Per-event dedup happens after the event index is resolved (08 §3.4).
    return true;
  }

  private async emitLatePaymentDetected(
    entry: PostCancelMonitorEntry,
    record: Trc20Record,
    eventIndex: number,
  ): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: LatePaymentDetectedData = {
      paymentAddressId: entry.options.paymentAddressId,
      transactionId: entry.options.transactionId,
      txHash: record.transaction_id,
      eventIndex,
      fromAddress: record.from,
      toAddress: record.to,
      contractAddress: record.token_info.address,
      tokenSymbol: entry.options.expectedSymbol,
      amount: formatTokenAmount(record.value, decimals),
      blockTimestampMs: record.block_timestamp,
      detectedAt: this.clock().toISOString(),
      monitorState: entry.state,
    };
    await this.deliver({
      endpoint: this.deps.webhookEndpoints.latePaymentDetected,
      event: BlockchainWebhookEvents.LatePaymentDetected,
      data,
      entry,
    });
    transfersTotal.inc({ type: 'LATE_PAYMENT_INCOMING', status: 'DETECTED' });
  }

  private async emitWrongTokenIncoming(
    entry: PostCancelMonitorEntry,
    record: Trc20Record,
    actualSymbol: StablecoinSymbol,
    eventIndex: number,
  ): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: WrongTokenIncomingData = {
      paymentAddressId: entry.options.paymentAddressId,
      transactionId: entry.options.transactionId,
      txHash: record.transaction_id,
      eventIndex,
      fromAddress: record.from,
      toAddress: record.to,
      expectedContractAddress: entry.options.expectedContract,
      actualContractAddress: record.token_info.address,
      actualTokenSymbol: actualSymbol,
      amount: formatTokenAmount(record.value, decimals),
      blockTimestampMs: record.block_timestamp,
      detectedAt: this.clock().toISOString(),
    };
    await this.deliver({
      endpoint: this.deps.webhookEndpoints.wrongTokenIncoming,
      event: BlockchainWebhookEvents.WrongTokenIncoming,
      data,
      entry,
    });
    transfersTotal.inc({ type: 'WRONG_TOKEN_INCOMING', status: 'DETECTED' });
  }

  private async emitSpamTokenIncoming(
    entry: PostCancelMonitorEntry,
    record: Trc20Record,
    eventIndex: number,
  ): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: SpamTokenIncomingData = {
      paymentAddressId: entry.options.paymentAddressId,
      transactionId: entry.options.transactionId,
      txHash: record.transaction_id,
      eventIndex,
      fromAddress: record.from,
      toAddress: record.to,
      expectedContractAddress: entry.options.expectedContract,
      actualContractAddress: record.token_info.address,
      amount: formatTokenAmount(record.value, decimals),
      blockTimestampMs: record.block_timestamp,
      detectedAt: this.clock().toISOString(),
    };
    await this.deliver({
      endpoint: this.deps.webhookEndpoints.spamTokenIncoming,
      event: BlockchainWebhookEvents.SpamTokenIncoming,
      data,
      entry,
    });
    transfersTotal.inc({ type: 'SPAM_TOKEN_INCOMING', status: 'DETECTED' });
  }

  private async emitStateChanged(
    entry: PostCancelMonitorEntry,
    previousState: PostCancelMonitorState,
    newState: PostCancelMonitorState,
    newStateExpiresAt: Date | null,
  ): Promise<void> {
    const data: PostCancelMonitorStateChangedData = {
      paymentAddressId: entry.options.paymentAddressId,
      transactionId: entry.options.transactionId,
      address: entry.options.address,
      previousState,
      newState,
      newStateExpiresAt: newStateExpiresAt?.toISOString() ?? null,
      cancelledAt: entry.options.cancelledAt.toISOString(),
      changedAt: this.clock().toISOString(),
    };
    await this.deliver({
      endpoint: this.deps.webhookEndpoints.postCancelMonitorStateChanged,
      event: BlockchainWebhookEvents.PostCancelMonitorStateChanged,
      data,
      entry,
    });
  }

  private async deliver<TData>(args: {
    endpoint: string;
    event: BlockchainWebhookEvent;
    data: TData;
    entry: PostCancelMonitorEntry;
  }): Promise<void> {
    const envelope: BlockchainWebhookEnvelope<TData> = {
      event: args.event,
      timestamp: this.clock().toISOString(),
      data: args.data,
    };
    try {
      await this.webhookSender(
        args.endpoint,
        envelope as unknown as AnyBlockchainWebhookPayload,
        args.entry.correlationId,
      );
    } catch (err) {
      if (err instanceof WebhookDeliveryError && !err.retryable) {
        logger.error(
          {
            err: err.message,
            event: args.event,
            address: args.entry.options.address,
            correlationId: args.entry.correlationId,
          },
          'Post-cancel webhook rejected — payload dropped',
        );
        return;
      }
      throw err;
    }
  }
}
