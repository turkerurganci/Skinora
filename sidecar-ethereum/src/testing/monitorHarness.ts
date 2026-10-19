import { EthereumRpcClient } from '../rpc/RpcClient.js';
import { DepositRegistry, type ActiveStartOptions } from '../monitor/DepositRegistry.js';
import { PaymentScanner } from '../monitor/PaymentScanner.js';
import { RatePacer } from '../reconcile/RatePacer.js';
import { BalanceReader } from '../reconcile/BalanceReader.js';
import { WebhookDeliveryError, type SendWebhook } from '../webhook/WebhookClient.js';
import type { BlockchainWebhookEvent } from '../webhook/WebhookPayloads.js';
import type { FinalityStatus } from '../finality/FinalityTracker.js';
import { FakeEthereumChain, type FakeChainOptions } from './FakeEthereumChain.js';

/** Test wiring of the payment monitor over a {@link FakeEthereumChain} (T151). */

export const USDT = '0xdac17f958d2ee523a2206206994597c13d831ec7';
export const USDC = '0xa0b86991c6218b36c1d19d4a2e9eb0ce3606eb48';
export const SPAM = '0x5555555555555555555555555555555555555555';
export const BUYER = '0xb0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0';
export const HOT = '0x7070707070707070707070707070707070707070';
export const T0 = 1_800_000_000_000;

/** A deposit address that is clearly not any other test address. */
export function deposit(i: number): string {
  return '0x' + 'de' + i.toString(16).padStart(38, '0');
}

export interface Sent {
  event: BlockchainWebhookEvent;
  data: Record<string, unknown>;
}

export interface HarnessOptions extends FakeChainOptions {
  /** The provider's configured `eth_getLogs` range (the client splits by it). */
  providerRange?: number;
  configuredChainId?: number;
  restartLookbackBlocks?: number;
  catchUpBlocksPerTick?: number;
  rearmWindowMs?: number;
  /**
   * A real rate budget (CU/s) whose waits move the harness clock instead of
   * sleeping — for timing a tick sequence. Omitted: an unlimited pacer.
   */
  cuPerSecond?: number;
  tickIntervalMs?: number;
  /** A pacer of the test's own (e.g. on a virtual timeline); wins over {@link cuPerSecond}. */
  pacer?: RatePacer;
}

export function monitorHarness(options: HarnessOptions = {}) {
  const chain = new FakeEthereumChain(options);
  const clock = { now: T0 };
  const rpc = new EthereumRpcClient({
    providers: [
      {
        name: 'alchemy',
        url: 'https://primary.example/v2/key',
        maxBlockRange: options.providerRange ?? 10,
        maxAddressesPerQuery: 1000,
      },
    ],
    chainId: options.configuredChainId ?? 11155111,
    timeoutMs: 1000,
    failoverCooldownMs: 30_000,
    fetchFn: chain.fetchFn,
    now: () => clock.now,
  });
  const registry = new DepositRegistry({ clock: () => new Date(clock.now) });
  const sent: Sent[] = [];
  /** Send attempt number (1-based, counted over the harness' life) → the status it fails with (0 = transport). */
  const failAt = new Map<number, number>();
  let attempts = 0;
  const send: SendWebhook = async (event, data) => {
    attempts++;
    const status = failAt.get(attempts);
    if (status !== undefined) throw new WebhookDeliveryError(status, event);
    sent.push({ event, data: data as Record<string, unknown> });
  };
  /** When set, the finalized head the monitor is told (a lagging provider). */
  const head: { override: bigint | null } = { override: null };
  const finality = (): FinalityStatus => ({
    state: 'ok',
    snapshot: {
      number: head.override ?? chain.finalized,
      timestamp: chain.timestampOf(chain.finalized),
      readAt: clock.now,
      provider: 'alchemy',
    },
    ageSeconds: 900,
    lastError: null,
  });
  const pacer =
    options.pacer ??
    (options.cuPerSecond === undefined
      ? new RatePacer({ cuPerSecond: 1e12 })
      : new RatePacer({
          cuPerSecond: options.cuPerSecond,
          now: () => clock.now,
          sleep: async (ms) => {
            clock.now += ms;
          },
        }));
  const allowlist = { USDT, USDC };
  const reader = new BalanceReader(rpc, allowlist, pacer);
  const makeScanner = (reg = registry) =>
    new PaymentScanner({
      rpc,
      registry: reg,
      finality,
      allowlist,
      send,
      balances: reader,
      pacer,
      tickIntervalMs: options.tickIntervalMs ?? 15_000,
      restartLookbackBlocks: options.restartLookbackBlocks ?? 200,
      rearmWindowMs: options.rearmWindowMs ?? 300_000,
      catchUpBlocksPerTick: options.catchUpBlocksPerTick ?? 10_000,
      minProviderBlockRange: options.providerRange ?? 10,
      now: () => clock.now,
    });
  const scanner = makeScanner();

  const arm = (address: string, extra: Partial<ActiveStartOptions> = {}) =>
    registry.startActive({
      address,
      paymentAddressId: `pa-${address.slice(-4)}`,
      transactionId: `tx-${address.slice(-4)}`,
      expectedContract: USDT,
      expectedSymbol: 'USDT',
      ...extra,
    });

  return {
    chain,
    clock,
    rpc,
    registry,
    sent,
    failAt,
    attempts: () => attempts,
    head,
    send,
    finality,
    pacer,
    reader,
    scanner,
    makeScanner,
    arm,
    allowlist,
  };
}

/** The (event, txHash, eventIndex) triples sent — what the backend's idempotency key sees. */
export function keysOf(sent: readonly Sent[]): string[] {
  return sent.map((s) => `${s.event}:${String(s.data.txHash)}:${String(s.data.eventIndex)}`);
}
