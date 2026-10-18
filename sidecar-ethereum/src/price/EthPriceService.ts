import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';
import { USDT_SCALE, formatScaled, parseScaled } from '../fee/feeMath.js';

/**
 * ETH/USDT price for the Ethereum fee estimate and the fee cap (T154 —
 * 08 §3.7.7). Same two keyless sources as the TRON service's TRX price
 * (08 §3.1a step 5), with rules for a wrong price, not only a missing one:
 *
 * <list type="number">
 *   <item><b>Sources.</b> Binance `avgPrice?symbol=ETHUSDT` (primary — a
 *     5-minute average, so one off trade does not move it; `closeTime` is the
 *     last trade) and CoinGecko `simple/price` with `last_updated_at`
 *     (secondary). Both are asked on every refresh; the answer is cached for
 *     `cacheTtlMs` (5 min), so that is ~12 calls an hour to each.</item>
 *   <item><b>A quote is valid</b> only if it is a plain positive decimal
 *     inside the sanity band (`minPrice`–`maxPrice`, 1–1,000,000 USDT — a
 *     unit slip or a wrong pair lands outside it) and its own timestamp is
 *     younger than `sourceMaxAgeMs` (10 min — a halted market or a frozen
 *     cache answers with an old price and HTTP 200).</item>
 *   <item><b>Two valid quotes</b> must agree within `maxSourceDivergenceBp`
 *     (2%); the primary's value is used. If they disagree, neither is
 *     trusted.</item>
 *   <item><b>One valid quote</b> (the other source down) is used only if it is
 *     within `maxUncorroboratedJumpBp` (20%) of the <b>reference price</b> —
 *     the last price both sources agreed on (younger than `anchorMaxAgeMs`,
 *     24 h). A bigger move must be confirmed by both sources. With no recent
 *     reference (cold start, or the last agreement older than 24 h) a single
 *     quote is NOT used — nothing to check it against, so both sources must
 *     agree (manager decision, T154 validation ⚠).</item>
 *   <item><b>Only an agreement moves the reference</b> (manager decision, T154
 *     validation ⚠). A single-source price is served and cached, but it does
 *     not become the base of the next ±20% check and does not restart the
 *     24 h window — otherwise one source could walk the price 20% per
 *     refresh (2,000 → 16,128 in an hour at a 5-minute cache).</item>
 *   <item><b>Nothing trusted</b> → the last accepted price is served while it
 *     is younger than `staleGraceMs` (60 min, as on TRON); after that
 *     `ETH_PRICE_UNAVAILABLE` (retryable). The backend answers any estimate
 *     failure with its static fallback (refund) and the fee cap waits
 *     (fail-closed) — a price outage never moves money at a guessed price.</item>
 * </list>
 */

const BINANCE_URL = 'https://api.binance.com/api/v3/avgPrice?symbol=ETHUSDT';
const COINGECKO_URL =
  'https://api.coingecko.com/api/v3/simple/price?ids=ethereum&vs_currencies=usd&include_last_updated_at=true';

export type EthPriceSource = 'binance' | 'coingecko' | 'cache';

export interface EthPriceQuote {
  /** Micro-USDT per 1 ETH (6 fraction digits). */
  readonly priceMicroUsdt: bigint;
  /** The same price as a decimal string with 6 fraction digits. */
  readonly priceUsdt: string;
  /** `cache` = served from the last accepted price (fresh or stale). */
  readonly source: EthPriceSource;
  /** Seconds since the served price was accepted (0 for a just-fetched one). */
  readonly ageSeconds: number;
}

export interface EthPriceServiceOptions {
  cacheTtlMs?: number;
  staleGraceMs?: number;
  sourceMaxAgeMs?: number;
  anchorMaxAgeMs?: number;
  /** Basis points; 200 = 2%. */
  maxSourceDivergenceBp?: number;
  /** Basis points; 2000 = 20%. */
  maxUncorroboratedJumpBp?: number;
  /** USDT per ETH, inclusive. */
  minPrice?: string;
  maxPrice?: string;
  requestTimeoutMs?: number;
  fetchFn?: typeof fetch;
  now?: () => number;
}

interface Accepted {
  readonly priceMicroUsdt: bigint;
  readonly acceptedAt: number;
}

interface Chosen extends SourceQuote {
  /** Both sources agreed on it — only such a price moves the reference. */
  readonly corroborated: boolean;
}

interface SourceQuote {
  readonly source: 'binance' | 'coingecko';
  readonly priceMicroUsdt: bigint;
}

export class EthPriceService {
  private readonly cacheTtlMs: number;
  private readonly staleGraceMs: number;
  private readonly sourceMaxAgeMs: number;
  private readonly anchorMaxAgeMs: number;
  private readonly maxSourceDivergenceBp: bigint;
  private readonly maxUncorroboratedJumpBp: bigint;
  private readonly minPriceMicro: bigint;
  private readonly maxPriceMicro: bigint;
  private readonly requestTimeoutMs: number;
  private readonly fetchFn: typeof fetch;
  private readonly now: () => number;
  /** The last price served from a fresh quote (cache TTL and stale grace). */
  private accepted: Accepted | null = null;
  /** The last price both sources agreed on — the single-source check's base. */
  private reference: Accepted | null = null;
  private inflight: Promise<EthPriceQuote> | null = null;

  constructor(options: EthPriceServiceOptions = {}) {
    this.cacheTtlMs = options.cacheTtlMs ?? 5 * 60_000;
    this.staleGraceMs = options.staleGraceMs ?? 60 * 60_000;
    this.sourceMaxAgeMs = options.sourceMaxAgeMs ?? 10 * 60_000;
    this.anchorMaxAgeMs = options.anchorMaxAgeMs ?? 24 * 60 * 60_000;
    this.maxSourceDivergenceBp = BigInt(options.maxSourceDivergenceBp ?? 200);
    this.maxUncorroboratedJumpBp = BigInt(options.maxUncorroboratedJumpBp ?? 2000);
    this.minPriceMicro = mustScale(options.minPrice ?? '1');
    this.maxPriceMicro = mustScale(options.maxPrice ?? '1000000');
    this.requestTimeoutMs = options.requestTimeoutMs ?? 5_000;
    this.fetchFn = options.fetchFn ?? fetch;
    this.now = options.now ?? Date.now;
  }

  /** The current price, or `ETH_PRICE_UNAVAILABLE` (retryable). Concurrent callers share one refresh. */
  async getPrice(correlationId?: string): Promise<EthPriceQuote> {
    const at = this.now();
    if (this.accepted && at - this.accepted.acceptedAt < this.cacheTtlMs) {
      return this.fromAccepted(this.accepted, at);
    }
    if (!this.inflight) {
      this.inflight = this.refresh(correlationId).finally(() => {
        this.inflight = null;
      });
    }
    return this.inflight;
  }

  private async refresh(correlationId?: string): Promise<EthPriceQuote> {
    const [binance, coingecko] = await Promise.all([
      this.tryBinance(correlationId),
      this.tryCoingecko(correlationId),
    ]);
    const at = this.now();
    const chosen = this.choose(binance, coingecko, at, correlationId);
    if (chosen) {
      this.accepted = { priceMicroUsdt: chosen.priceMicroUsdt, acceptedAt: at };
      if (chosen.corroborated) this.reference = this.accepted;
      return {
        priceMicroUsdt: chosen.priceMicroUsdt,
        priceUsdt: formatScaled(chosen.priceMicroUsdt, USDT_SCALE),
        source: chosen.source,
        ageSeconds: 0,
      };
    }

    if (this.accepted && at - this.accepted.acceptedAt < this.staleGraceMs) {
      logger.warn(
        { ageMs: at - this.accepted.acceptedAt, correlationId },
        'ETH price: no trusted fresh quote — serving the last accepted price',
      );
      return this.fromAccepted(this.accepted, at);
    }

    throw new SidecarError(
      'ETH/USDT price unavailable: no trusted quote from Binance or CoinGecko and no accepted price within the stale grace.',
      'ETH_PRICE_UNAVAILABLE',
      true,
    );
  }

  private choose(
    binance: SourceQuote | null,
    coingecko: SourceQuote | null,
    at: number,
    correlationId?: string,
  ): Chosen | null {
    if (binance && coingecko) {
      if (withinBp(binance.priceMicroUsdt, coingecko.priceMicroUsdt, this.maxSourceDivergenceBp)) {
        return { ...binance, corroborated: true };
      }
      logger.error(
        {
          binance: formatScaled(binance.priceMicroUsdt, USDT_SCALE),
          coingecko: formatScaled(coingecko.priceMicroUsdt, USDT_SCALE),
          maxDivergenceBp: Number(this.maxSourceDivergenceBp),
          correlationId,
        },
        'ETH price sources disagree — neither quote is trusted',
      );
      return null;
    }

    const single = binance ?? coingecko;
    if (!single) return null;
    const anchor =
      this.reference && at - this.reference.acceptedAt < this.anchorMaxAgeMs
        ? this.reference
        : null;
    if (!anchor) {
      logger.warn(
        {
          source: single.source,
          quote: formatScaled(single.priceMicroUsdt, USDT_SCALE),
          correlationId,
        },
        'ETH price: only one source answered and there is no recent two-source price to check it against — quote not trusted',
      );
      return null;
    }
    if (!withinBp(single.priceMicroUsdt, anchor.priceMicroUsdt, this.maxUncorroboratedJumpBp)) {
      logger.error(
        {
          source: single.source,
          quote: formatScaled(single.priceMicroUsdt, USDT_SCALE),
          reference: formatScaled(anchor.priceMicroUsdt, USDT_SCALE),
          maxJumpBp: Number(this.maxUncorroboratedJumpBp),
          correlationId,
        },
        'ETH price moved beyond the uncorroborated limit from the last two-source price and the other source is unavailable — quote not trusted',
      );
      return null;
    }
    return { ...single, corroborated: false };
  }

  private fromAccepted(accepted: Accepted, at: number): EthPriceQuote {
    return {
      priceMicroUsdt: accepted.priceMicroUsdt,
      priceUsdt: formatScaled(accepted.priceMicroUsdt, USDT_SCALE),
      source: 'cache',
      ageSeconds: Math.floor((at - accepted.acceptedAt) / 1000),
    };
  }

  private async tryBinance(correlationId?: string): Promise<SourceQuote | null> {
    try {
      const body = (await this.getJson(BINANCE_URL)) as { price?: unknown; closeTime?: unknown };
      const price = this.validPrice(body.price);
      this.assertFresh(body.closeTime, 1);
      return { source: 'binance', priceMicroUsdt: price };
    } catch (err) {
      logger.warn({ err: (err as Error).message, correlationId }, 'Binance ETHUSDT quote rejected');
      return null;
    }
  }

  private async tryCoingecko(correlationId?: string): Promise<SourceQuote | null> {
    try {
      const body = (await this.getJson(COINGECKO_URL)) as {
        ethereum?: { usd?: unknown; last_updated_at?: unknown };
      };
      const usd = body.ethereum?.usd;
      const price = this.validPrice(typeof usd === 'number' ? String(usd) : usd);
      this.assertFresh(body.ethereum?.last_updated_at, 1000);
      return { source: 'coingecko', priceMicroUsdt: price };
    } catch (err) {
      logger.warn(
        { err: (err as Error).message, correlationId },
        'CoinGecko ethereum quote rejected',
      );
      return null;
    }
  }

  private async getJson(url: string): Promise<unknown> {
    const controller = new AbortController();
    const timer = setTimeout(() => controller.abort(), this.requestTimeoutMs);
    try {
      const response = await this.fetchFn(url, {
        headers: { accept: 'application/json' },
        signal: controller.signal,
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return await response.json();
    } finally {
      clearTimeout(timer);
    }
  }

  private validPrice(value: unknown): bigint {
    const scaled = typeof value === 'string' ? parseScaled(value, USDT_SCALE) : null;
    if (scaled === null) throw new Error(`unparsable price ${JSON.stringify(value)}`);
    if (scaled < this.minPriceMicro || scaled > this.maxPriceMicro) {
      throw new Error(
        `price ${formatScaled(scaled, USDT_SCALE)} outside the sanity band ${formatScaled(this.minPriceMicro, USDT_SCALE)}–${formatScaled(this.maxPriceMicro, USDT_SCALE)}`,
      );
    }
    return scaled;
  }

  /** `stamp × unitMs` is the source's own time of the quote; missing or old → rejected. */
  private assertFresh(stamp: unknown, unitMs: number): void {
    if (typeof stamp !== 'number' || !Number.isFinite(stamp) || stamp <= 0) {
      throw new Error(`quote has no timestamp (${JSON.stringify(stamp)})`);
    }
    const age = this.now() - stamp * unitMs;
    if (age > this.sourceMaxAgeMs) {
      throw new Error(
        `quote is ${Math.round(age / 1000)} s old (limit ${this.sourceMaxAgeMs / 1000} s)`,
      );
    }
  }
}

function mustScale(text: string): bigint {
  const value = parseScaled(text, USDT_SCALE);
  if (value === null || value <= 0n) throw new RangeError(`invalid price bound ${text}`);
  return value;
}

/** |a − b| ≤ ref × bp / 10,000, with `b` as the reference. */
function withinBp(a: bigint, b: bigint, bp: bigint): boolean {
  const diff = a > b ? a - b : b - a;
  return diff * 10_000n <= b * bp;
}
