import client from 'prom-client';
import type { Request, Response } from 'express';

// Collect default metrics (CPU, memory, event loop, GC)
client.collectDefaultMetrics({ prefix: 'skinora_steam_' });

// Custom metrics — 05 §9.2
export const httpRequestDuration = new client.Histogram({
  name: 'skinora_steam_http_request_duration_seconds',
  help: 'Duration of HTTP requests in seconds',
  labelNames: ['method', 'route', 'status_code'] as const,
  buckets: [0.01, 0.05, 0.1, 0.5, 1, 2, 5],
});

export const httpRequestsTotal = new client.Counter({
  name: 'skinora_steam_http_requests_total',
  help: 'Total number of HTTP requests',
  labelNames: ['method', 'route', 'status_code'] as const,
});

export const steamApiRequestDuration = new client.Histogram({
  name: 'skinora_steam_api_request_duration_seconds',
  help: 'Duration of Steam API requests',
  labelNames: ['endpoint', 'status'] as const,
  buckets: [0.1, 0.5, 1, 2, 5, 10],
});

export const steamApiErrorsTotal = new client.Counter({
  name: 'skinora_steam_api_errors_total',
  help: 'Total Steam API errors',
  labelNames: ['endpoint', 'error_type'] as const,
});

export const activeBotSessions = new client.Gauge({
  name: 'skinora_steam_active_bot_sessions',
  help: 'Number of active bot sessions',
});

export const tradeOffersTotal = new client.Counter({
  name: 'skinora_steam_trade_offers_total',
  help: 'Total trade offers processed',
  labelNames: ['direction', 'status'] as const,
});

/**
 * Inventory cache lookups by outcome (T120 — 08 §2.3).
 *
 * `bypass` is a deliberate cache skip requested via `?refresh=true`, NOT a
 * miss. The two must stay distinguishable: a rising `bypass` share is the
 * delivery-verification read load, and it is what consumes the Community
 * queue budget (08 §2.6) — conflating it with `miss` would hide the cause of
 * queue saturation behind an apparent cache-tuning problem.
 */
export const inventoryCacheTotal = new client.Counter({
  name: 'skinora_steam_inventory_cache_total',
  help: 'Steam inventory cache lookups by outcome (hit / miss / bypass)',
  labelNames: ['result'] as const,
});

/**
 * Pending task depth per rate-limited Steam queue (T120 — 08 §2.6).
 *
 * The Web API and Community endpoints run in separate queues; a persistently
 * non-zero `community` depth is the practical ceiling on concurrent delivery
 * verifications (each verification costs two reads — seller + buyer), which
 * 10 §4 records as an MVP capacity limit.
 */
export const rateLimitedQueueDepth = new client.Gauge({
  name: 'skinora_steam_queue_depth',
  help: 'Pending tasks in each rate-limited Steam request queue',
  labelNames: ['queue'] as const,
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
