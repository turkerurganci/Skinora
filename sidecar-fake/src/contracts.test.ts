import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { existsSync, readdirSync, readFileSync } from 'fs';
import path from 'path';
import type { Server } from 'http';
import type { AddressInfo } from 'net';
import { buildApp, type Surface } from './app.js';
import type { DepositPaymentAddress } from './db.js';
import { weiToUsdtCentsCeil } from './feeMath.js';
import { ETHEREUM, TRON, type NetworkProfile } from './networks.js';
import {
  lateDetectedEnvelope,
  paymentConfirmedEnvelope,
  paymentDetectedEnvelope,
  spamTokenEnvelope,
  wrongTokenEnvelope,
} from './webhookEnvelopes.js';

/**
 * T155 — the fake bound to the REAL services' contract examples. The backend's
 * contract tests replay the same files (BlockchainWebhookContractTests,
 * BlockchainSidecarEstimateFeeContractTests, BlockchainSidecarMonitorStart…,
 * and for Ethereum EthereumWebhookContractTests / EthereumFeeWeiContractTests),
 * and so do the services' own handler tests — so a body the fake sends or
 * accepts is held against the one file all three sides read (the #327 lesson:
 * both ends of a process boundary pinned to the same example).
 *
 *   • TRON examples: `sidecar-blockchain/contracts/`.
 *   • Ethereum examples: `sidecar-ethereum/contracts/`, added by T151 (webhook)
 *     and T154 (estimate-fee, transfer-status). Each Ethereum block runs only
 *     when its directory exists — once it does, every example in it is bound,
 *     and a NEW example file without a binding fails the inventory test.
 */

const ROOT = path.resolve(__dirname, '..', '..');
const TRON_CONTRACTS = path.join(ROOT, 'sidecar-blockchain', 'contracts');
const ETH_CONTRACTS = path.join(ROOT, 'sidecar-ethereum', 'contracts');

function readJson(file: string): Record<string, unknown> {
  return JSON.parse(readFileSync(file, 'utf8')) as Record<string, unknown>;
}

/** Recursive key/type skeleton: same keys, same JSON types (null is its own type). */
function shapeOf(value: unknown): unknown {
  if (value === null) return 'null';
  if (Array.isArray(value)) return value.map(shapeOf);
  if (typeof value === 'object') {
    const obj = value as Record<string, unknown>;
    return Object.fromEntries(
      Object.keys(obj)
        .sort()
        .map((k) => [k, shapeOf(obj[k])]),
    );
  }
  return typeof value;
}

const servers: Server[] = [];
const base: Partial<Record<Surface, string>> = {};

beforeAll(async () => {
  for (const surface of ['TRON', 'ETHEREUM'] as Surface[]) {
    const server = buildApp(surface).listen(0, '127.0.0.1');
    await new Promise<void>((resolve) => server.once('listening', () => resolve()));
    servers.push(server);
    base[surface] = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  }
});

afterAll(() => {
  for (const s of servers) s.close();
});

async function post(surface: Surface, route: string, body: unknown) {
  const res = await fetch(`${base[surface]}${route}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  return { status: res.status, body: (await res.json()) as Record<string, unknown> };
}

/** The fake's envelope for a webhook example: same deposit, same amounts, same index. */
function fakeEnvelopeFor(
  name: string,
  example: Record<string, unknown>,
  profile: NetworkProfile,
): Record<string, unknown> | null {
  const data = example.data as Record<string, unknown>;
  const dep: DepositPaymentAddress = {
    paymentAddressId: String(data.paymentAddressId),
    transactionId: String(data.transactionId),
    address: String(data.toAddress ?? profile.depositAddress(1)),
    expectedAmount: String(data.amount ?? '0'),
    tokenSymbol: String(data.tokenSymbol ?? 'USDT'),
    network: profile.network,
  };
  const common = {
    profile,
    dep,
    txHash: profile.txHash('contract'),
    eventIndex: Number(data.eventIndex ?? 0),
    amount: String(data.amount ?? '0'),
  };
  switch (name) {
    case 'payment-detected.json':
      return paymentDetectedEnvelope(common);
    case 'payment-confirmed.json':
      return paymentConfirmedEnvelope(common);
    case 'wrong-token.json':
      return wrongTokenEnvelope({ ...common, actualTokenSymbol: String(data.actualTokenSymbol) });
    case 'spam-token.json':
      return spamTokenEnvelope(common);
    case 'late-payment-detected.json':
      return lateDetectedEnvelope({ ...common, monitorState: String(data.monitorState) });
    default:
      return null;
  }
}

// Examples the fake never sends, with the reason. Anything else in a webhook
// examples directory must have a builder (inventory test below).
const NOT_SENT: Record<string, string> = {
  // The post-cancel window is driven by the backend's own stop/start calls in
  // e2e; no scenario needs the service to announce a state change.
  'post-cancel-monitor-state-changed.json': 'no e2e lever needs it',
};

function webhookCases(dir: string): string[] {
  return readdirSync(path.join(dir, 'webhook'))
    .filter((f) => f.endsWith('.json'))
    .sort();
}

describe('TRON — sidecar-blockchain/contracts', () => {
  it.each(webhookCases(TRON_CONTRACTS))('webhook/%s: the fake sends the same shape', (name) => {
    const example = readJson(path.join(TRON_CONTRACTS, 'webhook', name));
    const fake = fakeEnvelopeFor(name, example, TRON);
    if (fake === null) {
      expect(
        NOT_SENT[name],
        `webhook/${name} has neither a builder nor a NOT_SENT reason`,
      ).toBeTruthy();
      return;
    }
    expect(shapeOf(fake)).toEqual(shapeOf(example));
    expect(fake.network).toBe('TRON');
    expect(fake.event).toBe(example.event);
  });

  it.each(['payout.request.json', 'refund.request.json'])(
    'estimate-fee/%s is accepted and answered with a feeUsdt the backend parses',
    async (name) => {
      const r = await post(
        'TRON',
        '/api/transfer/estimate-fee',
        readJson(path.join(TRON_CONTRACTS, 'estimate-fee', name)),
      );
      expect(r.status).toBe(200);
      expect(r.body.feeUsdt).toMatch(/^\d+\.\d{2}$/);
    },
  );

  it('estimate-fee/sweep.request.json is accepted and answered with SUN + TRX price', async () => {
    const r = await post(
      'TRON',
      '/api/transfer/estimate-sweep-fee',
      readJson(path.join(TRON_CONTRACTS, 'estimate-fee', 'sweep.request.json')),
    );
    expect(r.status).toBe(200);
    // EstimateSweepFeeResponse (HttpSidecarGasFeeEstimator) requires both.
    expect(Number.isInteger(r.body.sweepCostSun)).toBe(true);
    expect(Number(r.body.trxPriceUsdt)).toBeGreaterThan(0);
  });

  it.each(['payment.request.json', 'holding.request.json'])(
    'monitor-start/%s is accepted',
    async (name) => {
      const r = await post(
        'TRON',
        '/api/monitor/start',
        readJson(path.join(TRON_CONTRACTS, 'monitor-start', name)),
      );
      expect(r.status).toBe(200);
      expect(r.body.acknowledged).toBe(true);
    },
  );

  it('the fake reports the same TRON USDT contract as the examples', () => {
    const example = readJson(path.join(TRON_CONTRACTS, 'webhook', 'payment-detected.json'));
    expect(TRON.contracts().USDT).toBe((example.data as Record<string, unknown>).contractAddress);
  });
});

const ethWebhookDir = path.join(ETH_CONTRACTS, 'webhook');
describe.skipIf(!existsSync(ethWebhookDir))('ETHEREUM — sidecar-ethereum/contracts/webhook', () => {
  const cases = existsSync(ethWebhookDir) ? webhookCases(ETH_CONTRACTS) : [];

  it.each(cases)('webhook/%s: same shape, same formats, same contracts', (name) => {
    const example = readJson(path.join(ethWebhookDir, name));
    const fake = fakeEnvelopeFor(name, example, ETHEREUM);
    if (fake === null) {
      expect(
        NOT_SENT[name],
        `webhook/${name} has neither a builder nor a NOT_SENT reason`,
      ).toBeTruthy();
      return;
    }
    expect(shapeOf(fake)).toEqual(shapeOf(example));
    expect(fake.network).toBe('ETHEREUM');
    const f = fake.data as Record<string, unknown>;
    const e = example.data as Record<string, unknown>;
    for (const key of ['txHash']) {
      expect(f[key]).toMatch(/^0x[0-9a-f]{64}$/);
      expect(e[key]).toMatch(/^0x[0-9a-f]{64}$/);
    }
    // Addresses lower-case 0x + 40 on both sides (06 §1.4).
    for (const key of ['fromAddress', 'contractAddress', 'expectedContractAddress']) {
      if (key in e) {
        expect(f[key]).toMatch(/^0x[0-9a-f]{40}$/);
        expect(e[key]).toMatch(/^0x[0-9a-f]{40}$/);
      }
    }
    // The allowlisted token contracts are the SAME values, not just the same
    // shape: a payment the fake reports from another contract would be a wrong
    // token. (A spam token's contract is any non-allowlisted one — shape only.)
    const allowlisted = ['contractAddress', 'expectedContractAddress'];
    if (name === 'wrong-token.json') allowlisted.push('actualContractAddress');
    for (const key of allowlisted) {
      if (key in e) expect(f[key]).toBe(e[key]);
    }
  });
});

const ethFeeDir = path.join(ETH_CONTRACTS, 'estimate-fee');
describe.skipIf(!existsSync(ethFeeDir))(
  'ETHEREUM — sidecar-ethereum/contracts/estimate-fee',
  () => {
    it.each(['payout.request.json', 'refund.request.json'])('%s is accepted', async (name) => {
      const r = await post(
        'ETHEREUM',
        '/api/transfer/estimate-fee',
        readJson(path.join(ethFeeDir, name)),
      );
      expect(r.status).toBe(200);
    });

    it('the answer has the example response shape, and feeUsdt follows from feeWei × price', async () => {
      const example = readJson(path.join(ethFeeDir, 'response.json'));
      // The example is self-consistent under the fake's arithmetic …
      expect(weiToUsdtCentsCeil(BigInt(String(example.feeWei)), String(example.ethPriceUsdt))).toBe(
        example.feeUsdt,
      );
      // … and the fake's own answer has the same keys and types.
      const r = await post(
        'ETHEREUM',
        '/api/transfer/estimate-fee',
        readJson(path.join(ethFeeDir, 'refund.request.json')),
      );
      expect(shapeOf(r.body)).toEqual(shapeOf(example));
      expect(r.body.feeUsdt).toBe(
        weiToUsdtCentsCeil(BigInt(String(r.body.feeWei)), String(r.body.ethPriceUsdt)),
      );
    });
  },
);

const ethStatusDir = path.join(ETH_CONTRACTS, 'transfer-status');
describe.skipIf(!existsSync(ethStatusDir))(
  'ETHEREUM — sidecar-ethereum/contracts/transfer-status',
  () => {
    it('a final transfer has the example shape (finalized:true, informational block count, wei as a string)', async () => {
      const example = readJson(path.join(ethStatusDir, 'confirmed.response.json'));
      const hash = String(example.txHash);
      const res = await fetch(`${base.ETHEREUM}/api/transfer/status/${hash}`);
      const body = (await res.json()) as Record<string, unknown>;
      expect(res.status).toBe(200);
      expect(shapeOf(body)).toEqual(shapeOf(example));
      expect(body.finalized).toBe(true);
    });
  },
);
