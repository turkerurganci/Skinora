# T63a — Platform public endpoint'leri (backend)

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (yapım bitti, validator ayrı chat) | **Tarih:** 2026-05-08

---

## Yapılan İşler

T63a, 07 §10.1 P1 ve §10.2 P2 contract'larını gerçekleyen iki anonim okuma endpoint'ini canlıya alır: landing page için `GET /platform/stats` (15 dk cache; tamamlanan işlem sayısı + uptime) ve C08 maintenance banner için `GET /platform/maintenance` (30 sn cache; aktif/tip/mesaj/planlı bitiş). Maintenance state için 4 yeni `platform.maintenance.*` SystemSetting + validator (enum + ISO 8601 + cross-key) eklenir; admin SET path'i T63 mevcut `/admin/settings` uçtası üzerinden aynı validator zincirine uğrayarak çalışır — T63a yeni admin endpoint'i tanımlamaz.

1. **Yeni controller — `Skinora.API/Controllers/PlatformController.cs`:** `[ApiController] [AllowAnonymous] [Route("api/v1/platform")]`. İki action: `GET stats` + `GET maintenance`, ikisi de `[RateLimit("public")]` (mevcut bucket; 30 req / 60 sn).

2. **Yeni servis — `Skinora.API/Services/PlatformPublicService.cs`** (`IPlatformPublicService`):
   - `GetStatsAsync` — `IMemoryCache.TryGetValue("platform:stats")` → fresh ise döner; aksi halde `_db.Set<Transaction>().AsNoTracking().CountAsync(t => t.Status == COMPLETED, ct)` + `_options.UptimePercent` + 15 dk TTL ile cache'e yazar.
   - `GetMaintenanceAsync` — 4 SystemSetting key'i (`platform.maintenance.{active,type,message,planned_end}`) tek query ile okur (`Where(s => keys.Contains(s.Key))`), `"NONE"` sentinel'i null'a normalleştirir, 30 sn TTL ile cache'e yazar. `bool.TryParse` ile `active` parse edilir; başarısız parse `false`'a düşer (defansif).

3. **DTO'lar — `Skinora.API/Services/PlatformPublicDtos.cs`:**
   - `PlatformStatsResponse(int TotalCompletedTransactions, decimal PlatformUptimePercent)` — 07 §10.1 örneğine 1:1 (`12480` int + `99.9` decimal).
   - `PlatformMaintenanceResponse(bool Active, string? Type, string? Message, string? PlannedEnd)` — 07 §10.2 tablosuna 1:1, sentinel→null map'i belgelendi.

4. **Config — `Skinora.API/Services/PlatformOptions.cs`** (`PlatformOptions.SectionName = "Platform"`): `decimal UptimePercent { get; set; } = 99.9m`. `appsettings.json`'a `"Platform": { "UptimePercent": 99.9 }` blok eklendi. `Program.cs` → `builder.Services.Configure<PlatformOptions>(builder.Configuration.GetSection(PlatformOptions.SectionName))`.

5. **DI — `Program.cs`:** `AddMemoryCache()` + `AddScoped<IPlatformPublicService, PlatformPublicService>()`. AdminDashboardService satırının hemen altına yerleştirildi (mantıksal komşuluk: cross-module orchestration). Cache backend kararı yorumda belgelendi (Redis yerine IMemoryCache: replica başına 30 sn drift kabul edilebilir; çapraz-replica invalidation gerekmiyor).

6. **SystemSetting catalog + seed (4 yeni anahtar, T63a SPEC_GAP doldurma):**
   - `SystemSettingsCatalog.cs` — 4 metadata (ApiCategory `"platform_maintenance"`).
   - `SystemSettingSeed.cs` — index 38–41, `Default(...)` (hepsi configured): `active="false"`, `type="NONE"`, `message="NONE"`, `planned_end="NONE"`. DB Category `"Platform"`.
   - `SystemSettingsValidator.cs` — yeni statik `MaintenanceTypes` set (`PLANNED_MAINTENANCE | PLATFORM_MAINTENANCE | STEAM_OUTAGE | BLOCKCHAIN_DEGRADATION | NONE`); range stage'de `platform.maintenance.type` enum kontrolü ve `platform.maintenance.planned_end` ISO 8601 + `NONE` parse kontrolü; cross-key kuralı: `active=true` iken `type=NONE` reddedilir; `TryReadBool` helper eklendi.

7. **Migration — `20260508182045_T63a_AddPlatformMaintenanceSettings`:** `dotnet ef migrations add` ile üretildi; Up: 4 `InsertData` satırı, Down: 4 `DeleteData`. AppDbContextModelSnapshot güncellendi.

8. **Doküman uyumu:** Plan/spec değişikliği yok — 07 §10.1–§10.2 contract'ı birebir uygulandı. SystemSetting key isimleri spec'te tanımlı değildi; T63a'da `platform.maintenance.*` namespace'i ile basit/aramalı bir taksonomi seçildi (`auth.*`, `wallet.*`, `multi_account.*`, `reputation.*` mevcut konvansiyonu mirror).

## Etkilenen Modüller / Dosyalar

**Yeni — `Skinora.API/`:**
- `backend/src/Skinora.API/Controllers/PlatformController.cs`
- `backend/src/Skinora.API/Services/IPlatformPublicService.cs`
- `backend/src/Skinora.API/Services/PlatformPublicService.cs`
- `backend/src/Skinora.API/Services/PlatformPublicDtos.cs`
- `backend/src/Skinora.API/Services/PlatformOptions.cs`

**Yeni — Migration + test:**
- `backend/src/Skinora.Shared/Persistence/Migrations/20260508182045_T63a_AddPlatformMaintenanceSettings.cs` (+Designer)
- `backend/tests/Skinora.API.Tests/Integration/PlatformPublicEndpointTests.cs` (6 test)

**Değişiklik:**
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs` — 4 yeni metadata.
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsValidator.cs` — `MaintenanceTypes` + 2 range branch + cross-key + `TryReadBool` helper.
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs` — index 38–41.
- `backend/src/Skinora.API/Program.cs` — `AddMemoryCache()` + `Configure<PlatformOptions>` + `IPlatformPublicService` DI.
- `backend/src/Skinora.API/appsettings.json` — `Platform.UptimePercent = 99.9`.
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` — 4 yeni HasData snapshot.
- `backend/tests/Skinora.Platform.Tests/Unit/Settings/SystemSettingsValidatorTests.cs` — 6 yeni `[Theory]/[Fact]` (maintenance type enum + planned_end ISO + cross-key 3 senaryo).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /platform/stats` → platform istatistikleri (tamamlanan işlem sayısı, toplam hacim vb.), 15dk cache | ✓ | `PlatformController:GetStats` `[AllowAnonymous]+[RateLimit("public")]`. `PlatformPublicService.GetStatsAsync` `IMemoryCache` 15 dk TTL (`StatsCacheTtl = TimeSpan.FromMinutes(15)`). Integration `Stats_AfterCompletedTransactions_AggregatesCount` 4 işlem (2×COMPLETED + CREATED + CANCELLED_BUYER) seed → `totalCompletedTransactions=2`. **Toplam hacim:** 07 §10.1 example body'sinde yok (`totalCompletedTransactions` + `platformUptimePercent` ikilisi sözleşme); plan tanımındaki "vb." ifadesi spec ile uyumlu. |
| 2 | `GET /platform/maintenance` → bakım durumu (aktif/pasif, mesaj, tahmini bitiş) | ✓ | `PlatformController:GetMaintenance` `[AllowAnonymous]+[RateLimit("public")]`. `PlatformPublicService.GetMaintenanceAsync` 4 SystemSetting key'i okur, `"NONE"` → null. Integration `Maintenance_DefaultSeed_ReturnsInactive_WithNullFields` (active=false + 3 null) ve `Maintenance_ActiveState_ReturnsTypeMessageAndPlannedEnd` (active=true + PLATFORM_MAINTENANCE + TR mesaj + ISO planlı bitiş) iki yönü kanıtlar. |
| 3 | Anonim erişim (auth gerekmez) | ✓ | Controller seviyesi `[AllowAnonymous]`; `RateLimit("public")` kovası IP-bazlı (auth'lı UserScopedPolicies setinde değil — `RateLimitMiddleware:UserScopedPolicies`). Integration test'lerde `_factory.CreateClient()` token'sız çağrı 200 döner. |

**Doğrulama kontrol listesi:**

- [x] **07 §10.1–§10.2 endpoint sözleşmeleri doğru mu?** ✓ — DTO field isimleri ve tipleri 07 örneklerine 1:1 (`totalCompletedTransactions`, `platformUptimePercent`, `active`, `type`, `message`, `plannedEnd`); `active`/`type` kombinasyon tablosu (07 §10.2) cross-key kuralı ile zorunlu kılındı (active=true ⇒ type≠NONE); inactive durumda 3 alan null (sözleşmedeki "Bakım/kesinti yoksa" örneği).
- [x] **Cache mekanizması çalışıyor mu?** ✓ — `Stats_SecondCall_ServesCachedValue_NotFreshDbRead` ve `Maintenance_SecondCall_ServesCachedValue_NotFreshDbRead`: ilk çağrıda cache'e yazar, ara değişiklik (yeni COMPLETED tx / SystemSetting toggle) sonrası ikinci çağrı stale değeri döner.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Skinora.Platform.Tests `SystemSettingsValidatorTests` + `SystemSettingsCatalogTests`) | ✓ 71/71 | Yeni 6 maintenance test (3 type enum theory + 1 planned_end theory + 3 cross-key); catalog 1:1 seed coverage 4 yeni anahtarı görüyor. `dotnet test ... --filter "FullyQualifiedName~SystemSettingsValidatorTests\|FullyQualifiedName~SystemSettingsCatalogTests"`. |
| Unit (Skinora.Platform.Tests filter `!Integration`) | ✓ 102/102 | Tüm modül unit'ler regresyon temiz. |
| Integration (PlatformPublicEndpointTests) | ✓ 6/6 | Anon stats 200 / completed count agg / stats cache hit / default maintenance inactive null / active state / maintenance cache hit. SQLite in-memory + IMemoryCache singleton (Reset between tests). |
| Integration (Skinora.API.Tests filter `~PlatformPublicEndpointTests`) | ✓ 6/6 | Aynı paket. |
| Skinora.API.Tests tam koşum (Release) | 324/334 | 10 fail = lokal Docker/Testcontainers (`InitialMigrationTests`, `RestartRecoveryServiceTests`, `OutboxTests`, `HangfireTests` vb.) — Windows lokal Docker yok; CI Linux runner'da çalışacak (T28/T62 paterni). |
| Build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` tüm sln. |
| Format verify | ✓ exit=0 | `dotnet format Skinora.sln --verify-no-changes`. |

**Lokal toplam (T63a kapsamı):** Platform unit 102 + Platform Public integration 6 = **108 lokal pass, 0 fail**. Docker-bağımlı integration (migration + outbox + hangfire) CI'da doğrulanacak.

## Altyapı Değişiklikleri

- **Migration:** `20260508182045_T63a_AddPlatformMaintenanceSettings` — 4 InsertData (id 38–41) + 4 DeleteData. AppDbContextModelSnapshot güncellendi.
- **SystemSetting:** 4 yeni anahtar `platform.maintenance.{active,type,message,planned_end}` (Catalog + Seed + Validator).
- **Config/env:** `Platform:UptimePercent` (default `99.9`); `appsettings.json`'da varsayılan değer; üretimde `SKINORA__PLATFORM__UPTIMEPERCENT` env var ile override edilebilir (ASP.NET config provider zinciri).
- **DI:** `IMemoryCache` (built-in) + `IOptions<PlatformOptions>` + `IPlatformPublicService`. Yeni dış paket yok — `Microsoft.Extensions.Caching.Memory` `Microsoft.NET.Sdk.Web` ile birlikte gelir.
- **Docker/CI:** Yok — yeni dosyalar mevcut COPY listesinde (Skinora.API csproj scope), pipeline değişikliği gerekmedi.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyalarda secret yok; integration test JWT secret'ı fixture-only (`t63a-platform-test-secret-key-minimum-32-chars-padding!!`).
- **Auth/authorization:** Endpoint'ler `[AllowAnonymous]` (07 §10 spec gereği). Rate limit `public` bucket (30 req / 60 sn IP bazlı) — anonymous abuse yüzeyi sınırlandırılmış. Maintenance toggle path'i (admin) T63'te zaten `Permission:MANAGE_SETTINGS` korumalı.
- **Input validation:** Endpoint girdi yüzeyi sıfır (no path/query/body params). `platform.maintenance.type` enum kontrolü ve `planned_end` ISO 8601 parse kontrolü Validator'da; admin SET path'inden bypass yok (`SystemSettingsService` aynı validator'ı kullanır — T41).
- **Yeni dış bağımlılık:** Yok.
- **Cache poisoning yüzeyi:** Cache key'leri sabit literal (`"platform:stats"`, `"platform:maintenance"`); kullanıcı girdisinden türetilmez. TTL bazlı; admin maintenance toggle yaparsa 30 sn (maintenance) / 15 dk (stats) içinde yansır (K1).

## Commit & PR

- Branch: `task/T63a-platform-public-endpoints`
- Commits: `7acf17e` (T63a kod + test + migration), `<hash>` (rapor + status + memory yansıtma)
- PR: TBD — push sonrası açılır; CI watch raporda finalize edilir.

## Known Limitations / Follow-up

- **K1 — Cache invalidation explicit hook yok.** Admin `/admin/settings` üzerinden `platform.maintenance.*` veya başka stats etkileyici (yok şu an) bir setting güncellediğinde, cache invalidation olmadığı için max 30 sn (maintenance) / 15 dk (stats) gecikmeyle yansır. T63a sadece GET endpoint'i; T63 SET endpoint'inde `_cache.Remove("platform:maintenance")` çağrısı eklenmesi 1-2 satırlık follow-up — T-future cache observer pattern (publisher → subscribers) içinde ele alınabilir. Spec TTL açısından sıkı bir invalidation gereksinimi belirtmiyor.
- **K2 — `platformUptimePercent` heartbeat hesabına bağlanmamıştır.** SystemHeartbeat singleton'dan gerçek uptime ratio hesaplaması (örn. son 30 gün outage window'ları) `OutageWindow` tablosu/job'ı gerektirir. Şu an config-driven sabit (`Platform:UptimePercent`, default 99.9). `PlatformOptions.cs` yorumunda forward-devir notu belgelendi; T-future operations/observability task'ı devraldığında DI swap (yeni `IPlatformUptimeProvider` interface arkası) ile değiştirilebilir.
- **K3 — Maintenance `Clients.All` SignalR push'u (07 §11.2 RT2 `MaintenanceStatusChanged`) henüz tetiklenmiyor.** T62 K3 follow-up notunda yer alıyor. Admin maintenance toggle endpoint'i (T63 `PUT /admin/settings/{key}`) içinde 1-2 satır publisher inject + `await publisher.PublishMaintenanceStatusChangedAsync(...)` eklendiğinde aktif olur. T63a kapsamı sadece GET endpoint; toggle path'i değişmez (mevcut T63 admin SET zaten çalışır + validator zincirine uğrar).
- **K4 — `IMemoryCache` çapraz-replica invalidation yok.** Multi-instance API host'ta her instance kendi cache'ini tutar; admin maintenance toggle'ı bir replica'da yapılırsa diğer replica'lar 30 sn'ye kadar eski değeri serve eder. F3 fazında tek host runtime için sorun değil; T-future scale-out task'ında IDistributedCache (Redis backend, mevcut `Redis:ConnectionString`) swap'ı ile çözülür. Cache key contract'ı (`PlatformPublicService.{Stats,Maintenance}CacheKey`) public sabit olarak expose edildi — değişimde geriye uyumlu.

## Notlar

- **Working tree pre-flight:** clean (`git status --short` boş). Adım -1 ✓.
- **Main CI startup pre-flight:** son 3 main run ✓ — `25570554435` (T63 PR #100), `25570554411` (T63 PR #100), `25513910803` (T62 PR #99). Adım 0 ✓.
- **Bağımlılık kontrolü:** T04 ✓ (F1'de Skinora.Platform modülü tanımlandı + InitialCreate migration).
- **Dış varsayım kontrolü (Adım 4):**
  - `IMemoryCache` `Microsoft.Extensions.Caching.Memory` namespace ile `Microsoft.NET.Sdk.Web` template'inde mevcut, ek NuGet gerekmez. ✓ — Skinora.API zaten built-in extension'ları kullanıyor.
  - 07 §10.1 stats payload alanları `totalCompletedTransactions` + `platformUptimePercent` (uptime kaynağı belirtilmemiş). ✓ Doğrulandı — kullanıcı kararı: sabit appsettings (T96+ heartbeat hesabına devir, K2).
  - 07 §10.2 maintenance payload + type enum + sentinel davranışı: spec'te `null` field kullanımı belirtilmiş ama storage tarafı tanımsız. ✓ Doğrulandı — kullanıcı kararı: 4 yeni `platform.maintenance.*` SystemSetting + `"NONE"` sentinel + null normalleştirme.
  - SystemSettings cross-key validator zinciri (T41) admin SET path'inde de çalışır (T63'ün `PUT /admin/settings/{key}` ve `SettingsBootstrapService`). ✓ — kod incelemesinde `SystemSettingsService.UpdateAsync` ve `SettingsBootstrapService` ikisi de `SystemSettingsValidator.Instance.ValidateSingle` + `ValidateCrossKey` kullanır; yeni maintenance kuralları otomatik devreye girer.
  - T28 + T30 + T56 migration paterni (her yeni seed satırı için ayrı migration): ✓ — `dotnet ef migrations add` ile aynı patern uygulandı.
- **Cache backend kararı (IMemoryCache vs Redis):** Kullanıcı onayı ile IMemoryCache. Sebep: 15 dk stats / 30 sn maintenance cache window'ları replica başına farklı state için ihmal edilebilir (sözleşmede çapraz-replica tutarlılık yok); IDistributedCache eklenmesi yeni bağımlılık + serialization yüzeyi getirir. K4 forward-devir.
- **Maintenance store kararı (4 SystemSetting vs singleton entity):** Kullanıcı onayı ile 4 SystemSetting. Sebep: T63 admin SET endpoint'i ile zaten yönetilebilir; ayrı entity + admin endpoint scope'u büyütür. Validator type/range/cross-key zinciri mevcut altyapıyı yeniden kullanır.
- **`ApiCategory` "platform_maintenance" lowercase:** 07 §9.8 admin GET settings response'unda kullanılan API category dialect (`transaction_limits`, `cancel_rules`, `auth.*` `geo_blocking`/`age_verification` paterni). DB Category column ise `"Platform"` (PascalCase), mevcut `"Limit"`, `"Wallet"`, `"AccessControl"` paterniyle uyumlu.
