import crypto from 'crypto';
import { logger } from '../logger.js';
import { transfersTotal } from '../metrics.js';
import { reportActiveMonitorCount } from './activeMonitorGauge.js';
import type { Trc20Record, TransferLogEntry, TronGridClient } from '../tron/TronGridClient.js';
import { sendCallback, WebhookDeliveryError } from '../webhook/WebhookClient.js';
import type {
  AnyBlockchainWebhookPayload,
  BlockchainWebhookEvent,
  BlockchainWebhookEnvelope,
  PaymentConfirmedData,
  PaymentDetectedData,
  SpamTokenIncomingData,
  WrongTokenIncomingData,
} from '../webhook/WebhookPayloads.js';
import { BlockchainWebhookEvents } from '../webhook/WebhookPayloads.js';
import {
  classifyToken,
  confirmationCount,
  formatTokenAmount,
  isFinalized,
  isIncomingFor,
  isTransferRecord,
  type StablecoinAllowlist,
  type StablecoinSymbol,
} from './PaymentMonitorRules.js';

export interface MonitorStartOptions {
  address: string;
  paymentAddressId: string;
  transactionId: string;
  expectedContract: string;
  expectedSymbol: StablecoinSymbol;
}

export interface MonitorRegistryDeps {
  client: TronGridClient;
  allowlist: StablecoinAllowlist;
  intervalMs: number;
  minConfirmations: number;
  pageLimit: number;
  webhookEndpoints: {
    paymentDetected: string;
    paymentConfirmed: string;
    wrongTokenIncoming: string;
    spamTokenIncoming: string;
  };
  /** Default `Date.now()` — overridable for deterministic finality tests. */
  clock?: () => Date;
  /** Default `sendCallback` — overridable in unit tests. */
  webhookSender?: typeof sendCallback;
}

interface PendingFinality {
  txHash: string;
  eventIndex: number;
  blockNumber: number | null;
  firstSeenAt: number;
}

interface MonitorState {
  options: MonitorStartOptions;
  correlationId: string;
  phase1Fingerprint?: string;
  phase2Fingerprint?: string;
  /**
   * `${txHash}:${eventIndex}` keys for which an event has been emitted —
   * guards against re-emit on polling overlap. WP10 dedups at event-index
   * granularity (08 §3.4) so a single transaction carrying several Transfer
   * events to the deposit address surfaces each one exactly once. The common
   * single-transfer case is `${txHash}:0`.
   */
  seenEvents: Set<string>;
  pendingFinality: Map<string, PendingFinality>;
}

/** Composite dedup / pending-finality key (08 §3.4 — WP10). */
function eventKey(txHash: string, eventIndex: number): string {
  return `${txHash}:${eventIndex}`;
}

/**
 * Manages active payment monitors per deposit address (T71). One registry
 * instance per sidecar. The backend calls `start` when the buyer's payment
 * window opens — the `ACCEPTED -> SELLER_CONFIRMED` transition, which is also
 * the first moment the deposit address is revealed to the buyer — and `stop`
 * when that window closes: on the post-cancel handover (T75 then takes the
 * same address onto the gradual cadence) or once the deposit has been swept
 * into the hot wallet.
 *
 * Both calls arrive from the T139 pair (`PaymentMonitorStartDispatcher` on the
 * outbox fast path, `EnsurePaymentMonitorJob` as the per-minute reconciler).
 * Before T139 neither endpoint had a backend caller at all — this registry
 * only ever held what a manual request put in it, so a real buyer's transfer
 * produced no `payment-detected` event.
 *
 * <para>
 * Polling cadence is driven by a single shared `setInterval` regardless of
 * monitor count — each tick visits every address sequentially. The
 * registry is `polling`-guarded so a slow tick never overlaps itself.
 * </para>
 *
 * <para>
 * Idempotency: per-address `seenEvents` is the in-memory dedup layer
 * (sidecar lifetime), keyed by `${txHash}:${eventIndex}`. Backend
 * defence-in-depth comes from `BlockchainTransaction (TxHash, EventIndex)`
 * UNIQUE (06 §3.8). WP10 (08 §3.4) closes the T71 K3 gap: a single TRC-20
 * transaction carrying multiple Transfer events to the deposit address is
 * now dedup'd at event-index granularity, with the real on-chain log index
 * resolved via `TronGridClient.resolveTransferEventIndices`. When the
 * solidity node has not yet surfaced the logs the resolver falls back to
 * index 0 (status-quo single-event behaviour) so it never regresses.
 * </para>
 */
export class MonitorRegistry {
  private readonly monitors = new Map<string, MonitorState>();
  private timer?: NodeJS.Timeout;
  private polling = false;
  private stopped = false;
  private readonly clock: () => Date;
  private readonly webhookSender: typeof sendCallback;

  constructor(private readonly deps: MonitorRegistryDeps) {
    this.clock = deps.clock ?? (() => new Date());
    this.webhookSender = deps.webhookSender ?? sendCallback;
  }

  /**
   * Register a deposit address for active monitoring. Idempotent — restarting
   * the same address keeps existing pagination cursors and dedup state.
   */
  start(options: MonitorStartOptions): { started: boolean } {
    if (this.stopped) {
      throw new Error('MonitorRegistry has been shut down');
    }
    if (this.monitors.has(options.address)) {
      logger.info(
        { address: options.address, transactionId: options.transactionId },
        'Monitor already active for address — no-op restart',
      );
      return { started: false };
    }
    this.monitors.set(options.address, {
      options,
      correlationId: crypto.randomUUID(),
      seenEvents: new Set(),
      pendingFinality: new Map(),
    });
    reportActiveMonitorCount('active', this.monitors.size);
    logger.info(
      {
        address: options.address,
        transactionId: options.transactionId,
        expectedSymbol: options.expectedSymbol,
      },
      'Monitor started',
    );
    this.ensureTimer();
    return { started: true };
  }

  /**
   * Stop monitoring the given address. Returns `true` if the address was
   * being monitored, `false` otherwise — both are valid outcomes for the
   * backend (state machine may have cancelled an already-confirmed monitor).
   */
  stop(address: string): { stopped: boolean } {
    const had = this.monitors.delete(address);
    reportActiveMonitorCount('active', this.monitors.size);
    if (had) {
      logger.info({ address }, 'Monitor stopped');
    }
    if (this.monitors.size === 0) {
      this.clearTimer();
    }
    return { stopped: had };
  }

  size(): number {
    return this.monitors.size;
  }

  async shutdown(): Promise<void> {
    this.stopped = true;
    this.clearTimer();
    this.monitors.clear();
    reportActiveMonitorCount('active', 0);
  }

  /** Public for tests — runs a single polling iteration deterministically. */
  async tick(): Promise<void> {
    if (this.polling || this.stopped) {
      return;
    }
    this.polling = true;
    try {
      const states = [...this.monitors.values()];
      for (const state of states) {
        await this.pollOne(state);
      }
    } finally {
      this.polling = false;
    }
  }

  private ensureTimer(): void {
    if (this.timer || this.stopped) return;
    this.timer = setInterval(() => {
      void this.tick().catch((err) => {
        logger.error({ err: (err as Error).message }, 'Monitor tick crashed');
      });
    }, this.deps.intervalMs);
    this.timer.unref?.();
  }

  private clearTimer(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = undefined;
    }
  }

  private async pollOne(state: MonitorState): Promise<void> {
    // Per-tick cache of resolved transfer-log entries keyed by
    // `${txHash}:${contract}` so multiple list records of the same
    // transaction share a single `gettransactioninfobyid` lookup (08 §3.4).
    const logCache = new Map<string, TransferLogEntry[]>();
    try {
      await this.pollPhase1(state, logCache);
      await this.pollPhase2(state, logCache);
      if (state.pendingFinality.size > 0) {
        await this.checkFinality(state);
      }
    } catch (err) {
      logger.error(
        {
          err: (err as Error).message,
          address: state.options.address,
          correlationId: state.correlationId,
        },
        'Monitor poll failed — will retry next tick',
      );
    }
  }

  private async pollPhase1(
    state: MonitorState,
    logCache: Map<string, TransferLogEntry[]>,
  ): Promise<void> {
    const response = await this.deps.client.listTrc20({
      address: state.options.address,
      contractAddress: state.options.expectedContract,
      fingerprint: state.phase1Fingerprint,
      limit: this.deps.pageLimit,
    });
    if (response.fingerprint) {
      state.phase1Fingerprint = response.fingerprint;
    }
    for (const record of response.records) {
      if (!this.shouldEmit(record, state)) continue;
      // Defensive: only proceed if the record really belongs to the expected contract.
      if (record.token_info.address !== state.options.expectedContract) {
        logger.debug(
          { txHash: record.transaction_id, contract: record.token_info.address },
          'Phase 1 returned a non-expected contract row — skipping',
        );
        continue;
      }
      const eventIndex = await this.resolveEventIndex(state, record, logCache);
      const key = eventKey(record.transaction_id, eventIndex);
      if (state.seenEvents.has(key)) continue;
      await this.emitPaymentDetected(state, record, eventIndex);
      state.seenEvents.add(key);
      state.pendingFinality.set(key, {
        txHash: record.transaction_id,
        eventIndex,
        blockNumber: null,
        firstSeenAt: this.clock().getTime(),
      });
    }
  }

  private async pollPhase2(
    state: MonitorState,
    logCache: Map<string, TransferLogEntry[]>,
  ): Promise<void> {
    const response = await this.deps.client.listTrc20({
      address: state.options.address,
      fingerprint: state.phase2Fingerprint,
      limit: this.deps.pageLimit,
    });
    if (response.fingerprint) {
      state.phase2Fingerprint = response.fingerprint;
    }
    for (const record of response.records) {
      if (!this.shouldEmit(record, state)) continue;
      const classification = classifyToken({
        contractAddress: record.token_info.address,
        expectedContract: state.options.expectedContract,
        allowlist: this.deps.allowlist,
      });
      const eventIndex = await this.resolveEventIndex(state, record, logCache);
      const key = eventKey(record.transaction_id, eventIndex);
      if (state.seenEvents.has(key)) continue;
      if (classification.kind === 'expected') {
        // Late catch — phase 1's fingerprint advanced past this record (rare
        // but possible if phase 1's first call missed it). Treat as detected.
        await this.emitPaymentDetected(state, record, eventIndex);
        state.pendingFinality.set(key, {
          txHash: record.transaction_id,
          eventIndex,
          blockNumber: null,
          firstSeenAt: this.clock().getTime(),
        });
      } else if (classification.kind === 'wrong_token') {
        await this.emitWrongTokenIncoming(state, record, classification.symbol, eventIndex);
      } else {
        await this.emitSpamTokenIncoming(state, record, eventIndex);
      }
      state.seenEvents.add(key);
    }
  }

  /**
   * Resolve the on-chain log index for a trc20-list record (08 §3.4 — WP10).
   * The record is correlated to its log entry by matching the raw transfer
   * value, so the per-event amount stays authoritative while a stable, real
   * event index is assigned. Falls back to index 0 (status-quo single-event
   * behaviour) when the solidity node has not yet surfaced the logs or the
   * value cannot be matched — this never regresses the common single-transfer
   * case and degrades gracefully to today's txid-level behaviour.
   */
  private async resolveEventIndex(
    state: MonitorState,
    record: Trc20Record,
    logCache: Map<string, TransferLogEntry[]>,
  ): Promise<number> {
    const cacheKey = `${record.transaction_id}:${record.token_info.address}`;
    let entries = logCache.get(cacheKey);
    if (entries === undefined) {
      entries = await this.deps.client.resolveTransferEventIndices(
        record.transaction_id,
        record.token_info.address,
        state.options.address,
      );
      logCache.set(cacheKey, entries);
    }
    for (const entry of entries) {
      if (entry.value !== record.value) continue;
      if (state.seenEvents.has(eventKey(record.transaction_id, entry.index))) continue;
      return entry.index;
    }
    return 0;
  }

  private async checkFinality(state: MonitorState): Promise<void> {
    const currentSolid = await this.deps.client.getNowSolidBlock();
    const pendings = [...state.pendingFinality.values()];
    // Finality is per-transaction (every event in a tx confirms together), so
    // resolve each unique txHash's block height exactly once per tick.
    const blockByTx = new Map<string, number | null>();
    for (const pending of pendings) {
      if (pending.blockNumber === null) {
        let block = blockByTx.get(pending.txHash);
        if (block === undefined) {
          const info = await this.deps.client.getTransactionInfoById(pending.txHash);
          block = info && info.blockNumber !== undefined ? info.blockNumber : null;
          blockByTx.set(pending.txHash, block);
        }
        if (block === null) {
          // Not yet on the solid node — try again next tick.
          continue;
        }
        pending.blockNumber = block;
      }
      if (
        isFinalized({
          currentSolidBlock: currentSolid,
          txBlock: pending.blockNumber,
          minConfirmations: this.deps.minConfirmations,
        })
      ) {
        const conf = confirmationCount({
          currentSolidBlock: currentSolid,
          txBlock: pending.blockNumber,
        });
        await this.emitPaymentConfirmed(state, pending, conf);
        state.pendingFinality.delete(eventKey(pending.txHash, pending.eventIndex));
      }
    }
  }

  private shouldEmit(record: Trc20Record, state: MonitorState): boolean {
    if (!isTransferRecord(record.type)) {
      logger.debug(
        { type: record.type, txHash: record.transaction_id },
        'Skipping non-Transfer record',
      );
      return false;
    }
    if (!isIncomingFor(record, state.options.address)) {
      return false;
    }
    // Per-event dedup happens after the event index is resolved (08 §3.4).
    return true;
  }

  private async emitPaymentDetected(
    state: MonitorState,
    record: Trc20Record,
    eventIndex: number,
  ): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: PaymentDetectedData = {
      paymentAddressId: state.options.paymentAddressId,
      transactionId: state.options.transactionId,
      txHash: record.transaction_id,
      eventIndex,
      fromAddress: record.from,
      toAddress: record.to,
      contractAddress: record.token_info.address,
      tokenSymbol: state.options.expectedSymbol,
      amount: formatTokenAmount(record.value, decimals),
      blockTimestampMs: record.block_timestamp,
      detectedAt: this.clock().toISOString(),
    };
    await this.deliver({
      endpoint: this.deps.webhookEndpoints.paymentDetected,
      event: BlockchainWebhookEvents.PaymentDetected,
      data,
      state,
    });
    transfersTotal.inc({ type: 'BUYER_PAYMENT', status: 'DETECTED' });
  }

  private async emitPaymentConfirmed(
    state: MonitorState,
    pending: PendingFinality,
    confirmations: number,
  ): Promise<void> {
    if (pending.blockNumber === null) {
      throw new Error('Cannot confirm a payment whose block has not been resolved');
    }
    const data: PaymentConfirmedData = {
      paymentAddressId: state.options.paymentAddressId,
      transactionId: state.options.transactionId,
      txHash: pending.txHash,
      eventIndex: pending.eventIndex,
      blockNumber: pending.blockNumber,
      confirmationCount: confirmations,
      confirmedAt: this.clock().toISOString(),
    };
    await this.deliver({
      endpoint: this.deps.webhookEndpoints.paymentConfirmed,
      event: BlockchainWebhookEvents.PaymentConfirmed,
      data,
      state,
    });
    transfersTotal.inc({ type: 'BUYER_PAYMENT', status: 'CONFIRMED' });
  }

  private async emitWrongTokenIncoming(
    state: MonitorState,
    record: Trc20Record,
    actualSymbol: StablecoinSymbol,
    eventIndex: number,
  ): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: WrongTokenIncomingData = {
      paymentAddressId: state.options.paymentAddressId,
      transactionId: state.options.transactionId,
      txHash: record.transaction_id,
      eventIndex,
      fromAddress: record.from,
      toAddress: record.to,
      expectedContractAddress: state.options.expectedContract,
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
      state,
    });
    transfersTotal.inc({ type: 'WRONG_TOKEN_INCOMING', status: 'DETECTED' });
  }

  private async emitSpamTokenIncoming(
    state: MonitorState,
    record: Trc20Record,
    eventIndex: number,
  ): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: SpamTokenIncomingData = {
      paymentAddressId: state.options.paymentAddressId,
      transactionId: state.options.transactionId,
      txHash: record.transaction_id,
      eventIndex,
      fromAddress: record.from,
      toAddress: record.to,
      expectedContractAddress: state.options.expectedContract,
      actualContractAddress: record.token_info.address,
      amount: formatTokenAmount(record.value, decimals),
      blockTimestampMs: record.block_timestamp,
      detectedAt: this.clock().toISOString(),
    };
    await this.deliver({
      endpoint: this.deps.webhookEndpoints.spamTokenIncoming,
      event: BlockchainWebhookEvents.SpamTokenIncoming,
      data,
      state,
    });
    transfersTotal.inc({ type: 'SPAM_TOKEN_INCOMING', status: 'DETECTED' });
  }

  private async deliver<TData>(args: {
    endpoint: string;
    event: BlockchainWebhookEvent;
    data: TData;
    state: MonitorState;
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
        args.state.correlationId,
      );
    } catch (err) {
      if (err instanceof WebhookDeliveryError && !err.retryable) {
        logger.error(
          {
            err: err.message,
            event: args.event,
            address: args.state.options.address,
            correlationId: args.state.correlationId,
          },
          'Webhook rejected with non-retryable error — payload dropped',
        );
        return;
      }
      // Retryable: bubble up so the polling loop retries the whole tick.
      throw err;
    }
  }
}
