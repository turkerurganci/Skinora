export const config = {
  port: parseInt(process.env.PORT || '5100', 10),
  nodeEnv: process.env.NODE_ENV || 'development',

  // Backend communication
  backendUrl: process.env.BACKEND_URL || 'http://skinora-backend:5000',
  internalKey: process.env.INTERNAL_KEY || '',
  webhookSecret: process.env.WEBHOOK_SECRET || '',

  // Steam API
  steamApiKey: process.env.STEAM_API_KEY || '',

  // Logging
  lokiUrl: process.env.LOKI_URL || 'http://skinora-loki:3100',
  logLevel: process.env.LOG_LEVEL || 'info',

  // Rate limiting
  steamTradeOfferLimitPerMinute: 5,
  steamWebApiRequestsPerSecond: 1,

  // Graceful shutdown
  shutdownTimeoutMs: 10_000,
} as const;
