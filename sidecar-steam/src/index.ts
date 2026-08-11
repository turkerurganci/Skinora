import express from 'express';
import Redis from 'ioredis';
import { config } from './config/index.js';
import { logger } from './logger.js';
import { correlationMiddleware } from './api/middleware.js';
import { buildRouter } from './api/routes.js';
import { BotManager } from './bot/BotManager.js';
import { BotHealthCheck } from './bot/BotHealthCheck.js';
import { TradeOfferService } from './trade/TradeOfferService.js';
import { TradeOfferMonitor } from './trade/TradeOfferMonitor.js';
import { InventoryService, SteamCommunityInventoryFetcher } from './trade/InventoryService.js';
import { TradeHoldService } from './trade/TradeHoldService.js';
import { RateLimitedQueue } from './queue/RateLimitedQueue.js';
import { inventoryCacheTotal, rateLimitedQueueDepth } from './metrics.js';
import {
  InMemoryInventoryCache,
  RedisInventoryCache,
  type InventoryCache,
} from './cache/InventoryCache.js';

const app = express();
const botManager = new BotManager();
const botHealthCheck = new BotHealthCheck(botManager);
const tradeOfferService = new TradeOfferService(botManager);
const tradeOfferMonitor = new TradeOfferMonitor(botManager);

// T120 — queue depth is published from the wiring layer so the queue itself
// stays free of the Prometheus import (the prom-client registry is
// process-global and must be touched from exactly one place).
const observeQueueDepth =
  (queue: string) =>
  (depth: number): void => {
    rateLimitedQueueDepth.set({ queue }, depth);
  };

// Trade-hold / MA check (08 §2.2) — Steam Web API call rate-limited to the
// documented 1 req/s budget shared with other Web API usage.
const steamWebApiQueue = new RateLimitedQueue(
  config.steamWebApiRequestsPerSecond,
  1_000,
  observeQueueDepth('webapi'),
);
const tradeHoldService = new TradeHoldService(config.steamApiKey, steamWebApiQueue);

// T120 — the Steam Community inventory endpoint gets its OWN queue (08 §2.6).
// Its limit is far tighter than the Web API's and it is now on the critical
// path: inventory reads are the only means of verifying delivery (02 §9.2), so
// they must not queue behind trade-hold checks governed by a different budget.
const steamCommunityQueue = new RateLimitedQueue(
  config.steamCommunityRequestsPerMinute,
  60_000,
  observeQueueDepth('community'),
);

// T67 — inventory service wires:
//   * Anonymous SteamCommunity instance (read-only; profile auth not required)
//   * Redis when REDIS_URL is set, else in-memory fallback (dev/test friendly).
//   * The Community queue above (T120).
const inventoryCache: InventoryCache = config.redisUrl
  ? new RedisInventoryCache(new Redis(config.redisUrl))
  : new InMemoryInventoryCache();
const inventoryService = new InventoryService(
  new SteamCommunityInventoryFetcher(),
  inventoryCache,
  steamCommunityQueue,
  (result) => inventoryCacheTotal.inc({ result }),
);

// Middleware
app.use(express.json());
app.use(correlationMiddleware);

// Routes
app.use(buildRouter({ botManager, tradeOfferService, inventoryService, tradeHoldService }));

// Start server
const server = app.listen(config.port, '0.0.0.0', async () => {
  logger.info({ port: config.port }, 'Steam sidecar listening');
  await botManager.initialize();
  // Attach AFTER initialize so the bot pool is populated; the underlying
  // TradeOfferManager instances exist from BotSession construction and queue
  // events until the polling cycle kicks in post-webSession.
  tradeOfferMonitor.start();
  botHealthCheck.start();
});

// Graceful shutdown (09 §17.9)
async function shutdown(signal: string): Promise<void> {
  logger.info({ signal }, 'Graceful shutdown started');

  // 1. Stop accepting new connections
  server.close(() => {
    logger.info('HTTP server closed');
  });

  // 2. Stop health check loop and shutdown bot sessions
  botHealthCheck.stop();
  await botManager.shutdown();

  // 3. Force exit after timeout
  const forceTimer = setTimeout(() => {
    logger.error('Forced shutdown — timeout exceeded');
    process.exit(1);
  }, config.shutdownTimeoutMs);
  forceTimer.unref();

  logger.info('Graceful shutdown complete');
  process.exit(0);
}

process.on('SIGTERM', () => shutdown('SIGTERM'));
process.on('SIGINT', () => shutdown('SIGINT'));
