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
import {
  InventoryService,
  SteamCommunityInventoryFetcher,
} from './trade/InventoryService.js';
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

// T67 — inventory service wires:
//   * Anonymous SteamCommunity instance (read-only; profile auth not required)
//   * Redis when REDIS_URL is set, else in-memory fallback (dev/test friendly).
const inventoryCache: InventoryCache = config.redisUrl
  ? new RedisInventoryCache(new Redis(config.redisUrl))
  : new InMemoryInventoryCache();
const inventoryService = new InventoryService(
  new SteamCommunityInventoryFetcher(),
  inventoryCache,
);

// Middleware
app.use(express.json());
app.use(correlationMiddleware);

// Routes
app.use(buildRouter({ botManager, tradeOfferService, inventoryService }));

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
