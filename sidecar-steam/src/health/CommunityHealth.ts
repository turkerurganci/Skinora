/**
 * Steam Community erişilebilirliğinin PASİF sağlık göstergesi (08 §2.2a).
 *
 * NEDEN PASİF: `/health`'in Web API probu bilerek `api.steampowered.com`'a
 * bakıyor ve community host'una hiç dokunmuyor — çünkü o host dakikada ~10
 * istekle sınırlı ve teslimat doğrulaması o bütçeden yaşıyor; 5 saniyede bir
 * koşan bir liveness probu tek başına bütçenin tamamını yerdi.
 *
 * Ama iki host BAĞIMSIZ arızalanıyor ve bunun bir bedeli var: community
 * tarafında bir kesinti, Web API ayakta olduğu için `/health`'e hiç
 * yansımıyordu. `PlatformHealthProbeJob` yalnız `/health`'e bakıyor, dolayısıyla
 * böyle bir kesintide timeout'lar DONMUYOR — oysa 08 §2.2a kapısı community'ye
 * bağlı olduğu için tam o sırada her işlem fail-closed reddediliyor. Yani
 * kullanıcı ilerleyemezken süresi işlemeye devam ederdi.
 *
 * Bu sınıf açığı ek istek harcamadan kapatır: sidecar community'ye ZATEN
 * gidiyor, bu yalnız o çağrıların sonucunu sayar. Kesintiyi ölçen trafik,
 * kesintiden etkilenen trafiğin kendisidir.
 */
export interface CommunityHealthSnapshot {
  consecutiveFailures: number;
  lastFailureAt: number | null;
}

/**
 * Üst üste bu kadar başarısızlık görülürse host arızalı sayılır. Tek bir hata
 * (bir 429, bir kopan bağlantı) her zaman olur ve tek başına kesinti değildir.
 */
export const COMMUNITY_UNHEALTHY_AFTER_FAILURES = 3;

/**
 * Arıza bu süre boyunca bildirilir. Sonrasında sessizlik "iyileşti" değil
 * "bilmiyoruz" sayılır ve sağlıklıya dönülür — kalıcı olarak sağlıksız kalan
 * bir sidecar her Steam-bağlı timeout'u sonsuza kadar dondurur; T133'ün bot
 * oturumu kontrolünü silerken kaldırdığı arıza tam olarak budur ve başka bir
 * kapıdan geri gelmemeli.
 */
export const COMMUNITY_FAILURE_WINDOW_MS = 5 * 60_000;

export class CommunityHealthTracker {
  private consecutiveFailures = 0;
  private lastFailureAt: number | null = null;

  constructor(private readonly now: () => number = () => Date.now()) {}

  recordSuccess(): void {
    this.consecutiveFailures = 0;
    this.lastFailureAt = null;
  }

  recordFailure(): void {
    this.consecutiveFailures++;
    this.lastFailureAt = this.now();
  }

  snapshot(): CommunityHealthSnapshot {
    return { consecutiveFailures: this.consecutiveFailures, lastFailureAt: this.lastFailureAt };
  }

  /**
   * Hiç çağrı yapılmamışsa ya da son arıza penceresi geçmişse SAĞLIKLI döner.
   * "Kimse sormuyor" ile "herkes hata alıyor" ayrımı bu yüzden önemli: sessiz
   * bir sidecar arızalı değildir.
   */
  isUnhealthy(): boolean {
    if (this.consecutiveFailures < COMMUNITY_UNHEALTHY_AFTER_FAILURES) return false;
    if (this.lastFailureAt === null) return false;
    return this.now() - this.lastFailureAt < COMMUNITY_FAILURE_WINDOW_MS;
  }
}
