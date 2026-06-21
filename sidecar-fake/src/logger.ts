import pino from 'pino';
import crypto from 'crypto';
import type { IncomingMessage } from 'http';
import { config } from './config.js';

const SERVICE_NAME = 'skinora-fake-sidecar';

// Test harness only — log to stdout, no Loki transport (keeps the image lean).
export type Logger = pino.Logger;

export const logger: Logger = pino({
  level: config.logLevel,
  base: { service: SERVICE_NAME, environment: config.nodeEnv },
  messageKey: 'message',
  redact: {
    paths: ['*.secret', '*.password', 'secret', 'password', 'headers.authorization'],
    censor: '***',
    remove: false,
  },
});

/**
 * Request-scoped child logger carrying a correlationId (header or fresh UUID).
 */
export function loggerForRequest(req: IncomingMessage) {
  const headerValue = req.headers['x-correlation-id'];
  const correlationId =
    (Array.isArray(headerValue) ? headerValue[0] : headerValue) || crypto.randomUUID();
  return { logger: logger.child({ correlationId }), correlationId };
}
