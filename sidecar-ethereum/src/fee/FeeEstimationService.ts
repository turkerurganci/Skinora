import { Interface, parseUnits } from 'ethers';
import { SidecarError } from '../errors/SidecarError.js';
import { sameAddress } from '../address/canonicalAddress.js';
import { RpcError, type BlockHeader, type FeeHistory } from '../rpc/RpcClient.js';
import type { EthPriceQuote, EthPriceSource } from '../price/EthPriceService.js';
import {
  MAX_GAS_ESTIMATE,
  NATIVE_TRANSFER_GAS,
  formatCents,
  isPlausibleGasEstimate,
  signingTip,
  tokenGasLimit,
  weiToUsdtCentsCeil,
} from './feeMath.js';
import { SERVICE_NETWORK } from '../network.js';

/**
 * Pre-send fee estimate of one outbound ERC-20 transfer (T154 — 08 §3.7.7,
 * the Ethereum counterpart of 08 §3.1a). Read-only: nothing is signed or sent.
 *
 * <list type="number">
 *   <item><b>Gas</b> — `eth_estimateGas` of exactly this `transfer(to, amount)`
 *     from exactly this sender, at the latest block (explicit number). For a
 *     deposit this is the estimate, not the gas a draining transfer finally
 *     uses (T142: 46,201 estimated vs 41,273 used) — the fuel is sized from
 *     the estimate, so the estimate is what it costs. T159a: from a deposit
 *     the fuel is sized from the gas LIMIT the signer uses (estimate + 10 %,
 *     `tokenGasLimit`), so that is the gas priced; the fuel send's own gas is
 *     estimated too — a plain ETH send from the fuel wallet to this deposit,
 *     whose account state decides it (21,000 on mainnet; 207,391 to a new
 *     account on Sepolia, 2026-10-19) — never an assumed 21,000.</item>
 *   <item><b>Price per gas</b> — `eth_feeHistory` over the last 20 blocks:
 *     the next block's base fee (last `baseFeePerGas` entry) and the median of
 *     the blocks' median priority fee (T142's "son 20 bloğun medyan tip'i"),
 *     picked and floored as the signer does (`signingTip`, T159a).
 *     Expected price = base + tip; cap price = 2 × base + tip.</item>
 *   <item><b>Who pays what</b> — from the hot wallet (payout, refund after
 *     sweep) the sender pays `gas × (base + tip)`. From a deposit (refund
 *     before sweep) the fuel wallet first sends `gas limit × (2 × base + tip)`
 *     ETH to the deposit (08 §3.7.5); the leftover stays there and is not
 *     collected, so the whole fuel amount is the cost, plus the fuel send's own
 *     estimated gas at the expected price (manager decision, T154 validation ⚠ —
 *     the buyer bears both, ~0.05 $ today, as on TRON the deposit activation
 *     fee is deducted from the buyer's refund; shown at the F8 gate).
 *     T169a (proje sahibi kararı 2026-10-21 — ⚠ PARA: the refund is charged the
 *     platform's whole outlay): the fuel is what the deposit LACKS of that
 *     budget, as the signer sends it (`TransferService.sendFuel`: send needs −
 *     available) — the deposit's own ETH (an earlier fuel's remainder) is read
 *     at the same block and subtracted, and a deposit that already holds the
 *     whole budget gets no fuel and costs the platform nothing (the signer
 *     reports `platformSpendWei` 0 then — T160c F1).</item>
 *   <item><b>USDT</b> — wei × live ETH/USDT price (`EthPriceService`), rounded
 *     UP to cents, all in integers.</item>
 * </list>
 *
 * Every integer leaves as a decimal string (`feeWei`, `gasLimit`, …) so the
 * backend receives it without a float in between (05 §3.3a).
 */

const ERC20 = new Interface(['function transfer(address to, uint256 amount)']);
/** USDT and USDC both have 6 decimals on Ethereum (08 §3.7.1). */
const TOKEN_DECIMALS = 6;
const FEE_HISTORY_BLOCKS = 20;
const TIP_PERCENTILE = 50;

/**
 * T159a — the value of the fuel's gas probe: what decides a fuel's gas is
 * whether it creates the deposit's account (any value above 0 does), not its
 * amount; 1 wei is the smallest such value (TransferService probes the same).
 */
const FUEL_ESTIMATE_PROBE_WEI = 1n;

export type EthTokenSymbol = 'USDT' | 'USDC';

export interface EthFeeEstimateRequest {
  /** Sender. Omitted → hot wallet. A deposit address prices the fuel too. */
  readonly fromAddress?: string;
  readonly toAddress: string;
  /** Human units, e.g. "10.20" (validated by the handler: ≤ 6 decimals). */
  readonly amount: string;
  readonly token: EthTokenSymbol;
  readonly correlationId?: string;
}

export interface EthFeeEstimateResult {
  readonly network: typeof SERVICE_NETWORK;
  /** What the backend deducts, USDT, rounded UP to 2 decimals. */
  readonly feeUsdt: string;
  /** The same cost in wei, exact. */
  readonly feeWei: string;
  readonly sender: 'HOT_WALLET' | 'DEPOSIT';
  /** The block the estimate was evaluated at. */
  readonly blockNumber: string;
  /**
   * Gas of the token transfer the fee is priced with: from the hot wallet its
   * `eth_estimateGas` (what it is expected to use); from a deposit the gas
   * limit the signer uses (estimate + 10 % — the fuel is sized from it).
   */
  readonly gasLimit: string;
  /**
   * Gas of the fuel send that precedes a deposit's transfer — its
   * `eth_estimateGas` to this deposit (T159a; 21,000 on mainnet today) — or
   * 0 from the hot wallet.
   */
  readonly fuelGasLimit: string;
  /**
   * T169a — the deposit's own ETH at the estimate block, wei, subtracted from
   * the fuel; "0" from the hot wallet (no fuel there).
   */
  readonly depositBalanceWei: string;
  readonly baseFeePerGasWei: string;
  readonly maxPriorityFeePerGasWei: string;
  readonly maxFeePerGasWei: string;
  readonly ethPriceUsdt: string;
  readonly priceSource: EthPriceSource;
  readonly priceAgeSeconds: number;
}

/** The RPC reads this service needs — `EthereumRpcClient` satisfies it. */
export interface FeeRpc {
  getBlockHeader(block: 'latest'): Promise<BlockHeader>;
  estimateGas(
    tx: { from: string; to: string; data: string; value?: bigint },
    blockNumber: bigint,
  ): Promise<bigint>;
  feeHistory(
    blockCount: number,
    newestBlock: bigint,
    rewardPercentiles: readonly number[],
  ): Promise<FeeHistory>;
  /** T169a — the deposit's ETH at the priced block (what the fuel tops up). */
  getBalance(address: string, blockNumber: bigint): Promise<bigint>;
}

export interface FeePriceSource {
  getPrice(correlationId?: string): Promise<EthPriceQuote>;
}

export interface EthFeeEstimationDeps {
  readonly rpc: FeeRpc;
  readonly prices: FeePriceSource;
  readonly tokenContracts: { readonly USDT: string; readonly USDC: string };
  readonly hotWalletAddress: string | null;
  /**
   * T159a — the fuel wallet (the sender of a deposit's fuel); the fuel's gas
   * probe is sent from it. Absent → the hot wallet: a plain ETH send's gas
   * depends on the recipient, not on which account sends it.
   */
  readonly fuelWalletAddress?: string | null;
}

export class EthFeeEstimationService {
  constructor(private readonly deps: EthFeeEstimationDeps) {}

  async estimate(request: EthFeeEstimateRequest): Promise<EthFeeEstimateResult> {
    const contract = this.deps.tokenContracts[request.token];
    if (!contract) {
      throw new SidecarError(
        `No ${request.token} contract is configured on this chain.`,
        'TOKEN_CONTRACT_NOT_CONFIGURED',
        false,
      );
    }
    const hot = this.deps.hotWalletAddress;
    const from = request.fromAddress ?? hot;
    if (!from) {
      throw new SidecarError(
        'A hot-wallet transfer was asked for but ETHEREUM_HOT_WALLET_ADDRESS is not configured.',
        'HOT_WALLET_NOT_CONFIGURED',
        false,
      );
    }
    const fromDeposit = request.fromAddress !== undefined && !sameAddress(request.fromAddress, hot);
    const fuelFrom = this.deps.fuelWalletAddress ?? hot;
    if (fromDeposit && !fuelFrom) {
      throw new SidecarError(
        'Deposit sender: no fuel wallet and no hot wallet (ETHEREUM_HOT_WALLET_ADDRESS) configured — the fuel cannot be priced.',
        'FEE_ESTIMATE_SIMULATION_FAILED',
        true,
      );
    }
    const data = ERC20.encodeFunctionData('transfer', [
      request.toAddress,
      parseUnits(request.amount, TOKEN_DECIMALS),
    ]);

    const latest = await this.deps.rpc.getBlockHeader('latest');
    const [estimate, fuelGas, history, price, depositBalance] = await Promise.all([
      this.simulate({ from, to: contract, data }, latest.number),
      fromDeposit && fuelFrom
        ? this.simulate(
            { from: fuelFrom, to: from, data: '0x', value: FUEL_ESTIMATE_PROBE_WEI },
            latest.number,
          )
        : Promise.resolve(0n),
      this.deps.rpc.feeHistory(FEE_HISTORY_BLOCKS, latest.number, [TIP_PERCENTILE]),
      this.deps.prices.getPrice(request.correlationId),
      fromDeposit ? this.deps.rpc.getBalance(from, latest.number) : Promise.resolve(0n),
    ]);

    const baseFee = history.baseFeePerGas[history.baseFeePerGas.length - 1];
    const tips = history.reward.map((row) => row[0]).filter((t): t is bigint => t !== undefined);
    // T159a: the tip the signer pays — same pick, same 0.01 gwei floor.
    const tip = signingTip(tips);
    const expectedPerGas = baseFee + tip;
    const maxPerGas = 2n * baseFee + tip;

    // From a deposit the fuel is the signer's gas LIMIT × the cap price, less
    // the ETH the deposit already holds (T169a — what the signer actually
    // sends), plus the fuel send's own fee; no fuel, no cost.
    const gasLimit = fromDeposit ? tokenGasLimit(estimate) : estimate;
    const fuelWei = fromDeposit ? gasLimit * maxPerGas - depositBalance : 0n;
    const feeWei = fromDeposit
      ? fuelWei > 0n
        ? fuelWei + fuelGas * expectedPerGas
        : 0n
      : gasLimit * expectedPerGas;

    return {
      network: SERVICE_NETWORK,
      feeUsdt: formatCents(weiToUsdtCentsCeil(feeWei, price.priceMicroUsdt)),
      feeWei: feeWei.toString(),
      sender: fromDeposit ? 'DEPOSIT' : 'HOT_WALLET',
      blockNumber: latest.number.toString(),
      gasLimit: gasLimit.toString(),
      fuelGasLimit: fuelGas.toString(),
      depositBalanceWei: depositBalance.toString(),
      baseFeePerGasWei: baseFee.toString(),
      maxPriorityFeePerGasWei: tip.toString(),
      maxFeePerGasWei: maxPerGas.toString(),
      ethPriceUsdt: price.priceUsdt,
      priceSource: price.source,
      priceAgeSeconds: price.ageSeconds,
    };
  }

  /**
   * A revert is the request's own answer (balance, blacklist) — reported as a
   * failed simulation. T159a: an estimate outside 21,000…1,000,000 is not
   * believed either (`GAS_ESTIMATE_ABNORMAL`, 502 → the backend charges its
   * fallback; the send itself would wait with an alarm).
   */
  private async simulate(
    tx: { from: string; to: string; data: string; value?: bigint },
    block: bigint,
  ): Promise<bigint> {
    let gas: bigint;
    try {
      gas = await this.deps.rpc.estimateGas(tx, block);
      if (gas <= 0n) throw new RpcError('eth_estimateGas returned 0', 'rpc', 'unknown');
    } catch (err) {
      if (err instanceof RpcError && err.kind === 'rpc') {
        throw new SidecarError(
          `eth_estimateGas failed for this transfer: ${err.message}`,
          'FEE_ESTIMATE_SIMULATION_FAILED',
          true,
        );
      }
      throw err;
    }
    if (!isPlausibleGasEstimate(gas)) {
      throw new SidecarError(
        `eth_estimateGas answered ${gas} — outside ${NATIVE_TRANSFER_GAS}…${MAX_GAS_ESTIMATE}; not believed (T159a).`,
        'GAS_ESTIMATE_ABNORMAL',
        true,
      );
    }
    return gas;
  }
}
