import type { Request, Response, NextFunction } from 'express';
import { timingSafeEqual } from 'node:crypto';
import { loggerForRequest } from '../logger.js';

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
