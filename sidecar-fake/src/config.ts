/**
 * Fake-sidecar configuration (T107 E2E). Every value is environment-driven so
 * the same image can stand in for BOTH real sidecars inside
 * `docker-compose.e2e.yml`. This service is NEVER deployed to production.
 */
export const config = {
  nodeEnv: process.env.NODE_ENV || 'development',
  logLevel: process.env.LOG_LEVEL || 'info',

  // Two HTTP servers run in one process: 5100 answers the Steam sidecar
  // surface, 5200 the blockchain sidecar surface. Compose network aliases map
  // both `skinora-steam-sidecar` and `skinora-blockchain-sidecar` hostnames
  // here, so the backend reaches each surface on its expected port.
  steamPort: parseInt(process.env.STEAM_PORT || '5100', 10),
  blockchainPort: parseInt(process.env.BLOCKCHAIN_PORT || '5200', 10),

  // Backend base URL the fake posts inbound webhooks to (05 §3.4).
  backendUrl: process.env.BACKEND_URL || 'http://skinora-backend:5000',

  // X-Internal-Key the backend attaches on outbound calls. Validated only when
  // set (empty = skip, mirrors sidecar-steam internalKeyAuth).
  internalKey: process.env.INTERNAL_KEY || '',

  // Separate HMAC secrets per sidecar — the backend's WebhookSignatureMiddleware
  // selects the secret by route prefix, so steam webhooks MUST be signed with
  // the steam secret and blockchain webhooks with the blockchain secret.
  steamWebhookSecret: process.env.STEAM_WEBHOOK_SECRET || process.env.WEBHOOK_SECRET || '',
  blockchainWebhookSecret:
    process.env.BLOCKCHAIN_WEBHOOK_SECRET || process.env.WEBHOOK_SECRET || '',

  // Delay before the fake self-emits `trade_offer.accepted` after answering a
  // `POST /api/trade-offers/send`. Gives the dispatch job time to commit the
  // TRADE_OFFER_SENT_TO_* state before the acceptance webhook arrives.
  tradeAcceptDelayMs: parseInt(process.env.FAKE_TRADE_ACCEPT_DELAY_MS || '2000', 10),

  // SteamId reported as the escrow bot in trade webhooks. Match the seeded
  // ACTIVE PlatformSteamBot so any bot-identity assertion lines up.
  botSteamId: process.env.FAKE_BOT_STEAM_ID || '76561190000000001',

  // Finality the fake reports for outgoing transfers (>= 20 = confirmed).
  transferConfirmations: parseInt(process.env.FAKE_TRANSFER_CONFIRMATIONS || '25', 10),

  // SQL Server connection — used ONLY by the /__e2e control endpoints to
  // resolve a transaction's deposit PaymentAddress (id + expected amount/token)
  // so the inbound payment webhook carries backend-valid values.
  db: {
    server: process.env.DB_SERVER || 'skinora-db',
    port: parseInt(process.env.DB_PORT_INTERNAL || '1433', 10),
    user: process.env.DB_USER || 'sa',
    password: process.env.DB_PASSWORD || '',
    database: process.env.DB_NAME || 'Skinora',
  },

  shutdownTimeoutMs: 10_000,
} as const;
