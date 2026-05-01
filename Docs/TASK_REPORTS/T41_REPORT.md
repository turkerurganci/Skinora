# T41 — Admin parametre yönetimi

**Faz:** F2 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-05-01

---

## Yapılan İşler

- 07 §9.8 `GET /admin/settings` ve §9.9 `PUT /admin/settings/:key` endpoint'leri `AdminController` üzerine eklendi (`Permission:MANAGE_SETTINGS` policy gate, `admin-read` / `admin-write` rate-limit kovaları).
- `Skinora.Platform/Application/Settings/` paketi: `ISystemSettingsService` + `SystemSettingsService` (List + Update orchestration), `SystemSettingsCatalog` (per-key metadata: API kategori dialect 07 §9.8'e göre lowercase + `label` + `unit` + `valueType` mapping; 32 entry seed ile 1:1), `SystemSettingsValidator` (3-aşamalı tip + range + cross-key validator), DTO + outcome record'ları, `SettingsErrorCodes` (`SETTING_NOT_FOUND` 404 / `VALIDATION_ERROR` 400).
- Audit log kaydı: `SystemSettingsService.UpdateAsync` her başarılı update'te `AuditAction.SYSTEM_SETTING_CHANGED` row'u yazar (`ActorType=ADMIN`, `ActorId`+`UserId`=admin, `EntityType="SystemSetting"`, `EntityId=key`, `OldValue`/`NewValue` JSON, IP HttpContext'ten). Append-only guard'a (`AppDbContext.EnforceAppendOnly`) uyumlu — insert allow.
- `SettingsBootstrapService` (T26): tip validation `SystemSettingsValidator.ValidateSingle`'a delege edildi + post-hydration `ValidateCrossKey` adımı eklendi. Artık env var ile range-bozuk veya cross-key invariant ihlali olan kombinasyon startup'ta fail-fast.
- `PlatformModule.AddPlatformModule()` DI extension yazıldı; `Program.cs`'te `AddAdminModule()` sonrasında kayıt edildi.
- Mevcut `PermissionCatalog` ve `PermissionAuthorizationHandler` hiç değiştirilmedi — `MANAGE_SETTINGS` zaten T39'da catalog'daydı.

## Etkilenen Modüller / Dosyalar

**Yeni (8):**
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs`
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsValidator.cs`
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsService.cs`
- `backend/src/Modules/Skinora.Platform/Application/Settings/ISystemSettingsService.cs`
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingDtos.cs`
- `backend/src/Modules/Skinora.Platform/Application/Settings/SettingsErrorCodes.cs`
- `backend/src/Modules/Skinora.Platform/PlatformModule.cs`
- `backend/tests/Skinora.Platform.Tests/Unit/Settings/SystemSettingsCatalogTests.cs`
- `backend/tests/Skinora.Platform.Tests/Unit/Settings/SystemSettingsValidatorTests.cs`
- `backend/tests/Skinora.Platform.Tests/Integration/SystemSettingsServiceTests.cs`
- `backend/tests/Skinora.API.Tests/Integration/AdminSettingsEndpointTests.cs`

**Değişen (3):**
- `backend/src/Skinora.API/Controllers/AdminController.cs` — `ListSettings` + `UpdateSetting` endpoint'leri, `ISystemSettingsService` ctor inject.
- `backend/src/Skinora.API/Program.cs` — `Skinora.Platform` using + `AddPlatformModule()` kaydı.
- `backend/src/Modules/Skinora.Platform/Infrastructure/Bootstrap/SettingsBootstrapService.cs` — validator'a delege + cross-key fail-fast.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /admin/settings` → tüm platform parametreleri | ✓ | `AdminSettingsEndpointTests.ListSettings_AdminWithManageSettings_Returns200WithCatalogPayload`; `SystemSettingsServiceTests.ListAsync_Returns_Catalog_Entries_Mapped_To_Seed_Values` (32 catalog entry × 32 seed key, 1:1 doğrulandı). |
| 2 | `PUT /admin/settings/:key` → tek parametre güncelleme | ✓ | `AdminSettingsEndpointTests.UpdateSetting_AdminWithManageSettings_Returns200_And_Persists_Audit`; `SystemSettingsServiceTests.UpdateAsync_Configured_Setting_Updates_Value_And_Writes_Audit`. |
| 3 | Parametre değişikliği anında aktif olur, aktif işlemleri etkilemez | ✓ kısmi | "Anında aktif" kısmı: hiçbir cache yok — `SystemSettingsService.ListAsync` her çağrıda DB'den okur (test: aynı request içinde update + list çağrısı yeni değeri görür). "Aktif işlemleri etkilemez" kısmı T44+ sorumluluğu (transaction state machine snapshot semantik); T41'in bu güvenceyi bozacak değişikliği yok. Forward-devir Notlar bölümünde belgeli. |
| 4 | AuditLog kaydı oluşturulur | ✓ | `SystemSettingsServiceTests.UpdateAsync_Configured_Setting_Updates_Value_And_Writes_Audit` + `AdminSettingsEndpointTests.UpdateSetting_AdminWithManageSettings_Returns200_And_Persists_Audit` — `SYSTEM_SETTING_CHANGED` row + ActorId + IP + JSON old/new. Validation hatasında audit yazılmaz: `UpdateAsync_BadType_Returns_ValidationFailed_And_No_Audit_Written`. |
| 5 | Tüm 02 §16.2'deki parametreler yönetilebilir | ✓ | `SystemSettingsCatalogTests.Catalog_Covers_Every_Seeded_Key` — 32 seed key (02 §16.2 parametre satırlarının tamamını kapsar) catalog'da. Yönetim aksiyonları (Steam hesap durumu, flag/emergency hold, audit log görüntüleme) ayrı task'lara devirli (T59/T63/T100/T101/T103/T106) — scope onaylandı. |

## Doğrulama Kontrol Listesi

| # | Madde | Sonuç |
|---|---|---|
| 1 | 02 §16.2'deki tüm parametre satırları mevcut mu? | ✓ — seed 32 key (T26 + T30 + T34) 02 §16.2'nin **parametre** satırlarını tam karşılar; `SystemSettingsCatalogTests.Catalog_Covers_Every_Seeded_Key` 1:1 guard. |
| 2 | 07 §9.8–§9.9 sözleşmeleri doğru mu? | ✓ — DTO alanları (`key/value/category/label/description/unit/valueType` + `key/value/updatedAt`), error code'lar (`SETTING_NOT_FOUND`/`VALIDATION_ERROR`), HTTP status (200/400/404), `MANAGE_SETTINGS` permission. `AdminSettingsEndpointTests` 8 senaryoda kapsanır. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (T41 yeni) | ✓ 54/54 | `dotnet test tests/Skinora.Platform.Tests --filter "FullyQualifiedName~SystemSettingsCatalogTests\|FullyQualifiedName~SystemSettingsValidatorTests"` — 12 catalog + 42 validator (Theory case'ler). |
| Integration (T41 yeni service) | ✓ 7/7 | `SystemSettingsServiceTests` — list + update success/notFound/badType/range/crossKey/unconfigured-hydrate (real SQL Server fixture). |
| Integration (T41 yeni endpoint) | ✓ 8/8 | `AdminSettingsEndpointTests` — 401 anon + 403 user/no-perm + 200 admin/super-admin + 200 update + 404 unknown + 400 validation + 403 missing perm. |
| Tüm Skinora.Platform.Tests | ✓ 89/89 | 20 s |
| Tüm Skinora.API.Tests | ✓ 239/239 | 3 m 21 s |
| Solution toplam | ✓ tümü PASS (regresyon yok) | Admin 20 / Notifications 63 / Disputes 11 / Platform 89 / Auth 93 / Fraud 12 / Steam 21 / Shared 166 / Transactions 68 / API 239 + diğer modüller. |
| Build (Release) | ✓ 0W/0E | `dotnet build -c Release` |
| Format | ✓ temiz | `dotnet format --verify-no-changes` çıktı yok. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (bağımsız validator chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok — yeni entity/column yok. `dotnet ef migrations has-pending-model-changes` "No changes have been made to the model since the last migration." döndü.
- **Config/env:** Yok — yeni env var yok. T26'nın `SKINORA_SETTING_*` sözleşmesi korundu; bootstrap artık range + cross-key validation'ı da uyguluyor (env var ile geçersiz değer enjekte etmek artık startup'ta fail-fast).
- **Docker:** Yok.
- **Yeni NuGet paketi:** Yok.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok.
- **Auth/AuthZ:** İki yeni endpoint `[Authorize(Policy = "Permission:MANAGE_SETTINGS")]` arkasında. Anonymous → 401; non-admin → 403; admin without permission → 403; super-admin bypass via `PermissionAuthorizationHandler`. 8 entegrasyon testiyle doğrulandı.
- **Input validation:** Admin update yolu `SystemSettingsValidator` üzerinden geçer (tip + range + cross-key). String CSV (`auth.banned_countries`) yalnız ISO-3166-1 alpha-2 veya `NONE` literal'i kabul eder. Bool case-insensitive normalize. Ratio-key'ler `0 < x < 1` zorlanır.
- **IP capture:** `HttpContext.Connection.RemoteIpAddress` — header trust yok.
- **Audit immutability:** AuditLog row INSERT, mevcut `EnforceAppendOnly` guard tarafından korunur (UPDATE/DELETE kabul etmez).
- **Yeni dış bağımlılık:** Yok.

## Commit & PR

- **Branch:** `task/T41-admin-settings`
- **Commit:** (yapım sonrası tek commit ile push'lanacak, hash branch push sonrası eklenir)
- **PR:** (yapım sonrası açılacak; numara eklenecek)
- **CI:** (run ID push sonrası watch edilecek)

## Known Limitations / Follow-up

- **Aktif işlemleri etkilemez** kabul kriterinin bağlayıcı semantiği T44+ transaction state machine'in snapshot field'larıyla (mevcut transaction CREATE-time'da komisyon/timeout vb. snapshot'lar). T41 sadece settings CRUD'unu açar; bu senaryoyu test edecek transaction servisleri henüz yok.
- **Merkezi `IAuditLogger` (T42 devri):** `SystemSettingsService` bugün `AppDbContext.Set<AuditLog>().Add(...)` yapıyor. T42 merkezi service'i wire edince call site sadece `IAuditLogger.LogAsync(...)` çağrısına değişir — entity şeması ve alanlar aynı.
- **Cross-key kuralları seti minimal:** Şu an `payment_timeout_min < default < max` ve `monitoring 24h < 7d < 30d` doğrulanıyor. Doc-spec'te (06 §3.17) açıkça listelenenler bu ikisi. İleride yeni invariant gelirse `SystemSettingsValidator.ValidateCrossKey` tek noktada genişler.
- **Bootstrap range validation davranış değişikliği:** T26 sadece tip parse ediyordu; T41 sonrası env var ile `commission_rate=1.5` veya `payment_timeout_min > max` deneyen deploy startup'ta fail-fast. Bilinçli sıkılaştırma — admin UI'dan kabul edilmeyecek değer artık env'den de kabul edilmiyor (security by parity).
- **02 §16.2'nin "yönetim aksiyonları" satırları:** Steam hesap durum izleme T103, flag yönetimi T100, emergency hold yönetimi T59, audit log görüntüleme T42/T106 devirli. T41 yalnız parametre yönetimini kapsar.

## Notlar

- **Working tree (Adım -1):** Temiz başladı.
- **Main CI startup check (Adım 0):** Son 3 main run'ı `success` (run id'leri `25223615598`, `25223615596`, `25223108325`).
- **Dış varsayımlar (Adım 4):** Yok — T26 SystemSetting infrastructure + T39 admin pattern + T40 RBAC enforcement zaten yerinde, T41 sadece üstüne ekleme yapıyor. Yeni dış servis/paket/API yok.
- **Scope onayı (üç karar noktası):** Kullanıcı 2026-05-01 onayladı:
  1. Yalnız parametre satırları (yönetim aksiyonları başka task'lara).
  2. AuditLog `AppDbContext.Set<AuditLog>().Add` ile direkt yazılır (T42 sonrası merkezi service'e refactor).
  3. Cross-key + range validation hem update'te hem bootstrap'ta uygulanır.
- **`min_refund_threshold_ratio` özel kuralı:** Çoğu ratio-key `0 < x < 1` ama bu bir multiplier (default 2.0 — "iade < gas fee × 2.0 ise iade yapılmaz"); validator'da ayrı branch + yorum.
- **AdminController + ApiResponseWrapperFilter:** Endpoint'ler `Ok(result)` döner; filter `{ success, data: <result>, traceId }` envelope'una sarar. Test assert'leri `data` katmanını peel eder (ilk koşuda eksikti, fix'lendi).
