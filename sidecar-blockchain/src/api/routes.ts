import { Router } from 'express';
import { healthCheck } from '../health/HealthController.js';
import { internalKeyAuth } from './middleware.js';

const router = Router();

// Health check — no auth required
router.get('/health', healthCheck);

// Authenticated API routes (stub — will be implemented in T70–T77)
const apiRouter = Router();
apiRouter.use(internalKeyAuth);

// Wallet management — T70
apiRouter.post('/wallet/generate-address', (_req, res) => {
  res.status(501).json({ error: 'Not implemented — see T70' });
});

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

export { router };
