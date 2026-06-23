# T113 — E2E: Admin Akışları

**Faz:** F6 | **Durum:** ✓ Tamamlandı — bağımsız validator PASS | **Tarih:** 2026-06-24 (yapım 2026-06-23)

---

## Yapılan İşler

03 §8 (Admin Akışları) için uçtan uca (E2E) kapsam — T107–T112 ile aynı seam (Playwright + `docker-compose.e2e.yml` + `sidecar-fake`). Admin back-office endpoint'lerinin tümü zaten **tam wire-li** (AD1 dashboard / AD6–AD7 işlem liste+detay T63'ten; AD8–AD9 settings T39/T102'den; AD11–AD14 roller T39/T104'ten; AD18 audit log T42/T106'dan; AD2/AD4 flag review T54'ten) olduğundan bu task **yalnız test kapsamı ekler — sıfır production kaynak değişikliği** (T108/T109/T111/T112 gibi).

- **`e2e/tests/admin-flows.spec.ts` (yeni):** 7 test (6 kabul kriteri + 1 validator-fix AD17 testi), her kabul kriterini karşılar.
  1. **AC1 — Admin giriş + dashboard (§8.1).** `user`-rol JWT → `GET /admin/dashboard` **403** (sadece admin panele erişir; `AuthPolicies.AdminAccess`). `super_admin` JWT → **200**; `summaryCards` (activeTransactions/pendingFlags/dailyCompleted/weeklyCompleted) sayısal, sapan-fiyatlı (300) FLAGGED listing ile `pendingFlags ≥ 1`, `recentFlags` (son 5) flag'lenen işlemi içerir, `steamAccounts` (seed'li ACTIVE bot) ≥ 1.
  2. **AC2 — Flag inceleme + onay/red (§8.2).** Sapan-fiyat → FLAGGED + PENDING flag → AD2 review kuyruğunda görünür → AD4 approve → işlem CREATED'a döner. **Özet senaryo** (owner kararı 2026-06-23); tam approve/reject/account-flag matrisi T111 `fraud-flags.spec.ts`'te.
  3. **AC3 — İşlem listesi + detay (§8.3).** Normal (flag'siz) CREATED işlem → AD6 liste (`status=CREATED` filtreli) işlemi içerir (id/status/price/seller.steamId) → AD7 detay id/status/price + `statusHistory[]` + `adminActions` → olmayan id → **404** TRANSACTION_NOT_FOUND.
  4. **AC4 — Parametre değişikliği (§8.4).** AD8 liste `high_volume_amount_threshold`'u içerir → AD9 PUT yeni değer '7500' → 200, `key`/`value` round-trip, taze AD8 değişimi yansıtır → geçersiz değer 'not-a-number' → **400** VALIDATION_ERROR; `finally`'de orijinale restore.
  5. **AC5 — Rol yönetimi (§8.6).** AD11 roller + `availablePermissions` → AD12 create (benzersiz ad, 1 yetki) **201** → AD13 update (rename + yetki kümesi genişlet) **200** → **AD11 taze re-read ile persisted `permissions` doğrulanır** (validator-fix; AD12/AD13 response'u request'i echo'lar → DB okuması şart) → AD14 delete **200** + listeden kalktığı teyit edilir. **+ Ayrı AD17 testi (§8.6 step 4 "kullanıcıları rollere atayabilir" — validator-fix):** AD12 create → AD17 `PUT /admin/users/:id/role` ile seed buyer atanır → AD11 `assignedUserCount ≥ 1` (persisted re-read) → AD14 delete **422 ROLE_HAS_USERS** (atama guard'ı) → `finally` unassign(null) + delete.
  6. **AC6 — Audit log görüntüleme (§8).** AD9 settings değişikliği → `SYSTEM_SETTING_CHANGED` audit satırı (aynı DB transaction'ında) → AD18 `?search=high_volume_amount_threshold&category=ADMIN_ACTION` → satır mevcut, `category=ADMIN_ACTION`, `actor.steamId` = seed admin; `finally`'de restore.
- **`e2e/src/api.ts`:** 11 admin helper — `getAdminDashboard` (AD1), `listAdminTransactions`/`getAdminTransaction` (AD6/AD7), `listSettings`/`updateSetting` (AD8/AD9), `listRoles`/`createRole`/`updateRole`/`deleteRole` (AD11–AD14), **`assignUserRole` (AD17 — validator-fix)**, `listAuditLogs` (AD18). Mevcut `listFlags`/`approveFlag` (AD2/AD4) yeniden kullanıldı.
- **`e2e/src/db.ts`:** `seedHappyPath` cleanup batch'ine `DELETE FROM AdminUserRoles WHERE UserId IN (@s,@b)` (validator-fix — AD17 testi atama satırı bıraktığından, NO-ACTION FK'in re-run'da buyer silmesini bloke etmemesi için).
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
| 5 | Rol yönetimi | ✓ | AC5 testi: AD11 `listRoles` roller+availablePermissions (≥2); AD12 `createRole` **201** (name+permissions); AD13 `updateRole` **200** (rename+genişletilmiş yetki); **AD11 taze re-read → persisted `permissions` = [perm0,perm1]** (validator-fix); AD14 `deleteRole` **200** + listeden silinmiş. **AD17 testi:** AD17 `assignUserRole(buyer, role)` → AD11 `assignedUserCount ≥ 1` (persisted) → AD14 delete **422 ROLE_HAS_USERS**. |
| 6 | Audit log görüntüleme | ✓ | AC6 testi: AD9 settings değişikliği → AD18 `listAuditLogs(search=key, category=ADMIN_ACTION)` → `action=SYSTEM_SETTING_CHANGED` + `category=ADMIN_ACTION` + `actor.steamId=adminSteamId`. |

**Doğrulama kontrol listesi:** `[x] Admin paneli tüm akışları çalışıyor mu?` — dashboard + erişim kontrolü, flag review, işlem liste/detay, parametre güncelleme, rol CRUD + **kullanıcı→rol atama (AD17)**, audit log görüntüleme; 7 testle uçtan uca kaplandı.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| E2E (yapım — lokal tam docker stack) | ✓ 6/6 passed (4.8s) | `npm run test:admin` (6 test) + idempotency (6/6→6/6) + cross-suite fraud→admin (4/4→6/6). |
| E2E (validator — lokal tam docker stack, AD17/AC5-fix sonrası) | ✓ 7/7 passed (2.3s) | Validator firsthand: db+redis+fake-sidecar+backend (mevcut image — e2e-only değişiklik → byte-aynı; migrations `dotnet ef database update`). İki temiz ardışık koşum 7/7→7/7. **3.+ ardışık koşum `RATE_LIMIT_EXCEEDED`** (`admin-write` 30/60s; tek koşum ~14 write → CI tek-koşum etkilenmez). Yeni AD17 testi + güçlendirilmiş AC5 dahil. |
| Statik (e2e harness — validator) | ✓ | `tsc --noEmit` exit 0; `eslint .` 0/0. Lokal `prettier` CRLF uyarıları dokunulmayan dosyalarda da var → `core.autocrlf` artifaktı; CI "1. Lint" (LF) yetkili ve yeşil. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** (bağımsız validator, ayrı chat 2026-06-24, rapor görülmeden) |
| Bulgu sayısı | 1 (AC5 echo-assertion kapsam derinliği — **validator-fix ile çözüldü**, owner kararı); ayrıca pre-existing T104 B1 (non-blocking, scope dışı) |
| Düzeltme gerekli mi | Yapıldı (AD17 testi + AC5 persisted re-read; lokal 7/7 + CI yeşil bekleniyor) |

## Altyapı Değişiklikleri

- Migration: Yok (yeni şema yok; admin endpoint'leri mevcut tablolar üzerinde çalışır).
- Config/env değişikliği: Yok (yeni env yok).
- Docker değişikliği: Yok (yeni servis/image yok).
- CI: advisory `e2e-smoke` job'a T113 adımı; `timeout-minutes` 80'de kaldı.

## Commit & PR

- Branch: `task/T113-e2e-admin-flows`
- Commit: (bu commit) — T113: E2E — Admin akışları (test coverage)
- PR: [#206](https://github.com/turkerurganci/Skinora/pull/206)
- CI: ✓ task CI HEAD `55e3d2f` run [`28051907362`](https://github.com/turkerurganci/Skinora/actions/runs/28051907362) — 15/15 job success (CI Gate dahil) + advisory `e2e-smoke` job success, **"Run API admin-flows E2E (T113)" adımı `conclusion=success`** (continue-on-error maskelemiyor → 6 test gerçek migrated docker-compose stack'inde geçti, vacuous değil); aynı run'da happy-path + T108–T112 adımları da `success` → regresyon yok. Post-merge main CI + Docker Publish watch = validator çıkış kapısı (Adım 18).

## Known Limitations / Follow-up

- **B1 (non-blocking, pre-existing T104 backend gözlemi):** `UQ_AdminRoles_Name` **filtresiz** unique index + `AdminRoleService.DeleteAsync` **soft-delete** (IsDeleted=1, query filter `!IsDeleted`) → silinen bir rolün adı kalıcı olarak rezerve kalır; aynı adı yeniden insert/rename **500 INTERNAL_ERROR** (`Cannot insert duplicate key … UQ_AdminRoles_Name`) verir, temiz 409 değil. Admin "rol sil → aynı adla yeni rol" akışında 03 §8.6 için latent bir backend kusuru. **T113 kusuru değil** (T104'te geldi) ve T113 scope'unda (E2E test coverage) değil → doc/backend follow-up önerilir. Test bu davranıştan etkilenmemek için per-run benzersiz rol adı kullanır (CI tek koşum/fresh DB'de her ad çalışır; benzersiz ad re-run robustluğu verir).
- **Flag review kapsamı (AC2):** tek özet senaryo (queue → approve → CREATED). Tam approve/reject/account-flag-block matrisi T111 `fraud-flags.spec.ts`'te (owner kararı 2026-06-23: duplikasyondan kaçın, T113 suite'i kendi kendine yeterli kalsın). Validator bağımsız teyit: T111'de `fraud-flags.spec.ts:119 › price deviation → FLAGGED → admin reject → CANCELLED_ADMIN ✓` (reject ayağı gerçekten kaplı).
- **AC5 echo-assertion (validator bulgusu — ÇÖZÜLDÜ):** Yapım AC5 yetki-atama ayağını yalnız AD12/AD13 response'uyla asserte ediyordu; `AdminRoleService` Create/Update response'u `permissions`'ı `request.Permissions`'tan türetir (`AdminRoleService.cs:104,165`), DB'den re-read etmez → sessizce kırık bir `AdminRolePermission` insert'i yine echo'lardı (suite'in tek echo-asserted kriteriydi; AC3/AC4/AC6 hepsi taze re-read yapar). **Owner kararı (AskUserQuestion 2026-06-24): "AD17'yi de kapsama ekle"** → validator-fix: (a) AC5'e AD11 `listRoles` taze re-read + persisted `permissions` assert; (b) yeni AD17 testi (§8.6 step 4 — kullanıcı→rol atama + `assignedUserCount` persisted + `ROLE_HAS_USERS` 422 guard). Lokal 7/7 + idempotent + `tsc0/eslint0`.
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

---

## Doğrulama (Bağımsız Validator — ayrı chat, 2026-06-24)

**Verdict: ✓ PASS** — 6/6 kabul kriteri karşılandı (AC5 başta `~ Kısmi` idi; validator-fix sonrası `✓`). Yapım raporu görülmeden bağımsız verdict oluşturuldu, sonra karşılaştırıldı.

**Kapılar:**
- Adım -1 (Working tree): temiz.
- Adım 0 (Main CI son-3): hepsi `success` (`28047344492`/`28047344552` T112 #205 · `28027679980` T111 #204).
- Adım 0b (Memory drift): T113 satırı repo memory'de mevcut.
- Production kaynak değişikliği: **YOK** (`git diff origin/main...HEAD` yalnız `e2e/`+`ci.yml`+docs+memory).

**CI step-level kanıt (kritik — e2e-smoke job `continue-on-error: true` advisory):** job conclusion `success` adım başarısını maskeleyebileceğinden **adım** seviyesine bakıldı. İki başarılı task-branch run'ında (`28051907362` + `28054478733`) **"Run API admin-flows E2E (T113)" adımı `conclusion=success`**, `6 passed (2.2s)`, `if: failure()` "Dump compose logs" adımı `skipped` → hiçbir adım kırılmadı. AC2 reject ayağı T111 `fraud-flags.spec.ts:119` ile gerçekten kaplı.

**Bağımsız firsthand koşum (validator):** tam docker stack ayağa kaldırıldı (db+redis+fake+backend healthy, migrations uygulandı). AD17/AC5-fix sonrası **`npm run test:admin` 7/7 passed** (2 temiz ardışık koşum). 3.+ ardışık koşum `RATE_LIMIT_EXCEEDED` (`admin-write` 30/60s; tek koşum ~14 write) — saf harness-hammering artifaktı, CI tek-koşumda görülmez.

**Adversarial doğrulama (8-ajan workflow, refute-default):** AC1/AC3/AC4/AC6 + doc-conformance + security = sound (high confidence). AC2 = sound (reject T111'de kaplı). **AC5 = refuted (medium):** yetki-atama yalnız response-echo ile asserte ediliyordu (kaynak teyidi `AdminRoleService.cs:104,165` request-türevli). Bulgu owner'a sunuldu → "AD17'yi de kapsama ekle" kararı → validator-fix (yukarıda). Bulgu çözüldü.

**Güvenlik:** secret yok (test-fixture JWT), yeni runtime dependency yok (`package.json` yalnız `test:admin` script), admin authz gate gerçekten test ediliyor (`user`→403). Salt test/CI/docs değişikliği.

**Yapım raporu karşılaştırması:** Tam uyumlu. Yapımın yakalamadığı tek ek bulgu = AC5 echo-assertion (bağımsız validator değeri); validator-fix ile kapatıldı. B1 (pre-existing T104) yapım raporuyla aynı şekilde non-blocking/scope-dışı.
