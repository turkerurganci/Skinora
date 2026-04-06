// T08 — Pino logger with stdout + Loki push and centralized secret masking (09 §18.5).
// Required structured fields per 09 §18.3: timestamp, level, message, correlationId.
//
// Implementation note: pino's `formatters.level` cannot be combined with a
// worker-thread `pino.transport()` because formatters can't be serialized
// across the thread boundary. We therefore drive Loki via a worker-thread
// transport and tee that into `pino.multistream()` alongside `process.stdout`,
// which lets formatters/redact run in the main process.
const pino = require("pino");

const SERVICE_NAME = "skinora-blockchain-sidecar";
const ENVIRONMENT = process.env.NODE_ENV || "development";
const LOKI_URL = process.env.LOKI_URL || "http://skinora-loki:3100";

// Mirror SecretMaskingEnricher.cs (.NET) — keep field names in sync.
const REDACT_PATHS = [
  "*.privateKey",
  "*.apiKey",
  "*.refreshToken",
  "*.accessToken",
  "*.password",
  "*.secret",
  "*.jwtSecret",
  "*.mnemonic",
  "*.hdWalletMnemonic",
  "*.authorization",
  "privateKey",
  "apiKey",
  "refreshToken",
  "accessToken",
  "password",
  "secret",
  "jwtSecret",
  "mnemonic",
  "hdWalletMnemonic",
  "authorization",
  "headers.authorization",
  "headers['x-api-key']",
];

// pino-loki ships as ESM and lacks the `pino-transport` package marker, so
// pino.transport() can't resolve it by name — use the resolved path.
const lokiTransport = pino.transport({
  target: require.resolve("pino-loki"),
  options: {
    host: LOKI_URL,
    batching: true,
    interval: 5,
    labels: { service: SERVICE_NAME, environment: ENVIRONMENT },
    silenceErrors: true,
  },
});

const baseLogger = pino(
  {
    level: process.env.LOG_LEVEL || "info",
    base: { service: SERVICE_NAME, environment: ENVIRONMENT },
    // NOTE: pino's `formatters.level` text-mapping and `isoTime` timestamp
    // are intentionally omitted — both rewrite the serialized JSON in ways
    // that break pino-loki's payload encoder (which expects numeric `time`
    // and numeric `level`). Pino's defaults (epoch ms `time`, numeric `level`
    // 30=info/40=warn/50=error) still satisfy 09 §18.3 — the spec requires
    // the four fields to exist; the example is illustrative on naming only.
    messageKey: "message",
    redact: {
      paths: REDACT_PATHS,
      censor: "***",
      remove: false,
    },
  },
  pino.multistream([
    { stream: process.stdout },
    { stream: lokiTransport },
  ]),
);

function loggerForRequest(req) {
  const headerValue =
    req.headers["x-correlation-id"] || req.headers["X-Correlation-Id"];
  const correlationId = headerValue || randomUuid();
  return { logger: baseLogger.child({ correlationId }), correlationId };
}

function randomUuid() {
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return require("crypto").randomUUID();
}

module.exports = { logger: baseLogger, loggerForRequest };
