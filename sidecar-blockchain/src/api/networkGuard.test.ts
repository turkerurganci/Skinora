import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import express from 'express';
import type { AddressInfo } from 'node:net';
import { describe, expect, it } from 'vitest';
import { networkGuard } from './middleware.js';

/**
 * T148 (05 §3.3a) — this service is TRON's. A request body naming another
 * network means the backend resolved that network's service to this address;
 * the guard refuses it (400 NETWORK_MISMATCH, which the backend treats as a
 * rejected, non-retried request) so nothing is signed for the wrong chain.
 */
async function serve() {
  const app = express();
  app.use(express.json());
  app.use(networkGuard);
  const reached: unknown[] = [];
  app.post('/api/echo', (req, res) => {
    reached.push(req.body);
    res.status(200).json({ ok: true });
  });
  app.get('/api/echo', (_req, res) => {
    reached.push('GET');
    res.status(200).json({ ok: true });
  });
  const server = await new Promise<import('http').Server>((r) => {
    const s = app.listen(0, () => r(s));
  });
  const port = (server.address() as AddressInfo).port;
  return {
    reached,
    post: (body: unknown) =>
      fetch(`http://127.0.0.1:${port}/api/echo`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      }),
    get: () => fetch(`http://127.0.0.1:${port}/api/echo`),
    close: () => new Promise<void>((r) => server.close(() => r())),
  };
}

function contract(path: string): Record<string, unknown> {
  return JSON.parse(readFileSync(resolve(__dirname, '../../contracts', path), 'utf8')) as Record<
    string,
    unknown
  >;
}

describe('networkGuard', () => {
  it.each([
    ['monitor-start/payment.request.json'],
    ['monitor-start/holding.request.json'],
    ['estimate-fee/payout.request.json'],
    ['estimate-fee/refund.request.json'],
  ])('passes the backend body %s, which names TRON', async (file) => {
    const s = await serve();
    try {
      const res = await s.post(contract(file));
      expect(res.status).toBe(200);
      expect(s.reached).toHaveLength(1);
    } finally {
      await s.close();
    }
  });

  it.each([['ETHEREUM'], ['tron'], [''], [1], [null]])(
    'refuses a body naming network %j with 400 NETWORK_MISMATCH and never reaches the handler',
    async (network) => {
      const s = await serve();
      try {
        const res = await s.post({ ...contract('monitor-start/payment.request.json'), network });
        expect(res.status).toBe(400);
        expect(await res.json()).toMatchObject({ error: 'NETWORK_MISMATCH' });
        expect(s.reached).toHaveLength(0);
      } finally {
        await s.close();
      }
    },
  );

  it('accepts a body without the field — operator calls from the runbook predate it', async () => {
    const s = await serve();
    try {
      const body = contract('monitor-start/payment.request.json');
      delete body.network;
      const res = await s.post(body);
      expect(res.status).toBe(200);
      expect(s.reached).toHaveLength(1);
    } finally {
      await s.close();
    }
  });

  it('leaves GET routes alone', async () => {
    const s = await serve();
    try {
      const res = await s.get();
      expect(res.status).toBe(200);
      expect(s.reached).toEqual(['GET']);
    } finally {
      await s.close();
    }
  });
});
