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
} from './InventoryService.js';
import { InMemoryInventoryCache, INVENTORY_CACHE_TTL_SECONDS } from '../cache/InventoryCache.js';

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

    const result = await svc.getInventory('76561198000000001');

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

    const result = await svc.getInventory('76561198000000002');

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

  it('throws InventoryPrivateError when steamcommunity reports a private profile', async () => {
    const fetcher: InventoryFetcher = {
      async fetch() {
        throw new Error('This profile is private.');
      },
    };
    const svc = new InventoryService(fetcher, cache);

    await expect(svc.getInventory('76561198000000005')).rejects.toBeInstanceOf(
      InventoryPrivateError,
    );
  });

  it('wraps any other fetch error in SteamUnavailableError', async () => {
    const fetcher: InventoryFetcher = {
      async fetch() {
        throw new Error('HTTP 503 Service Unavailable');
      },
    };
    const svc = new InventoryService(fetcher, cache);

    await expect(svc.getInventory('76561198000000006')).rejects.toBeInstanceOf(
      SteamUnavailableError,
    );
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

    await expect(svc.getInventory('76561198000000007')).rejects.toBeInstanceOf(
      SteamUnavailableError,
    );
    const result = await svc.getInventory('76561198000000007');

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

    const result = await svc.getInventory('76561198000000008');

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

    const result = await svc.getInventory('76561198000000009');

    // assetid/classid/instanceid land as strings regardless of source type;
    // numeric 0 round-trips to "0" rather than degrading to null.
    expect(result.items[0].instanceId).toBe('0');
    expect(result.items[0].type).toBeNull();
    expect(result.items[0].exterior).toBeNull();
  });

  it('returns an empty envelope for a public-but-empty inventory', async () => {
    const fetcher = buildFetcher([]);
    const svc = new InventoryService(fetcher, cache);

    const result = await svc.getInventory('76561198000000010');

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
