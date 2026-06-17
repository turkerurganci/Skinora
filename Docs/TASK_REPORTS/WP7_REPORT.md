# WP7 — Outage/maintenance (admin freeze/resume toggle)

**Faz:** Pre-F6 (P3 — Operasyon) | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-06-17

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
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsValidator.cs` (F4 — string `nvarchar(500)` max-length kuralı; AD9 + AD30 ortak)
- `Docs/07_API_DESIGN.md` (§9.31 AD30/AD31 + permission tablosu)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Admin `POST /admin/maintenance/freeze` → `FreezeManyAsync(reason)` | ✓ | `Freeze_PlatformMaintenance_...` 2 işlem `MAINTENANCE` reason ile dondu; `Freeze_SteamOutage_...` / `Freeze_BlockchainDegradation_...` scope-doğru |
| 2 | `/resume` → `ResumeManyAsync` (kalan süre korunur) | ✓ | `Resume_AfterFreeze_...` freeze trio temizlendi, `affectedTransactions=1`; `ResumeAsync` mevcut 05 §4.4 reschedule mantığını kullanır |
| 3 | `platform.maintenance.*` update'inde cache-evict + `PublishMaintenanceStatusChangedAsync` | ✓ | `PublicMaintenance_ReflectsFreezeState_AfterCacheEvict` (GET önce inactive → freeze → GET active); `DirectSettingEdit_...` (raw-key → push fired) |
| 4 | `PLANNED_MAINTENANCE` banner-only (freeze yok) | ✓ | `Freeze_PlannedMaintenance_...` `affectedTransactions=0`, işlem donmadı, banner+push aktif |
| 5 | Permission `MANAGE_SETTINGS` enforce | ✓ | `Freeze_Anonymous_Returns401`, `Freeze_AdminWithoutManageSettings_Returns403`, `Resume_..._Returns403` |
| 6 | Audit (05 §4.4 maintenance giriş/çıkış) | ✓ | `MAINTENANCE_MODE_CHANGED` satırı `EntityType=Maintenance`, ActorType=ADMIN; envelope `OldValue={settings:{4 ayar}}` / `NewValue={settings:{4 ayar}, affectedTransactions:N}` (07 §9.31 işlem sayısı dahil — F1 fix) |
| 7 | Atomiklik (banner ↔ freeze ayrışmaz) | ✓ | `ApplyAsync` tek explicit transaction; settings+audit+freeze tek commit |
| 8 | STEAM_OUTAGE/BLOCKCHAIN_DEGRADATION auto-detect | ⬚ WP16'ya ertelendi | Owner kararı — health-probe altyapısı WP16 kapsamı, eşik tanımsız (SPEC_GAP) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Integration (WP7) | ✓ 14/14 | `AdminMaintenanceEndpointTests` (freeze×4 tip + scope, resume×2, validation×3 [type/plannedEnd/**message-maxlen**], auth×3, cache-evict, raw-key refresh) |
| Unit (validator) | ✓ 70/70 | `SystemSettingsValidatorTests` (+2: string `nvarchar(500)` cap sınırı 500✓/501✗) |
| Integration (regresyon) | ✓ 507/507 | `dotnet test Skinora.API.Tests` tam suite |
| Unit (CI parite) | ✓ tümü yeşil | `dotnet test Skinora.sln -c Release --filter "FQN!~.Integration&FQN!~.Contract"` (Shared 363/363 + Platform 111/111 + Transactions 492 + Notifications 90 + Steam 28 + Realtime 25 + Fraud 18 + API 44) — ilk push'ta atlanmıştı, Unit-fix sonrası eklendi |
| Build | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| Format | ✓ clean | `dotnet format Skinora.sln --verify-no-changes --severity error` (exit 0) |
| Migration drift | ✓ yok | `dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model" |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız validator (ayrı chat, 2026-06-17, rapor görülmeden) |
| Bulgu sayısı | 0 bloke-edici (2 non-blocking gözlem) |
| Düzeltme gerekli mi | Hayır |

### Bağımsız Validator Sonucu (2026-06-17)

**Verdict: ✓ PASS** — kabul kriterleri 7/8 ✓ + 1 ~ (AC8 auto-detect owner-onaylı WP16 ertelemesi, bloke etmez), 0 bloke-edici bulgu.

**Kapılar:** Adım -1 working tree temiz · Adım 0 main son-3 success (`27686562037`/`27686562073`/`27679584356`) · Adım 0b memory WP7 satırı mevcut · Adım 8a task CI HEAD `f525ddf` run [`27709860380`](https://github.com/turkerurganci/Skinora/actions/runs/27709860380) **tüm job success**.

**Validator lokal koşumu (HEAD `f525ddf`):** API.Tests `~AdminMaintenance` **14/14** · Platform `~AuditLogCategoryMap|~SystemSettingsValidator` **106/106** · Shared `~EnumTests` **203/203** — hepsi pass. (Tam regresyon + gerçek SQL Server integration CI-authoritative; task CI yeşil.)

**Bağımsız teyitler:** tip→freeze map `FreezeReasonFor` + `TimeoutFreezeReasonScopes.For` 07 §10.2 birebir; atomiklik tek explicit DB transaction (settings flush → freeze/resume → audit → commit; cache-evict + push post-commit); F1 audit envelope `affectedTransactions` mevcut; F3 (test doc ref) + F4 (`nvarchar(500)` cap, AD9+AD30 ortak `ValidateSingle`) düzeltmeleri doğrulandı; 4 `platform.maintenance.*` ayarı `SystemSettingsCatalog` ile seeded; güvenlik temiz (MANAGE_SETTINGS + admin-write rate-limit, 0 dep, migration yok, drift yok).

**Non-blocking gözlemler:**
- **O1 — Tip-değiştirme stranding:** resume yapmadan tip değiştirilirse eski-reason donmuş tx'ler yeni-tip resume'da çözülmez → manuel eski-tip resume gerekir. Sonuç fazla-donma (güvenli taraf), para kaybı/erken-timeout yok; spec stacked-type semantiğini tanımlamıyor. Operasyonel kenar, MVP kabul edilebilir.
- **O2 — Tek-instance cache/push:** Bilinen `Program.cs:258` MVP kısıtı (Redis backplane §3 kapsamı dışı), 30 sn TTL ile sınırlı.

## Altyapı Değişiklikleri
- **Migration:** Yok (yalnız string-backed `AuditAction` değeri eklendi; ayarlar T63a'dan seeded; yeni entity/kolon/index yok). `has-pending-model-changes` drift yok ile teyit.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni dış bağımlılık:** Yok.

## Commit & PR
- Branch: `task/WP7-maintenance-toggle`
- Commit'ler: `8fba943` (özellik) · `0a5cbcb` (rapor/status/memory) · `3a36f2a` (CI Unit-fix — aşağıdaki Notlar)
- PR: #176
- CI: ✓ **PASS** — run [`27692508663`](https://github.com/turkerurganci/Skinora/actions/runs/27692508663) (HEAD `3a36f2a`) **tüm job success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate). Önceki run `27691506749` yalnız Unit job'da fail'di (aşağı bkz.).

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
- **F3 düzeltmesi (validator K-note):** `AdminMaintenanceEndpointTests` XML doc'unda yanlış doküman referansı `07 §10.3` (yok) → `07 §9.31` (admin endpoint'leri). Salt yorum.
- **F4 düzeltmesi (validator K-note → owner kararı: paylaşılan validator'da çöz):** `message` (ve tüm string ayarlar) için uzunluk doğrulaması yoktu → >500 karakter `SystemSetting.Value` `nvarchar(500)` kolonunda `SaveChanges` 500 fırlatırdı. `SystemSettingsValidator.TryValidateType` "string" dalına `MaxStringValueLength=500` kuralı eklendi (kolon genişliğiyle birebir) → hem AD9 (`SystemSettingsService.UpdateAsync` zaten `ValidateSingle` çağırır) hem AD30 (`FreezeAsync` artık `message`'ı da `ValidateSingle`'dan geçirir) temiz **400 VALIDATION_ERROR** döner. Testler: validator unit +2 (500✓/501✗), WP7 integration +1 (`Freeze_MessageExceedsMaxLength_Returns400` — SQLite kolon genişliğini yok saydığından bu test DB'yi değil **validator kapısını** kanıtlar).
- **F1 düzeltmesi (bağımsız validator turu, 2026-06-17):** Validator, audit `Old/NewValue`'nun 07 §9.31'in (+ `AuditAction` yorumunun) vaat ettiği **işlem sayısını** içermediğini tespit etti — count, audit satırı yazıldıktan *sonra* hesaplanıyordu. Owner kararı (AskUserQuestion): count'u audit'e ekle + re-validate. Fix: `AdminMaintenanceService.ApplyAsync` sıralaması değişti (settings flush → freeze/resume → **audit**, hepsi tek transaction'da → atomiklik korunur) ve envelope `{ settings, affectedTransactions }` yapısına geçti; freeze (AD30, count=2) + resume (AD31, count=1) testlerine `affectedTransactions` assertion eklendi. Doküman/yorum değişmedi (zaten count'u söylüyordu). Re-validation ayrı chat'te.
- **Working tree:** Adım -1 temiz.
- **Main CI startup (Adım 0):** son 3 run success (`27686562037` WP6 / `27686562073` WP6 / `27679584356` WP5).
- **Owner kararları (AskUserQuestion 2026-06-17):** (1) auto-detect → WP16'ya ertele (manuel-only) · (2) birleşik atomik set/clear endpoint · (3) MANAGE_SETTINGS reuse.
- **Atomiklik kararı:** `FreezeManyAsync` count==0'da kendi SaveChanges'ini atlar → staged ayarları flush etmez; bu yüzden explicit DB transaction ile sarıldı (settings SaveChanges + bulk freeze aynı transaction'da commit) — hem count==0 hem freeze-failure split-brain'ini kapatır.
- **CI Unit-fix (`3a36f2a`):** İlk push (`0a5cbcb`) CI'ı **yalnız Unit job'da** fail'di — yeni `AuditAction.MAINTENANCE_MODE_CHANGED` değeri iki **kardeş test projesindeki** exact-count parity guard'ını bozdu: (1) `Skinora.Shared.Tests.EnumTests` `AuditAction_ShouldHave28Values` (→29 + value theory), (2) `Skinora.Platform.Tests.AuditLogCategoryMapTests` `Every_AuditAction_Has_A_Category` — `AuditLogCategoryMap.CategoryFor` eşlenmemiş değerde **throw** ediyordu (yalnız test değil, `GET /admin/audit-logs` kategorize yolunda **latent prod bug**). Fix: yeni değer `AuditLogCategoryMap`'te `ADMIN_ACTION`'a eşlendi (SYSTEM_SETTING_CHANGED yanı) + iki parity testi (count 28→29, AdminAction 15→16). Ders: enum değeri eklerken push'tan önce tam Unit-filter suite koşulmalı (parity guard'ları başka projelerde); ilk push'ta yalnız `Skinora.API.Tests` koşulmuştu (Integration job → etkilenmedi).
- **BYPASS_LOG:** `3a36f2a` push'u Layer-2 (son CI run failure) guard'ına takıldı; **fix-for-the-red-run** olduğundan `SKINORA_ALLOW_DIRECT_PUSH=1` ile geçildi (BYPASS_LOG.md otomatik `ci-failure` kaydı, bu commit ile commit'lendi).
