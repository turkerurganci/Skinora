import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { createApp } from './routes.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import type { TransferService } from '../transfer/TransferService.js';
import type { EthFeeEstimationService } from '../fee/FeeEstimationService.js';

/**
 * T157 — the backend now sends `POST /api/transfer/cold-wallet` for Ethereum
 * (admin hot → cold consolidation, 07 §9.20). This replays the body the
 * backend's `HttpBlockchainSidecarClient` produces
 * (`contracts/cold-wallet/request.json`, asserted byte for byte by the
 * backend's `EthereumSidecarAdminContractTests`) against the real handler:
 * if the handler stops accepting it — a renamed field, a required field the
 * backend does not send — this fails here, and the backend's half fails there.
 */

const KEY = 'c'.repeat(64);

const requests: unknown[] = [];
const fake = {
  coldWalletTransfer: (request: unknown) => {
    requests.push(request);
    return Promise.resolve({ txHash: '0x' + 'cd'.repeat(32), nonce: 1, replayed: false });
  },
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
    feeEstimator: {} as EthFeeEstimationService,
  });
  server = app.listen(0, '127.0.0.1');
  await new Promise((resolve) => server.once('listening', resolve));
  base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
});

afterAll(async () => {
  await new Promise((resolve) => server.close(resolve));
});

describe('contracts/cold-wallet/request.json', () => {
  const example = JSON.parse(
    readFileSync(resolve(__dirname, '../../contracts/cold-wallet/request.json'), 'utf8'),
  ) as Record<string, string>;

  it('is accepted by the cold-wallet handler and reaches the service as sent', async () => {
    const res = await fetch(`${base}/api/transfer/cold-wallet`, {
      method: 'POST',
      headers: { 'content-type': 'application/json', 'x-internal-key': KEY },
      body: JSON.stringify(example),
    });
    expect(res.status).toBe(200);
    expect(requests).toEqual([
      {
        coldTransferId: example.coldTransferId,
        toColdAddress: example.toColdAddress,
        amount: example.amount,
        token: example.token,
        feeCapUsd: example.feeCapUsd,
      },
    ]);
  });
});
