import express from 'express';
import { config } from './config/index.js';
import { logger } from './logger.js';
import { correlationMiddleware } from './api/middleware.js';
import { createRouter } from './api/routes.js';
import { WalletManager } from './wallet/WalletManager.js';
import { MonitorRegistry } from './monitor/MonitorRegistry.js';
import { TronGridClient } from './tron/TronGridClient.js';

const app = express();
const walletManager = new WalletManager();
const tronGridClient = new TronGridClient();
const monitorRegistry = new MonitorRegistry({
  client: tronGridClient,
  allowlist: { USDT: config.allowlist.USDT, USDC: config.allowlist.USDC },
  intervalMs: config.paymentPollingIntervalMs,
  minConfirmations: config.minConfirmations,
  pageLimit: config.monitorPageLimit,
  webhookEndpoints: config.webhookEndpoints,
});

// Middleware
app.use(express.json());
app.use(correlationMiddleware);

// Routes
app.use(createRouter({ walletManager, monitorRegistry }));

// Start server
const server = app.listen(config.port, '0.0.0.0', async () => {
  logger.info({ port: config.port, network: config.tronNetwork }, 'Blockchain sidecar listening');
  await walletManager.initialize();
});

// Graceful shutdown (09 §17.9)
async function shutdown(signal: string): Promise<void> {
  logger.info({ signal }, 'Graceful shutdown started');

  // 1. Stop accepting new connections
  server.close(() => {
    logger.info('HTTP server closed');
  });

  // 2. Stop active monitors
  await monitorRegistry.shutdown();

  // 3. Shutdown wallet manager
  await walletManager.shutdown();

  // 4. Force exit after timeout
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
