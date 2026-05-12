import { Router } from 'express';
import { botStatusFactory, healthCheckFactory } from '../health/HealthController.js';
import { metricsHandler } from '../metrics.js';
import { internalKeyAuth } from './middleware.js';
import type { BotManager } from '../bot/BotManager.js';

export function buildRouter(botManager?: BotManager): Router {
  const router = Router();

  // Health check — no auth required
  router.get('/health', healthCheckFactory(botManager));

  // Prometheus metrics — no auth required (T16)
  router.get('/metrics', metricsHandler);

  // Authenticated API routes
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

  // Bot pool status (T64)
  apiRouter.get('/bots/status', botStatusFactory(botManager));

  router.use('/api', apiRouter);
  return router;
}
