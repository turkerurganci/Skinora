import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { createApp } from './routes.js';
import { NOT_YET_IMPLEMENTED } from './notImplemented.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import type { EthFeeEstimationService } from '../fee/FeeEstimationService.js';

const KEY = 'i'.repeat(64);
const MNEMONIC = `${'abandon '.repeat(11)}about`;

let finality: FinalityStatus = {
  state: 'ok',
  snapshot: { number: 100n, timestamp: 0, readAt: 0, provider: 'alchemy' },
  ageSeconds: 900,
  lastError: null,
};
let server: Server;
let base: string;

beforeAll(async () => {
  const app = createApp({
    internalKey: KEY,
    chainId: 11155111,
    wallet: new EthereumHdWallet(MNEMONIC),
    finality: () => finality,
    // T154 — covered by feeHandlers.test.ts; never reached here.
    feeEstimator: {} as EthFeeEstimationService,
  });
  server = app.listen(0, '127.0.0.1');
  await new Promise((resolve) => server.once('listening', resolve));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  await new Promise((resolve) => server.close(resolve));
});

function post(path: string, body: unknown, key: string | null = KEY) {
  return fetch(base + path, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      ...(key === null ? {} : { 'x-internal-key': key }),
    },
    body: JSON.stringify(body),
  });
}

describe('internal key (05 §3.4) — always required', () => {
  it.each([
    ['missing', null],
    ['wrong', 'w'.repeat(64)],
    ['a prefix of the right key', KEY.slice(0, 10)],
  ])('refuses a %s key with 401', async (_label, key) => {
    const res = await post('/api/wallet/derive', { network: 'ETHEREUM', index: 0 }, key);
    expect(res.status).toBe(401);
  });
});

describe('network guard (05 §3.3a)', () => {
  it.each([
    ['TRON', { network: 'TRON', index: 0 }],
    ['no network field', { index: 0 }],
    ['a lowercase name', { network: 'ethereum', index: 0 }],
  ])('refuses %s with 400 NETWORK_MISMATCH', async (_label, body) => {
    const res = await post('/api/wallet/derive', body);
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'NETWORK_MISMATCH' });
  });
});

describe('POST /api/wallet/derive', () => {
  it('derives the T143 vector at index 0 (EIP-55, absolute path)', async () => {
    const res = await post('/api/wallet/derive', {
      network: 'ETHEREUM',
      index: 0,
      transactionId: '00000000-0000-0000-0000-000000000001',
    });
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({
      address: '0x9858EfFD232B4033E47d90003D41EC34EcaEda94',
      derivationPath: "m/44'/60'/0'/0/0",
      index: 0,
    });
  });

  it.each([-1, 1.5, 'zero', 2_147_483_648])('refuses index %s with 400', async (index) => {
    const res = await post('/api/wallet/derive', { network: 'ETHEREUM', index });
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'INVALID_DERIVATION_INDEX' });
  });
});

describe('endpoints of the contract this skeleton does not serve yet', () => {
  it.each(NOT_YET_IMPLEMENTED.map((e) => [e.method.toUpperCase(), e.path, e.task]))(
    '%s %s → 501 NOT_IMPLEMENTED (%s)',
    async (method, path, task) => {
      const url = base + '/api' + path.replace(':txHash', '0x' + 'a'.repeat(64));
      const res = await fetch(url, {
        method,
        headers: { 'content-type': 'application/json', 'x-internal-key': KEY },
        body: method === 'POST' ? JSON.stringify({ network: 'ETHEREUM' }) : undefined,
      });
      expect(res.status).toBe(501);
      expect(await res.json()).toMatchObject({ error: 'NOT_IMPLEMENTED', retryable: true });
      expect(task).toMatch(/^T15[1-4]$/);
    },
  );

  it('are still behind the internal key', async () => {
    const res = await post('/api/transfer/payout', { network: 'ETHEREUM' }, null);
    expect(res.status).toBe(401);
  });
});

describe('GET /health (no key — internal network only)', () => {
  it('is 200 healthy with the network and chain when finality is fresh', async () => {
    finality = { ...finality, state: 'ok' };
    const res = await fetch(base + '/health');
    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({
      status: 'healthy',
      service: 'skinora-ethereum-sidecar',
      network: 'ETHEREUM',
      chainId: 11155111,
      finalized: { blockNumber: '100', ageSeconds: 900, provider: 'alchemy' },
    });
  });

  it('is 200 degraded while only the backup answers', async () => {
    finality = { ...finality, state: 'backup' };
    const res = await fetch(base + '/health');
    expect(res.status).toBe(200);
    expect(((await res.json()) as { status: string }).status).toBe('degraded');
  });

  it.each(['stalled', 'unreadable', 'starting'] as const)(
    'is 503 unhealthy when %s',
    async (state) => {
      finality = { ...finality, state };
      const res = await fetch(base + '/health');
      expect(res.status).toBe(503);
      expect(((await res.json()) as { status: string }).status).toBe('unhealthy');
    },
  );
});

describe('GET /metrics', () => {
  it('exposes the service metrics without a key', async () => {
    const res = await fetch(base + '/metrics');
    expect(res.status).toBe(200);
    expect(await res.text()).toContain('skinora_ethereum_finalized_block_age_seconds');
  });
});
