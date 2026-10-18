import { logger } from '../logger.js';
import {
  monitorCatchUpDeposits,
  monitorScanFailuresTotal,
  monitorScannedBlock,
  paymentEventsTotal,
} from '../metrics.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import type { EthereumRpcClient, RpcLog } from '../rpc/RpcClient.js';
import type { BalanceReader } from '../reconcile/BalanceReader.js';
import { CU_COST, type RatePacer } from '../reconcile/RatePacer.js';
import {
  BlockchainWebhookEvents,
  type LatePaymentDetectedData,
  type PaymentConfirmedData,
  type PaymentDetectedData,
  type SpamTokenIncomingData,
  type WrongTokenIncomingData,
} from '../webhook/WebhookPayloads.js';
import { WebhookDeliveryError, type SendWebhook } from '../webhook/WebhookClient.js';
import type { DepositRegistry, WatchEntry } from './DepositRegistry.js';
import {
  TRANSFER_TOPIC,
  addressWord,
  decodeTransferLog,
  formatTokenAmount,
  type Erc20Transfer,
} from './erc20.js';
import {
  STABLECOIN_DECIMALS,
  allowlistedSymbol,
  classifyToken,
  type StablecoinAllowlist,
  type StablecoinSymbol,
} from './tokenRules.js';

/**
 * The Ethereum payment monitor (T151 — 08 §3.7.4, 11 §F8 K5).
 *
 * <para>
 * <b>One query for every deposit.</b> A block range is read with one
 * `eth_getLogs` for ERC-20 `Transfer` events whose recipient (topic 2) is any
 * watched deposit, from any contract — the expected token, the allowlisted
 * wrong token and spam alike, so the TRON service's two phases are one query
 * here. A second query reads the allowlisted tokens' transfers OUT of the
 * deposits (sweep, refund); those are not reported, only counted for the
 * balance reconciliation. The RPC client splits each query by the answering
 * provider's block range and 1,000-address limits (T150).
 * </para>
 *
 * <para>
 * <b>Only finalized blocks (K5).</b> The upper end of every range is the
 * `finalized` head the FinalityTracker last read — never `latest`. A transfer
 * is therefore final when it is first seen: `payment.detected` and
 * `payment.confirmed` are sent back to back, and a reorg cannot take back a
 * reported payment. A head that goes backwards (a lagging provider answered)
 * is ignored.
 * </para>
 *
 * <para>
 * <b>Forward block cursor.</b> {@link scannedBlock} is the last finalized
 * block processed for every caught-up deposit; it moves only after every
 * event in the range was delivered (or refused for good with a 4xx). A failed
 * read or a retryable webhook failure leaves it where it was and the range is
 * read again; events already delivered are skipped by the in-memory
 * `(txHash, logIndex)` set, and the backend's `(Network, TxHash, EventIndex)`
 * key makes a repeat after a restart idempotent. The log index is the chain's
 * own — no guessing as on TRON (08 §3.7.4 "Tekillik").
 * </para>
 *
 * <para>
 * <b>Where a deposit starts.</b> Memory is lost on restart and the backend
 * re-arms every open address within a minute, so a newly registered address
 * is read back from an earlier block ("catch-up", paced and bounded per tick):
 * <list type="bullet">
 *   <item>registered within the re-arm window after start, HOLDING or
 *     post-cancel → from {@link PaymentScannerDeps.restartLookbackBlocks}
 *     before the start block (default 7,200 ≈ 24 h);</item>
 *   <item>a fresh PAYMENT deposit → its balances are read at the finalized
 *     head; all zero means nothing ever arrived and it starts there, anything
 *     else (or a failed read) means it is read back the same lookback.</item>
 * </list>
 * A transfer older than the lookback is not reported by the log path; the
 * balance reconciliation is the second path for it (08 §3.7.8).
 * </para>
 */

export interface PaymentScannerDeps {
  rpc: Pick<EthereumRpcClient, 'getLogs' | 'getBlockHeader'>;
  registry: DepositRegistry;
  finality: () => FinalityStatus;
  allowlist: StablecoinAllowlist;
  send: SendWebhook;
  balances: Pick<BalanceReader, 'read' | 'configuredSymbols'>;
  pacer: RatePacer;
  tickIntervalMs: number;
  restartLookbackBlocks: number;
  rearmWindowMs: number;
  catchUpBlocksPerTick: number;
  /** Smallest `eth_getLogs` block range among the providers — sizes the pacing estimate. */
  minProviderBlockRange: number;
  /** Most blocks one head step reads; the rest waits for the next step of the same tick. */
  headChunkBlocks?: number;
  now?: () => number;
}

/** Decimals assumed for a spam token's amount — its contract is never called (it could stall the scan). */
export const SPAM_TOKEN_DECIMALS = 18;

export class PaymentScanner {
  private cursor: bigint | null = null;
  private bootBlock: bigint | null = null;
  /** When this process began watching — the re-arm window counts from here (not from the first head read). */
  private readonly bootAt: number;
  private scanning = false;
  private timer?: NodeJS.Timeout;
  private running = false;
  private readonly now: () => number;

  constructor(private readonly deps: PaymentScannerDeps) {
    this.now = deps.now ?? (() => Date.now());
    this.bootAt = this.now();
  }

  /** Last finalized block processed for every caught-up deposit; null before the first head. */
  scannedBlock(): bigint | null {
    return this.cursor;
  }

  /** True once {@link start} ran — part of `acceptsPayments` (/health). */
  isRunning(): boolean {
    return this.running;
  }

  start(): void {
    this.running = true;
    this.timer = setInterval(() => void this.tick(), this.deps.tickIntervalMs);
    this.timer.unref();
  }

  stop(): void {
    this.running = false;
    if (this.timer) clearInterval(this.timer);
    this.timer = undefined;
  }

  /** One pass: pick up new deposits, read new finalized blocks, catch up, advance post-cancel states. Never throws. */
  async tick(): Promise<void> {
    if (this.scanning) return;
    this.scanning = true;
    try {
      const head = this.deps.finality().snapshot;
      if (!head) return;
      const finalized = head.number;
      if (this.cursor === null) {
        this.cursor = finalized;
        this.bootBlock = finalized;
        monitorScannedBlock.set(Number(finalized));
        logger.info(
          { finalized: String(finalized) },
          'Ethereum payment monitor started at the finalized head',
        );
      }
      await this.pickUpNewEntries(finalized);
      if (finalized > this.cursor) await this.scanHead(finalized);
      await this.catchUp(finalized);
      await this.deps.registry.advancePostCancelStates((data, correlationId) =>
        this.deliver(BlockchainWebhookEvents.PostCancelMonitorStateChanged, data, correlationId),
      );
    } catch (err) {
      monitorScanFailuresTotal.inc();
      logger.warn(
        {
          reason: (err as Error).message,
          scanned: this.cursor === null ? null : String(this.cursor),
        },
        'Ethereum payment monitor pass failed — the same blocks are read again next tick',
      );
    } finally {
      this.scanning = false;
      monitorCatchUpDeposits.set(this.behind().length);
    }
  }

  private behind(): WatchEntry[] {
    const cursor = this.cursor;
    if (cursor === null) return [];
    return this.deps.registry
      .entries()
      .filter((e) => e.scan.scannedThrough !== null && e.scan.scannedThrough < cursor);
  }

  private async pickUpNewEntries(finalized: bigint): Promise<void> {
    const lookback = BigInt(this.deps.restartLookbackBlocks);
    const floor = (b: bigint) => (b > lookback ? b - lookback : 0n);
    for (const entry of this.deps.registry.entries()) {
      const scan = entry.scan;
      if (scan.scannedThrough !== null) continue;
      const rearmed = entry.armedAt - this.bootAt < this.deps.rearmWindowMs;
      if (entry.kind === 'active' && entry.cadence === 'PAYMENT' && !rearmed) {
        // A fresh deposit: if nothing is on it at the finalized head, nothing
        // ever arrived (nothing leaves a deposit before something arrives).
        let empty = false;
        try {
          const b = await this.deps.balances.read(entry.address, finalized);
          empty =
            b.ETH === 0n &&
            this.deps.balances.configuredSymbols().every((s) => (b.tokens[s] ?? 0n) === 0n);
        } catch (err) {
          logger.warn(
            { address: entry.address, reason: (err as Error).message },
            'Fresh deposit balance probe failed — reading it back from the lookback block',
          );
        }
        const start = empty ? finalized : floor(finalized);
        scan.scannedThrough = start;
        scan.assumeEmptyStart = true;
        scan.baseline = { block: start, eth: 0n, tokens: this.zeroTokens() };
        logger.info(
          { address: entry.address, fromBlock: String(start + 1n), empty },
          'Ethereum deposit picked up',
        );
      } else {
        // Re-armed after a restart, or a deposit that may already hold funds:
        // read it back; its first reconciliation reading is its baseline.
        const base = rearmed ? (this.bootBlock ?? finalized) : (this.cursor ?? finalized);
        scan.scannedThrough = floor(base);
        scan.assumeEmptyStart = false;
        scan.baseline = null;
        logger.info(
          { address: entry.address, kind: entry.kind, fromBlock: String(scan.scannedThrough + 1n) },
          'Ethereum deposit picked up — reading back its past blocks',
        );
      }
    }
  }

  private zeroTokens(): Partial<Record<StablecoinSymbol, bigint>> {
    const tokens: Partial<Record<StablecoinSymbol, bigint>> = {};
    for (const s of this.deps.balances.configuredSymbols()) tokens[s] = 0n;
    return tokens;
  }

  private async scanHead(finalized: bigint): Promise<void> {
    const cursor = this.cursor!;
    const entries = this.deps.registry
      .entries()
      .filter((e) => e.scan.scannedThrough !== null && e.scan.scannedThrough >= cursor);
    if (entries.length === 0) {
      // Nothing to read for: move on without a request.
      this.advanceCursor(finalized);
      return;
    }
    const step = BigInt(this.deps.headChunkBlocks ?? 1000);
    let from = cursor + 1n;
    while (from <= finalized) {
      const to = from + step - 1n < finalized ? from + step - 1n : finalized;
      await this.scanRange(from, to, entries, finalized);
      for (const e of entries) {
        if (e.scan.scannedThrough! < to) e.scan.scannedThrough = to;
      }
      this.advanceCursor(to);
      from = to + 1n;
    }
  }

  private advanceCursor(block: bigint): void {
    this.cursor = block;
    monitorScannedBlock.set(Number(block));
  }

  private async catchUp(finalized: bigint): Promise<void> {
    let budget = BigInt(this.deps.catchUpBlocksPerTick);
    while (budget > 0n) {
      const behind = this.behind();
      if (behind.length === 0) return;
      const cursor = this.cursor!;
      const from =
        behind.reduce((m, e) => (e.scan.scannedThrough! < m ? e.scan.scannedThrough! : m), cursor) +
        1n;
      const to = from + budget - 1n < cursor ? from + budget - 1n : cursor;
      const group = behind.filter((e) => e.scan.scannedThrough! < to);
      await this.scanRange(from, to, group, finalized);
      for (const e of group) {
        if (e.scan.scannedThrough! < to) e.scan.scannedThrough = to;
      }
      budget -= to - from + 1n;
    }
  }

  /** Reads [from, to] for <c>entries</c> and delivers what arrived. Throws to have the range read again. */
  async scanRange(
    from: bigint,
    to: bigint,
    entries: readonly WatchEntry[],
    finalized: bigint,
  ): Promise<void> {
    const byKey = new Map(entries.map((e) => [e.key, e]));
    const recipients = [...byKey.keys()].map(addressWord);
    const tokens = (['USDT', 'USDC'] as const)
      .map((s) => this.deps.allowlist[s])
      .filter((c) => c !== '');

    await this.pace(from, to, recipients.length, tokens.length > 0 ? 2 : 1);
    const incoming = await this.deps.rpc.getLogs({
      topics: [TRANSFER_TOPIC, null, recipients],
      fromBlock: from,
      toBlock: to,
    });
    const outgoing =
      tokens.length === 0
        ? []
        : await this.deps.rpc.getLogs({
            address: tokens,
            topics: [TRANSFER_TOPIC, recipients],
            fromBlock: from,
            toBlock: to,
          });

    for (const t of this.transfers(outgoing, from, to)) {
      const entry = byKey.get(t.from);
      const symbol = allowlistedSymbol(t.contract, this.deps.allowlist);
      if (entry && symbol) {
        recordFlow(entry, `out:${t.txHash}:${t.logIndex}`, t.blockNumber, symbol, -t.value);
      }
    }

    const timestamps = new Map<bigint, number>();
    for (const t of this.transfers(incoming, from, to)) {
      const entry = byKey.get(t.to);
      if (!entry) {
        logger.error(
          { txHash: t.txHash, to: t.to },
          'eth_getLogs returned a transfer to an address that was not asked for — ignored',
        );
        continue;
      }
      await this.handleIncoming(entry, t, finalized, timestamps);
    }
  }

  /** Decoded ERC-20 transfers in (block, logIndex) order; drops what must not be acted on. */
  private transfers(logs: readonly RpcLog[], from: bigint, to: bigint): Erc20Transfer[] {
    const out: Erc20Transfer[] = [];
    for (const log of logs) {
      if (log.removed) {
        logger.error(
          { txHash: log.transactionHash, block: String(log.blockNumber) },
          'Removed log inside a finalized range — ignored',
        );
        continue;
      }
      if (log.blockNumber < from || log.blockNumber > to) {
        logger.error(
          { txHash: log.transactionHash, block: String(log.blockNumber) },
          'Log outside the requested range — ignored',
        );
        continue;
      }
      const t = decodeTransferLog(log);
      if (!t) continue; // ERC-721 or a non-standard event sharing the topic
      out.push(t);
    }
    return out.sort((a, b) =>
      a.blockNumber === b.blockNumber
        ? a.logIndex - b.logIndex
        : a.blockNumber < b.blockNumber
          ? -1
          : 1,
    );
  }

  private async handleIncoming(
    entry: WatchEntry,
    t: Erc20Transfer,
    finalized: bigint,
    timestamps: Map<bigint, number>,
  ): Promise<void> {
    const key = `${t.txHash}:${t.logIndex}`;
    const symbol = allowlistedSymbol(t.contract, this.deps.allowlist);
    if (!entry.scan.reported.has(key)) {
      let blockTimestampMs = timestamps.get(t.blockNumber);
      if (blockTimestampMs === undefined) {
        await this.deps.pacer.acquire(CU_COST.eth_getBlockByNumber);
        blockTimestampMs = (await this.deps.rpc.getBlockHeader(t.blockNumber)).timestamp * 1000;
        timestamps.set(t.blockNumber, blockTimestampMs);
      }
      try {
        await this.report(entry, t, finalized, blockTimestampMs);
      } catch (err) {
        if (!(err instanceof WebhookDeliveryError) || err.retryable) throw err;
        logger.error(
          {
            event: err.event,
            status: err.status,
            txHash: t.txHash,
            logIndex: t.logIndex,
            address: entry.address,
          },
          'Backend refused the event for good (4xx) — not sent again',
        );
      }
      entry.scan.reported.add(key);
    }
    if (symbol) recordFlow(entry, `in:${key}`, t.blockNumber, symbol, t.value);
  }

  private async report(
    entry: WatchEntry,
    t: Erc20Transfer,
    finalized: bigint,
    blockTimestampMs: number,
  ): Promise<void> {
    const detectedAt = new Date(this.now()).toISOString();
    const common = {
      paymentAddressId: entry.paymentAddressId,
      transactionId: entry.transactionId,
      txHash: t.txHash,
      eventIndex: t.logIndex,
      fromAddress: t.from,
      toAddress: t.to,
      blockTimestampMs,
      detectedAt,
    };
    const kind = classifyToken({
      contract: t.contract,
      expectedContract: entry.expectedContract,
      allowlist: this.deps.allowlist,
    });

    if (kind.kind === 'expected') {
      const amount = formatTokenAmount(t.value, STABLECOIN_DECIMALS);
      if (entry.kind === 'post_cancel') {
        const late: LatePaymentDetectedData = {
          ...common,
          contractAddress: t.contract,
          tokenSymbol: entry.expectedSymbol,
          amount,
          monitorState: entry.postCancel?.state ?? 'POST_CANCEL_24H',
        };
        await this.deliver(BlockchainWebhookEvents.LatePaymentDetected, late, entry.correlationId);
        return;
      }
      const detected: PaymentDetectedData = {
        ...common,
        contractAddress: t.contract,
        tokenSymbol: entry.expectedSymbol,
        amount,
      };
      await this.deliver(BlockchainWebhookEvents.PaymentDetected, detected, entry.correlationId);
      // Finalized when first seen (K5): the confirmation follows at once.
      const confirmed: PaymentConfirmedData = {
        paymentAddressId: entry.paymentAddressId,
        transactionId: entry.transactionId,
        txHash: t.txHash,
        eventIndex: t.logIndex,
        blockNumber: Number(t.blockNumber),
        confirmationCount: Number(finalized - t.blockNumber),
        confirmedAt: new Date(this.now()).toISOString(),
      };
      await this.deliver(BlockchainWebhookEvents.PaymentConfirmed, confirmed, entry.correlationId);
      return;
    }

    if (kind.kind === 'wrong_token') {
      const wrong: WrongTokenIncomingData = {
        ...common,
        expectedContractAddress: entry.expectedContract,
        actualContractAddress: t.contract,
        actualTokenSymbol: kind.symbol,
        amount: formatTokenAmount(t.value, STABLECOIN_DECIMALS),
      };
      await this.deliver(BlockchainWebhookEvents.WrongTokenIncoming, wrong, entry.correlationId);
      return;
    }

    const spam: SpamTokenIncomingData = {
      ...common,
      expectedContractAddress: entry.expectedContract,
      actualContractAddress: t.contract,
      amount: formatTokenAmount(t.value, SPAM_TOKEN_DECIMALS),
    };
    await this.deliver(BlockchainWebhookEvents.SpamTokenIncoming, spam, entry.correlationId);
  }

  private async deliver<TData>(
    event: (typeof BlockchainWebhookEvents)[keyof typeof BlockchainWebhookEvents],
    data: TData,
    correlationId: string,
  ): Promise<void> {
    await this.deps.send(event, data, correlationId);
    paymentEventsTotal.inc({ event });
  }

  /** Waits for the RPC budget the two `eth_getLogs` queries of a range will spend. */
  private async pace(from: bigint, to: bigint, addresses: number, queries: number): Promise<void> {
    const blocks = Number(to - from + 1n);
    const calls =
      Math.ceil(blocks / Math.max(1, this.deps.minProviderBlockRange)) *
      Math.max(1, Math.ceil(addresses / 1000)) *
      queries;
    await this.deps.pacer.acquire(calls * CU_COST.eth_getLogs);
  }
}

function recordFlow(
  entry: WatchEntry,
  key: string,
  block: bigint,
  symbol: StablecoinSymbol,
  delta: bigint,
): void {
  if (entry.scan.flowKeys.has(key)) return;
  entry.scan.flowKeys.add(key);
  entry.scan.flows.push({ key, block, symbol, delta });
}
