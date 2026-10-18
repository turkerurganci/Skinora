import { buildApp } from './app.js';
import { config } from './config.js';
import { logger } from './logger.js';
import { closePool } from './db.js';

// Three listeners — the backend reaches the Steam surface on 5100, the TRON
// blockchain surface on 5200 and (T155) the Ethereum surface on 5300; compose
// network aliases route all three sidecar hostnames to this container. Each
// listener has its own app because TRON and Ethereum share route paths
// (app.ts); the in-memory stores behind them are shared module state.
const steamServer = buildApp('STEAM').listen(config.steamPort, '0.0.0.0', () => {
  logger.info({ port: config.steamPort }, 'Fake sidecar (steam surface) listening');
});
const blockchainServer = buildApp('TRON').listen(config.blockchainPort, '0.0.0.0', () => {
  logger.info({ port: config.blockchainPort }, 'Fake sidecar (TRON surface) listening');
});
const ethereumServer = buildApp('ETHEREUM').listen(config.ethereumPort, '0.0.0.0', () => {
  logger.info({ port: config.ethereumPort }, 'Fake sidecar (Ethereum surface) listening');
});

async function shutdown(signal: string): Promise<void> {
  logger.info({ signal }, 'Graceful shutdown started');
  steamServer.close();
  blockchainServer.close();
  ethereumServer.close();
  await closePool().catch(() => undefined);
  const forceTimer = setTimeout(() => process.exit(1), config.shutdownTimeoutMs);
  forceTimer.unref();
  logger.info('Graceful shutdown complete');
  process.exit(0);
}

process.on('SIGTERM', () => void shutdown('SIGTERM'));
process.on('SIGINT', () => void shutdown('SIGINT'));
