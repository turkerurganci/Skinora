import { logger } from '../logger.js';
import {
  monitorCatchUpDeposits,
  monitorHistoryDeposits,
  monitorScanFailuresTotal,
  monitorScannedBlock,
  paymentEventsTotal,
  spamScanUnavailableTotal,
} from '../metrics.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import { RpcError, type EthereumRpcClient, type RpcLog } from '../rpc/RpcClient.js';
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
import type { DepositRegistry, ScanState, WatchEntry } from './DepositRegistry.js';
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
 *
 * <para>
 * <b>T158 (h) — the persisted start block wins.</b> A deposit the backend
 * arms with `watchFromBlock` (its `PaymentAddress.MonitorFromBlock`, the
 * finalized head when the address was derived) is read from the block after
 * it with a zero baseline, whatever the restart or the outage.
 * </para>
 *
 * <para>
 * <b>Two lanes (T158 düzeltme turu, yönetici kararı).</b> After a restart the
 * backend re-arms every deposit from the block it was derived at — up to 30
 * days back for a post-cancel deposit. Read oldest-first in one lane, that
 * history held a fresh payment back until it was done (measured: ~1.8 h behind
 * a 30-day deposit → payment timeout → wrong cancel). So each deposit's blocks
 * are split:
 * <list type="bullet">
 *   <item><b>live lane</b> ({@link ScanState.scannedThrough}) — from the block
 *     the pre-T158 rules above would start it at (the read-back floor of a
 *     re-armed deposit; a fresh deposit's own start block), never earlier than
 *     its start block. Read every tick with the normal catch-up budget (and
 *     within {@link LIVE_TICK_SHARE} of the tick), the gaps that fit the
 *     budget first: a fresh payment is reported as fast as before T158
 *     however long the history behind it is;</item>
 *   <item><b>history lane</b> ({@link ScanState.history}) — from the start
 *     block up to where the live lane begins, read in the background with what
 *     is left of the tick's block budget and only within the first
 *     {@link HISTORY_TICK_SHARE} of the tick, so it never makes the next head
 *     read wait.</item>
 * </list>
 * Both lanes deliver through the same `(txHash, logIndex)` set (no double
 * report) and record the same flows; the zero baseline stays at the start
 * block, and the reconciliation compares a deposit only once its history is
 * read ({@link contiguousThrough}).
 * </para>
 */

/**
 * Share of the tick interval the live lane's catch-up may run for: the tick
 * then ends before the next one is due, so the head is read every tick (a
 * catch-up that overran made the next head read wait, T158 düzeltme turu).
 */
export const LIVE_TICK_SHARE = 0.9;

/**
 * Share of the tick interval after which the history lane stops: whatever
 * the live lane leaves of the first half. The other half of the rate budget
 * stays free for the hourly reconciliation and the backend's balance requests.
 */
export const HISTORY_TICK_SHARE = 0.5;

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
    const startedAt = this.now();
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
      this.extendToEarlierStarts(finalized);
      if (finalized > this.cursor) await this.scanHead(finalized);
      const used = await this.catchUp(
        finalized,
        startedAt + this.deps.tickIntervalMs * LIVE_TICK_SHARE,
      );
      await this.readHistory(
        finalized,
        BigInt(this.deps.catchUpBlocksPerTick) - used,
        startedAt + this.deps.tickIntervalMs * HISTORY_TICK_SHARE,
      );
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
      monitorHistoryDeposits.set(this.withHistory().length);
    }
  }

  private behind(): WatchEntry[] {
    const cursor = this.cursor;
    if (cursor === null) return [];
    return this.deps.registry
      .entries()
      .filter((e) => e.scan.scannedThrough !== null && e.scan.scannedThrough < cursor);
  }

  private withHistory(): WatchEntry[] {
    return this.deps.registry.entries().filter((e) => e.scan.history !== null);
  }

  private async pickUpNewEntries(finalized: bigint): Promise<void> {
    const lookback = BigInt(this.deps.restartLookbackBlocks);
    const floor = (b: bigint) => (b > lookback ? b - lookback : 0n);
    for (const entry of this.deps.registry.entries()) {
      const scan = entry.scan;
      if (scan.scannedThrough !== null) continue;
      const rearmed = entry.armedAt - this.bootAt < this.deps.rearmWindowMs;
      if (entry.watchFromBlock !== null) {
        // T158 (h) — the backend persisted the finalized head of the moment
        // the address was derived: nothing can have reached it at or before
        // that block, so it is read from the next one on — however long this
        // service was down, past any restart read-back — and its balance
        // there is zero by construction (the reconciliation compares from it).
        const start = entry.watchFromBlock < finalized ? entry.watchFromBlock : finalized;
        // Two lanes: the live lane starts where the pre-T158 rules would
        // (never before the start block); the rest is the history lane.
        const near = floor(rearmed ? (this.bootBlock ?? finalized) : (this.cursor ?? finalized));
        const live = start > near ? start : near;
        scan.scannedThrough = live;
        scan.coveredFrom = start;
        scan.history = start < live ? { next: start + 1n, through: live } : null;
        scan.assumeEmptyStart = true;
        scan.baseline = { block: start, eth: 0n, tokens: this.zeroTokens() };
        logger.info(
          {
            address: entry.address,
            kind: entry.kind,
            fromBlock: String(start + 1n),
            liveFromBlock: String(live + 1n),
          },
          'Ethereum deposit picked up — read from the block the backend persisted',
        );
        continue;
      }
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
        scan.coveredFrom = start;
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
        scan.coveredFrom = scan.scannedThrough;
        scan.assumeEmptyStart = false;
        scan.baseline = null;
        logger.info(
          { address: entry.address, kind: entry.kind, fromBlock: String(scan.scannedThrough + 1n) },
          'Ethereum deposit picked up — reading back its past blocks',
        );
      }
    }
  }

  /**
   * A re-start that brought an EARLIER start block than the one the address
   * is read from (it was set up without one; DepositRegistry keeps the
   * earliest): the history lane is extended down to it. The zero baseline
   * moves with it unless a reconciliation reading already replaced it — a
   * reading stays a valid baseline, and the flows before it are gone.
   */
  private extendToEarlierStarts(finalized: bigint): void {
    for (const entry of this.deps.registry.entries()) {
      const scan = entry.scan;
      if (scan.scannedThrough === null || scan.coveredFrom === null) continue;
      if (entry.watchFromBlock === null) continue;
      const start = entry.watchFromBlock < finalized ? entry.watchFromBlock : finalized;
      if (start >= scan.coveredFrom) continue;
      const previous = scan.coveredFrom;
      scan.history = { next: start + 1n, through: scan.history?.through ?? previous };
      scan.coveredFrom = start;
      if (scan.baseline === null || (scan.assumeEmptyStart && scan.baseline.block === previous)) {
        scan.assumeEmptyStart = true;
        scan.baseline = { block: start, eth: 0n, tokens: this.zeroTokens() };
      }
      logger.info(
        {
          address: entry.address,
          fromBlock: String(start + 1n),
          previousFromBlock: String(previous + 1n),
        },
        'Ethereum deposit start block moved earlier — its history is read down to it',
      );
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

  /**
   * The live lane's catch-up: deposits whose live progress is behind the
   * cursor (at most the restart read-back). The gaps that fit the tick's
   * remaining block budget go first — a fresh deposit's few blocks are never
   * queued behind a re-armed deposit's day of read-back — then the rest
   * oldest-first. Read in chunks of about a second of the rate budget and
   * stopped at <c>deadline</c>, so the tick ends before the next one is due
   * and the head is read every tick — one chunk always runs, so a slow head
   * read cannot stall the catch-up for good. Returns the blocks spent.
   */
  private async catchUp(finalized: bigint, deadline: number): Promise<bigint> {
    const total = BigInt(this.deps.catchUpBlocksPerTick);
    const chunk = this.chunkBlocks(this.behind().length);
    let budget = total;
    let first = true;
    while (budget > 0n && (first || this.now() < deadline)) {
      first = false;
      const behind = this.behind();
      if (behind.length === 0) break;
      const cursor = this.cursor!;
      const short = behind.filter((e) => cursor - e.scan.scannedThrough! <= budget);
      const candidates = short.length > 0 ? short : behind;
      const from =
        candidates.reduce(
          (m, e) => (e.scan.scannedThrough! < m ? e.scan.scannedThrough! : m),
          cursor,
        ) + 1n;
      const step = budget < chunk ? budget : chunk;
      const to = from + step - 1n < cursor ? from + step - 1n : cursor;
      const group = candidates.filter((e) => e.scan.scannedThrough! < to);
      await this.scanRange(from, to, group, finalized);
      for (const e of group) {
        if (e.scan.scannedThrough! < to) e.scan.scannedThrough = to;
      }
      budget -= to - from + 1n;
    }
    return total - budget;
  }

  /**
   * The history lane: what lies between a deposit's start block and its live
   * lane, oldest first, with what is left of the tick's block budget and only
   * until <c>deadline</c> ({@link HISTORY_TICK_SHARE} of the tick) — the rest
   * of the tick's rate budget stays free for the next head read, the hourly
   * reconciliation and the backend's balance requests. When the live lane
   * has nothing to catch up one chunk always runs, so slow head reads cannot
   * stall the history for good.
   */
  private async readHistory(
    finalized: bigint,
    blocksLeft: bigint,
    deadline: number,
  ): Promise<void> {
    const pending = this.withHistory();
    if (pending.length === 0) return;
    const chunk = this.chunkBlocks(pending.length);
    let budget = blocksLeft;
    let first = this.behind().length === 0;
    while (budget > 0n && (first || this.now() < deadline)) {
      first = false;
      const open = this.withHistory();
      if (open.length === 0) return;
      const lead = open.reduce((m, e) => (e.scan.history!.next < m.scan.history!.next ? e : m));
      const from = lead.scan.history!.next;
      const end = lead.scan.history!.through;
      const step = budget < chunk ? budget : chunk;
      const to = from + step - 1n < end ? from + step - 1n : end;
      const group = open.filter((e) => e.scan.history!.next <= to);
      await this.scanRange(from, to, group, finalized);
      for (const e of group) advanceHistory(e, to);
      budget -= to - from + 1n;
    }
  }

  /** Blocks whose two `eth_getLogs` queries cost about one second of the rate budget (at least one provider range). */
  private chunkBlocks(addresses: number): bigint {
    const queries = this.tokenContracts().length > 0 ? 2 : 1;
    const costPerRange = Math.max(1, Math.ceil(addresses / 1000)) * queries * CU_COST.eth_getLogs;
    const ranges = Math.max(1, Math.floor(this.deps.pacer.cuPerSecond / costPerRange));
    const blocks = ranges * Math.max(1, this.deps.minProviderBlockRange);
    return BigInt(Math.min(blocks, Number.MAX_SAFE_INTEGER));
  }

  private tokenContracts(): string[] {
    return (['USDT', 'USDC'] as const).map((s) => this.deps.allowlist[s]).filter((c) => c !== '');
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
    const tokens = this.tokenContracts();

    await this.pace(from, to, recipients.length, tokens.length > 0 ? 2 : 1);
    const incoming = await this.incomingLogs(from, to, recipients, tokens);
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

  /**
   * Transfers TO the deposits from any contract — or, when the provider
   * refuses a query without an address (measured: Sepolia PublicNode answers
   * -32701 "Please specify an address"; dRPC, Tenderly and 1RPC answer it, T151
   * probe), from the allowlisted tokens only. The payment and the wrong token
   * are then still seen; only spam is not, for that range — logged and counted
   * (`skinora_ethereum_monitor_spam_scan_unavailable_total`). Transport and
   * size (limit) failures still fail the range — they say nothing about it.
   */
  private async incomingLogs(
    from: bigint,
    to: bigint,
    recipients: string[],
    tokens: string[],
  ): Promise<RpcLog[]> {
    try {
      return await this.deps.rpc.getLogs({
        topics: [TRANSFER_TOPIC, null, recipients],
        fromBlock: from,
        toBlock: to,
      });
    } catch (err) {
      if (!(err instanceof RpcError) || err.kind !== 'rpc' || tokens.length === 0) throw err;
      spamScanUnavailableTotal.inc();
      logger.warn(
        { provider: err.provider, reason: err.message, from: String(from), to: String(to) },
        'Provider refused eth_getLogs without an address — reading the allowlisted tokens only; spam is not visible for this range',
      );
      await this.deps.pacer.acquire(CU_COST.eth_getLogs);
      return this.deps.rpc.getLogs({
        address: tokens,
        topics: [TRANSFER_TOPIC, null, recipients],
        fromBlock: from,
        toBlock: to,
      });
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

  /**
   * Waits for the RPC budget the two `eth_getLogs` queries of a range will
   * spend — one reservation per call (RatePacer "one call, one reservation"),
   * so a concurrent balance request is not queued behind the whole range.
   */
  private async pace(from: bigint, to: bigint, addresses: number, queries: number): Promise<void> {
    const blocks = Number(to - from + 1n);
    const calls =
      Math.ceil(blocks / Math.max(1, this.deps.minProviderBlockRange)) *
      Math.max(1, Math.ceil(addresses / 1000)) *
      queries;
    for (let i = 0; i < calls; i++) await this.deps.pacer.acquire(CU_COST.eth_getLogs);
  }
}

/** Marks the history lane read through <c>to</c>; done once it meets the live lane. */
function advanceHistory(entry: WatchEntry, to: bigint): void {
  const scan: ScanState = entry.scan;
  const h = scan.history;
  if (h === null) return;
  if (h.next <= to) h.next = to + 1n;
  if (h.next > h.through) {
    scan.history = null;
    logger.info(
      { address: entry.address, through: String(h.through) },
      'Ethereum deposit history read — the reconciliation compares it from its start block',
    );
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
