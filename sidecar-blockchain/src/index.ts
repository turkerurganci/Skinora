import TronWeb from 'tronweb';
import express from 'express';
import { config } from './config/index.js';
import { InternalKeyError } from './config/internalKey.js';
import {
  SharedSecretError,
  loadSharedSecrets,
  type SharedSecrets,
} from './config/sharedSecrets.js';
import {
  SigningSecretError,
  loadSigningMaterial,
  type SigningMaterial,
} from './config/signingSecrets.js';
import { logger } from './logger.js';
import { configureWebhookSecret } from './webhook/WebhookClient.js';
import { publishTronGridDailyRequestBudget } from './metrics.js';
import { correlationMiddleware } from './api/middleware.js';
import { createRouter } from './api/routes.js';
import { WalletManager } from './wallet/WalletManager.js';
import { HdWalletService } from './wallet/HdWalletService.js';
import { MonitorRegistry } from './monitor/MonitorRegistry.js';
import { PostCancelMonitorRegistry } from './monitor/PostCancelMonitor.js';
import { TronGridClient } from './tron/TronGridClient.js';
import { TronTransferClient } from './tron/TronTransferClient.js';
import { TronDelegationClient } from './tron/TronDelegationClient.js';
import { EnergyDelegationService } from './wallet/EnergyDelegationService.js';
import { TransferService } from './transfer/TransferService.js';
import { RefundService } from './transfer/RefundService.js';
import { TransferVerifier } from './transfer/TransferVerifier.js';
import { TransferGuard, parseLimitUnits } from './transfer/TransferGuard.js';
import { DepositRefundLimit, openDepositRefundLedger } from './transfer/DepositRefundLimit.js';
import { DepositRefundPermitStore } from './transfer/DepositRefundPermits.js';
import { depositRefundPermitsTotal } from './metrics.js';
import { TronResourceClient } from './tron/TronResourceClient.js';
import { TrxPriceService } from './fee/TrxPriceService.js';
import { FeeEstimationService } from './fee/FeeEstimationService.js';
import { SolidBlockTracker } from './chain/SolidBlockTracker.js';

// T161 / T171a — fail closed before anything is built or listens: without its
// internal key (05 §3.4) and its webhook secret (05 §3.3a) the service does not
// start. T171a — both come from their Docker secret files, the same files the
// backend reads (config/sharedSecrets.ts); outside NODE_ENV=development|test a
// plain INTERNAL_KEY / WEBHOOK_SECRET variable stops the service. The error
// carries the code; the stack trace on stderr is the container's last word.
const sharedSecrets = startupSharedSecrets();
const internalKey = sharedSecrets.internalKey;
// WebhookClient signs with this secret; it is handed over here, never read from env.
configureWebhookSecret(sharedSecrets.webhookSecret);

function startupSharedSecrets(): SharedSecrets {
  try {
    return loadSharedSecrets(process.env);
  } catch (err) {
    const code =
      err instanceof InternalKeyError || err instanceof SharedSecretError
        ? err.code
        : 'STARTUP_FAILED';
    logger.fatal({ code }, `Blockchain sidecar refused to start: ${code}`);
    throw err;
  }
}

// T163 — the seed and the hot wallet key come from their Docker secret files
// (config/signingSecrets.ts); outside NODE_ENV=development|test a plain
// HD_WALLET_MNEMONIC / HOT_WALLET_PRIVATE_KEY variable stops the service. The
// startup line below names the SOURCE of each and the public address the key
// controls (never a value), so an operator can check a restore against the
// backup summary (DEPLOY_RUNBOOK §L).
const signingMaterial = startupSigningMaterial();

function startupSigningMaterial(): SigningMaterial {
  try {
    return loadSigningMaterial(process.env, config.hotWalletAddress);
  } catch (err) {
    const code = err instanceof SigningSecretError ? err.code : 'STARTUP_FAILED';
    logger.fatal({ code }, `Blockchain sidecar refused to start: ${code}`);
    throw err;
  }
}

logger.info(
  {
    hdMnemonicSource: signingMaterial.sources.hdMnemonic,
    hotWalletKeySource: signingMaterial.sources.hotWalletKey,
    hotWalletAddressFromKey: signingMaterial.hotWalletAddressFromKey,
    internalKeySource: sharedSecrets.sources.internalKey,
    webhookSecretSource: sharedSecrets.sources.webhookSecret,
  },
  'Signing secrets: loaded (sources only — values are never logged)',
);

const app = express();
const walletManager = new WalletManager(new HdWalletService(signingMaterial.hdMnemonic));
const tronGridClient = new TronGridClient();
const monitorRegistry = new MonitorRegistry({
  client: tronGridClient,
  allowlist: { USDT: config.allowlist.USDT, USDC: config.allowlist.USDC },
  intervalMs: config.paymentPollingIntervalMs,
  holdingIntervalMs: config.paymentHoldingPollingIntervalMs,
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
// One resource client for both consumers: the delegation flow and the fee
// estimate must read the chain the same way (planDelegation is shared too).
const tronResourceClient = new TronResourceClient(config.tronFullNodeUrl, config.tronApiKey);
const energyDelegation = new EnergyDelegationService({
  client: tronDelegationClient,
  resources: tronResourceClient,
  sweeperAddress: config.hotWalletAddress,
  sweeperPrivateKey: signingMaterial.hotWalletPrivateKey,
  // T149 — the hybrid stake is dormant; OFF unless ENERGY_STAKING_ENABLED=true.
  stakingEnabled: config.energyStakingEnabled,
  stakeAddress: config.stakeAccountAddress,
  stakePermissionId: config.stakeAccountPermissionId,
  fallbackAmountSun: config.sweepTrxFallbackSun,
});
const tokenContracts = { USDT: config.usdtContract, USDC: config.usdcContract };
const decimalsPower = 10n ** BigInt(config.tokenDecimals);
// Shared by payout, refund, sweep and cold consolidation: destinations the
// caller may not choose and amounts it may not exceed (05 §3.3). Constructing
// it throws on a malformed pinned address, so a misconfigured signer never
// reaches the listen call.
const transferGuard = new TransferGuard({
  hotWalletAddress: config.hotWalletAddress,
  coldWalletAddress: config.coldWalletAddress,
  maxSingleTransferUnits: parseLimitUnits(config.maxSingleTransferUsdt, decimalsPower),
  maxDailyOutflowUnits: parseLimitUnits(config.maxDailyOutflowUsdt, decimalsPower),
  tokenContracts,
  history: tronGridClient,
});
// T160c düzeltme turu — one price service: the fee estimate and the status
// read's `nativePriceUsdt` (the backend's accounting) share its cache.
const trxPriceService = new TrxPriceService();
const transferService = new TransferService({
  walletManager,
  client: tronTransferClient,
  tokenContracts,
  hotWalletAddress: config.hotWalletAddress,
  hotWalletPrivateKey: signingMaterial.hotWalletPrivateKey,
  tokenDecimals: config.tokenDecimals,
  energyDelegation,
  guard: transferGuard,
  priceService: trxPriceService,
});
// T172b — the signer's 24-hour cap on refunds leaving deposits. A ledger that
// cannot be opened does not stop the service (monitoring and sweeps go on);
// every deposit refund then waits until the volume is fixed.
const depositRefundLedger = openDepositRefundLedger(config.depositRefundLedgerDir);
// Sahip kararı 2026-10-23 — one-time operator permits past the cap, written
// on the server by `node dist/cli/depositRefundPermit.js` into the ledger's
// volume. No HTTP route creates one.
let depositRefundPermits: DepositRefundPermitStore | null = null;
if (depositRefundLedger.ledger) {
  try {
    depositRefundPermits = new DepositRefundPermitStore({
      baseDir: config.depositRefundLedgerDir,
      network: 'TRON',
      isValidAddress: (address) => TronWeb.isAddress(address),
      onEvent: (event) => depositRefundPermitsTotal.inc({ event }),
    });
    const permits = depositRefundPermits;
    permits.reportExpiredAll();
    setInterval(() => permits.reportExpiredAll(), 5 * 60 * 1000).unref();
  } catch (err) {
    logger.error(
      { reason: (err as Error).message },
      'Deposit refund permits directory unavailable — no refund passes over the cap',
    );
  }
}
const depositRefundLimit = new DepositRefundLimit({
  maxDailyUnits: parseLimitUnits(config.depositRefundDailyLimitUsdt, decimalsPower),
  ledger: depositRefundLedger.ledger,
  ledgerError: depositRefundLedger.error,
  decimalsPower,
  permits: depositRefundPermits,
});
const refundService = new RefundService({
  walletManager,
  client: tronTransferClient,
  tokenContracts,
  tokenDecimals: config.tokenDecimals,
  energyDelegation,
  guard: transferGuard,
  depositRefundLimit,
});
// T172a — the closure tool's chain check (read-only; solidified transactions only).
const transferVerifier = new TransferVerifier({
  solidityUrl: config.tronSolidityUrl,
  apiKey: config.tronApiKey,
  tokenContracts,
});
const feeEstimationService = new FeeEstimationService({
  resourceClient: tronResourceClient,
  priceService: trxPriceService,
  tokenContracts,
  hotWalletAddress: config.hotWalletAddress,
  // A refund is charged for the path it will take: the estimate reads the
  // stake from the same account the broadcast delegates from.
  delegationSource: energyDelegation,
  tokenDecimals: config.tokenDecimals,
});

// Which account the Energy is delegated from, read back from the two services
// that use it. An operator checks it right after a restart (DEPLOY_RUNBOOK
// §C.2 step 10) instead of waiting for the first sweep to burn, and
// startupWiring.test.ts pins the path from .env to both services, which no
// unit test reaches (#325 re-validation).
logger.info(
  {
    stakingEnabled: energyDelegation.stakingEnabled,
    delegationOwner: energyDelegation.delegationOwner,
    permissionId: energyDelegation.delegationPermissionId ?? null,
    refundEstimateReadsStakeFrom: feeEstimationService.delegationOwner,
  },
  !energyDelegation.stakingEnabled
    ? 'Energy delegation: OFF (ENERGY_STAKING_ENABLED is not true) — no stake is read, every deposit transfer burns'
    : energyDelegation.delegationPermissionId === undefined
      ? 'Energy delegation: the hot wallet holds its own stake (STAKE_ACCOUNT_ADDRESS unset)'
      : 'Energy delegation: stake account, signed by the hot wallet key through an active permission',
);

// T139-ActiveMonitorQuotaAlarm — the two cadences and the budget the
// tron-quota-projection alert divides by, read back from the objects that use
// them. startupWiring.test.ts starts this file the way production does and
// reads this line, so a value that never reaches the registry or the gauge
// shows there rather than in a quota exhausted a week later.
logger.info(
  {
    paymentPollingIntervalMs: monitorRegistry.cadenceIntervalMs('PAYMENT'),
    holdingPollingIntervalMs: monitorRegistry.cadenceIntervalMs('HOLDING'),
    tronGridDailyRequestBudget: publishTronGridDailyRequestBudget(
      config.tronGridDailyRequestBudget,
    ),
  },
  'Payment monitoring: cadences and TronGrid daily request budget',
);

// T170 — is the TRON chain progressing? (tron-solid-block-stalled). Reads the
// solid head every TRON_SOLID_BLOCK_POLL_INTERVAL_MS and publishes its age.
const solidBlockTracker = new SolidBlockTracker({
  client: tronGridClient,
  pollIntervalMs: config.tronSolidBlockPollIntervalMs,
  stallThresholdSeconds: config.tronSolidBlockStallSeconds,
});
solidBlockTracker.start();
logger.info(
  {
    solidBlockPollIntervalMs: config.tronSolidBlockPollIntervalMs,
    solidBlockStallSeconds: config.tronSolidBlockStallSeconds,
  },
  'Chain progress: TRON solid block tracker started',
);

// T172b — the deposit refund cap as the refund path holds it; an operator
// reads this line after a restart instead of finding out at the first refund.
logger.info(
  {
    depositRefundDailyLimitUsdt: config.depositRefundDailyLimitUsdt || null,
    depositRefundLedgerDir: config.depositRefundLedgerDir || null,
    ledgerOpen: depositRefundLedger.ledger !== null,
    countedLast24hUnits: depositRefundLimit.usedUnits().toString(),
  },
  depositRefundLedger.ledger === null ||
    parseLimitUnits(config.depositRefundDailyLimitUsdt, decimalsPower) === null
    ? 'Deposit refund daily limit: NOT usable (unset limit or no ledger) — every refund waits'
    : 'Deposit refund daily limit: active',
);

// Middleware
app.use(express.json());
app.use(correlationMiddleware);

// Routes
app.use(
  createRouter({
    internalKey,
    walletManager,
    monitorRegistry,
    postCancelMonitorRegistry,
    transferService,
    refundService,
    feeEstimationService,
    transferVerifier,
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
  solidBlockTracker.stop();
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
