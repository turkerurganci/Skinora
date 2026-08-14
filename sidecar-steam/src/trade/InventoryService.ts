import SteamCommunity from 'steamcommunity';
import type CEconItem from 'steamcommunity/classes/CEconItem.js';
import { SidecarError } from '../errors/SidecarError.js';
import type { InventoryCache } from '../cache/InventoryCache.js';
import type { TaskQueue } from '../queue/RateLimitedQueue.js';
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
  /**
   * T125 — Steam's per-asset `asset_properties` (T122 runbook §5), forwarded
   * verbatim when Steam sent any. Omitted rather than emitted as `[]` so the
   * 07 §6.1 shape is unchanged for the majority of assets (T122 measured
   * properties on 91 of 199 — weapons carry them, collectibles do not).
   *
   * Audit material for the delivery launch gate only. It is NOT an input to
   * 02 §9.2 delivery verification, which decides on a class count delta.
   */
  assetProperties?: AssetProperty[];
}

/**
 * One `asset_properties` entry: `Pattern Template` · `Wear Rating` ·
 * `Item Certificate` · `Name Tag` · `Charm Template` (T122 runbook §5).
 * Steam types the value three ways and sends exactly one per entry.
 */
export interface AssetProperty {
  propertyId: number;
  name: string;
  intValue?: string;
  floatValue?: string;
  stringValue?: string;
}

/** Response payload — totals are derived once during merge to keep callers cheap. */
export interface InventoryResponse {
  items: InventoryItem[];
  totalCount: number;
  tradeableCount: number;
}

/**
 * 08 §2.3 — three-valued read outcome (v3.0).
 *
 * `Private` and `Unavailable` are NOT interchangeable and neither is
 * "inventory read, item absent": the first is a closed evidence path, the
 * second is absence of information, the third is evidence. Collapsing them
 * lets a Steam outage be read as "item never delivered" and refund a
 * transaction that was in fact settled.
 */
export type InventoryVisibility = 'PUBLIC' | 'PRIVATE' | 'UNAVAILABLE';

/**
 * Result of one inventory read. The visibility is a **value**, not control
 * flow — 08 §2.3 states the read "returns as one of three states", so callers
 * cannot accidentally swallow the distinction in a `catch`.
 *
 * The failure variants carry the original {@link SidecarError}, which already
 * encodes the wire code and 08 §2.7's retry polarity (`Private` → not
 * retryable, user action required; `Unavailable` → retryable).
 */
export type InventoryReadResult =
  | { visibility: 'PUBLIC'; inventory: InventoryResponse }
  | { visibility: 'PRIVATE'; error: InventoryPrivateError }
  | { visibility: 'UNAVAILABLE'; error: SteamUnavailableError };

/** Per-read options (08 §2.3 cache bypass). */
export interface InventoryReadOptions {
  /**
   * Skip the cache **read** (08 §2.3 `refresh`). Delivery verification and
   * seller readiness confirmation must set this: stale data harms in both
   * directions — not seeing a delivered item causes an unfair refund, still
   * seeing a sold item lets a buyer pay against a stale listing.
   *
   * The freshly fetched result is still written back to cache, so a bypassing
   * read leaves the next ordinary reader warm rather than colder.
   */
  refresh?: boolean;
}

/**
 * Outcome of the cache decision for one read. `bypass` is a deliberate skip
 * (`refresh`), NOT a miss — conflating the two hides delivery-verification
 * load behind what looks like a cache-tuning problem (08 §2.6).
 */
export type CacheOutcome = 'hit' | 'miss' | 'bypass';

/**
 * Observer for {@link CacheOutcome}. Wired to the Prometheus counter in
 * `index.ts`; injected rather than imported so this module carries no
 * module-load side effects (the prom-client registry is process-global).
 */
export type CacheOutcomeObserver = (outcome: CacheOutcome) => void;

/**
 * Read-only port over an authenticated/anonymous SteamCommunity instance. Lets
 * tests inject a stub without monkey-patching steamcommunity. The real
 * implementation is a thin adapter around `SteamCommunity.getUserInventoryContents`.
 */
export interface InventoryFetcher {
  fetch(steamId: string, language: string): Promise<InventoryFetchResult>;
}

/**
 * What one upstream fetch produced: the merged assets, plus Steam's own
 * `total_inventory_count` for the whole inventory.
 *
 * T125 — the count is carried so {@link InventoryService} can tell a COMPLETE
 * read from a SHORT one (08 §2.3 pagination). It is `null` when the fetcher
 * could not report one, which disables the check rather than failing it.
 */
export interface InventoryFetchResult {
  items: CEconItem[];
  totalInventoryCount: number | null;
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

  fetch(steamId: string, language: string): Promise<InventoryFetchResult> {
    return new Promise((resolve, reject) => {
      // @types/steamcommunity narrows Callback to `(err) => any`, but the real
      // library invokes `(err, inventory, currencies, totalCount)` (see
      // node_modules/steamcommunity/components/users.js line 628). Cast through
      // unknown so we can carry the inventory argument out.
      //
      // T125 — the fourth argument is Steam's `total_inventory_count`, which
      // the library forwards only from the LAST page of the pagination loop
      // (users.js: `callback(null, inventory, currency, body.total_inventory_count)`).
      // It is therefore the honest yardstick for "did we get the whole thing".
      const callback = (
        err: Error | null,
        inventory: CEconItem[] | undefined,
        _currency: unknown,
        totalInventoryCount: number | undefined,
      ): void => {
        if (err) {
          reject(err);
          return;
        }
        resolve({
          items: inventory ?? [],
          totalInventoryCount: typeof totalInventoryCount === 'number' ? totalInventoryCount : null,
        });
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
 * T125 — raised when a read came back with FEWER assets than Steam's own
 * `total_inventory_count` says the inventory holds, i.e. the pagination loop
 * stopped early.
 *
 * Why this is not a warning: a short read is indistinguishable, downstream,
 * from a genuinely smaller inventory. Delivery verification (02 §9.2) counts
 * copies of one item class, so a truncated read produces "the count did not
 * rise" — a *negative finding* — for a delivery that did happen, and the
 * buyer gets refunded for an item they received. Reporting UNAVAILABLE turns
 * a silent wrong answer into a retryable absence of information (08 §2.7).
 *
 * Only the `<` direction is checked. An excess is not treated as an error:
 * `total_inventory_count` counts the whole inventory while the merged list
 * excludes currency items, so the two are not required to be equal — only for
 * the list never to fall short.
 */
/**
 * 08 §2.3 — Steam Community envanter okuma.
 *
 * Flow per request:
 *   1. Cache lookup (Redis-backed, 120s TTL) — key `inventory:{steamId}`.
 *      Skipped entirely when `refresh` is set (08 §2.3 cache bypass).
 *   2. Cache miss / bypass → `steamcommunity.getUserInventoryContents`, dispatched
 *      through the **Community** rate-limited queue (08 §2.6 — separate from the
 *      Web API queue). Pagination + merge are handled by the library; `count`
 *      defaults to the library's 1000/page, under the 5000 Steam max.
 *   3. Map `CEconItem[]` to the normalized `InventoryItem[]` shape (08 §2.3
 *      `assets + descriptions` table) and write it back to cache.
 *
 * The read never throws for expected Steam conditions — it returns an
 * {@link InventoryReadResult} whose `visibility` distinguishes Public / Private
 * / Unavailable (08 §2.3). Only programming errors propagate.
 *
 * Invalidation: callers (backend on transaction create) hit the cache directly
 * via {@link invalidate}. Library-side fetches stay deterministic — no
 * event-driven invalidation here.
 */
export class InventoryService {
  private static readonly DEFAULT_LANGUAGE = 'english';
  /**
   * Error message emitted by steamcommunity when the profile/inventory is
   * private. The library offers no error code for this case — `users.js`
   * translates "HTTP 403 + null body" into exactly this string
   * (steamcommunity@3.50.0), so string matching is the only available signal.
   * Anything else is treated as Unavailable, which fails safe: an unrecognized
   * error never becomes evidence of absence.
   */
  private static readonly PRIVATE_INVENTORY_MARKER = 'This profile is private.';

  constructor(
    private readonly fetcher: InventoryFetcher,
    private readonly cache: InventoryCache,
    private readonly queue?: TaskQueue,
    private readonly onCacheOutcome: CacheOutcomeObserver = () => {},
    private readonly log: Logger = defaultLogger,
  ) {}

  /**
   * Resolve `steamId`'s CS2 inventory as a three-valued result (08 §2.3).
   *
   * Pass `{ refresh: true }` to bypass the cache read; the fresh result is
   * still cached for subsequent ordinary readers. A failed refresh leaves any
   * existing cache entry untouched — the caller asked for fresh data and is
   * told `UNAVAILABLE` rather than being silently downgraded to stale data.
   */
  async getInventory(
    steamId: string,
    options: InventoryReadOptions = {},
  ): Promise<InventoryReadResult> {
    const refresh = options.refresh === true;

    if (refresh) {
      this.onCacheOutcome('bypass');
      this.log.debug({ steamId }, 'Inventory cache bypassed (refresh)');
    } else {
      const cached = await this.cache.get(steamId);
      if (cached) {
        this.onCacheOutcome('hit');
        this.log.debug({ steamId, totalCount: cached.totalCount }, 'Inventory cache hit');
        return { visibility: 'PUBLIC', inventory: cached };
      }
      this.onCacheOutcome('miss');
    }

    let fetched: InventoryFetchResult;
    try {
      fetched = await this.fetch(steamId);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      if (message === InventoryService.PRIVATE_INVENTORY_MARKER) {
        this.log.info({ steamId }, 'Steam inventory is private');
        return { visibility: 'PRIVATE', error: new InventoryPrivateError(steamId) };
      }
      this.log.warn({ steamId, err: message }, 'Steam inventory fetch failed');
      return { visibility: 'UNAVAILABLE', error: new SteamUnavailableError(message) };
    }

    // T125 — completeness gate, BEFORE the result is cached or returned. A
    // truncated inventory must never be written to the cache either: it would
    // then answer the next 120 seconds of reads as if it were the whole thing.
    const shortRead = detectShortRead(fetched);
    if (shortRead) {
      this.log.warn(
        {
          steamId,
          received: shortRead.received,
          expected: shortRead.expected,
        },
        'Steam inventory read is short of total_inventory_count — reported UNAVAILABLE (08 §2.3)',
      );
      return { visibility: 'UNAVAILABLE', error: shortRead };
    }

    const inventory = buildInventoryResponse(fetched.items);
    await this.cache.set(steamId, inventory);
    this.log.info(
      {
        steamId,
        totalCount: inventory.totalCount,
        tradeableCount: inventory.tradeableCount,
        refresh,
      },
      'Inventory fetched and cached',
    );
    return { visibility: 'PUBLIC', inventory };
  }

  /**
   * Dispatch the upstream call through the Community queue when one is wired.
   * Cache hits deliberately never reach here — a cached read must not wait
   * behind the rate limiter (08 §2.6: the cache exists to reduce queue load).
   */
  private fetch(steamId: string): Promise<InventoryFetchResult> {
    const run = (): Promise<InventoryFetchResult> =>
      this.fetcher.fetch(steamId, InventoryService.DEFAULT_LANGUAGE);
    return this.queue ? this.queue.enqueue(run) : run();
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
 * T125 — raised when a read came back with FEWER assets than Steam's own
 * `total_inventory_count` says the inventory holds, i.e. the pagination loop
 * stopped early.
 *
 * A subclass of {@link SteamUnavailableError} rather than a sibling, because a
 * short read IS an unreadable inventory as far as every caller is concerned:
 * retryable, and never evidence of absence (08 §2.7).
 *
 * Why it cannot be a warning. A truncated read is indistinguishable, downstream,
 * from a genuinely smaller inventory. Delivery verification (02 §9.2) counts
 * copies of one item class, so a short read yields "the count did not rise" — a
 * *negative finding* — for a delivery that did happen, and the buyer is refunded
 * for an item they received.
 *
 * Only the `<` direction is checked. An excess is not an error:
 * `total_inventory_count` covers the whole inventory while the merged list
 * excludes currency items, so the two need not be equal — the list must simply
 * never fall short.
 */
export class InventoryShortReadError extends SteamUnavailableError {
  constructor(
    public readonly received: number,
    public readonly expected: number,
  ) {
    super(
      `Inventory read returned ${received} assets but Steam reports ${expected} — pagination incomplete`,
    );
    this.name = 'InventoryShortReadError';
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
  //
  // Deliberately NOT mapped: `market_tradable_restriction`. T122-B measured it
  // at 7 on an item whose `tradable` was 1 — the field carries the item CLASS's
  // policy ("items of this class are restricted for 7 days when acquired via
  // trade/market"), not this asset's current lock state, and reads identically
  // for a locked and a free copy (runbook §6.1). Forwarding it would invite a
  // consumer to treat every free item as locked. `tradable` is the only lock
  // signal Steam gives anonymously, and even that is class-level (runbook §6).
  const assetProperties = mapAssetProperties(raw);
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
    ...(assetProperties.length > 0 ? { assetProperties } : {}),
  };
}

/** Raw `asset_properties` entry as steamcommunity attaches it to a CEconItem. */
interface RawAssetProperty {
  propertyid?: number | string;
  name?: string;
  int_value?: number | string;
  float_value?: number | string;
  string_value?: string;
}

/**
 * Project Steam's `asset_properties` into the wire shape (T122 runbook §5).
 *
 * Values stay STRINGS: `Wear Rating` arrives as a 19-digit decimal
 * (`0.0608838982880115509`) that a JS number silently rounds, and
 * `Item Certificate` is a hex string. The launch-gate reviewer compares these
 * across two inventories, so a lossy round-trip would corrupt exactly the
 * comparison the capture exists for.
 */
function mapAssetProperties(raw: CEconItem): AssetProperty[] {
  const entries = (raw as { asset_properties?: unknown }).asset_properties;
  if (!Array.isArray(entries)) return [];

  const mapped: AssetProperty[] = [];
  for (const entry of entries as RawAssetProperty[]) {
    if (entry == null || typeof entry !== 'object') continue;
    const propertyId = Number(entry.propertyid);
    mapped.push({
      propertyId: Number.isFinite(propertyId) ? propertyId : 0,
      name: entry.name ?? '',
      ...(entry.int_value != null ? { intValue: String(entry.int_value) } : {}),
      ...(entry.float_value != null ? { floatValue: String(entry.float_value) } : {}),
      ...(entry.string_value != null ? { stringValue: String(entry.string_value) } : {}),
    });
  }
  return mapped;
}

/**
 * T125 — compare the merged asset list against Steam's `total_inventory_count`.
 * Returns an error when the list falls short, `null` when the read looks
 * complete or when no total was reported.
 */
function detectShortRead(fetched: InventoryFetchResult): InventoryShortReadError | null {
  const { items, totalInventoryCount } = fetched;
  if (totalInventoryCount == null) return null;
  if (items.length >= totalInventoryCount) return null;
  return new InventoryShortReadError(items.length, totalInventoryCount);
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
