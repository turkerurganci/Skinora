import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../logger.js', () => ({
  logger: {
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
    debug: vi.fn(),
    child: vi.fn().mockReturnThis(),
  },
}));

import {
  InventoryService,
  InventoryPrivateError,
  InventoryShortReadError,
  SteamUnavailableError,
  buildInventoryResponse,
  type InventoryFetcher,
  type InventoryReadResult,
  type InventoryResponse,
} from './InventoryService.js';
import type { TaskQueue } from '../queue/RateLimitedQueue.js';
import { InMemoryInventoryCache, INVENTORY_CACHE_TTL_SECONDS } from '../cache/InventoryCache.js';

/**
 * Narrow a read result to its PUBLIC payload, failing the test loudly if the
 * read was Private/Unavailable. Keeps the happy-path assertions readable
 * without sprinkling non-null assertions through every test.
 */
function expectPublic(result: InventoryReadResult): InventoryResponse {
  expect(result.visibility).toBe('PUBLIC');
  if (result.visibility !== 'PUBLIC') throw new Error('unreachable');
  return result.inventory;
}

/** Pass-through queue double — records dispatches without any timing behaviour. */
function buildQueue(): TaskQueue & { calls: number } {
  const queue: TaskQueue & { calls: number } = {
    calls: 0,
    async enqueue<T>(fn: () => Promise<T>): Promise<T> {
      queue.calls += 1;
      return fn();
    },
  };
  return queue;
}

interface FakeCEconItem {
  id: string;
  assetid?: string | number;
  classid: string | number;
  instanceid?: string | number | null;
  name: string;
  market_hash_name: string;
  type?: string;
  tags?: Array<{ category: string; name?: string; localized_tag_name?: string }>;
  tradable: boolean;
  marketable: boolean;
  getImageURL?: () => string;
  /** T122 runbook §5 — steamcommunity attaches these per asset when Steam sends them. */
  asset_properties?: Array<{
    propertyid?: number | string;
    name?: string;
    int_value?: number | string;
    float_value?: number | string;
    string_value?: string;
  }>;
  /** T122-B / runbook §6.1 — present on real items; deliberately never mapped. */
  market_tradable_restriction?: number;
}

function buildAk47(overrides: Partial<FakeCEconItem> = {}): FakeCEconItem {
  return {
    id: '27348562891',
    assetid: '27348562891',
    classid: '310776959',
    instanceid: '188530139',
    name: 'AK-47 | Redline',
    market_hash_name: 'AK-47 | Redline (Field-Tested)',
    type: 'Classified Rifle',
    tags: [
      { category: 'Type', localized_tag_name: 'Rifle' },
      { category: 'Exterior', localized_tag_name: 'Field-Tested' },
    ],
    tradable: true,
    marketable: true,
    getImageURL: () => 'https://steamcdn.test/ak47.png',
    ...overrides,
  };
}

/**
 * Fetcher double. `totalInventoryCount` defaults to the number of items, i.e.
 * a COMPLETE read — the T125 short-read guard is silent unless a test asks for
 * a mismatch explicitly.
 */
function buildFetcher(
  items: FakeCEconItem[] = [],
  totalInventoryCount: number | null = items.length,
): InventoryFetcher & { calls: number } {
  const stub: InventoryFetcher & { calls: number } = {
    calls: 0,
    async fetch() {
      stub.calls += 1;
      return { items: items as never, totalInventoryCount };
    },
  };
  return stub;
}

describe('InventoryService (T67 — 08 §2.3)', () => {
  let cache: InMemoryInventoryCache;

  beforeEach(() => {
    cache = new InMemoryInventoryCache();
  });

  it('maps CEconItem fields to the 07 §6.1 envelope shape', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const svc = new InventoryService(fetcher, cache);

    const result = expectPublic(await svc.getInventory('76561198000000001'));

    expect(result.totalCount).toBe(1);
    expect(result.tradeableCount).toBe(1);
    expect(result.items).toEqual([
      {
        assetId: '27348562891',
        classId: '310776959',
        instanceId: '188530139',
        name: 'AK-47 | Redline',
        marketHashName: 'AK-47 | Redline (Field-Tested)',
        type: 'Rifle',
        exterior: 'Field-Tested',
        iconUrl: 'https://steamcdn.test/ak47.png',
        tradable: true,
        marketable: true,
      },
    ]);
  });

  it('counts tradeable items separately from total', async () => {
    const fetcher = buildFetcher([
      buildAk47({ assetid: '1', tradable: true }),
      buildAk47({ assetid: '2', tradable: false }),
      buildAk47({ assetid: '3', tradable: false }),
    ]);
    const svc = new InventoryService(fetcher, cache);

    const result = expectPublic(await svc.getInventory('76561198000000002'));

    expect(result.totalCount).toBe(3);
    expect(result.tradeableCount).toBe(1);
  });

  it('serves cached responses without re-calling the fetcher (TTL 2 dakika)', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const svc = new InventoryService(fetcher, cache);

    await svc.getInventory('76561198000000003');
    await svc.getInventory('76561198000000003');

    expect(fetcher.calls).toBe(1);
    expect(INVENTORY_CACHE_TTL_SECONDS).toBe(120);
  });

  it('re-fetches after invalidation', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const svc = new InventoryService(fetcher, cache);

    await svc.getInventory('76561198000000004');
    await svc.invalidate('76561198000000004');
    await svc.getInventory('76561198000000004');

    expect(fetcher.calls).toBe(2);
  });

  it('reports PRIVATE (not an exception) when steamcommunity says the profile is private', async () => {
    const fetcher: InventoryFetcher = {
      async fetch() {
        throw new Error('This profile is private.');
      },
    };
    const svc = new InventoryService(fetcher, cache);

    const result = await svc.getInventory('76561198000000005');

    expect(result.visibility).toBe('PRIVATE');
    if (result.visibility !== 'PRIVATE') throw new Error('unreachable');
    expect(result.error).toBeInstanceOf(InventoryPrivateError);
    expect(result.error.code).toBe('INVENTORY_PRIVATE');
    // 08 §2.7: a private profile needs a user action, so it is NOT retryable.
    expect(result.error.retryable).toBe(false);
  });

  it('reports UNAVAILABLE for any other fetch error', async () => {
    const fetcher: InventoryFetcher = {
      async fetch() {
        throw new Error('HTTP 503 Service Unavailable');
      },
    };
    const svc = new InventoryService(fetcher, cache);

    const result = await svc.getInventory('76561198000000006');

    expect(result.visibility).toBe('UNAVAILABLE');
    if (result.visibility !== 'UNAVAILABLE') throw new Error('unreachable');
    expect(result.error).toBeInstanceOf(SteamUnavailableError);
    expect(result.error.code).toBe('STEAM_UNAVAILABLE');
    // 08 §2.7: an upstream failure is retried, never read as "not delivered".
    expect(result.error.retryable).toBe(true);
  });

  it('does not cache when the fetch fails (next call retries)', async () => {
    let attempts = 0;
    const fetcher: InventoryFetcher = {
      async fetch() {
        attempts += 1;
        if (attempts === 1) {
          throw new Error('HTTP 503');
        }
        return { items: [buildAk47()] as never, totalInventoryCount: 1 };
      },
    };
    const svc = new InventoryService(fetcher, cache);

    const failed = await svc.getInventory('76561198000000007');
    expect(failed.visibility).toBe('UNAVAILABLE');
    const result = expectPublic(await svc.getInventory('76561198000000007'));

    expect(result.totalCount).toBe(1);
    expect(attempts).toBe(2);
  });

  it('falls back to CEconItem.type when the Type tag is absent', async () => {
    const fetcher = buildFetcher([
      buildAk47({
        tags: [{ category: 'Exterior', localized_tag_name: 'Field-Tested' }],
        type: 'Classified Rifle',
      }),
    ]);
    const svc = new InventoryService(fetcher, cache);

    const result = expectPublic(await svc.getInventory('76561198000000008'));

    expect(result.items[0].type).toBe('Classified Rifle');
  });

  it('handles instanceid=0 / empty tags / no type fallback gracefully', async () => {
    const fetcher = buildFetcher([
      buildAk47({
        instanceid: 0,
        tags: [],
        type: undefined,
      }),
    ]);
    const svc = new InventoryService(fetcher, cache);

    const result = expectPublic(await svc.getInventory('76561198000000009'));

    // assetid/classid/instanceid land as strings regardless of source type;
    // numeric 0 round-trips to "0" rather than degrading to null.
    expect(result.items[0].instanceId).toBe('0');
    expect(result.items[0].type).toBeNull();
    expect(result.items[0].exterior).toBeNull();
  });

  it('returns an empty envelope for a public-but-empty inventory', async () => {
    const fetcher = buildFetcher([]);
    const svc = new InventoryService(fetcher, cache);

    const read = await svc.getInventory('76561198000000010');

    // The money-safety distinction of 08 §2.3: an empty PUBLIC inventory is
    // EVIDENCE the item is absent. It must never be reported as PRIVATE
    // (evidence path closed) or UNAVAILABLE (no information).
    expect(read.visibility).toBe('PUBLIC');
    const result = expectPublic(read);
    expect(result.items).toEqual([]);
    expect(result.totalCount).toBe(0);
    expect(result.tradeableCount).toBe(0);
  });

  it('buildInventoryResponse is a pure function over the raw items', () => {
    const raw = [buildAk47({ assetid: '1' }), buildAk47({ assetid: '2', tradable: false })];

    const result = buildInventoryResponse(raw as never);

    expect(result.totalCount).toBe(2);
    expect(result.tradeableCount).toBe(1);
    expect(result.items.map((i) => i.assetId)).toEqual(['1', '2']);
  });
});

describe('InventoryService — refresh cache bypass (T120 — 08 §2.3)', () => {
  let cache: InMemoryInventoryCache;

  beforeEach(() => {
    cache = new InMemoryInventoryCache();
  });

  it('skips the cache read and re-fetches when refresh is set', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const svc = new InventoryService(fetcher, cache);

    await svc.getInventory('76561198000000030');
    await svc.getInventory('76561198000000030', { refresh: true });

    expect(fetcher.calls).toBe(2);
  });

  it('defaults to the cached read when no options are passed', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const svc = new InventoryService(fetcher, cache);

    await svc.getInventory('76561198000000031');
    await svc.getInventory('76561198000000031', {});
    await svc.getInventory('76561198000000031', { refresh: false });

    expect(fetcher.calls).toBe(1);
  });

  it('writes the bypassed result back to cache (next ordinary read is warm)', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const svc = new InventoryService(fetcher, cache);

    await svc.getInventory('76561198000000032', { refresh: true });
    const second = expectPublic(await svc.getInventory('76561198000000032'));

    // One upstream call total: the refresh populated the cache rather than
    // merely bypassing it, so the following ordinary read is served warm.
    expect(fetcher.calls).toBe(1);
    expect(second.totalCount).toBe(1);
  });

  it('reports miss / hit / bypass separately to the cache observer', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const outcomes: string[] = [];
    const svc = new InventoryService(fetcher, cache, undefined, (o) => outcomes.push(o));

    await svc.getInventory('76561198000000034');
    await svc.getInventory('76561198000000034');
    await svc.getInventory('76561198000000034', { refresh: true });

    // `bypass` must stay distinct from `miss`: it measures delivery-verification
    // load against the Community queue budget, not cache effectiveness (08 §2.6).
    expect(outcomes).toEqual(['miss', 'hit', 'bypass']);
  });

  it('leaves an existing cache entry intact when the refresh fetch fails', async () => {
    let attempts = 0;
    const fetcher: InventoryFetcher = {
      async fetch() {
        attempts += 1;
        if (attempts === 1) return { items: [buildAk47()] as never, totalInventoryCount: 1 };
        throw new Error('HTTP 503');
      },
    };
    const svc = new InventoryService(fetcher, cache);

    await svc.getInventory('76561198000000033');
    const refreshed = await svc.getInventory('76561198000000033', { refresh: true });
    const afterwards = await svc.getInventory('76561198000000033');

    // The refresh reports UNAVAILABLE rather than silently downgrading the
    // caller to stale data — but it does not destroy the entry either, so an
    // ordinary reader that tolerates 2-minute-old data still gets an answer.
    expect(refreshed.visibility).toBe('UNAVAILABLE');
    expect(expectPublic(afterwards).totalCount).toBe(1);
    expect(attempts).toBe(2);
  });
});

describe('InventoryService — Community queue (T120 — 08 §2.6)', () => {
  let cache: InMemoryInventoryCache;

  beforeEach(() => {
    cache = new InMemoryInventoryCache();
  });

  it('dispatches upstream fetches through the injected queue', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const queue = buildQueue();
    const svc = new InventoryService(fetcher, cache, queue);

    const result = expectPublic(await svc.getInventory('76561198000000040'));

    expect(queue.calls).toBe(1);
    expect(fetcher.calls).toBe(1);
    expect(result.totalCount).toBe(1);
  });

  it('does not enqueue a cache hit — only real upstream calls consume budget', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const queue = buildQueue();
    const svc = new InventoryService(fetcher, cache, queue);

    await svc.getInventory('76561198000000041');
    await svc.getInventory('76561198000000041');

    expect(queue.calls).toBe(1);
    expect(fetcher.calls).toBe(1);
  });

  it('enqueues refresh reads — a cache bypass still respects the rate limit', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const queue = buildQueue();
    const svc = new InventoryService(fetcher, cache, queue);

    await svc.getInventory('76561198000000042');
    await svc.getInventory('76561198000000042', { refresh: true });

    expect(queue.calls).toBe(2);
  });

  it('routes failing fetches through the queue too (budget is consumed either way)', async () => {
    const fetcher: InventoryFetcher = {
      async fetch() {
        throw new Error('HTTP 429 Too Many Requests');
      },
    };
    const queue = buildQueue();
    const svc = new InventoryService(fetcher, cache, queue);

    const result = await svc.getInventory('76561198000000043');

    expect(queue.calls).toBe(1);
    expect(result.visibility).toBe('UNAVAILABLE');
  });

  it('still works without a queue (the queue stays optional)', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const svc = new InventoryService(fetcher, cache);

    const result = expectPublic(await svc.getInventory('76561198000000044'));

    expect(result.totalCount).toBe(1);
  });
});

describe('InventoryService — asset_properties passthrough (T125 — T122 runbook §5)', () => {
  let cache: InMemoryInventoryCache;

  beforeEach(() => {
    cache = new InMemoryInventoryCache();
  });

  it('forwards asset_properties verbatim, keeping every value a string', async () => {
    const fetcher = buildFetcher([
      buildAk47({
        asset_properties: [
          { propertyid: 1, int_value: '744', name: 'Pattern Template' },
          { propertyid: 2, float_value: '0.0608838982880115509', name: 'Wear Rating' },
          { propertyid: 6, string_value: 'B0A0654AAF3FF0', name: 'Item Certificate' },
        ],
      }),
    ]);
    const svc = new InventoryService(fetcher, cache);

    const result = expectPublic(await svc.getInventory('76561198000000050'));

    // The wear float is 19 significant digits — a JS number would round it, and
    // the launch-gate reviewer compares this value across two inventories
    // (T122 runbook §7 B3). Strings are the only lossless carrier.
    expect(result.items[0].assetProperties).toEqual([
      { propertyId: 1, name: 'Pattern Template', intValue: '744' },
      { propertyId: 2, name: 'Wear Rating', floatValue: '0.0608838982880115509' },
      { propertyId: 6, name: 'Item Certificate', stringValue: 'B0A0654AAF3FF0' },
    ]);
  });

  it('omits the field entirely when Steam sent no properties', async () => {
    const fetcher = buildFetcher([buildAk47()]);
    const svc = new InventoryService(fetcher, cache);

    const result = expectPublic(await svc.getInventory('76561198000000051'));

    // T122 measured properties on 91 of 199 assets, so absence is ordinary —
    // emitting `assetProperties: []` on every item would change the 07 §6.1
    // shape for the majority for no gain.
    expect('assetProperties' in result.items[0]).toBe(false);
  });

  it('never surfaces market_tradable_restriction (T122-B8 trap)', async () => {
    const fetcher = buildFetcher([
      // The exact shape the owner capture recorded: FREELY TRADABLE, yet the
      // restriction field reads 7 (runbook §6.1).
      buildAk47({ tradable: true, market_tradable_restriction: 7 }),
    ]);
    const svc = new InventoryService(fetcher, cache);

    const result = expectPublic(await svc.getInventory('76561198000000052'));

    expect(result.items[0]).not.toHaveProperty('market_tradable_restriction');
    expect(result.items[0]).not.toHaveProperty('marketTradableRestriction');
    // The item IS tradable — which is the whole point: a consumer that read the
    // restriction field as a lock would have called this locked.
    expect(result.items[0].tradable).toBe(true);
  });
});

describe('InventoryService — short-read guard (T125 — 08 §2.3 pagination)', () => {
  let cache: InMemoryInventoryCache;

  beforeEach(() => {
    cache = new InMemoryInventoryCache();
  });

  it('reports UNAVAILABLE when fewer assets came back than Steam reports', async () => {
    // Two assets merged, but Steam says the inventory holds five: the
    // pagination loop stopped early.
    const fetcher = buildFetcher([buildAk47({ assetid: '1' }), buildAk47({ assetid: '2' })], 5);
    const svc = new InventoryService(fetcher, cache);

    const result = await svc.getInventory('76561198000000060');

    // NOT a PUBLIC read of two items. Downstream, "two items" is a positive
    // finding — 02 §9.2 counts class copies, so a truncated inventory reads as
    // "the buyer's count did not rise" and refunds a delivered transaction.
    expect(result.visibility).toBe('UNAVAILABLE');
    if (result.visibility !== 'UNAVAILABLE') throw new Error('unreachable');
    expect(result.error).toBeInstanceOf(InventoryShortReadError);
    expect(result.error.code).toBe('STEAM_UNAVAILABLE');
    expect(result.error.retryable).toBe(true);
    expect((result.error as InventoryShortReadError).received).toBe(2);
    expect((result.error as InventoryShortReadError).expected).toBe(5);
  });

  it('does not cache a short read (the next call retries instead of serving it)', async () => {
    let attempt = 0;
    const fetcher: InventoryFetcher = {
      async fetch() {
        attempt += 1;
        return attempt === 1
          ? { items: [buildAk47()] as never, totalInventoryCount: 4 }
          : { items: [buildAk47()] as never, totalInventoryCount: 1 };
      },
    };
    const svc = new InventoryService(fetcher, cache);

    const short = await svc.getInventory('76561198000000061');
    const retried = await svc.getInventory('76561198000000061');

    // A cached truncated inventory would answer the next 120 seconds of reads
    // as though it were complete — the guard would then only delay the wrong
    // answer rather than prevent it.
    expect(short.visibility).toBe('UNAVAILABLE');
    expect(expectPublic(retried).totalCount).toBe(1);
    expect(attempt).toBe(2);
  });

  it('accepts a read that matches or exceeds the reported total', async () => {
    const exact = new InventoryService(buildFetcher([buildAk47()], 1), cache);
    expect(expectPublic(await exact.getInventory('76561198000000062')).totalCount).toBe(1);

    // total_inventory_count covers the whole inventory while the merged list
    // excludes currency items, so only the `<` direction is an error.
    const excess = new InventoryService(buildFetcher([buildAk47(), buildAk47()], 1), cache);
    expect(expectPublic(await excess.getInventory('76561198000000063')).totalCount).toBe(2);
  });

  it('stays silent when no total was reported (the check disables, not fails)', async () => {
    const fetcher = buildFetcher([buildAk47()], null);
    const svc = new InventoryService(fetcher, cache);

    const result = await svc.getInventory('76561198000000064');

    expect(result.visibility).toBe('PUBLIC');
  });

  it('treats a public-but-empty inventory as complete, not short', async () => {
    const fetcher = buildFetcher([], 0);
    const svc = new InventoryService(fetcher, cache);

    const result = await svc.getInventory('76561198000000065');

    // 0 >= 0. An empty PUBLIC inventory stays EVIDENCE of absence (08 §2.3);
    // the guard must not convert it into "no information".
    expect(result.visibility).toBe('PUBLIC');
    expect(expectPublic(result).totalCount).toBe(0);
  });
});

describe('InMemoryInventoryCache TTL semantics', () => {
  it('returns null after the TTL window elapses', async () => {
    let now = 1_000_000;
    const cache = new InMemoryInventoryCache(120_000, () => now);

    await cache.set('76561198000000020', { items: [], totalCount: 0, tradeableCount: 0 });
    expect((await cache.get('76561198000000020'))?.totalCount).toBe(0);

    now += 120_001;
    expect(await cache.get('76561198000000020')).toBeNull();
  });

  it('delete removes the entry immediately', async () => {
    const cache = new InMemoryInventoryCache();
    await cache.set('76561198000000021', { items: [], totalCount: 0, tradeableCount: 0 });
    await cache.delete('76561198000000021');
    expect(await cache.get('76561198000000021')).toBeNull();
  });
});
