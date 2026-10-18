import { Router } from 'express';
import type { Network } from '../networks.js';

/**
 * `/health` of one fake surface. A blockchain surface names its network the
 * way the real services do (T148 — the backend's probe reports a service
 * answering for the WRONG network as unhealthy) and declares
 * `acceptsPayments: true`: since T151 the backend keeps Ethereum out of
 * `supportedNetworks` unless its service says it can see payments, and the
 * fake's payment levers are exactly that capability.
 */
export function createHealthRouter(network?: Network): Router {
  const router = Router();
  router.get('/health', (_req, res) => {
    res.json(
      network === undefined
        ? { status: 'ok', service: 'skinora-fake-sidecar' }
        : { status: 'ok', service: 'skinora-fake-sidecar', network, acceptsPayments: true },
    );
  });
  return router;
}
