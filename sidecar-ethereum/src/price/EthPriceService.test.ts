import { describe, expect, it } from 'vitest';
import { EthPriceService } from './EthPriceService.js';
import { SidecarError } from '../errors/SidecarError.js';

const T0 = 1_792_324_190_000; // ms

type Answer = Response | Error | (() => Response | Error);

/**
 * Two fake price sources. Each test sets what Binance and CoinGecko answer;
 * every call is counted per source.
 */
function sources() {
  let clock = T0;
  const calls = { binance: 0, coingecko: 0 };
  let binance: Answer = binanceQuote('2414.57999137', T0);
  let coingecko: Answer = coingeckoQuote(2411.96, T0);
  const fetchFn = (async (input: string | URL | Request) => {
    const url = String(input);
    const which = url.includes('binance') ? 'binance' : 'coingecko';
    calls[which]++;
    let answer = which === 'binance' ? binance : coingecko;
    if (typeof answer === 'function') answer = answer();
    if (answer instanceof Error) throw answer;
    return answer.clone();
  }) as typeof fetch;
  const service = new EthPriceService({ fetchFn, now: () => clock });
  return {
    service,
    calls,
    setBinance: (a: Answer) => (binance = a),
    setCoingecko: (a: Answer) => (coingecko = a),
    advance: (ms: number) => (clock += ms),
    now: () => clock,
  };
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'content-type': 'application/json' },
  });
}

function binanceQuote(price: unknown, closeTimeMs: unknown): Response {
  return json({ mins: 5, price, closeTime: closeTimeMs });
}

function coingeckoQuote(usd: unknown, lastUpdatedMs: number | undefined): Response {
  return json({
    ethereum: {
      usd,
      ...(lastUpdatedMs === undefined ? {} : { last_updated_at: Math.floor(lastUpdatedMs / 1000) }),
    },
  });
}

const down = () =>
  Object.assign(new TypeError('fetch failed'), { cause: { code: 'ECONNREFUSED' } });

async function unavailable(p: Promise<unknown>): Promise<void> {
  await expect(p).rejects.toSatisfy(
    (e: unknown) => e instanceof SidecarError && e.code === 'ETH_PRICE_UNAVAILABLE' && e.retryable,
  );
}

describe('EthPriceService — two sources agree', () => {
  it('uses the primary (Binance 5-min average), exact to 6 decimals', async () => {
    const s = sources();
    const q = await s.service.getPrice();
    expect(q).toEqual({
      priceMicroUsdt: 2_414_579_991n,
      priceUsdt: '2414.579991',
      source: 'binance',
      ageSeconds: 0,
    });
    expect(s.calls).toEqual({ binance: 1, coingecko: 1 });
  });

  it('a genuine move larger than 20% is accepted when both sources confirm it', async () => {
    const s = sources();
    await s.service.getPrice();
    s.advance(6 * 60_000);
    s.setBinance(binanceQuote('1700.00', s.now()));
    s.setCoingecko(coingeckoQuote(1701.5, s.now()));
    expect((await s.service.getPrice()).priceUsdt).toBe('1700.000000');
  });
});

describe('EthPriceService — cache', () => {
  it('serves the accepted price for 5 minutes without asking again', async () => {
    const s = sources();
    await s.service.getPrice();
    s.advance(5 * 60_000 - 1);
    const q = await s.service.getPrice();
    expect(q.source).toBe('cache');
    expect(q.ageSeconds).toBe(299);
    expect(s.calls).toEqual({ binance: 1, coingecko: 1 });
    s.advance(1);
    expect((await s.service.getPrice()).source).toBe('binance');
    expect(s.calls).toEqual({ binance: 2, coingecko: 2 });
  });

  it('concurrent callers share one refresh', async () => {
    const s = sources();
    const quotes = await Promise.all([
      s.service.getPrice(),
      s.service.getPrice(),
      s.service.getPrice(),
    ]);
    expect(new Set(quotes.map((q) => q.priceUsdt)).size).toBe(1);
    expect(s.calls).toEqual({ binance: 1, coingecko: 1 });
  });
});

describe('EthPriceService — one source down', () => {
  it('cold start: a single quote is not trusted — both sources must agree first', async () => {
    const s = sources();
    s.setBinance(down());
    await unavailable(s.service.getPrice());
    // Once both answer and agree, the price is accepted.
    s.setBinance(binanceQuote('2414.57999137', s.now()));
    expect((await s.service.getPrice()).source).toBe('binance');
  });

  it('a single quote within 20% of the last accepted price is used', async () => {
    const s = sources();
    await s.service.getPrice();
    s.advance(6 * 60_000);
    s.setCoingecko(json({}, 429));
    s.setBinance(binanceQuote('2800.00', s.now())); // +16%
    expect((await s.service.getPrice()).priceUsdt).toBe('2800.000000');
  });

  it('a single quote more than 20% away is not trusted — the last accepted price is served', async () => {
    const s = sources();
    await s.service.getPrice();
    s.advance(6 * 60_000);
    s.setCoingecko(down());
    s.setBinance(binanceQuote('3000.00', s.now())); // +24%
    const q = await s.service.getPrice();
    expect(q).toMatchObject({ source: 'cache', priceUsdt: '2414.579991', ageSeconds: 360 });
  });

  it('a two-source price older than 24 h is no reference — a single quote is not trusted', async () => {
    const s = sources();
    await s.service.getPrice();
    s.advance(24 * 60 * 60_000 - 1);
    s.setCoingecko(down());
    s.setBinance(binanceQuote('2500.00', s.now()));
    expect((await s.service.getPrice()).priceUsdt).toBe('2500.000000'); // reference still valid
    s.advance(24 * 60 * 60_000);
    s.setBinance(binanceQuote('2500.00', s.now()));
    await unavailable(s.service.getPrice());
  });

  // Manager decision (T154 validation): only a two-source agreement moves the
  // reference. Were a single-source price to become the next base, one source
  // could walk the price 20% per refresh — 2,000 → 16,128 in an hour.
  it('no chained drift: a single source rising 19% per step is refused at the second step', async () => {
    const s = sources();
    s.setBinance(binanceQuote('2000.00', T0));
    s.setCoingecko(coingeckoQuote(2000, T0));
    expect((await s.service.getPrice()).priceUsdt).toBe('2000.000000'); // reference
    s.setCoingecko(down());
    s.advance(6 * 60_000);
    s.setBinance(binanceQuote('2380.00', s.now())); // +19% of the reference
    expect(await s.service.getPrice()).toMatchObject({
      source: 'binance',
      priceUsdt: '2380.000000',
    });
    s.advance(6 * 60_000);
    s.setBinance(binanceQuote('2832.20', s.now())); // +19% of 2380, +41.6% of the reference
    expect(await s.service.getPrice()).toMatchObject({
      source: 'cache',
      priceUsdt: '2380.000000',
    });
    // Both sources confirming the move do move it.
    s.advance(6 * 60_000);
    s.setCoingecko(coingeckoQuote(2830, s.now()));
    expect(await s.service.getPrice()).toMatchObject({
      source: 'binance',
      priceUsdt: '2832.200000',
    });
  });

  it('a single-source price does not restart the 24 h reference window', async () => {
    const s = sources();
    await s.service.getPrice(); // reference 2414.58 at T0
    s.setCoingecko(down());
    s.advance(23 * 60 * 60_000 + 30 * 60_000);
    s.setBinance(binanceQuote('2500.00', s.now()));
    expect((await s.service.getPrice()).source).toBe('binance'); // reference 23.5 h old
    s.advance(31 * 60_000); // reference 24 h 1 min old; the single-source price 31 min old
    s.setBinance(binanceQuote('2500.00', s.now()));
    expect(await s.service.getPrice()).toMatchObject({
      source: 'cache',
      priceUsdt: '2500.000000',
      ageSeconds: 31 * 60,
    });
  });
});

describe('EthPriceService — wrong prices', () => {
  it('two sources more than 2% apart → neither is trusted', async () => {
    const s = sources();
    s.setCoingecko(coingeckoQuote(2363.0, T0)); // −2.14%
    await unavailable(s.service.getPrice());
  });

  it('two sources exactly at the 2% limit are accepted', async () => {
    const s = sources();
    s.setBinance(binanceQuote('2040.00', T0));
    s.setCoingecko(coingeckoQuote(2000, T0));
    expect((await s.service.getPrice()).priceUsdt).toBe('2040.000000');
    const t = sources();
    t.setBinance(binanceQuote('2040.01', T0));
    t.setCoingecko(coingeckoQuote(2000, T0));
    await unavailable(t.service.getPrice());
  });

  it.each([
    ['below the band (a wrong pair, e.g. ETHBTC)', '0.0371'],
    ['above the band (a unit slip)', '2414579991'],
    ['zero', '0'],
    ['negative', '-2414.5'],
    ['exponent', '2.4e3'],
    ['a number instead of a string', 2414.5],
    ['missing', undefined],
  ])(
    'Binance price %s is rejected — the valid CoinGecko quote answers alone',
    async (_label, price) => {
      const s = sources();
      await s.service.getPrice(); // anchor 2414.58
      s.advance(6 * 60_000);
      s.setBinance(binanceQuote(price, s.now()));
      s.setCoingecko(coingeckoQuote(2411.96, s.now()));
      // Had the bad quote been taken as valid, the sources would disagree and
      // the stale cache (source 'cache') would answer instead.
      expect(await s.service.getPrice()).toMatchObject({
        source: 'coingecko',
        priceUsdt: '2411.960000',
      });
    },
  );

  it.each([
    ['below the band (a wrong pair, e.g. ETHBTC)', '0.0371', 0.0371],
    ['above the band (a unit slip)', '2414579991', 2414579991],
  ])('both sources agreeing %s are still refused', async (_label, binance, coingecko) => {
    const s = sources();
    s.setBinance(binanceQuote(binance, T0));
    s.setCoingecko(coingeckoQuote(coingecko, T0));
    await unavailable(s.service.getPrice());
  });

  it('a quote older than 10 minutes by its own timestamp is rejected (halted market)', async () => {
    const s = sources();
    s.setBinance(binanceQuote('2414.58', T0 - 10 * 60_000 - 1));
    s.setCoingecko(coingeckoQuote(2411.96, T0 - 11 * 60_000));
    await unavailable(s.service.getPrice());
  });

  // Binance's closeTime is milliseconds, CoinGecko's last_updated_at seconds.
  // With a reference in place the valid CoinGecko quote answers alone; had the
  // Binance quote been read as fresh, the two would agree and Binance would.
  it('Binance closeTime is milliseconds: 10 min + 1 ms old is rejected, CoinGecko answers alone', async () => {
    const s = sources();
    await s.service.getPrice(); // reference
    s.advance(6 * 60_000);
    s.setBinance(binanceQuote('2414.58', s.now() - 10 * 60_000 - 1));
    s.setCoingecko(coingeckoQuote(2411.96, s.now()));
    expect(await s.service.getPrice()).toMatchObject({
      source: 'coingecko',
      priceUsdt: '2411.960000',
    });
  });

  it('a Binance closeTime in seconds reads as 1970 and is rejected', async () => {
    const s = sources();
    await s.service.getPrice(); // reference
    s.advance(6 * 60_000);
    s.setBinance(binanceQuote('2414.58', Math.floor(s.now() / 1000)));
    s.setCoingecko(coingeckoQuote(2411.96, s.now()));
    expect((await s.service.getPrice()).source).toBe('coingecko');
  });

  it('a quote at exactly 10 minutes old is still accepted', async () => {
    const s = sources();
    s.setBinance(binanceQuote('2414.58', T0 - 10 * 60_000));
    s.setCoingecko(coingeckoQuote(2411.96, T0 - 10 * 60_000));
    expect((await s.service.getPrice()).source).toBe('binance');
  });

  it('a quote with no timestamp is rejected', async () => {
    const s = sources();
    s.setBinance(binanceQuote('2414.58', undefined));
    s.setCoingecko(coingeckoQuote(2411.96, undefined));
    await unavailable(s.service.getPrice());
  });

  it('HTTP errors and non-JSON bodies are rejections, not prices', async () => {
    const s = sources();
    s.setBinance(json({ code: -1003, msg: 'banned' }, 418));
    s.setCoingecko(new Response('<html>rate limited</html>', { status: 200 }));
    await unavailable(s.service.getPrice());
  });
});

describe('EthPriceService — stale grace', () => {
  it('serves the last accepted price for up to 60 minutes when nothing is trusted, then fails', async () => {
    const s = sources();
    await s.service.getPrice();
    s.setBinance(down());
    s.setCoingecko(down());
    s.advance(60 * 60_000 - 1);
    expect(await s.service.getPrice()).toMatchObject({ source: 'cache', ageSeconds: 3599 });
    s.advance(1);
    await unavailable(s.service.getPrice());
  });

  it('with no accepted price at all, an outage is ETH_PRICE_UNAVAILABLE', async () => {
    const s = sources();
    s.setBinance(down());
    s.setCoingecko(down());
    await unavailable(s.service.getPrice());
  });

  it('a rejected outlier does not replace the accepted price', async () => {
    const s = sources();
    await s.service.getPrice();
    s.advance(6 * 60_000);
    s.setCoingecko(down());
    s.setBinance(binanceQuote('9000.00', s.now()));
    await s.service.getPrice(); // stale answer
    s.setBinance(binanceQuote('2500.00', s.now()));
    // Measured against 2414.58 (still the anchor), +3.5% → accepted.
    expect((await s.service.getPrice()).priceUsdt).toBe('2500.000000');
  });
});
