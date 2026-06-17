# WP7 — Outage/maintenance (admin freeze/resume toggle)

**Faz:** Pre-F6 (P3 — Operasyon) | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız validator bekliyor) | **Tarih:** 2026-06-17

---

## Yapılan İşler

WP7, **uçtan uca bağlı ama hiç çağrılmayan** bakım/kesinti kontrol yüzeyini aktive eder. Frontend (`MaintenanceBanner` + `RealtimeProvider` SignalR aboneliği) zaten hazırdı → **salt backend wiring**.

Kapatılan boşluklar:
- `TimeoutFreezeService.FreezeManyAsync` / `ResumeManyAsync` → **0 prod çağıran** (yalnız test).
- `SignalRNotificationRealtimePublisher.PublishMaintenanceStatusChangedAsync` → **0 çağıran**.
- `SystemSettingsService.UpdateAsync` (`PUT /admin/settings`) → `platform.maintenance.*` güncellemesinde cache-evict + push yoktu.

Uygulama:
1. **`AdminMaintenanceController`** — `POST /admin/maintenance/freeze` + `/resume`, `MANAGE_SETTINGS` permission (07 §9.31 AD30/AD31).
2. **`AdminMaintenanceService`** — tek **atomik** işlem (tek explicit DB transaction):
   - dört `platform.maintenance.*` ayarını yazar (banner read-model),
   - tipe göre `FreezeManyAsync`/`ResumeManyAsync` ile aktif işlemlerin timeout'larını topluca dondurur/çözer,
   - 30 sn public cache'i invalidate eder (`PlatformPublicService.MaintenanceCacheKey`),
   - RT2 `MaintenanceStatusChanged` push'unu yayınlar,
   - `MAINTENANCE_MODE_CHANGED` audit satırı yazar.
   - **Banner ↔ freeze durumu asla ayrışmaz** (no split-brain). `tip → freeze` eşlemesi 07 §10.2 birebir: `PLATFORM_MAINTENANCE`→tümü (`MAINTENANCE`), `STEAM_OUTAGE`→Steam-bağımlı, `BLOCKCHAIN_DEGRADATION`→ödeme adımı, `PLANNED_MAINTENANCE`→freeze yok (yalnız banner).
3. **Generic `PUT /admin/settings/:key`** — `platform.maintenance.*` anahtarında artık cache-evict + re-broadcast (`RefreshPublicStateAsync`); banner stale kalmaz. Freeze **bilinçli olarak** raw-key yolundan tetiklenmez (freeze AD30/AD31'e özgü).
4. **`AuditAction.MAINTENANCE_MODE_CHANGED`** — string-backed enum (no schema change).

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Skinora.API/Services/IAdminMaintenanceService.cs` (interface + DTO'lar: `MaintenanceFreezeRequest`, `MaintenanceStateDto`, `MaintenanceOperationOutcome`)
- `backend/src/Skinora.API/Services/AdminMaintenanceService.cs`
- `backend/src/Skinora.API/Controllers/AdminMaintenanceController.cs`
- `backend/tests/Skinora.API.Tests/Integration/AdminMaintenanceEndpointTests.cs` (13 test)

**Değişen:**
- `backend/src/Skinora.Shared/Enums/AuditAction.cs` (+`MAINTENANCE_MODE_CHANGED`)
- `backend/src/Skinora.API/Controllers/AdminController.cs` (`UpdateSetting` → maintenance-key refresh; ctor +`IAdminMaintenanceService`)
- `backend/src/Skinora.API/Program.cs` (DI kaydı)
- `Docs/07_API_DESIGN.md` (§9.31 AD30/AD31 + permission tablosu)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Admin `POST /admin/maintenance/freeze` → `FreezeManyAsync(reason)` | ✓ | `Freeze_PlatformMaintenance_...` 2 işlem `MAINTENANCE` reason ile dondu; `Freeze_SteamOutage_...` / `Freeze_BlockchainDegradation_...` scope-doğru |
| 2 | `/resume` → `ResumeManyAsync` (kalan süre korunur) | ✓ | `Resume_AfterFreeze_...` freeze trio temizlendi, `affectedTransactions=1`; `ResumeAsync` mevcut 05 §4.4 reschedule mantığını kullanır |
| 3 | `platform.maintenance.*` update'inde cache-evict + `PublishMaintenanceStatusChangedAsync` | ✓ | `PublicMaintenance_ReflectsFreezeState_AfterCacheEvict` (GET önce inactive → freeze → GET active); `DirectSettingEdit_...` (raw-key → push fired) |
| 4 | `PLANNED_MAINTENANCE` banner-only (freeze yok) | ✓ | `Freeze_PlannedMaintenance_...` `affectedTransactions=0`, işlem donmadı, banner+push aktif |
| 5 | Permission `MANAGE_SETTINGS` enforce | ✓ | `Freeze_Anonymous_Returns401`, `Freeze_AdminWithoutManageSettings_Returns403`, `Resume_..._Returns403` |
| 6 | Audit (05 §4.4 maintenance giriş/çıkış) | ✓ | `MAINTENANCE_MODE_CHANGED` satırı `EntityType=Maintenance`, ActorType=ADMIN |
| 7 | Atomiklik (banner ↔ freeze ayrışmaz) | ✓ | `ApplyAsync` tek explicit transaction; settings+audit+freeze tek commit |
| 8 | STEAM_OUTAGE/BLOCKCHAIN_DEGRADATION auto-detect | ⬚ WP16'ya ertelendi | Owner kararı — health-probe altyapısı WP16 kapsamı, eşik tanımsız (SPEC_GAP) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Integration (WP7) | ✓ 13/13 | `AdminMaintenanceEndpointTests` (freeze×4 tip + scope, resume×2, validation×2, auth×3, cache-evict, raw-key refresh) |
| Integration (regresyon) | ✓ 507/507 | `dotnet test Skinora.API.Tests` tam suite |
| Build | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| Format | ✓ clean | `dotnet format Skinora.sln --verify-no-changes --severity error` (exit 0) |
| Migration drift | ✓ yok | `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model" |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- **Migration:** Yok (yalnız string-backed `AuditAction` değeri eklendi; ayarlar T63a'dan seeded; yeni entity/kolon/index yok). `has-pending-model-changes` drift yok ile teyit.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni dış bağımlılık:** Yok.

## Commit & PR
- Branch: `task/WP7-maintenance-toggle`
- Commit: `8fba943` — WP7: Outage/maintenance — admin freeze/resume toggle + push/cache + audit
- PR: #176
- CI: ⏳ izleniyor

## Dış Varsayımlar (Ön-uçuş)
- `FreezeManyAsync`/`ResumeManyAsync` mevcut + çalışır (T50, testlerle teyit) ✓
- `PublishMaintenanceStatusChangedAsync` + FE aboneliği mevcut (T62) ✓
- 4 `platform.maintenance.*` ayarı seeded + validator (T63a) ✓
- Migration-free ulaşılabilir (MANAGE_SETTINGS reuse + yeni entity yok) ✓
- Paid-feature / plan-tier / yeni paket varsayımı: **yok** ✓

## Known Limitations / Follow-up
- **Auto-detect (STEAM_OUTAGE/BLOCKCHAIN_DEGRADATION health-check):** WP16'ya ertelendi (owner kararı) — health-probe altyapısı WP16 kapsamı; bu endpoint'ler hazır, WP16 üstüne biner.
- **Raw-key freeze divergence:** `PUT /admin/settings/platform.maintenance.*` cache-evict + push yapar ama freeze tetiklemez. Bakıma giriş/çıkış için AD30/AD31 önerilir. `active=true`+`type=NONE` cross-key invariant ile zaten reddedilir.
- **`suspend-signalr` canlı force-restrict:** MVP-dışı (07 §9.22a), bilinçli hariç.
- **Tek-instance cache:** 30 sn cache per-replica; cross-replica invalidation MVP-dışı (Program.cs:258 notu) — tek-instance MVP için yeterli.

## Notlar
- **Working tree:** Adım -1 temiz.
- **Main CI startup (Adım 0):** son 3 run success (`27686562037` WP6 / `27686562073` WP6 / `27679584356` WP5).
- **Owner kararları (AskUserQuestion 2026-06-17):** (1) auto-detect → WP16'ya ertele (manuel-only) · (2) birleşik atomik set/clear endpoint · (3) MANAGE_SETTINGS reuse.
- **Atomiklik kararı:** `FreezeManyAsync` count==0'da kendi SaveChanges'ini atlar → staged ayarları flush etmez; bu yüzden explicit DB transaction ile sarıldı (settings SaveChanges + bulk freeze aynı transaction'da commit) — hem count==0 hem freeze-failure split-brain'ini kapatır.
