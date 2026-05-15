import SteamCommunity from 'steamcommunity';
import type CEconItem from 'steamcommunity/classes/CEconItem.js';
import { SidecarError } from '../errors/SidecarError.js';
import type { InventoryCache } from '../cache/InventoryCache.js';
import { logger as defaultLogger, type Logger } from '../logger.js';

/**
 * Item shape returned by the sidecar inventory endpoint (08 §2.3 merge result).
 * Matches the contract the backend reshapes into 07 §6.1 `GET /steam/inventory`.
 */
export interface InventoryItem {
  assetId: string;
  classId: string;
  instanceId: string | null;
  name: string;
  marketHashName: string;
  type: string | null;
  exterior: string | null;
  iconUrl: string | null;
  tradable: boolean;
  marketable: boolean;
}

/** Response payload — totals are derived once during merge to keep callers cheap. */
export interface InventoryResponse {
  items: InventoryItem[];
  totalCount: number;
  tradeableCount: number;
}

/**
 * Read-only port over an authenticated/anonymous SteamCommunity instance. Lets
 * tests inject a stub without monkey-patching steamcommunity. The real
 * implementation is a thin adapter around `SteamCommunity.getUserInventoryContents`.
 */
export interface InventoryFetcher {
  fetch(steamId: string, language: string): Promise<CEconItem[]>;
}

/**
 * Adapter for the live `steamcommunity` package — handles pagination internally
 * (08 §2.3 `start_assetid` / `more_items` loop runs inside the library) and
 * returns the merged `assets[] × descriptions[]` result as `CEconItem[]`.
 *
 * App ID 730 (CS2) + context 2 are hard-coded per task scope; if the platform
 * ever supports another game this becomes a parameter on the constructor.
 */
export class SteamCommunityInventoryFetcher implements InventoryFetcher {
  /** CS2 = appID 730, inventory context 2 (08 §2.3 path parameters). */
  private static readonly CS2_APP_ID = 730;
  private static readonly CS2_CONTEXT_ID = 2;

  constructor(private readonly community: SteamCommunity = new SteamCommunity()) {}

  fetch(steamId: string, language: string): Promise<CEconItem[]> {
    return new Promise((resolve, reject) => {
      // @types/steamcommunity narrows Callback to `(err) => any`, but the real
      // library invokes `(err, inventory, currencies, totalCount)` (see
      // node_modules/steamcommunity/components/users.js line 628). Cast through
      // unknown so we can carry the inventory argument out.
      const callback = (err: Error | null, inventory: CEconItem[] | undefined): void => {
        if (err) {
          reject(err);
          return;
        }
        resolve(inventory ?? []);
      };
      this.community.getUserInventoryContents(
        steamId,
        SteamCommunityInventoryFetcher.CS2_APP_ID,
        SteamCommunityInventoryFetcher.CS2_CONTEXT_ID,
        false,
        language,
        callback as unknown as (err: Error | null) => void,
      );
    });
  }
}

/**
 * 08 §2.3 — Steam Community envanter okuma.
 *
 * Flow per request:
 *   1. Cache lookup (Redis-backed, 120s TTL) — key `inventory:{steamId}`.
 *   2. Cache miss → `steamcommunity.getUserInventoryContents` (pagination + merge
 *      handled by the library; `count` defaults to library's 1000/page which is
 *      under the 5000 Steam max — adequate, no monkey-patching needed).
 *   3. Map `CEconItem[]` to the normalized `InventoryItem[]` shape (08 §2.3
 *      `assets + descriptions` table) and write it back to cache.
 *
 * Invalidation: callers (backend on transaction create, T68 on trade offer
 * terminal events) hit the cache directly via {@link invalidate}. Library-side
 * fetches stay deterministic — no event-driven invalidation here.
 */
export class InventoryService {
  private static readonly DEFAULT_LANGUAGE = 'english';
  /** Error message emitted by steamcommunity when the profile/inventory is private. */
  private static readonly PRIVATE_INVENTORY_MARKER = 'This profile is private.';

  constructor(
    private readonly fetcher: InventoryFetcher,
    private readonly cache: InventoryCache,
    private readonly log: Logger = defaultLogger,
  ) {}

  /**
   * Resolve `steamId`'s CS2 inventory. Throws {@link InventoryPrivateError} if
   * the profile/inventory is private and {@link SteamUnavailableError} for
   * upstream/transport failures.
   */
  async getInventory(steamId: string): Promise<InventoryResponse> {
    const cached = await this.cache.get(steamId);
    if (cached) {
      this.log.debug({ steamId, totalCount: cached.totalCount }, 'Inventory cache hit');
      return cached;
    }

    let rawItems: CEconItem[];
    try {
      rawItems = await this.fetcher.fetch(steamId, InventoryService.DEFAULT_LANGUAGE);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (message === InventoryService.PRIVATE_INVENTORY_MARKER) {
        this.log.info({ steamId }, 'Steam inventory is private');
        throw new InventoryPrivateError(steamId);
      }
      this.log.warn({ steamId, err: message }, 'Steam inventory fetch failed');
      throw new SteamUnavailableError(message);
    }

    const response = buildInventoryResponse(rawItems);
    await this.cache.set(steamId, response);
    this.log.info(
      { steamId, totalCount: response.totalCount, tradeableCount: response.tradeableCount },
      'Inventory fetched and cached',
    );
    return response;
  }

  /** Drop the cached inventory for `steamId`. Idempotent — safe to call when empty. */
  async invalidate(steamId: string): Promise<void> {
    await this.cache.delete(steamId);
    this.log.debug({ steamId }, 'Inventory cache invalidated');
  }
}

export class InventoryPrivateError extends SidecarError {
  constructor(public readonly steamId: string) {
    super(`Steam inventory for ${steamId} is private`, 'INVENTORY_PRIVATE', false);
    this.name = 'InventoryPrivateError';
  }
}

export class SteamUnavailableError extends SidecarError {
  constructor(message: string) {
    super(message, 'STEAM_UNAVAILABLE', true);
    this.name = 'SteamUnavailableError';
  }
}

/**
 * Map `CEconItem[]` to the normalized `InventoryItem[]` shape. Pure / exported
 * for unit testing — `descriptions` join is performed by the library, here we
 * only project the fields the backend exposes (07 §6.1) and 03 §2.2 step 8
 * needs.
 */
export function buildInventoryResponse(rawItems: CEconItem[]): InventoryResponse {
  const items = rawItems.map(mapItem);
  return {
    items,
    totalCount: items.length,
    tradeableCount: items.filter((it) => it.tradable).length,
  };
}

function mapItem(raw: CEconItem): InventoryItem {
  // CEconItem types assetid/classid/instanceid as `number | string` — Steam can
  // emit assetIDs larger than `Number.MAX_SAFE_INTEGER`, so we normalize to
  // string at the wire boundary to avoid precision loss in downstream consumers.
  return {
    assetId: String(raw.assetid ?? raw.id),
    classId: String(raw.classid),
    instanceId: raw.instanceid != null ? String(raw.instanceid) : null,
    name: raw.name,
    marketHashName: raw.market_hash_name,
    type: extractTag(raw, 'Type') ?? raw.type ?? null,
    exterior: extractTag(raw, 'Exterior'),
    iconUrl: typeof raw.getImageURL === 'function' ? raw.getImageURL() : null,
    tradable: Boolean(raw.tradable),
    marketable: Boolean(raw.marketable),
  };
}

interface TagEntry {
  category?: string;
  category_name?: string;
  internal_name?: string;
  localized_tag_name?: string;
  name?: string;
}

function extractTag(raw: CEconItem, category: string): string | null {
  const tags = Array.isArray(raw.tags) ? (raw.tags as TagEntry[]) : [];
  for (const tag of tags) {
    if (tag.category === category) {
      return tag.localized_tag_name ?? tag.name ?? tag.internal_name ?? null;
    }
  }
  return null;
}
