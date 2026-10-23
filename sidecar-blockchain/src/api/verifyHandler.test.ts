import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import express from 'express';
import { createRouter, type RouterDeps } from './routes.js';
import type {
  VerifyRequest,
  VerifyResult,
  TransferVerifier,
} from '../transfer/TransferVerifier.js';
import { SidecarError } from '../errors/SidecarError.js';
import { amountUnits6 } from './verifyHandler.js';

/**
 * T172a — `POST /api/transfer/verify` on the TRON service, over HTTP and
 * through the real router (internal key + network guard). The request is the
 * backend's body (`contracts/transfer-verify/request.json`, asserted byte for
 * byte by the backend's `TronSidecarVerifyContractTests`) and the answer is the
 * shared response file; 400 for anything not exactly shaped; 502 when the node
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

let answer: VerifyResult | Error = {
  outcome: 'MATCH',
  blockNumber: response.blockNumber as number,
  matchingTransfers: 1,
};
const asked: VerifyRequest[] = [];
const verifier = {
  verify: async (r: VerifyRequest) => {
    asked.push(r);
    if (answer instanceof Error) throw answer;
    return answer;
  },
} as unknown as TransferVerifier;

let server: Server;
let base: string;

beforeAll(async () => {
  const app = express();
  app.use(express.json());
  app.use(
    createRouter({
      internalKey: KEY,
      transferVerifier: verifier,
    } as unknown as RouterDeps),
  );
  server = app.listen(0, '127.0.0.1');
  await new Promise((r) => server.once('listening', r));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  await new Promise((r) => server.close(r));
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

describe('POST /api/transfer/verify (TRON)', () => {
  it('accepts the backend request file and answers the shared response file', async () => {
    answer = {
      outcome: 'MATCH',
      blockNumber: response.blockNumber as number,
      matchingTransfers: 1,
    };
    asked.length = 0;
    const res = await post(request);

    expect(res.status).toBe(200);
    expect(await res.json()).toEqual(response);
    expect(asked[0]).toEqual({
      txHash: request.txHash,
      token: 'USDT',
      from: request.from,
      to: request.to,
      amountUnits: 98_200_000n,
    });
  });

  it.each([['NO_MATCHING_TRANSFER'], ['REVERTED'], ['NOT_FOUND']] as const)(
    '%s is matches:false',
    async (outcome) => {
      answer = { outcome, blockNumber: null, matchingTransfers: 0 };
      const res = await post(request);
      expect(res.status).toBe(200);
      expect(await res.json()).toMatchObject({ outcome, matches: false });
    },
  );

  it('works without a sender', async () => {
    answer = { outcome: 'MATCH', blockNumber: 1, matchingTransfers: 1 };
    asked.length = 0;
    const withoutFrom: Record<string, string> = { ...request };
    delete withoutFrom.from;
    const res = await post(withoutFrom);
    expect(res.status).toBe(200);
    expect(asked[0]?.from).toBeNull();
  });

  it.each([
    ['an Ethereum-shaped hash', { txHash: '0x' + 'ab'.repeat(32) }],
    ['a short hash', { txHash: 'ab'.repeat(31) }],
    ['an unknown token', { token: 'DAI' }],
    [
      'a recipient that is not a TRON address',
      { to: '0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed' },
    ],
    ['a bad base58 recipient', { to: 'TXqH2JBkDgGWyCFg4GZzg8eUjG5JMZ7hPL' }],
    ['a bad sender', { from: 'nope' }],
    ['seven decimals (never rounded)', { amount: '98.2000001' }],
    ['a numeric amount', { amount: 98.2 }],
  ])('400 for %s', async (_name, change) => {
    asked.length = 0;
    const res = await post({ ...request, ...change });
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'INVALID_VERIFY_REQUEST' });
    expect(asked).toHaveLength(0);
  });

  it('502 when the node cannot answer — never a pass', async () => {
    answer = new SidecarError('down', 'TRANSFER_VERIFY_HTTP_ERROR', true);
    const res = await post(request);
    expect(res.status).toBe(502);
  });

  it('502 on an unexpected error', async () => {
    answer = new Error('boom');
    const res = await post(request);
    expect(res.status).toBe(502);
  });

  it('refuses a body that names Ethereum (network guard)', async () => {
    const res = await post({ ...request, network: 'ETHEREUM' });
    expect(res.status).toBe(400);
    expect(await res.json()).toMatchObject({ error: 'NETWORK_MISMATCH' });
  });

  it('401 without the internal key', async () => {
    const res = await post(request, null);
    expect(res.status).toBe(401);
  });

  it('amountUnits6 is exact', () => {
    expect(amountUnits6('98.2')).toBe(98_200_000n);
    expect(amountUnits6('0.000001')).toBe(1n);
    expect(amountUnits6('2900')).toBe(2_900_000_000n);
  });
});
