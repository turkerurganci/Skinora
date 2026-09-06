import { logger } from '../logger.js';
import { SidecarError } from '../errors/SidecarError.js';

/**
 * TRX/USDT spot price with layered sources (Prova-GasFeeChargedIsFixedGuess):
 *
 * <list type="number">
 *   <item>Binance public ticker — primary. No API key; the public
 *     weight budget (~6000/min per IP) dwarfs our call rate because results
 *     are cached for <see cref="cacheTtlMs"/>.</item>
 *   <item>CoinGecko simple-price — fallback when Binance errors. Free tier,
 *     no key; stricter rate limit, reached only when Binance is down.</item>
 *   <item>Stale cache — when BOTH providers fail, a previous quote younger
 *     than <see cref="staleGraceMs"/> is served with a warning. TRX/USDT
 *     volatility over an hour is far smaller than the estimate's other
 *     error bars (energy price, resource drift between estimate and send).</item>
 * </list>
 *
 * All three exhausted → <c>TRX_PRICE_UNAVAILABLE</c> (retryable). The backend
 * caller treats any estimate failure as "fall back to the static
 * SystemSetting", so a price outage degrades to pre-round behaviour instead
 * of blocking a money path.
 */

const BINANCE_URL = 'https://api.binance.com/api/v3/ticker/price?symbol=TRXUSDT';
const COINGECKO_URL = 'https://api.coingecko.com/api/v3/simple/price?ids=tron&vs_currencies=usd';

export type TrxPriceSource = 'binance' | 'coingecko' | 'cache';

export interface TrxPriceQuote {
  /** USDT per 1 TRX. */
  priceUsdt: number;
  source: TrxPriceSource;
}

export interface TrxPriceServiceDeps {
  /** Fresh-cache TTL. Default 5 min. */
  cacheTtlMs?: number;
  /** How old a cached quote may be when both providers fail. Default 60 min. */
  staleGraceMs?: number;
  fetchFn?: typeof fetch;
  now?: () => number;
}

interface CachedQuote {
  priceUsdt: number;
  fetchedAt: number;
}

export class TrxPriceService {
  private readonly cacheTtlMs: number;
  private readonly staleGraceMs: number;
  private readonly fetchFn: typeof fetch;
  private readonly now: () => number;
  private cached: CachedQuote | null = null;

  constructor(deps: TrxPriceServiceDeps = {}) {
    this.cacheTtlMs = deps.cacheTtlMs ?? 5 * 60_000;
    this.staleGraceMs = deps.staleGraceMs ?? 60 * 60_000;
    this.fetchFn = deps.fetchFn ?? fetch;
    this.now = deps.now ?? Date.now;
  }

  async getPrice(correlationId?: string): Promise<TrxPriceQuote> {
    const at = this.now();
    if (this.cached && at - this.cached.fetchedAt < this.cacheTtlMs) {
      return { priceUsdt: this.cached.priceUsdt, source: 'cache' };
    }

    const binance = await this.tryBinance(correlationId);
    if (binance !== null) {
      this.cached = { priceUsdt: binance, fetchedAt: at };
      return { priceUsdt: binance, source: 'binance' };
    }

    const coingecko = await this.tryCoingecko(correlationId);
    if (coingecko !== null) {
      this.cached = { priceUsdt: coingecko, fetchedAt: at };
      return { priceUsdt: coingecko, source: 'coingecko' };
    }

    if (this.cached && at - this.cached.fetchedAt < this.staleGraceMs) {
      logger.warn(
        { ageMs: at - this.cached.fetchedAt, correlationId },
        'TRX price providers unavailable — serving stale cached quote',
      );
      return { priceUsdt: this.cached.priceUsdt, source: 'cache' };
    }

    throw new SidecarError(
      'TRX/USDT price unavailable: Binance and CoinGecko both failed and no cached quote is fresh enough.',
      'TRX_PRICE_UNAVAILABLE',
      true,
    );
  }

  private async tryBinance(correlationId?: string): Promise<number | null> {
    try {
      const response = await this.fetchFn(BINANCE_URL, { headers: { accept: 'application/json' } });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const body = (await response.json()) as { price?: unknown };
      const price = Number(body.price);
      if (!Number.isFinite(price) || price <= 0) throw new Error(`unparsable price: ${body.price}`);
      return price;
    } catch (err) {
      logger.warn({ err: (err as Error).message, correlationId }, 'Binance TRXUSDT ticker failed');
      return null;
    }
  }

  private async tryCoingecko(correlationId?: string): Promise<number | null> {
    try {
      const response = await this.fetchFn(COINGECKO_URL, {
        headers: { accept: 'application/json' },
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const body = (await response.json()) as { tron?: { usd?: unknown } };
      const price = Number(body.tron?.usd);
      if (!Number.isFinite(price) || price <= 0)
        throw new Error(`unparsable price: ${body.tron?.usd}`);
      return price;
    } catch (err) {
      logger.warn({ err: (err as Error).message, correlationId }, 'CoinGecko tron price failed');
      return null;
    }
  }
}
