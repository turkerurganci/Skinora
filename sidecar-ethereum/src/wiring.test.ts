import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { EthereumRpcClient } from './rpc/RpcClient.js';
import { buildOutbound, buildServices, type OutboundConfig, type WiringConfig } from './wiring.js';
import type { FeePriceSource } from './fee/FeeEstimationService.js';
import type { EthUsdPriceSource } from './transfer/FeePolicy.js';
import { TxJournal } from './transfer/TxJournal.js';
import { EthereumHdWallet } from './wallet/HdWalletService.js';
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

/**
 * T152 × T154 bütünleştirmesi — the fee cap a send is signed under and the fee
 * estimate the backend shows convert ETH with the SAME EthPriceService
 * instance (one cache, one reference price), built by `buildOutbound` exactly
 * as index.ts builds it. Two instances would let the cap and the estimate
 * disagree on the price — and would double the calls to the price sources.
 */
describe('outbound wiring — sends and the fee estimate share one ETH price service', () => {
  const NOW = 1_790_000_000_000;
  let dir: string;
  let fetches: string[];
  let ethUsdt: string;

  beforeEach(() => {
    dir = mkdtempSync(join(tmpdir(), 'eth-outbound-'));
    fetches = [];
    ethUsdt = '2500.12';
  });
  afterEach(() => {
    rmSync(dir, { recursive: true, force: true });
  });

  const priceFetch = (async (input: string | URL | Request) => {
    const url = String(input);
    fetches.push(url);
    const body = url.includes('binance')
      ? { mins: 5, price: ethUsdt, closeTime: NOW }
      : { ethereum: { usd: Number(ethUsdt), last_updated_at: Math.floor(NOW / 1000) } };
    return new Response(JSON.stringify(body), {
      status: 200,
      headers: { 'content-type': 'application/json' },
    });
  }) as typeof fetch;

  function outbound(fetchFn: typeof fetch = priceFetch, overrides: Partial<OutboundConfig> = {}) {
    const config: OutboundConfig = {
      chainId: 11155111,
      tokenContracts: { USDT, USDC },
      hotWalletKey: null,
      hotWalletAddress: null,
      coldWalletAddress: null,
      fuelWalletKey: null,
      walletBalances: {
        fuelFloorWei: 6n * 10n ** 16n,
        hotFloorWei: 6n * 10n ** 16n,
        intervalMs: 300_000,
      },
      transfers: {
        journalDir: dir,
        maxSingleTransferUnits: null,
        maxDailyOutflowUnits: null,
        fuelMaxSingleWei: null,
        fuelMaxDailyWei: null,
        waitAlarmSeconds: 10_800,
        keeperIntervalMs: 30_000,
        bumpAfterSeconds: 300,
        journalAcknowledgedNonces: new Map(),
      },
      ...overrides,
    };
    const built = buildOutbound(config, {
      rpc: new EthereumRpcClient({
        providers: CONFIG.rpc.providers,
        chainId: 11155111,
        timeoutMs: 1000,
        failoverCooldownMs: 30_000,
        fetchFn: new FakeEthereumChain().fetchFn,
      }),
      journal: new TxJournal(dir),
      wallet: new EthereumHdWallet(`${'abandon '.repeat(11)}about`),
      finalizedHead: () => null,
      feeHotWalletAddress: null,
      priceOptions: { fetchFn, now: () => NOW },
    });
    // What each consumer actually holds (private deps — read, never changed).
    const estimatorPrices = (built.feeEstimator as unknown as { deps: { prices: FeePriceSource } })
      .deps.prices;
    const sendPrices = (built.transfers as unknown as { deps: { prices: EthUsdPriceSource } }).deps
      .prices;
    return { ...built, estimatorPrices, sendPrices };
  }

  it('T153 M33: the hot and the fuel wallet are both watched for their ETH floor, each with its own', () => {
    const hd = new EthereumHdWallet(`${'abandon '.repeat(11)}about`);
    const hot = hd.deriveSigner(30);
    const fuel = hd.deriveSigner(40);
    const { walletBalances } = outbound(priceFetch, {
      hotWalletKey: hot.privateKey,
      hotWalletAddress: hot.address.toLowerCase(),
      fuelWalletKey: fuel.privateKey,
      walletBalances: { fuelFloorWei: 7n, hotFloorWei: 9n, intervalMs: 300_000 },
    });
    const watched = (walletBalances as unknown as { deps: { wallets: unknown[] } }).deps.wallets;
    expect(watched).toEqual([
      { label: 'fuel', address: fuel.address.toLowerCase(), floorWei: 7n },
      { label: 'hot', address: hot.address.toLowerCase(), floorWei: 9n },
    ]);
    // No key mounted: nothing to watch (and nothing to alarm on).
    const none = outbound().walletBalances as unknown as { deps: { wallets: unknown[] } };
    expect(none.deps.wallets).toEqual([]);
  });

  it('the estimate holds the built price service itself', () => {
    const { ethPrice, estimatorPrices } = outbound();
    expect(estimatorPrices).toBe(ethPrice);
  });

  it('a send reads the price the estimate fetched — same cache, no second fetch', async () => {
    const { estimatorPrices, sendPrices } = outbound();
    const quote = await estimatorPrices.getPrice();
    expect(quote.priceMicroUsdt).toBe(2_500_120_000n);
    expect(fetches).toHaveLength(2); // Binance + CoinGecko, once

    ethUsdt = '3100.00'; // the market moves; a SEPARATE service would fetch and see it
    expect(await sendPrices.ethUsdMicros()).toBe(2_500_120_000n);
    expect(fetches).toHaveLength(2);
  });

  it('no trusted price → the send sees none and waits (fail closed)', async () => {
    const down = (async () => new Response('', { status: 503 })) as unknown as typeof fetch;
    const { sendPrices } = outbound(down);
    expect(await sendPrices.ethUsdMicros()).toBeNull();
  });
});
