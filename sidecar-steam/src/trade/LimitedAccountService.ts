import { SidecarError } from '../errors/SidecarError.js';
import type { TaskQueue } from '../queue/RateLimitedQueue.js';
import type { LimitedAccountCache } from '../cache/LimitedAccountCache.js';
import { logger as defaultLogger, type Logger } from '../logger.js';

/**
 * 08 §2.2a — Steam "limited account" kontrolü (🔴 `Prova-LimitedAccountNeverChecked`).
 *
 * NEDEN AYRI BİR UÇ VAR: `GetTradeHoldDurations` *"takas ne kadar bekletilir"*
 * sorusuna cevap verir, *"bu hesap takas edebilir mi"* sorusuna değil. 5 USD
 * harcamamış (limited) bir hesap da `escrow_end_duration_seconds = 0` döndürür;
 * 2026-09-02 canlı provası tam olarak buna takıldı — alıcının parası zincirde
 * onaylandıktan SONRA item'ın teslim edilemeyeceği ortaya çıktı
 * (`Docs/TEST_REPORTS/REHEARSAL_2026-09-02.md`). Bu yüzden takas edebilirlik
 * tek bir boolean'dan ÇIKARILMAZ; üç bağımsız koşulun her biri kendi
 * kaynağından okunur:
 *
 *   1. limited değil      → bu servis (Steam Community profil XML'i)
 *   2. hesap 15 günlük    → backend, girişte çekilen `timecreated`
 *   3. escrow bekletmesi 0 → `TradeHoldService` (Steam Web API)
 *
 * ARIZA CEVAP GİBİ GÖRÜNÜYOR — bu ucun tasarımını belirleyen ölçüm
 * (2026-09-14, canlı): olmayan bir SteamID64 **HTTP 200 + HTML hata sayfası**
 * döndürüyor, bozuk bir id **HTTP 200 + `<error>` XML'i**. Yani `response.ok`
 * geçerlilik testi DEĞİLDİR ve alanın yokluğunu "limited değil" saymak kapıyı
 * doğduğu gün fail-open yapar. (`steamcommunity` paketinin kendi okuyucusu tam
 * olarak bunu yapıyor: `processItem('isLimitedAccount') == 1` → alan yoksa
 * `undefined == 1` → false. Bu yüzden kullanılmıyor.) Alan okunamadığında bu
 * servis FIRLATIR, kapı 503 ile kapanır.
 *
 * ASİMETRİK TEYİT — `0` teyit ister, `1` istemez. Profil XML'i arada bayat
 * cevap döndürüyor ve ölçülmüş yalan yönü **tek taraflı**: 2026-09-02'de iki
 * kez "limit kalktı" (`0`) dedi, ardışık örnekleme ikisini de yalanladı; ters
 * yönde (gerçekte temiz hesap için `1`) hiç gözlenmedi. Dolayısıyla `1` tek
 * okumada bağlayıcıdır, `0` ancak {@link LimitedAccountOptions.sampleCount}
 * ardışık örnek aynı sonucu verirse kabul edilir.
 *
 * ⚠️ ÖRNEK SAYISI, VAR OLAN TEK ÖLÇÜMÜN ALTINDA. `DEPLOY_RUNBOOK §G.5`
 * **5 ardışık okuma** diyor (ve 2 örnekli yordam iki kez yanlış alarm verdi);
 * buradaki varsayılan **3**. Bu bilinçli bir sapma: runbook'taki kural bir
 * insan prosedürüdür ve tek seferlik çalışır, buradaki ise her soğuk kullanıcı
 * için Steam Community kotasından (10/dk) örnek sayısı kadar görev harcar.
 * Değer yapılandırılabilir (`STEAM_LIMITED_ACCOUNT_SAMPLES`); kota ölçümü
 * yenilendiğinde 5'e çıkarılması için rebuild gerekmez.
 */
export interface LimitedAccountResult {
  /** True → hesap Steam tarafından kısıtlı, takas EDEMEZ. */
  limited: boolean;
  /** Bu cevap için Steam'e kaç kez gidildi (önbellekten gelen cevapta 0). */
  samples: number;
  /** Cevabın kaynağı — `cache` yalnızca doğrulanmış TEMİZ sonuç için görülebilir. */
  source: 'live' | 'cache';
}

/**
 * Enjekte edilebilir fetch yüzeyi. `TradeHoldService`'inkinden tek farkı
 * gövdenin `json()` değil `text()` ile okunması: cevap XML ve arıza hâlinde
 * HTML olabiliyor.
 */
export type TextFetchLike = (
  input: string,
  init?: { method?: string; headers?: Record<string, string> },
) => Promise<{ ok: boolean; status: number; text: () => Promise<string> }>;

const STEAM_PROFILE_XML_URL = 'https://steamcommunity.com/profiles';

/**
 * Alan tam olarak `<isLimitedAccount>0</isLimitedAccount>` biçiminde geliyor —
 * CDATA sarmalı ve öznitelik taşımıyor. Bu ölçülmüş veridir: canlı provanın
 * kendi kontrol komutu da aynı `grep` deseniydi (`DEPLOY_RUNBOOK §G.5`).
 */
const LIMITED_FLAG_REGEX = /<isLimitedAccount>\s*([01])\s*<\/isLimitedAccount>/;

/** Profil cevabı okunamadığında fırlatılır — kapı fail-closed 503 ile kapanır. */
export class SteamProfileUnreadableError extends SidecarError {
  constructor(
    message: string,
    /** Log/metrik etiketi: cevabın neye benzediği. Karara GİRMEZ, yalnız teşhis içindir. */
    public readonly bodyShape: 'html' | 'profile_error' | 'field_missing' | 'transport' | 'deadline',
  ) {
    super(message, 'STEAM_PROFILE_UNREADABLE', true);
    this.name = 'SteamProfileUnreadableError';
  }
}

export interface LimitedAccountOptions {
  /** `0` cevabını kabul etmek için gereken ardışık örnek sayısı. Varsayılan 3. */
  sampleCount?: number;
  /**
   * Örnekler arası bekleme. Kuyruktan MİRAS ALINMAZ: kuyruk boşken görevler
   * gecikmesiz koşar, yani aralık yazılmazsa üç örnek milisaniyeler içinde peş
   * peşe gider ve büyük olasılıkla AYNI bayat CDN düğümünü okur — örneklemenin
   * savuşturmak için var olduğu arızanın ta kendisi. Ölçülmüş yordamdaki
   * aralık 3 saniyedir.
   */
  sampleDelayMs?: number;
  /**
   * Tüm kontrolün üst sınırı. Backend'in sidecar istemcisi 30 saniyede kesiyor
   * (`SteamSidecarOptions`); kuyruk doluyken üçüncü örnek bunu aşabilir ve o
   * zaman arıza "kodlu 503" yerine "backend timeout" diye görünür. Kendi son
   * tarihimiz 30 saniyeden kısa olmalı.
   */
  deadlineMs?: number;
}

export interface LimitedAccountDeps extends LimitedAccountOptions {
  cache?: LimitedAccountCache;
  queue?: TaskQueue;
  fetchFn?: TextFetchLike;
  sleepFn?: (ms: number) => Promise<void>;
  now?: () => number;
  /** Sonuç sayacı — `index.ts` Prometheus'a bağlar (prom-client kaydı süreç-global). */
  onOutcome?: (outcome: 'limited' | 'clean' | 'cache_hit' | 'unreadable') => void;
  log?: Logger;
}

export const DEFAULT_LIMITED_ACCOUNT_SAMPLES = 3;
export const DEFAULT_LIMITED_ACCOUNT_SAMPLE_DELAY_MS = 3_000;
export const DEFAULT_LIMITED_ACCOUNT_DEADLINE_MS = 20_000;

export class LimitedAccountService {
  private readonly cache?: LimitedAccountCache;
  private readonly queue?: TaskQueue;
  private readonly fetchFn: TextFetchLike;
  private readonly sleepFn: (ms: number) => Promise<void>;
  private readonly now: () => number;
  private readonly onOutcome?: LimitedAccountDeps['onOutcome'];
  private readonly log: Logger;
  private readonly sampleCount: number;
  private readonly sampleDelayMs: number;
  private readonly deadlineMs: number;

  constructor(deps: LimitedAccountDeps = {}) {
    this.cache = deps.cache;
    this.queue = deps.queue;
    this.fetchFn = deps.fetchFn ?? (fetch as unknown as TextFetchLike);
    this.sleepFn = deps.sleepFn ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
    this.now = deps.now ?? (() => Date.now());
    this.onOutcome = deps.onOutcome;
    this.log = deps.log ?? defaultLogger;
    this.sampleCount = deps.sampleCount ?? DEFAULT_LIMITED_ACCOUNT_SAMPLES;
    this.sampleDelayMs = deps.sampleDelayMs ?? DEFAULT_LIMITED_ACCOUNT_SAMPLE_DELAY_MS;
    this.deadlineMs = deps.deadlineMs ?? DEFAULT_LIMITED_ACCOUNT_DEADLINE_MS;
  }

  /**
   * `steamId` hesabının Steam tarafından kısıtlanıp kısıtlanmadığını çözer.
   * Okunamayan her cevapta {@link SteamProfileUnreadableError} fırlatır —
   * çağıran fail-closed davranır (07 §5.16a ailesi).
   */
  async isLimited(steamId: string): Promise<LimitedAccountResult> {
    if (this.cache && (await this.cache.isKnownClean(steamId))) {
      this.onOutcome?.('cache_hit');
      return { limited: false, samples: 0, source: 'cache' };
    }

    const deadline = this.now() + this.deadlineMs;
    let samples = 0;

    for (let attempt = 0; attempt < this.sampleCount; attempt++) {
      if (this.now() >= deadline) {
        this.onOutcome?.('unreadable');
        throw new SteamProfileUnreadableError(
          `Limited-account check exceeded ${this.deadlineMs}ms after ${samples} sample(s)`,
          'deadline',
        );
      }

      let limited: boolean;
      try {
        // Her örnek kuyruğa AYRI görev olarak girer. Tek görevde döngmek
        // limitleyiciyi 3 yerine 1 saydırır ve Steam'in gerçek kotasını üç kat
        // hızlı tüketir — kuyruk isteği değil görevi sayıyor.
        limited = await this.dispatch(() => this.readLimitedFlag(steamId));
      } catch (err) {
        this.onOutcome?.('unreadable');
        throw err;
      }
      samples++;

      if (limited) {
        // Tek okuma yeter: ölçülmüş yalan yönü hep `0`. Ve bu sonuç
        // ÖNBELLEKLENMEZ — kısıtı kaldıran kullanıcı anında geçebilmeli.
        this.onOutcome?.('limited');
        return { limited: true, samples, source: 'live' };
      }

      if (attempt < this.sampleCount - 1) {
        await this.sleepFn(this.sampleDelayMs);
      }
    }

    await this.cache?.markClean(steamId);
    this.onOutcome?.('clean');
    return { limited: false, samples, source: 'live' };
  }

  /** Önbellekteki temiz kaydı siler — sonraki kontrol canlı okur. */
  async invalidate(steamId: string): Promise<void> {
    await this.cache?.forget(steamId);
  }

  private dispatch<T>(run: () => Promise<T>): Promise<T> {
    return this.queue ? this.queue.enqueue(run) : run();
  }

  private async readLimitedFlag(steamId: string): Promise<boolean> {
    const url = `${STEAM_PROFILE_XML_URL}/${steamId}?xml=1`;

    let response: Awaited<ReturnType<TextFetchLike>>;
    try {
      response = await this.fetchFn(url, { method: 'GET' });
    } catch (err) {
      throw new SteamProfileUnreadableError(
        `Steam profile XML transport failure: ${(err as Error).message}`,
        'transport',
      );
    }

    let body: string;
    try {
      body = await response.text();
    } catch (err) {
      throw new SteamProfileUnreadableError(
        `Steam profile XML body read failure: ${(err as Error).message}`,
        'transport',
      );
    }

    const match = LIMITED_FLAG_REGEX.exec(body);
    if (!match) {
      // `response.ok` burada bilerek karara girmiyor: ölçümde İKİ arıza şekli de
      // HTTP 200 ile döndü. Tek geçerlilik testi alanın kendisidir.
      const shape = classifyUnreadableBody(body);
      this.log.warn(
        { steamId, status: response.status, shape, bodyLength: body.length },
        'Steam profile XML did not carry isLimitedAccount',
      );
      throw new SteamProfileUnreadableError(
        `Steam profile XML did not carry isLimitedAccount (shape: ${shape})`,
        shape,
      );
    }

    return match[1] === '1';
  }
}

/**
 * Okunamayan gövdeyi teşhis için etiketler. Bu etiket KARARA GİRMEZ — üç şekil
 * de aynı fail-closed sonucu verir; ayrım yalnız logda "Steam bozuk mu, biz mi
 * yanlış id gönderdik" sorusunu cevaplayabilmek içindir.
 */
function classifyUnreadableBody(body: string): 'html' | 'profile_error' | 'field_missing' {
  if (body.includes('<error>')) return 'profile_error';
  const head = body.slice(0, 200).toLowerCase();
  if (head.includes('<!doctype html') || head.includes('<html')) return 'html';
  return 'field_missing';
}
