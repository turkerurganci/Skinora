import { describe, expect, it } from 'vitest';
import { walletBalanceReadFailuresTotal, walletEthBalance, walletEthFloor } from '../metrics.js';
import { WalletBalanceMonitor, weiToEthNumber } from './WalletBalanceMonitor.js';

/**
 * T153 — the fuel and hot wallet ETH floors (08 §3.7.5, T142: 0.06 ETH):
 * what the `ethereum-wallet-eth-low` alert divides is the balance the service
 * read and the floor it was configured with.
 */

const FUEL = '0x' + 'f'.repeat(40);
const HOT = '0x' + 'a'.repeat(40);
const FLOOR = 60_000_000_000_000_000n; // 0.06 ETH

function rpc(balances: Map<string, bigint>, failing = new Set<string>()) {
  return {
    getBlockHeader: async () => ({
      number: 100n,
      hash: '0x' + '0'.repeat(64),
      timestamp: 0,
      provider: 'fake',
      baseFeePerGas: 1n,
    }),
    getBalance: async (address: string) => {
      if (failing.has(address)) throw new Error('rpc down');
      return balances.get(address) ?? 0n;
    },
  };
}

async function gauge(metric: typeof walletEthBalance, wallet: string): Promise<number | undefined> {
  return (await metric.get()).values.find((v) => v.labels.wallet === wallet)?.value;
}

describe('WalletBalanceMonitor', () => {
  it('publishes each wallet’s floor at construction and its balance on every reading, in ETH', async () => {
    const balances = new Map([
      [FUEL, 59_000_000_000_000_000n],
      [HOT, 2n * 10n ** 18n],
    ]);
    const monitor = new WalletBalanceMonitor({
      rpc: rpc(balances),
      wallets: [
        { label: 'fuel', address: FUEL, floorWei: FLOOR },
        { label: 'hot', address: HOT, floorWei: FLOOR / 2n },
      ],
      intervalMs: 60_000,
    });
    expect(await gauge(walletEthFloor, 'fuel')).toBe(0.06);
    expect(await gauge(walletEthFloor, 'hot')).toBe(0.03);

    await monitor.checkOnce();
    expect(await gauge(walletEthBalance, 'fuel')).toBe(0.059);
    expect(await gauge(walletEthBalance, 'hot')).toBe(2);

    balances.set(FUEL, 10n ** 18n); // topped up
    await monitor.checkOnce();
    expect(await gauge(walletEthBalance, 'fuel')).toBe(1);
  });

  it('a failed read keeps the last value and counts the failure', async () => {
    const balances = new Map([[FUEL, 10n ** 17n]]);
    const failing = new Set<string>();
    const monitor = new WalletBalanceMonitor({
      rpc: rpc(balances, failing),
      wallets: [{ label: 'fuel', address: FUEL, floorWei: FLOOR }],
      intervalMs: 60_000,
    });
    await monitor.checkOnce();
    const before =
      (await walletBalanceReadFailuresTotal.get()).values.find((v) => v.labels.wallet === 'fuel')
        ?.value ?? 0;
    failing.add(FUEL);
    balances.set(FUEL, 0n);
    await monitor.checkOnce();
    expect(await gauge(walletEthBalance, 'fuel')).toBe(0.1);
    const after = (await walletBalanceReadFailuresTotal.get()).values.find(
      (v) => v.labels.wallet === 'fuel',
    )?.value;
    expect(after).toBe(before + 1);
  });

  it('wei → ETH keeps gwei precision and never loses the order of magnitude', () => {
    expect(weiToEthNumber(60_000_000_000_000_000n)).toBe(0.06);
    expect(weiToEthNumber(1_000_000_000n)).toBe(1e-9);
    expect(weiToEthNumber(123n * 10n ** 18n)).toBe(123);
  });

  it('with no wallet to watch it never starts a timer', () => {
    const monitor = new WalletBalanceMonitor({ rpc: rpc(new Map()), wallets: [], intervalMs: 1 });
    monitor.start();
    expect((monitor as unknown as { timer: unknown }).timer).toBeNull();
  });
});
