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
