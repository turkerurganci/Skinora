import { timingSafeEqual } from 'node:crypto';
import type { NextFunction, Request, Response } from 'express';
import { logger as rootLogger, loggerForRequest } from '../logger.js';
import { SERVICE_NETWORK } from '../network.js';

/** Attaches a request-scoped logger and correlationId to the request. */
export function correlationMiddleware(req: Request, res: Response, next: NextFunction): void {
  const { logger, correlationId } = loggerForRequest(req);
  req.log = logger;
  req.correlationId = correlationId;
  res.setHeader('X-Correlation-Id', correlationId);
  logger.info({ method: req.method, url: req.url }, 'Incoming request');
  next();
}

/**
 * `X-Internal-Key` check (05 §3.4). Unlike the TRON service there is no
 * "no key configured — skip auth" mode: the key is required at startup
 * (config/index.ts), so every /api request is authenticated. Compared in
 * constant time.
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

/**
 * Every POST body must name this service's network (05 §3.3a). The backend
 * writes `network` on every POST (T148). Unlike the TRON service a body
 * WITHOUT the field is refused too — there are no pre-T148 runbook calls to
 * keep working, and a request that does not say which chain it is for is not
 * one this signer acts on. 400 `NETWORK_MISMATCH` is a rejected, non-retried
 * request on the backend side.
 */
export function networkGuard(req: Request, res: Response, next: NextFunction): void {
  if (req.method !== 'POST') {
    next();
    return;
  }
  const body: unknown = req.body;
  const claimed =
    body !== null && typeof body === 'object' ? (body as { network?: unknown }).network : undefined;
  if (claimed !== SERVICE_NETWORK) {
    (req.log ?? rootLogger).error(
      { claimed: claimed ?? null, serviceNetwork: SERVICE_NETWORK, url: req.url },
      'Request does not name this network — refused',
    );
    res.status(400).json({
      error: 'NETWORK_MISMATCH',
      message: `This is the ${SERVICE_NETWORK} service; the request names ${JSON.stringify(claimed ?? null)}.`,
    });
    return;
  }
  next();
}
