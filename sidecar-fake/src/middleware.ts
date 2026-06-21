import type { Request, Response, NextFunction } from 'express';
import crypto from 'crypto';
import { config } from './config.js';
import { logger } from './logger.js';

/** Attaches a request-scoped correlationId (header or fresh UUID). */
export function correlationMiddleware(req: Request, res: Response, next: NextFunction): void {
  const header = req.headers['x-correlation-id'];
  const correlationId = (Array.isArray(header) ? header[0] : header) || crypto.randomUUID();
  req.correlationId = correlationId;
  res.setHeader('X-Correlation-Id', correlationId);
  logger.debug({ method: req.method, url: req.url, correlationId }, 'Incoming request');
  next();
}

/**
 * Validates the X-Internal-Key header for backend → sidecar calls (05 §3.4).
 * Skipped when no key is configured (mirrors sidecar-steam internalKeyAuth).
 */
export function internalKeyAuth(req: Request, res: Response, next: NextFunction): void {
  if (!config.internalKey) {
    next();
    return;
  }
  if (req.headers['x-internal-key'] !== config.internalKey) {
    res.status(401).json({ error: 'Unauthorized' });
    return;
  }
  next();
}
