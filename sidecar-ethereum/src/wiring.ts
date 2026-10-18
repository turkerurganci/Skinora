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
