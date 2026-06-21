import express from 'express';
import type { Express } from 'express';
import { correlationMiddleware, internalKeyAuth } from './middleware.js';
import { healthRouter } from './routes/health.js';
import { controlRouter } from './routes/control.js';
import { steamRouter } from './routes/steam.js';
import { blockchainRouter } from './routes/blockchain.js';

/**
 * Single Express app serving both sidecar surfaces. Mounted on two ports by
 * index.ts (5100 steam / 5200 blockchain); the route paths are disjoint so one
 * handler set safely answers both.
 */
export function buildApp(): Express {
  const app = express();
  app.use(express.json());
  app.use(correlationMiddleware);

  // Unauthenticated: health probe + E2E control surface (caller is the test).
  app.use(healthRouter);
  app.use(controlRouter);

  // Backend → sidecar surface, gated by X-Internal-Key when configured.
  app.use(internalKeyAuth);
  app.use(steamRouter);
  app.use(blockchainRouter);

  return app;
}
