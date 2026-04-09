import type { Request, Response, NextFunction } from 'express';
import { config } from '../config/index.js';
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
 */
export function internalKeyAuth(req: Request, res: Response, next: NextFunction): void {
  const key = req.headers['x-internal-key'];
  if (!config.internalKey) {
    // No key configured — skip auth (dev mode)
    next();
    return;
  }
  if (key !== config.internalKey) {
    res.status(401).json({ error: 'Unauthorized' });
    return;
  }
  next();
}
