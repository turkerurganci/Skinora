import { SidecarError } from '../errors/SidecarError.js';
import type { BlockHeader, EthereumRpcClient } from '../rpc/RpcClient.js';
import type { FeePriceSource } from '../fee/FeeEstimationService.js';
import { MIN_PRIORITY_FEE_WEI } from '../fee/feeMath.js';
import { WEI_PER_ETH } from './units.js';

/**
 * Fee price of a send and the platform's fee cap (08 §3.7.7, T142 —
 * öneriyle kaydedildi — ⚠; T159a — yönetici kararı, ⚠ PARA).
 *
 * <list type="bullet">
 *   <item><b>Price.</b> EIP-1559: `maxPriorityFeePerGas` = the median tip of
 *     the last 20 blocks (T142's measure of the effective price; never below
 *     {@link MIN_PRIORITY_FEE_WEI}), `maxFeePerGas` = 2 × base fee + tip (the
 *     usual headroom for six full blocks of base-fee growth).</item>
 *   <item><b>Cap — per transaction, not per gas</b> (yönetici kararı, T152
 *     doğrulaması 2026-10-18). The cap in USD (backend setting
 *     `blockchain.ethereum.fee_cap_usd_per_transfer`, 0.50) covers the WORST
 *     the signed transactions of one flow can cost: `gasLimit × maxFeePerGas`
 *     summed over them ≤ cap (wei), cap in wei = capUsd ÷ ETH price.</item>
 *   <item><b>No assumed gas (T159a).</b> The cap is shared out by the
 *     ESTIMATED gas of the flow's own transactions, never by a reference gas:
 *     in the two deposit flows (sweep, refund before the sweep) the fuel send
 *     (T153) is paid out of the same 0.50 $, so the token transaction gets
 *     cap × its gas limit ÷ (its gas limit + the fuel's gas limit) and the
 *     fuel the rest; payout, cold transfer and hot-wallet refund have no fuel
 *     leg and their transaction gets the whole cap. Both shares are one price
 *     per gas — cap ÷ the flow's gas — rounded down, so they never add up to
 *     more than the cap. The gases T142 measured on mainnet (sweep 46,201 +
 *     fuel 21,000 = 67,201; first-time recipient 64,031 + 21,000 = 85,031)
 *     were the split until T159a, when a Sepolia tariff that takes 204,600 gas
 *     for a fuel to a new account made the fixed 21,000 revert on chain. They
 *     remain measurements (08 §3.7.5), not inputs.</item>
 *   <item>A send goes out only when `gasLimit × (base fee + tip) ≤` its
 *     share, and `maxFeePerGas` is held at ⌊share ÷ gasLimit⌋; above it, or
 *     with no ETH price, it waits (fail closed). Refunds and cold transfers
 *     are capped too (yönetici kararları 2026-10-18).</item>
 * </list>
 */

/** Source of the ETH/USD price in micro-dollars per ETH (T154's EthPriceService, via {@link ethUsdFromPriceService}). */
export interface EthUsdPriceSource {
  ethUsdMicros(): Promise<bigint | null>;
}

/**
 * The fee cap's price, read from T154's ETH/USDT price service — the SAME
 * instance the fee estimate uses (wiring.ts `buildOutbound`), so the cap and
 * the estimate never price ETH differently. Micro-USDT per ETH is taken as
 * micro-USD (the cap is a USD figure; T154 prices against USDT). Any failure
 * of the price service (`ETH_PRICE_UNAVAILABLE` or otherwise) is "no price":
 * the capped send waits (08 §3.7.7 "kur yoksa bekler", fail closed).
 */
export function ethUsdFromPriceService(prices: FeePriceSource): EthUsdPriceSource {
  return {
    ethUsdMicros: async () => {
      try {
        return (await prices.getPrice()).priceMicroUsdt;
      } catch {
        return null;
      }
    },
  };
}

/**
 * The flows a cap applies to. `SWEEP` and `REFUND` leave a deposit and may
 * need fuel; `PAYOUT`, `COLD` and `HOT_REFUND` (T158 (f) — a refund after the
 * sweep, from the hot wallet) are signed by the hot wallet, which pays its
 * own gas.
 */
export type CappedFlow = 'SWEEP' | 'PAYOUT' | 'COLD' | 'REFUND' | 'HOT_REFUND';

/** Re-exported for the send path; defined with the rest of the fee arithmetic (one source for the estimate too). */
export { MIN_PRIORITY_FEE_WEI };

/** Blocks the median tip is read over (T142). */
export const TIP_SAMPLE_BLOCKS = 20;

export interface FeeQuote {
  readonly baseFeePerGas: bigint;
  readonly priorityFeePerGas: bigint;
  /** 2 × base + tip, before any cap. */
  readonly maxFeePerGas: bigint;
}

/**
 * Why a send waits (503 `deferred: true`). The four fuel reasons are T153's
 * (08 §3.7.5): `FUEL_REQUIRED` — the deposit has no gas and no fuel can be
 * sent now (fuel key or caps not configured, the fuel arrived short);
 * `FUEL_PENDING` — the fuel for this send is signed and waits for a block;
 * `FUEL_LIMIT_REACHED` — the fuel single-send or 24-hour cap stops it;
 * `FUEL_WALLET_LOW` — the fuel wallet cannot pay the fuel. T159a (yönetici
 * kararı, ⚠ PARA): `GAS_ESTIMATE_UNAVAILABLE` — the node could not estimate
 * the gas of the send or of its fuel (a transient RPC answer: the send waits,
 * it is not FAILED — proje sahibi kararı); `GAS_ESTIMATE_ABNORMAL` — an
 * estimate outside the plausible range (below 21,000 or above
 * `MAX_GAS_ESTIMATE`): nothing is signed and an alarm fires.
 */
export type DeferralReason =
  | 'FEE_ABOVE_CAP'
  | 'ETH_PRICE_UNAVAILABLE'
  | 'GAS_ESTIMATE_UNAVAILABLE'
  | 'GAS_ESTIMATE_ABNORMAL'
  | 'FUEL_REQUIRED'
  | 'FUEL_PENDING'
  | 'FUEL_LIMIT_REACHED'
  | 'FUEL_WALLET_LOW'
  | 'HOT_WALLET_GAS_LOW'
  // T172b — the signer's 24-hour deposit refund cap (TransferGuard).
  | 'DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED'
  | 'DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED';

export class TransferDeferredError extends SidecarError {
  constructor(
    message: string,
    public readonly reason: DeferralReason,
    public waitingSince: number | null = null,
  ) {
    super(message, reason, true);
    this.name = 'TransferDeferredError';
  }
}

export async function quoteFees(
  rpc: Pick<EthereumRpcClient, 'medianPriorityFee'>,
  head: BlockHeader,
): Promise<FeeQuote> {
  if (head.baseFeePerGas === null || head.baseFeePerGas === undefined) {
    throw new SidecarError(
      `Block ${head.number} has no base fee — cannot price an EIP-1559 send.`,
      'FEE_QUOTE_UNAVAILABLE',
      true,
    );
  }
  const median = await rpc.medianPriorityFee(TIP_SAMPLE_BLOCKS, head.number);
  const tip = median > MIN_PRIORITY_FEE_WEI ? median : MIN_PRIORITY_FEE_WEI;
  return {
    baseFeePerGas: head.baseFeePerGas,
    priorityFeePerGas: tip,
    maxFeePerGas: 2n * head.baseFeePerGas + tip,
  };
}

/**
 * T159a — one transaction's share of a flow's cap, wei: capUsd ÷ ETH price ×
 * its gas limit ÷ the flow's gas (its own plus, in a deposit flow, the fuel
 * send's), rounded down in one division so the rounding never favours the
 * platform's spend. With `ownGas === flowGas` (no fuel leg) it is the whole
 * cap. The two shares of a deposit flow never add up to more than the cap.
 */
export function capShareWei(
  capUsdMicros: bigint,
  ethUsdMicros: bigint,
  ownGas: bigint,
  flowGas: bigint,
): bigint {
  if (capUsdMicros <= 0n || ethUsdMicros <= 0n || ownGas <= 0n || flowGas < ownGas) return 0n;
  return (capUsdMicros * WEI_PER_ETH * ownGas) / (flowGas * ethUsdMicros);
}

/** Highest `maxFeePerGas` a transaction of `gasLimit` may carry under a budget: ⌊budget ÷ gasLimit⌋. */
export function feeCeilingPerGas(capWei: bigint, gasLimit: bigint): bigint {
  return gasLimit > 0n ? capWei / gasLimit : 0n;
}

/**
 * Applies the cap to a quote for a transaction of `gasLimit` inside a flow
 * of `flowGas` (T159a: `gasLimit` + the fuel's estimated gas in a deposit
 * flow, `gasLimit` otherwise): returns the fee ceiling to sign with and the
 * transaction's share, or throws {@link TransferDeferredError} when the send
 * has to wait.
 */
export async function cappedMaxFee(
  quote: FeeQuote,
  capUsdMicros: bigint,
  prices: EthUsdPriceSource,
  gasLimit: bigint,
  flowGas: bigint,
): Promise<{ maxFeePerGas: bigint; capWei: bigint; ethUsdMicros: bigint }> {
  const ethUsd = await prices.ethUsdMicros();
  if (ethUsd === null || ethUsd <= 0n) {
    throw new TransferDeferredError(
      'No ETH/USD price — the fee cap cannot be converted; the send waits (08 §3.7.7).',
      'ETH_PRICE_UNAVAILABLE',
    );
  }
  const capWei = capShareWei(capUsdMicros, ethUsd, gasLimit, flowGas);
  const ceiling = feeCeilingPerGas(capWei, gasLimit);
  const needed = quote.baseFeePerGas + quote.priorityFeePerGas;
  if (needed > ceiling) {
    throw new TransferDeferredError(
      `Fee ${gasLimit} gas × ${needed} wei/gas (base ${quote.baseFeePerGas} + tip ${quote.priorityFeePerGas}) is above the transaction's cap ${capWei} wei (its share of ${flowGas} estimated gas) — the send waits (08 §3.7.7).`,
      'FEE_ABOVE_CAP',
    );
  }
  return {
    maxFeePerGas: quote.maxFeePerGas < ceiling ? quote.maxFeePerGas : ceiling,
    capWei,
    // T153: the fuel send's share is priced with the SAME reading.
    ethUsdMicros: ethUsd,
  };
}
