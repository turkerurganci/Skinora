import { loadConfig, providerHost, type EthereumServiceConfig } from './config/index.js';
import { logger } from './logger.js';
import { SidecarError } from './errors/SidecarError.js';
import { SERVICE_NETWORK } from './network.js';
import { EthereumHdWallet } from './wallet/HdWalletService.js';
import { checkWalletKeys, type WalletKeyAddresses } from './wallet/walletKeys.js';
import { EthereumRpcClient } from './rpc/RpcClient.js';
import { FinalityTracker } from './finality/FinalityTracker.js';
import { createApp } from './api/routes.js';

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
  try {
    config = loadConfig(process.env);
    wallet = new EthereumHdWallet(config.hdMnemonic);
    wallet.derive(0);
    keys = checkWalletKeys(config);
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
  const finality = new FinalityTracker({
    rpc,
    primaryProvider: config.rpc.providers[0].name,
    pollIntervalMs: config.finality.pollIntervalMs,
    stallThresholdSeconds: config.finality.stallThresholdSeconds,
  });

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
    },
    'Ethereum service configured',
  );

  const app = createApp({
    internalKey: config.internalKey,
    chainId: config.chainId,
    wallet,
    finality: () => finality.status(),
  });

  const server = app.listen(config.port, '0.0.0.0', () => {
    logger.info({ port: config.port, network: SERVICE_NETWORK }, 'Ethereum service listening');
    finality.start();
  });

  const shutdown = (signal: string): void => {
    logger.info({ signal }, 'Graceful shutdown started');
    finality.stop();
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
