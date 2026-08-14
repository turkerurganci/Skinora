import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, it, expect, vi } from 'vitest';

vi.mock('../logger.js', () => ({
  logger: {
    info: vi.fn(),
    warn: vi.fn(),
    error: vi.fn(),
    debug: vi.fn(),
    child: vi.fn().mockReturnThis(),
  },
}));

import SteamCommunity from 'steamcommunity';
import { SteamCommunityInventoryFetcher, buildInventoryResponse } from './InventoryService.js';

/**
 * T125 — contract tests over the LIVE `steamcommunity` pagination consumer.
 *
 * Every other inventory test injects an {@link InventoryFetcher} double, which
 * means the actual reader of Steam's `more_items` / `last_assetid` protocol —
 * `steamcommunity`'s own loop — is never exercised. These tests stub the
 * library's HTTP layer instead of replacing the library, so the assertions land
 * on the real termination rule.
 *
 * Why it matters (T122 §4.2, finding B9): Steam omits `more_items` entirely on
 * the last page rather than sending `more_items: 0`. The library terminates on
 * `if (body.more_items)` — a truthiness test, which handles absence correctly.
 * A consumer that required the key, or compared it to `0`, would either reject
 * the final page or loop forever. These tests fail loudly if a library upgrade
 * changes that rule.
 */

const STEAM_ID = '76561198000000001';
const CS2_APP_ID = 730;

/** One page of Steam's `/inventory/{id}/730/2` response. */
interface InventoryPage {
  success: number;
  assets: Array<Record<string, unknown>>;
  descriptions: Array<Record<string, unknown>>;
  total_inventory_count: number;
  more_items?: number;
  last_assetid?: string;
  asset_properties?: Array<Record<string, unknown>>;
}

interface CapturedRequest {
  uri: string;
  startAssetId: string | undefined;
}

/**
 * Build a SteamCommunity whose HTTP layer serves `pages` in order, recording
 * what each request asked for. `httpRequest` is the single seam every
 * component call funnels through (`components/http.js`).
 */
function communityServing(pages: InventoryPage[]): {
  community: SteamCommunity;
  requests: CapturedRequest[];
} {
  const requests: CapturedRequest[] = [];
  const community = new SteamCommunity();

  const stub = (
    options: { uri: string; qs?: { start_assetid?: string } },
    callback: (err: Error | null, response: unknown, body: unknown) => void,
  ): void => {
    const index = requests.length;
    requests.push({ uri: options.uri, startAssetId: options.qs?.start_assetid });
    const page = pages[index];
    if (!page) {
      callback(
        new Error(`unexpected request #${index + 1} — only ${pages.length} page(s) staged`),
        null,
        null,
      );
      return;
    }
    // Async, like the real transport: a synchronous callback would mask
    // re-entrancy problems in the library's recursive pagination.
    setImmediate(() => callback(null, { statusCode: 200 }, page));
  };

  (community as unknown as { httpRequest: unknown }).httpRequest = stub;
  return { community, requests };
}

function asset(assetid: string, classid = '310776959', instanceid = '188530139') {
  return { appid: CS2_APP_ID, contextid: '2', assetid, classid, instanceid, amount: '1' };
}

function description(classid = '310776959', instanceid = '188530139', overrides = {}) {
  return {
    appid: CS2_APP_ID,
    classid,
    instanceid,
    name: 'AK-47 | Redline',
    market_hash_name: 'AK-47 | Redline (Field-Tested)',
    type: 'Classified Rifle',
    tradable: 1,
    marketable: 1,
    icon_url: 'abc123',
    tags: [{ category: 'Exterior', localized_tag_name: 'Field-Tested' }],
    ...overrides,
  };
}

describe('steamcommunity pagination consumer (T125 — T122 §4.2 / B9)', () => {
  it('terminates when the last page OMITS more_items entirely', async () => {
    const { community, requests } = communityServing([
      {
        success: 1,
        assets: [asset('1'), asset('2')],
        descriptions: [description()],
        more_items: 1,
        last_assetid: '2',
        total_inventory_count: 3,
      },
      {
        // T122-B measured exactly this: the final page carries neither
        // `more_items` nor `last_assetid`. "No continuation" is the ABSENCE of
        // the key, not `more_items: 0`.
        success: 1,
        assets: [asset('3')],
        descriptions: [description()],
        total_inventory_count: 3,
      },
    ]);

    const result = await new SteamCommunityInventoryFetcher(community).fetch(STEAM_ID, 'english');

    expect(requests).toHaveLength(2);
    expect(result.items.map((i) => String(i.assetid))).toEqual(['1', '2', '3']);
    expect(result.totalInventoryCount).toBe(3);
  });

  it('follows last_assetid as the next page cursor', async () => {
    const { community, requests } = communityServing([
      {
        success: 1,
        assets: [asset('1')],
        descriptions: [description()],
        more_items: 1,
        last_assetid: '1',
        total_inventory_count: 2,
      },
      { success: 1, assets: [asset('2')], descriptions: [description()], total_inventory_count: 2 },
    ]);

    await new SteamCommunityInventoryFetcher(community).fetch(STEAM_ID, 'english');

    expect(requests[0].startAssetId).toBeUndefined();
    expect(requests[1].startAssetId).toBe('1');
  });

  it('terminates on a single page that omits more_items (one-page inventory)', async () => {
    // The shape of the committed T122-B owner capture: total_inventory_count 1,
    // no more_items, no last_assetid. Exactly one request must be issued.
    const { community, requests } = communityServing([
      { success: 1, assets: [asset('1')], descriptions: [description()], total_inventory_count: 1 },
    ]);

    const result = await new SteamCommunityInventoryFetcher(community).fetch(STEAM_ID, 'english');

    expect(requests).toHaveLength(1);
    expect(result.items).toHaveLength(1);
    expect(result.totalInventoryCount).toBe(1);
  });

  it('reports total_inventory_count from the LAST page, covering every page', async () => {
    const { community } = communityServing([
      {
        success: 1,
        assets: [asset('1')],
        descriptions: [description()],
        more_items: 1,
        last_assetid: '1',
        total_inventory_count: 2,
      },
      { success: 1, assets: [asset('2')], descriptions: [description()], total_inventory_count: 2 },
    ]);

    const result = await new SteamCommunityInventoryFetcher(community).fetch(STEAM_ID, 'english');

    // The short-read guard compares merged length against this number, so it
    // has to describe the whole inventory rather than the final page.
    expect(result.items).toHaveLength(2);
    expect(result.totalInventoryCount).toBe(2);
  });

  it('merges asset_properties onto the assets of each page', async () => {
    const { community } = communityServing([
      {
        success: 1,
        assets: [asset('1')],
        descriptions: [description()],
        asset_properties: [
          {
            appid: CS2_APP_ID,
            contextid: '2',
            assetid: '1',
            asset_properties: [
              { propertyid: 2, float_value: '0.138679757714271545', name: 'Wear Rating' },
            ],
          },
        ],
        total_inventory_count: 1,
      },
    ]);

    const result = await new SteamCommunityInventoryFetcher(community).fetch(STEAM_ID, 'english');
    const mapped = buildInventoryResponse(result.items);

    expect(mapped.items[0].assetProperties).toEqual([
      { propertyId: 2, name: 'Wear Rating', floatValue: '0.138679757714271545' },
    ]);
  });
});

describe('market_tradable_restriction trap (T125 — T122-B8, runbook §6.1)', () => {
  /**
   * The capture the project owner produced from their own Steam session,
   * committed alongside the T122 runbook. Read from the canonical location
   * rather than copied here: the assertion below is about what Steam ACTUALLY
   * returned, and a local copy could drift from the recorded measurement.
   */
  const capture = JSON.parse(
    readFileSync(
      resolve(__dirname, '../../../Docs/INTEGRATION_RUNBOOKS/data/T122_owner_capture.json'),
      'utf8',
    ),
  ) as {
    assets: Array<Record<string, unknown>>;
    descriptions: Array<Record<string, unknown>>;
    asset_properties?: Array<Record<string, unknown>>;
    total_inventory_count: number;
  };

  it('the measured capture pairs tradable:1 with market_tradable_restriction:7', () => {
    const [item] = capture.descriptions;

    // This single row is the whole finding: the item is FREELY TRADABLE right
    // now, and the restriction field still reads 7. The field carries the item
    // CLASS's policy ("restricted for 7 days when acquired via trade/market"),
    // never this asset's current lock state.
    expect(item.tradable).toBe(1);
    expect(item.market_tradable_restriction).toBe(7);
  });

  it('reading the restriction as a lock misclassifies a free item', async () => {
    const { community } = communityServing([
      {
        success: 1,
        assets: capture.assets,
        descriptions: capture.descriptions,
        ...(capture.asset_properties ? { asset_properties: capture.asset_properties } : {}),
        total_inventory_count: capture.total_inventory_count,
      },
    ]);

    const result = await new SteamCommunityInventoryFetcher(community).fetch(STEAM_ID, 'english');
    const raw = result.items[0] as unknown as { market_tradable_restriction: number };

    // What a consumer reading the restriction field WOULD conclude...
    const lockedPerRestrictionField = raw.market_tradable_restriction > 0;
    // ...versus the truth Steam states directly.
    const actuallyTradable = Boolean(result.items[0].tradable);

    expect(lockedPerRestrictionField).toBe(true);
    expect(actuallyTradable).toBe(true);
    // The two disagree. That disagreement is why T125's evidence engine may not
    // read this field — and why it does not read lock state at all: `tradable`
    // itself is class-level and carries no expiry anonymously (runbook §6),
    // so cooldown has no signature the platform can observe.
  });

  it('the mapped wire shape drops the restriction field', async () => {
    const { community } = communityServing([
      {
        success: 1,
        assets: capture.assets,
        descriptions: capture.descriptions,
        total_inventory_count: capture.total_inventory_count,
      },
    ]);

    const result = await new SteamCommunityInventoryFetcher(community).fetch(STEAM_ID, 'english');
    const mapped = buildInventoryResponse(result.items);

    expect(mapped.items[0]).not.toHaveProperty('market_tradable_restriction');
    expect(mapped.items[0]).not.toHaveProperty('marketTradableRestriction');
    expect(Object.keys(mapped.items[0])).not.toContain('market_marketable_restriction');
  });
});
