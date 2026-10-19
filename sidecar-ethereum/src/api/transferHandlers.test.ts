import { resolve } from 'node:path';
import { readFileSync } from 'node:fs';
import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { createApp } from './routes.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { SidecarError } from '../errors/SidecarError.js';
import { TransferDeferredError } from '../transfer/FeePolicy.js';
import type { TransferService } from '../transfer/TransferService.js';
import type { EthFeeEstimationService } from '../fee/FeeEstimationService.js';

/**
 * T152 — the HTTP answers of the send endpoints and what each means to the
 * backend (HttpBlockchainTransferClient): 200 signed, 400 refused for good,
 * 503 + deferred waits without spending a retry, 502 transient; status 404 /
 * 409 are row-specific (the confirmation job keeps going), 502 is the network.
 */

/** T158 — an example body both sides of the contract test against (sidecar-ethereum/contracts/). */
function example(path: string): Record<string, unknown> {
  return JSON.parse(readFileSync(resolve(__dirname, '../../contracts', path), 'utf8')) as Record<
    string,
    unknown
  >;
}

const KEY = 'i'.repeat(64);
const ID = '00000000-0000-0000-0000-000000000001';
const SELLER = '0x1111111111111111111111111111111111111111';
const DEPOSIT = '0x9858EfFD232B4033E47d90003D41EC34EcaEda94';
const HASH = '0x' + 'ab'.repeat(32);

let next: () => Promise<unknown>;
const calls: Array<{ method: string; request: unknown }> = [];

const fake = {
  payout: (request: unknown) => (calls.push({ method: 'payout', request }), next()),
  refund: (request: unknown) => (calls.push({ method: 'refund', request }), next()),
  sweep: (request: unknown) => (calls.push({ method: 'sweep', request }), next()),
  coldWalletTransfer: (request: unknown) => (calls.push({ method: 'cold', request }), next()),
  status: (hash: unknown, expected: unknown) => (
    calls.push({ method: 'status', request: { hash, expected } }),
    next()
  ),
} as unknown as TransferService;

let server: Server;
let base: string;

beforeAll(async () => {
  const app = createApp({
    internalKey: KEY,
    chainId: 11155111,
    wallet: new EthereumHdWallet(`${'abandon '.repeat(11)}about`),
    finality: () => ({ state: 'ok', snapshot: null, ageSeconds: null, lastError: null }),
    transfers: fake,
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

beforeEach(() => {
  calls.length = 0;
  next = () => Promise.resolve({ txHash: HASH, nonce: 3, replayed: false });
});

function post(path: string, body: unknown) {
  return fetch(base + '/api' + path, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-key': KEY },
    body: JSON.stringify(body),
  });
}

const PAYOUT = {
  network: 'ETHEREUM',
  blockchainTransactionId: ID,
  toAddress: SELLER,
  amount: '12.5',
  token: 'USDT',
  feeCapUsd: '0.5',
};

describe('POST /api/transfer/payout', () => {
  it('200 { txHash, nonce, replayed } and the fields reach the service', async () => {
    const res = await post('/transfer/payout', PAYOUT);
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ txHash: HASH, nonce: 3, replayed: false });
    expect(calls[0]).toEqual({
      method: 'payout',
      request: {
        blockchainTransactionId: ID,
        toAddress: SELLER,
        amount: '12.5',
        token: 'USDT',
        feeCapUsd: '0.5',
      },
    });
  });

  it.each([
    ['no feeCapUsd', { feeCapUsd: undefined }],
    ['an id that is not a GUID', { blockchainTransactionId: '../x' }],
    ['a TRON address', { toAddress: 'TXYZopYRdj2D9XRtbG411XZZ3kM5VkAeBf' }],
    ['a bad EIP-55 checksum', { toAddress: '0x9858efFD232B4033E47d90003D41EC34EcaEda94' }],
    ['an unknown token', { token: 'DAI' }],
    ['a numeric amount', { amount: 12.5 }],
  ])('400 INVALID_TRANSFER_REQUEST for %s — the service is not called', async (_label, change) => {
    const res = await post('/transfer/payout', { ...PAYOUT, ...change });
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'INVALID_TRANSFER_REQUEST', retryable: false });
    expect(calls).toHaveLength(0);
  });

  it('a body without network ETHEREUM is refused before anything (NETWORK_MISMATCH)', async () => {
    const res = await post('/transfer/payout', { ...PAYOUT, network: 'TRON' });
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'NETWORK_MISMATCH' });
    expect(calls).toHaveLength(0);
  });

  it('a wait is 503 { deferred: true, waitingSince } — nothing signed, retry not spent', async () => {
    next = () => {
      const err = new TransferDeferredError('above cap', 'FEE_ABOVE_CAP');
      err.waitingSince = Date.UTC(2026, 9, 18, 9, 0, 0);
      return Promise.reject(err);
    };
    const res = await post('/transfer/payout', PAYOUT);
    expect(res.status).toBe(503);
    expect(await res.json()).toMatchObject({
      error: 'FEE_ABOVE_CAP',
      deferred: true,
      retryable: true,
      waitingSince: '2026-10-18T09:00:00.000Z',
    });
  });

  it('a refusal is 400 with its code (terminal on the backend)', async () => {
    next = () => Promise.reject(new SidecarError('blacklisted', 'RECIPIENT_BLACKLISTED', false));
    const res = await post('/transfer/payout', PAYOUT);
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'RECIPIENT_BLACKLISTED', retryable: false });
  });

  it('a transient failure is 502, an unexpected one 500 (both retried)', async () => {
    next = () => Promise.reject(new SidecarError('rpc down', 'RPC_ALL_PROVIDERS_FAILED', true));
    expect((await post('/transfer/payout', PAYOUT)).status).toBe(502);
    next = () => Promise.reject(new Error('boom'));
    expect((await post('/transfer/payout', PAYOUT)).status).toBe(500);
    // T152 düzeltme turu 2: a node-side JSON-RPC error that is not a revert.
    next = () => Promise.reject(new SidecarError('header not found', 'RPC_UNAVAILABLE', true));
    expect((await post('/transfer/payout', PAYOUT)).status).toBe(502);
  });

  it('journal behind chain is 503 without deferred — transient for the backend, row stays PENDING', async () => {
    next = () =>
      Promise.reject(new SidecarError('chain ahead of journal', 'JOURNAL_BEHIND_CHAIN', true));
    const res = await post('/transfer/payout', PAYOUT);
    expect(res.status).toBe(503);
    const body = (await res.json()) as Record<string, unknown>;
    expect(body).toMatchObject({ error: 'JOURNAL_BEHIND_CHAIN', retryable: true });
    expect(body.deferred).toBeUndefined();
  });
});

describe('POST /api/transfer/refund, /sweep, /cold-wallet', () => {
  it('refund needs a known refundType and feeCapUsd', async () => {
    const body = {
      network: 'ETHEREUM',
      blockchainTransactionId: ID,
      depositIndex: 0,
      depositAddress: DEPOSIT,
      toBuyerAddress: SELLER,
      amount: '1',
      token: 'USDC',
      refundType: 'WRONG_TOKEN_REFUND',
      feeCapUsd: '0.5',
    };
    expect((await post('/transfer/refund', body)).status).toBe(200);
    expect((await post('/transfer/refund', { ...body, refundType: 'SOMETHING' })).status).toBe(400);
    expect((await post('/transfer/refund', { ...body, refundType: undefined })).status).toBe(400);
    expect((await post('/transfer/refund', { ...body, feeCapUsd: undefined })).status).toBe(400);
    expect(calls.map((c) => c.method)).toEqual(['refund']);
  });

  it('T158 (f): a HOT_WALLET refund body reaches the service with no deposit; deposit fields or an unknown source are 400', async () => {
    const body = {
      network: 'ETHEREUM',
      blockchainTransactionId: ID,
      source: 'HOT_WALLET',
      toBuyerAddress: SELLER,
      amount: '20.4',
      token: 'USDT',
      refundType: 'BUYER_REFUND',
      feeCapUsd: '0.5',
    };
    expect((await post('/transfer/refund', body)).status).toBe(200);
    expect(calls[0]).toEqual({
      method: 'refund',
      request: {
        blockchainTransactionId: ID,
        source: 'HOT_WALLET',
        depositIndex: null,
        depositAddress: null,
        toBuyerAddress: SELLER,
        amount: '20.4',
        token: 'USDT',
        refundType: 'BUYER_REFUND',
        feeCapUsd: '0.5',
      },
    });
    expect((await post('/transfer/refund', { ...body, depositIndex: 0 })).status).toBe(400);
    expect((await post('/transfer/refund', { ...body, depositAddress: DEPOSIT })).status).toBe(400);
    expect((await post('/transfer/refund', { ...body, source: 'COLD' })).status).toBe(400);
    expect((await post('/transfer/refund', { ...body, feeCapUsd: undefined })).status).toBe(400);
    expect(calls).toHaveLength(1);
  });

  it('T158 (f): the hot-wallet refund body the backend sends (contracts/transfer-refund/hot-wallet.request.json) is accepted', async () => {
    const sample = example('transfer-refund/hot-wallet.request.json');
    expect((await post('/transfer/refund', sample)).status).toBe(200);
    expect(calls[0].request).toMatchObject({
      blockchainTransactionId: sample.blockchainTransactionId,
      source: 'HOT_WALLET',
      depositIndex: null,
      depositAddress: null,
      toBuyerAddress: sample.toBuyerAddress,
      amount: sample.amount,
    });
  });

  it('sweep and cold-wallet need feeCapUsd', async () => {
    const sweep = {
      network: 'ETHEREUM',
      blockchainTransactionId: ID,
      depositIndex: 0,
      depositAddress: DEPOSIT,
      toHotWalletAddress: SELLER,
      amount: '1',
      token: 'USDT',
      feeCapUsd: '0.5',
    };
    expect((await post('/transfer/sweep', sweep)).status).toBe(200);
    expect((await post('/transfer/sweep', { ...sweep, feeCapUsd: undefined })).status).toBe(400);
    const cold = {
      network: 'ETHEREUM',
      coldTransferId: ID,
      toColdAddress: SELLER,
      amount: '1',
      token: 'USDT',
      feeCapUsd: '0.5',
    };
    expect((await post('/transfer/cold-wallet', cold)).status).toBe(200);
    expect((await post('/transfer/cold-wallet', { ...cold, feeCapUsd: undefined })).status).toBe(
      400,
    );
  });
});

describe('T153 — a sweep waiting for its fuel', () => {
  it.each(['FUEL_PENDING', 'FUEL_REQUIRED', 'FUEL_LIMIT_REACHED', 'FUEL_WALLET_LOW'] as const)(
    '%s is 503 { deferred: true } — the backend keeps the row PENDING, never FAILED',
    async (reason) => {
      next = () => Promise.reject(new TransferDeferredError('fuel', reason));
      const res = await post('/transfer/sweep', {
        network: 'ETHEREUM',
        blockchainTransactionId: ID,
        depositIndex: 0,
        depositAddress: DEPOSIT,
        toHotWalletAddress: SELLER,
        amount: '1',
        token: 'USDT',
        feeCapUsd: '0.5',
      });
      expect(res.status).toBe(503);
      expect(await res.json()).toMatchObject({ error: reason, deferred: true, retryable: true });
    },
  );
});

describe('GET /api/transfer/status/:txHash', () => {
  function get(hash: string) {
    return fetch(`${base}/api/transfer/status/${hash}`, { headers: { 'x-internal-key': KEY } });
  }

  it('200 with the service answer', async () => {
    next = () =>
      Promise.resolve({
        txHash: HASH,
        blockNumber: 7,
        contractRet: 'SUCCESS',
        confirmations: 3,
        finalized: true,
      });
    const res = await get(HASH);
    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ finalized: true, contractRet: 'SUCCESS' });
  });

  it.each([
    ['TRANSFER_NOT_FOUND', false, 404],
    ['TRANSFER_NONCE_CONFLICT', false, 409],
    ['RPC_ALL_PROVIDERS_FAILED', true, 502],
  ] as const)('%s → %s', async (code, retryable, status) => {
    next = () => Promise.reject(new SidecarError(code, code, retryable));
    expect((await get(HASH)).status).toBe(status);
  });

  it('a malformed hash is 400 without asking the service', async () => {
    expect((await get('0x1234')).status).toBe(400);
    expect(calls).toHaveLength(0);
  });

  const USDT = '0xdAC17F958D2ee523a2206206994597C13D831ec7';
  const query = (q: Record<string, string>) => `${HASH}?${new URLSearchParams(q).toString()}`;
  const FULL = { contract: USDT, from: DEPOSIT, to: SELLER, amount: '20.4' };

  it('T158 (g): the expectation (contract, from, to, amount) reaches the service canonical, in raw units', async () => {
    next = () => Promise.resolve({ txHash: HASH, contractRet: 'SUCCESS', finalized: true });
    expect((await get(query(FULL))).status).toBe(200);
    expect(calls[0].request).toEqual({
      hash: HASH,
      expected: {
        contract: USDT.toLowerCase(),
        from: DEPOSIT.toLowerCase(),
        to: SELLER,
        amountUnits: 20_400_000n,
      },
    });
  });

  it('T158 (g): no query = no expectation (null)', async () => {
    next = () => Promise.resolve({ txHash: HASH });
    await get(HASH);
    expect(calls[0].request).toEqual({ hash: HASH, expected: null });
  });

  it.each([
    ['a partial set', { contract: USDT, from: DEPOSIT, to: SELLER }],
    ['a TRON address', { ...FULL, to: 'TD5gsCwxykWsLN9aPrq2TAfNjByuZKYp4E' }],
    ['a zero amount', { ...FULL, amount: '0' }],
    ['seven decimals', { ...FULL, amount: '1.0000001' }],
  ])('T158 (g): %s is 400 INVALID_STATUS_EXPECTATION without asking the service', async (_l, q) => {
    const res = await get(query(q));
    expect(res.status).toBe(400);
    expect(((await res.json()) as { error: string }).error).toBe('INVALID_STATUS_EXPECTATION');
    expect(calls).toHaveLength(0);
  });
});
