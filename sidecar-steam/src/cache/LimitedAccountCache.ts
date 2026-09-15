import type Redis from 'ioredis';
import { logger as defaultLogger, type Logger } from '../logger.js';

/**
 * 08 §2.2a — Steam "limited account" okumasının önbelleği.
 *
 * ÖNBELLEK YALNIZ **TEMİZ** SONUCU TUTAR — bu bir eksiklik değil, karar
 * (proje sahibi, 2026-09-14). Üç cevap türünün üçü de saklanabilirdi ve
 * ikisinin bedeli ölçülmüş:
 *
 *   - `limited = true` saklanırsa, 5 USD'yi harcayıp kısıtı **Steam tarafında
 *     anında** kaldıran kullanıcı bizim defterimizde TTL boyunca kısıtlı
 *     kalır. `DEPLOY_RUNBOOK §G.5`'in kendi kuralı bunun tersini söylüyor:
 *     "nihai testi ürün üzerinden yap".
 *   - Arıza (Steam cevap vermedi / cevap okunamadı) saklanırsa, dakikalık bir
 *     kesinti TTL boyunca herkesi bloke eder.
 *
 * Bu yüzden **bloke eden hiçbir karar önbellekten verilmez**: `limited` ve
 * arıza her seferinde canlı okunur. Saklanan tek şey, N ardışık örneğin
 * doğruladığı "bu hesap takas edebilir" cevabıdır.
 *
 * KABUL EDİLEN BEDEL: temiz okunduktan sonra TTL içinde kısıtlanan bir hesap
 * (ör. chargeback) bir kapıdan daha geçebilir. Bunun kaçış yolu
 * `DELETE /api/account-limited/:steamId/cache` — envanter önbelleğiyle aynı
 * kalıp (08 §2.3).
 */
export interface LimitedAccountCache {
  /** True yalnızca "bu hesabın temiz olduğu doğrulandı ve kaydı hâlâ taze" anlamına gelir. */
  isKnownClean(steamId: string): Promise<boolean>;
  /** N ardışık örnekle doğrulanmış temiz sonucu kaydeder. */
  markClean(steamId: string): Promise<void>;
  /** Kaydı siler — kısıtlanmış olabileceğinden şüphelenilen hesap için elle tazeleme yolu. */
  forget(steamId: string): Promise<void>;
}

/**
 * TTL — 24 saat. Çapraz test iddiaları için export edilir; test tarafında
 * YENİDEN BİLDİRİLMEZ, import edilir (bu depoda bir tur, testin kendi
 * kopyaladığı sabit bayatladığı için sessizce boşalmıştı).
 */
export const LIMITED_ACCOUNT_CACHE_TTL_SECONDS = 86_400;

/** Anahtar ön eki — envanter önbelleğiyle aynı `skinora:steam:` ad alanı. */
export const LIMITED_ACCOUNT_CACHE_KEY_PREFIX = 'skinora:steam:account-limited:';

export function limitedAccountCacheKey(steamId: string): string {
  return `${LIMITED_ACCOUNT_CACHE_KEY_PREFIX}${steamId}`;
}

/** Redis'e yazılan tek değer. Varlığı "temiz" demektir; içeriği yalnız insan okuru içindir. */
const CLEAN_MARKER = 'clean';

/**
 * Üretim / staging uygulaması. Envanter önbelleğiyle aynı sözleşme: `SETEX`
 * ile atomik yazma, tüm Redis hataları yutulur ve loglanır.
 *
 * Yutmanın yönü burada envanterdekinden FARKLI ve kasıtlı: Redis düştüğünde
 * `isKnownClean` **false** döner, yani kapı önbelleği atlayıp canlı okumaya
 * düşer. Bozulma yönü "daha pahalı ama doğru" tarafındadır; hiçbir arıza
 * kapıyı açık bırakmaz.
 */
export class RedisLimitedAccountCache implements LimitedAccountCache {
  constructor(
    private readonly redis: Redis,
    private readonly ttlSeconds: number = LIMITED_ACCOUNT_CACHE_TTL_SECONDS,
    private readonly log: Logger = defaultLogger,
  ) {}

  async isKnownClean(steamId: string): Promise<boolean> {
    try {
      return (await this.redis.get(limitedAccountCacheKey(steamId))) === CLEAN_MARKER;
    } catch (err) {
      this.log.warn({ steamId, err: (err as Error).message }, 'Limited-account cache GET failed');
      return false;
    }
  }

  async markClean(steamId: string): Promise<void> {
    try {
      await this.redis.setex(limitedAccountCacheKey(steamId), this.ttlSeconds, CLEAN_MARKER);
    } catch (err) {
      this.log.warn({ steamId, err: (err as Error).message }, 'Limited-account cache SET failed');
    }
  }

  async forget(steamId: string): Promise<void> {
    try {
      await this.redis.del(limitedAccountCacheKey(steamId));
    } catch (err) {
      this.log.warn({ steamId, err: (err as Error).message }, 'Limited-account cache DEL failed');
    }
  }
}

/**
 * Redis olmayan kurulumlar için bellek-içi uygulama.
 *
 * ⚠️ 24 SAATLİK TTL BURADA "KONTEYNER YENİDEN BAŞLAYANA KADAR" DEMEKTİR.
 * `REDIS_URL` boş bırakılan her kurulumda (docker-compose varsayılanı boş
 * geçiyor) süre bir `Map`'te yaşar ve her deploy'da sıfırlanır. 2 dakikalık
 * envanter TTL'i için bu önemsizdi; 24 saat için ölçüm iddiasını sessizce
 * çürütür — kesinti sonrası ilk kapı dalgası Steam Community kotasını yeniden
 * harcar. Üretimde `REDIS_URL` doldurulmalıdır (`DEPLOY_RUNBOOK §D`).
 */
export class InMemoryLimitedAccountCache implements LimitedAccountCache {
  private readonly store = new Map<string, number>();

  constructor(
    private readonly ttlMs: number = LIMITED_ACCOUNT_CACHE_TTL_SECONDS * 1000,
    private readonly now: () => number = () => Date.now(),
  ) {}

  async isKnownClean(steamId: string): Promise<boolean> {
    const expiresAt = this.store.get(steamId);
    if (expiresAt === undefined) return false;
    if (expiresAt <= this.now()) {
      this.store.delete(steamId);
      return false;
    }
    return true;
  }

  async markClean(steamId: string): Promise<void> {
    this.store.set(steamId, this.now() + this.ttlMs);
  }

  async forget(steamId: string): Promise<void> {
    this.store.delete(steamId);
  }

  /** Test yardımcısı — üretim yolları çağırmaz. */
  size(): number {
    return this.store.size;
  }
}
