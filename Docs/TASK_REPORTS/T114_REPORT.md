# T114 — E2E: Downtime ve Bakım Senaryoları

**Faz:** F6 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız doğrulama bekliyor) | **Tarih:** 2026-06-24

---

## Yapılan İşler

03 §11 (Downtime Akışları) için uçtan uca (E2E) kapsam — T107–T113 ile aynı seam (Playwright + `docker-compose.e2e.yml` + `sidecar-fake`). Admin maintenance/outage kontrol yüzeyi (`POST /admin/maintenance/freeze|resume`, public `GET /platform/maintenance` banner, type→`TimeoutFreezeReason` bulk freeze/resume scope'u) **WP7'de tam wire-li** olduğundan bu task **yalnız test kapsamı ekler — sıfır production kaynak değişikliği** (T108/T109/T111/T112/T113 gibi).

- **`e2e/tests/downtime.spec.ts` (yeni):** 3 test, 3 kabul kriterini birebir karşılar.
  1. **AC1 — Platform bakımı (§11.1).** Seed CREATED işlem → `user` token ile `freezeMaintenance` **403** (MANAGE_SETTINGS gate) → admin `freezeMaintenance('PLATFORM_MAINTENANCE', {message, plannedEnd})` **200** (`active=true`, `type=PLATFORM_MAINTENANCE`, `affectedTransactions ≥ 1`) → **bakım banner** `GET /platform/maintenance` (`active=true` + type + message) → freeze trio DB'de (`TimeoutFreezeReason=MAINTENANCE`, `TimeoutFrozenAt` set, `TimeoutRemainingSeconds>0`, `IsOnHold=false`) → **decisive "timeout dondurma":** `backdateDeadline(AcceptDeadline)` + `assertStatusStable('CREATED', 18s)` (DeadlineScannerJob 5s aralık, frozen satır `!IsOnHold && TimeoutFrozenAt IS NULL` ile atlanır → CANCELLED_TIMEOUT'a düşmez) → `resumeMaintenance` **200** (`active=false`, `type=null`) + banner temizlenir + freeze trio null → **resume sonrası devam:** alıcı kabul eder → akış `ITEM_ESCROWED`'a ilerler.
  2. **AC2 — Steam kesintisi (§11.2).** `suppressTradeAccept('SELLER_TO_BOT')` ile işlem `TRADE_OFFER_SENT_TO_SELLER`'a park edilir (Steam-bound state) → `setDeadlineFromNow(TradeOfferToSellerDeadline, +30dk)` (e2e fast-path bu deadline'ı stamp'lemez; WP7 integration testi de seller-bound deadline'ı +12h seed'ler — gerçek outage canlı deadline'lı işlemi dondurur) → `freezeMaintenance('STEAM_OUTAGE', {message})` → `type=STEAM_OUTAGE`, `affectedTransactions ≥ 1` → **bildirim:** banner `type=STEAM_OUTAGE` + message (= kullanıcı-yüzlü §11.2 step-3 uyarısı; inbox bildirimi yok, MaintenanceStatusChanged broadcast SignalR-only) → freeze trio (`STEAM_OUTAGE`, remainder>0) → decisive `backdateDeadline` + `assertStatusStable(18s)` → `resumeMaintenance` clears banner + freeze trio + deadline re-armed (status hâlâ TRADE_OFFER_SENT_TO_SELLER).
  3. **AC3 — Blockchain degradasyonu (§11.3).** Happy-path drive → `ITEM_ESCROWED` (ödeme yapılmaz) → `freezeMaintenance('BLOCKCHAIN_DEGRADATION', {message})` → `type=BLOCKCHAIN_DEGRADATION`, `affectedTransactions ≥ 1` → banner `type=BLOCKCHAIN_DEGRADATION` → freeze trio (`BLOCKCHAIN_DEGRADATION`, PaymentDeadline remainder>0) → **decisive "ödeme timeout dondurma":** `backdateDeadline(PaymentDeadline)` + `assertStatusStable('ITEM_ESCROWED', 18s)` (per-tx payment-timeout Hangfire job freeze'de silinir + scanner frozen satırı atlar) → `resumeMaintenance` clears → **gecikmeli ödeme tespiti:** `payViaFake` → `PAYMENT_RECEIVED` (§11.3 step 5).
  - Her test `finally`'de `resumeMaintenance` (+ Test 2'de `resetTradeControl`) çağırır — maintenance state global (4 `platform.maintenance.*` SystemSetting), bir test ortada düşse bile global state temiz kalır.
- **`e2e/src/api.ts` (+3 helper, additive):** `freezeMaintenance` (WP7 freeze), `resumeMaintenance` (WP7 resume), `getPlatformMaintenance` (P2 anonim banner). Mevcut helper'lar (`createTransaction`/`acceptTransaction`/`pollStatus`/`suppressTradeAccept`/`resetTradeControl`/`payViaFake`/`assertStatusStable`/`unwrap`) yeniden kullanıldı.
- **`e2e/src/db.ts` (+1 helper, additive):** `setDeadlineFromNow` (`backdateDeadline`'ın ileri-yön aynası; aynı allow-list + bound int). Freeze trio okuması mevcut `getTransactionHoldState` ile (yeni db helper'a gerek yok — `TimeoutFreezeReason` zaten string olarak okunuyor).
- **`e2e/package.json`:** `test:downtime` script.
- **`.github/workflows/ci.yml`:** advisory `e2e-smoke` job'u **tek sıralı job'dan 8-leg'li matrix'e bölündü** (proje sahibi kararı 2026-06-24 — eski tek-job deseni 7 suite'te 80 dk cap'ine dayanmıştı, T114'te ölçeklenmiyordu). Her matrix leg kendi izole docker-compose stack'ini kurar (build + db + migrate + backend up) ve **tek** suite koşar; `continue-on-error: true` (advisory), `fail-fast: false`, `ci-gate.needs` dışında, `timeout-minutes: 30` (tek suite). T114 leg'i: `{ suite: T114 downtime, script: test:downtime }`.

## Etkilenen Modüller / Dosyalar

- `e2e/tests/downtime.spec.ts` (yeni — 3 test)
- `e2e/src/api.ts` (değişti — +3 maintenance helper, additive)
- `e2e/src/db.ts` (değişti — +`setDeadlineFromNow`, additive)
- `e2e/package.json` (değişti — `test:downtime`)
- `.github/workflows/ci.yml` (değişti — `e2e-smoke` tek-job → 8-leg matrix)

**Sıfır production kaynak değişikliği:** `git diff origin/main...HEAD` yalnız `e2e/` + `ci.yml` + `Docs/` + `.claude/memory/` (0 `.cs`, 0 frontend). `api.ts`/`db.ts` değişiklikleri **purely additive** (83 ekleme, 0 silme) → mevcut suite'lere regresyon riski yok.

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T114) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Platform bakımı: timeout dondurma, bakım banner, resume sonrası devam | ✓ | Test 1: freeze(PLATFORM_MAINTENANCE) → `MAINTENANCE` freeze trio + banner `active/type/message`; `backdateDeadline(AcceptDeadline)` + `assertStatusStable('CREATED',18s)` (scanner atlar, CANCELLED_TIMEOUT yok); resume → banner+trio clear; alıcı accept → `ITEM_ESCROWED` (devam). |
| 2 | Steam kesintisi: timeout dondurma, bildirim | ✓ | Test 2: TRADE_OFFER_SENT_TO_SELLER'a park + canlı deadline → freeze(STEAM_OUTAGE) → `STEAM_OUTAGE` freeze trio; banner `type=STEAM_OUTAGE`+message = kullanıcı bildirimi (§11.2 step-3); `backdate`+`assertStatusStable(18s)`; resume → clear + re-arm. |
| 3 | Blockchain degradasyonu: ödeme timeout dondurma | ✓ | Test 3: ITEM_ESCROWED → freeze(BLOCKCHAIN_DEGRADATION) → `BLOCKCHAIN_DEGRADATION` freeze trio (PaymentDeadline); `backdate(PaymentDeadline)`+`assertStatusStable('ITEM_ESCROWED',18s)` (per-tx job silinir + scanner atlar); resume → clear; `payViaFake` → `PAYMENT_RECEIVED` (gecikmeli tespit). |

**Doğrulama kontrol listesi:** `[x] Downtime senaryolarında freeze/resume doğru mu?` — 3 senaryonun her birinde freeze (reason-spesifik scope + banner + decisive scanner-skip) ve resume (banner+trio clear + akış devam/re-arm) uçtan uca kaplandı.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| E2E (yapım — lokal tam docker stack) | ✓ 3/3 passed (3.5m) → 3/3 (2.9m) | `npx playwright test downtime` iki temiz ardışık koşum (idempotent — maintenance state global, `finally`-resume ile temiz). Stack: db+redis+fake-sidecar+backend healthy; migrations `dotnet ef database update` (Skinora.Shared + Skinora.API). Backend image main ile byte-aynı (e2e-only değişiklik). |
| Statik (e2e harness) | ✓ | `tsc --noEmit` exit 0; `eslint .` 0/0; `prettier --end-of-line=auto` (değişen 4 dosya) temiz. |
| CI YAML | ✓ | `ci.yml` js-yaml parse OK; `e2e-smoke` 8-leg matrix (continue-on-error, fail-fast:false, timeout 30, ci-gate.needs dışı). |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor (ayrı chat) |
| Yapım-içi adversarial review | ✓ 6/6 boyut **sound**, **0 bloke-edici** (6-ajan refute-default workflow) |
| Bulgu sayısı | 0 bloke-edici · 10 non-blocking (hepsi tasarımı **doğruluyor** veya opsiyonel doc-açıklığı) |
| Düzeltme gerekli mi | Hayır (1 opsiyonel doc-açıklığı uygulandı — aşağı) |

**Adversarial review (6-ajan, refute-default — her ajan ilgili e2e + backend dosyalarını firsthand okudu):** 6 boyut — AC1-platform-maintenance · AC2-steam-outage · AC3-blockchain-degradation · freeze/resume-decisiveness-false-positive · doc-conformance · scope-security-ci — **hepsi sound, 0 bloke-edici bulgu**. Öne çıkan teyitler:
- **freeze/resume decisive, vacuous değil:** scanner-skip kanıtı gerçek — frozen olmayan backdate'li bir satır 18s/≥3 sweep penceresinde CANCELLED_TIMEOUT'a düşerdi; `assertStatusStable` ilk farklı statüde throw eder. Maintenance freeze `IsOnHold` set etmediğinden `ProjectStatus` underlying fazı döndürür (EMERGENCY_HOLD maskelemesi yok) → assertion anlamlı. Freeze trio okuması taze/uncached SQL; banner 30s cache freeze/resume post-commit `InvalidateCache` ile elenir; `try/finally` resume yalnız kendi hatasını yutar (body `expect` hatasını maskelemez).
- **`setDeadlineFromNow` (Steam outage) honest:** production'da hiçbir kod `TradeOfferToSellerDeadline`'ı send-transition'da stamp etmiyor (fake'in trade leg'i atlar) → e2e'de null; lever WP7 integration testinin seller-bound deadline'ı +12h seed'lemesiyle birebir parite. Defect maskelemiyor, allow-list + bound-param güvenli.
- **"bildirim = banner/broadcast" spec-correct:** 06 §2.13 NotificationType kataloğunda **hiç** maintenance/downtime tipi yok → §11 user bildirimi yalnız public banner (P2) + RT2 `MaintenanceStatusChanged` broadcast olarak realize edilir; per-user inbox satırı yok. Test banner'ı asserte eder, SignalR push broadcast-only olduğundan asserte edilmez (T112/T113 deseni). Atlanmış normatif inbox adımı yok.
- **scope/security/CI clean:** 0 production `.cs`/frontend; 0 yeni runtime dep (yalnız `test:downtime` script); MANAGE_SETTINGS gate gerçekten test ediliyor (`user`→403, `super_admin`→bypass); CI 8-leg matrix doğru (continue-on-error, fail-fast:false, timeout 30, `ci-gate.needs` dışı, her leg izole stack + tek suite).

**Uygulanan opsiyonel iyileştirme:** `assertStatusStable` docstring'i (api.ts) yalnız EMERGENCY_HOLD (T112) semantiğini anlatıyordu → maintenance-freeze yolu (IsOnHold=false → underlying faz projeksiyonu) için genelleştirildi (yalnız yorum; davranış değişmedi; tsc0/eslint0/prettier clean). Diğer non-blocking notlar (Test 3 re-arm liveness implicit, §11.2 buyer-side state yalnız integration'da, §11.1 step 2/7 per-user bildirim backend'de yok) zaten yeterli kapsanmış/scope-dışı — değişiklik gerektirmedi.

## Altyapı Değişiklikleri

- Migration: **Yok** (yeni şema yok; maintenance freeze WP7 `platform.maintenance.*` SystemSettings + 06 §3.5 freeze kolonları mevcut).
- Config/env değişikliği: **Yok** (e2e `Timeouts__DeadlineScannerIntervalSeconds=5` T109'dan mevcut).
- Docker değişikliği: **Yok** (yeni servis/image yok).
- CI: `e2e-smoke` job'u 8-leg matrix'e refactor edildi (advisory, blocking değil).

## Commit & PR

- Branch: `task/T114-e2e-downtime`
- Commit: `8e07539` (+ `db8f5b3` PR# backfill)
- PR: [#207](https://github.com/turkerurganci/Skinora/pull/207)
- CI: ✓ **PASS** — task CI HEAD `db8f5b3` run [`28083376285`](https://github.com/turkerurganci/Skinora/actions/runs/28083376285) `conclusion=success`. **Tüm blocking job success** (1.Lint, 2.Build, 3.Unit, 3b.JS test, 4.Integration, 5.Contract, 6.Migration dry-run, 4× Docker build, CI Gate). **8-leg advisory `e2e-smoke` matrix: hepsi `conclusion=success`** — **"E2E T114 downtime (advisory)" leg `success`** (continue-on-error maskelemiyor → 3 test gerçek migrated docker-compose stack'inde geçti, vacuous değil) + önceki 7 leg (happy-path/T108–T113) da success → **matrix split regresyon yok**. Post-merge main CI + Docker Publish watch = validator çıkış kapısı (Adım 18).

## Known Limitations / Follow-up

- **Seller-trade deadline e2e fast-path artefaktı:** `TradeOfferSentToSeller` fazına park eden e2e işlemde `TradeOfferToSellerDeadline` **null** kalıyor (fake'in trade leg'i production deadline-stamp'ından geçmez; T109 da bu deadline'ı timeout'u tetiklemek için explicit `backdate` ile SET eder). Test 2 gerçek outage senaryosunu temsil etmek için freeze öncesi `setDeadlineFromNow(+30dk)` ile canlı bir pencere verir (WP7 integration testi de seller-bound deadline'ı +12h seed'ler). **T114 kusuru değil**; production'da bu deadline'ın hangi mekanizmayla stamp'lendiği (Hangfire delayed job vs. DB kolon) ayrı bir backend gözlemi — bu E2E scope'unda değil.
- **"Bildirim" = banner/broadcast:** §11.2/§11.3 kullanıcı bildirimi = public maintenance banner + `MaintenanceStatusChanged` realtime broadcast (per-user inbox satırı **yok** — WP7 `AdminMaintenanceService` yalnız settings + audit + broadcast yazar). Test banner'ı (API-gözlemlenebilir) asserte eder; SignalR realtime push broadcast-only olduğundan asserte edilmez (T112/T113 deseni).
- **§11.2a (tekil bot kısıtlanması):** T114 AC'sinde **yok** (recovery/manual intervention akışı — ayrı feature, T69 forward); kapsanmadı.
- **CI matrix maliyeti:** 8 leg paralel, her biri kendi image build + stack bring-up'ını yapar (Docker layer cache runner'lar arası paylaşılmaz). Advisory job olduğundan kabul edilebilir; wall-clock tek-suite ile sınırlı (~15 dk/leg).

## Notlar

- **Adım -1 (Working tree):** temiz (session başında `task/T113-e2e-admin-flows` branch'inde, `git status --short` boş).
- **Adım 0 (Main CI startup):** son 3 main run `success` — `28062174449` + `28062174527` (T113 #206) + `28047344492` (T112 #205). main `c6d9e04`'e fast-forward edildi (T113 merged).
- **Adım 0b (Memory):** repo + auto memory T113 kapanışını yansıtıyor.
- **Dış varsayımlar (Adım 4 — kod-okumayla doğrulandı, kırık yok):**
  - `POST /admin/maintenance/freeze|resume` (`AdminMaintenanceController.cs`) MANAGE_SETTINGS-gated; `super_admin` claim PermissionAuthorizationHandler ile bypass eder; `user` → 403.
  - `MaintenanceFreezeRequest{Type,Message,PlannedEnd}` → response `MaintenanceStateDto{Active,Type,Message,PlannedEnd,AffectedTransactions}` (camelCase, ApiResponse `data` wrapper). Activatable types: PLANNED_MAINTENANCE/PLATFORM_MAINTENANCE/STEAM_OUTAGE/BLOCKCHAIN_DEGRADATION; NONE → 400.
  - `FreezeReasonFor`: PLATFORM_MAINTENANCE→`MAINTENANCE`, STEAM_OUTAGE→`STEAM_OUTAGE`, BLOCKCHAIN_DEGRADATION→`BLOCKCHAIN_DEGRADATION`, PLANNED_MAINTENANCE→null (banner-only).
  - `TimeoutFreezeReasonScopes.For`: MAINTENANCE=AllActive, STEAM_OUTAGE=SteamBound(TRADE_OFFER_SENT_TO_SELLER/_BUYER), BLOCKCHAIN_DEGRADATION=PaymentOnly(ITEM_ESCROWED).
  - `FreezeManyAsync` filtresi `!IsDeleted && !IsOnHold && TimeoutFrozenAt==null && statuses.Contains(Status)`; `IsOnHold` set ETMEZ (emergency hold değil) → detay projeksiyonu underlying status kalır.
  - `DeadlineScannerJob` filtresi `!IsDeleted && !IsOnHold && TimeoutFrozenAt==null` → frozen satır atlanır; e2e scanner aralığı 5s (`Timeouts__DeadlineScannerIntervalSeconds=5`).
  - `GET /platform/maintenance` (`PlatformController`, AllowAnonymous) → `PlatformMaintenanceResponse{Active,Type,Message,PlannedEnd}`; 30s cache, freeze/resume post-commit `InvalidateCache` eder (split-brain yok).
  - Admin audit FK: `MAINTENANCE_MODE_CHANGED` audit satırı `ActorId` NO-ACTION FK → admin User satırı olmalı (`ensureAdmin`).
- **Lokal stack notu (audit trail):** Docker Desktop (engine 29.2.1); `docker compose -f docker-compose.e2e.yml build skinora-backend skinora-fake-sidecar` (CACHED) → db up + healthy → `dotnet ef database update` → backend+fake `--wait` healthy → `npx playwright test downtime`. İki temiz ardışık koşum 3/3 → 3/3.
