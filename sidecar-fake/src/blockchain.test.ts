import { afterAll, beforeAll, beforeEach, describe, expect, it } from 'vitest';
import type { Server } from 'http';
import type { AddressInfo } from 'net';
import { buildApp, type Surface } from './app.js';
import { resetBlockchainState } from './blockchainState.js';
import { ETHEREUM, TRON } from './networks.js';
import { weiToUsdtCentsCeil } from './feeMath.js';

/**
 * T155 — the fake's two blockchain surfaces over real HTTP. Each check below
 * is one the real service makes and one a cross-network mix-up in the backend
 * would trip; the fake is only useful to the E2E if it refuses what the real
 * service refuses (routes/blockchain.ts).
 */

const servers: Server[] = [];
const base: Record<Surface, string> = { STEAM: '', TRON: '', ETHEREUM: '' };

beforeAll(async () => {
  for (const surface of ['STEAM', 'TRON', 'ETHEREUM'] as Surface[]) {
    const server = buildApp(surface).listen(0, '127.0.0.1');
    await new Promise<void>((resolve) => server.once('listening', () => resolve()));
    servers.push(server);
    base[surface] = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  }
});

afterAll(() => {
  for (const s of servers) s.close();
});

beforeEach(() => resetBlockchainState());

async function post(surface: Surface, path: string, body: unknown) {
  const res = await fetch(`${base[surface]}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  return { status: res.status, body: (await res.json()) as Record<string, unknown> };
}

async function get(surface: Surface, path: string) {
  const res = await fetch(`${base[surface]}${path}`);
  return { status: res.status, body: (await res.json()) as Record<string, unknown> };
}

const ETH_ADDR = '0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed';
const TRON_ADDR = 'TKzxdSv2FZKQrEqkKVgp5DcwEXBEKMg2Ax';

describe('health', () => {
  it('names the network and declares acceptsPayments on each blockchain surface', async () => {
    expect((await get('ETHEREUM', '/health')).body).toMatchObject({
      status: 'ok',
      network: 'ETHEREUM',
      acceptsPayments: true,
    });
    expect((await get('TRON', '/health')).body).toMatchObject({ network: 'TRON' });
    expect((await get('STEAM', '/health')).body).not.toHaveProperty('network');
  });
});

describe('network guard', () => {
  it('Ethereum refuses a POST that names no network or another network', async () => {
    for (const body of [{ index: 0 }, { network: 'TRON', index: 0 }]) {
      const r = await post('ETHEREUM', '/api/wallet/derive', body);
      expect(r.status).toBe(400);
      expect(r.body.error).toBe('NETWORK_MISMATCH');
    }
  });

  it('TRON lets an absent network through but refuses another network', async () => {
    expect((await post('TRON', '/api/wallet/derive', { index: 0 })).status).toBe(200);
    const r = await post('TRON', '/api/wallet/derive', { network: 'ETHEREUM', index: 0 });
    expect(r.status).toBe(400);
    expect(r.body.error).toBe('NETWORK_MISMATCH');
  });
});

describe('derive', () => {
  it('Ethereum answers an EIP-55 address on the standard path', async () => {
    const r = await post('ETHEREUM', '/api/wallet/derive', { network: 'ETHEREUM', index: 7 });
    expect(r.status).toBe(200);
    expect(r.body.address).toBe(ETHEREUM.depositAddress(7));
    expect(r.body.address).toMatch(/^0x[0-9a-fA-F]{40}$/);
    expect(r.body.address).not.toBe(String(r.body.address).toLowerCase());
    expect(r.body.derivationPath).toBe("m/44'/60'/0'/0/7");
  });

  it('T158 (h): Ethereum names the start block (watchFromBlock, decimal string); TRON does not', async () => {
    const eth = await post('ETHEREUM', '/api/wallet/derive', { network: 'ETHEREUM', index: 3 });
    expect(eth.body.watchFromBlock).toMatch(/^\d+$/);
    const tron = await post('TRON', '/api/wallet/derive', { network: 'TRON', index: 3 });
    expect(tron.body.watchFromBlock).toBeUndefined();
  });

  it('TRON keeps its base58 address and coin type 195', async () => {
    const r = await post('TRON', '/api/wallet/derive', { network: 'TRON', index: 7 });
    expect(r.body.address).toBe(TRON.depositAddress(7));
    expect(r.body.derivationPath).toBe("m/44'/195'/0'/0/7");
  });

  it('Ethereum refuses a non-integer index like the real service', async () => {
    const r = await post('ETHEREUM', '/api/wallet/derive', { network: 'ETHEREUM', index: '1' });
    expect(r.status).toBe(400);
    expect(r.body.error).toBe('INVALID_DERIVATION_INDEX');
  });
});

describe('transfers', () => {
  it('Ethereum payout answers a 0x hash; a TRON destination is refused', async () => {
    const ok = await post('ETHEREUM', '/api/transfer/payout', {
      network: 'ETHEREUM',
      blockchainTransactionId: 'b1',
      toAddress: ETH_ADDR.toLowerCase(),
      amount: '100',
      token: 'USDT',
    });
    expect(ok.status).toBe(200);
    expect(ok.body.txHash).toMatch(/^0x[0-9a-f]{64}$/);

    const wrong = await post('ETHEREUM', '/api/transfer/payout', {
      network: 'ETHEREUM',
      blockchainTransactionId: 'b2',
      toAddress: TRON_ADDR,
      amount: '100',
      token: 'USDT',
    });
    expect(wrong.status).toBe(400);
    expect(wrong.body.error).toBe('INVALID_ADDRESS');
  });

  it('a refund through the TRON service to an Ethereum address is refused', async () => {
    const r = await post('TRON', '/api/transfer/refund', {
      network: 'TRON',
      blockchainTransactionId: 'b3',
      depositIndex: 1,
      depositAddress: TRON.depositAddress(1),
      toBuyerAddress: ETH_ADDR,
      amount: '10',
      token: 'USDT',
    });
    expect(r.status).toBe(400);
    expect(r.body.error).toBe('INVALID_ADDRESS');
  });

  it('T158 (f): an Ethereum refund after the sweep (source HOT_WALLET, no deposit) is accepted; deposit fields with it, or the source on TRON, are refused', async () => {
    const hot = {
      network: 'ETHEREUM',
      blockchainTransactionId: 'hot-1',
      source: 'HOT_WALLET',
      toBuyerAddress: ETH_ADDR,
      amount: '10',
      token: 'USDT',
    };
    expect((await post('ETHEREUM', '/api/transfer/refund', hot)).status).toBe(200);
    const withDeposit = await post('ETHEREUM', '/api/transfer/refund', {
      ...hot,
      depositAddress: ETHEREUM.depositAddress(1),
    });
    expect(withDeposit.status).toBe(400);
    // T158 düzeltme turu (F02) — either deposit field, even null, like the real service.
    for (const depositField of [
      { depositIndex: 1 },
      { depositIndex: null },
      { depositAddress: null },
    ]) {
      const r = await post('ETHEREUM', '/api/transfer/refund', { ...hot, ...depositField });
      expect(r.status).toBe(400);
      expect(r.body.error).toBe('INVALID_TRANSFER_REQUEST');
    }
    const onTron = await post('TRON', '/api/transfer/refund', {
      ...hot,
      network: 'TRON',
      toBuyerAddress: TRON.depositAddress(2),
    });
    expect(onTron.status).toBe(400);
  });

  it('TRON keeps the pre-T155 hash for the same transfer id', async () => {
    const r = await post('TRON', '/api/transfer/sweep', {
      network: 'TRON',
      blockchainTransactionId: 'sweep-1',
      depositIndex: 1,
      depositAddress: TRON.depositAddress(1),
      toHotWalletAddress: 'TE2eHotWa11etFakeAddress2222222222',
      amount: '102',
      token: 'USDT',
    });
    expect(r.status).toBe(200);
    expect(r.body.txHash).toBe(TRON.txHash('sweep-1'));
    expect(r.body.txHash).toMatch(/^[0-9a-f]{64}$/);
  });
});

describe('transfer status', () => {
  it('Ethereum reports finality as finalized:true; the block count is the real service’s informational one (K5)', async () => {
    const hash = ETHEREUM.txHash('x');
    const r = await get('ETHEREUM', `/api/transfer/status/${hash}`);
    expect(r.status).toBe(200);
    expect(r.body).toMatchObject({ txHash: hash, contractRet: 'SUCCESS', finalized: true });
    expect(typeof r.body.confirmations).toBe('number');
    expect(typeof r.body.realizedFeeWei).toBe('string');
  });

  it('TRON reports >= 20 confirmations and no finalized flag', async () => {
    const r = await get('TRON', `/api/transfer/status/${TRON.txHash('x')}`);
    expect(Number(r.body.confirmations)).toBeGreaterThanOrEqual(20);
    expect(r.body).not.toHaveProperty('finalized');
  });

  it("each surface refuses the other network's hash format", async () => {
    expect((await get('ETHEREUM', `/api/transfer/status/${TRON.txHash('x')}`)).status).toBe(400);
    expect((await get('TRON', `/api/transfer/status/${ETHEREUM.txHash('x')}`)).status).toBe(400);
  });
});

describe('monitor start/stop', () => {
  const start = {
    network: 'ETHEREUM',
    address: ETHEREUM.depositAddress(3).toLowerCase(),
    paymentAddressId: 'pa-1',
    transactionId: 'tx-1',
    expectedContract: '0xdAC17F958D2ee523a2206206994597C13D831ec7',
    expectedSymbol: 'USDT',
    cadence: 'PAYMENT',
  };

  it('arms an Ethereum deposit for the Ethereum USDT contract and stops it', async () => {
    const r = await post('ETHEREUM', '/api/monitor/start', start);
    expect(r.status).toBe(200);
    expect(r.body).toMatchObject({ acknowledged: true, started: true, cadence: 'PAYMENT' });
    const listed = await get('ETHEREUM', '/__e2e/blockchain/monitors?network=ETHEREUM');
    expect((listed.body.monitors as unknown[]).length).toBe(1);

    const stop = await post('ETHEREUM', '/api/monitor/stop', {
      network: 'ETHEREUM',
      address: ETHEREUM.depositAddress(3),
    });
    expect(stop.body).toMatchObject({ acknowledged: true, stopped: true });
    const after = await get('ETHEREUM', '/__e2e/blockchain/monitors?network=ETHEREUM');
    expect(after.body.monitors).toEqual([]);
  });

  it('refuses an Ethereum deposit armed with the TRON USDT contract', async () => {
    const r = await post('ETHEREUM', '/api/monitor/start', {
      ...start,
      expectedContract: 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t',
    });
    expect(r.status).toBe(400);
    expect(r.body.error).toBe('EXPECTED_CONTRACT_MISMATCH');
  });

  it('refuses USDC armed with the USDT contract (symbol and contract must agree)', async () => {
    const r = await post('ETHEREUM', '/api/monitor/start', { ...start, expectedSymbol: 'USDC' });
    expect(r.status).toBe(400);
    expect(r.body.error).toBe('EXPECTED_CONTRACT_MISMATCH');
  });
});

describe('fee estimates', () => {
  it('Ethereum estimate-fee: feeUsdt is exactly the rounded-up wei × price the backend re-derives', async () => {
    const r = await post('ETHEREUM', '/api/transfer/estimate-fee', {
      network: 'ETHEREUM',
      toAddress: ETH_ADDR.toLowerCase(),
      amount: '100',
      token: 'USDT',
    });
    expect(r.status).toBe(200);
    expect(r.body.feeUsdt).toBe(
      weiToUsdtCentsCeil(BigInt(String(r.body.feeWei)), String(r.body.ethPriceUsdt)),
    );
    expect(r.body.feeUsdt).toBe('0.05');
    expect(r.body.sender).toBe('HOT_WALLET');
    expect(typeof r.body.feeWei).toBe('string');
  });

  it('Ethereum estimate from a deposit names the deposit sender and the fuel gas', async () => {
    const r = await post('ETHEREUM', '/api/transfer/estimate-fee', {
      network: 'ETHEREUM',
      fromAddress: ETHEREUM.depositAddress(1),
      toAddress: ETH_ADDR,
      amount: '10.2',
      token: 'USDT',
    });
    expect(r.body).toMatchObject({ sender: 'DEPOSIT', fuelGasLimit: '21000' });
  });

  it('TRON estimate-sweep-fee answers the T149 worked example (2.90 line, not the 3.00 fallback)', async () => {
    const r = await post('TRON', '/api/transfer/estimate-sweep-fee', {
      network: 'TRON',
      token: 'USDT',
    });
    expect(r.status).toBe(200);
    expect(r.body.sweepCostSun).toBe(8_441_349);
    expect(
      Number(r.body.activationSun) + Number(r.body.burnTopUpSun) + Number(r.body.bandwidthSun),
    ).toBe(8_441_349);
    // The backend's line: ceil2(SUN / 1e6 × price × (1 + 0.007)).
    const line =
      Math.ceil((Number(r.body.sweepCostSun) / 1e6) * Number(r.body.trxPriceUsdt) * 1.007 * 100) /
      100;
    expect(line).toBe(2.9);
  });

  it('Ethereum has no sweep-fee endpoint (501, like the real service)', async () => {
    const r = await post('ETHEREUM', '/api/transfer/estimate-sweep-fee', {
      network: 'ETHEREUM',
      token: 'USDT',
    });
    expect(r.status).toBe(501);
  });

  it('switching a network estimate off answers 502 so the backend takes its fallback', async () => {
    await post('TRON', '/__e2e/blockchain/fees', { network: 'TRON', available: false });
    const r = await post('TRON', '/api/transfer/estimate-sweep-fee', {
      network: 'TRON',
      token: 'USDT',
    });
    expect(r.status).toBe(502);
    // The other network is untouched.
    const eth = await post('ETHEREUM', '/api/transfer/estimate-fee', {
      network: 'ETHEREUM',
      toAddress: ETH_ADDR,
      amount: '1',
      token: 'USDT',
    });
    expect(eth.status).toBe(200);
  });

  it('refuses an unknown fee knob', async () => {
    const r = await post('TRON', '/__e2e/blockchain/fees', { network: 'TRON', feeUsd: '1' });
    expect(r.status).toBe(400);
  });
});

describe('call journal', () => {
  it('records which network surface answered each call, with its status', async () => {
    await post('ETHEREUM', '/api/transfer/payout', {
      network: 'ETHEREUM',
      blockchainTransactionId: 'j1',
      toAddress: ETH_ADDR,
      amount: '1',
      token: 'USDT',
    });
    await post('TRON', '/api/transfer/payout', {
      network: 'ETHEREUM',
      blockchainTransactionId: 'j2',
      toAddress: ETH_ADDR,
      amount: '1',
      token: 'USDT',
    });
    const eth = await get('TRON', '/__e2e/blockchain/calls?network=ETHEREUM&path=/api/transfer');
    const tron = await get('TRON', '/__e2e/blockchain/calls?network=TRON&path=/api/transfer');
    expect(eth.body.calls).toMatchObject([{ path: '/api/transfer/payout', status: 200 }]);
    expect(tron.body.calls).toMatchObject([{ path: '/api/transfer/payout', status: 400 }]);
  });
});
