import { logger } from '../logger.js';
import { walletBalanceReadFailuresTotal, walletEthBalance, walletEthFloor } from '../metrics.js';
import type { EthereumRpcClient } from '../rpc/RpcClient.js';

/**
 * ETH balance of the two platform wallets that pay gas (T153 — 08 §3.7.5,
 * T142: floor 0.06 ETH each, öneriyle kaydedildi — ⚠).
 *
 * <list type="bullet">
 *   <item><b>Fuel wallet</b> — sends every deposit its gas before a sweep or
 *     a refund. Empty, those sends wait (`FUEL_WALLET_LOW`).</item>
 *   <item><b>Hot wallet</b> — pays its own gas for payouts and cold
 *     transfers. Empty, those wait (`HOT_WALLET_GAS_LOW`). Both are topped
 *     up by hand (DEPLOY_RUNBOOK §D).</item>
 * </list>
 *
 * <para>
 * Each wallet's balance (in ETH) and its floor are published side by side —
 * `skinora_ethereum_wallet_eth_balance{wallet}` and
 * `skinora_ethereum_wallet_eth_floor{wallet}` — and the
 * `ethereum-wallet-eth-low` alert divides one by the other, so the alert
 * fires at the floor the service was given, not at a number repeated in the
 * rule. The floors are env (`ETHEREUM_FUEL_WALLET_MIN_ETH`,
 * `ETHEREUM_HOT_WALLET_MIN_ETH`, default 0.06) kept in parity with the
 * backend settings `blockchain.ethereum.{fuel,hot}_wallet_eth_balance_minimum`
 * (DEPLOY_RUNBOOK §D — the T150/T152 pattern). Below the floor the first
 * reading logs an error; a failed read keeps the last value and counts
 * `skinora_ethereum_wallet_balance_read_failures_total` (an RPC outage is
 * also `ethereum-finalized-stalled`).
 * </para>
 */

export type WalletLabel = 'fuel' | 'hot';

export interface WatchedWallet {
  readonly label: WalletLabel;
  /** Canonical address. */
  readonly address: string;
  readonly floorWei: bigint;
}

export interface WalletBalanceMonitorDeps {
  rpc: Pick<EthereumRpcClient, 'getBlockHeader' | 'getBalance'>;
  /** Wallets with a key mounted; one without a key is not watched (nothing to alarm on). */
  wallets: readonly WatchedWallet[];
  intervalMs: number;
}

/** Wei → ETH as a gauge value (gwei precision is plenty for a floor of 0.06). */
export function weiToEthNumber(wei: bigint): number {
  return Number(wei / 1_000_000_000n) / 1e9;
}

export class WalletBalanceMonitor {
  private timer: NodeJS.Timeout | null = null;
  private readonly low = new Set<WalletLabel>();
  private running = false;

  constructor(private readonly deps: WalletBalanceMonitorDeps) {
    for (const wallet of deps.wallets) {
      walletEthFloor.set({ wallet: wallet.label }, weiToEthNumber(wallet.floorWei));
    }
  }

  start(): void {
    if (this.timer || this.deps.wallets.length === 0) return;
    void this.checkOnce();
    this.timer = setInterval(() => void this.checkOnce(), this.deps.intervalMs);
    this.timer.unref();
  }

  stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }

  /** One reading of every watched wallet. Never throws. */
  async checkOnce(): Promise<void> {
    if (this.running) return;
    this.running = true;
    try {
      let head: bigint;
      try {
        head = (await this.deps.rpc.getBlockHeader('latest')).number;
      } catch (err) {
        for (const wallet of this.deps.wallets) {
          walletBalanceReadFailuresTotal.inc({ wallet: wallet.label });
        }
        logger.warn(
          { reason: (err as Error).message },
          'Platform wallet balances could not be read — kept the last values',
        );
        return;
      }
      for (const wallet of this.deps.wallets) {
        let balance: bigint;
        try {
          balance = await this.deps.rpc.getBalance(wallet.address, head);
        } catch (err) {
          walletBalanceReadFailuresTotal.inc({ wallet: wallet.label });
          logger.warn(
            { wallet: wallet.label, reason: (err as Error).message },
            'Platform wallet balance could not be read — kept the last value',
          );
          continue;
        }
        walletEthBalance.set({ wallet: wallet.label }, weiToEthNumber(balance));
        if (balance < wallet.floorWei) {
          if (!this.low.has(wallet.label)) {
            this.low.add(wallet.label);
            logger.error(
              {
                wallet: wallet.label,
                address: wallet.address,
                balanceWei: balance.toString(),
                floorWei: wallet.floorWei.toString(),
                block: head.toString(),
              },
              `ETHEREUM ${wallet.label.toUpperCase()} WALLET ETH BELOW FLOOR — top it up (DEPLOY_RUNBOOK §D, 08 §3.7.5)`,
            );
          }
        } else if (this.low.delete(wallet.label)) {
          logger.info(
            { wallet: wallet.label, balanceWei: balance.toString() },
            'Platform wallet ETH back above its floor',
          );
        }
      }
    } finally {
      this.running = false;
    }
  }
}
