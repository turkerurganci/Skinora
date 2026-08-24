/**
 * Read a positive integer from the environment, falling back when the value is
 * missing, non-numeric or non-positive. Rate limits must fail safe: a `NaN`
 * ceiling silently disables throttling (`length >= NaN` is always false), which
 * would turn a typo into a Steam IP ban rather than a startup error.
 */
function positiveIntFromEnv(raw: string | undefined, fallback: number): number {
  const parsed = parseInt(raw ?? '', 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

export const config = {
  port: parseInt(process.env.PORT || '5100', 10),
  nodeEnv: process.env.NODE_ENV || 'development',

  // Backend communication
  backendUrl: process.env.BACKEND_URL || 'http://skinora-backend:5000',
  internalKey: process.env.INTERNAL_KEY || '',

  // Steam API
  steamApiKey: process.env.STEAM_API_KEY || '',

  // Inventory cache — 08 §2.3 (Redis-backed, 2 minute TTL). Empty URL falls back
  // to in-memory cache (suitable for tests and single-process dev runs).
  redisUrl: process.env.REDIS_URL || '',

  // Logging
  lokiUrl: process.env.LOKI_URL || 'http://skinora-loki:3100',
  logLevel: process.env.LOG_LEVEL || 'info',

  // Rate limiting — 08 §2.6. The Web API and the Steam Community endpoint are
  // limited independently and therefore run in SEPARATE queues (T120).
  steamWebApiRequestsPerSecond: 1,
  // Community inventory endpoint (08 §2.6: "~10-20 istek/dakika (IP başına)",
  // undocumented by Valve and explicitly an estimate). The conservative end of
  // that range is the default because overshooting is punished with an IP-level
  // block, while undershooting only slows delivery verification. Tunable
  // without a rebuild.
  //
  // WP7 — T122 MEASURED this (2026-08-13) and the result is worse than the
  // default suggests: at exactly 10/min (6s spacing) 429s began after ~90
  // seconds / ~18 requests, and they KEPT COMING even after slowing to 4/min.
  // The limiter therefore counts a window LONGER than a minute, so a queue that
  // only paces per-minute can still exhaust it. No Retry-After header was
  // returned, so backoff is a guess. The run came from a residential IP;
  // production egresses from a datacenter range, where Steam is stricter — the
  // real ceiling may be lower still. The value was NOT changed on that basis:
  // lowering it needs a second measurement from the production IP (08 §2.6).
  steamCommunityRequestsPerMinute: positiveIntFromEnv(
    process.env.STEAM_COMMUNITY_REQUESTS_PER_MINUTE,
    10,
  ),

  // Graceful shutdown
  shutdownTimeoutMs: 10_000,
} as const;
