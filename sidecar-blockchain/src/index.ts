import express from 'express';
import { config } from './config/index.js';
import { logger } from './logger.js';
import { correlationMiddleware } from './api/middleware.js';
import { createRouter } from './api/routes.js';
import { WalletManager } from './wallet/WalletManager.js';
import { MonitorRegistry } from './monitor/MonitorRegistry.js';
import { PostCancelMonitorRegistry } from './monitor/PostCancelMonitor.js';
import { TronGridClient } from './tron/TronGridClient.js';
import { TronTransferClient } from './tron/TronTransferClient.js';
import { TronDelegationClient } from './tron/TronDelegationClient.js';
import { EnergyDelegationService } from './wallet/EnergyDelegationService.js';
import { TransferService } from './transfer/TransferService.js';
import { RefundService } from './transfer/RefundService.js';
import { TronResourceClient } from './tron/TronResourceClient.js';
import { TrxPriceService } from './fee/TrxPriceService.js';
import { FeeEstimationService } from './fee/FeeEstimationService.js';

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
const postCancelMonitorRegistry = new PostCancelMonitorRegistry({
  client: tronGridClient,
  allowlist: { USDT: config.allowlist.USDT, USDC: config.allowlist.USDC },
  tickIntervalMs: config.postCancelTickIntervalMs,
  pageLimit: config.monitorPageLimit,
  webhookEndpoints: {
    latePaymentDetected: config.webhookEndpoints.latePaymentDetected,
    postCancelMonitorStateChanged: config.webhookEndpoints.postCancelMonitorStateChanged,
    wrongTokenIncoming: config.webhookEndpoints.wrongTokenIncoming,
    spamTokenIncoming: config.webhookEndpoints.spamTokenIncoming,
  },
  cadences: {
    POST_CANCEL_24H: config.postCancelCadence24hMs,
    POST_CANCEL_7D: config.postCancelCadence7dMs,
    POST_CANCEL_30D: config.postCancelCadence30dMs,
  },
  windows: {
    POST_CANCEL_24H: config.postCancelWindow24hMs,
    POST_CANCEL_7D: config.postCancelWindow7dMs,
    POST_CANCEL_30D: config.postCancelWindow30dMs,
  },
});
const tronTransferClient = new TronTransferClient(
  config.tronFullNodeUrl,
  config.tronSolidityUrl,
  config.tronApiKey,
);
const tronDelegationClient = new TronDelegationClient(config.tronFullNodeUrl, config.tronApiKey);
const energyDelegation = new EnergyDelegationService({
  client: tronDelegationClient,
  sweeperAddress: config.hotWalletAddress,
  sweeperPrivateKey: config.hotWalletPrivateKey,
  delegationAmountSun: config.sweepEnergyDelegationSun,
  fallbackAmountSun: config.sweepTrxFallbackSun,
});
const tokenContracts = { USDT: config.usdtContract, USDC: config.usdcContract };
const transferService = new TransferService({
  walletManager,
  client: tronTransferClient,
  tokenContracts,
  hotWalletAddress: config.hotWalletAddress,
  hotWalletPrivateKey: config.hotWalletPrivateKey,
  tokenDecimals: config.tokenDecimals,
  energyDelegation,
});
const refundService = new RefundService({
  walletManager,
  client: tronTransferClient,
  tokenContracts,
  tokenDecimals: config.tokenDecimals,
  energyDelegation,
});
const feeEstimationService = new FeeEstimationService({
  resourceClient: new TronResourceClient(config.tronFullNodeUrl, config.tronApiKey),
  priceService: new TrxPriceService(),
  tokenContracts,
  hotWalletAddress: config.hotWalletAddress,
  tokenDecimals: config.tokenDecimals,
});

// Middleware
app.use(express.json());
app.use(correlationMiddleware);

// Routes
app.use(
  createRouter({
    walletManager,
    monitorRegistry,
    postCancelMonitorRegistry,
    transferService,
    refundService,
    feeEstimationService,
  }),
);

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
  await postCancelMonitorRegistry.shutdown();

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
