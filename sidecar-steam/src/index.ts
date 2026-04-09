import express from 'express';
import { config } from './config/index.js';
import { logger } from './logger.js';
import { correlationMiddleware } from './api/middleware.js';
import { router } from './api/routes.js';
import { BotManager } from './bot/BotManager.js';

const app = express();
const botManager = new BotManager();

// Middleware
app.use(express.json());
app.use(correlationMiddleware);

// Routes
app.use(router);

// Start server
const server = app.listen(config.port, '0.0.0.0', async () => {
  logger.info({ port: config.port }, 'Steam sidecar listening');
  await botManager.initialize();
});

// Graceful shutdown (09 §17.9)
async function shutdown(signal: string): Promise<void> {
  logger.info({ signal }, 'Graceful shutdown started');

  // 1. Stop accepting new connections
  server.close(() => {
    logger.info('HTTP server closed');
  });

  // 2. Shutdown bot sessions
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
