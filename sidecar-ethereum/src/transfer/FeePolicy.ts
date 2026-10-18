import { SidecarError } from '../errors/SidecarError.js';
import type { BlockHeader, EthereumRpcClient } from '../rpc/RpcClient.js';
import type { FeePriceSource } from '../fee/FeeEstimationService.js';
import { WEI_PER_ETH } from './units.js';

/**
 * Fee price of a send and the platform's fee cap (08 §3.7.7, T142 —
 * öneriyle kaydedildi — ⚠).
 *
 * <list type="bullet">
 *   <item><b>Price.</b> EIP-1559: `maxPriorityFeePerGas` = the median tip of
 *     the last 20 blocks (T142's measure of the effective price; never below
 *     {@link MIN_PRIORITY_FEE_WEI}), `maxFeePerGas` = 2 × base fee + tip (the
 *     usual headroom for six full blocks of base-fee growth).</item>
 *   <item><b>Cap.</b> Sweep, payout and cold transfer are platform-paid: the
 *     cap in USD (backend setting `blockchain.ethereum.fee_cap_usd_per_transfer`,
 *     0.50) is converted to wei per gas with the live ETH price over the
 *     reference gas of the flow — sweep 67,201 (fuel 21,000 + sweep estimate
 *     46,201), payout 64,031. A send goes out only when base fee + tip is under
 *     the cap, and its `maxFeePerGas` never exceeds it. Above the cap, or with
 *     no ETH price, it waits (fail closed). Refunds are capped too (yönetici
 *     kararı 2026-10-18 — the platform never pays an unforeseeably high fee on a
 *     refund): reference gas 85,031 = fuel 21,000 + a transfer to a first-time
 *     recipient 64,031 (08 §3.7.5).</item>
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

/** Reference gas the cap is spread over (08 §3.7.7). */
export const CAP_REFERENCE_GAS = {
  SWEEP: 67_201n,
  PAYOUT: 64_031n,
  COLD: 64_031n,
  REFUND: 85_031n,
} as const;

export type CappedFlow = keyof typeof CAP_REFERENCE_GAS;

/** Floor on the tip — 0.01 gwei; a keyless provider answered `eth_maxPriorityFeePerGas` with 0. */
export const MIN_PRIORITY_FEE_WEI = 10_000_000n;

/** Blocks the median tip is read over (T142). */
export const TIP_SAMPLE_BLOCKS = 20;

export interface FeeQuote {
  readonly baseFeePerGas: bigint;
  readonly priorityFeePerGas: bigint;
  /** 2 × base + tip, before any cap. */
  readonly maxFeePerGas: bigint;
}

export class TransferDeferredError extends SidecarError {
  constructor(
    message: string,
    public readonly reason:
      | 'FEE_ABOVE_CAP'
      | 'ETH_PRICE_UNAVAILABLE'
      | 'FUEL_REQUIRED'
      | 'HOT_WALLET_GAS_LOW',
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

/** Cap in wei per gas: capUsd ÷ (reference gas × ETH price), rounded down. */
export function capWeiPerGas(
  capUsdMicros: bigint,
  referenceGas: bigint,
  ethUsdMicros: bigint,
): bigint {
  if (capUsdMicros <= 0n || referenceGas <= 0n || ethUsdMicros <= 0n) return 0n;
  return (capUsdMicros * WEI_PER_ETH) / (referenceGas * ethUsdMicros);
}

/**
 * Applies the cap to a quote: returns the fee ceiling to sign with, or throws
 * {@link TransferDeferredError} when the send has to wait.
 */
export async function cappedMaxFee(
  quote: FeeQuote,
  flow: CappedFlow,
  capUsdMicros: bigint,
  prices: EthUsdPriceSource,
): Promise<{ maxFeePerGas: bigint; capWeiPerGas: bigint }> {
  const ethUsd = await prices.ethUsdMicros();
  if (ethUsd === null || ethUsd <= 0n) {
    throw new TransferDeferredError(
      'No ETH/USD price — the fee cap cannot be converted; the send waits (08 §3.7.7).',
      'ETH_PRICE_UNAVAILABLE',
    );
  }
  const cap = capWeiPerGas(capUsdMicros, CAP_REFERENCE_GAS[flow], ethUsd);
  const needed = quote.baseFeePerGas + quote.priorityFeePerGas;
  if (needed > cap) {
    throw new TransferDeferredError(
      `Fee ${needed} wei/gas (base ${quote.baseFeePerGas} + tip ${quote.priorityFeePerGas}) is above the cap ${cap} wei/gas — the send waits (08 §3.7.7).`,
      'FEE_ABOVE_CAP',
    );
  }
  return {
    maxFeePerGas: quote.maxFeePerGas < cap ? quote.maxFeePerGas : cap,
    capWeiPerGas: cap,
  };
}
