# T67 — Steam Sidecar Envanter Okuma

**Faz:** F4 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama beklemede) | **Tarih:** 2026-05-14

---

## Yapılan İşler

### Sidecar (Node.js / Vitest)

- `sidecar-steam/src/trade/InventoryService.ts` — stub → gerçek implementasyon.
  - `SteamCommunityInventoryFetcher` adapter `steamcommunity.getUserInventoryContents(userID, 730, 2, false, 'english', cb)` çağrısı yapar; pagination (`start_assetid`/`more_items` döngüsü) ve `assets[] × descriptions[]` merge işlemleri kütüphane içinde (her sayfa 1000 item, max 5000'in altında, 08 §2.3 contract'ına uygun).
  - `InventoryService.getInventory(steamId)` → cache lookup → fetch → `buildInventoryResponse` ile `CEconItem[]` üzerinden normalize edilmiş `{items, totalCount, tradeableCount}` envelope üretir; sonucu cache'e yazar.
  - `mapItem`: assetid/classid/instanceid → string'e normalize (Steam asset ID'leri `Number.MAX_SAFE_INTEGER`'ı geçebilir), type → `tags[category=="Type"]` fallback `CEconItem.type`, exterior → `tags[category=="Exterior"]`, iconUrl → `getImageURL()`.
  - Private envanter algılaması: kütüphane `Error('This profile is private.')` fırlatınca `InventoryPrivateError` (`INVENTORY_PRIVATE`, retryable=false). Diğer hatalar → `SteamUnavailableError` (`STEAM_UNAVAILABLE`, retryable=true).
  - `invalidate(steamId)` cache'i siler; sidecar restart → next call yeniden fetch.
- `sidecar-steam/src/cache/InventoryCache.ts` — yeni port + iki implementasyon.
  - `RedisInventoryCache` (`ioredis` `SETEX`/`GET`/`DEL`, 120s TTL, `skinora:steam:inventory:{steamId}` key). Tüm Redis hataları log + swallow — cache optimizasyon, correctness boundary değil.
  - `InMemoryInventoryCache` (test/dev fallback, TTL semantiği aynen Redis ile eşleşir).
- `sidecar-steam/src/api/routes.ts` — 2 yeni endpoint:
  - `GET /api/inventory/:steamId` → InventoryService.getInventory. Steam ID regex (`/^7656119[0-9]{10}$/`) ön doğrulama; 200 envelope / 422 INVENTORY_PRIVATE / 503 STEAM_UNAVAILABLE / 400 invalid id / 503 service not ready.
  - `DELETE /api/inventory/:steamId/cache` → InventoryService.invalidate, 204 No Content (idempotent).
- `sidecar-steam/src/index.ts` — `REDIS_URL` dolu ise `RedisInventoryCache`, boş ise `InMemoryInventoryCache`; `InventoryService` instantiate edilip router'a inject ediliyor.
- `sidecar-steam/src/config/index.ts` — yeni `redisUrl` config (`process.env.REDIS_URL`).
- `sidecar-steam/src/logger.ts` — `Logger` type alias export (`pino.Logger`) — DI parametrelerinde kullanılıyor.
- `sidecar-steam/package.json` — `ioredis@^5.4.0` eklendi (lockfile `5.10.1`'e resolve).
- 26 yeni Vitest testi: `InventoryService.test.ts` 13 (mapping, totals, cache hit, invalidate, private, unavailable, no-cache-on-failure, type fallback, instanceid=0, empty inventory, pure builder, TTL expiry, immediate delete) + `routes.test.ts` `+13` (GET 200/422/503/400/503-not-init, DELETE 204/400, içeren T67 alt-suite'ler). Toplam sidecar test sayısı 103 → 123 (sidecar testleri 103/103 → 123/123 PASS).

### Backend (.NET / xUnit)

- `Skinora.Steam/Application/Inventory/` (yeni klasör):
  - `SteamSidecarOptions.cs` — `SteamSidecar` config binding (BaseUrl, InternalKey, TimeoutSeconds=30).
  - `SteamInventoryDtos.cs` — `SteamInventoryItemDto` (07 §6.1 alanları + ClassId/InstanceId/MarketHashName/Marketable internal kullanım) ve `SteamInventoryDto` (items, totalCount, tradeableCount).
  - `ISteamSidecarInventoryClient.cs` + `SteamSidecarInventoryResult` + `SteamSidecarStatus` enum (`Success`/`InventoryPrivate`/`Unavailable`) — HTTP port + discriminated outcome.
  - `HttpSteamSidecarInventoryClient.cs` — `HttpClient`-backed; aynı sınıf hem `ISteamSidecarInventoryClient` hem `ISteamInventoryCacheInvalidator` arayüzlerini sağlar (single transport, single auth header). `X-Internal-Key` header (05 §3.4 servis-arası auth), `Accept: application/json`, `JsonSerializerDefaults.Web`, 422→`InventoryPrivate`, 5xx/transport→`Unavailable`. Internal JSON DTO'lar `JsonPropertyName` ile sabit, sidecar camelCase çıktısıyla 1:1.
  - `ISteamInventoryQueryService.cs` + `SteamInventoryQueryService.cs` — application service backing S1 endpoint, sidecar status'unu controller-friendly `GetInventoryStatus`'a (Success/InventoryPrivate/SteamUnavailable) maps eder.
  - `SidecarSteamInventoryReader.cs` — `ISteamInventoryReader` impl, `TryGetItemAsync` envelope içinde `assetId` ordinal eşleşmesi bulamayınca null; private + unavailable durumlarda da null (caller `STEAM_INVENTORY_UNAVAILABLE`'a düşer).
- `Skinora.Transactions/Application/Steam/` (yeni dosyalar):
  - `ISteamInventoryCacheInvalidator.cs` — port (failure semantics: implementasyonlar throw etmez, cache optimizasyon).
  - `NullSteamInventoryCacheInvalidator.cs` — no-op default. Tests + stub default.
- `Skinora.API/Controllers/SteamController.cs` — `GET /api/v1/steam/inventory`, `Authenticated` policy, `[RateLimit("steam-inventory")]` (5/dk policy zaten `appsettings.json`'da kayıtlıydı). SteamID claim'inden çekilen kendi envanteri; 200 envelope / 422 INVENTORY_PRIVATE / 503 STEAM_UNAVAILABLE (ApiResponse.Fail body ile, traceId dahil).
- `Skinora.API/Configuration/SteamModule.cs` — sidecar HTTP client wiring (`AddHttpClient<HttpSteamSidecarInventoryClient>` BaseUrl + Timeout), `ISteamSidecarInventoryClient` + `ISteamInventoryQueryService` kayıtları, `services.Replace` ile `ISteamInventoryReader` ve `ISteamInventoryCacheInvalidator` swap (stub → sidecar impl). `AddSteamModule(IConfiguration)` imzasına IConfiguration eklendi.
- `Skinora.API/Configuration/TransactionsModule.cs` — `ISteamInventoryCacheInvalidator` için `TryAddScoped<NullSteamInventoryCacheInvalidator>` (stub default; SteamModule production swap'lar).
- `Skinora.API/Program.cs` — `AddSteamModule(builder.Configuration)` (yeni imza).
- `Skinora.API/appsettings.json` + `appsettings.Development.json` — `SteamSidecar` config bölümü (BaseUrl, InternalKey, TimeoutSeconds).
- `Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs` — constructor'a `ISteamInventoryCacheInvalidator` parametresi eklendi; `SaveChangesAsync` sonrası yeni "Stage 10b" adımı seller'ın SteamID'si için cache invalidate eder (best-effort, failure swallow).
- Mevcut `TransactionCreationServiceTests.cs` `BuildSut`'a `new NullSteamInventoryCacheInvalidator()` eklendi (zaten `using` mevcuttu).
- 22 yeni .NET testi: `SteamInventoryEndpointTests.cs` (6 integration — unauth 401, auth success 200 envelope, private 422 INVENTORY_PRIVATE, unavailable 503 STEAM_UNAVAILABLE, multi-user claim forwarding, rate-limit 5/dk 6th call 429), `HttpSteamSidecarInventoryClientTests.cs` (8 unit — 200 mapping, X-Internal-Key header, 422 private, 5xx unavailable, transport error, DELETE shape, transport-swallow, ISteamInventoryCacheInvalidator bridge), `SidecarSteamInventoryReaderTests.cs` (5 unit — found/missing/private/unavailable/empty-inputs short-circuit). Tümü PASS.

## Etkilenen Modüller / Dosyalar

### Sidecar
**Oluşturulan**
- `sidecar-steam/src/cache/InventoryCache.ts`
- `sidecar-steam/src/trade/InventoryService.test.ts`

**Güncellenen**
- `sidecar-steam/src/trade/InventoryService.ts` (stub → real impl)
- `sidecar-steam/src/api/routes.ts` (GET inventory + DELETE cache)
- `sidecar-steam/src/api/routes.test.ts` (T67 alt-suite: 13 yeni test)
- `sidecar-steam/src/index.ts` (Redis + InventoryService wiring)
- `sidecar-steam/src/config/index.ts` (redisUrl)
- `sidecar-steam/src/logger.ts` (Logger type export)
- `sidecar-steam/package.json` + `package-lock.json` (ioredis@^5.4.0)

### Backend
**Oluşturulan**
- `backend/src/Modules/Skinora.Steam/Application/Inventory/SteamSidecarOptions.cs`
- `backend/src/Modules/Skinora.Steam/Application/Inventory/SteamInventoryDtos.cs`
- `backend/src/Modules/Skinora.Steam/Application/Inventory/ISteamSidecarInventoryClient.cs`
- `backend/src/Modules/Skinora.Steam/Application/Inventory/HttpSteamSidecarInventoryClient.cs`
- `backend/src/Modules/Skinora.Steam/Application/Inventory/ISteamInventoryQueryService.cs`
- `backend/src/Modules/Skinora.Steam/Application/Inventory/SteamInventoryQueryService.cs`
- `backend/src/Modules/Skinora.Steam/Application/Inventory/SidecarSteamInventoryReader.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Steam/ISteamInventoryCacheInvalidator.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Steam/NullSteamInventoryCacheInvalidator.cs`
- `backend/src/Skinora.API/Controllers/SteamController.cs`
- `backend/tests/Skinora.API.Tests/Integration/SteamInventoryEndpointTests.cs`
- `backend/tests/Skinora.Steam.Tests/Unit/HttpSteamSidecarInventoryClientTests.cs`
- `backend/tests/Skinora.Steam.Tests/Unit/SidecarSteamInventoryReaderTests.cs`

**Güncellenen**
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs` (invalidator dep + Stage 10b)
- `backend/src/Skinora.API/Configuration/SteamModule.cs` (HTTP client + DI swap, IConfiguration)
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` (TryAddScoped invalidator)
- `backend/src/Skinora.API/Program.cs` (AddSteamModule(configuration))
- `backend/src/Skinora.API/appsettings.json` + `appsettings.Development.json` (SteamSidecar)
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCreationServiceTests.cs` (BuildSut)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Steam Community envanter endpoint: `inventory/{steamId}/730/2` | ✓ | `SteamCommunityInventoryFetcher.fetch` `community.getUserInventoryContents(steamId, 730, 2, false, 'english', cb)` çağırır; library altında `https://steamcommunity.com/inventory/{steamId}/730/2` çağrısı (`node_modules/steamcommunity/components/users.js:587`). |
| 2 | Pagination desteği (5000+ item, `start_assetid`/`more_items`) | ✓ | Library `getUserInventoryContents` `more_items=1` olduğu sürece `start_assetid` ile devam eder (`users.js:585-624`). Per-page 1000 item (5000 max altında, library kararı). Test: `InventoryService.test.ts` "buildInventoryResponse is a pure function" + sidecar restart sonrası tüm sayfaları yeniden çeker (`re-fetches after invalidation`). |
| 3 | Assets + descriptions merge (classid + instanceid join) | ✓ | Library merge işlemi `CEconItem` constructor'unda yapar; `mapItem` projeksiyonu `classId`+`instanceId`+`marketHashName`+`type`+`exterior` alanlarını tek envelope item'ında üretir. Test: `InventoryService.test.ts` "maps CEconItem fields to the 07 §6.1 envelope shape" (10 alan eşitlik kontrolü). |
| 4 | Redis cache: 2dk TTL, işlem sonrası invalidation | ✓ | `INVENTORY_CACHE_TTL_SECONDS = 120` (`InventoryCache.ts`); `RedisInventoryCache.set` `SETEX` ile yazar; `TransactionCreationService` Stage 10b `await _inventoryCacheInvalidator.InvalidateAsync(seller.SteamId, ct)` → `HttpSteamSidecarInventoryClient` DELETE `/api/inventory/{steamId}/cache` ile sidecar cache'i temizler. Test: `InventoryService.test.ts` "serves cached responses without re-calling the fetcher" + "re-fetches after invalidation" + `HttpSteamSidecarInventoryClientTests` "InvalidateInventoryAsync sends DELETE with cache suffix". Trade-offer-side invalidation → **K1 T68 devir**. |
| 5 | API endpoint: `GET /steam/inventory` (backend → sidecar HTTP çağrısı) | ✓ | `SteamController.GetInventory` Authenticated + `[RateLimit("steam-inventory")]` 5/dk; SteamID claim'inden `ISteamInventoryQueryService` → `ISteamSidecarInventoryClient.GetInventoryAsync` → `GET {SteamSidecar:BaseUrl}/api/inventory/{steamId}` (`X-Internal-Key` header). Test: `SteamInventoryEndpointTests` 6 integration test (auth, success, private, unavailable, multi-user, rate-limit). |
| 6 | Private envanter tespiti → kullanıcıya uyarı | ✓ | Library `Error('This profile is private.')` → `InventoryPrivateError` (sidecar) → HTTP 422 → backend `SteamSidecarStatus.InventoryPrivate` → `SteamController` 422 `ApiResponse.Fail("INVENTORY_PRIVATE", "Steam inventory is private. Profile must be public to read items.")`. Test: `InventoryService.test.ts` "throws InventoryPrivateError when steamcommunity reports a private profile" + `SteamInventoryEndpointTests` "GetInventory_Private_Returns422_InventoryPrivate" + `HttpSteamSidecarInventoryClientTests` "Returns_InventoryPrivate_On_422". |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Sidecar build | ✓ | `npm run build` (`tsc`) — 0 error |
| Sidecar unit/integration (Vitest) | ✓ 123/123 PASS | `npm test` — TradeOfferService 13 + BotSession 20 + routes 13 (T67 +9 inventory) + TradeOfferMonitor 20 + BotManager 10 + InventoryService 13 (T67 yeni) + WebhookPayloads 19 + BotHealthCheck 6 + BotConfig 9 |
| Sidecar lint | ✓ | `npm run lint` (ESLint) — 0 error |
| Backend Release build | ✓ 0W/0E | `dotnet build backend/Skinora.sln -c Release` |
| Backend Debug build | ✓ 0W/0E | `dotnet build backend/Skinora.sln -c Debug` |
| Backend Skinora.Steam.Tests (Unit) | ✓ 13/13 PASS | `dotnet test --filter "FullyQualifiedName~Skinora.Steam.Tests.Unit"` — HttpSteamSidecarInventoryClient 8 + SidecarSteamInventoryReader 5 |
| Backend Skinora.API.Tests SteamInventoryEndpoint | ✓ 6/6 PASS | `dotnet test --filter "FullyQualifiedName~SteamInventoryEndpointTests"` — unauth 401, success 200, private 422, unavailable 503, multi-user claim, rate-limit 6th 429 |
| Backend full API integration (SQLite) | ✓ 315/315 PASS (non-Docker) | Lokal Docker yok → Testcontainers MSSQL gerektiren 25 test atlandı (T11.3 design, CI runner'da geçer). 0 yeni regresyon. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (yapım bitti, validate chat açılacak) |
| Bulgu sayısı | — (henüz validate edilmedi) |
| Düzeltme gerekli mi | — |

**Adım -1 working tree:** temiz (T66 merge sonrası `888b219` fast-forward'lı main).
**Adım 0 main CI startup:** son 3 main run hepsi `success` — `25881647522` + `25881647526` (T66 #106 merge), `25824801517` (T65 #105 merge). HARD STOP yok.
**Adım 0b repo memory:** `.claude/memory/MEMORY.md` T66 satırı mevcut (`bb97834`/PR #98 yerine T66 doğru hash `15ac139`/PR #106; memory MEMORY.md T66 detay satırları F4 statüsü hazır). T67 satırı bu PR'da eklenecek.
**Adım 7a task branch CI:** push sonrası tetiklenecek (PR açılınca run ID görünür → izlenecek).

## Altyapı Değişiklikleri

- **Migration:** Yok (yeni DB tablo yok; cache invalidation flag bir state değişikliği değil).
- **Config/env değişikliği:**
  - Backend `appsettings.json` + `appsettings.Development.json`: yeni `SteamSidecar` bölümü (BaseUrl, InternalKey, TimeoutSeconds).
  - Sidecar: yeni `REDIS_URL` env var (opsiyonel — boşsa in-memory cache'e düşer, T16 ile gelen Redis docker-compose servisinden bağlanabilir).
- **Docker değişikliği:** Yok (mevcut `skinora-redis` servisi sidecar tarafından da kullanılabilir; deployment compose dosyasında sidecar-steam'in `REDIS_URL=redis://skinora-redis:6379` ile başlaması beklenir, T16 Phase F0'da redis servisi zaten ayağa kalkıyor).
- **Yeni paket:** Sidecar `ioredis@^5.4.0` (resolved 5.10.1). Backend tarafı mevcut `StackExchange.Redis` yeterli; SteamSidecar wiring sadece `HttpClient` kullanıyor.

## Commit & PR

- Branch: `task/T67-steam-inventory-read`
- Commit: pending (push aşamasında)
- PR: pending
- CI: pending

## Known Limitations / Follow-up

- **K1 — Trade-offer-side cache invalidation T68 devir:** 08 §2.3 invalidation iki tetikleyici tanımlıyor: (a) işlem başlatma — bu PR'da `TransactionCreationService` Stage 10b ile karşılandı, (b) trade offer terminal event sonrası — backend tarafında trade offer webhook handler T68'de gelecek. Şu an T66 sidecar event'leri (`trade_offer.accepted` vb.) backend'de tüketilmiyor; T68 webhook handler'lar bağlanınca aynı `ISteamInventoryCacheInvalidator` portu kullanılarak çağrılacak (port hazır).
- **K2 — CS2 inspect link çıkarımı:** `SidecarSteamInventoryReader.InspectLink` daima `null`. Steam Community endpoint inspect URL'lerini `actions[]` listesinde template formatında verir (`steam://rungame/730/76561202255233023/+csgo_econ_action_preview {assetid}`); T67 scope'unda template parsing dahil değil — T81 (Steam Market entegrasyonu) veya gelecek bir trade pipeline task'ı bu template'i derive edebilir. Şimdilik 03 §2.2 step 8 tradeability check için inspect link gerekli değil.
- **K3 — Sidecar inventory health probe atlandı:** `BotHealthCheck` (T64) bot session probe'u içeriyor; ayrı bir InventoryService health probe yok. `getInventory` çağrıları zaten transient hataları yakalar ve `SteamUnavailable` döndürür. Geleneksel `/health` endpoint zaten 200 dönüyor (sidecar service availability ≠ Steam Community availability).
- **K4 — Bot session inventory fetch path'i kullanılmıyor:** Sidecar `new SteamCommunity()` anonymous instance ile fetch yapıyor. Inventory okumak için bot login gerekli değil; ancak rate limit baskısı altında bot session kullanmak yardımcı olabilir. Şimdilik anonymous yeterli. T81 / Steam outage çözümlemeleri sırasında bot pool kullanımına geçilebilir.
- **K5 — Sidecar restart sonrası cache reset:** Redis cache durable; sidecar restart cache'i etkilemez. Ancak Redis URL boş veya Redis kapalıysa `InMemoryInventoryCache` kullanılır ve restart'ta sıfırlanır. Production deployment'ta Redis bağlantısı zorunlu; dev/test fallback in-memory.
- **K6 — Prettier drift carry-over:** Mevcut T14/T65/T66 prettier drift'i devam ediyor. T67'nin yeni dosyaları `prettier --write` ile formatlandı; chore PR'da topluca temizlenecek.
- **K7 — Single-replica InventoryService instance:** Sidecar tek replica çalıştığı için bir SteamCommunity instance'ı yeterli. Multi-replica scale-out durumunda her replica kendi instance'ını tutacak; rate limit dağıtımı için Redis-backed rate-limit kuyruğu (mevcut `RateLimitedQueue` T14 altyapısı) genişletilebilir.

## Notlar

- **Working tree:** Adım -1 temiz (T66 merge sonrası fast-forward'lı main: `888b219`).
- **Main CI startup check (Adım 0):** Son 3 main run hepsi `success` — `25881647522` + `25881647526` (T66 #106 merge), `25824801517` (T65 #105 merge). HARD STOP yok.
- **Dış varsayımlar (Adım 4):**
  - `steamcommunity@^3.48.3.getUserInventoryContents` mevcut ve documented — ✓ `node_modules/@types/steamcommunity/components/users.d.ts:84` typedef + `users.js:565` runtime.
  - Library pagination internal — ✓ `users.js:585-624` `more_items` döngüsü.
  - Private envanter algılaması: `Error('This profile is private.')` — ✓ `users.js:606`.
  - `ioredis@^5.4.0` mevcut — ✓ `npm view ioredis version` → `5.10.1`.
  - Backend `[RateLimit("steam-inventory")]` policy `appsettings.json` zaten kayıtlı — ✓ `Limit: 5, WindowSeconds: 60`.
  - Skinora.Steam zaten Skinora.Transactions reference'ı taşıyor (csproj inceleme) — ✓ port swap için reference cycle yok.
- **Mini güvenlik kontrolü:**
  - Secret sızıntısı: `SteamSidecar:InternalKey` `REPLACE_IN_ENV` placeholder; production'da env var ile override edilir. `X-Internal-Key` header sidecar tarafında doğrulanır (`internalKeyAuth` middleware).
  - Auth/authorization: `GET /api/v1/steam/inventory` Authenticated policy + 5/dk rate limit (07 §6.1 contract). SteamID claim'inden çekilir, kullanıcı keyfi steamId pass edemez (no path/query param).
  - Input validation: Sidecar endpoint Steam ID regex (`/^7656119[0-9]{10}$/`) ön doğrulama; SidecarSteamInventoryReader empty string short-circuit.
  - Yeni dış bağımlılık: `ioredis@^5.4.0` (sidecar only); yaygın olarak kullanılan, aktif maintenance, tanınmış kütüphane.
- **Doğrulama önerisi:** Bağımsız validate chat açıldığında yapım raporu görülmeden çalışır; integration tests + unit tests + sidecar tests zaten branch'te commitli ve CI'da geçeceği için validator independent verification yapabilir.
