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

export const activeMonitors = getOrCreateGauge({
  name: 'skinora_blockchain_active_monitors',
  help: 'Number of active payment monitors',
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
