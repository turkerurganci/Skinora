import express from 'express';
import Redis from 'ioredis';
import { config } from './config/index.js';
import { logger } from './logger.js';
import { correlationMiddleware } from './api/middleware.js';
import { buildRouter } from './api/routes.js';
import { InventoryService } from './trade/InventoryService.js';
import { HttpInventoryFetcher } from './trade/HttpInventoryFetcher.js';
import { TradeHoldService } from './trade/TradeHoldService.js';
import { LimitedAccountService } from './trade/LimitedAccountService.js';
import { RateLimitedQueue } from './queue/RateLimitedQueue.js';
import { CommunityHealthTracker } from './health/CommunityHealth.js';
import {
  inventoryCacheTotal,
  limitedAccountChecksTotal,
  rateLimitedQueueDepth,
} from './metrics.js';
import {
  InMemoryInventoryCache,
  RedisInventoryCache,
  type InventoryCache,
} from './cache/InventoryCache.js';
import {
  InMemoryLimitedAccountCache,
  RedisLimitedAccountCache,
  type LimitedAccountCache,
} from './cache/LimitedAccountCache.js';

// T133 — the sidecar is a READ-ONLY Steam proxy. It boots with no Steam
// account of any kind: the bot pool, its credentials and the trade offer
// send/monitor stack went with the custody layer (05 §3.2). Everything wired
// below is keyed on the platform's Steam Web API key or on anonymous
// Community reads, so a missing/empty STEAM_API_KEY degrades a single route
// (trade-hold → 503) instead of blocking startup.
const app = express();

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
// Tek Redis bağlantısı iki önbelleğe de hizmet eder. İstemci bilerek bir
// değişkene alındı: `new Redis(...)`'i her önbelleğin kendi üçlü ifadesinde
// kurmak ikinci bir bağlantı açardı ve bunu kimse fark etmezdi.
const redisClient = config.redisUrl ? new Redis(config.redisUrl) : null;
const inventoryCache: InventoryCache = redisClient
  ? new RedisInventoryCache(redisClient)
  : new InMemoryInventoryCache();
const inventoryService = new InventoryService(
  // F2 — `steamcommunity` paketi yerine doğrudan HTTP: paketin taşıdığı
  // eski `request` yığını Steam tarafından 429 ile reddediliyordu, aynı
  // konteynerden düz fetch 200 dönüyordu (`UITour-InventoryClientRefusedBySteam`).
  new HttpInventoryFetcher(),
  inventoryCache,
  steamCommunityQueue,
  (result) => inventoryCacheTotal.inc({ result }),
);

// Limited-account kontrolü (08 §2.2a) — Steam Community profil XML'i, yani
// envanter okumalarıyla AYNI kotayı paylaşır ve bu yüzden aynı kuyruğa girer.
// Önbellek yalnız doğrulanmış TEMİZ sonucu tutar; kısıtlı ve arıza cevapları
// her seferinde canlı okunur (bloke eden karar bayat veriden verilmez).
const limitedAccountCache: LimitedAccountCache = redisClient
  ? new RedisLimitedAccountCache(redisClient, config.steamLimitedAccountCacheTtlSeconds)
  : new InMemoryLimitedAccountCache(config.steamLimitedAccountCacheTtlSeconds * 1000);
const communityHealth = new CommunityHealthTracker();
const limitedAccountService = new LimitedAccountService({
  cache: limitedAccountCache,
  queue: steamCommunityQueue,
  sampleCount: config.steamLimitedAccountSamples,
  sampleDelayMs: config.steamLimitedAccountSampleDelayMs,
  deadlineMs: config.steamLimitedAccountDeadlineMs,
  onOutcome: (result) => {
    limitedAccountChecksTotal.inc({ result });
    // Pasif community sağlığı: bu çağrılar zaten yapılıyor, sonuçları
    // sayılınca community kesintisi ek istek harcamadan görünür olur.
    if (result === 'unreadable') communityHealth.recordFailure();
    else if (result !== 'cache_hit') communityHealth.recordSuccess();
  },
});

// Middleware
app.use(express.json());
app.use(correlationMiddleware);

// Routes
app.use(
  buildRouter({
    inventoryService,
    tradeHoldService,
    limitedAccountService,
    health: { communityHealth },
  }),
);

// Start server
const server = app.listen(config.port, '0.0.0.0', () => {
  logger.info({ port: config.port }, 'Steam sidecar listening');
});

// Graceful shutdown (09 §17.9)
function shutdown(signal: string): void {
  logger.info({ signal }, 'Graceful shutdown started');

  // Stop accepting new connections. Nothing else holds a long-lived resource:
  // the sidecar keeps no Steam session and the rate-limited queues are
  // in-process timers that die with it.
  server.close(() => {
    logger.info('HTTP server closed');
  });

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
