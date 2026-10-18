import express, { Router, type Express } from 'express';
import { healthCheckFactory } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import type { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { correlationMiddleware, internalKeyAuth, networkGuard } from './middleware.js';
import { deriveAddressHandler } from './walletHandlers.js';
import { NOT_YET_IMPLEMENTED, notImplementedHandler } from './notImplemented.js';

export interface AppDeps {
  internalKey: string;
  chainId: number;
  wallet: EthereumHdWallet;
  finality: () => FinalityStatus;
}

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
  router.get('/health', healthCheckFactory({ finality: deps.finality, chainId: deps.chainId }));
  router.get('/metrics', metricsHandler);

  const api = Router();
  api.use(internalKeyAuth(deps.internalKey));
  api.use(networkGuard);
  api.post('/wallet/derive', deriveAddressHandler(deps.wallet));
  for (const endpoint of NOT_YET_IMPLEMENTED) {
    api[endpoint.method](endpoint.path, notImplementedHandler(endpoint.task));
  }
  router.use('/api', api);

  app.use(router);
  return app;
}
