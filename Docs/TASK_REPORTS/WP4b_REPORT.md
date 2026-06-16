# WP4b — Fraud kapsam tamlığı (retro-scan + FLAGGED yolları)

**Faz:** PRE_F6_PLAN (P2 — Fraud/uyum) | **Durum:** ✓ Tamamlandı — bağımsız validator PASS | **Tarih:** 2026-06-17

---

## Yapılan İşler

WP4b dört fraud-kapsam boşluğunu kapatır. **İki gerçek boşluk + bir gecikme-iyileştirmesi + bir spec-uyumlu doğrulama.** Tüm owner kararları AskUserQuestion ile alındı (hepsi öneri seçeneği). **Migration YOK** (EF model değişmedi).

- **#1 MultiAccount retro-scan (T56) — gerçek boşluk.** `MultiAccountDetector.EvaluateAsync` tam kuruluydu ama yalnız wallet-update'te (`WalletAddressService.cs:144`) tetikleniyordu → yeni-hesap çakışmasının eski-hesap tarafı + tarihsel çakışmalar hiç değerlendirilmiyordu. Yeni `MultiAccountRetroScanJob` (Skinora.API, `AutoUnsuspendJob` deseni) **günlük const cron** (`0 2 * * *`) cüzdanlı (payout/refund adresi olan) aktif (silinmemiş/deaktive-olmayan) kullanıcıları mevcut `IMultiAccountDetector` ile yeniden tarar. **Yeni tespit mantığı yok** — detector reusable seam. Dedup = detector'ın mevcut per-user idempotency gate'i (kaba, owner kararı). Per-user hata loglanıp yutulur (sweep abort olmaz; cancellation propagate).
- **#3 FLAGGED-approval payment-address allocation — gecikme-iyileştirmesi.** Inline allocation yalnız CREATED'de çalışıyordu (`TransactionCreationService.cs:301`; FLAGGED açıkça atlanıyordu — kod yorumu bu approve yolunu "future task entry point" diye adlandırıyor). `FraudFlagService.ApproveAsync` artık FLAGGED→CREATED **commit'inden sonra** `IPaymentAddressAllocator.AllocateAsync`'i çağırır (allocator pre-commit FLAGGED'i reddeder → commit zorunlu). Best-effort/non-fatal: sidecar kesintisi commit'lenmiş approve'u 500'e çevirmez; `EnsurePaymentAddressJob` ({CREATED,ACCEPTED}, dakikalık) zaten recovery sağlıyordu → yalnız ~60s gecikme silinir, parite kurulur. Idempotent (`AlreadyExisted`).
- **#4 fraud-note max-length (T54) — gerçek boşluk.** `NormalizeNote` yalnız trim ediyordu; `AdminNote` kolonu `HasMaxLength(2000)` → 2000+ char not SaveChanges'te truncation **500** veriyordu. `ApproveAsync`/`RejectAsync` artık `BeginTransaction`'dan **önce** 2000 char doğrular → `ValidationFailed` outcome → controller 400 `VALIDATION_ERROR` (`FraudFlagErrorCodes.ValidationError` zaten vardı). Kanonik değer **2000** (kolon genişliği + 06 §3.12:886 + kardeş `AdminUserSuspensionService` emsali); `T54_REPORT.md`'deki "1000" advisory stale. Not opsiyonel → min floor yok.
- **#2 FLAGGED-approve per-tx accept-timeout job (T54) — SADECE DOĞRULA, yeni job YOK.** Spec çelişkisi: 05 §4.4:513 + 06 §3.5:650 accept-deadline'ları **bilinçli olarak poller-driven** yapar (yalnız ITEM_ESCROWED per-tx Hangfire job alır). `ApproveAsync` zaten `AcceptDeadline`'ı setliyordu ve `DeadlineScannerJob.cs:99` bunu normal CREATED tx ile birebir enforce ediyor. Yeni per-tx job belgelenmiş Aşama ayrımı'nı ihlal eder + gereksiz migration (`Transaction.AcceptTimeoutJobId` kolonu) gerektirirdi. **Owner kararı: kurulmadı.** Bunun yerine uçtan-uca regresyon testi (`FlaggedApprove_AcceptDeadline_IsEnforcedByDeadlineScanner`) ApproveAsync→scanner→CANCELLED_TIMEOUT zincirini doğrular; backlog resolved-by-design işaretlendi.

## Etkilenen Modüller / Dosyalar

**Yeni (Skinora.API):**
- `backend/src/Skinora.API/Services/Fraud/MultiAccountRetroScanJob.cs` — günlük retro-scan job + `MultiAccountRetroScanOutcome` sayaç kaydı
- `backend/src/Skinora.API/Services/Fraud/MultiAccountRetroScanJobRegistrar.cs` — `IHostedService` recurring kayıt (scope-per-start)

**Değişen (Skinora.Fraud):**
- `Application/Flags/FraudFlagService.cs` — ctor'a `IPaymentAddressAllocator` + `ILogger`; `MaxNoteLength=2000`; ApproveAsync note-validation + nested-using tx-block + post-commit `TryAllocatePaymentAddressAsync`; RejectAsync note-validation; helper
- `Application/Flags/FraudFlagOutcomes.cs` — `ApproveFlagOutcome.ValidationFailed` + `RejectFlagOutcome.ValidationFailed`

**Değişen (Skinora.API):**
- `Controllers/AdminFlagsController.cs` — Approve+Reject `ValidationFailed` → 400 `VALIDATION_ERROR`
- `Program.cs` — `MultiAccountRetroScanJob` + registrar DI/hosted-service kaydı

**Test:**
- `tests/Skinora.Fraud.Tests/TestSupport/StubPaymentAddressAllocator.cs` (yeni)
- `tests/Skinora.Fraud.Tests/Integration/FraudFlagServiceTests.cs` (+8 test: note approve/reject/boundary, allocation 3, scanner-enforce 1)
- `tests/Skinora.Fraud.Tests/Integration/MultiAccountDetectorTests.cs` (ctor güncelleme)
- `tests/Skinora.API.Tests/Integration/Fraud/MultiAccountRetroScanJobTests.cs` (+2 test: candidate-filter, fault-isolation)

**Doküman:**
- `Docs/07_API_DESIGN.md` §9.4/§9.5 (note 2000 char + 400 `VALIDATION_ERROR`)
- `Docs/DEFERRED_BACKLOG.md` (4 kalem ✅ ÇÖZÜLDÜ; #2 resolved-by-design)
- `Docs/PRE_F6_PLAN.md` (WP4b ✅ + çözüm notu)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Retro-scan job cüzdanlı aktif kullanıcıları tarar, deaktive/silinmiş/cüzdansız atlar | ✓ | `Scans_Only_Active_WalletBearing_Users` (set eşitliği) |
| 2 | Retro-scan per-user hata sweep'i abort etmez | ✓ | `PerUser_Failure_Does_Not_Abort_The_Sweep` (3 scan, 1 fail, b+c işlenir) |
| 3 | Retro-scan mevcut detector idempotency gate'i (kaba dedup) kullanır, yeni tespit mantığı yok | ✓ | Job yalnız `EvaluateAsync` çağırır; `MultiAccountDetector.cs:76-86` gate; `MultiAccountDetectorTests` yeşil |
| 4 | FLAGGED-approve sonrası payment-address eager allocate (post-commit, best-effort) | ✓ | `Approve_TransactionFlag_AllocatesPaymentAddress_PostCommit`; account-flag etmez; allocator throw → approve commit'li kalır |
| 5 | Note > 2000 char → approve+reject 400 `VALIDATION_ERROR`, state mutasyonu yok; =2000 kabul | ✓ | `Approve/Reject_NoteTooLong_*` + `Approve_NoteAtMaxLength_IsAccepted`; controller 400 map |
| 6 | FLAGGED-approve accept-deadline poller (`DeadlineScannerJob`) ile enforce edilir; yeni per-tx job yok | ✓ | `FlaggedApprove_AcceptDeadline_IsEnforcedByDeadlineScanner` → CANCELLED_TIMEOUT; 05 §4.4 + 06 §3.5:650 |
| 7 | Migration yok (EF model drift yok) | ✓ | `InitialMigrationTests` `Model_HasNoPendingChanges` 6/6 PASS |
| 8 | 07 kontrat + backlog + plan doc'ları güncel | ✓ | 07 §9.4/§9.5, DEFERRED_BACKLOG 4 kalem, PRE_F6_PLAN WP4b |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Fraud (integration) | ✓ 35/35 | `FraudFlagServiceTests`+`MultiAccountDetectorTests` (`--filter ...`) 23s |
| API (integration) | ✓ 14/14 | `MultiAccountRetroScanJobTests`+`AdminFlagsEndpointTests` 18s |
| Migration drift | ✓ 6/6 | `InitialMigrationTests` (`Model_HasNoPendingChanges` dahil) |
| Build (Debug+Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Debug` + `-c Release` |
| Format | ✓ temiz | `dotnet format Skinora.sln --verify-no-changes --severity error` (exit 0) |

Diğer integration test (regresyon geniş kapsam) → CI authoritative.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Yapım-içi self-check | 8/8 ✓ |
| Bulgu sayısı | 0 bloke-edici (S1/S2/S3) · 2 NOTE (non-blocking) |
| Düzeltme gerekli mi | Hayır |

### Bağımsız Validator (ayrı chat, 2026-06-17 — rapor görülmeden kendi verdict'i)

**VERDICT: ✓ PASS — 8/8 AC, 0 bloke-edici bulgu.**

**Kapılar:** Adım -1 working tree temiz · Adım 0 main son-3 CI success (`27644750670`/`27644750649`/`27577316206`) · Adım 0b repo memory WP4b mevcut · Adım 8a task CI HEAD `2357347` run [`27649130700`](https://github.com/turkerurganci/Skinora/actions/runs/27649130700) **tüm job success** (+ `e262cab` run `27648552366` success).

**Validator lokal yeniden çalıştırma (gerçek SQL Server / TestContainers, Docker mevcut):** `dotnet build -c Release` **0W/0E** · **Skinora.Fraud.Tests tam proje 91/91** (raporun `--filter` 35/35'ini kapsar + Fraud regresyon yok) · **Skinora.API.Tests `~MultiAccountRetroScanJob|~AdminFlags` 14/14**.

**Bağımsız spec/kod teyidi (4 kalem):**
- **#1 retro-scan** — `MultiAccountRetroScanJob` + registrar `AutoUnsuspendJob`/`AutoUnsuspendJobRegistrar` desenini birebir izler; aday filtresi (cüzdanlı + silinmemiş + deaktive-olmayan) doğru çünkü detector **strong** wallet-match sinyali gerektirir (supporting sinyaller `MultiAccountDetector.cs:125` "evidence only — never flag alone" → cüzdansız kullanıcı zaten flag'lenemez); dedup detector idempotency gate'i (`MultiAccountDetector.cs:76-86`); per-user hata izolasyonu; cron `0 2 * * *` geçerli; `Program.cs:327-328` kayıtlı.
- **#2 accept-timeout (verify-only)** — 05 §4.4:513 + 06 §3.5 (`AcceptDeadline` poller-driven, yalnız ITEM_ESCROWED per-tx job) bağımsız okundu → owner kararı spec-doğru; CREATED state→deadline invariant'ı (`AcceptDeadline NOT NULL`) ApproveAsync commit öncesi sağlanır; regresyon testi gerçek `DeadlineScannerJob`'u koşturup `CANCELLED_TIMEOUT` üretir.
- **#3 allocation** — post-commit zorunlu çünkü `PaymentAddressAllocator.cs:25-29` eligibility = {CREATED,ACCEPTED} (yalnız commit sonrası görünür); best-effort swallow (yalnız `OperationCanceledException` rethrow); idempotent `AlreadyExisted` (`PaymentAddressAllocator.cs:66-81`) → eager inline + `EnsurePaymentAddressJob` (`EligibleStates`+`PaymentAddress==null`, dakikalık) çift-allocate etmez; yalnız `TRANSACTION_PRE_CREATE` promosyonunda çalışır.
- **#4 note-limit** — `MaxNoteLength=2000` = `FraudFlagConfiguration.HasMaxLength(2000)` kolon genişliği birebir; kontrol **normalize-edilmiş** (trim'li, persist edilen) not üzerinde, **herhangi bir DB yazımından önce** → `ValidationFailed` → controller 400 `VALIDATION_ERROR`; off-by-one yok (=2000 kabul, 2001 red, testlerle kanıtlı); 07 §9.4/§9.5 güncel.

**Constructor değişikliği:** `new FraudFlagService(` yalnız 2 test call-site (ikisi de güncel); prod DI (`FraudModule.cs:26`) + `IPaymentAddressAllocator` (`TransactionsModule.cs:165`) çözülür; Fraud→Transactions referansı zaten var (yeni cycle yok). Build temiz teyit eder.

**Güvenlik:** secret sızıntısı yok · auth etkisi yok (AdminFlags approve/reject zaten `MANAGE_FLAGS` gated; retro-scan endpoint'siz background job) · input validation **iyileştirildi** (note max-length) · yeni dış bağımlılık yok (modül-içi `IPaymentAddressAllocator`/`ILogger`).

**Adversarial workflow (11 ajan, 5 boyut refute-default + adversarial verify):** 6 ham aday → **0 onaylı bloke-edici**; 5 çürütüldü (detector idempotency read-then-write race = WP4b-dışı pre-existing, zarar benign; no-wallet filtresi doğru; 02 §14.3 doc ambiguity benign pre-existing; #2 verify-only spec-doğru). 1 "confirmed" = NOTE (flagged-allocation-detail split'inin meşruiyetini **teyit eder** — payment-address yarısı WP4b'de gerçekten teslim edildi, tx-detail yarısı WP13'e meşru forward).

**Validator NOTE (non-blocking follow-up):** allocator'ın non-success status dalı (`FraudFlagService` `TryAllocatePaymentAddressAsync` içinde `Created`/`AlreadyExisted` dışı → yalnız warning log) Fraud tarafında ayrı testle kapsanmıyor; davranışsal olarak test edilen throw-yoluyla özdeş (her ikisi de swallow+log+`EnsurePaymentAddressJob` recovery) → bloke etmez.

**Yapım raporuyla uyum:** Tam uyumlu — AC tablosu, known-limitations (retro-scan dedup granülaritesi/ölçek, tx-detail→WP13, #2 resolved-by-design), test kanıtları bağımsız verdict'le örtüşüyor. (Tek fark: validator tam Fraud projesini koştu 91/91; rapor `--filter` 35/35 koşmuş — ikisi de yeşil, çelişki değil.)

## Altyapı Değişiklikleri

- **Migration:** Yok — EF model değişmedi (`Model_HasNoPendingChanges` PASS). `AdminNote` kolonu zaten `HasMaxLength(2000)`.
- **Config/env değişikliği:** Yok — retro-scan cron const (`0 2 * * *`), SystemSetting eklenmedi.
- **Docker değişikliği:** Yok.
- **Yeni dış bağımlılık:** Yok — `IPaymentAddressAllocator` Transactions modülünde kayıtlı (Fraud→Transactions referansı zaten var); retro-scan job `IBackgroundJobScheduler` (mevcut) üzerinden.

## Commit & PR

- Branch: `task/WP4b-fraud-coverage-completeness`
- Commit: `35fb09f` — WP4b: Fraud kapsam tamlığı — retro-scan + FLAGGED allocation + note-limit + accept-timeout doğrulama
- PR: [#173](https://github.com/turkerurganci/Skinora/pull/173)
- CI: ✓ PASS — HEAD `2357347` run [`27649130700`](https://github.com/turkerurganci/Skinora/actions/runs/27649130700) **tüm job success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate; Guard skipped) — validator doğruladı; önceki `e262cab` run `27648552366` de success. Integration yeşil → 8 Fraud + 2 API testi SQL Server'da geçti; Migration dry-run → model drift-free temiz uygulandı.

## Known Limitations / Follow-up

- **Retro-scan dedup granülaritesi (kaba, owner kararı):** mevcut gate user+type bazlı; admin önceki flag'i REJECT ettikten sonra yeni-distinct link yeniden flag'ler (intended), ama prior flag PENDING/APPROVED iken yeni-distinct link suppress edilir. Finer per-finding dedup MVP-sonrası.
- **Retro-scan ölçek:** günlük tam tarama O(N) `EvaluateAsync` (her biri ~30 sorgu). MVP kullanıcı ölçeğinde kabul edilebilir; çok büyük tabanda watermark-cursor/recently-active filtreleme follow-up (owner "tüm aktif" seçti).
- **tx-detail payment/payout/refund/dispute alt-DTO'ları null** (flagged-allocation-detail'in ikinci yarısı) → **WP13** (FE/DTO kapsamı), bu PR'da değil.
- **#2 resolved-by-design:** ileride çok-instance worker'da poll yükü artarsa per-tx job kararı yeniden değerlendirilebilir (şu an spec poller-driven; tek-instance MVP).

## Notlar

- **Working tree (Adım -1):** temiz (session başı `git status --short` boş).
- **Main CI startup (Adım 0):** son 3 main run success (`27644750670`/`27644750649`/`27577316206`); WP4a PR #172 → main `51b5f57` merge teyit.
- **Dış varsayımlar (Ön-uçuş):** (1) `IPaymentAddressAllocator` Transactions modülünde kayıtlı — `TransactionsModule.cs:165` doğrulandı; (2) allocator idempotent + state-guarded (CREATED/ACCEPTED) — `PaymentAddressAllocator.cs:59-81` doğrulandı; (3) `DeadlineScannerJob` CREATED+AcceptDeadline tarar — `DeadlineScannerJob.cs:99` doğrulandı; (4) Hangfire `IBackgroundJobScheduler.AddOrUpdateRecurring` mevcut — `IBackgroundJobScheduler.cs:58` doğrulandı. Hepsi somut kanıtlı.
- **Anlama fazı:** 7-ajanlı keşif workflow (6 paralel discovery + completeness critic, 673k subagent token) + bağımsız file:line doğrulama; #2 spec-çelişkisi 4/6 ajan tarafından bağımsız tespit edildi → owner'a sunuldu.
