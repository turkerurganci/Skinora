import pino from 'pino';
import crypto from 'crypto';
import type { IncomingMessage } from 'http';

export const SERVICE_NAME = 'skinora-ethereum-sidecar';

// Read straight from the environment, not from config/index.ts: the logger has
// to work while the configuration is being refused (a startup that fails must
// still say why), and none of these three values is a secret.
const lokiUrl = process.env.LOKI_URL || 'http://skinora-loki:3100';
const logLevel = process.env.LOG_LEVEL || 'info';
const environment = process.env.NODE_ENV || 'development';

// Mirror SecretMaskingEnricher.cs (.NET) and the TRON service — keep field
// names in sync. `url` is added here: an RPC provider URL carries its API key.
const REDACT_PATHS = [
  '*.privateKey',
  '*.apiKey',
  '*.refreshToken',
  '*.accessToken',
  '*.password',
  '*.secret',
  '*.jwtSecret',
  '*.mnemonic',
  '*.hdMnemonic',
  '*.hdWalletMnemonic',
  '*.internalKey',
  '*.webhookSecret',
  '*.authorization',
  '*.url',
  'privateKey',
  'apiKey',
  'refreshToken',
  'accessToken',
  'password',
  'secret',
  'jwtSecret',
  'mnemonic',
  'hdMnemonic',
  'hdWalletMnemonic',
  'internalKey',
  'webhookSecret',
  'authorization',
  'url',
  'headers.authorization',
  "headers['x-api-key']",
  "headers['x-internal-key']",
];

// pino-loki ships as ESM and lacks the `pino-transport` package marker, so
// pino.transport() can't resolve it by name — use the resolved path.
const lokiTransport = pino.transport({
  target: require.resolve('pino-loki'),
  options: {
    host: lokiUrl,
    batching: true,
    interval: 5,
    labels: { service: SERVICE_NAME, environment },
    silenceErrors: true,
  },
});

export const logger = pino(
  {
    level: logLevel,
    base: { service: SERVICE_NAME, environment },
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
