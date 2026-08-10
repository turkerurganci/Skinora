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

function buildFetcher(items: FakeCEconItem[] = []): InventoryFetcher & { calls: number } {
  const stub: InventoryFetcher & { calls: number } = {
    calls: 0,
    async fetch() {
      stub.calls += 1;
      return items as never;
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
        return [buildAk47()] as never;
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
        if (attempts === 1) return [buildAk47()] as never;
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
