import { Router } from 'express';
import { healthCheck } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth } from './middleware.js';

const router = Router();

// Health check — no auth required
router.get('/health', healthCheck);

// Prometheus metrics — no auth required (T16)
router.get('/metrics', metricsHandler);

// Authenticated API routes (stub — will be implemented in T64–T69)
const apiRouter = Router();
apiRouter.use(internalKeyAuth);

// Placeholder routes for future implementation
apiRouter.post('/trade-offers/send', (_req, res) => {
  res.status(501).json({ error: 'Not implemented — see T65' });
});

apiRouter.get('/trade-offers/:offerId/status', (_req, res) => {
  res.status(501).json({ error: 'Not implemented — see T66' });
});

apiRouter.get('/inventory/:steamId', (_req, res) => {
  res.status(501).json({ error: 'Not implemented — see T67' });
});

apiRouter.get('/bots/status', (_req, res) => {
  res.status(501).json({ error: 'Not implemented — see T64' });
});

router.use('/api', apiRouter);

export { router };
