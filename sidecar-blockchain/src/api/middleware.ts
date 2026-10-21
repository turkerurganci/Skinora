import type { Request, Response, NextFunction } from 'express';
import { timingSafeEqual } from 'node:crypto';
import { logger as rootLogger, loggerForRequest } from '../logger.js';
import { SERVICE_NETWORK } from '../network.js';

/**
 * Attaches a request-scoped logger and correlationId to the request.
 */
export function correlationMiddleware(req: Request, res: Response, next: NextFunction): void {
  const { logger, correlationId } = loggerForRequest(req);
  req.log = logger;
  req.correlationId = correlationId;
  res.setHeader('X-Correlation-Id', correlationId);

  logger.info({ method: req.method, url: req.url }, 'Incoming request');
  next();
}

/**
 * T148 (05 §3.3a) — a request body that names a network must name this
 * service's (`SERVICE_NETWORK`). The backend sends `network` on every POST;
 * one naming another network means the backend resolved that network's service
 * to this address — refused with 400 `NETWORK_MISMATCH`, which the backend
 * treats as a rejected (non-retried) request, so nothing is signed for the
 * wrong chain. A body without the field is accepted as addressed to this
 * service: operator calls from the runbook (`/api/wallet/balances`,
 * `/api/wallet/derive`) predate the field. GET routes carry no body.
 */
export function networkGuard(req: Request, res: Response, next: NextFunction): void {
  const body: unknown = req.body;
  if (req.method !== 'POST' || body === null || typeof body !== 'object' || !('network' in body)) {
    next();
    return;
  }
  const claimed = (body as { network?: unknown }).network;
  if (claimed !== SERVICE_NETWORK) {
    (req.log ?? rootLogger).error(
      { claimed, serviceNetwork: SERVICE_NETWORK, url: req.url },
      'Request names another network — refused (backend service address points at the wrong service?)',
    );
    res.status(400).json({
      error: 'NETWORK_MISMATCH',
      message: `This is the ${SERVICE_NETWORK} service; the request names ${JSON.stringify(claimed)}.`,
    });
    return;
  }
  next();
}

/**
 * Validates X-Internal-Key header for .NET → Sidecar requests (05 §3.4).
 *
 * T161 — fail closed. The key is bound when the router is built and an empty
 * key refuses to build it: there is no "no key configured, skip auth" path any
 * more (the entry point already refuses to start without one —
 * config/internalKey.ts). Constant-time comparison, as in the Ethereum service.
 */
export function internalKeyAuth(expectedKey: string) {
  if (!expectedKey) {
    throw new Error('internalKeyAuth needs a key — the service does not run without one.');
  }
  const expected = Buffer.from(expectedKey, 'utf8');
  return function checkInternalKey(req: Request, res: Response, next: NextFunction): void {
    const header = req.headers['x-internal-key'];
    const presented = Buffer.from(typeof header === 'string' ? header : '', 'utf8');
    if (presented.length !== expected.length || !timingSafeEqual(presented, expected)) {
      res.status(401).json({ error: 'Unauthorized' });
      return;
    }
    next();
  };
}
