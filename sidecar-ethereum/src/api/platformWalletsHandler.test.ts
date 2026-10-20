import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import type { Server } from 'node:http';
import type { AddressInfo } from 'node:net';
import { createApp } from './routes.js';
import { EthereumHdWallet } from '../wallet/HdWalletService.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import type { TransferService } from '../transfer/TransferService.js';
import type { EthFeeEstimationService } from '../fee/FeeEstimationService.js';
import type { PlatformWallets } from './platformWalletsHandler.js';

/**
 * T157 — `POST /api/wallet/platform`: the service's own wallets, so the admin
 * panel can show the fuel wallet's address (derived from its key, in no
 * configuration). The response shape is the shared contract file
 * `contracts/platform-wallets/response.json`, which the backend's client test
 * reads too.
 */

const KEY = 'p'.repeat(64);
const MNEMONIC = `${'abandon '.repeat(11)}about`;

const finality: FinalityStatus = {
  state: 'ok',
  snapshot: { number: 100n, timestamp: 0, readAt: 0, provider: 'alchemy' },
  ageSeconds: 900,
  lastError: null,
};

let wallets: PlatformWallets;
let server: Server;
let base: string;

function build(platformWallets?: () => PlatformWallets) {
  return createApp({
    internalKey: KEY,
    chainId: 11155111,
    wallet: new EthereumHdWallet(MNEMONIC),
    finality: () => finality,
    transfers: {} as TransferService,
    feeEstimator: {} as EthFeeEstimationService,
    platformWallets,
  });
}

async function listen(app: ReturnType<typeof build>): Promise<Server> {
  const s = app.listen(0, '127.0.0.1');
  await new Promise((resolve) => s.once('listening', resolve));
  base = `http://127.0.0.1:${(s.address() as AddressInfo).port}`;
  return s;
}

function post(body: unknown, key: string | null = KEY) {
  return fetch(`${base}/api/wallet/platform`, {
    method: 'POST',
    headers: {
      'content-type': 'application/json',
      ...(key === null ? {} : { 'x-internal-key': key }),
    },
    body: JSON.stringify(body),
  });
}

describe('POST /api/wallet/platform', () => {
  beforeAll(async () => {
    const contract = JSON.parse(
      readFileSync(resolve(__dirname, '../../contracts/platform-wallets/response.json'), 'utf8'),
    ) as PlatformWallets;
    // Lowercase in, EIP-55 out: the service shows what it signs with in the checksummed form.
    wallets = {
      hotWalletAddress: contract.hotWalletAddress?.toLowerCase() ?? null,
      fuelWalletAddress: contract.fuelWalletAddress?.toLowerCase() ?? null,
      coldWalletAddress: contract.coldWalletAddress?.toLowerCase() ?? null,
    };
    server = await listen(build(() => wallets));
  });

  afterAll(async () => {
    await new Promise((resolve) => server.close(resolve));
  });

  it('answers exactly the contract file (checksummed)', async () => {
    const res = await post({ network: 'ETHEREUM' });
    expect(res.status).toBe(200);
    const contract = JSON.parse(
      readFileSync(resolve(__dirname, '../../contracts/platform-wallets/response.json'), 'utf8'),
    );
    expect(await res.json()).toEqual(contract);
  });

  it('reports a wallet that is not provisioned as null', async () => {
    wallets = { ...wallets, fuelWalletAddress: null };
    const res = await post({ network: 'ETHEREUM' });
    expect(await res.json()).toMatchObject({ fuelWalletAddress: null });
    expect(wallets.hotWalletAddress).not.toBeNull();
  });

  it('needs the internal key and the network, like every other call', async () => {
    expect((await post({ network: 'ETHEREUM' }, null)).status).toBe(401);
    const wrong = await post({ network: 'TRON' });
    expect(wrong.status).toBe(400);
    expect(await wrong.json()).toMatchObject({ error: 'NETWORK_MISMATCH' });
  });
});

describe('without the wiring', () => {
  it('answers 501 rather than an empty list of wallets', async () => {
    const s = await listen(build(undefined));
    try {
      expect((await post({ network: 'ETHEREUM' })).status).toBe(501);
    } finally {
      await new Promise((resolve) => s.close(resolve));
    }
  });
});
