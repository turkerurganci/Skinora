import { loadConfig, providerHost, type EthereumServiceConfig } from './config/index.js';
import { logger } from './logger.js';
import { SidecarError } from './errors/SidecarError.js';
import { SERVICE_NETWORK } from './network.js';
import { EthereumHdWallet } from './wallet/HdWalletService.js';
import { checkWalletKeys, type WalletKeyAddresses } from './wallet/walletKeys.js';
import { EthereumRpcClient } from './rpc/RpcClient.js';
import { createApp } from './api/routes.js';
import { TxJournal } from './transfer/TxJournal.js';
import { TransferVerifier } from './transfer/TransferVerifier.js';
import { FEE_CAP_WAIT_REASONS, GAS_WAIT_REASONS } from './transfer/FeeWaitTracker.js';
import { setTransferWaitSource, transferWaitAlarmSeconds } from './metrics.js';
import { buildOutbound, buildServices } from './wiring.js';

/**
 * Entry point of the Ethereum service (T150 — 05 §3.3a, 08 §3.7).
 *
 * Everything the service must not run without is checked before it listens:
 * configuration (internal key, seed, primary RPC — config/index.ts), the seed
 * itself (the master node is built and index 0 derived), and the platform
 * signing keys (walletKeys.ts). Any of them failing exits non-zero with the
 * error code on the last log line; the container's restart policy then shows
 * a crash loop rather than a service that looks up but cannot sign.
 */
function refuseToStart(err: unknown): void {
  const code = err instanceof SidecarError ? err.code : 'STARTUP_FAILED';
  // Never the raw error object: a parse error could quote its input.
  const message = err instanceof SidecarError ? err.message : 'Unexpected startup failure.';
  logger.fatal({ code }, `Ethereum service refused to start: ${code} — ${message}`);
  process.exitCode = 1;
  // Let the log streams flush, then leave (the Loki transport keeps a worker).
  setTimeout(() => process.exit(1), 200);
}

function main(): void {
  let config: EthereumServiceConfig;
  let wallet: EthereumHdWallet;
  let keys: WalletKeyAddresses;
  let journal: TxJournal;
  try {
    config = loadConfig(process.env);
    wallet = new EthereumHdWallet(config.hdMnemonic);
    wallet.derive(0);
    keys = checkWalletKeys(config);
    // T152 — a signer that cannot remember what it signed must not sign.
    journal = new TxJournal(config.transfers.journalDir);
  } catch (err) {
    refuseToStart(err);
    return;
  }

  const rpc = new EthereumRpcClient({
    providers: config.rpc.providers,
    chainId: config.chainId,
    timeoutMs: config.rpc.timeoutMs,
    failoverCooldownMs: config.rpc.failoverCooldownMs,
  });
  const services = buildServices(config, rpc);
  const { finality, registry, reader, readiness } = services;

  if (config.rpc.providers.length < 2) {
    logger.warn(
      'Only one RPC provider is configured (ETHEREUM_RPC_BACKUP_URL unset) — no failover (08 §3.7.1 asks for a backup).',
    );
  }
  if (!config.tokenContracts.USDT) {
    logger.warn(
      'ETHEREUM_USDT_CONTRACT is not set on a testnet — the payment token is unknown until it is (T159 deploys the Sepolia copy).',
    );
  }

  // Read back from the objects built above — startupWiring.test.ts starts this
  // file the way production does and reads this line.
  logger.info(
    {
      network: SERVICE_NETWORK,
      chain: config.chain,
      chainId: config.chainId,
      derivationPathTemplate: "m/44'/60'/0'/0/{index}",
      depositIndex0: wallet.derive(0).address,
      rpcProviders: config.rpc.providers.map((p) => ({
        name: p.name,
        host: providerHost(p.url),
        maxBlockRange: p.maxBlockRange,
        maxAddressesPerQuery: p.maxAddressesPerQuery,
      })),
      finalizedPollIntervalMs: config.finality.pollIntervalMs,
      finalizedStallSeconds: config.finality.stallThresholdSeconds,
      hotWalletAddress: keys.hotWalletAddress,
      fuelWalletAddress: keys.fuelWalletAddress,
      coldWalletConfigured: config.coldWalletAddress !== null,
      webhookConfigured: config.webhookHmacKey !== '',
      tokenContracts: config.tokenContracts,
      txJournal: { dir: config.transfers.journalDir, records: journal.size },
      sendLimitsConfigured: {
        single: config.transfers.maxSingleTransferUnits !== null,
        daily: config.transfers.maxDailyOutflowUnits !== null,
        fuelSingle: config.transfers.fuelMaxSingleWei !== null,
        fuelDaily: config.transfers.fuelMaxDailyWei !== null,
      },
      feeCapWaitAlarmSeconds: config.transfers.waitAlarmSeconds,
    },
    'Ethereum service configured',
  );

  // T151 — payment monitor, USDT status, balance reconciliation (08 §3.7.4).
  const allowlist = config.tokenContracts;
  if (config.webhookHmacKey === '') {
    logger.error(
      'WEBHOOK_SECRET is not set — payments cannot be reported; /health declares acceptsPayments=false (T151).',
    );
  }
  logger.info(
    {
      monitorTickMs: config.monitor.tickIntervalMs,
      restartLookbackBlocks: config.monitor.restartLookbackBlocks,
      reconciliationIntervalMs: config.reconciliation.intervalMs,
      reconciliationCuPerSecond: config.reconciliation.cuPerSecond,
      reconciliationMaxFinalizedAgeSeconds: config.reconciliation.maxFinalizedAgeSeconds,
      reconciliationCuPerDeposit: reader.costPerAddress(),
    },
    'Ethereum payment monitor configured',
  );

  // T152 sends (08 §3.7.6, §3.7.7) and T154's fee estimate, over ONE ETH
  // price service: the fee cap and the estimate use the same price.
  const { feeEstimator, feeWaits, transfers, walletBalances, fuelLedgerSeeded } = buildOutbound(
    config,
    {
      rpc,
      journal,
      wallet,
      finalizedHead: () => finality.status().snapshot?.number ?? null,
      // The configured address prices a payout even before the key is mounted.
      feeHotWalletAddress: keys.hotWalletAddress ?? config.hotWalletAddress,
      // T153 — fuel the journal holds in a block is not foreign ETH.
      fuelLedger: services.fuel,
    },
  );
  logger.info(
    {
      fuelWalletAddress: keys.fuelWalletAddress,
      fuelCapsConfigured:
        config.transfers.fuelMaxSingleWei !== null && config.transfers.fuelMaxDailyWei !== null,
      fuelLedgerSeeded,
      fuelWalletFloorWei: config.walletBalances.fuelFloorWei.toString(),
      hotWalletFloorWei: config.walletBalances.hotFloorWei.toString(),
      walletBalanceIntervalMs: config.walletBalances.intervalMs,
    },
    'Ethereum fuel wallet configured',
  );
  if (!config.fuelWalletKey) {
    logger.warn(
      'The fuel wallet key is not mounted (ethereum_fuel_wallet_private_key) — sweeps and refunds from deposits wait (FUEL_REQUIRED) until it is (T153).',
    );
  }
  transferWaitAlarmSeconds.set(config.transfers.waitAlarmSeconds);
  setTransferWaitSource({
    feeCapWaitSeconds: () => feeWaits.longestWaitSeconds(FEE_CAP_WAIT_REASONS),
    unminedSeconds: () => transfers.oldestUnminedSeconds(),
    gasWaitSeconds: () => feeWaits.longestWaitSeconds(GAS_WAIT_REASONS),
    nonceConflicts: () => transfers.nonceConflictCount(),
    journalBehindChain: () => transfers.journalBehindChain().length,
    journalAckAheadOfChain: () => transfers.acknowledgementsAheadOfChain().length,
  });
  let keeping = false;
  const keeper = setInterval(() => {
    if (keeping) return;
    keeping = true;
    void transfers.keepAll().finally(() => {
      keeping = false;
    });
  }, config.transfers.keeperIntervalMs);
  keeper.unref();

  const app = createApp({
    internalKey: config.internalKey,
    chainId: config.chainId,
    wallet,
    finality: () => finality.status(),
    transfers,
    payments: {
      registry,
      allowlist,
      reader,
      maxFinalizedAgeSeconds: config.reconciliation.maxFinalizedAgeSeconds,
      readiness,
    },
    feeEstimator,
    verifier: new TransferVerifier({ rpc, tokenContracts: config.tokenContracts }),
    platformWallets: () => ({
      hotWalletAddress: keys.hotWalletAddress,
      fuelWalletAddress: transfers.fuelWalletAddress(),
      coldWalletAddress: config.coldWalletAddress,
    }),
  });

  const server = app.listen(config.port, '0.0.0.0', () => {
    logger.info({ port: config.port, network: SERVICE_NETWORK }, 'Ethereum service listening');
    services.start();
    walletBalances.start();
  });

  const shutdown = (signal: string): void => {
    logger.info({ signal }, 'Graceful shutdown started');
    services.stop();
    walletBalances.stop();
    clearInterval(keeper);
    server.close(() => logger.info('HTTP server closed'));
    const forceTimer = setTimeout(() => {
      logger.error('Forced shutdown — timeout exceeded');
      process.exit(1);
    }, config.shutdownTimeoutMs);
    forceTimer.unref();
    process.exit(0);
  };
  process.on('SIGTERM', () => shutdown('SIGTERM'));
  process.on('SIGINT', () => shutdown('SIGINT'));
}

main();
