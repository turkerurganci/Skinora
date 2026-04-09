import client from 'prom-client';
import type { Request, Response } from 'express';

// Collect default metrics (CPU, memory, event loop, GC)
client.collectDefaultMetrics({ prefix: 'skinora_blockchain_' });

// Custom metrics — 05 §9.2
export const httpRequestDuration = new client.Histogram({
  name: 'skinora_blockchain_http_request_duration_seconds',
  help: 'Duration of HTTP requests in seconds',
  labelNames: ['method', 'route', 'status_code'] as const,
  buckets: [0.01, 0.05, 0.1, 0.5, 1, 2, 5],
});

export const httpRequestsTotal = new client.Counter({
  name: 'skinora_blockchain_http_requests_total',
  help: 'Total number of HTTP requests',
  labelNames: ['method', 'route', 'status_code'] as const,
});

export const tronApiRequestDuration = new client.Histogram({
  name: 'skinora_blockchain_tron_api_request_duration_seconds',
  help: 'Duration of TronGrid API requests',
  labelNames: ['endpoint', 'status'] as const,
  buckets: [0.1, 0.5, 1, 2, 5, 10],
});

export const tronApiErrorsTotal = new client.Counter({
  name: 'skinora_blockchain_tron_api_errors_total',
  help: 'Total TronGrid API errors',
  labelNames: ['endpoint', 'error_type'] as const,
});

export const activeMonitors = new client.Gauge({
  name: 'skinora_blockchain_active_monitors',
  help: 'Number of active payment monitors',
});

export const transfersTotal = new client.Counter({
  name: 'skinora_blockchain_transfers_total',
  help: 'Total blockchain transfers',
  labelNames: ['type', 'status'] as const,
});

export const hotWalletBalance = new client.Gauge({
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
