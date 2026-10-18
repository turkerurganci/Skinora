import express from 'express';
import type { Express } from 'express';
import { correlationMiddleware, internalKeyAuth } from './middleware.js';
import { createHealthRouter } from './routes/health.js';
import { controlRouter } from './routes/control.js';
import { steamRouter } from './routes/steam.js';
import { createBlockchainRouter } from './routes/blockchain.js';
import { profileFor, type Network } from './networks.js';

/** Which backend-facing surface a listener answers (index.ts). */
export type Surface = 'STEAM' | Network;

/**
 * One Express app per listener. The Steam surface (5100) and the TRON surface
 * (5200) keep the original layout — Steam routes plus the TRON blockchain
 * routes, whose paths are disjoint. T155 added the ETHEREUM surface (5300):
 * its blockchain paths are the SAME as TRON's (05 §3.3a — one contract, one
 * service per network), so it gets its own router bound to the Ethereum
 * profile and the two networks can never answer for each other.
 *
 * The E2E control surface is mounted on every listener; it resolves the
 * network of a deposit from the database, not from the port it was called on.
 */
export function buildApp(surface: Surface = 'TRON'): Express {
  const app = express();
  app.use(express.json());
  app.use(correlationMiddleware);

  // Unauthenticated: health probe + E2E control surface (caller is the test).
  app.use(createHealthRouter(surface === 'STEAM' ? undefined : surface));
  app.use(controlRouter);

  // Backend → sidecar surface, gated by X-Internal-Key when configured.
  app.use(internalKeyAuth);
  app.use(steamRouter);
  app.use(createBlockchainRouter(profileFor(surface === 'ETHEREUM' ? 'ETHEREUM' : 'TRON')));

  return app;
}
