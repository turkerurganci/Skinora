import { describe, it, expect, vi } from 'vitest';
import { TrxPriceService } from './TrxPriceService.js';
import { SidecarError } from '../errors/SidecarError.js';

function jsonResponse(body: unknown, ok = true, status = 200): Response {
  return {
    ok,
    status,
    json: async () => body,
  } as unknown as Response;
}

describe('TrxPriceService', () => {
  it('returns the Binance price on the primary path', async () => {
    const fetchFn = vi.fn(async (url: string) => jsonResponse({ price: '0.3244', url }));
    const service = new TrxPriceService({ fetchFn: fetchFn as unknown as typeof fetch });

    const quote = await service.getPrice();

    expect(quote).toEqual({ priceUsdt: 0.3244, source: 'binance' });
    expect(fetchFn).toHaveBeenCalledTimes(1);
    expect(String(fetchFn.mock.calls[0][0])).toContain('binance');
  });

  it('falls back to CoinGecko when Binance fails', async () => {
    const fetchFn = vi.fn(async (url: string) => {
      if (String(url).includes('binance')) return jsonResponse({}, false, 500);
      return jsonResponse({ tron: { usd: 0.3241 } });
    });
    const service = new TrxPriceService({ fetchFn: fetchFn as unknown as typeof fetch });

    const quote = await service.getPrice();

    expect(quote).toEqual({ priceUsdt: 0.3241, source: 'coingecko' });
  });

  it('serves a fresh cached quote without refetching', async () => {
    let nowMs = 1_000_000;
    const fetchFn = vi.fn(async () => jsonResponse({ price: '0.30' }));
    const service = new TrxPriceService({
      fetchFn: fetchFn as unknown as typeof fetch,
      cacheTtlMs: 300_000,
      now: () => nowMs,
    });

    await service.getPrice();
    nowMs += 60_000; // still inside TTL
    const quote = await service.getPrice();

    expect(quote).toEqual({ priceUsdt: 0.3, source: 'cache' });
    expect(fetchFn).toHaveBeenCalledTimes(1);
  });

  it('serves a stale cached quote when both providers fail within the grace window', async () => {
    let nowMs = 1_000_000;
    let healthy = true;
    const fetchFn = vi.fn(async () =>
      healthy ? jsonResponse({ price: '0.30' }) : jsonResponse({}, false, 503),
    );
    const service = new TrxPriceService({
      fetchFn: fetchFn as unknown as typeof fetch,
      cacheTtlMs: 300_000,
      staleGraceMs: 3_600_000,
      now: () => nowMs,
    });

    await service.getPrice();
    healthy = false;
    nowMs += 600_000; // past TTL, inside grace
    const quote = await service.getPrice();

    expect(quote).toEqual({ priceUsdt: 0.3, source: 'cache' });
  });

  it('throws TRX_PRICE_UNAVAILABLE when both providers fail and no cache exists', async () => {
    const fetchFn = vi.fn(async () => jsonResponse({}, false, 503));
    const service = new TrxPriceService({ fetchFn: fetchFn as unknown as typeof fetch });

    await expect(service.getPrice()).rejects.toMatchObject({
      code: 'TRX_PRICE_UNAVAILABLE',
      retryable: true,
    });
    await expect(service.getPrice()).rejects.toBeInstanceOf(SidecarError);
  });

  it('rejects an unparsable Binance price and uses the fallback', async () => {
    const fetchFn = vi.fn(async (url: string) => {
      if (String(url).includes('binance')) return jsonResponse({ price: 'not-a-number' });
      return jsonResponse({ tron: { usd: 0.5 } });
    });
    const service = new TrxPriceService({ fetchFn: fetchFn as unknown as typeof fetch });

    const quote = await service.getPrice();

    expect(quote.source).toBe('coingecko');
    expect(quote.priceUsdt).toBe(0.5);
  });
});
