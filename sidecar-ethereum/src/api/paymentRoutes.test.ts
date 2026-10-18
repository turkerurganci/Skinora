import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { createApp } from './routes.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { DepositRegistry } from '../monitor/DepositRegistry.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import { paymentReadiness, type PaymentReadiness } from '../health/paymentReadiness.js';
import type { AddressBalances } from '../reconcile/BalanceReader.js';

/**
 * T151 endpoints over HTTP: `/api/monitor/*`, `/api/wallet/balances` and the
 * `acceptsPayments` flag of `/health` (05 §3.3a, 11 §F8 T151 kod düzeyi kapı).
 */

const KEY = 'i'.repeat(64);
const USDT = '0xdac17f958d2ee523a2206206994597c13d831ec7';
const USDC = '0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48';
const DEPOSIT = '0x9858EfFD232B4033E47d90003D41EC34EcaEda94';

let finality: FinalityStatus;
let readiness: PaymentReadiness;
const reads: Array<{ address: string; block: bigint }> = [];
const registry = new DepositRegistry();
let server: Server;
let base: string;

beforeAll(async () => {
  finality = {
    state: 'ok',
    snapshot: { number: 21_000_000n, timestamp: 0, readAt: 0, provider: 'alchemy' },
    ageSeconds: 900,
    lastError: null,
  };
  readiness = paymentReadiness({ monitorRunning: true, webhookConfigured: true, usdt: 'ok' });
  const app = createApp({
    internalKey: KEY,
    chainId: 1,
    wallet: new EthereumHdWallet(`${'abandon '.repeat(11)}about`),
    finality: () => finality,
    payments: {
      registry,
      allowlist: { USDT, USDC },
      reader: {
        configuredSymbols: () => ['USDT', 'USDC'],
        read: async (address: string, block: bigint): Promise<AddressBalances> => {
          reads.push({ address, block });
          return { ETH: 5n, tokens: { USDT: 20_400_000n, USDC: 0n } };
        },
      },
      maxFinalizedAgeSeconds: 1500,
      readiness: () => readiness,
    },
  });
  server = app.listen(0, '127.0.0.1');
  await new Promise((resolve) => server.once('listening', resolve));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  await new Promise((resolve) => server.close(resolve));
});

function post(path: string, body: Record<string, unknown>) {
  return fetch(base + path, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-key': KEY },
    body: JSON.stringify({ network: 'ETHEREUM', ...body }),
  });
}

const START = {
  address: DEPOSIT,
  paymentAddressId: 'aaaaaaaa-1111-2222-3333-444444444444',
  transactionId: 'bbbbbbbb-1111-2222-3333-444444444444',
  expectedContract: '0xdAC17F958D2ee523a2206206994597C13D831ec7',
  expectedSymbol: 'USDT',
};

describe('/health — acceptsPayments (kod düzeyi kapı)', () => {
  it('publishes acceptsPayments: true when the monitor can see payments', async () => {
    const body = (await (await fetch(base + '/health')).json()) as Record<string, unknown>;
    expect(body.acceptsPayments).toBe(true);
    expect(body.network).toBe('ETHEREUM');
  });

  it('publishes false — with the reason — while USDT is paused; the status code stays liveness', async () => {
    readiness = paymentReadiness({ monitorRunning: true, webhookConfigured: true, usdt: 'paused' });
    const res = await fetch(base + '/health');
    const body = (await res.json()) as {
      acceptsPayments: boolean;
      payments: { reasons: string[] };
    };
    expect(res.status).toBe(200);
    expect(body.acceptsPayments).toBe(false);
    expect(body.payments.reasons).toEqual(['USDT_PAUSED']);
    readiness = paymentReadiness({ monitorRunning: true, webhookConfigured: true, usdt: 'ok' });
  });
});

describe('/api/monitor/* — the TRON contract, Ethereum rules', () => {
  it('start is idempotent and echoes the cadence', async () => {
    const first = await post('/api/monitor/start', START);
    expect(first.status).toBe(200);
    expect(await first.json()).toMatchObject({
      acknowledged: true,
      started: true,
      cadence: 'PAYMENT',
    });
    const again = await post('/api/monitor/start', {
      ...START,
      address: DEPOSIT.toLowerCase(),
      cadence: 'HOLDING',
    });
    expect(await again.json()).toMatchObject({ started: false, cadence: 'HOLDING' });
    expect(registry.size()).toBe(1);
  });

  it.each([
    [
      'another contract than the configured USDT',
      { expectedContract: USDC },
      'EXPECTED_CONTRACT_MISMATCH',
    ],
    [
      'a TRON address',
      { address: 'TD5gsCwxykWsLN9aPrq2TAfNjByuZKYp4E' },
      'INVALID_MONITOR_REQUEST',
    ],
    ['a bad checksum', { address: DEPOSIT.replace('E', 'e') }, 'INVALID_MONITOR_REQUEST'],
    ['an unknown symbol', { expectedSymbol: 'DAI' }, 'UNSUPPORTED_SYMBOL'],
    ['an unknown cadence', { cadence: 'FAST' }, 'UNSUPPORTED_CADENCE'],
    ['a missing field', { transactionId: undefined }, 'INVALID_MONITOR_REQUEST'],
  ])('refuses %s with 400 %s', async (_label, patch, code) => {
    const res = await post('/api/monitor/start', { ...START, ...patch });
    expect(res.status).toBe(400);
    expect(((await res.json()) as { error: string }).error).toBe(code);
  });

  it('stop answers 200 either way', async () => {
    expect(await (await post('/api/monitor/stop', { address: DEPOSIT })).json()).toMatchObject({
      stopped: true,
    });
    expect(await (await post('/api/monitor/stop', { address: DEPOSIT })).json()).toMatchObject({
      stopped: false,
    });
  });

  it('post-cancel start derives the state from cancelledAt and validates it', async () => {
    const ok = await post('/api/monitor/post-cancel-start', {
      ...START,
      cancelledAt: new Date().toISOString(),
    });
    expect(await ok.json()).toMatchObject({ started: true, state: 'POST_CANCEL_24H' });
    const bad = await post('/api/monitor/post-cancel-start', {
      ...START,
      cancelledAt: 'yesterday',
    });
    expect(((await bad.json()) as { error: string }).error).toBe('INVALID_CANCELLED_AT');
    const state = await post('/api/monitor/post-cancel-start', {
      ...START,
      address: '0x' + '1'.repeat(40),
      cancelledAt: new Date().toISOString(),
      initialState: 'ACTIVE',
    });
    expect(((await state.json()) as { error: string }).error).toBe('INVALID_INITIAL_STATE');
    expect(
      await (await post('/api/monitor/post-cancel-stop', { address: DEPOSIT })).json(),
    ).toMatchObject({
      stopped: true,
    });
  });
});

describe('/api/wallet/balances — finalized block, raw units', () => {
  it('reads every address at the finalized block and names every configured symbol', async () => {
    reads.length = 0;
    const res = await post('/api/wallet/balances', { addresses: [DEPOSIT] });
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({
      blockNumber: 21_000_000,
      balances: [{ address: DEPOSIT, tokens: { ETH: '5', USDT: '20400000', USDC: '0' } }],
    });
    expect(reads).toEqual([{ address: DEPOSIT, block: 21_000_000n }]);
  });

  it('refuses when the finalized head is older than the archive-safe age', async () => {
    finality = { ...finality, ageSeconds: 1501 };
    const res = await post('/api/wallet/balances', { addresses: [DEPOSIT] });
    expect(res.status).toBe(502);
    expect(((await res.json()) as { error: string }).error).toBe('FINALIZED_STALE');
    finality = { ...finality, ageSeconds: 900 };
  });

  it('refuses a non-Ethereum address and more than 100 addresses', async () => {
    expect((await post('/api/wallet/balances', { addresses: ['TD5gs'] })).status).toBe(400);
    expect(
      (await post('/api/wallet/balances', { addresses: Array(101).fill(DEPOSIT) })).status,
    ).toBe(400);
  });
});
