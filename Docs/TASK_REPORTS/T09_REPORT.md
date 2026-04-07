# T09 — Hangfire Setup ve Background Job Altyapısı

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-07

---

## Yapılan İşler

### Backend — Hangfire altyapısı

- **Paket:** `Hangfire.AspNetCore 1.8.18` ve `Hangfire.SqlServer 1.8.18` `Skinora.API.csproj`'a eklendi (transitive: `Hangfire.NetCore`, `Hangfire.Core`, `Newtonsoft.Json`).
- **`IBackgroundJobScheduler` abstraction (`Skinora.Shared/BackgroundJobs/`):** Modüllerin Hangfire'a doğrudan bağımlılık taşımadan job schedule edebileceği interface. Üç metod:
  - `string Schedule<T>(Expression<Action<T>>, TimeSpan)` — delayed one-shot job, jobId döner.
  - `string Enqueue<T>(Expression<Action<T>>)` — fire-and-forget.
  - `bool Delete(string jobId)` — pending job iptali (processing'e başlamış job'ları durdurmaz; bu yüzden 09 §13.3 state validation pattern'ı handler'larda zorunlu — XML doc'ta belirtildi).
- **`HangfireBackgroundJobScheduler` (`Skinora.API/BackgroundJobs/`):** Interface'in Hangfire `IBackgroundJobClient` üzerine yazılmış implementasyonu. Skinora.Shared'a Hangfire bağımlılığı sızmıyor, sadece API host'unda Hangfire bilgisi var.
- **`HangfireOptions` (`Skinora.API/BackgroundJobs/`):** appsettings'ten bind edilen ayar POCO'su — `DashboardEnabled`, `DashboardPath`, `SchemaName`, `PollingIntervalSeconds`, `DefaultRetryAttempts`, `WorkerCount`.
- **`HangfireModule` (`Skinora.API/BackgroundJobs/`):** İki extension method:
  - `AddHangfireModule(IConfiguration)`:
    - SqlServerStorage konfigürasyonu (`SchemaName`, `PrepareSchemaIfNecessary = true`, recommended isolation level, configurable polling interval).
    - `SetDataCompatibilityLevel(CompatibilityLevel.Version_180)`, `UseSimpleAssemblyNameTypeSerializer`, `UseRecommendedSerializerSettings`.
    - **AutomaticRetry(Attempts = 3) global filter:** Hangfire kütüphanesi default olarak `Attempts = 10` ile `AutomaticRetryAttribute`'u global filters'a ekliyor; module bu instance'ı bulup `Attempts = 3` ile mutate eder (yeni filter eklemek yerine — yoksa iki ayrı retry filter çakışırdı). 09 §13.5 ile uyumlu.
    - `AddHangfireServer` — API host hem web hem worker (T16'da/post-MVP'de ayrı container'a bölünebilir, application code değişmeden).
    - `IBackgroundJobScheduler` → `HangfireBackgroundJobScheduler` scoped DI kaydı.
  - `UseHangfireModule(WebApplication)`: Dashboard'u `/hangfire` path'ine mount eder, `HangfireDashboardAuthFilter` ile gating yapar. `DisplayStorageConnectionString = false` (DB connection string dashboard'da görünmez).
- **`HangfireDashboardAuthFilter` (`Skinora.API/BackgroundJobs/`):** `IDashboardAuthorizationFilter` impl. `HttpContext.User`'da `AuthClaimTypes.Role` claim'i `Admin` veya `SuperAdmin` mi diye bakar. Anonim → 401, authenticated non-admin → 403, admin/superadmin → 200. T06 `AuthRoles` ile tam uyumlu.
- **`Program.cs`:**
  - `builder.Services.AddHangfireModule(builder.Configuration)` rate limiting kaydından sonra eklendi.
  - `app.UseHangfireModule()` `UseAntiforgery`'den sonra, `MapControllers`'tan önce mount edildi (auth/rate-limit/authorization middleware'lerinden sonra olması zorunlu — dashboard auth filter'ı authenticated principal görmek zorunda).
- **`appsettings.json`:** `Hangfire` bölümü eklendi (default değerlerle).

### Pattern referansları (09 §13.3, §13.6 — kod değil dokümantasyon + test)

T09 implementation phase'inin doğru zamanlaması: Transaction entity (T19), TimeoutService (T47–T50) henüz yok. T09'un işi pattern'ları **uygulanabilir** kılmak (`IBackgroundJobScheduler` ile altyapı), pattern'ların kendi referans implementasyonu T44+ task'larında gelir. Pattern'lar:

- **Timeout scheduling pattern (09 §13.3):** `IBackgroundJobScheduler.Schedule(...)` çağrısı + dönen jobId'nin entity'de saklanması + state geçişinde `Delete(jobId)`. Test edildi (`HangfireTests.Schedule_ThenDelete_ReturnsTrue_AndStorageMarksDeleted`).
- **State validation pattern (09 §13.3):** Job handler runtime'ında state reload edip koşul tutmuyorsa no-op. `HangfireTests` içinde `SampleStateValidatedTimeoutHandler` sınıfı pattern'ı tam olarak gösterir; 5 farklı senaryo test edildi (entity yok, status değişmiş, frozen, deadline ileride, tüm koşullar tutuyor → process). T44+ handler'ları bu şablona uyacak.
- **Freeze/resume pattern (09 §13.6):** Schedule → Delete → yeni Schedule (extended deadline). `HangfireTests.FreezeResume_Pattern_DeletesOldJob_AndReschedulesNewOne` ile schedule + delete + yeniden schedule akışı end-to-end doğrulandı, eski jobId'nin Deleted state'inde, yenisinin Scheduled state'inde olduğu kanıtlandı.

### Test altyapısı genelleştirme

T09 sırasında fark edildi: mevcut testler (`AuthenticationTests`, `MiddlewarePipelineTests`, `RateLimitTests`) default `WebApplicationFactory<Program>` kullanıyordu. Production `AddHangfireModule` SqlServerStorage'ı initialize ederken DB'ye bağlanmaya çalışıyor (4 retry × 30s timeout); test ortamında SQL Server olmadığı için her test factory build'i ~30 saniye gecikiyor, paralel test çalışma süresi 4 dakikaya çıkıyor ve xUnit collection sınırları sebebiyle 9 test fail ediyordu.

**Çözüm:** `Skinora.API.Tests/Common/HangfireBypassFactory.cs` ortak base eklendi. Production AddHangfireModule SqlServerStorage'ını DI'dan sökerek `InMemoryStorage` ile değiştirir, `IBackgroundJobScheduler`'ı yeniden kayıt eder. Ek olarak `GlobalJobFilters`'taki AutomaticRetry'ı `Attempts = 3` ile mutate eder (production paritesi). Mevcut test class'ları `IClassFixture<HangfireBypassFactory>`'ye geçirildi. `HangfireTests.TestFactory` bu base'den inherit eder, ek olarak `ITestJobTarget` ve JWT settings ekler.

Bu değişiklik scope creep değildir — T09'un Hangfire enjekte etmesi mevcut testleri kıracak olduğu için aynı task içinde adresleniyor (T08 raporundaki Dockerfile drift fix patterniyle paralel: yeni özellik mevcut altyapıyı kırarsa fix aynı task'ta uygulanır).

## Etkilenen Modüller / Dosyalar

### Backend (yeni dosyalar)

- [backend/src/Skinora.Shared/BackgroundJobs/IBackgroundJobScheduler.cs](backend/src/Skinora.Shared/BackgroundJobs/IBackgroundJobScheduler.cs)
- [backend/src/Skinora.API/BackgroundJobs/HangfireOptions.cs](backend/src/Skinora.API/BackgroundJobs/HangfireOptions.cs)
- [backend/src/Skinora.API/BackgroundJobs/HangfireBackgroundJobScheduler.cs](backend/src/Skinora.API/BackgroundJobs/HangfireBackgroundJobScheduler.cs)
- [backend/src/Skinora.API/BackgroundJobs/HangfireDashboardAuthFilter.cs](backend/src/Skinora.API/BackgroundJobs/HangfireDashboardAuthFilter.cs)
- [backend/src/Skinora.API/BackgroundJobs/HangfireModule.cs](backend/src/Skinora.API/BackgroundJobs/HangfireModule.cs)

### Backend (değişen dosyalar)

- [backend/src/Skinora.API/Skinora.API.csproj](backend/src/Skinora.API/Skinora.API.csproj) — Hangfire.AspNetCore + Hangfire.SqlServer paketleri eklendi
- [backend/src/Skinora.API/Program.cs](backend/src/Skinora.API/Program.cs) — `AddHangfireModule` + `UseHangfireModule` wiring (using directive eklenmesi dahil)
- [backend/src/Skinora.API/appsettings.json](backend/src/Skinora.API/appsettings.json) — `Hangfire` section eklendi

### Test projesi (yeni dosyalar)

- [backend/tests/Skinora.API.Tests/Common/HangfireBypassFactory.cs](backend/tests/Skinora.API.Tests/Common/HangfireBypassFactory.cs)
- [backend/tests/Skinora.API.Tests/Integration/HangfireTests.cs](backend/tests/Skinora.API.Tests/Integration/HangfireTests.cs) — 16 integration test

### Test projesi (değişen dosyalar)

- [backend/tests/Skinora.API.Tests/Skinora.API.Tests.csproj](backend/tests/Skinora.API.Tests/Skinora.API.Tests.csproj) — `Hangfire.InMemory 1.0.0` paketi eklendi
- [backend/tests/Skinora.API.Tests/Integration/AuthenticationTests.cs](backend/tests/Skinora.API.Tests/Integration/AuthenticationTests.cs) — `IClassFixture<HangfireBypassFactory>`
- [backend/tests/Skinora.API.Tests/Integration/MiddlewarePipelineTests.cs](backend/tests/Skinora.API.Tests/Integration/MiddlewarePipelineTests.cs) — `IClassFixture<HangfireBypassFactory>`
- [backend/tests/Skinora.API.Tests/Integration/RateLimitTests.cs](backend/tests/Skinora.API.Tests/Integration/RateLimitTests.cs) — `HangfireBypassFactory` kullanımı (iki yer)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Hangfire SQL Server storage konfigüre edildi | ✓ | [HangfireModule.cs:46-58](backend/src/Skinora.API/BackgroundJobs/HangfireModule.cs#L46-L58) — `UseSqlServerStorage(connectionString, new SqlServerStorageOptions { SchemaName, PrepareSchemaIfNecessary = true, ... })`. Connection string `appsettings.json:38` `DefaultConnection`'dan, prod'da `DB_CONNECTION_STRING` env override (docker-compose'dan zaten geliyor, yeni env eklenmedi). |
| 2 | UTC timezone ayarı | ✓ | Hangfire 1.8 default davranışı: `BackgroundJob.Schedule(TimeSpan)` ve cron expression'ları UTC olarak yorumlanır; explicit `DateTime` overload'ları code review ile kısıtlanır (09 §7.1). HangfireModule içinde [HangfireModule.cs:62-66](backend/src/Skinora.API/BackgroundJobs/HangfireModule.cs#L62-L66) yorum bloğunda not edildi. |
| 3 | `AutomaticRetry(Attempts = 3)` varsayılan | ✓ | [HangfireModule.cs:68-77](backend/src/Skinora.API/BackgroundJobs/HangfireModule.cs#L68-L77) — Hangfire'ın default `AutomaticRetryAttribute(Attempts = 10)` instance'ı `GlobalJobFilters.Filters` içinde mutate ediliyor (`Attempts = 3`). Test: `HangfireTests.GlobalJobFilters_ContainAutomaticRetry_WithThreeAttempts` → PASS. |
| 4 | Timeout scheduling pattern tanımlı (delayed job schedule/cancel) | ✓ | `IBackgroundJobScheduler.Schedule<T>(Expression, TimeSpan)` + `Delete(jobId)` API'si. Tests: `Schedule_DelayedJob_ReturnsNonEmptyJobId`, `Schedule_ThenDelete_ReturnsTrue_AndStorageMarksDeleted`, `Delete_NonExistentJob_ReturnsFalse`, `Enqueue_ReturnsNonEmptyJobId` → 4/4 PASS. |
| 5 | Job handler state doğrulama pattern tanımlı (güncel state kontrol, koşul tutmuyorsa no-op) | ✓ | `HangfireTests.SampleStateValidatedTimeoutHandler` (XML doc ile 09 §13.3'e referans verir) pattern'ı tam olarak gösterir. Tests: `StateValidatedJob_Processes_WhenAllConditionsHold`, `StateValidatedJob_NoOps_WhenEntityMissing`, `StateValidatedJob_NoOps_WhenStatusChanged`, `StateValidatedJob_NoOps_WhenFrozen`, `StateValidatedJob_NoOps_WhenDeadlineNotReached` → 5/5 PASS. T44+ timeout handler'ları bu şablona uyar. |
| 6 | Timeout freeze/resume pattern tanımlı | ✓ | `HangfireTests.FreezeResume_Pattern_DeletesOldJob_AndReschedulesNewOne` → schedule (30 dk) → delete (freeze) → yeni schedule (45 dk extended) akışı end-to-end. Eski jobId Hangfire storage'da `DeletedState`, yeni jobId `ScheduledState`. PASS. T50 (freeze/resume implementation) bu API'yi doğrudan kullanır. |
| 7 | Hangfire dashboard erişilebilir | ✓ | `app.UseHangfireModule()` → dashboard `/hangfire` path'inde mount. Tests: `HangfireDashboard_AdminToken_Returns200`, `HangfireDashboard_SuperAdminToken_Returns200` → 2/2 PASS (dashboard HTML render edilir). Güvenlik: `HangfireDashboard_AnonymousRequest_Returns401` (anonim → 401), `HangfireDashboard_AuthenticatedNonAdmin_Returns403` (user role → 403) → 2/2 PASS. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit + Integration (HangfireTests) | ✓ 16/16 PASS | `dotnet test --filter "FullyQualifiedName~HangfireTests"` → "Failed: 0, Passed: 16, Duration: ~9 s" |
| Regression (Skinora.API.Tests tamamı) | ✓ 79/79 PASS | `dotnet test tests/Skinora.API.Tests/Skinora.API.Tests.csproj` → "Failed: 0, Passed: 79, Duration: 3 m 7 s". T08 sonrası 63 testten 79'a çıkıldı (16 yeni HangfireTests). |
| Solution build | ✓ | `dotnet build Skinora.sln` → "Build succeeded. 0 Warning(s), 0 Error(s)". |
| docker-compose syntax | ✓ | `docker compose config --quiet` → exit 0 (yeni env değişikliği yok, mevcut DB connection string Hangfire için yeterli). |

## Doğrulama Kontrol Listesi (11 §T09)

- ✅ **09 §13.3 timeout scheduling pattern uygulanmış mı?** — `IBackgroundJobScheduler.Schedule<T>(Expression, TimeSpan)` + entity'ye jobId yazma + state geçişinde `Delete(jobId)` akışı. `HangfireTests` schedule/delete/storage state assertion'larıyla doğrulandı. State validation handler pattern'ı (no-op defensive checks) `SampleStateValidatedTimeoutHandler` ile somut olarak gösterildi (5 senaryo).
- ✅ **09 §13.6 freeze/resume pattern uygulanmış mı?** — `FreezeResume_Pattern_DeletesOldJob_AndReschedulesNewOne` testi: schedule → delete (freeze) → yeni schedule (extended deadline) akışı end-to-end. Eski jobId `DeletedState`, yeni `ScheduledState` doğrulandı. Production T50'de business state güncellemesi (`TimeoutFrozenAt`, `PaymentDeadline` extension) bu altyapı üzerinde uygulanır.
- ✅ **Hangfire dashboard admin auth arkasında mı?** — `HangfireDashboardAuthFilter` `AuthClaimTypes.Role` claim'inde `Admin` veya `SuperAdmin` arar. Tests: anonim → 401, user role → 403, admin → 200, super admin → 200 (4/4 PASS). `DashboardOptions.DisplayStorageConnectionString = false` ile DB connection string dashboard'da gösterilmez.

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Doğrulama tarihi | 2026-04-07 |
| Validator | Claude (bağımsız spec conformance reviewer, ayrı validation chat) |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

**Validator özeti:** Tüm 7 kabul kriteri ✓, doğrulama kontrol listesi 3/3 ✓, HangfireTests 16/16 PASS, Skinora.API.Tests regresyon 79/79 PASS, Skinora.Shared.Tests 37/37 PASS, build 0 warning/0 error, güvenlik kontrolü temiz (secret yok, dashboard admin claim arkasında, `DisplayStorageConnectionString=false`). Yapım raporu ile bağımsız değerlendirme tam uyumlu — kriter 5/6 için "pattern tanımlı" yorumu doğru kabul edildi (T09 altyapı task'ı, business handler'lar T44+/T50'de gelecek).

## Altyapı Değişiklikleri

- **Migration:** Yok (Hangfire kendi schema'sını ilk job schedule'da `PrepareSchemaIfNecessary = true` ile otomatik kurar — F1'de eklenecek `HangFire` schema'sı T28 initial migration ile çakışmaz çünkü farklı schema'da).
- **Config/env değişikliği:** Var — `appsettings.json`'a `Hangfire` section eklendi. **Yeni env değişkeni gerekli değil**, mevcut `ConnectionStrings:DefaultConnection` (prod'da `DB_CONNECTION_STRING`) Hangfire tarafından da kullanılıyor.
- **Docker değişikliği:** Yok — backend container'ı zaten DB'ye bağlanıyor, Hangfire aynı bağlantıyı kullanıyor. Ek port yok (`/hangfire` mevcut backend portunun (5000) altında).

## Commit & PR

- **Branch:** `task/T09-hangfire-altyapisi`
- **Commit:** `9528930` — "T09: Hangfire setup ve background job altyapisi"
- **PR:** Yok (T11 öncesi — branch protection aktif değil, manuel doğrulama uygulandı)
- **CI:** T11 öncesi olduğundan branch protection aktif değil, validator manuel PASS verdi (bkz. Doğrulama bölümü).

## Known Limitations / Follow-up

- **Hangfire processing server API host'unda çalışıyor.** API container'ı hem web request'leri hem job worker'ları işliyor. Production'da yüksek hacim altında ayrı worker container'ına ayrılması gerekebilir. Bu T16 (monitoring) veya post-MVP'de değerlendirilecek; uygulama kodu değiştirilmeden `AddHangfireServer` ayrı bir host'a taşınabilir.
- **`Hangfire.SqlServer.PrepareSchemaIfNecessary = true`.** Hangfire kendi schema'sını ilk başlatmada oluşturuyor. Production'da bu DB user'ının `CREATE SCHEMA` yetkisine sahip olmasını gerektirir. T28 (initial migration) öncesi tek seferlik kurulum maliyeti — F1 başlangıcında tekrar değerlendirilecek.
- **Forwarded headers / HTTPS scheme:** Test'lerde HTTPS BaseAddress kullanmak zorunda kaldık çünkü Skinora'nın anti-forgery cookie config'i `SecurePolicy = Always`. Production'da Nginx HTTPS terminate edip backend'e HTTP iletiyorsa `UseForwardedHeaders` middleware'i gerekecek (mevcut değil). Bu Hangfire dashboard'a özgü değil, genel Skinora bir konu — T16 (reverse proxy / monitoring) kapsamında ele alınacak.
- **State validation pattern abstraction kasten yapılmadı.** Generic `StateValidatedJobBase<T>` gibi bir abstract base eklenmedi çünkü T44+ timeout handler'larının concrete entity ihtiyaçlarını henüz bilmiyoruz. Pattern referans implementation `SampleStateValidatedTimeoutHandler` ile gösterildi; T47'de Transaction-aware handler bu şablona uyacak. Speculative abstraction'dan kaçınıldı.
- **`ITimeoutFreezeCoordinator` benzer şekilde:** İskelet impl eklenmedi. T50 freeze/resume implementation'ı doğrudan `IBackgroundJobScheduler` üzerinde Transaction entity bilgisiyle yazılır.
- **Generic job logging filter (09 §13.5 "Her job başlangıç ve bitiş loglar"):** T09'da bir Hangfire `JobFilterAttribute` (Serilog enricher / `IServerFilter`) eklenmedi. T09 kabul kriterlerinde yer almıyor; concrete handler logging T44+ task'larında ele alınacak. Validator follow-up notu olarak kaydedildi — düşük maliyetli generic `LoggingJobFilter` eklenmesi sonraki bir altyapı task'ında değerlendirilebilir.

## Notlar

- **AutomaticRetryAttribute mutation pattern:** Hangfire 1.8 kütüphanesi `GlobalJobFilters` koleksiyonuna kendi default `AutomaticRetryAttribute(Attempts = 10)`'unu zaten ekliyor. `UseFilter(new AutomaticRetryAttribute { Attempts = 3 })` çağrısı bunu **kaldırmıyor**, yan yana iki filter oluşturuyor. Doğru çözüm: mevcut instance'ı bulup `Attempts` property'sini in-place mutate etmek. Bu HangfireModule'da, HangfireBypassFactory'de aynı şekilde uygulandı (test paritesi için).
- **Hangfire dashboard anti-forgery + `Cookie.SecurePolicy = Always`:** Hangfire dashboard her request'te kendi POST formları (job retry/delete) için `IAntiforgery.GetAndStoreTokens` çağırıyor; bu Skinora'nın anti-forgery config'inde `Cookie.SecurePolicy = Always` set edilmiş olduğu için HTTP request'lerinde `InvalidOperationException` fırlatıyor. Test'te HTTPS BaseAddress kullanılarak çözüldü (production'da dashboard zaten HTTPS arkasında çalışacak). Forwarded headers konfigürasyonu T16 kapsamında.
- **Test paralelizasyonu ve regression kırılması:** İlk denemelerde 9 test fail oldu (4 dakika çalışma süresi). Kök neden: production AddHangfireModule SqlServerStorage Initialize() çağrısı DB'ye bağlanmaya çalışıp 4 retry × 30s timeout uyguluyor, paralel test factory build'leri bu gecikmeyi catastrophically multipling yapıyor. Çözüm: `HangfireBypassFactory` ortak base eklendi, mevcut testler buna geçirildi. T08'in T05+ test pattern'ının (DbContext SQLite swap) Hangfire benzeri.
- **Skinora.Shared'ın temizliği korundu:** Hangfire bağımlılığı sadece Skinora.API'da. `IBackgroundJobScheduler` interface'i Skinora.Shared'da, sadece `System.Linq.Expressions` kullanır (BCL parçası, ek paket yok). T44+ modülleri Hangfire'a değil sadece Skinora.Shared'a bağımlı kalır.
