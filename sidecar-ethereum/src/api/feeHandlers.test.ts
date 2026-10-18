import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { createApp } from './routes.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { EthFeeEstimationService, type FeeRpc } from '../fee/FeeEstimationService.js';
import { MAINNET_TOKEN_CONTRACTS } from '../network.js';
import { SidecarError } from '../errors/SidecarError.js';
import { RpcError } from '../rpc/RpcClient.js';
import { parseScaled, realizedFeeWei, weiToUsdtCentsCeil, formatCents } from '../fee/feeMath.js';
import type { EthPriceQuote } from '../price/EthPriceService.js';
import type { TransferService } from '../transfer/TransferService.js';

const KEY = 'k'.repeat(64);
const HOT = '0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed';
const CONTRACTS = resolve(__dirname, '../../contracts');

function example(path: string): Record<string, unknown> {
  return JSON.parse(readFileSync(resolve(CONTRACTS, path), 'utf8')) as Record<string, unknown>;
}

// What the fakes answer — switched per test.
let gas: bigint | Error = 46_201n;
let price: EthPriceQuote | Error = {
  priceMicroUsdt: 2_414_579_991n,
  priceUsdt: '2414.579991',
  source: 'binance',
  ageSeconds: 0,
};
const estimated: Array<{ from: string; to: string; data: string }> = [];

const rpc: FeeRpc = {
  getBlockHeader: async () => ({
    number: 26_215_000n,
    hash: '0x1',
    timestamp: 0,
    provider: 'alchemy',
  }),
  estimateGas: async (tx) => {
    estimated.push(tx);
    if (gas instanceof Error) throw gas;
    return gas;
  },
  feeHistory: async () => ({ baseFeePerGas: [167_000_000n], reward: [[50_000_000n]] }),
};

let server: Server;
let base: string;

beforeAll(async () => {
  const app = createApp({
    internalKey: KEY,
    chainId: 1,
    wallet: new EthereumHdWallet(`${'abandon '.repeat(11)}about`),
    finality: () => ({ state: 'starting', snapshot: null, ageSeconds: null, lastError: null }),
    // T152 — sends have their own route tests (transferHandlers.test.ts).
    transfers: {} as TransferService,
    feeEstimator: new EthFeeEstimationService({
      rpc,
      prices: {
        getPrice: async () => {
          if (price instanceof Error) throw price;
          return price;
        },
      },
      tokenContracts: { ...MAINNET_TOKEN_CONTRACTS },
      hotWalletAddress: HOT,
    }),
  });
  server = app.listen(0, '127.0.0.1');
  await new Promise((r) => server.once('listening', r));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  await new Promise((r) => server.close(r));
});

function post(body: unknown, key: string | null = KEY) {
  return fetch(base + '/api/transfer/estimate-fee', {
    method: 'POST',
    headers: { 'content-type': 'application/json', ...(key ? { 'x-internal-key': key } : {}) },
    body: JSON.stringify(body),
  });
}

const VALID = {
  network: 'ETHEREUM',
  toAddress: '0xfb6916095ca1df60bb79ce92ce3ea74c37c5d359',
  amount: '10.2',
  token: 'USDT',
};

describe('POST /api/transfer/estimate-fee — the backend bodies (contracts/estimate-fee)', () => {
  it.each(['payout.request.json', 'refund.request.json'])(
    '%s is accepted and priced from its own sender',
    async (file) => {
      gas = 46_201n;
      const body = example(`estimate-fee/${file}`);
      estimated.length = 0;
      const res = await post(body);
      expect(res.status).toBe(200);
      const answer = (await res.json()) as Record<string, unknown>;
      expect(answer.sender).toBe(body.fromAddress === undefined ? 'HOT_WALLET' : 'DEPOSIT');
      expect(estimated[0].from.toLowerCase()).toBe(String(body.fromAddress ?? HOT).toLowerCase());
      // The example amount passes the amount rule with all six decimals in use (payout).
      expect(parseScaled(String(body.amount), 6)).not.toBeNull();
    },
  );

  it('the answer has exactly the example response fields, each of the same JSON type', async () => {
    const expected = example('estimate-fee/response.json');
    const res = await post(example('estimate-fee/refund.request.json'));
    const answer = (await res.json()) as Record<string, unknown>;
    expect(Object.keys(answer).sort()).toEqual(Object.keys(expected).sort());
    for (const [key, value] of Object.entries(expected)) {
      expect(typeof answer[key], key).toBe(typeof value);
    }
  });

  it("the example response is consistent with this service's own arithmetic (feeUsdt = ceil(feeWei × price))", () => {
    const r = example('estimate-fee/response.json');
    const priceMicro = parseScaled(String(r.ethPriceUsdt), 6);
    expect(priceMicro).not.toBeNull();
    const wei = BigInt(String(r.feeWei));
    // The example's wei is odd and above 2^53 on purpose: a double cannot hold it.
    expect(wei > 2n ** 53n).toBe(true);
    expect(Number(r.feeWei).toString()).not.toBe(r.feeWei);
    expect(formatCents(weiToUsdtCentsCeil(wei, priceMicro!))).toBe(r.feeUsdt);
  });

  it('the example response is internally consistent: feeWei follows from its gas and per-gas prices (deposit sender)', () => {
    const r = example('estimate-fee/response.json');
    const n = (k: string) => BigInt(String(r[k]));
    expect(r.sender).toBe('DEPOSIT');
    const expected = n('baseFeePerGasWei') + n('maxPriorityFeePerGasWei');
    expect(n('maxFeePerGasWei')).toBe(2n * n('baseFeePerGasWei') + n('maxPriorityFeePerGasWei'));
    expect(n('fuelGasLimit')).toBe(21_000n);
    expect(n('feeWei')).toBe(n('gasLimit') * n('maxFeePerGasWei') + n('fuelGasLimit') * expected);
  });

  it("transfer-status example: realizedFeeWei is this service's receipt arithmetic, as a string (T152 serves it)", () => {
    const r = example('transfer-status/confirmed.response.json');
    const fee = realizedFeeWei({
      gasUsed: '0x' + (63_209).toString(16),
      effectiveGasPrice: '0x' + (142_500_000_123).toString(16),
    });
    expect(typeof r.realizedFeeWei).toBe('string');
    expect(r.realizedFeeWei).toBe(fee.toString());
    expect(r.finalized).toBe(true);
  });
});

describe('POST /api/transfer/estimate-fee — request rule', () => {
  it.each([
    ['fromAddress: null', { ...VALID, fromAddress: null }],
    ['fromAddress a TRON address', { ...VALID, fromAddress: 'TD5gsCwxykWsLN9aPrq2TAfNjByuZKYp4E' }],
    [
      'toAddress with a bad checksum',
      { ...VALID, toAddress: '0xFB6916095ca1df60bB79Ce92cE3Ea74c37c5d359' },
    ],
    ['toAddress missing', { ...VALID, toAddress: undefined }],
    ['amount with 7 decimals', { ...VALID, amount: '1.0000001' }],
    ['amount zero', { ...VALID, amount: '0.000000' }],
    ['amount as a number', { ...VALID, amount: 10.2 }],
    ['amount with a sign', { ...VALID, amount: '-1' }],
    ['amount with a leading zero', { ...VALID, amount: '01.5' }],
    ['token ETH', { ...VALID, token: 'ETH' }],
  ])('%s → 400 INVALID_ESTIMATE_REQUEST', async (_label, body) => {
    const res = await post(body);
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'INVALID_ESTIMATE_REQUEST' });
  });

  it('accepts all-lowercase and EIP-55 addresses alike', async () => {
    gas = 46_201n;
    const res = await post({
      ...VALID,
      toAddress: '0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359'.toLowerCase(),
    });
    expect(res.status).toBe(200);
    const res2 = await post({ ...VALID, toAddress: '0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359' });
    expect(res2.status).toBe(200);
  });

  it('is behind the internal key and the network guard', async () => {
    expect((await post(VALID, null)).status).toBe(401);
    const res = await post({ ...VALID, network: 'TRON' });
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'NETWORK_MISMATCH' });
  });
});

describe('POST /api/transfer/estimate-fee — failures map to the backend fallback statuses', () => {
  it('no ETH price → 502 ETH_PRICE_UNAVAILABLE retryable', async () => {
    gas = 46_201n;
    const saved = price;
    price = new SidecarError('no price', 'ETH_PRICE_UNAVAILABLE', true);
    const res = await post(VALID);
    price = saved;
    expect(res.status).toBe(502);
    expect(await res.json()).toMatchObject({ error: 'ETH_PRICE_UNAVAILABLE', retryable: true });
  });

  it('a reverted simulation → 502 FEE_ESTIMATE_SIMULATION_FAILED', async () => {
    gas = new RpcError('eth_estimateGas: execution reverted', 'rpc', 'alchemy');
    const res = await post(VALID);
    gas = 46_201n;
    expect(res.status).toBe(502);
    expect(await res.json()).toMatchObject({ error: 'FEE_ESTIMATE_SIMULATION_FAILED' });
  });

  it('an unexpected error → 500 INTERNAL_ERROR, nothing leaked', async () => {
    gas = new Error('secret-ish internals');
    const res = await post(VALID);
    gas = 46_201n;
    expect(res.status).toBe(500);
    const body = (await res.json()) as { message: string };
    expect(body.message).not.toContain('secret');
  });
});
