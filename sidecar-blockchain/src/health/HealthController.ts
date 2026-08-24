import type { Request, Response } from 'express';
import { TronGridClient } from '../tron/TronGridClient.js';

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
 * How long the Tron probe may take before the node counts as unreachable.
 * Short on purpose: the caller is a periodic liveness probe, and a node that
 * needs longer than this to return its head block is not one the transfer path
 * can rely on either.
 */
const PROBE_TIMEOUT_MS = 4000;

/**
 * The probe result is cached for this long so a tight polling loop (or several
 * probers at once) cannot turn the health endpoint into its own load source on
 * TronGrid, whose rate limit the transfer path also depends on.
 */
const CACHE_TTL_MS = 5000;

export interface HealthDeps {
  /** Override the chain probe. Defaults to a real TronGridClient call. */
  probeBlock?: () => Promise<number>;
}

let cached: { at: number; check: HealthCheck } | null = null;

/**
 * Factory for the `/health` handler.
 *
 * WP5 (`SidecarHealthChecksArePlacebo`) — this used to answer a hardcoded
 * `healthy` with the message "Skeleton — not yet connected", left over from the
 * pre-T70 skeleton. That was harmless while nothing consumed it, and stopped
 * being harmless in WP1: `PlatformHealthProbeJob` now freezes active
 * transaction timeouts on the degraded edge of exactly this signal. A constant
 * `healthy` means the outage it exists to detect can never be detected and the
 * freeze can never fire — a control that reports success by construction.
 *
 * The check now measures something real:
 *  - `tron-node` asks TronGrid for its current solid block. It is the cheapest
 *    call that still proves the whole path works (DNS, TLS, API key, node
 *    liveness) rather than merely that the process is up.
 *
 * LIVENESS ONLY, deliberately. Configuration completeness is not a check here:
 * an unset HOT_WALLET_ADDRESS is a real limitation (nothing can be broadcast)
 * but it is a steady state of a supported deployment, and after WP1 a
 * permanently non-healthy sidecar would keep every payment-phase timeout frozen
 * forever. A health signal that can never return to healthy is not a signal —
 * that is the same trap T133 removed from the steam sidecar. Config gaps belong
 * in a startup warning, the way WP1 surfaced the PRICE_DEVIATION one.
 */
export function healthCheckFactory(deps: HealthDeps = {}) {
  // Injectable so tests stay hermetic — a unit test must not depend on TronGrid
  // being reachable, or it reports the network instead of the code.
  const probeBlock = deps.probeBlock ?? (() => new TronGridClient().getNowSolidBlock());

  return async function healthCheck(_req: Request, res: Response): Promise<void> {
    const checks: HealthCheck[] = [await probeTronNode(probeBlock)];

    const overallStatus = checks.every((c) => c.status === 'healthy')
      ? 'healthy'
      : checks.some((c) => c.status === 'unhealthy')
        ? 'unhealthy'
        : 'degraded';

    const response: HealthResponse = {
      status: overallStatus,
      service: 'skinora-blockchain-sidecar',
      uptime: process.uptime(),
      checks,
    };

    const statusCode = overallStatus === 'unhealthy' ? 503 : 200;
    res.status(statusCode).json(response);
  };
}

async function probeTronNode(probeBlock: () => Promise<number>): Promise<HealthCheck> {
  const now = Date.now();
  if (cached && now - cached.at < CACHE_TTL_MS) return cached.check;

  const check = await runTronProbe(probeBlock);
  cached = { at: now, check };
  return check;
}

async function runTronProbe(probeBlock: () => Promise<number>): Promise<HealthCheck> {
  try {
    const block = await withTimeout(probeBlock(), PROBE_TIMEOUT_MS);
    return {
      name: 'tron-node',
      status: 'healthy',
      message: `solid block ${block}`,
    };
  } catch (error) {
    // Unhealthy, not degraded: without a reachable node this sidecar cannot
    // confirm a payment or broadcast a transfer, which is all it is for.
    return {
      name: 'tron-node',
      status: 'unhealthy',
      message: error instanceof Error ? error.message : 'probe failed',
    };
  }
}

function withTimeout<T>(promise: Promise<T>, ms: number): Promise<T> {
  return new Promise<T>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error(`probe timed out after ${ms}ms`)), ms);
    promise.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      (error: unknown) => {
        clearTimeout(timer);
        reject(error instanceof Error ? error : new Error(String(error)));
      },
    );
  });
}

/** Test seam — drops the cached probe result so cases do not leak into each other. */
export function resetHealthCacheForTests(): void {
  cached = null;
}
