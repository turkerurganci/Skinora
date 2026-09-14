import express from 'express';
import type { AddressInfo } from 'node:net';
import { afterEach, describe, expect, it } from 'vitest';
import { healthCheckFactory, resetHealthCacheForTests } from './HealthController.js';

/**
 * WP5 (`SidecarHealthChecksArePlacebo`) — `/health` used to answer a hardcoded
 * `healthy`. It now probes Steam, which matters because `PlatformHealthProbeJob`
 * freezes active transaction timeouts on this signal's degraded edge (02 §3.3):
 * a constant `healthy` meant the outage could never be detected and the freeze
 * could never fire.
 */
async function serve(
  fetchImpl: () => Promise<{ ok: boolean; status: number }>,
  communityHealth?: { isUnhealthy(): boolean },
) {
  const app = express();
  app.get('/health', healthCheckFactory({ fetchImpl, communityHealth }));
  const server = await new Promise<import('http').Server>((resolve) => {
    const s = app.listen(0, () => resolve(s));
  });
  const port = (server.address() as AddressInfo).port;
  return {
    url: `http://127.0.0.1:${port}/health`,
    close: () => new Promise<void>((resolve) => server.close(() => resolve())),
  };
}

describe('steam sidecar /health', () => {
  afterEach(() => resetHealthCacheForTests());

  it('reports healthy with 200 when Steam answers', async () => {
    const s = await serve(() => Promise.resolve({ ok: true, status: 200 }));
    try {
      const res = await fetch(s.url);
      expect(res.status).toBe(200);
      const body = (await res.json()) as { status: string; checks: Array<{ name: string }> };
      expect(body.status).toBe('healthy');
      expect(body.checks.map((c) => c.name)).toContain('steam-api');
    } finally {
      await s.close();
    }
  });

  it('reports unhealthy with 503 when Steam is unreachable', async () => {
    // The case the whole change exists for — before WP5 this still answered 200
    // healthy and the platform never learned Steam was down.
    const s = await serve(() => Promise.reject(new Error('ENOTFOUND')));
    try {
      const res = await fetch(s.url);
      expect(res.status).toBe(503);
      const body = (await res.json()) as {
        status: string;
        checks: Array<{ name: string; status: string; message?: string }>;
      };
      expect(body.status).toBe('unhealthy');
      expect(body.checks[0].status).toBe('unhealthy');
      expect(body.checks[0].message).toContain('ENOTFOUND');
    } finally {
      await s.close();
    }
  });

  it('reports unhealthy when Steam answers with an error status', async () => {
    const s = await serve(() => Promise.resolve({ ok: false, status: 503 }));
    try {
      const res = await fetch(s.url);
      expect(res.status).toBe(503);
      const body = (await res.json()) as { checks: Array<{ message?: string }> };
      expect(body.checks[0].message).toContain('503');
    } finally {
      await s.close();
    }
  });

  it('does not report configuration gaps as health', async () => {
    // Liveness only, on purpose. A missing STEAM_API_KEY is a steady state of a
    // supported deployment; reporting it here would leave the sidecar
    // permanently non-healthy and — after WP1 — every Steam-bound timeout
    // frozen forever. That is the trap T133 removed with the bot-session check.
    const s = await serve(() => Promise.resolve({ ok: true, status: 200 }));
    try {
      const body = (await (await fetch(s.url)).json()) as { checks: Array<{ name: string }> };
      const names = body.checks.map((c) => c.name);
      expect(names).toEqual(['steam-api']);
      expect(names).not.toContain('steam-api-key');
      expect(names).not.toContain('bot-session');
    } finally {
      await s.close();
    }
  });

  it('caches the probe so a polling loop does not become its own load source', async () => {
    let calls = 0;
    const s = await serve(() => {
      calls++;
      return Promise.resolve({ ok: true, status: 200 });
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

  // --- 08 §2.2a — Steam Community, ikinci ve bağımsız arıza yüzeyi ---

  it('community sağlığı verilmezse kontrol HİÇ raporlanmaz — bilmediğini iddia etmez', async () => {
    const s = await serve(() => Promise.resolve({ ok: true, status: 200 }));
    try {
      const body = (await (await fetch(s.url)).json()) as { checks: Array<{ name: string }> };
      expect(body.checks.map((c) => c.name)).not.toContain('steam-community');
    } finally {
      await s.close();
    }
  });

  it('Web API ayakta ama community çökmüşse SAĞLIKSIZ döner', async () => {
    // Bu turun açtığı açık: 08 §2.2a kapısı community'ye bağlı, ama /health
    // yalnız Web API'ye bakıyordu. O hâliyle community kesintisinde kapı her
    // işlemi reddederken PlatformHealthProbeJob timeout'ları dondurmuyordu.
    const s = await serve(
      () => Promise.resolve({ ok: true, status: 200 }),
      { isUnhealthy: () => true },
    );
    try {
      const res = await fetch(s.url);
      expect(res.status).toBe(503);
      const body = (await res.json()) as {
        status: string;
        checks: Array<{ name: string; status: string }>;
      };
      expect(body.status).toBe('unhealthy');
      const community = body.checks.find((c) => c.name === 'steam-community');
      expect(community?.status).toBe('unhealthy');
    } finally {
      await s.close();
    }
  });

  it('community sağlıklıyken genel durum sağlıklı kalır', async () => {
    const s = await serve(
      () => Promise.resolve({ ok: true, status: 200 }),
      { isUnhealthy: () => false },
    );
    try {
      const res = await fetch(s.url);
      expect(res.status).toBe(200);
      const body = (await res.json()) as { status: string; checks: Array<{ name: string }> };
      expect(body.status).toBe('healthy');
      expect(body.checks.map((c) => c.name)).toContain('steam-community');
    } finally {
      await s.close();
    }
  });
});
