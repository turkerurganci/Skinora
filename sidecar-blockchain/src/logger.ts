import pino from 'pino';
import crypto from 'crypto';
import type { IncomingMessage } from 'http';
import { config } from './config/index.js';

const SERVICE_NAME = 'skinora-blockchain-sidecar';

// Mirror SecretMaskingEnricher.cs (.NET) — keep field names in sync.
const REDACT_PATHS = [
  '*.privateKey',
  '*.apiKey',
  '*.refreshToken',
  '*.accessToken',
  '*.password',
  '*.secret',
  '*.jwtSecret',
  '*.mnemonic',
  '*.hdWalletMnemonic',
  '*.authorization',
  'privateKey',
  'apiKey',
  'refreshToken',
  'accessToken',
  'password',
  'secret',
  'jwtSecret',
  'mnemonic',
  'hdWalletMnemonic',
  'authorization',
  'headers.authorization',
  "headers['x-api-key']",
];

// pino-loki ships as ESM and lacks the `pino-transport` package marker, so
// pino.transport() can't resolve it by name — use the resolved path.
const lokiTransport = pino.transport({
  target: require.resolve('pino-loki'),
  options: {
    host: config.lokiUrl,
    batching: true,
    interval: 5,
    labels: { service: SERVICE_NAME, environment: config.nodeEnv },
    silenceErrors: true,
  },
});

export const logger = pino(
  {
    level: config.logLevel,
    base: { service: SERVICE_NAME, environment: config.nodeEnv },
    messageKey: 'message',
    redact: {
      paths: REDACT_PATHS,
      censor: '***',
      remove: false,
    },
  },
  pino.multistream([{ stream: process.stdout }, { stream: lokiTransport }]),
);

/**
 * Build a request-scoped child logger carrying correlationId.
 * Reads X-Correlation-Id header or generates a new UUID v4.
 */
export function loggerForRequest(req: IncomingMessage) {
  const headerValue = req.headers['x-correlation-id'];
  const correlationId =
    (Array.isArray(headerValue) ? headerValue[0] : headerValue) || crypto.randomUUID();
  return { logger: logger.child({ correlationId }), correlationId };
}
