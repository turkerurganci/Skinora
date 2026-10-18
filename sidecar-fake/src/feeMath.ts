/**
 * T155 — the fee arithmetic the fake must get exactly right, because the
 * backend re-derives it.
 *
 * Ethereum: the backend (T154, `WeiAmount.TryUsdtCentsCeil`) charges an
 * estimate only when `feeUsdt` is EXACTLY `ceil_cents(feeWei × ethPriceUsdt /
 * 10^18)`; any other pair falls back to the static setting. The real service
 * computes the same (`sidecar-ethereum/src/fee/feeMath.ts`). Integer math only
 * — a wei fee passes 2^53 at ~0.009 ETH, so no `number` may hold it.
 *
 * TRON: the sweep-fee answer is SUN + a TRX price; the backend applies its own
 * buffer and rounds once (T149), so nothing is rounded here.
 */

const PRICE_SCALE = 6;

/** "2414.579991" → 2414579991n (micro-USDT). Positive, at most 6 fraction digits. */
export function parsePriceMicro(text: string): bigint {
  const match = /^(\d+)(?:\.(\d{1,6}))?$/.exec(text);
  if (!match) throw new Error(`invalid price "${text}" (digits, at most 6 fraction digits)`);
  const micro = BigInt(match[1] + (match[2] ?? '').padEnd(PRICE_SCALE, '0'));
  if (micro <= 0n) throw new Error(`price must be positive: "${text}"`);
  return micro;
}

/** USDT cost of `feeWei` at `ethPriceUsdt`, rounded UP to cents, as "x.yy". */
export function weiToUsdtCentsCeil(feeWei: bigint, ethPriceUsdt: string): string {
  if (feeWei < 0n) throw new Error('feeWei must be non-negative');
  // wei × micro-USDT / (10^18 wei/ETH × 10^4 micro-USDT/cent), rounded up.
  const denominator = 10n ** 22n;
  const cents = (feeWei * parsePriceMicro(ethPriceUsdt) + denominator - 1n) / denominator;
  const whole = cents / 100n;
  const frac = (cents % 100n).toString().padStart(2, '0');
  return `${whole}.${frac}`;
}
