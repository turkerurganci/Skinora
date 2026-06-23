# T113 — E2E: Admin Akışları

**Faz:** F6 | **Durum:** ⏳ Devam ediyor — yapım bitti, doğrulama bekliyor | **Tarih:** 2026-06-23

---

## Yapılan İşler

03 §8 (Admin Akışları) için uçtan uca (E2E) kapsam — T107–T112 ile aynı seam (Playwright + `docker-compose.e2e.yml` + `sidecar-fake`). Admin back-office endpoint'lerinin tümü zaten **tam wire-li** (AD1 dashboard / AD6–AD7 işlem liste+detay T63'ten; AD8–AD9 settings T39/T102'den; AD11–AD14 roller T39/T104'ten; AD18 audit log T42/T106'dan; AD2/AD4 flag review T54'ten) olduğundan bu task **yalnız test kapsamı ekler — sıfır production kaynak değişikliği** (T108/T109/T111/T112 gibi).

- **`e2e/tests/admin-flows.spec.ts` (yeni):** 6 test, her biri bir kabul kriterini karşılar.
  1. **AC1 — Admin giriş + dashboard (§8.1).** `user`-rol JWT → `GET /admin/dashboard` **403** (sadece admin panele erişir; `AuthPolicies.AdminAccess`). `super_admin` JWT → **200**; `summaryCards` (activeTransactions/pendingFlags/dailyCompleted/weeklyCompleted) sayısal, sapan-fiyatlı (300) FLAGGED listing ile `pendingFlags ≥ 1`, `recentFlags` (son 5) flag'lenen işlemi içerir, `steamAccounts` (seed'li ACTIVE bot) ≥ 1.
  2. **AC2 — Flag inceleme + onay/red (§8.2).** Sapan-fiyat → FLAGGED + PENDING flag → AD2 review kuyruğunda görünür → AD4 approve → işlem CREATED'a döner. **Özet senaryo** (owner kararı 2026-06-23); tam approve/reject/account-flag matrisi T111 `fraud-flags.spec.ts`'te.
  3. **AC3 — İşlem listesi + detay (§8.3).** Normal (flag'siz) CREATED işlem → AD6 liste (`status=CREATED` filtreli) işlemi içerir (id/status/price/seller.steamId) → AD7 detay id/status/price + `statusHistory[]` + `adminActions` → olmayan id → **404** TRANSACTION_NOT_FOUND.
  4. **AC4 — Parametre değişikliği (§8.4).** AD8 liste `high_volume_amount_threshold`'u içerir → AD9 PUT yeni değer '7500' → 200, `key`/`value` round-trip, taze AD8 değişimi yansıtır → geçersiz değer 'not-a-number' → **400** VALIDATION_ERROR; `finally`'de orijinale restore.
  5. **AC5 — Rol yönetimi (§8.6).** AD11 roller + `availablePermissions` → AD12 create (benzersiz ad, 1 yetki) **201** → AD13 update (rename + yetki kümesi genişlet) **200** → AD14 delete **200** + listeden kalktığı teyit edilir.
  6. **AC6 — Audit log görüntüleme (§8).** AD9 settings değişikliği → `SYSTEM_SETTING_CHANGED` audit satırı (aynı DB transaction'ında) → AD18 `?search=high_volume_amount_threshold&category=ADMIN_ACTION` → satır mevcut, `category=ADMIN_ACTION`, `actor.steamId` = seed admin; `finally`'de restore.
- **`e2e/src/api.ts`:** 10 admin helper — `getAdminDashboard` (AD1), `listAdminTransactions`/`getAdminTransaction` (AD6/AD7), `listSettings`/`updateSetting` (AD8/AD9), `listRoles`/`createRole`/`updateRole`/`deleteRole` (AD11–AD14), `listAuditLogs` (AD18). Mevcut `listFlags`/`approveFlag` (AD2/AD4) yeniden kullanıldı.
- **`e2e/package.json`:** `test:admin` script.
- **`.github/workflows/ci.yml`:** advisory `e2e-smoke` job'a "Run API admin-flows E2E (T113)" adımı (T112 deseniyle birebir) + job yorumu 7 suite'e güncellendi. `timeout-minutes` 80'de bırakıldı (T113 ucuz — admin endpoint'leri senkron read/write, cron beklemesi yok).

## Etkilenen Modüller / Dosyalar

- `e2e/tests/admin-flows.spec.ts` (yeni — 6 test)
- `e2e/src/api.ts` (değişti — 10 admin helper + `RoleBody`)
- `e2e/package.json` (değişti — `test:admin`)
- `.github/workflows/ci.yml` (değişti — T113 e2e adımı + yorum 7 suite)

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T113) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Admin giriş ve dashboard | ✓ | AC1 testi: `user` token → `getAdminDashboard` **403**; `super_admin` → **200** body `summaryCards` 4 sayaç + `pendingFlags ≥ 1` (sapan-fiyat FLAGGED) + `recentFlags` flag'leneni içerir + `steamAccounts ≥ 1`. |
| 2 | Flag inceleme ve onay/red | ✓ | AC2 testi: sapan-fiyat → FLAGGED + PENDING flag (`getFlagForTransaction`); AD2 `listFlags(PENDING, TRANSACTION_PRE_CREATE)` kuyruğunda görünür; AD4 `approveFlag` → `transactionStatus=CREATED`; satıcı `getTransaction` → CREATED. (Tam matris T111.) |
| 3 | İşlem listesi ve detay | ✓ | AC3 testi: CREATED işlem; AD6 `listAdminTransactions(status=CREATED)` → satır id/status=CREATED/price=100/seller.steamId; AD7 `getAdminTransaction` → id/status/price + `statusHistory[]` + `adminActions`; olmayan guid → **404** TRANSACTION_NOT_FOUND. |
| 4 | Parametre değişikliği | ✓ | AC4 testi: AD8 `listSettings` `high_volume_amount_threshold` içerir; AD9 `updateSetting('7500')` → 200 round-trip + taze AD8 yansıtır; `'not-a-number'` → **400** VALIDATION_ERROR; restore. |
| 5 | Rol yönetimi | ✓ | AC5 testi: AD11 `listRoles` roller+availablePermissions (≥2); AD12 `createRole` **201** (name+permissions); AD13 `updateRole` **200** (rename+genişletilmiş yetki); AD14 `deleteRole` **200** + listeden silinmiş. |
| 6 | Audit log görüntüleme | ✓ | AC6 testi: AD9 settings değişikliği → AD18 `listAuditLogs(search=key, category=ADMIN_ACTION)` → `action=SYSTEM_SETTING_CHANGED` + `category=ADMIN_ACTION` + `actor.steamId=adminSteamId`. |

**Doğrulama kontrol listesi:** `[x] Admin paneli tüm akışları çalışıyor mu?` — dashboard + erişim kontrolü, flag review, işlem liste/detay, parametre güncelleme, rol CRUD, audit log görüntüleme; 6 testle uçtan uca kaplandı.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| E2E (lokal tam docker stack, ilk koşum) | ✓ 6/6 passed (4.8s) | `E2E_BASE_URL=http://localhost:5000 npm run test:admin`: AC1 1.1s / AC2 638ms / AC3 337ms / AC4 120ms / AC5 280ms / AC6 150ms. Stack: db + redis + fake-sidecar + backend (migrations `dotnet ef database update` ile, backend healthy). |
| E2E (idempotency — back-to-back, aynı backend) | ✓ 6/6 → 6/6 (3.3s / 2.9s) | İlk re-run AC5'i (rol CRUD) **yakaladı** — sabit rol adı + soft-delete + filtresiz unique index → 500 (bkz. Notlar/B1). Per-run benzersiz ad fix'i sonrası iki ardışık koşum da 6/6. |
| E2E (cross-suite — fraud → admin, aynı backend) | ✓ 4/4 → 6/6 (3.8s / 3.4s) | CI'nin sıralı tek-job desenini doğrular; T113 bildirim/outbox assert etmediğinden outbox-poison sınıfına yapısal olarak bağışık. |
| Statik (e2e harness) | ✓ | `tsc --noEmit` exit 0; `eslint .` 0/0; `prettier --check` değişen dosyalarda temiz ("All matched files use Prettier code style!"). Lokal CRLF uyarıları diğer (dokunulmayan) dosyalarda `core.autocrlf` artifaktı — CI "1. Lint" (LF) yetkili. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Bulgu sayısı | — (yapım self-check: 0 bloke-edici · 1 non-blocking gözlem B1) |
| Düzeltme gerekli mi | Hayır |

## Altyapı Değişiklikleri

- Migration: Yok (yeni şema yok; admin endpoint'leri mevcut tablolar üzerinde çalışır).
- Config/env değişikliği: Yok (yeni env yok).
- Docker değişikliği: Yok (yeni servis/image yok).
- CI: advisory `e2e-smoke` job'a T113 adımı; `timeout-minutes` 80'de kaldı.

## Commit & PR

- Branch: `task/T113-e2e-admin-flows`
- Commit: (bu commit) — T113: E2E — Admin akışları (test coverage)
- PR: [#206](https://github.com/turkerurganci/Skinora/pull/206)
- CI: ⏳ task CI watch (advisory e2e-smoke "Run API admin-flows E2E (T113)" adımı dahil). Validator çıkış kapısı.

## Known Limitations / Follow-up

- **B1 (non-blocking, pre-existing T104 backend gözlemi):** `UQ_AdminRoles_Name` **filtresiz** unique index + `AdminRoleService.DeleteAsync` **soft-delete** (IsDeleted=1, query filter `!IsDeleted`) → silinen bir rolün adı kalıcı olarak rezerve kalır; aynı adı yeniden insert/rename **500 INTERNAL_ERROR** (`Cannot insert duplicate key … UQ_AdminRoles_Name`) verir, temiz 409 değil. Admin "rol sil → aynı adla yeni rol" akışında 03 §8.6 için latent bir backend kusuru. **T113 kusuru değil** (T104'te geldi) ve T113 scope'unda (E2E test coverage) değil → doc/backend follow-up önerilir. Test bu davranıştan etkilenmemek için per-run benzersiz rol adı kullanır (CI tek koşum/fresh DB'de her ad çalışır; benzersiz ad re-run robustluğu verir).
- **Flag review kapsamı (AC2):** tek özet senaryo (queue → approve → CREATED). Tam approve/reject/account-flag-block matrisi T111 `fraud-flags.spec.ts`'te (owner kararı 2026-06-23: duplikasyondan kaçın, T113 suite'i kendi kendine yeterli kalsın).
- **Login surrogate:** Steam OAuth scriptlenemez → harness JWT-inject eder (`src/jwt.ts`, T107'den yerleşik). AC1'in 403 kontrolü "admin paneline yönlendirilir" kapısının API-düzeyi kanıtıdır (sadece admin panele erişir).
- **Audit/realtime:** AD18 inbox-satırı asserte edilir; admin aksiyonlarının SignalR realtime push'u (overlay) asserte edilmez (realtime-only; state geçişi + audit satırıyla kanıtlı — T111/T112 deseni).

## Notlar

- **Adım -1 (Working tree):** temiz (session başında `task/T112-e2e-emergency-hold` branch'inde, `git status --short` boş).
- **Adım 0 (Main CI startup):** son 3 main run `success` — `28047344492` + `28047344552` (T112 #205) + `28027679980` (T111 #204). main `de2479e`'e fast-forward edildi (T112 merged).
- **Adım 0b (Memory):** repo + auto memory mevcut, T112 kapanışı yansımış.
- **Dış varsayımlar (kod-okumayla doğrulandı, kırık yok):**
  - AD1 dashboard `AuthPolicies.AdminAccess` = `role ∈ {admin, super_admin}` (`AuthModule.cs:112`); `user` rol → 403.
  - AD2/AD4/AD6–AD7/AD8–AD9/AD11–AD14/AD18 policy'leri `super_admin` claim ile `PermissionAuthorizationHandler` bypass (`PermissionAuthorizationHandler.cs:17`).
  - AD9 settings update `SYSTEM_SETTING_CHANGED` audit satırını aynı transaction'da yazar (`SystemSettingsService.cs:136`); `AuditLogCategoryMap` → `ADMIN_ACTION` (`AuditLogCategoryMap.cs:41`); AD18 `search` EntityId substring + actor/subject SteamId/displayName (`AuditLogQueryService.cs:72`).
  - `high_volume_amount_threshold` seed'li (T111 default 5000), `fraud_detection` kategorisi (`SystemSettingsCatalog.cs:77`), generic pozitif-sayı range kuralı (`SystemSettingsValidator.cs:288`) → '7500' geçerli / 'not-a-number' decimal-tip reddi.
  - String enum query/body binding global (`Program.cs` `JsonStringEnumConverter`); `status=CREATED` query bind eder.
- **Lokal stack notu (audit trail):** Docker Desktop başlatıldı (engine 29.2.1); `docker compose -f docker-compose.e2e.yml build` (backend+fake) → db/redis up → `dotnet ef database update` (Skinora.Shared + Skinora.API) → backend+fake `--wait` healthy → test:admin. Backend image main ile byte-aynı (e2e-only değişiklik). Stack teardown çıkışta `docker compose ... down -v`.
