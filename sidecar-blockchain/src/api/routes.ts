import { Router } from 'express';
import { healthCheck } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth } from './middleware.js';
import { deriveAddressHandler } from './walletHandlers.js';
import { startMonitorHandler, stopMonitorHandler } from './monitorHandlers.js';
import { WalletManager } from '../wallet/WalletManager.js';
import type { MonitorRegistry } from '../monitor/MonitorRegistry.js';

export interface RouterDeps {
  walletManager: WalletManager;
  monitorRegistry: MonitorRegistry;
}

export function createRouter(deps: RouterDeps): Router {
  const router = Router();

  // Health check — no auth required
  router.get('/health', healthCheck);

  // Prometheus metrics — no auth required (T16)
  router.get('/metrics', metricsHandler);

  // Authenticated API routes
  const apiRouter = Router();
  apiRouter.use(internalKeyAuth);

  // Wallet management — T70
  // POST /api/wallet/derive { index, transactionId? }
  //   → 200 { address, derivationPath, index }
  //   → 400 { error: 'INVALID_DERIVATION_INDEX' }
  //   → 503 { error: 'HD_WALLET_NOT_CONFIGURED' }
  apiRouter.post('/wallet/derive', deriveAddressHandler(deps.walletManager));

  // Payment monitoring — T71
  // POST /api/monitor/start { address, paymentAddressId, transactionId, expectedContract, expectedSymbol }
  //   → 200 { acknowledged: true, started: boolean, address }
  //   → 400 { error: 'INVALID_MONITOR_REQUEST' | 'UNSUPPORTED_SYMBOL' }
  apiRouter.post('/monitor/start', startMonitorHandler(deps.monitorRegistry));

  // POST /api/monitor/stop { address }
  //   → 200 { acknowledged: true, stopped: boolean, address }
  apiRouter.post('/monitor/stop', stopMonitorHandler(deps.monitorRegistry));

  // Transfers — T73
  apiRouter.post('/transfer/payout', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T73' });
  });

  apiRouter.post('/transfer/refund', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T73' });
  });

  apiRouter.post('/transfer/sweep', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T73' });
  });

  // Balance check — T77
  apiRouter.get('/wallet/hot-wallet-balance', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T77' });
  });

  router.use('/api', apiRouter);

  return router;
}
