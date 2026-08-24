import express from 'express';
import type { AddressInfo } from 'node:net';
import { afterEach, describe, expect, it } from 'vitest';
import { healthCheckFactory, resetHealthCacheForTests } from './HealthController.js';

/**
 * WP5 (`SidecarHealthChecksArePlacebo`) — `/health` used to answer a hardcoded
 * `healthy` with the message "Skeleton — not yet connected". It now probes the
 * chain, which matters because `PlatformHealthProbeJob` freezes payment-phase
 * transaction timeouts on this signal's degraded edge (02 §3.3): a constant
 * `healthy` meant the outage could never be detected and the freeze could never
 * fire.
 */
async function serve(probeBlock: () => Promise<number>) {
  const app = express();
  app.get('/health', healthCheckFactory({ probeBlock }));
  const server = await new Promise<import('http').Server>((resolve) => {
    const s = app.listen(0, () => resolve(s));
  });
  const port = (server.address() as AddressInfo).port;
  return {
    url: `http://127.0.0.1:${port}/health`,
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
}

describe('blockchain sidecar /health', () => {
  afterEach(() => resetHealthCacheForTests());

  it('reports healthy with 200 and names the observed block', async () => {
    const s = await serve(() => Promise.resolve(65_000_123));
    try {
      const res = await fetch(s.url);
      expect(res.status).toBe(200);
      const body = (await res.json()) as {
        status: string;
        checks: Array<{ name: string; message?: string }>;
      };
      expect(body.status).toBe('healthy');
      expect(body.checks[0].name).toBe('tron-node');
      // The block number is the evidence the probe actually reached the chain
      // rather than merely that the process is up.
      expect(body.checks[0].message).toContain('65000123');
    } finally {
      await s.close();
    }
  });

  it('reports unhealthy with 503 when the node is unreachable', async () => {
    // The case the whole change exists for — before WP5 this still answered 200
    // healthy and the platform never learned the chain was unreachable.
    const s = await serve(() => Promise.reject(new Error('ECONNREFUSED')));
    try {
      const res = await fetch(s.url);
      expect(res.status).toBe(503);
      const body = (await res.json()) as {
        status: string;
        checks: Array<{ status: string; message?: string }>;
      };
      expect(body.status).toBe('unhealthy');
      expect(body.checks[0].message).toContain('ECONNREFUSED');
    } finally {
      await s.close();
    }
  });

  it('does not report configuration gaps as health', async () => {
    // Liveness only, on purpose. An unset HOT_WALLET_ADDRESS is a steady state
    // of a supported deployment; reporting it here would leave the sidecar
    // permanently non-healthy and — after WP1 — every payment-phase timeout
    // frozen forever.
    const s = await serve(() => Promise.resolve(1));
    try {
      const body = (await (await fetch(s.url)).json()) as { checks: Array<{ name: string }> };
      expect(body.checks.map((c) => c.name)).toEqual(['tron-node']);
    } finally {
      await s.close();
    }
  });

  it('caches the probe so a polling loop does not spend the TronGrid rate limit', async () => {
    let calls = 0;
    const s = await serve(() => {
      calls++;
      return Promise.resolve(1);
    });
    try {
      await fetch(s.url);
      await fetch(s.url);
      await fetch(s.url);
      expect(calls).toBe(1);
    } finally {
      await s.close();
    }
  });
});
