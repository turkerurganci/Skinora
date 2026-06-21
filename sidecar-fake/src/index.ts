import { buildApp } from './app.js';
import { config } from './config.js';
import { logger } from './logger.js';
import { closePool } from './db.js';

const app = buildApp();

// Two listeners share one app — the backend reaches the Steam surface on 5100
// and the blockchain surface on 5200 (compose network aliases route both
// sidecar hostnames to this container).
const steamServer = app.listen(config.steamPort, '0.0.0.0', () => {
  logger.info({ port: config.steamPort }, 'Fake sidecar (steam surface) listening');
});
const blockchainServer = app.listen(config.blockchainPort, '0.0.0.0', () => {
  logger.info({ port: config.blockchainPort }, 'Fake sidecar (blockchain surface) listening');
});

async function shutdown(signal: string): Promise<void> {
  logger.info({ signal }, 'Graceful shutdown started');
  steamServer.close();
  blockchainServer.close();
  await closePool().catch(() => undefined);
  const forceTimer = setTimeout(() => process.exit(1), config.shutdownTimeoutMs);
  forceTimer.unref();
  logger.info('Graceful shutdown complete');
  process.exit(0);
}

process.on('SIGTERM', () => void shutdown('SIGTERM'));
process.on('SIGINT', () => void shutdown('SIGINT'));
