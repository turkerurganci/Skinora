import client from 'prom-client';
import type { Request, Response } from 'express';

// prom-client stores its default registry in module-level state. Under
// vitest's `singleFork: true` pool, multiple test files share the process
// but each fresh import of this file tries to re-register the same metric
// names — prom-client throws on duplicates. Guarding registration with a
// globalThis-scoped Symbol makes the side effect idempotent across reloads
// while remaining a no-op in production (the symbol is set exactly once).

const REGISTRY_FLAG = Symbol.for('skinora_blockchain_metrics_registered');
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
  client.collectDefaultMetrics({ prefix: 'skinora_blockchain_' });
  (globalThis as unknown as GlobalFlag)[REGISTRY_FLAG] = true;
}

// Custom metrics — 05 §9.2
export const httpRequestDuration = getOrCreateHistogram({
  name: 'skinora_blockchain_http_request_duration_seconds',
  help: 'Duration of HTTP requests in seconds',
  labelNames: ['method', 'route', 'status_code'] as const,
  buckets: [0.01, 0.05, 0.1, 0.5, 1, 2, 5],
});

export const httpRequestsTotal = getOrCreateCounter({
  name: 'skinora_blockchain_http_requests_total',
  help: 'Total number of HTTP requests',
  labelNames: ['method', 'route', 'status_code'] as const,
});

export const tronApiRequestDuration = getOrCreateHistogram({
  name: 'skinora_blockchain_tron_api_request_duration_seconds',
  help: 'Duration of TronGrid API requests',
  labelNames: ['endpoint', 'status'] as const,
  buckets: [0.1, 0.5, 1, 2, 5, 10],
});

export const tronApiErrorsTotal = getOrCreateCounter({
  name: 'skinora_blockchain_tron_api_errors_total',
  help: 'Total TronGrid API errors',
  labelNames: ['endpoint', 'error_type'] as const,
});

// Write through `reportActiveMonitorCount` (monitor/activeMonitorGauge.ts), not
// directly: two registries feed this one label-free gauge and a bare `.set()`
// from either publishes a partial total.
export const activeMonitors = getOrCreateGauge({
  name: 'skinora_blockchain_active_monitors',
  help: 'Deposit addresses under monitoring — active payment (T71) plus post-cancel (T75)',
});

/**
 * Wrap a `fetch` so every call lands in {@link tronApiRequestDuration} — the
 * series whose rate the tron-quota-projection alert sums. TronGridClient
 * measures its own calls; this covers the fetch-based probes beside it
 * (fee estimate, delegation planning, transfer status), which spend the same
 * key's quota. Calls TronWeb builds internally (broadcasts) stay uncounted:
 * a few per outbound transfer, inside the alert's 20% headroom.
 */
export function timedTronFetch(fetchFn: typeof fetch, endpoint: string): typeof fetch {
  return (async (...args: Parameters<typeof fetch>) => {
    const stop = tronApiRequestDuration.startTimer({ endpoint });
    try {
      const response = await fetchFn(...args);
      stop({ status: response.ok ? 'ok' : 'error' });
      return response;
    } catch (err) {
      stop({ status: 'error' });
      throw err;
    }
  }) as typeof fetch;
}

/**
 * The TronGrid plan's daily request budget (TRONGRID_DAILY_REQUEST_BUDGET),
 * set once at startup. The tron-quota-projection alert divides the measured
 * request rate by it; TronGrid reports no budget of its own (08 §3.4).
 */
export const tronGridDailyRequestBudget = getOrCreateGauge({
  name: 'skinora_blockchain_trongrid_daily_request_budget',
  help: "Configured TronGrid daily request budget — the plan's quota the request rate is compared with",
});

/**
 * Publish the budget and return what was published. Refuses anything but a
 * positive whole number: an unparsable env value would publish NaN, the
 * alert's division would yield no series, and the rule would sit in NoData —
 * a watchman that never fires, which is the failure this alert exists to end.
 */
export function publishTronGridDailyRequestBudget(budget: number): number {
  if (!Number.isInteger(budget) || budget <= 0) {
    throw new Error(
      `TRONGRID_DAILY_REQUEST_BUDGET must be a positive whole number of requests per day, got ${budget}`,
    );
  }
  tronGridDailyRequestBudget.set(budget);
  return budget;
}

// ---- T170 — is the TRON chain progressing? ---------------------------------

/** Number of the last solid (irreversible) TRON block the service read. */
export const solidBlockNumber = getOrCreateGauge({
  name: 'skinora_blockchain_tron_solid_block_number',
  help: 'Number of the last solid TRON block read',
});

// Held on globalThis (not a module variable): under vitest's shared process a
// later import of this file gets the gauge created by the first one, whose
// collect() would otherwise read the first module instance's variable — the
// same arrangement as the Ethereum service's finalized-age gauge.
const SOLID_AGE_SOURCE = Symbol.for('skinora_blockchain_solid_age_source');
type SolidAgeHolder = { [SOLID_AGE_SOURCE]?: () => number | null };

/**
 * Seconds since the last solid TRON block was produced — computed at scrape
 * time from the block's own timestamp, so a service that can no longer read
 * the head (TronGrid down, key refused) shows a growing age rather than a
 * frozen one. Normal: ~60 s (19 blocks × 3 s behind the head). Alert
 * `tron-solid-block-stalled` divides it by
 * {@link solidBlockStallThresholdSeconds}.
 */
export const solidBlockAgeSeconds = getOrCreateGauge({
  name: 'skinora_blockchain_tron_solid_block_age_seconds',
  help: 'Seconds since the timestamp of the last solid TRON block read',
  collect() {
    const age = (globalThis as unknown as SolidAgeHolder)[SOLID_AGE_SOURCE]?.();
    if (age !== null && age !== undefined) this.set(age);
  },
});

/** Where the age gauge reads from at scrape time (the SolidBlockTracker). */
export function setSolidBlockAgeSource(source: () => number | null): void {
  (globalThis as unknown as SolidAgeHolder)[SOLID_AGE_SOURCE] = source;
}

/** TRON_SOLID_BLOCK_STALL_SECONDS, published so the alert divides by the service's own number. */
export const solidBlockStallThresholdSeconds = getOrCreateGauge({
  name: 'skinora_blockchain_tron_solid_block_stall_threshold_seconds',
  help: 'Solid-block age above which the TRON chain counts as not progressing',
});

/** Reads of the solid head that failed (the last value is kept; the age grows). */
export const solidBlockReadFailuresTotal = getOrCreateCounter({
  name: 'skinora_blockchain_tron_solid_block_read_failures_total',
  help: 'Reads of the current solid TRON block that failed',
});

/**
 * T170 (DEFERRED_BACKLOG admin-alert-consumers) — an Energy delegation the
 * signer could not take back after the deposit transfer ("stranded
 * delegation"): the stake stays lent to a deposit address until an admin
 * undelegates it by hand. Label-free, so the series exists at 0 from start-up
 * and its first increment is visible to `increase()`. Alert
 * `tron-energy-delegation-stranded`. Never moves while the hybrid stake is
 * dormant (ENERGY_STAKING_ENABLED=false — the F8 default).
 */
export const energyDelegationStrandedTotal = getOrCreateCounter({
  name: 'skinora_blockchain_energy_delegation_stranded_total',
  help: 'Energy delegations the TRON service could not undelegate (stranded — admin action needed)',
});

export const transfersTotal = getOrCreateCounter({
  name: 'skinora_blockchain_transfers_total',
  help: 'Total blockchain transfers',
  labelNames: ['type', 'status'] as const,
});

export const hotWalletBalance = getOrCreateGauge({
  name: 'skinora_blockchain_hot_wallet_balance',
  help: 'Hot wallet balance in token units',
  labelNames: ['token'] as const,
});

/**
 * T172b — deposit refunds the signer refused under the per-network 24-hour
 * deposit refund cap (DEPOSIT_REFUND_DAILY_LIMIT_USDT). Every refused attempt
 * counts (the backend asks again every minute while the row waits), so the
 * tron-deposit-refund-limit-refused alarm keeps firing while one waits.
 */
export const depositRefundLimitRefusalsTotal = getOrCreateCounter({
  name: 'skinora_blockchain_deposit_refund_limit_refusals_total',
  help: 'Deposit refunds the signer refused under the 24-hour deposit refund cap, by reason',
  labelNames: ['reason'] as const,
});

/** T172b — USDT (USDC 1:1) the signer counted for deposit refunds in the last 24 hours. */
export const depositRefundWindowUsdt = getOrCreateGauge({
  name: 'skinora_blockchain_deposit_refund_window_usdt',
  help: 'Deposit refunds signed in the last 24 hours, USDT (USDC counted 1:1)',
});

/**
 * T172b — one-time operator permits past the deposit refund cap (sahip kararı
 * 2026-10-23): created (by the CLI process — not this registry), seen (the
 * service first read it), used, restored (its transfer never left), expired
 * (unused), revoked.
 */
export const depositRefundPermitsTotal = getOrCreateCounter({
  name: 'skinora_blockchain_deposit_refund_permits_total',
  help: 'Deposit refund cap permits by event (seen | used | restored | expired | revoked)',
  labelNames: ['event'] as const,
});

/** T172b — the configured cap; no series while it is unset (every deposit refund then waits). */
export const depositRefundDailyLimitUsdt = getOrCreateGauge({
  name: 'skinora_blockchain_deposit_refund_daily_limit_usdt',
  help: 'Configured 24-hour deposit refund cap, USDT',
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
