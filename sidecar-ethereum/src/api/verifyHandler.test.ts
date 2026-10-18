import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { createApp } from './routes.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import { TransferVerifier } from '../transfer/TransferVerifier.js';
import { TRANSFER_TOPIC, addressTopic } from '../transfer/erc20.js';
import type { TransferService } from '../transfer/TransferService.js';
import type { EthFeeEstimationService } from '../fee/FeeEstimationService.js';
import type { TransactionReceipt } from '../rpc/RpcClient.js';

/**
 * T157 — `POST /api/transfer/verify` over HTTP. The request is the backend's
 * body (`contracts/transfer-verify/request.json`, asserted byte for byte by the
 * backend's `EthereumSidecarAdminContractTests`) and the answer is the shared
 * response file; 400 for anything that is not exactly shaped; 502 when the node
 * cannot answer (the backend reads that as "not verified").
 */

const KEY = 'v'.repeat(64);
const CONTRACTS = resolve(__dirname, '../../contracts/transfer-verify');
const request = JSON.parse(readFileSync(resolve(CONTRACTS, 'request.json'), 'utf8')) as Record<
  string,
  string
>;
const response = JSON.parse(readFileSync(resolve(CONTRACTS, 'response.json'), 'utf8')) as Record<
  string,
  unknown
>;

const USDT = '0xdac17f958d2ee523a2206206994597c13d831ec7';
const USDC = '0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48';

let node: 'match' | 'unknown' | 'down' = 'match';
const reads: string[] = [];

const rpc = {
  getTransactionReceipt: async (hash: string): Promise<TransactionReceipt | null> => {
    reads.push(hash);
    if (node === 'down') throw new Error('node down');
    if (node === 'unknown') return null;
    return {
      transactionHash: hash,
      blockNumber: BigInt(response.blockNumber as number),
      blockHash: '0x' + '01'.repeat(32),
      status: 1,
      from: request.from,
      to: USDT,
      gasUsed: 50_000n,
      effectiveGasPrice: 1n,
      logs: [
        {
          address: USDT,
          topics: [TRANSFER_TOPIC, addressTopic(request.from), addressTopic(request.to)],
          data: '0x' + 98_200_000n.toString(16).padStart(64, '0'),
          blockNumber: 1n,
          blockHash: '0x' + '01'.repeat(32),
          transactionHash: hash,
          logIndex: 0,
          removed: false,
        },
      ],
    };
  },
};

let server: Server;
let base: string;

beforeAll(async () => {
  const app = createApp({
    internalKey: KEY,
    chainId: 11155111,
    wallet: new EthereumHdWallet(`${'abandon '.repeat(11)}about`),
    finality: () => ({ state: 'ok', snapshot: null, ageSeconds: null, lastError: null }),
    transfers: {} as TransferService,
    feeEstimator: {} as EthFeeEstimationService,
    verifier: new TransferVerifier({ rpc, tokenContracts: { USDT, USDC } }),
  });
  server = app.listen(0, '127.0.0.1');
  await new Promise((resolveListening) => server.once('listening', resolveListening));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  await new Promise((resolveClose) => server.close(resolveClose));
});

function post(body: unknown, key: string | null = KEY) {
  return fetch(`${base}/api/transfer/verify`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      ...(key === null ? {} : { 'x-internal-key': key }),
    },
    body: JSON.stringify(body),
  });
}

describe('POST /api/transfer/verify', () => {
  it('accepts the backend request file and answers the shared response file', async () => {
    node = 'match';
    const res = await post(request);

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual(response);
  });

  it('says matches:false with the reason when the amount differs', async () => {
    node = 'match';
    const res = await post({ ...request, amount: '98.200001' });

    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ outcome: 'NO_MATCHING_TRANSFER', matches: false });
  });

  it('works without a sender', async () => {
    node = 'match';
    const withoutFrom: Record<string, string> = { ...request };
    delete withoutFrom.from;
    const res = await post(withoutFrom);

    expect(await res.json()).toMatchObject({ matches: true });
  });

  it('says NOT_FOUND for a hash the node does not know', async () => {
    node = 'unknown';
    const res = await post(request);

    expect(res.status).toBe(200);
    expect(await res.json()).toMatchObject({ outcome: 'NOT_FOUND', matches: false });
  });

  it('answers 502 when the node cannot answer — never a pass', async () => {
    node = 'down';
    const res = await post(request);

    expect(res.status).toBe(502);
    expect(((await res.json()) as { matches?: boolean }).matches).toBeUndefined();
  });

  it.each([
    ['a hash that is not 0x + 64 hex', { txHash: '0x1234' }],
    ['an unknown token', { token: 'DAI' }],
    ['a recipient that is not an address', { to: 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t' }],
    ['a sender that is not an address', { from: 'nope' }],
    ['an amount with seven decimals (never rounded)', { amount: '1.0000001' }],
    ['a negative amount', { amount: '-1' }],
    ['an amount in exponent form', { amount: '1e2' }],
    ['no amount', { amount: undefined }],
  ])('refuses %s with 400 and never asks the node', async (_label, over) => {
    reads.length = 0;
    node = 'match';
    const res = await post({ ...request, ...over });

    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'INVALID_VERIFY_REQUEST' });
    expect(reads).toEqual([]);
  });

  it('needs the internal key and the network, like every other call', async () => {
    expect((await post(request, null)).status).toBe(401);
    const wrong = await post({ ...request, network: 'TRON' });
    expect(wrong.status).toBe(400);
    expect(await wrong.json()).toMatchObject({ error: 'NETWORK_MISMATCH' });
  });
});

describe('without the wiring', () => {
  it('answers 501', async () => {
    const app = createApp({
      internalKey: KEY,
      chainId: 11155111,
      wallet: new EthereumHdWallet(`${'abandon '.repeat(11)}about`),
      finality: () => ({ state: 'ok', snapshot: null, ageSeconds: null, lastError: null }),
      transfers: {} as TransferService,
      feeEstimator: {} as EthFeeEstimationService,
    });
    const s = app.listen(0, '127.0.0.1');
    await new Promise((resolveListening) => s.once('listening', resolveListening));
    try {
      const url = `http://127.0.0.1:${(s.address() as AddressInfo).port}/api/transfer/verify`;
      const res = await fetch(url, {
        method: 'POST',
        headers: { 'content-type': 'application/json', 'x-internal-key': KEY },
        body: JSON.stringify(request),
      });
      expect(res.status).toBe(501);
    } finally {
      await new Promise((resolveClose) => s.close(resolveClose));
    }
  });
});
