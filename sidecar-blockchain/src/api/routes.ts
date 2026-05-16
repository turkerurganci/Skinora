import { Router } from 'express';
import { healthCheck } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth } from './middleware.js';
import { deriveAddressHandler } from './walletHandlers.js';
import { WalletManager } from '../wallet/WalletManager.js';

export interface RouterDeps {
  walletManager: WalletManager;
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
  apiRouter.post('/monitor/start', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T71' });
  });

  apiRouter.post('/monitor/stop', (_req, res) => {
    res.status(501).json({ error: 'Not implemented — see T71' });
  });

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
