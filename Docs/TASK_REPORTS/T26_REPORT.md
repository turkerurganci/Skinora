# T26 — Seed Data

**Faz:** F1 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-04-19

---

## Yapılan İşler
- **Seed sabitleri (06 §8.9):** `Skinora.Shared.Domain.Seed.SeedConstants` — `SystemUserId` (sabit Guid), `SystemSteamId` (17-haneli sentinel), `SystemHeartbeatId = 1`, `SeedAnchorUtc` (determinist migration çıkışı için sabit tarih).
- **SYSTEM service account seed:** `UserConfiguration.HasData(...)` — 06 §8.9'daki tüm sentinel alanlar (`SteamId = "00000000000000001"`, `SteamDisplayName = "System"`, `IsDeactivated = true`, `MobileAuthenticatorVerified = false`) ile tek satır.
- **SystemHeartbeat singleton seed:** `SystemHeartbeatConfiguration.HasData(...)` — Id=1 ile tek satır, `CK_SystemHeartbeats_Singleton` ile zaten korunuyor.
- **SystemSetting seed (28 satır):** `SystemSettingSeed.All` — 06 §3.17'deki tüm parametreler, determinist Guid üretimiyle. 8 satır `IsConfigured = true` (varsayılanı olan: commission_rate=0.02, gas_fee_protection_ratio=0.10, monitoring_* ve open_link_enabled=false, min_refund_threshold_ratio=2.0). 20 satır `IsConfigured = false, Value = NULL` (varsayılanı "—" olan — lansman öncesi zorunlu).
- **SettingsBootstrapService** (Skinora.Platform/Infrastructure/Bootstrap): `IsConfigured = false` satırları için `SKINORA_SETTING_{KEY_UPPER}` env var araması, DataType parse validasyonu (int/decimal/bool/string), başarılıysa `Value` + `IsConfigured = true`, sonrasında kalan eksik parametre varsa `InvalidOperationException` ile fail-fast.
- **SettingsBootstrapHook** (Skinora.API/Startup): IHostedService, startup'ta `SettingsBootstrapService.ExecuteAsync()` çağırır. OutboxStartupHook'tan **önce** kaydedildi (StartAsync registration sırasına göre çalışır) — dispatcher chain yalnızca configuration tam ise başlar.
- **T25 test uyumluluğu:** `SystemSettingEntityTests` seed-collision için `test_` prefix'li key'lere güncellendi (commission_rate → test_commission_rate, cancel_limit_count → test_cancel_limit_count). `SystemHeartbeatEntityTests` seed Id=1 satırı göz önüne alınarak yeniden yazıldı (INSERT testi → UPDATE testi, "seed row present" testi eklendi).
- **Yeni testler:** `SeedDataTests` (6) + `SettingsBootstrapTests` (5) — toplam 11 yeni integration test.

## Etkilenen Modüller / Dosyalar

### Yeni Dosyalar
- `backend/src/Skinora.Shared/Domain/Seed/SeedConstants.cs`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Bootstrap/SettingsBootstrapService.cs`
- `backend/src/Skinora.API/Startup/SettingsBootstrapHook.cs`
- `backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs`
- `backend/tests/Skinora.Platform.Tests/Integration/SettingsBootstrapTests.cs`

### Değişen Dosyalar
- `backend/src/Modules/Skinora.Users/Infrastructure/Persistence/UserConfiguration.cs` — SYSTEM user `HasData(...)`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemHeartbeatConfiguration.cs` — singleton `HasData(...)`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingConfiguration.cs` — `HasData(SystemSettingSeed.All)`
- `backend/src/Skinora.API/Program.cs` — `AddScoped<SettingsBootstrapService>` + `AddHostedService<SettingsBootstrapHook>` (Outbox'tan önce)
- `backend/tests/Skinora.Platform.Tests/Skinora.Platform.Tests.csproj` — `Microsoft.Extensions.Configuration` + `.Configuration.Binder` PackageReference (bootstrap testleri için `ConfigurationBuilder`)
- `backend/tests/Skinora.Platform.Tests/Integration/SystemSettingEntityTests.cs` — seed collision'dan kaçınmak için `test_` prefix'li key'ler
- `backend/tests/Skinora.Platform.Tests/Integration/SystemHeartbeatEntityTests.cs` — seed Id=1 satırıyla uyumlu testler

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | SYSTEM service account: User tablosunda sabit GUID `00000000-0000-0000-0000-000000000001`, SteamId `"00000000000000001"`, IsDeactivated=true | ✓ | `SeedConstants.SystemUserId/SystemSteamId`, `UserConfiguration.HasData(...)`. Test `Seed_SystemUser_IsPresent_With_Sentinel_SteamId_And_Deactivated`. |
| 2 | SystemHeartbeat: Id=1 ile tek satır | ✓ | `SystemHeartbeatConfiguration.HasData(new SystemHeartbeat { Id = 1, ... })`. Test `Seed_SystemHeartbeat_IsSingleton_With_Id_One`. |
| 3 | SystemSetting: 28 platform parametresi seed edildi, varsayılanı olanlar IsConfigured=true, olmayanlar false | ✓ | `SystemSettingSeed.All` (28 satır, 06 §3.17 sıralamasıyla). Testler `Seed_SystemSettings_Has_28_Rows_With_Unique_Keys`, `Seed_SystemSettings_Defaulted_Parameters_Are_Configured` (8 key), `Seed_SystemSettings_Mandatory_Parameters_Are_Unconfigured_And_Null` (20 key). **Doc fark:** 11_IMPLEMENTATION_PLAN "27 parametre" der, 06 v4.9 §3.17 28 satır listeler — source of truth = 06. |
| 4 | Env var bootstrap: `SKINORA_SETTING_{KEY_UPPER}` formatında env var ile SystemSetting hydration | ✓ | `SettingsBootstrapService.LookupEnvValue` + `ExecuteAsync` (sadece `IsConfigured = false` satırları). Test `Execute_With_All_Required_Env_Vars_Hydrates_And_Completes`, `Execute_Does_Not_Override_Already_Configured_Parameters` (security clause). |
| 5 | Startup fail-fast: IsConfigured=false olan zorunlu parametreler kontrol edildi | ✓ | `ExecuteAsync` env hydration'dan sonra `IsConfigured = false` kalan her key için `InvalidOperationException`. Test `Execute_Throws_When_Required_Parameter_Missing`. DataType parse başarısızlığı da fail-fast tetikler: `Execute_Throws_When_Env_Value_Fails_DataType_Validation`. |
| 6 | 06 §8.9'daki tüm seed kayıtları var mı? | ✓ | SYSTEM user + SystemHeartbeat + 28 SystemSetting = 06 §8.9 tablosunun tamamı. |
| 7 | 27 SystemSetting parametresi eksiksiz mi? | ✓ (28) | 06 §3.17 28 satır içeriyor; 11_IMPLEMENTATION_PLAN "27" sayısı doc güncellemesi sonrası güncel değil. Doküman kaynaktır — 28 seed edildi. |
| 8 | Env var bootstrap doğru çalışıyor mu? | ✓ | `Execute_With_All_Required_Env_Vars_Hydrates_And_Completes` — env var hydrated rows' `Value` doğru, `IsConfigured = true`. Security: `Execute_Does_Not_Override_Already_Configured_Parameters` pre-existing `commission_rate = 0.02` env-override saldırısını reddeder. |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 Error, 0 Warning | `dotnet build Skinora.sln --nologo` — 24 proje başarıyla build. |
| Unit testler | — | T26 scope'unda yeni unit test yok; bootstrap mantığı integration testlerle kapsanıyor. |
| Integration testler (lokal) | ⏸ Skipped | Docker daemon lokalde yok (T25'deki known-limitation aynısı). Tüm 11 yeni integration test + mevcut SystemHeartbeat/SystemSetting testleri CI'da TestContainers SQL Server ile çalıştı. |
| Integration testler (CI) | ✓ PASS | Retry #4 run `24626039083` — 11/11 job success + CI Gate ✓. Tüm `SeedDataTests` (6) + `SettingsBootstrapTests` (5) + T25 regresyon testleri PASS. |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Yapım tamamlandı, validate chat'ine devir bekliyor |
| Bulgu sayısı | 0 (yapım chat'inde tespit edilen) |
| Main CI Check (Adım 0) | ✓ 3/3 success — son 3 completed main run: T25 main run `24624092864`, T24 run `24612180850`, T24 Docker `24612180855` (hepsi `conclusion=success`). T25 Docker Publish run `24624092863` hâlâ `in_progress`, skill kuralı gereği sayılmıyor. |
| Lokal Build | ✓ 0 Warning, 0 Error |
| Güvenlik | ✓ Secret sızıntısı yok. Yeni auth etkisi yok. Env var hydration yalnızca `IsConfigured = false` satırları etkiler (override saldırılarına karşı test kapsamında). Yeni dış bağımlılık: `Microsoft.Extensions.Configuration` + `.Binder` (Microsoft first-party). |
| Doküman uyumu | ✓ 06 §8.9 seed contract'ın tamamı (SYSTEM user + SystemHeartbeat + 28 SystemSetting), 06 §3.17 parametre listesi, env var bootstrap mekanizması ve startup fail-fast davranışı. |

## Altyapı Değişiklikleri
- **Migration:** Yok (T28'de initial migration — bu görevdeki HasData seed, T28 migration'ın içine gömülecek).
- **Yeni env var kontratı:** `SKINORA_SETTING_{KEY_UPPER}` — her SystemSetting key'i için opsiyonel. Örnek: `SKINORA_SETTING_COMMISSION_RATE=0.025`. 20 zorunlu parametre için lansman/deploy pipeline'ı ilk seferde env var veya admin API ile hydrate eder. Zaten yapılandırılmış parametreler override edilmez.
- **Startup fail-fast:** API'nin `SettingsBootstrapHook` StartAsync'i, eksik zorunlu parametre veya parse hatasında `InvalidOperationException` fırlatır — host durur, pod crash olur, deployment geri alınır.

## Commit & PR
- Branch: `task/T26-seed-data`
- Commits (pending squash): `cefdb84` (seed + bootstrap + tests) → `70bb576` (dotnet format) → `4b63ba6` (HangfireBypassFactory scrub) → `67bb972` (SQLite RowVersion placeholder) → `1a71dec` (provider-conditional RowVersion)
- PR: #30
- CI: ✓ PASS (retry #4 — run `24626039083`, 11/11 job + CI Gate success)
- BYPASS_LOG entries: 4 × `[ci-failure]` (her düzeltme push'u — son CI failure'a karşı hook koruması)

## Known Limitations / Follow-up
- **Range validation scope dışı:** 06 §3.17'de "Alan aralığı ve çapraz doğrulama" olarak listelenen kurallar (timeout > 0, commission_rate 0<x<1, payment_timeout_min < max, monitoring sıralaması vb.) bu task'ta uygulanmadı. DataType parse validasyonu yeterli minimum savunmadır. Range kontrolü **T41** (Admin parameter yönetimi, F2) scope'una aittir — Admin update path'i bu kuralları zorunlu koşacak ve aynı validator startup bootstrap'e sonradan çekilebilir.
- **Integration test lokal engelli:** T25 raporundaki aynı sınırlama — Docker daemon olmadan TestContainers çalışmıyor. Validator CI run output'undan doğrulayacak.
- **Env var kaynağı IConfiguration:** `SettingsBootstrapService` env var'ı doğrudan `Environment.GetEnvironmentVariable` yerine `IConfiguration`'dan okuyor. Bu, in-memory provider ile test edilebilirlik + ASP.NET Core'un standart env var binding'ine (varsayılan) uyum sağlar. Deploy'da .NET host env var'ları configuration'a otomatik yükler — davranış eşdeğerdir.
- **CI düzeltme turları (4 adet):** İlk 3 push CI'da başarısız oldu, her biri ayrı bir gerçek sorunu ortaya çıkardı: (1) `dotnet format` hizalama boşluklarını reddetti → auto-fix; (2) Skinora.API.Tests `WebApplicationFactory` orijinal SqlServer kayıtlarını koruyarak SQLite ekleyince EF "multiple providers" hatası verdi → `HangfireBypassFactory`'ye `SettingsBootstrapHook` scrub eklendi (T10 OutboxStartupHook pattern'inin aynısı); (3) EF Core `.IsRowVersion()` `ValueGeneratedOnAddOrUpdate` yaptığı için HasData INSERT'lerini kolonsuz emit ediyor — SQL Server rowversion auto-populate, SQLite NOT NULL fail → provider-conditional `RowVersion` config: SQL Server'da `IsRowVersion()`, diğerlerinde `IsConcurrencyToken() + HasDefaultValue(new byte[8])`. Bu son değişiklik `AppDbContext` global config'ine dokundu ama production (SQL Server) davranışı değişmedi. 4 `[ci-failure]` bypass BYPASS_LOG.md'ye kaydedildi.

## Notlar
- **Working tree:** Temiz (task başlarken).
- **Main CI Startup Check:** 3 son **completed** run `conclusion=success` (T25 main, T24 main+docker). T25 Docker Publish in_progress — skill kuralı sayılmaz.
- **Dış varsayım:** Yok. EF Core 9 `HasData` API'si kararlı, `SeedConstants.SystemUserId` Guid formatı doğrulandı (`00000000-0000-0000-0000-000000000001`), `SystemSettingSeed.All` 28 satır 06 §3.17 sırasıyla.
- **Tasarım seçimi — HasData vs runtime seed:** Seed kayıtları `HasData` üzerinden migration'a gömülüyor (T28'de). Runtime seed (IHostedService içinde upsert) reddedildi: migration idempotency kaybı + her deploy'da race condition riski. `HasData`, EF Core'un idiomatic yolu; `EnsureCreated` ve `migrate` her ikisi de seed kayıtları otomatik yerleştiriyor.
- **Hook sırası:** `SettingsBootstrapHook` OutboxStartupHook'tan **önce** kaydedildi (`Program.cs` 82-84 ↔ 96-99). IHostedService.StartAsync registration sırasına göre çalışır; configuration hydrasyonu + fail-fast, dispatcher chain'in primelaştırılmasından önce.
- **T25 test refactor kapsamı:** SystemSetting 2 satırlık key değişikliği + 1 assert; SystemHeartbeat testi tamamen yeniden yazıldı (INSERT → UPDATE paradigma değişimi). Toplam 2 test dosyası, davranışsal kapsam korundu.
