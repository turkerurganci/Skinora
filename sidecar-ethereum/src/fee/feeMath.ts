/**
 * Exact fee arithmetic for the Ethereum service (T154 — 08 §3.7.7).
 *
 * Wei, gas and prices are integers (`bigint`) end to end; a JavaScript
 * `number` never carries money here. A fee in wei passes 2^53 at ~0.009 ETH,
 * so a `number` would silently round it — every quantity that leaves the
 * service is a decimal string (05 §3.3a "Tutar ve ücret alanları").
 *
 * Prices are held as micro-USDT per ETH (6 fraction digits — the USDT unit),
 * so USDT amounts derived from them are exact integer divisions.
 */

export const WEI_PER_ETH = 10n ** 18n;
/** Fraction digits of a price or a USDT amount (the token's own decimals). */
export const USDT_SCALE = 6;
/** Micro-USDT per cent. */
const MICRO_PER_CENT = 10n ** BigInt(USDT_SCALE - 2);

/**
 * The intrinsic gas of any transaction (the protocol's base cost) — a plain
 * ETH send to an account that already exists costs exactly this. T159a: it
 * is a FLOOR, never the gas of a send: a fuel send is estimated
 * (`eth_estimateGas`), because a send that creates the account can cost far
 * more (Sepolia 2026-10-19: 204,600 used, 207,391 estimated). Used only as
 * the lowest plausible estimate and as the fuel's place in the cap split
 * when no fuel wallet is provisioned (then no fuel can go out at all).
 */
export const NATIVE_TRANSFER_GAS = 21_000n;

/**
 * T159a — headroom of a token send's gas limit over its `eth_estimateGas`
 * (T152: +10 %). The recipient's or sender's token state can change between
 * the estimate and the block; unused gas is not charged. A fuel send carries
 * none: a plain ETH send's gas depends only on whether the recipient exists,
 * and that can only become cheaper (an account, once created, stays).
 */
export const GAS_LIMIT_HEADROOM_PERCENT = 110n;

/**
 * T159a (yönetici kararı, ⚠ PARA) — an estimate above this is not believed:
 * nothing is signed, the send waits and an alarm fires
 * (`ethereum-gas-estimate-abnormal`). ~5× the largest estimate measured (Sepolia
 * 2026-10-19: a first USDC receipt 169,926; a fuel to a new account 207,391)
 * and far below a block (≥ 30 M), so a real transfer never reaches it while a
 * changed token contract or a broken node answer does.
 */
export const MAX_GAS_ESTIMATE = 1_000_000n;

/** The gas limit a token send is signed with: ⌈estimate × 110 %⌉. */
export function tokenGasLimit(estimate: bigint): bigint {
  return (estimate * GAS_LIMIT_HEADROOM_PERCENT + 99n) / 100n;
}

/** The highest gas limit the signer accepts on any transaction (the abnormal-estimate ceiling + headroom). */
export const MAX_SIGNABLE_GAS_LIMIT = tokenGasLimit(MAX_GAS_ESTIMATE);

/** Whether an `eth_estimateGas` answer is plausible: at least the intrinsic gas, at most {@link MAX_GAS_ESTIMATE}. */
export function isPlausibleGasEstimate(estimate: bigint): boolean {
  return estimate >= NATIVE_TRANSFER_GAS && estimate <= MAX_GAS_ESTIMATE;
}

/**
 * Floor on the tip — 0.01 gwei (T152): a keyless provider answered
 * `eth_maxPriorityFeePerGas` with 0, and an empty-block median is 0.
 */
export const MIN_PRIORITY_FEE_WEI = 10_000_000n;

/**
 * The tip a send is signed with, from the blocks' median priority fees
 * (`eth_feeHistory` at the 50th percentile): the upper middle of the sorted
 * list (as `RpcClient.medianPriorityFee`), never below
 * {@link MIN_PRIORITY_FEE_WEI}. T159a: the fee estimate (T154) uses this same
 * rule, so the fee charged to a buyer is priced at the tip the signer pays.
 */
export function signingTip(blockMedians: readonly bigint[]): bigint {
  if (blockMedians.length === 0) return MIN_PRIORITY_FEE_WEI;
  const sorted = [...blockMedians].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
  const median = sorted[Math.floor(sorted.length / 2)];
  return median > MIN_PRIORITY_FEE_WEI ? median : MIN_PRIORITY_FEE_WEI;
}

const DECIMAL = /^(\d+)(?:\.(\d+))?$/;

/**
 * A non-negative decimal string → integer scaled by `10^scale`, rounded half
 * up past `scale` digits. `null` for anything that is not a plain decimal
 * (sign, exponent, separators, empty) — callers treat that as a malformed
 * value, never as zero.
 */
export function parseScaled(text: string, scale: number): bigint | null {
  const match = DECIMAL.exec(text);
  if (!match) return null;
  const whole = match[1];
  const fraction = match[2] ?? '';
  const kept = fraction.slice(0, scale).padEnd(scale, '0');
  let value = BigInt(whole + kept);
  const next = fraction.charAt(scale);
  if (next !== '' && next >= '5') value += 1n;
  return value;
}

/** An integer scaled by `10^scale` → its decimal string with exactly `scale` fraction digits. */
export function formatScaled(value: bigint, scale: number): string {
  if (value < 0n) throw new RangeError('formatScaled: negative value');
  const digits = value.toString().padStart(scale + 1, '0');
  if (scale === 0) return digits;
  return `${digits.slice(0, -scale)}.${digits.slice(-scale)}`;
}

function ceilDiv(numerator: bigint, denominator: bigint): bigint {
  return (numerator + denominator - 1n) / denominator;
}

/**
 * Cost in wei at a price (micro-USDT per ETH) → whole cents of USDT, rounded
 * UP (the charge precision, 02 §5 / 08 §3.1a step 6). Exact: one integer
 * division of `wei × price` by `10^18 × 10^4`.
 */
export function weiToUsdtCentsCeil(wei: bigint, priceMicroUsdt: bigint): bigint {
  if (wei < 0n || priceMicroUsdt <= 0n) {
    throw new RangeError('weiToUsdtCentsCeil: wei must be >= 0 and the price > 0');
  }
  return ceilDiv(wei * priceMicroUsdt, WEI_PER_ETH * MICRO_PER_CENT);
}

/** Whole cents → "x.yy". */
export function formatCents(cents: bigint): string {
  return formatScaled(cents, 2);
}

/**
 * The per-transfer fee cap (USD, 08 §3.7.7) as the highest price per gas in
 * wei a transfer of `gasUnits` may pay at the live ETH price, rounded DOWN:
 * `cap ÷ (gas × price)`. Example (T142's reference measurement, not a gas
 * the service assumes — T159a): 0.50 $ over 67,201 gas at 2,364.48 $ →
 * ~3.15 gwei. The sends pass the ESTIMATED gas of the whole flow
 * (`FeePolicy.capShareWei`). Callers without a price must not send
 * (fail-closed, "kur yoksa bekler").
 */
export function feeCapPerGasWei(
  capMicroUsdt: bigint,
  gasUnits: bigint,
  priceMicroUsdt: bigint,
): bigint {
  if (capMicroUsdt < 0n || gasUnits <= 0n || priceMicroUsdt <= 0n) {
    throw new RangeError('feeCapPerGasWei: cap >= 0, gas > 0 and price > 0 required');
  }
  return (capMicroUsdt * WEI_PER_ETH) / (gasUnits * priceMicroUsdt);
}

/**
 * The fee a mined transaction actually paid, in wei, from its receipt:
 * `gasUsed × effectiveGasPrice` (EIP-1559 receipts carry the effective price).
 * Recorded for measurement, never charged (08 §3.1a). The status endpoint
 * (T152) reports it as the decimal string `realizedFeeWei`.
 */
export function realizedFeeWei(receipt: { gasUsed: unknown; effectiveGasPrice: unknown }): bigint {
  const gasUsed = quantity(receipt.gasUsed, 'gasUsed');
  const price = quantity(receipt.effectiveGasPrice, 'effectiveGasPrice');
  return gasUsed * price;
}

function quantity(value: unknown, field: string): bigint {
  if (typeof value !== 'string' || !/^0x[0-9a-fA-F]+$/.test(value)) {
    throw new RangeError(`receipt ${field} is not a hex quantity: ${JSON.stringify(value)}`);
  }
  return BigInt(value);
}

/** Median of a non-empty list (lower middle for an even count — never above the data). */
export function medianOf(values: readonly bigint[]): bigint {
  if (values.length === 0) throw new RangeError('medianOf: empty list');
  const sorted = [...values].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
  return sorted[Math.floor((sorted.length - 1) / 2)];
}
