# T04 — EF Core Global Konfigürasyon

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-06

---

## Yapılan İşler
- UtcDateTimeConverter oluşturuldu (ValueConverter<DateTime, DateTime>)
- AppDbContext oluşturuldu: ConfigureConventions, OnModelCreating, SaveChanges audit
- ConfigureConventions'da tüm DateTime property'lere UtcDateTimeConverter uygulandı
- ISoftDeletable entity'lere HasQueryFilter(e => !e.IsDeleted) global filter eklendi
- BaseEntity.RowVersion için IsRowVersion() konfigürasyonu yapıldı
- Tüm FK ilişkilerinde DeleteBehavior.NoAction zorunlu kılındı
- IAuditableEntity için SaveChangesAsync'de otomatik CreatedAt/UpdatedAt güncelleme
- Program.cs'de DbContext DI registration (SQL Server, MigrationsAssembly)
- Connection string konfigürasyonu (appsettings.json, appsettings.Development.json)
- 8 integration test yazıldı ve geçti

## Etkilenen Modüller / Dosyalar
- `src/Skinora.Shared/Persistence/AppDbContext.cs` (yeni)
- `src/Skinora.Shared/Persistence/Converters/UtcDateTimeConverter.cs` (yeni)
- `src/Skinora.Shared/Skinora.Shared.csproj` (EF Core 9.0.3 paketi eklendi)
- `src/Skinora.API/Program.cs` (DbContext DI registration)
- `src/Skinora.API/Skinora.API.csproj` (EF Core SqlServer + Tools paketleri)
- `src/Skinora.API/appsettings.json` (connection string)
- `src/Skinora.API/appsettings.Development.json` (dev connection string + EF log)
- `tests/Skinora.API.Tests/Skinora.API.Tests.csproj` (EF Core SQLite paketi)
- `tests/Skinora.API.Tests/Integration/EfCoreGlobalConfigTests.cs` (yeni)

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | UtcDateTimeConverter oluşturuldu, ConfigureConventions'da tüm DateTime'lara uygulandı | ✓ | `UtcDateTimeConverter.cs` + `AppDbContext.ConfigureConventions()` + `UtcConverter_ReadDateTime_ReturnsUtcKind` test PASS |
| 2 | Soft delete global query filter: HasQueryFilter(e => !e.IsDeleted) tüm ISoftDeletable entity'lerde | ✓ | `AppDbContext.OnModelCreating()` loop + `SoftDeleteFilter_ExcludesDeletedEntities` + `SoftDeleteFilter_IgnoreQueryFilters_ReturnsDeletedEntities` testleri PASS |
| 3 | RowVersion property base'de tanımlı, IsRowVersion() EF config'de | ✓ | `BaseEntity.RowVersion` (T03) + `AppDbContext.OnModelCreating()` IsRowVersion() + `RowVersion_IsConfigured_OnBaseEntity` test PASS |
| 4 | Tüm FK'lerde DeleteBehavior.NoAction zorunlu | ✓ | `AppDbContext.OnModelCreating()` loop + `ForeignKeys_HaveNoActionDeleteBehavior` test PASS |
| 5 | Nullable reference types aktif | ✓ | Tüm csproj'larda `<Nullable>enable</Nullable>` (T01'den beri mevcut) |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration | ✓ 8/8 passed | `dotnet test tests/Skinora.API.Tests/ --verbosity normal` — tüm testler geçti (3.38s) |

**Test listesi:**
1. `UtcConverter_ReadDateTime_ReturnsUtcKind` — DB'den okunan DateTime.Kind = Utc
2. `SoftDeleteFilter_ExcludesDeletedEntities` — IsDeleted=true kayıtlar filtreden geçmez
3. `SoftDeleteFilter_IgnoreQueryFilters_ReturnsDeletedEntities` — IgnoreQueryFilters ile silinmişler de gelir
4. `ForeignKeys_HaveNoActionDeleteBehavior` — Tüm FK'ler NoAction
5. `RowVersion_IsConfigured_OnBaseEntity` — RowVersion concurrency token olarak konfigüre
6. `AuditFields_SetAutomatically_OnAdd` — CreatedAt/UpdatedAt otomatik set
7. `AuditFields_UpdatedAt_ChangesOnModify` — Güncelleme'de sadece UpdatedAt değişir
8. `NonSoftDeletableEntity_NoQueryFilter_Applied` — ISoftDeletable olmayan entity'ye filter uygulanmaz

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

### Doğrulama Kontrol Listesi
- [x] 09 §7.1 UTC kuralı uygulanmış mı? — `UtcDateTimeConverter` + `ConfigureConventions` tüm `DateTime` property'lere uygulandı
- [x] 09 §10.6 cascade kuralı uygulanmış mı? — `OnModelCreating`'de tüm FK'lere `DeleteBehavior.NoAction` atandı
- [x] Soft delete query filter'ı IgnoreQueryFilters() olmadan silinmiş kayıtları getirmiyor mu? — `SoftDeleteFilter_ExcludesDeletedEntities` + `SoftDeleteFilter_IgnoreQueryFilters_ReturnsDeletedEntities` testleri PASS

### Güvenlik Kontrolü
- [x] Secret sızıntısı: Temiz — connection string yalnızca appsettings'te, hardcoded secret yok
- [x] Auth etkisi: Temiz — bu task auth katmanını etkilemiyor
- [x] Input validation: Temiz — bu task input almıyor
- [x] Yeni bağımlılık: `Microsoft.EntityFrameworkCore` 9.0.3 (Shared), `Microsoft.EntityFrameworkCore.SqlServer` + `Tools` (API), `Microsoft.EntityFrameworkCore.Sqlite` (test)

### Yapım Raporu Karşılaştırması
- Uyum: Tam uyumlu — yapım raporu tüm kabul kriterlerini doğru raporlamış, ek audit field yönetimi (bonus) dokümante edilmiş
- Uyuşmazlık: Yok

## Altyapı Değişiklikleri
- Migration: Yok (DbContext hazır, entity eklendikçe migration oluşturulacak)
- Config/env değişikliği: Connection string eklendi (appsettings.json + Development)
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T04-efcore-global-config`
- Commit: `188fdf6` — T04: EF Core global konfigürasyon
- PR: Doğrulama sonrası açılacak
- CI: N/A (T11'de kurulacak)

## Known Limitations / Follow-up
- AppDbContext şu an entity tanımı içermiyor — modül entity'leri T17+ task'larında `ApplyConfigurationsFromAssembly()` ile eklenecek
- Migration henüz yok — T28'de initial migration oluşturulacak
- Integration testler SQLite in-memory kullanıyor (SQL Server-specific RowVersion davranışı tam test edilemiyor — T12'de TestContainers ile gerçek SQL Server testi kurulacak)

## Notlar
- AppDbContext `Skinora.Shared/Persistence/` altına konuldu — modül repository'lerinin doğrudan erişebilmesi için (09 §10.2 kod örneğiyle uyumlu)
- SaveChanges override'ı IAuditableEntity audit field'larını otomatik yönetiyor — entity'lerin her seferinde manuel set etmesi gerekmiyor
