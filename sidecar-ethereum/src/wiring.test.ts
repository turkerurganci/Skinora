import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EthereumRpcClient } from './rpc/RpcClient.js';
import { buildServices, type WiringConfig } from './wiring.js';
import { FakeEthereumChain } from './testing/FakeEthereumChain.js';
import { BUYER, USDC, USDT, deposit } from './testing/monitorHarness.js';
import type { BlockchainWebhookEvent } from './webhook/WebhookPayloads.js';

/**
 * The service as `index.ts` assembles and starts it (T151 doğrulaması).
 * Unit tests build each object by hand; these use `buildServices` — the same
 * wiring and the same start order — over a fake chain, so a test fails when
 * the REAL sequence leaves something closed or reads the wrong block.
 */

const CONFIG: WiringConfig = {
  rpc: {
    providers: [
      {
        name: 'alchemy',
        url: 'https://primary.example/v2/key',
        maxBlockRange: 10,
        maxAddressesPerQuery: 1000,
      },
    ],
    timeoutMs: 1000,
    failoverCooldownMs: 30_000,
  },
  finality: { pollIntervalMs: 30_000, stallThresholdSeconds: 1800 },
  tokenContracts: { USDT, USDC },
  reconciliation: { intervalMs: 3_600_000, cuPerSecond: 1e12, maxFinalizedAgeSeconds: 1500 },
  monitor: {
    tickIntervalMs: 15_000,
    restartLookbackBlocks: 200,
    rearmWindowMs: 300_000,
    catchUpBlocksPerTick: 10_000,
  },
  webhookHmacKey: 'secret',
  backendUrl: 'http://backend.invalid',
  usdtStatusIntervalMs: 600_000,
};

function build() {
  const chain = new FakeEthereumChain();
  const rpc = new EthereumRpcClient({
    providers: CONFIG.rpc.providers,
    chainId: 11155111,
    timeoutMs: CONFIG.rpc.timeoutMs,
    failoverCooldownMs: CONFIG.rpc.failoverCooldownMs,
    fetchFn: chain.fetchFn,
  });
  const sent: Array<{ event: BlockchainWebhookEvent; data: Record<string, unknown> }> = [];
  const services = buildServices(CONFIG, rpc, {
    send: async (event, data) => {
      sent.push({ event, data: data as Record<string, unknown> });
    },
  });
  return { chain, services, sent };
}

describe('start sequence — the USDT status is read soon after start, not 10 minutes later', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('is unknown while the finalized head is unread, then ok with the eth_call made within seconds; payments open', async () => {
    const { chain, services } = build();
    chain.mineTo(50n);
    chain.finalize(40n);
    vi.setSystemTime(new Date((chain.timestampOf(50n) + 60) * 1000));

    expect(services.readiness().acceptsPayments).toBe(false);
    services.start();
    // The first read ran before the finalized head arrived and was skipped.
    expect(services.usdt.status().state).toBe('unknown');

    await vi.advanceTimersByTimeAsync(16_000);
    expect(services.usdt.status()).toMatchObject({ state: 'ok', block: 40n });
    const reads = chain.calls.filter((c) => c.method === 'eth_call' && c.params[1] === '0x28');
    expect(reads.length).toBeGreaterThanOrEqual(4);
    expect(services.readiness()).toMatchObject({ acceptsPayments: true, reasons: [] });
    services.stop();
  });

  it('the gate follows the monitor: closed before start, closed after stop', async () => {
    const { chain, services } = build();
    chain.mineTo(50n);
    chain.finalize(40n);
    vi.setSystemTime(new Date((chain.timestampOf(50n) + 60) * 1000));
    services.start();
    await vi.advanceTimersByTimeAsync(16_000);
    expect(services.readiness().acceptsPayments).toBe(true);
    services.stop();
    expect(services.readiness()).toMatchObject({
      acceptsPayments: false,
      reasons: ['MONITOR_NOT_RUNNING'],
    });
  });

  it('a USDT contract that is switched on stays closed after the retry (the retry only covers unknown)', async () => {
    const { chain, services } = build();
    chain.mineTo(50n);
    chain.finalize(40n);
    chain.setSwitches(USDT, { basisPointsRate: 5n });
    vi.setSystemTime(new Date((chain.timestampOf(50n) + 60) * 1000));
    services.start();
    await vi.advanceTimersByTimeAsync(16_000);
    expect(services.usdt.status().state).toBe('fee_enabled');
    expect(services.readiness().acceptsPayments).toBe(false);
    services.stop();
  });
});

describe('the real finality tracker feeds the monitor — only finalized blocks are reported', () => {
  it('a transfer in a block that is latest but not finalized is not sent; it is once finalized', async () => {
    const { chain, services, sent } = build();
    chain.mineTo(80n);
    chain.finalize(80n);
    await services.finality.pollOnce();
    await services.scanner.tick(); // boot at the finalized head
    services.registry.startActive({
      address: deposit(1),
      paymentAddressId: 'pa-1',
      transactionId: 'tx-1',
      expectedContract: USDT,
      expectedSymbol: 'USDT',
    });
    chain.mineTo(89n);
    chain.mine([{ contract: USDT, from: BUYER, to: deposit(1), value: 20_000_000n }]); // block 90
    chain.mineTo(100n); // latest 100, finalized 80
    await services.finality.pollOnce();
    expect(services.finality.status().snapshot?.number).toBe(80n);
    await services.scanner.tick();
    expect(sent).toEqual([]);

    chain.finalize(95n);
    await services.finality.pollOnce();
    expect(services.finality.status().snapshot?.number).toBe(95n);
    await services.scanner.tick();
    expect(sent.map((s) => s.event)).toEqual(['payment.detected', 'payment.confirmed']);
    // The head is asked for by its tag, never as `latest`.
    const heads = chain.calls
      .filter((c) => c.method === 'eth_getBlockByNumber')
      .map((c) => c.params[0]);
    expect(heads).toContain('finalized');
    expect(heads).not.toContain('latest');
  });
});
