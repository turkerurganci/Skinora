# T07 — Rate Limiting Konfigürasyonu

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-06

---

## Yapılan İşler
- `IRateLimiterStore` abstraction: fixed-window check-and-increment kontratı, atomik garanti zorunluluğu
- `RedisRateLimiterStore`: StackExchange.Redis ile single round-trip Lua script (INCR + EXPIRE + PTTL); race koşulu yok
- `InMemoryRateLimiterStore`: ConcurrentDictionary tabanlı, per-key lock'lı, test ve local fallback için
- `RateLimitOptions` + `RateLimitPolicy`: appsettings.json'dan 7 policy bind ediliyor (Limit + WindowSeconds), KeyPrefix ve Enabled toggle
- `RateLimitAttribute`: opt-in policy işaretleme — controller veya action seviyesinde
- `RateLimitMiddleware`:
  - Endpoint metadata'dan `[RateLimit]` okur, policy yoksa pass-through
  - Partition key resolution: `user-*`/`admin-*`/`steam-inventory` → JWT `sub` claim, `auth`/`public` → client IP
  - Her limited response'a `X-RateLimit-Limit`, `X-RateLimit-Remaining`, `X-RateLimit-Reset` (Unix epoch) header'larını yazar
  - Aşıldığında 429 + `Retry-After` (saniye) + `RATE_LIMIT_EXCEEDED` envelope (07 §2.4 formatı)
  - Misconfigured policy adı → fail-fast `InvalidOperationException` (CI'da görünür)
- `RateLimitingServiceCollectionExtensions.AddRateLimiting`: `IConnectionMultiplexer` (singleton, lazy factory), `IRateLimiterStore` (RedisRateLimiterStore), `RateLimitOptions` registration
- Program.cs pipeline güncellemesi: middleware Authentication sonrası, Authorization öncesi
- appsettings.json: 7 policy konfigürasyonu (auth/user-read/user-write/steam-inventory/admin-read/admin-write/public) + Redis bağlantı placeholder'ı
- DiagnosticsController: 4 yeni `[RateLimit]` işaretli test endpoint'i (public/auth/user-read/steam-inventory)
- Integration testler: 11 test (header presence, decrement, 429, Retry-After, envelope, policy isolation, Enabled=false bypass)
- Test izolasyonu: per-test fresh `WebApplicationFactory`, `IRateLimiterStore` → `InMemoryRateLimiterStore` swap, `IConnectionMultiplexer` registration silinir → testler hiç Redis'e dokunmaz

## Etkilenen Modüller / Dosyalar
- `backend/src/Skinora.API/RateLimiting/RateLimitOptions.cs` (yeni)
- `backend/src/Skinora.API/RateLimiting/RateLimitPolicy.cs` (yeni — RateLimitOptions.cs içinde)
- `backend/src/Skinora.API/RateLimiting/RateLimitResult.cs` (yeni)
- `backend/src/Skinora.API/RateLimiting/IRateLimiterStore.cs` (yeni)
- `backend/src/Skinora.API/RateLimiting/RedisRateLimiterStore.cs` (yeni)
- `backend/src/Skinora.API/RateLimiting/InMemoryRateLimiterStore.cs` (yeni)
- `backend/src/Skinora.API/RateLimiting/RateLimitAttribute.cs` (yeni)
- `backend/src/Skinora.API/RateLimiting/RateLimitMiddleware.cs` (yeni)
- `backend/src/Skinora.API/RateLimiting/RateLimitingServiceCollectionExtensions.cs` (yeni)
- `backend/src/Skinora.API/Program.cs` (güncelleme — DI + pipeline)
- `backend/src/Skinora.API/appsettings.json` (güncelleme — Redis + RateLimit konfigürasyonu)
- `backend/src/Skinora.API/Skinora.API.csproj` (güncelleme — StackExchange.Redis 2.8.16)
- `backend/src/Skinora.API/Controllers/DiagnosticsController.cs` (güncelleme — 4 test endpoint)
- `backend/tests/Skinora.API.Tests/Integration/RateLimitTests.cs` (yeni — 11 test)

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Redis-based rate limiting konfigüre edildi | ✓ Karşılandı | `RedisRateLimiterStore` (atomic Lua: INCR+EXPIRE+PTTL), `IConnectionMultiplexer` singleton, `Redis:ConnectionString` config'den okunuyor. Production'da Redis kullanımı, testlerde InMemory swap. |
| 2 | Endpoint grupları: Auth 10/dk, GET 60/dk, POST/PUT/DELETE 20/dk, Steam inventory 5/dk, Admin okuma 120/dk, Admin yazma 30/dk, Public 30/dk | ✓ Karşılandı | `appsettings.json` `RateLimit:Policies` 7 grubu içerir, limitler 07 §2.9 ile birebir. `DifferentPolicies_AdvertiseTheirOwnLimits` testi 4 policy için header değerlerini doğrular. |
| 3 | 429 response + Retry-After header | ✓ Karşılandı | `BlockedResponse_IncludesRetryAfterHeader` testi: 429 + Retry-After 1-60 arası. Middleware `WriteRateLimitedResponseAsync` yazar. |
| 4 | X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset header'ları | ✓ Karşılandı | `LimitedEndpoint_ReturnsRateLimitHeaders` ve `RemainingHeader_DecreasesWithEachRequest` testleri 3 header'ı, doğru değerleri ve azalan remaining'i doğrular. |

## Doğrulama Kontrol Listesi
| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 07 §2.9'daki tüm endpoint grupları tanımlı mı? | ✓ | 7/7 grup `appsettings.json`'da, isimler: auth, user-read, user-write, steam-inventory, admin-read, admin-write, public |
| 2 | Header'lar doğru formatda mı? | ✓ | `X-RateLimit-Limit` int, `X-RateLimit-Remaining` int, `X-RateLimit-Reset` Unix epoch saniye, `Retry-After` saniye — 07 §2.9 örneğiyle eşleşiyor |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration (T07 yeni) | ✓ 11/11 passed | RateLimitTests sınıfı: header presence (3), 429 davranışı (4), policy isolation (2), disabled bypass (1), unmarked endpoint (1) |
| Mevcut testler (regresyon) | ✓ 37/37 passed | MiddlewarePipelineTests + EfCoreGlobalConfigTests + AuthenticationTests — kırılma yok |
| Toplam Skinora.API.Tests | ✓ 48/48 passed | `dotnet test tests/Skinora.API.Tests/Skinora.API.Tests.csproj` — 3 dk 8 sn |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |
| Yapım raporu uyumu | Tam uyumlu — uyuşmazlık yok |
| Doğrulayan testler | 48/48 passed (11 T07 + 37 regresyon) |
| Doküman uyumu | 07 §2.9 limit tablosu birebir, 07 §2.4 envelope formatı uyumlu, 05 §6.3 ile tutarlı |

### Güvenlik Kontrolü
- [x] Secret sızıntısı: Temiz (Redis bağlantı string'i `REPLACE_IN_ENV` placeholder, gerçek değer env var'dan)
- [x] Auth etkisi: Temiz (middleware authentication sonrası çalışır, JWT claim'i sadece partition key için okur)
- [x] Input validation: Temiz (kullanıcı girdisi alan yok)
- [x] Yeni bağımlılık: StackExchange.Redis 2.8.16 — Microsoft tarafından önerilen resmi Redis istemcisi, beklenen

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: `appsettings.json`'a `Redis:ConnectionString` ve `RateLimit` bölümleri eklendi. Docker compose `Redis__ConnectionString` env var'ını zaten geçiriyor (T02'den).
- Docker değişikliği: Yok (Redis container T02'de eklenmişti, halihazırda ayakta)

## Commit & PR
- Branch: `task/T07-rate-limiting`
- Commit: (yapılacak)
- PR: —
- CI: — (T11 öncesi manuel)

## Known Limitations / Follow-up
- **Default policy**: Şu an opt-in attribute modeli — controller'lar geldiğinde her endpoint explicit `[RateLimit("...")]` ile işaretlenmeli. F2+ task'larında kontroller yapılırken bu zorunlu olacak.
- **Forwarded headers**: Reverse proxy arkasında client IP doğru okunması için `UseForwardedHeaders` gerekiyor — T11 (CI/CD) veya T16 sırasında nginx/Cloudflare konfigürasyonuyla beraber etkinleştirilecek.
- **TestContainers Redis**: Tests halen InMemory store kullanıyor; T12'de gerçek Redis ile end-to-end Lua script doğrulaması eklenmeli.
- **Distributed clock skew**: Multi-instance dağıtık ortamda Redis tek otorite — tüm instance'lar aynı counter'ı paylaşır, sorun yok.
- **Rate-limit bypass for trusted clients (admin scripts)**: Şu an yok; sonraki fazlarda gerekirse policy override mekanizması eklenebilir.

## Notlar
- **Tasarım kararı: Custom middleware vs. 3rd party kütüphane**: cristipufu/RedisRateLimiting ve AspNetCoreRateLimit değerlendirildi. F0 altyapısı için custom yaklaşım seçildi: ~350 LOC, header/policy resolution üzerinde tam kontrol, başka bağımlılık yok. StackExchange.Redis zaten ileride distributed cache/lock için gerekli.
- **Atomik garanti**: Lua script `INCR + EXPIRE + PTTL` tek round-trip — TOCTOU yok. İlk hit'te EXPIRE set edilir, sonraki hit'lerde sıfırlanmaz (sliding değil fixed window).
- **Pipeline yeri**: Authentication sonrası, Authorization öncesi. User-scoped policy'ler için JWT claim'ine ihtiyaç var; Authorization öncesi olduğu için rate-limited isteklerde yetki kontrolü ziyan olmaz.
- **Misconfigured policy fail-fast**: Spec'te tanımlı olmayan policy adı kullanılırsa middleware `InvalidOperationException` atar. Bu CI'da hemen yakalanır, sessiz "limitsiz" davranışından daha güvenli.
- **Test izolasyonu**: Her test fresh `WebApplicationFactory` oluşturur (no `IClassFixture`). Counter state hiçbir test arasında sızmaz. `IConnectionMultiplexer` registration tamamen silinir → sadece in-memory store kullanılır.
