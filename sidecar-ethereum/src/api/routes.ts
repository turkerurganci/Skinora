import express, { Router, type Express } from 'express';
import { healthCheckFactory } from '../health/HealthController.js';
import type { PaymentReadiness } from '../health/paymentReadiness.js';
import { metricsHandler } from '../metrics.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import type { DepositRegistry } from '../monitor/DepositRegistry.js';
import type { StablecoinAllowlist } from '../monitor/tokenRules.js';
import type { BalanceReader } from '../reconcile/BalanceReader.js';
import type { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { correlationMiddleware, internalKeyAuth, networkGuard } from './middleware.js';
import { deriveAddressHandler } from './walletHandlers.js';
import { walletBalancesHandler } from './balancesHandler.js';
import {
  postCancelStartHandler,
  postCancelStopHandler,
  startMonitorHandler,
  stopMonitorHandler,
} from './monitorHandlers.js';
import { estimateFeeHandler } from './feeHandlers.js';
import type { EthFeeEstimationService } from '../fee/FeeEstimationService.js';
import { NOT_YET_IMPLEMENTED, notImplementedHandler } from './notImplemented.js';

/** The payment monitor's half of the surface (T151). */
export interface PaymentDeps {
  registry: DepositRegistry;
  allowlist: StablecoinAllowlist;
  reader: Pick<BalanceReader, 'read' | 'configuredSymbols'>;
  maxFinalizedAgeSeconds: number;
  readiness: () => PaymentReadiness;
}

export interface AppDeps {
  internalKey: string;
  chainId: number;
  wallet: EthereumHdWallet;
  finality: () => FinalityStatus;
  /** Absent = the T150 skeleton shape: the monitor endpoints answer 501, `acceptsPayments` is false. */
  payments?: PaymentDeps;
  /** T154 — `POST /api/transfer/estimate-fee`. */
  feeEstimator: EthFeeEstimationService;
}

/** The endpoints T151 brings (05 §3.3a "Uçlar"). */
export const PAYMENT_ENDPOINTS = [
  '/wallet/balances',
  '/monitor/start',
  '/monitor/stop',
  '/monitor/post-cancel-start',
  '/monitor/post-cancel-stop',
] as const;

/**
 * The service's HTTP surface (05 §3.3a). `/health` and `/metrics` are open on
 * the internal network like the other sidecars'; everything under `/api`
 * needs the internal key and a body naming `ETHEREUM`.
 */
export function createApp(deps: AppDeps): Express {
  const app = express();
  app.use(express.json());
  app.use(correlationMiddleware);

  const router = Router();
  router.get(
    '/health',
    healthCheckFactory({
      finality: deps.finality,
      chainId: deps.chainId,
      readiness: deps.payments?.readiness,
    }),
  );
  router.get('/metrics', metricsHandler);

  const api = Router();
  api.use(internalKeyAuth(deps.internalKey));
  api.use(networkGuard);
  api.post('/wallet/derive', deriveAddressHandler(deps.wallet));
  const payments = deps.payments;
  if (payments) {
    api.post(
      '/wallet/balances',
      walletBalancesHandler({
        reader: payments.reader,
        finality: deps.finality,
        maxFinalizedAgeSeconds: payments.maxFinalizedAgeSeconds,
      }),
    );
    api.post('/monitor/start', startMonitorHandler(payments.registry, payments.allowlist));
    api.post('/monitor/stop', stopMonitorHandler(payments.registry));
    api.post(
      '/monitor/post-cancel-start',
      postCancelStartHandler(payments.registry, payments.allowlist),
    );
    api.post('/monitor/post-cancel-stop', postCancelStopHandler(payments.registry));
  } else {
    for (const path of PAYMENT_ENDPOINTS) api.post(path, notImplementedHandler('T151'));
  }
  api.post('/transfer/estimate-fee', estimateFeeHandler(deps.feeEstimator));
  for (const endpoint of NOT_YET_IMPLEMENTED) {
    api[endpoint.method](endpoint.path, notImplementedHandler(endpoint.task));
  }
  router.use('/api', api);

  app.use(router);
  return app;
}
