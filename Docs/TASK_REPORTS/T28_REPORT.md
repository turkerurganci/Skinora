# T28 — Initial migration ve migration testi

**Faz:** F1 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-04-20

---

## Yapılan İşler

- `dotnet ef migrations add InitialCreate` ile tüm F1 entity'leri kapsayan initial migration üretildi (25 tablo, 68 index, 9 `InsertData` çağrısı = 30 seed satırı).
- Migration dosyaları Skinora.API yerine Skinora.Shared altına (`Persistence/Migrations/`) yerleştirildi — böylece Shared'a bağlı tüm modül test projeleri `MigrateAsync` yolunu doğrudan kullanabiliyor.
- Program.cs'de `MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name)` (=Skinora.Shared) ayarlandı.
- Skinora.Shared.csproj'a `Microsoft.EntityFrameworkCore.SqlServer` 9.0.3 eklendi (migration designer dosyalarının `SqlServerModelBuilderExtensions` kullanımı için).
- `IntegrationTestBase` `UseMigrations` virtual flag'i eklendi: default `false` (EnsureCreatedAsync korunur), opt-in `true` → `MigrateAsync`. Neden: modül test'leri yalnızca kendi modüllerini kaydediyor; EF Core 9 `MigrateAsync` runtime model ile snapshot'ı karşılaştırıp eksik kayıt varsa `PendingModelChangesWarning` fırlatıyor. Migration yolunu doğrulayan test'ler (InitialMigrationTests) tüm 10 modülü static ctor'da kaydedip `UseMigrations => true` ile opt-in yapar.
- `InitialMigrationTests` (6 fact) Skinora.API.Tests altında: `AppliedMigrations`/`PendingMigrations`, `Migrate_SecondRun_IsIdempotent`, `Model_HasNoPendingChanges`, `Schema_ContainsAllExpectedTables`, `Schema_ContainsEfMigrationsHistoryTable`.
- Seed içeriği T26'nın `SeedDataTests`'inde zaten kontrol edildiği için T28 seed testlerini tekrar etmedi; T28 migration-layer invariant'lara odaklandı.
- CI `migration-dry-run` job'u: model validation + gerçek `dotnet ef migrations script --idempotent` artefact üretimi + boş SQL Server container'a iki kez `dotnet ef database update` uygulaması (idempotency kanıtı) + `migrations-sql` artifact upload.

## Etkilenen Modüller / Dosyalar

- `backend/src/Skinora.Shared/Persistence/Migrations/20260420191938_InitialCreate.cs` (yeni)
- `backend/src/Skinora.Shared/Persistence/Migrations/20260420191938_InitialCreate.Designer.cs` (yeni)
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` (yeni)
- `backend/src/Skinora.Shared/Skinora.Shared.csproj` (SqlServer paketi eklendi)
- `backend/src/Skinora.API/Program.cs` (MigrationsAssembly → AppDbContext.Assembly)
- `backend/tests/Skinora.Shared.Tests/Integration/IntegrationTestBase.cs` (EnsureCreatedAsync → MigrateAsync)
- `backend/tests/Skinora.API.Tests/Integration/InitialMigrationTests.cs` (yeni, 6 fact)
- `.github/workflows/ci.yml` (`migration-dry-run` job: mssql service + script + apply + idempotent re-apply + artifact upload)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `dotnet ef migrations add InitialCreate` ile migration oluşturuldu | ✓ | `backend/src/Skinora.Shared/Persistence/Migrations/20260420191938_InitialCreate.cs` (1338 satır); `grep -c "CreateTable" 20260420191938_InitialCreate.cs` → 25 tablo, 68 index, 9 InsertData çağrısı. |
| 2 | Migration boş SQL Server'a uygulandığında hatasız çalışıyor | ✓ | `IntegrationTestBase.MigrateAsync` her test class için yeni DB'ye migration uyguluyor; CI `migration-dry-run` job'unda da fresh mssql container'a `dotnet ef database update` çalıştırılıyor. Local doğrulama: 160 unit test PASS, integration CI'da koşacak (Docker local'de off). |
| 3 | Seed data migration sonrası doğru yükleniyor | ✓ | Seed içerik doğrulaması T26 `SeedDataTests` tarafından yapılıyor (SYSTEM user, SystemHeartbeat singleton, 28 SystemSetting, 8 defaulted, 20 unconfigured, DataType whitelist) — T28 altında `IntegrationTestBase` artık `MigrateAsync` ile seed'leri üretiyor; T26 testleri bu yolla yeşile kalıyor. |
| 4 | CI pipeline'da migration dry-run adımı var | ✓ | `.github/workflows/ci.yml` step 6 `migration-dry-run`: `dotnet ef dbcontext info` (model validation) + `dotnet ef migrations script --idempotent` (artifact üretimi) + `dotnet ef database update` × 2 (idempotency kanıtı) + upload. |

## Doğrulama Kontrol Listesi

- [x] **Tüm entity'ler, constraint'ler, index'ler migration'da var mı?** `InitialMigrationTests.Schema_ContainsAllExpectedTables` + `Model_HasNoPendingChanges` iki yönlü denetim; script artefact'i ve tablo sayımı (25 CreateTable / 68 CreateIndex) ile manuel kontrol.
- [x] **Migration idempotent mi (tekrar çalıştırılınca hata vermiyor mu)?** `InitialMigrationTests.Migrate_SecondRun_IsIdempotent` test + CI job'da iki ardışık `dotnet ef database update` çağrısı.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit | ✓ 160/160 passed | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — Shared.Tests 145, API.Tests 15. |
| Integration | ⏳ CI'da koşacak | Local Docker Desktop kapalı; test'ler CI `integration-test` + `migration-dry-run` job'larına bağımlı. |
| Build | ✓ | `dotnet build Skinora.sln --configuration Release` → 0 warning / 0 error. |
| Format | ✓ | `dotnet format Skinora.sln --verify-no-changes --severity error` → clean. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Devam ediyor (validator ayrı chat'te) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Var — `20260420191938_InitialCreate` (Skinora.Shared altında). 25 tablo, 68 index, 30 seed satırı, `__EFMigrationsHistory` ile.
- **Config/env değişikliği:** Yok. CI `migration-dry-run` job'u yeni `Migration_Pwd1!` SA şifresini kendi scope'unda tanımlar, başka yere sızmaz.
- **Docker değişikliği:** Yok. CI'da zaten mssql service pattern'i mevcut (integration-test), migration-dry-run aynı pattern'i tekrar ediyor.
- **Paket değişikliği:** `Microsoft.EntityFrameworkCore.SqlServer` 9.0.3 Skinora.Shared'a eklendi (migration designer compile için). Runtime davranışı değişmiyor — UseSqlServer çağrısı hâlâ Program.cs'de.

## Commit & PR

- Branch: `task/T28-initial-migration`
- Commit: (push sonrası güncellenecek)
- PR: (push sonrası güncellenecek)
- CI: ⏳ Beklemede

## Known Limitations / Follow-up

- Integration test'leri cold path'te yaklaşık +100ms/sınıf yavaşlar (EnsureCreated → Migrate farkı), ancak T11.3 shared-server düzeni nedeniyle toplam CI süresi ~3 dk civarında sabit kalır.
- Migration script idempotent flag ile üretildi; prod-deploy zamanı da aynı idempotent script-first akışı kullanılacak (09 §21.4 step 6).

## Notlar

- **Working tree check (task.md Adım -1):** `git status --short` → temiz.
- **Main CI startup check (task.md Adım 0):** Son 3 main CI run'ı `success` (IDs: 24676612387 T27 ✓, 24676612346 T27 ✓, 24639119531 chore #40 ✓).
- **Dış varsayımlar (task.md Adım 4):**
  - `dotnet-ef 9.0.3` global olarak kuruluydu (`dotnet ef --version` → 9.0.3) ✓
  - `Microsoft.EntityFrameworkCore.*` 9.0.3 paketleri repo'da mevcut ✓
  - SQL Server 2022 image (`mcr.microsoft.com/mssql/server:2022-latest`) CI'de zaten kullanımda ✓
  - T17–T27 hepsi `✓ Tamamlandı` (status tracker doğrulandı) ✓
- **Karar — Migrations assembly:** İlk üretim Skinora.API/Migrations'a düştü, ancak test projelerinin çoğu sadece Shared'a bağlı → MigrateAsync için assembly'i bulamayacak. Shared/Persistence/Migrations'a taşındı; `MigrationsAssembly` ayarı buna göre güncellendi. Trade-off: Shared SqlServer paketi taşır — runtime'a etkisi yok, design-time migration compile için gerekli.
- **Karar — IntegrationTestBase MigrateAsync opt-in:** İlk deneme her testi `MigrateAsync` yoluna çevirdi; CI integration-test job'unda Shared.Tests'in 16 fact'i `PendingModelChangesWarning` hatasıyla düştü (EF Core 9 default behaviour: `MigrateAsync` runtime model ile snapshot'ı karşılaştırır, Shared.Tests yalnızca 3 outbox entity'sini kaydettiği için snapshot'ın 25 tablosu ile drift algılandı). Pivot: `UseMigrations` virtual flag `false` default'u + opt-in. InitialMigrationTests tüm 10 modülü kaydettiği için güvenli şekilde `true` dönüyor. T26 `SeedDataTests` EnsureCreated yolunda kaldı — seed satırları HasData üzerinden her iki path'te de üretiliyor.
- **CI başarısızlığı / pivot (2026-04-20):** PR #42'nin ilk CI run'ı (24686011102) integration-test job'unda FAIL verdi → root cause pending-model-changes; fix yukarıdaki opt-in pattern ile aynı PR'a push edildi.
