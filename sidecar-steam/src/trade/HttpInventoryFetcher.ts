import type CEconItem from 'steamcommunity/classes/CEconItem.js';
import { logger as defaultLogger, type Logger } from '../logger.js';
import type { InventoryFetcher, InventoryFetchResult } from './InventoryService.js';

/**
 * 08 §2.3 — Steam Community envanter okuma, doğrudan HTTP ile.
 *
 * NEDEN VAR (`UITour-InventoryClientRefusedBySteam` 🔴): `steamcommunity`
 * paketinin `getUserInventoryContents` çağrısı Steam'den **her denemede 429**
 * alıyordu; **aynı konteynerden, aynı dakikada**, birebir aynı URL + `Referer`
 * + `count=1000` ile düz `fetch` **her denemede 200** dönüyordu. Ölçümle elenen
 * hipotezler: User-Agent (paketin gönderdiği Chrome UA ile de 200) · URL biçimi
 * (`start_assetid` boş, sondaki eğik çizgi — üçü de 200) · profil gizliliği ·
 * `STEAM_API_KEY` (bu yol anahtar kullanmıyor). Geriye paketin taşıdığı eski
 * `request` HTTP yığını kaldı; bu adaptör onu devreden çıkarır.
 *
 * Kütüphanenin gözlenebilir davranışı **birebir** korunur, çünkü
 * `InventoryService` ona göre yazılmış:
 *   - sayfalama `more_items` / `last_assetid` üzerinden (08 §2.3),
 *   - `assets[] × descriptions[]` birleştirmesi `classid_instanceid` anahtarıyla,
 *   - `asset_properties` assetid'ye göre eşlenir,
 *   - `total_inventory_count` **son sayfadan** taşınır (T125 kısa-okuma kapısı
 *     bunu ölçüyor),
 *   - gizli envanter tam olarak `'This profile is private.'` mesajıyla fırlatılır
 *     — `InventoryService.PRIVATE_INVENTORY_MARKER` bu dizgeye eşitlik arıyor,
 *   - diğer HTTP hataları `'HTTP error {status}'` biçiminde fırlatılır, böylece
 *     loglar önceki uygulamayla karşılaştırılabilir kalır.
 *
 * `getImageURL()` bilerek eklenir: `InventoryService.mapItem` bunu bir **metot**
 * olarak çağırıyor (`typeof raw.getImageURL === 'function' ? … : null`). Düz
 * JSON döndürseydik her item'ın görseli sessizce `null` olurdu — kütüphaneyi
 * değiştirirken en kolay kaçırılacak regresyon buydu.
 */

/** CS2 = appID 730, envanter context 2 (08 §2.3 yol parametreleri). */
const CS2_APP_ID = 730;
const CS2_CONTEXT_ID = 2;

/** Steam'in sayfa başına üst sınırı 5000; kütüphanenin kullandığı değer 1000. */
const PAGE_SIZE = 1000;

/**
 * Sonsuz döngü emniyeti. 1000'lik sayfalarla 50 sayfa = 50.000 item; bilinen en
 * büyük CS2 envanterlerinin çok üstünde. Aşılırsa bu bir Steam davranış
 * değişikliğidir ve sessizce yarım veri döndürmektense hata vermek doğrudur.
 */
const MAX_PAGES = 50;

/** 429 ve 5xx için sınırlı yeniden deneme (08 §2.6 — kuyruk bütçesini yakmadan). */
const RETRY_STATUSES = new Set([429, 500, 502, 503, 504]);
const MAX_ATTEMPTS = 3;
const BASE_BACKOFF_MS = 1_000;

export const PRIVATE_INVENTORY_MESSAGE = 'This profile is private.';

interface SteamAsset {
  assetid?: string;
  id?: string;
  classid?: string;
  instanceid?: string;
  amount?: string;
  contextid?: string;
  currencyid?: string;
}

interface SteamInventoryResponse {
  success?: number | boolean;
  assets?: SteamAsset[];
  descriptions?: Record<string, unknown>[];
  asset_properties?: { assetid?: string; asset_properties?: unknown }[];
  total_inventory_count?: number;
  more_items?: number | boolean;
  last_assetid?: string;
  error?: string;
  Error?: string;
}

export interface HttpInventoryFetcherOptions {
  /** Test edilebilirlik için enjekte edilebilir fetch yüzeyi. */
  fetchImpl?: typeof fetch;
  /** Yeniden denemeler arası bekleme — testler 0 geçer. */
  sleep?: (ms: number) => Promise<void>;
  log?: Logger;
}

const defaultSleep = (ms: number): Promise<void> => new Promise((r) => setTimeout(r, ms));

export class HttpInventoryFetcher implements InventoryFetcher {
  private readonly fetchImpl: typeof fetch;
  private readonly sleep: (ms: number) => Promise<void>;
  private readonly log: Logger;

  constructor(options: HttpInventoryFetcherOptions = {}) {
    this.fetchImpl = options.fetchImpl ?? fetch;
    this.sleep = options.sleep ?? defaultSleep;
    this.log = options.log ?? defaultLogger;
  }

  async fetch(steamId: string, language: string): Promise<InventoryFetchResult> {
    const items: CEconItem[] = [];
    let totalInventoryCount: number | null = null;
    let startAssetId: string | undefined;

    for (let page = 0; page < MAX_PAGES; page++) {
      const body = await this.fetchPage(steamId, language, startAssetId);

      // Boş envanter — kütüphanenin ilk özel durumu.
      if (body.success && body.total_inventory_count === 0) {
        return { items: [], totalInventoryCount: 0 };
      }

      // CS2'ye özel: görünür item yokken `assets` hiç gelmez.
      if (body.success && !body.assets) {
        return { items, totalInventoryCount: body.total_inventory_count ?? totalInventoryCount };
      }

      if (!body.success || !body.assets || !body.descriptions) {
        throw new Error(body.error || body.Error || 'Malformed response');
      }

      const descriptions = indexDescriptions(body.descriptions);
      const assetProperties = indexAssetProperties(body.asset_properties);

      for (const asset of body.assets) {
        // Para birimi girdileri envanter listesine girmez (kütüphane bunları
        // ayrı `currency` dizisine koyuyor ve çağıran taraf kullanmıyor).
        if (asset.currencyid != null) continue;
        const instanceId = asset.instanceid || '0';
        const description = descriptions.get(`${asset.classid}_${instanceId}`);
        items.push(
          buildItem(asset, description, assetProperties.get(asset.assetid ?? asset.id ?? '')),
        );
      }

      // `total_inventory_count` son sayfadan taşınır — T125 kısa-okuma kapısının
      // ölçtüğü yardımcı ölçü budur.
      totalInventoryCount = body.total_inventory_count ?? totalInventoryCount;

      if (!body.more_items) return { items, totalInventoryCount };
      startAssetId = body.last_assetid;
      if (!startAssetId) return { items, totalInventoryCount };
    }

    throw new Error(`Inventory pagination exceeded ${MAX_PAGES} pages`);
  }

  private async fetchPage(
    steamId: string,
    language: string,
    startAssetId: string | undefined,
  ): Promise<SteamInventoryResponse> {
    const url = new URL(
      `https://steamcommunity.com/inventory/${steamId}/${CS2_APP_ID}/${CS2_CONTEXT_ID}`,
    );
    url.searchParams.set('l', language);
    url.searchParams.set('count', String(PAGE_SIZE));
    if (startAssetId) url.searchParams.set('start_assetid', startAssetId);

    let lastStatus = 0;
    for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
      const response = await this.fetchImpl(url.toString(), {
        headers: {
          Referer: `https://steamcommunity.com/profiles/${steamId}/inventory`,
          Accept: 'application/json, text/plain, */*',
        },
      });

      if (response.ok) return (await response.json()) as SteamInventoryResponse;

      lastStatus = response.status;

      // 403 + gövde `null` = "envanter gizli". Steam bunun için ayrı bir kod
      // vermiyor; gövdeyi okumak tek ayırt edici sinyal (kütüphane de aynısını
      // yapıyordu). Yeniden denenmez — durum kalıcıdır.
      if (response.status === 403) {
        const text = (await response.text()).trim();
        if (text === 'null' || text === '') throw new Error(PRIVATE_INVENTORY_MESSAGE);
        throw new Error(`HTTP error ${response.status}`);
      }

      if (!RETRY_STATUSES.has(response.status) || attempt === MAX_ATTEMPTS) break;

      const waitMs = BASE_BACKOFF_MS * 2 ** (attempt - 1);
      this.log.warn(
        { steamId, status: response.status, attempt, waitMs },
        'Steam inventory request rejected — retrying',
      );
      await this.sleep(waitMs);
    }

    throw new Error(`HTTP error ${lastStatus}`);
  }
}

/** `classid_instanceid` → description (kütüphanenin `getDescription` araması). */
function indexDescriptions(
  descriptions: Record<string, unknown>[],
): Map<string, Record<string, unknown>> {
  const map = new Map<string, Record<string, unknown>>();
  for (const d of descriptions) {
    const classId = String((d as { classid?: unknown }).classid ?? '');
    const instanceId = String((d as { instanceid?: unknown }).instanceid ?? '0') || '0';
    map.set(`${classId}_${instanceId}`, d);
  }
  return map;
}

function indexAssetProperties(
  entries: { assetid?: string; asset_properties?: unknown }[] | undefined,
): Map<string, unknown> {
  const map = new Map<string, unknown>();
  for (const entry of entries ?? []) {
    if (entry?.assetid != null) map.set(entry.assetid, entry.asset_properties);
  }
  return map;
}

/**
 * `CEconItem` ile aynı gözlenebilir şekli üretir: asset alanları, üstüne
 * ÇAKIŞMAYAN description alanları, normalize edilmiş id/instanceid ve boolean
 * bayraklar — artı `getImageURL()` metodu.
 */
function buildItem(
  asset: SteamAsset,
  description: Record<string, unknown> | undefined,
  assetProperties: unknown,
): CEconItem {
  const item: Record<string, unknown> = { ...asset };

  const id = asset.id ?? asset.assetid;
  item.assetid = id;
  item.id = id;
  item.instanceid = asset.instanceid || '0';
  item.contextid = asset.contextid ?? String(CS2_CONTEXT_ID);

  if (description) {
    for (const [key, value] of Object.entries(description)) {
      // Kütüphanenin kuralı: asset'te ZATEN olan alan description'dan
      // ezilmez (`!this.hasOwnProperty(thing)`).
      if (!(key in item)) item[key] = value;
    }
  }

  item.tradable = Boolean(item.tradable);
  item.marketable = Boolean(item.marketable);
  if (assetProperties != null) item.asset_properties = assetProperties;

  const iconUrl = item.icon_url;
  item.getImageURL = (): string | null =>
    typeof iconUrl === 'string' && iconUrl.length > 0
      ? `https://steamcommunity-a.akamaihd.net/economy/image/${iconUrl}/`
      : null;

  return item as unknown as CEconItem;
}
