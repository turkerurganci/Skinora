# WP14 — Settings propagasyon + 19 zorunlu ayar (runbook)

**Faz:** Pre-F6 | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-06-19

---

## Yapılan İşler

WP14'ün üç iş kolu, owner kararları (AskUserQuestion 2026-06-19: B=runbook+env parity, C=runbook) doğrultusunda:

1. **Cron job runtime re-register (KOD).** `SystemSettingsService.UpdateAsync` artık DB+audit yazımından sonra bir `ISettingChangePropagator.PropagateAsync` çağırıyor. API host'un implementasyonu (`CronSettingChangePropagator`) değişen anahtarı bir `ICronJobReconfigurer`'a eşliyor; eşleşen registrar (`ReconciliationJobRegistrar` / `HotWalletMonitorJobRegistrar`) Hangfire recurring job'unu yeni cron ile **restart'sız** re-register ediyor. Önceden `reconciliation.schedule_cron` ve `hot_wallet.monitor_cron` yalnız `StartAsync`'te bir kez okunuyordu (seed yorumu: "host restart gerekir / T96 devir").

2. **Cron-syntax validation (KOD — tamlık).** `SystemSettingsValidator` iki cron anahtarı için Cronos ile ifadeyi parse ediyor (5-field standard veya 6-field seconds); geçersiz cron **400 VALIDATION_ERROR** ile reddediliyor — hem admin update path'inde hem env-var bootstrap'ında. Böylece yarım-uygulama (DB'ye yazılıp Hangfire'da sessizce patlama) önleniyor; re-register'a ulaşan değer her zaman Hangfire'ın kabul edeceği bir değer.

3. **Sidecar cadence/sweep → env parity + runbook (DOC).** Owner kararı runtime push/pull DEĞİL: `monitoring_post_cancel_*` + `blockchain.sweep_*` sidecar env'inden okunur, backend SystemSetting kopyası admin-görünür ama runtime'a yansımaz; değişim sidecar env + restart gerektirir. `Docs/DEPLOY_RUNBOOK.md §D` + `.env.example`.

4. **19 zorunlu ayar → deploy runbook (DOC).** Owner kararı seed-default DEĞİL: fail-fast (06 §8.9) iş-kritik değerler için bilinçli korundu. 19 `SKINORA_SETTING_*` env var'ı `Docs/DEPLOY_RUNBOOK.md §A` + `.env.example`'da belgelendi. (Plan "21" diyordu — WP4a `price_deviation_threshold` + WP12 `timeout_warning_ratio` seed-default ile düşürdü → gerçek **19**; `SettingsBootstrapTests` ile teyit.)

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Skinora.Shared/BackgroundJobs/ICronJobReconfigurer.cs` — registrar re-register seam'i
- `backend/src/Modules/Skinora.Platform/Application/Settings/ISettingChangePropagator.cs` — propagasyon hook'u + `NoOpSettingChangePropagator`
- `backend/src/Skinora.API/BackgroundJobs/CronSettingChangePropagator.cs` — API host impl (key→reconfigurer routing, best-effort)
- `backend/tests/Skinora.API.Tests/Unit/BackgroundJobs/CronSettingChangePropagatorTests.cs`
- `backend/tests/Skinora.API.Tests/Unit/BackgroundJobs/CronJobReconfigurerTests.cs`
- `Docs/DEPLOY_RUNBOOK.md`

**Değişen:**
- `backend/src/Modules/Skinora.Platform/Skinora.Platform.csproj` — `Cronos` 0.8.4 PackageReference (cron validation)
- `.../Platform/Application/Settings/SystemSettingsValidator.cs` — `IsCronKey` + `TryValidateCron`
- `.../Platform/Application/Settings/SystemSettingsService.cs` — propagator inject + post-save `PropagateAsync`
- `.../Platform/PlatformModule.cs` — `TryAddSingleton` NoOp default
- `.../Skinora.API/Services/Reconciliation/ReconciliationJobRegistrar.cs` — `ICronJobReconfigurer` impl + `Reconfigure`
- `.../Skinora.API/Services/HotWallet/HotWalletMonitorJobRegistrar.cs` — aynı
- `.../Skinora.API/Configuration/TransactionsModule.cs` — registrar singleton + `ICronJobReconfigurer` exposure + `Replace` real propagator
- `backend/tests/.../SystemSettingsValidatorTests.cs` — 7 cron InlineData
- `backend/tests/.../SystemSettingsServiceTests.cs` — spy propagator + cron save/invalid + no-propagate-on-invalid
- `backend/tests/.../AdminSettingsEndpointTests.cs` — cron 400 + DI composition wiring
- `.env.example` — `SKINORA_SETTING_*` (19) + sidecar cadence/sweep + `INTERNAL_KEY`
- `Docs/PRE_F6_PLAN.md`, `Docs/DEFERRED_BACKLOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Ayar değişimi yansır (cron) | ✓ | Admin cron değişimi → `CronSettingChangePropagator` → registrar `Reconfigure` → `AddOrUpdateRecurring`; `CronJobReconfigurerTests` (jobId+cron doğru), `SystemSettingsServiceTests` (propagator post-save çağrılır) |
| 2 | Geçersiz cron reddi | ✓ | `SystemSettingsValidatorTests` (7 case) + `AdminSettingsEndpointTests.UpdateSetting_CronKey_InvalidExpression_Returns400` |
| 3 | Sidecar cadence/sweep prod açılır (parity belgeli) | ✓ | `DEPLOY_RUNBOOK §D` + `.env.example` sidecar bloğu (owner kararı: runtime propagasyon post-MVP) |
| 4 | 19 zorunlu ayar prod açılır (runbook) | ✓ | `DEPLOY_RUNBOOK §A` 19 env + `.env.example`; fail-fast korundu (seed-default değil) |
| 5 | DI composition doğru | ✓ | `AdminSettingsEndpointTests.Wiring_RegistersBothCronReconfigurers_AndRealPropagator` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build (solution) | ✓ 0W/0E | `dotnet build Skinora.sln -c Debug` |
| Format | ✓ exit 0 | `dotnet format Skinora.sln --verify-no-changes` |
| Platform Unit (validator) | ✓ 77/77 | `--filter SystemSettingsValidatorTests` (+7 cron case) |
| API Unit (BackgroundJobs) | ✓ 5/5 | propagator routing/swallow (3) + registrar reconfigure (2) |
| API Hangfire+Reconciliation+HotWallet+BackgroundJobs | ✓ 50/50 | `--filter` birleşik |
| AdminSettings endpoint sınıfı | ✓ 10/10 | 8 mevcut + cron 400 + wiring (SQLite in-memory) |
| Platform Integration (SystemSettingsServiceTests) | ⏳ CI | SQL Server gerektirir (lokal Docker yok) — spy/cron/no-propagate testleri CI-authoritative |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** YOK (cron re-register + runbook; seed-default seçilmedi → şema/seed değişmedi). §4 "WP14 migration-taşımaz" ile tutarlı.
- **Config/env:** `.env.example` genişletildi (19 `SKINORA_SETTING_*` + sidecar cadence/sweep + `INTERNAL_KEY`). Yeni runtime env zorunluluğu eklenmedi — zaten var olan zorunlular belgelendi.
- **Yeni dependency:** `Cronos` 0.8.4 (Platform). Reputable (Hangfire'ın embed ettiği parser), 0 transitive dep, MIT. Hangfire referansı olmayan Platform'a eklendi → "type exists in both Cronos and Hangfire.Core" çakışması yok (solution build doğruladı).
- **Docker:** Değişiklik yok.

## Commit & PR

- Branch: `task/WP14-settings-propagation`
- Commit: `28c7ffa` — WP14: Settings propagasyon — cron re-register + cron validation + deploy runbook
- PR: #187
- CI: ✓ PASS — Task CI HEAD `c83bf3b` run [`27837597559`](https://github.com/turkerurganci/Skinora/actions/runs/27837597559) tüm job success (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate). Integration job yeşil = `SystemSettingsServiceTests` spy/cron testleri gerçek SQL Server'da geçti; Migration dry-run yeşil = drift yok (migration yok).

## Known Limitations / Follow-up

- **Sidecar runtime push/pull** bilinçli olarak yapılmadı (owner kararı: post-MVP, T74 K1 / T96). Cadence/sweep değişimi sidecar restart gerektirir — runbook'ta belgeli.
- **`reconciliation.hot_wallet_address` / `cold_wallet_address`** prod'da set edilmezse reconciliation kapsamı atlanır (warn) — runbook §C'de "önerilen" olarak belgeli, fail-fast değil (by-design).
- AdminSettings endpoint fixture'ında valid-cron happy-path **bilinçli** unit-level'a taşındı (paylaşılan in-memory SQLite connection, nested scope dispose ile şemayı düşürüyor — harness artefaktı, prod sorunu değil; SQL Server per-scope pooled connection'da geçerli değil). Re-register `CronJobReconfigurerTests` + `SystemSettingsServiceTests` ile kanıtlanıyor.

## Notlar

- **Adım -1 (working tree):** temiz.
- **Adım 0 (main son-3 CI):** hepsi success — `27834306362` / `27834306341` / `27825433288`.
- **Adım 0b (repo memory):** mevcut (WP13 entry).
- **Dış Varsayımlar (Adım 4):**
  - Cronos paketi mevcut + sürüm: NuGet flat-container ile doğrulandı (0.8.3–0.13.0); Hangfire 1.8.18 Cronos'u **embed** ediyor (ayrı package dep yok), bu yüzden Hangfire referanssız Platform'a standalone Cronos eklemek çakışmasız → **solution build PASS ile doğrulandı**.
  - Hangfire `RecurringJob.AddOrUpdate` runtime cron değişimi için idempotent upsert (dökümante davranış) — `AddOrUpdateRecurring` zaten startup'ta kullanılıyordu.
  - Mandatory ayar sayısı = **19** (plan "21" stale) — `SettingsBootstrapTests` + seed grep ile doğrulandı.
  - Migration gereksiz — owner seed-default'u reddetti, yalnız cron re-register (şemasız) + doc.
- **Owner kararları (AskUserQuestion 2026-06-19):** Akış B (sidecar) = **Deploy runbook + env parity** · Akış C (19 zorunlu) = **Deploy runbook**.
