import crypto from 'crypto';
import { logger } from '../logger.js';
import { activeMonitors, transfersTotal } from '../metrics.js';
import type { Trc20Record, TronGridClient } from '../tron/TronGridClient.js';
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
  blockNumber: number | null;
  firstSeenAt: number;
}

interface MonitorState {
  options: MonitorStartOptions;
  correlationId: string;
  phase1Fingerprint?: string;
  phase2Fingerprint?: string;
  /** TxHashes for which any event has been emitted — guards against re-emit on polling overlap. */
  seenTxHashes: Set<string>;
  pendingFinality: Map<string, PendingFinality>;
}

/**
 * Manages active payment monitors per deposit address (T71). One registry
 * instance per sidecar; backend calls `start` when a transaction enters
 * PENDING_PAYMENT (T44 state) and `stop` once finality has been observed
 * or the transaction is cancelled (T75 takes over the post-cancel cadence).
 *
 * <para>
 * Polling cadence is driven by a single shared `setInterval` regardless of
 * monitor count — each tick visits every address sequentially. The
 * registry is `polling`-guarded so a slow tick never overlaps itself.
 * </para>
 *
 * <para>
 * Idempotency: per-address `seenTxHashes` is the in-memory dedup layer
 * (sidecar lifetime). Backend defence-in-depth comes from
 * `BlockchainTransaction.TxHash` UNIQUE (06 §3.8). Multi-event TRC-20
 * transactions are not handled at event-index granularity in T71 — see
 * K3 in TASK_REPORTS/T71_REPORT.md.
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
      seenTxHashes: new Set(),
      pendingFinality: new Map(),
    });
    activeMonitors.set(this.monitors.size);
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
    activeMonitors.set(this.monitors.size);
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
    activeMonitors.set(0);
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
    try {
      await this.pollPhase1(state);
      await this.pollPhase2(state);
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

  private async pollPhase1(state: MonitorState): Promise<void> {
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
      await this.emitPaymentDetected(state, record);
      state.seenTxHashes.add(record.transaction_id);
      state.pendingFinality.set(record.transaction_id, {
        txHash: record.transaction_id,
        blockNumber: null,
        firstSeenAt: this.clock().getTime(),
      });
    }
  }

  private async pollPhase2(state: MonitorState): Promise<void> {
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
      if (classification.kind === 'expected') {
        // Late catch — phase 1's fingerprint advanced past this record (rare
        // but possible if phase 1's first call missed it). Treat as detected.
        await this.emitPaymentDetected(state, record);
        state.pendingFinality.set(record.transaction_id, {
          txHash: record.transaction_id,
          blockNumber: null,
          firstSeenAt: this.clock().getTime(),
        });
      } else if (classification.kind === 'wrong_token') {
        await this.emitWrongTokenIncoming(state, record, classification.symbol);
      } else {
        await this.emitSpamTokenIncoming(state, record);
      }
      state.seenTxHashes.add(record.transaction_id);
    }
  }

  private async checkFinality(state: MonitorState): Promise<void> {
    const currentSolid = await this.deps.client.getNowSolidBlock();
    const txs = [...state.pendingFinality.values()];
    for (const pending of txs) {
      if (pending.blockNumber === null) {
        const info = await this.deps.client.getTransactionInfoById(pending.txHash);
        if (!info || info.blockNumber === undefined) {
          // Not yet on the solid node — try again next tick.
          continue;
        }
        pending.blockNumber = info.blockNumber;
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
        state.pendingFinality.delete(pending.txHash);
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
    if (state.seenTxHashes.has(record.transaction_id)) {
      return false;
    }
    return true;
  }

  private async emitPaymentDetected(state: MonitorState, record: Trc20Record): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: PaymentDetectedData = {
      paymentAddressId: state.options.paymentAddressId,
      transactionId: state.options.transactionId,
      txHash: record.transaction_id,
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
  ): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: WrongTokenIncomingData = {
      paymentAddressId: state.options.paymentAddressId,
      transactionId: state.options.transactionId,
      txHash: record.transaction_id,
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

  private async emitSpamTokenIncoming(state: MonitorState, record: Trc20Record): Promise<void> {
    const decimals = record.token_info.decimals ?? 6;
    const data: SpamTokenIncomingData = {
      paymentAddressId: state.options.paymentAddressId,
      transactionId: state.options.transactionId,
      txHash: record.transaction_id,
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
