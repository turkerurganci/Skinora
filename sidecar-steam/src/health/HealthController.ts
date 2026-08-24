import type { Request, Response } from 'express';

interface HealthCheck {
  name: string;
  status: 'healthy' | 'degraded' | 'unhealthy';
  message?: string;
}

interface HealthResponse {
  status: 'healthy' | 'degraded' | 'unhealthy';
  service: string;
  uptime: number;
  checks: HealthCheck[];
}

/**
 * Steam's own liveness endpoint. Keyless and cheap, and — the part that matters
 * here — it lives on the Web API host, NOT on steamcommunity.com. The community
 * host is the one under the tight per-IP limit that delivery verification
 * depends on (08 §2.6), and a liveness probe must never spend from that budget.
 */
const STEAM_SERVER_INFO_URL = 'https://api.steampowered.com/ISteamWebAPIUtil/GetServerInfo/v1/';

const PROBE_TIMEOUT_MS = 4000;

/**
 * Probe result cache. A periodic prober plus a container healthcheck can easily
 * poll this endpoint every few seconds; without the cache each of those becomes
 * an outbound Steam request.
 */
const CACHE_TTL_MS = 5000;

type FetchLike = (
  url: string,
  init?: { signal?: AbortSignal },
) => Promise<{ ok: boolean; status: number }>;

export interface HealthDeps {
  /** Override the outbound probe. Defaults to the global fetch. */
  fetchImpl?: FetchLike;
}

let cached: { at: number; check: HealthCheck } | null = null;

/**
 * Factory for the `/health` handler.
 *
 * WP5 (`SidecarHealthChecksArePlacebo`) — the `steam-api` check used to be a
 * hardcoded `healthy` carrying the message "Connectivity probe deferred to
 * T67". Harmless while nothing consumed it; no longer harmless after WP1, where
 * `PlatformHealthProbeJob` began freezing active transaction timeouts on the
 * degraded edge of this exact signal. A constant `healthy` means a real Steam
 * outage never reaches that logic and the freeze never fires — 02 §3.3's
 * automatic detection resting on a value that cannot change.
 *
 * T133's rule is kept and extended: this endpoint reports LIVENESS ONLY.
 * Configuration completeness is deliberately NOT a check here. A missing
 * STEAM_API_KEY is a real limitation (Mobile Authenticator verification stops
 * working) but it is a steady state of a supported deployment, and after WP1 a
 * permanently non-healthy sidecar would keep every Steam-bound transaction
 * timeout frozen forever. That is the failure T133 removed when it deleted the
 * bot-session check, and it must not come back through a different door —
 * config gaps belong in a startup warning, the way WP1 surfaced the
 * PRICE_DEVIATION one.
 */
export function healthCheckFactory(deps: HealthDeps = {}) {
  // Injectable so tests stay hermetic. A health probe that reaches the real
  // Steam API from a unit test would be both slow and flaky, and would report
  // the network rather than the code.
  const fetchImpl = deps.fetchImpl ?? globalThis.fetch;
  return async function healthCheck(_req: Request, res: Response): Promise<void> {
    const checks: HealthCheck[] = [await probeSteam(fetchImpl)];

    const overallStatus = checks.every((c) => c.status === 'healthy')
      ? 'healthy'
      : checks.some((c) => c.status === 'unhealthy')
        ? 'unhealthy'
        : 'degraded';

    const response: HealthResponse = {
      status: overallStatus,
      service: 'skinora-steam-sidecar',
      uptime: process.uptime(),
      checks,
    };

    const statusCode = overallStatus === 'unhealthy' ? 503 : 200;
    res.status(statusCode).json(response);
  };
}

async function probeSteam(fetchImpl: FetchLike): Promise<HealthCheck> {
  const now = Date.now();
  if (cached && now - cached.at < CACHE_TTL_MS) return cached.check;

  const check = await runSteamProbe(fetchImpl);
  cached = { at: now, check };
  return check;
}

async function runSteamProbe(fetchImpl: FetchLike): Promise<HealthCheck> {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), PROBE_TIMEOUT_MS);
  try {
    const response = await fetchImpl(STEAM_SERVER_INFO_URL, { signal: controller.signal });
    if (!response.ok) {
      return {
        name: 'steam-api',
        status: 'unhealthy',
        message: `GetServerInfo returned HTTP ${response.status}`,
      };
    }
    return { name: 'steam-api', status: 'healthy', message: 'GetServerInfo reachable' };
  } catch (error) {
    // Unhealthy rather than degraded: with Steam unreachable this sidecar
    // cannot read an inventory or check a trade hold, which is everything it
    // does. That is precisely the outage 02 §3.3 wants the timeouts frozen for.
    return {
      name: 'steam-api',
      status: 'unhealthy',
      message: error instanceof Error ? error.message : 'probe failed',
    };
  } finally {
    clearTimeout(timer);
  }
}

/** Test seam — drops the cached probe result so cases do not leak into each other. */
export function resetHealthCacheForTests(): void {
  cached = null;
}
