import type { EthereumServiceConfig } from './config/index.js';
import { FinalityTracker } from './finality/FinalityTracker.js';
import { paymentReadiness } from './health/paymentReadiness.js';
import { DepositRegistry } from './monitor/DepositRegistry.js';
import { PaymentScanner } from './monitor/PaymentScanner.js';
import { BalanceReader } from './reconcile/BalanceReader.js';
import { FuelLedger } from './reconcile/FuelLedger.js';
import { RatePacer } from './reconcile/RatePacer.js';
import { Reconciler } from './reconcile/Reconciler.js';
import type { EthereumRpcClient } from './rpc/RpcClient.js';
import { UsdtStatusMonitor } from './token/UsdtStatus.js';
import { createWebhookSender, type SendWebhook } from './webhook/WebhookClient.js';
import { EthFeeEstimationService } from './fee/FeeEstimationService.js';
import { EthPriceService, type EthPriceServiceOptions } from './price/EthPriceService.js';
import { ethUsdFromPriceService } from './transfer/FeePolicy.js';
import { FeeWaitTracker } from './transfer/FeeWaitTracker.js';
import { TransferGuard } from './transfer/TransferGuard.js';
import { TransferService } from './transfer/TransferService.js';
import type { TxJournal } from './transfer/TxJournal.js';
import type { EthereumHdWallet } from './wallet/HdWalletService.js';

/**
 * The service's moving parts and the order they are started in — the one
 * place both `index.ts` and the startup-sequence test use (T151 doğrulaması:
 * the USDT status read at start depends on the finalized head, so the real
 * order is what must be tested, not a hand-built one).
 */
export type WiringConfig = Pick<
  EthereumServiceConfig,
  | 'rpc'
  | 'finality'
  | 'tokenContracts'
  | 'reconciliation'
  | 'monitor'
  | 'webhookHmacKey'
  | 'backendUrl'
  | 'usdtStatusIntervalMs'
>;

export function buildServices(
  config: WiringConfig,
  rpc: EthereumRpcClient,
  options: { send?: SendWebhook } = {},
) {
  const allowlist = config.tokenContracts;
  const finality = new FinalityTracker({
    rpc,
    primaryProvider: config.rpc.providers[0].name,
    pollIntervalMs: config.finality.pollIntervalMs,
    stallThresholdSeconds: config.finality.stallThresholdSeconds,
  });
  const pacer = new RatePacer({ cuPerSecond: config.reconciliation.cuPerSecond });
  const reader = new BalanceReader(rpc, allowlist, pacer);
  const registry = new DepositRegistry();
  const fuel = new FuelLedger();
  const scanner = new PaymentScanner({
    rpc,
    registry,
    finality: () => finality.status(),
    allowlist,
    send:
      options.send ??
      createWebhookSender({ baseUrl: config.backendUrl, secret: config.webhookHmacKey }),
    balances: reader,
    pacer,
    tickIntervalMs: config.monitor.tickIntervalMs,
    restartLookbackBlocks: config.monitor.restartLookbackBlocks,
    rearmWindowMs: config.monitor.rearmWindowMs,
    catchUpBlocksPerTick: config.monitor.catchUpBlocksPerTick,
    minProviderBlockRange: Math.min(...config.rpc.providers.map((p) => p.maxBlockRange)),
  });
  const usdt = new UsdtStatusMonitor({
    rpc,
    contract: allowlist.USDT,
    finality: () => finality.status(),
    intervalMs: config.usdtStatusIntervalMs,
  });
  const reconciler = new Reconciler({
    registry,
    finality: () => finality.status(),
    scannedBlock: () => scanner.scannedBlock(),
    reader,
    fuel,
    intervalMs: config.reconciliation.intervalMs,
    maxFinalizedAgeSeconds: config.reconciliation.maxFinalizedAgeSeconds,
  });
  const readiness = () =>
    paymentReadiness({
      monitorRunning: scanner.isRunning(),
      webhookConfigured: config.webhookHmacKey !== '',
      usdt: usdt.status().state,
    });

  return {
    finality,
    registry,
    fuel,
    pacer,
    reader,
    scanner,
    usdt,
    reconciler,
    readiness,
    /** The start order production uses (finalized head first: everything else reads it). */
    start(): void {
      finality.start();
      scanner.start();
      usdt.start();
      reconciler.start();
    },
    stop(): void {
      finality.stop();
      scanner.stop();
      usdt.stop();
      reconciler.stop();
    },
  };
}

export type Services = ReturnType<typeof buildServices>;

/** What {@link buildOutbound} reads from the configuration. */
export type OutboundConfig = Pick<
  EthereumServiceConfig,
  | 'chainId'
  | 'tokenContracts'
  | 'hotWalletKey'
  | 'hotWalletAddress'
  | 'coldWalletAddress'
  | 'transfers'
>;

export interface OutboundDeps {
  rpc: EthereumRpcClient;
  journal: TxJournal;
  wallet: EthereumHdWallet;
  /** Number of the last `finalized` block (the FinalityTracker built by {@link buildServices}). */
  finalizedHead: () => bigint | null;
  /** The address the fee estimate prices a payout from (the key's own, else the configured one). */
  feeHotWalletAddress: string | null;
  /** Options of the ONE ETH price service (tests inject `fetchFn` / `now`). */
  priceOptions?: EthPriceServiceOptions;
}

/**
 * The sends (T152) and the fee estimate (T154) over ONE ETH/USDT price
 * service: the fee cap a send is signed under and the fee the backend shows
 * the user are converted with the same price, cache and reference
 * (wiring.test.ts pins it). Used by `index.ts`.
 */
export function buildOutbound(config: OutboundConfig, deps: OutboundDeps) {
  const { rpc, journal, wallet } = deps;
  const tokens = config.tokenContracts;
  const ethPrice = new EthPriceService(deps.priceOptions);
  const feeEstimator = new EthFeeEstimationService({
    rpc,
    prices: ethPrice,
    tokenContracts: tokens,
    hotWalletAddress: deps.feeHotWalletAddress,
  });
  const feeWaits = new FeeWaitTracker(config.transfers.journalDir);
  const transfers = new TransferService({
    rpc,
    journal,
    feeWaits,
    guard: new TransferGuard({
      rpc,
      hotWalletAddress: config.hotWalletAddress,
      coldWalletAddress: config.coldWalletAddress,
      tokenContracts: [tokens.USDT, tokens.USDC].filter((c) => c !== ''),
      maxSingleTransferUnits: config.transfers.maxSingleTransferUnits,
      maxDailyOutflowUnits: config.transfers.maxDailyOutflowUnits,
    }),
    wallet,
    policy: {
      chainId: BigInt(config.chainId),
      paymentToken: tokens.USDT,
      wrongTokenRefundToken: tokens.USDC,
      maxTokenGasLimit: 150_000n,
    },
    hotWalletKey: config.hotWalletKey,
    hotWalletAddress: config.hotWalletKey ? config.hotWalletAddress : null,
    tokenContracts: tokens,
    // The fee cap's ETH price: the same service the estimate above uses.
    prices: ethUsdFromPriceService(ethPrice),
    finalizedHead: deps.finalizedHead,
    bumpAfterMs: config.transfers.bumpAfterSeconds * 1000,
    conflictConfirmMs: config.transfers.keeperIntervalMs,
  });
  return { ethPrice, feeEstimator, feeWaits, transfers };
}
