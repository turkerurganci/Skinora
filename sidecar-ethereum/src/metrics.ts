import client from 'prom-client';
import type { Request, Response } from 'express';

// prom-client keeps its default registry in module state; vitest's
// `singleFork` pool shares the process across test files, so every metric is
// get-or-create (same guard as the TRON service).
const REGISTRY_FLAG = Symbol.for('skinora_ethereum_metrics_registered');
type GlobalFlag = { [REGISTRY_FLAG]?: true };

function getOrCreateHistogram(
  config: client.HistogramConfiguration<string>,
): client.Histogram<string> {
  const existing = client.register.getSingleMetric(config.name) as
    | client.Histogram<string>
    | undefined;
  return existing ?? new client.Histogram<string>(config);
}

function getOrCreateCounter(config: client.CounterConfiguration<string>): client.Counter<string> {
  const existing = client.register.getSingleMetric(config.name) as
    | client.Counter<string>
    | undefined;
  return existing ?? new client.Counter<string>(config);
}

function getOrCreateGauge(config: client.GaugeConfiguration<string>): client.Gauge<string> {
  const existing = client.register.getSingleMetric(config.name) as client.Gauge<string> | undefined;
  return existing ?? new client.Gauge<string>(config);
}

if (!(globalThis as unknown as GlobalFlag)[REGISTRY_FLAG]) {
  client.collectDefaultMetrics({ prefix: 'skinora_ethereum_' });
  (globalThis as unknown as GlobalFlag)[REGISTRY_FLAG] = true;
}

/**
 * Every JSON-RPC call, per provider and method (08 §3.7.1 — the quota both
 * free plans are sized against is a request budget: Alchemy compute units,
 * Chainstack requests). `outcome`: ok | transport | rpc | limit | chain_mismatch.
 */
export const rpcRequestDuration = getOrCreateHistogram({
  name: 'skinora_ethereum_rpc_request_duration_seconds',
  help: 'Duration of Ethereum JSON-RPC requests',
  labelNames: ['provider', 'method', 'outcome'] as const,
  buckets: [0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10],
});

/** A call that failed on one provider and went on to the next (08 §3.7.8). */
export const rpcFailoverTotal = getOrCreateCounter({
  name: 'skinora_ethereum_rpc_failover_total',
  help: 'Ethereum JSON-RPC calls that failed over from one provider to the next',
  labelNames: ['from', 'method'] as const,
});

/** Number of the last finalized block the service read (08 §3.7.4). */
export const finalizedBlockNumber = getOrCreateGauge({
  name: 'skinora_ethereum_finalized_block_number',
  help: 'Number of the last finalized Ethereum block read',
});

// Held on globalThis, not in a module variable: under vitest's shared process
// a later import of this file gets the gauge created by the first one, whose
// collect() would otherwise read the first module instance's variable.
const AGE_SOURCE = Symbol.for('skinora_ethereum_finalized_age_source');
type AgeHolder = { [AGE_SOURCE]?: () => number | null };

/**
 * Seconds since the last finalized block was produced — the finality-lag
 * signal (08 §3.7.8: normal 12.8–18.8 min, alarm past the stall threshold).
 * Computed at scrape time from the block's own timestamp, so a service that
 * can no longer read the head shows a growing age rather than a frozen one.
 */
export const finalizedBlockAgeSeconds = getOrCreateGauge({
  name: 'skinora_ethereum_finalized_block_age_seconds',
  help: 'Seconds since the timestamp of the last finalized Ethereum block read',
  collect() {
    const age = (globalThis as unknown as AgeHolder)[AGE_SOURCE]?.();
    if (age !== null && age !== undefined) this.set(age);
  },
});

/** Where the age gauge reads from at scrape time (the FinalityTracker). */
export function setFinalizedAgeSource(source: () => number | null): void {
  (globalThis as unknown as AgeHolder)[AGE_SOURCE] = source;
}

/**
 * The stall threshold the service's own /health applies (ETHEREUM_FINALIZED_STALL_SECONDS),
 * published so the ethereum-finalized-stalled alert divides by the same number
 * instead of repeating it.
 */
export const finalizedStallThresholdSeconds = getOrCreateGauge({
  name: 'skinora_ethereum_finalized_stall_threshold_seconds',
  help: 'Finalized-block age above which the Ethereum service reports unhealthy',
});

/** Reads of the finalized head that no provider could answer. */
export const finalizedReadFailuresTotal = getOrCreateCounter({
  name: 'skinora_ethereum_finalized_read_failures_total',
  help: 'Finalized-head reads that failed on every configured provider',
});

// ---- Outbound transfers (T152 — 08 §3.7.6, §3.7.7) -------------------------

/** Send requests by flow and outcome: signed | replayed | deferred | refused | error. */
export const transfersTotal = getOrCreateCounter({
  name: 'skinora_ethereum_transfers_total',
  help: 'Ethereum send requests by flow and outcome',
  labelNames: ['flow', 'outcome'] as const,
});

const TRANSFER_SOURCE = Symbol.for('skinora_ethereum_transfer_wait_source');
export interface TransferWaitSource {
  feeCapWaitSeconds: () => number;
  unminedSeconds: () => number;
  gasWaitSeconds: () => number;
  nonceConflicts: () => number;
  /** Accounts whose chain nonce is ahead of the journal — signing stopped for them. */
  journalBehindChain: () => number;
  /** Accounts whose configured acknowledgement is above the chain nonce — ignored. */
  journalAckAheadOfChain: () => number;
}
type TransferHolder = { [TRANSFER_SOURCE]?: TransferWaitSource };

/** Where the transfer gauges read from at scrape time (TransferService + FeeWaitTracker). */
export function setTransferWaitSource(source: TransferWaitSource): void {
  (globalThis as unknown as TransferHolder)[TRANSFER_SOURCE] = source;
}

/**
 * Longest current wait, seconds, by kind: `fee_cap` — a sweep / payout / cold
 * transfer not signed because the fee is above the cap or the ETH price is
 * missing (08 §3.7.7, 3-hour alarm); `unmined` — a signed transfer not yet in a
 * block; `gas` — a sender without ETH for gas (fuel, T153).
 */
export const transferWaitSeconds = getOrCreateGauge({
  name: 'skinora_ethereum_transfer_wait_seconds',
  help: 'Longest current wait of an Ethereum send, by kind (fee_cap | unmined | gas)',
  labelNames: ['kind'] as const,
  collect() {
    const source = (globalThis as unknown as TransferHolder)[TRANSFER_SOURCE];
    if (!source) return;
    this.set({ kind: 'fee_cap' }, source.feeCapWaitSeconds());
    this.set({ kind: 'unmined' }, source.unminedSeconds());
    this.set({ kind: 'gas' }, source.gasWaitSeconds());
  },
});

/**
 * The wait the ethereum-transfer-wait alert divides by
 * (ETHEREUM_FEE_CAP_WAIT_ALARM_SECONDS, 10,800 — parity with the backend's
 * blockchain.ethereum.fee_cap_wait_alarm_minutes 180, DEPLOY_RUNBOOK §D).
 */
export const transferWaitAlarmSeconds = getOrCreateGauge({
  name: 'skinora_ethereum_transfer_wait_alarm_seconds',
  help: 'Wait above which an Ethereum send raises the fee-cap wait alarm',
});

/** Journalled sends whose nonce another transaction used (key compromise / manual send). */
export const nonceConflicts = getOrCreateGauge({
  name: 'skinora_ethereum_outgoing_nonce_conflicts',
  help: 'Journalled Ethereum sends whose nonce was used by a transaction the service did not sign',
  collect() {
    const source = (globalThis as unknown as TransferHolder)[TRANSFER_SOURCE];
    if (source) this.set(source.nonceConflicts());
  },
});

/**
 * Accounts for which the last pre-signing check found the chain's nonce
 * (pending included) ahead of what the journal knows — the service signs
 * nothing new for them (08 §3.7.6 "Günlük zincirin gerisinde", T152
 * düzeltme turu 2). Alert `ethereum-journal-behind-chain`.
 */
export const journalBehindChain = getOrCreateGauge({
  name: 'skinora_ethereum_journal_behind_chain',
  help: 'Ethereum send accounts whose chain nonce is ahead of the transaction journal (signing stopped)',
  collect() {
    const source = (globalThis as unknown as TransferHolder)[TRANSFER_SOURCE];
    if (source) this.set(source.journalBehindChain());
  },
});

/**
 * Accounts whose `ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES` value is above the
 * account's nonce on chain (pending included) — a typo or stale value the
 * service ignores (T152 doğrulaması, yönetici kararı (a)). Alert
 * `ethereum-journal-ack-ahead-of-chain`, beside `ethereum-journal-behind-chain`.
 */
export const journalAckAheadOfChain = getOrCreateGauge({
  name: 'skinora_ethereum_journal_ack_ahead_of_chain',
  help: 'Ethereum send accounts whose journal acknowledgement is above the chain nonce (ignored)',
  collect() {
    const source = (globalThis as unknown as TransferHolder)[TRANSFER_SOURCE];
    if (source) this.set(source.journalAckAheadOfChain());
  },
});

// --- T151 — payment monitor, reconciliation, USDT status (08 §3.7.4, §3.7.1) ---

/** Deposits watched, by registry (`active` | `post_cancel`). */
export const watchedDeposits = getOrCreateGauge({
  name: 'skinora_ethereum_watched_deposits',
  help: 'Ethereum deposit addresses the payment monitor watches',
  labelNames: ['kind'] as const,
});

/** Highest finalized block whose logs the monitor has processed for every caught-up deposit. */
export const monitorScannedBlock = getOrCreateGauge({
  name: 'skinora_ethereum_monitor_scanned_block',
  help: 'Highest finalized Ethereum block the payment monitor has fully processed',
});

/** Deposits still being read back from their start block (restart / late arming). */
export const monitorCatchUpDeposits = getOrCreateGauge({
  name: 'skinora_ethereum_monitor_catch_up_deposits',
  help: 'Ethereum deposits whose past blocks the monitor is still reading',
});

/** Events delivered to the backend, by webhook event name. */
export const paymentEventsTotal = getOrCreateCounter({
  name: 'skinora_ethereum_payment_events_total',
  help: 'Payment monitor webhook events delivered to the backend',
  labelNames: ['event'] as const,
});

/**
 * Ranges read with the allowlisted tokens only because the provider refused
 * `eth_getLogs` without an address — spam to the deposits is not visible there.
 */
export const spamScanUnavailableTotal = getOrCreateCounter({
  name: 'skinora_ethereum_monitor_spam_scan_unavailable_total',
  help: 'Block ranges whose spam-token sweep the provider refused (address-less eth_getLogs)',
});

/** Block ranges whose scan failed (RPC or retryable webhook failure) and is retried. */
export const monitorScanFailuresTotal = getOrCreateCounter({
  name: 'skinora_ethereum_monitor_scan_failures_total',
  help: 'Payment monitor block ranges that failed and will be read again',
});

/**
 * A deposit's token balance moved differently from the transfers the monitor
 * saw (a log the provider left out, 08 §3.7.8) — admin alarm
 * (`ethereum-deposit-balance-mismatch`).
 */
export const reconciliationMismatchTotal = getOrCreateCounter({
  name: 'skinora_ethereum_reconciliation_mismatch_total',
  help: 'Ethereum deposits whose token balance does not match the observed transfers',
  labelNames: ['asset'] as const,
});

/** Native ETH that reached a deposit and is not recorded fuel — admin alarm (`ethereum-deposit-native-eth`). */
export const nativeEthReceivedTotal = getOrCreateCounter({
  name: 'skinora_ethereum_native_eth_received_total',
  help: 'Ethereum deposits that received native ETH not sent by the fuel wallet',
});

/** Reconciliation rounds skipped, by reason (`finalized_stale`, `read_failed`, `no_finalized`). */
export const reconciliationSkippedTotal = getOrCreateCounter({
  name: 'skinora_ethereum_reconciliation_skipped_total',
  help: 'Ethereum balance reconciliation rounds that did not run to completion',
  labelNames: ['reason'] as const,
});

/** Unix seconds of the last completed reconciliation round (process start until the first). */
export const reconciliationLastSuccess = getOrCreateGauge({
  name: 'skinora_ethereum_reconciliation_last_success_timestamp_seconds',
  help: 'Unix time of the last completed Ethereum balance reconciliation',
});

/** The configured reconciliation interval — the stale alarm divides by it. */
export const reconciliationIntervalSeconds = getOrCreateGauge({
  name: 'skinora_ethereum_reconciliation_interval_seconds',
  help: 'Configured interval of the Ethereum balance reconciliation',
});

/** Seconds the last reconciliation round took (spread by the rate pacer). */
export const reconciliationDurationSeconds = getOrCreateGauge({
  name: 'skinora_ethereum_reconciliation_duration_seconds',
  help: 'Duration of the last Ethereum balance reconciliation round',
});

/**
 * 1 when the USDT contract is usable (fee switch off, not paused, not
 * deprecated), 0 otherwise — admin alarm (`ethereum-usdt-status`). Not set
 * while no USDT contract is configured (a testnet before T159).
 */
export const usdtStatusOk = getOrCreateGauge({
  name: 'skinora_ethereum_usdt_status_ok',
  help: 'Whether the configured USDT contract is usable for payments (1) or not (0)',
});

export function metricsHandler(_req: Request, res: Response): void {
  client.register
    .metrics()
    .then((metrics) => {
      res.set('Content-Type', client.register.contentType);
      res.end(metrics);
    })
    .catch((err) => {
      res.status(500).end(String(err));
    });
}
