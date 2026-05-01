# T43 — User itibar skoru hesaplama

**Faz:** F2 | **Durum:** ⛔ BLOCKED → **Çözüldü** (proje sahibi kararı: A) | **Tarih:** 2026-05-01

> **Bu rapor BLOCKED kayıt + çözüm karar arşividir.** T43 implementasyon raporu (`✓ Tamamlandı` durumu, kabul kriterleri kanıtları, test sonuçları) yeni yapım chat'inde implementasyon bittiğinde bu dosyaya **eklenecektir** ("Implementasyon Sonucu" başlığı altında); BLOCKED bölümü tarihsel kayıt olarak korunur.

---

## BLOCKED Bilgisi

- **Alt tür:** SPEC_GAP

- **Neden:** Composite `reputationScore` (0-5 ölçekli kullanıcı itibar puanı) hesaplama formülü hiçbir spec dokümanında tanımlı değil. Boşluk üç doküman arasında çelişki üretiyor:

  1. **`Docs/02_PRODUCT_REQUIREMENTS.md` §13** yalnızca girdileri sayar — *"Tamamlanan işlem sayısı, başarılı işlem oranı, platformdaki hesap yaşı"* — ama formülü tanımlamaz.
  2. **`Docs/06_DATA_MODEL.md` §3.1** sadece bileşenlerden biri olan `SuccessfulTransactionRate` formülünü verir (`completed / (completed + cancelled_seller + cancelled_buyer + cancelled_timeout)`). Composite 0-5 skoru ne `User` entity'sinde alan olarak ne de §8.2 denormalized tablosunda görünür.
  3. **`Docs/07_API_DESIGN.md` §5.1, §5.2, §5.5, §6.x** kontratları `"reputationScore": 4.8` (0-5 ölçeği) emit eder, ama backend bu değeri **hangi formülle ürettiği yazılı değildir**. T33 raporu (2026-04-23) bu boşluğu fark edip `reputationScore: null` döndürerek "T43 forward devir" notu bıraktı.

  Üstüne, **T43 plan kabul kriterleri composite skoru explicit talep etmiyor** (`Docs/11_IMPLEMENTATION_PLAN.md` §T43 → 6 madde: count, rate, age, cancel etkisi, wash trading, cooldown). Bu durum kontrat (07) ile plan (11) arasında bir drift'tir: kontrat alanı emit ediyor, plan üretim mantığını istemiyor. Tahminle yazılan formül DB'deki `User.SuccessfulTransactionRate` denormalized değerleri üzerinden T44+ caller'larca persist'e bağlanırsa, formül sonradan değiştiğinde retroactive recompute borcu doğar; bu yüzden formül kararı verilmeden başlanmamalıdır.

- **Etkilenen dokümanlar:**
  - `Docs/02_PRODUCT_REQUIREMENTS.md` §13 (girdi listesi yeterli, formül yok)
  - `Docs/06_DATA_MODEL.md` §3.1 (SuccessfulTransactionRate var, composite score yok), §8.2 (denormalized güncelleme tablosu — composite alan yok)
  - `Docs/07_API_DESIGN.md` §5.1 (UserProfileDto), §5.2 (UserStatsDto), §5.5 (PublicUserProfileDto), §6.x (TransactionDto seller/buyer profilleri içinde `reputationScore: 4.8`)
  - `Docs/11_IMPLEMENTATION_PLAN.md` §T43 (kabul kriterleri composite skoru talep etmiyor — kontrat ile plan arasında drift)

- **Etkilenen task'lar:**
  - **T33** (User profil servisi, ✓ 2026-04-23) — `reputationScore: null` döndürüyor, T43 ile gerçek değere bağlanması bekleniyor (rapor Known Limitations'da explicit forward-devir).
  - **T45** (İşlem oluşturma akışı) — plan T43'e bağımlı (`Docs/11_IMPLEMENTATION_PLAN.md` §T45 bağımlılık satırı: `T44, T34, T43`).
  - **T46–T63** transaction akışları — COMPLETED/CANCELLED state geçişleri sonrası denormalized güncelleme handler'ı çağırır. T43 servisi yoksa bu çağrılar T44+ task'larında açılır kalır.
  - **T93** (Profil sayfaları S08, S09) — UI'da `reputationScore` görüntüleniyor; backend null döndüğünde frontend fallback kuralı belirsiz.
  - **F2 Gate Check** — bu task fazı kapatan son adım; karar verilmeden gate öncesi yeniden tetiklenmesi gerek.

## Çözüm Önerileri

1. **(A) Formül 02/06'da yazılır:** Proje sahibi composite skor formülünü tanımlar (örn. `reputationScore = ROUND(SuccessfulTransactionRate × 5, 1)` + hesap yaşı kuralı) ve `Docs/02_PRODUCT_REQUIREMENTS.md` §13 ile `Docs/06_DATA_MODEL.md` §3.1'e eklenir; T43 plan kabul kriterleri composite skoru içerecek şekilde güncellenir; T43 implement edilir.
2. **(B) `reputationScore` 07'den kaldırılır:** Backend yalnız `successfulTransactionRate` ve `completedTransactionCount` döndürür; UI tarafı hesaplama yapar veya alanı göstermez. 07 §5.1/§5.2/§5.5/§6.x örneklerindeki `4.8` değerleri silinir; T93/T101 frontend tasarımı buna göre netleşir.
3. **(C) Frontend hesaplar (kontrat sınırlı tutulur):** Backend yalnız ham girdileri (count + rate + accountAge) verir; frontend `successfulTransactionRate × 5` veya başka bir kompozit kuralla skoru render eder. 07'de alan input olarak işaretlenir, "computed by client" notu eklenir.

**Önerim — (A):** 07'deki örnekler backend-emit gösteriyor (kontrat sözleşmesi backend'in alanı doldurduğunu ima ediyor); A en az kontrat kırılmasıyla ilerler. Formül kararı verildikten sonra T43 yeni branch'te implement edilir. Mevcut T43 plan kabul kriterleri (count + rate + age + cancel + wash + cooldown) zaten implementable; composite skor maddesi ek olarak plan'a girer ve aynı PR'da implement edilir.

## Proje Sahibi Kararı

- **Karar:** **(A) Formül 02/06'da yazıldı + plan §T43 güncellendi.** Composite formülü:
  ```
  reputationScore =
    IF (accountAgeDays < reputation.min_account_age_days)
       OR (CompletedTransactionCount < reputation.min_completed_transactions)
       OR (SuccessfulTransactionRate IS NULL)
      → null
    ELSE
      → ROUND(SuccessfulTransactionRate × 5, 1)
  ```
  - Tip: `decimal(2,1)` aralık `[0.0, 5.0]`, 1 ondalık.
  - Eşikler SystemSetting: `reputation.min_account_age_days` (default `30`) + `reputation.min_completed_transactions` (default `3`). Kategori `reputation`, admin tarafından runtime ayarlanabilir.
  - Hesaplama yeri: **read path** (denormalized değil) — User entity'sinde alan tutulmaz, `UserProfileService` ve diğer DTO mapper'ları runtime hesaplar (eşikler değişebildiği için).
  - Yuvarlama: `MidpointRounding.ToZero` (truncation, 06 §8.3 finansal kuralla uyumlu).
- **Tarih:** 2026-05-01

## Doküman Yansıması (M2 doc-pass)

| Doküman | Bölüm | Değişiklik |
|---|---|---|
| `Docs/02_PRODUCT_REQUIREMENTS.md` | §13 | Skor ölçeği (0-5, 1 ondalık), formül, yetersiz veri eşikleri (30 gün + 3 işlem), wash trading paydası ve sorumluluk prensibi (06 §3.1 referansı) eklendi |
| `Docs/06_DATA_MODEL.md` | §3.1 | Composite formül + eşik kaynakları (SystemSetting key'leri) + örnek hesaplama tablosu (7 senaryo) + yuvarlama kuralı (`ToZero`) + read-path notu eklendi |
| `Docs/11_IMPLEMENTATION_PLAN.md` | §T43 | 7. kabul kriteri (composite formül + 2 yeni SystemSetting) + 2 yeni doğrulama kontrol listesi maddesi + dokümana 06 §3.1 referansı eklendi |
| `Docs/IMPLEMENTATION_STATUS.md` | Açık Bulgular | M2 → "Kapatılanlar" bölümüne taşındı; T43 satırı `⛔ BLOCKED (SPEC_GAP)` → `⬚ Bekliyor` geri çevrildi |
| `Docs/TASK_REPORTS/T43_REPORT.md` | (bu dosya) | Karar A doldu, BLOCKED → Çözüldü |
| `.claude/memory/MEMORY.md` | Current Status + T43 satırı | BLOCKED unblock + karar A |

## Working Tree + CI Kapı Kontrolü (skill task.md Adım -1, Adım 0)

| Kapı | Sonuç |
|---|---|
| Working tree (Adım -1) | ✓ temiz (`git status --short` — boş çıktı) |
| Main CI startup (Adım 0) | ✓ son 3 run success: `25230739077` (chore #70), `25230739065` (chore #70), `25229419559` (T42 #69) |
| Bağımlılıklar | ✓ T18 ✓ + T19 ✓ — implementation hazır, kontrat boşluğu yüzünden başlanmadı |

## Notlar

- Bu BLOCKED rapor, plan kabul kriterlerinin implementasyon edilebilir olmasına rağmen `reputationScore` kontratının çelişki üretmemesi için **proaktif** olarak açılmıştır. Karar yolu olarak kullanıcı **"Tam C"** seçti: plan kapsamındaki maddeler implement edilebilse bile çelişki kapanmadan T43'e dokunulmaması. Alternatif "C-light" (plan kapsamına daralt + M2 açık bulgu olarak bayrakla) önerildi ama kabul edilmedi.
- F2 Gate Check'in tetiklenebilmesi için karar zorunluydu — **2026-05-01'de proje sahibi karar A'yı verdi:** ana formül `ROUND(rate × 5, 1)` + iki SystemSetting eşiği (30 gün hesap yaşı + 3 işlem). Eşik altı veya rate=null durumunda skor null döner ("Yeni kullanıcı" UI durumu).
- T43 implementasyon **yeni bir yapım chat'inde** baştan başlar (bu PR'ın merge'inden sonra). İmplementasyon raporu bu dosyaya "Implementasyon Sonucu" başlığı altında eklenecek; BLOCKED + Karar bölümleri tarihsel arşiv olarak korunacak.

## Commit & PR

- Branch: `task/T43-blocked-spec-gap`
- Commit: `42e5420` — "T43: BLOCKED (SPEC_GAP) — composite reputationScore formülü tanımsız" (rapor + status + memory + M2 açık bulgu, kod yok)
- PR: [#71](https://github.com/turkerurganci/Skinora/pull/71)
- CI: ✓ PASS — run [`25231718147`](https://github.com/turkerurganci/Skinora/actions/runs/25231718147) (Detect changed paths ✓ + Lint ✓ + CI Gate ✓; Build/Unit/Integration/Contract/Migration/Docker paths-filter ile doc-only PR'da skip — beklenen davranış)
- Branch isolation (Layer 3): ✓ temiz — `git log main..HEAD` yalnızca T43 commit'i

## Bitiş Kapısı (skill task.md — BLOCKED edition)

BLOCKED akışında skill 8-madde kapısının kod-merkezli maddeleri (build/test) uygulanamaz; aşağıdaki adapte versiyon uygulandı:

- [x] Branch push edildi mi? — `task/T43-blocked-spec-gap` push'landı
- [x] PR açıldı mı? — PR [#71](https://github.com/turkerurganci/Skinora/pull/71)
- [x] PR numarası rapor footer'a yazıldı mı? — bu bölüm
- [x] Rapor + status push edildi mi? — `42e5420` ile aynı commit
- [x] CI run tamamlandı mı? — run `25231718147` concluded
- [x] CI run sonucu success mi? — ✓ PASS (CI Gate yeşil)
- [x] Branch izolasyon check temiz mi? — yalnızca T43 commit subject (`git log main..HEAD --format='%s' | grep -oE '^T[0-9]+...'` → `T43`)
- [x] Repo memory'de T43 satırı eklendi/güncellendi mi? — `.claude/memory/MEMORY.md` Current Status + Next + T43 detay satırı eklendi

---

# Implementasyon Sonucu

**Faz:** F2 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekleniyor) | **Tarih:** 2026-05-02

## Yapılan İşler

- **Composite reputation skoru (read path)** — `IReputationScoreCalculator` + `ReputationScoreCalculator` (`Skinora.Users.Application.Reputation`). 06 §3.1 formülü `ROUND(SuccessfulTransactionRate × 5, 1)` `MidpointRounding.ToZero` ile (06 §8.3 finansal yuvarlama kuralı). Eşik altı veya rate=null → `null` ("Yeni kullanıcı").
- **Eşik provider** — `IReputationThresholdsProvider` (port, `Skinora.Users`) + `ReputationThresholdsProvider` (impl, `Skinora.Platform.Infrastructure.Reputation`, SystemSetting okuyucu). `reputation.min_account_age_days` (default 30) + `reputation.min_completed_transactions` (default 3). Admin update'leri restart gerektirmez (her çağrıda re-read).
- **Denormalized aggregator (write path)** — `IReputationAggregator` + `ReputationAggregator` (`Skinora.Transactions.Application.Reputation`). Per-user `RecomputeAsync(userId)` Transaction + TransactionHistory'i okur, sorumluluk haritalama (06 §3.1) + wash trading filter (02 §14.1) uygular, `User.CompletedTransactionCount` + `User.SuccessfulTransactionRate`'i günceller. UoW disiplini: tracked entity mutate eder, SaveChanges caller'da.
- **Wash trading filter** — `WashTradingFilter` (`Skinora.Users.Application.Reputation`). Pure helper. Unordered (sellerId, buyerId) çifti üzerinde 1 ay (30 gün) penceresi. İlk işlem her zaman sayılır; sonrakiler son sayılan'a 30+ gün uzakta ise sayılır. Filter denominator + numerator'ı eşit etkiler ("skor etkisi kaldırılır", 02 §14.1) — `CompletedTransactionCount`'a uygulanmaz (raw count, 02 §13).
- **Sorumluluk haritalama** — `CANCELLED_SELLER` → seller, `CANCELLED_BUYER` → buyer, `CANCELLED_TIMEOUT` → `PreviousStatus`'e göre (CREATED→buyer, ACCEPTED|TRADE_OFFER_SENT_TO_SELLER→seller, ITEM_ESCROWED→buyer, TRADE_OFFER_SENT_TO_BUYER→buyer; eşleşmeyen → no-effect), `CANCELLED_ADMIN` → her iki tarafa skip.
- **Cancel cooldown evaluator** — `IUserCancelCooldownEvaluator` + `CancelCooldownEvaluator` (`Skinora.Transactions`) + `ICancelCooldownThresholdsProvider` + `CancelCooldownThresholdsProvider` (`Skinora.Platform`). Pencere içinde sorumluluk-attributed iptal sayar, limit aşıldıysa `User.CooldownExpiresAt = now + cancel_cooldown_hours`. Eşik 0 ise rule disabled (no-op, partial-bootstrap güvenli).
- **SystemSetting seed** — 32 → 34 satır: `reputation.min_account_age_days = 30`, `reputation.min_completed_transactions = 3` (kategori `Reputation`, IsConfigured=true). EF Core `HasData` kanalıyla seed'lendi → migration `20260501210909_T43_AddReputationThresholds` (yalnız 2 `InsertData`, schema değişikliği yok).
- **SystemSettingsCatalog** — 2 yeni metadata (ApiCategory: `reputation`, label + unit `gün`/`adet`).
- **T33 entegrasyonu** — `UserProfileService` (3 endpoint: `/users/me`, `/users/me/stats`, `/users/{steamId}`) `IReputationScoreCalculator` injected; `reputationScore` artık composite score, `cancelRate = 1 - successfulTransactionRate` (07 §5.1 example uyumlu, M1 fraction kanonik). T33'ün null devri kapandı.
- **DI** — `UsersModule.cs`'e 5 yeni kayıt eklendi (composite root, cross-module glue pattern).

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (12):**
- `backend/src/Modules/Skinora.Users/Application/Reputation/`
  - `IReputationScoreCalculator.cs` + `ReputationScoreCalculator.cs`
  - `IReputationThresholdsProvider.cs` (record `ReputationThresholds`)
  - `IReputationAggregator.cs` (record `ReputationSnapshot`)
  - `IUserCancelCooldownEvaluator.cs` (record `CooldownEvaluationResult`)
  - `ICancelCooldownThresholdsProvider.cs` (record `CancelCooldownThresholds`)
  - `WashTradingFilter.cs` (record struct `WashTradingResult<T>`)
- `backend/src/Modules/Skinora.Platform/Infrastructure/Reputation/`
  - `ReputationThresholdsProvider.cs`
  - `CancelCooldownThresholdsProvider.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Reputation/`
  - `ReputationAggregator.cs`
  - `CancelCooldownEvaluator.cs`
- `backend/src/Skinora.Shared/Persistence/Migrations/20260501210909_T43_AddReputationThresholds.cs` (+ Designer)

**Yeni test dosyaları (4):**
- `backend/tests/Skinora.Users.Tests/Unit/Reputation/ReputationScoreCalculatorTests.cs` (10 test)
- `backend/tests/Skinora.Users.Tests/Unit/Reputation/WashTradingFilterTests.cs` (7 test)
- `backend/tests/Skinora.Transactions.Tests/Integration/Reputation/ReputationAggregatorTests.cs` (9 test)
- `backend/tests/Skinora.Transactions.Tests/Integration/Reputation/CancelCooldownEvaluatorTests.cs` (5 test)

**Değişen dosyalar (9):**
- `Skinora.Users/Application/Profiles/UserProfileService.cs` — calculator entegrasyonu, cancelRate hesabı
- `Skinora.Users/Application/Profiles/UserProfileDtos.cs` — `T43 forward devir` notu kaldırıldı
- `Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs` — 2 yeni metadata
- `Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs` — 32→34 satır
- `Skinora.API/Configuration/UsersModule.cs` — 5 yeni DI kaydı
- `Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` — auto-regenerated
- `tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs` — count 32→34, configured listesi güncel
- `tests/Skinora.API.Tests/Integration/UserProfileEndpointTests.cs` — 3 mevcut test reputationScore non-null bekler, +1 yeni test (below-threshold)
- `tests/Skinora.Transactions.Tests/Skinora.Transactions.Tests.csproj` — `Microsoft.Extensions.TimeProvider.Testing` 9.0.0 eklendi

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Tamamlanan işlem sayısı denormalized güncelleme (COMPLETED'da) | ✓ | `ReputationAggregator.RecomputeAsync` raw COMPLETED sayısını `User.CompletedTransactionCount`'a yazar (wash filter NOT uygulanır). Test: `Recompute_All_Completed_Yields_Rate_One` (3 COMPLETED → count=3) + `Recompute_Wash_Trading_Removes_Repeat_Pair_From_Rate_Denominator` (3 COMPLETED → count=3 raw) |
| 2 | Başarılı işlem oranı (sorumluluk bazlı) | ✓ | `ResponsibilityFor` + `ResponsibleForTimeout`. Testler: `Recompute_Cancelled_Seller_Counts_Against_Seller_Only`, `Recompute_Cancelled_Timeout_Maps_To_Responsible_Party` (PreviousStatus=ITEM_ESCROWED→buyer), `Recompute_Cancelled_Timeout_Step3_Hits_Seller` (PreviousStatus=ACCEPTED→seller), `Recompute_Cancelled_Admin_Excludes_Both_Parties` |
| 3 | Hesap yaşı hesaplama | ✓ | `ReputationScoreCalculator.ComputeAsync` accountAge threshold check. Test: `Compute_When_Threshold_Fails_Returns_Null` (10 günlük hesap → null) + `Compute_Picks_Up_Threshold_Updates_On_Each_Call` |
| 4 | İptal oranı skoru etkiliyor | ✓ | Inherent — denominator'a sorumlu iptaller eklenir. Test: `Recompute_Cancelled_Seller_Counts_Against_Seller_Only` (1 success / 2 attempts = 0.5) |
| 5 | Wash trading 1 ay penceresi | ✓ | `WashTradingFilter.Apply`. Unit: `Same_Pair_Within_Window_Drops_Subsequent_Rows`, `Wash_Window_Restarts_From_Last_Counted_Not_From_First_Counted`, `Pair_Order_Does_Not_Matter`, `Different_Pairs_Are_Tracked_Independently`. Integration: `Recompute_Wash_Trading_Hides_Cancelled_From_Denominator` |
| 6 | CooldownExpiresAt hesaplama | ✓ | `CancelCooldownEvaluator.EvaluateAsync`. Test: `Exceeding_Limit_Stamps_New_CooldownExpiresAt`, `Below_Limit_Leaves_CooldownExpiresAt_Untouched`, `Cancellations_Outside_Window_Are_Ignored`, `Cancellations_For_Other_Party_Do_Not_Count`, `Disabled_Threshold_Returns_Zero_And_Skips_Update` |
| 7 | Composite reputationScore (06 §3.1) read path + 2 SystemSetting | ✓ | `ReputationScoreCalculator` + 3 endpoint entegrasyonu. SystemSettings 32→34. Tests: `ReputationScoreCalculatorTests.Compute_When_Eligible_Returns_Composite_Score` 4 senaryo (06 §3.1 örnek tablosu birebir: 4.8/5.0/4.0/2.5), `Compute_Truncates_Toward_Zero_Per_06_8_3_Financial_Rounding` (0.964→4.8), `SeedDataTests.Seed_SystemSettings_Has_34_Rows_With_Unique_Keys`, `UserProfileEndpointTests.GetMe_Authenticated_ReturnsOwnProfile` (4.8 + cancelRate 0.04), `GetPublic_ExistingUser_ReturnsLimitedProfile` (4.5), `GetMe_NewAccount_Below_Reputation_Thresholds_ReturnsNullScore` |

## Doğrulama Kontrol Listesi (plan §T43)

- [x] **02 §13 skor kriterleri (formül + eşikler) uygulanmış mı?** — Calculator formülü ve eşikleri 02 §13'le birebir.
- [x] **06 §3.1 composite reputationScore formülü ve örnek tablosu birebir doğrulandı mı?** — 7 senaryo birebir test edildi.
- [x] **06 §8.2 denormalized field güncelleme kuralları doğru mu?** — Aggregator idempotent, eventual consistency.
- [x] **`reputation.min_account_age_days` + `reputation.min_completed_transactions` SystemSetting seed (default 30 / 3) ve catalog/validator entry'leri eklendi mi?** — Seed 33,34. satırlar Default(30)/Default(3); Catalog ApiCategory `reputation`; Validator generic positive-int kuralı yeterli.
- [x] **T33 UserProfileService null devri composite hesaplamayla kapandı mı? UserProfileEndpointTests güncellendi mi?** — 3 endpoint composite skor + cancelRate döner; UserProfileDtos xmldoc güncellendi; UserProfileEndpointTests 3 test güncellendi + 1 below-threshold testi eklendi.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Reputation) | ✓ 17/17 passed | `Skinora.Users.Tests` Reputation namespace — 141 ms |
| Integration (Reputation) | ✓ 14/14 passed | `Skinora.Transactions.Tests` Reputation namespace — 5 s |
| Integration (Endpoints) | ✓ 6/6 passed | `Skinora.API.Tests.UserProfileEndpointTests` |
| Tüm test suite | ✓ PASS | Tüm 11 test assembly yeşil |
| Build | ✓ 0W/0E (Release) | `dotnet build -c Release` |
| Format | ✓ 0 değişiklik | `dotnet format --verify-no-changes` |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Beklemede (validator bağımsız chat) |

## Altyapı Değişiklikleri

- **Migration:** Var — `20260501210909_T43_AddReputationThresholds.cs`. Sadece 2 `InsertData` (SystemSettings tablosuna), `Down` 2 `DeleteData`. Schema/index değişikliği yok.
- **Config/env:** Yok. Yeni 2 SystemSetting `IsConfigured=true` default değerle seed.
- **Docker:** Yok.
- **Yeni paket:** `Microsoft.Extensions.TimeProvider.Testing` 9.0.0 (yalnız `Skinora.Transactions.Tests`).

## Commit & PR

- Branch: `task/T43-user-reputation`
- Commit: `7bc3130` — T43: User itibar skoru hesaplama
- PR: [#72](https://github.com/turkerurganci/Skinora/pull/72)
- CI: ✓ PASS (run [25234165174](https://github.com/turkerurganci/Skinora/actions/runs/25234165174), 9/9 job — Guard skipped beklenen)

## Known Limitations / Follow-up

- **Tetikleme/üretici tarafı (T44+ devir):** `IReputationAggregator.RecomputeAsync` ve `IUserCancelCooldownEvaluator.EvaluateAsync` çağrılma noktaları (Transaction state-machine OnEntry hook veya Outbox event handler) bu task kapsamında DEĞİL. T44 (state machine) ve sonraki task'larda her COMPLETED / CANCELLED_* transition sonrası iki servis çağrılır. Aggregator + cooldown idempotent (re-run aynı sonucu üretir).
- **Wash trading "1 ay" tanımı:** `WashTradingFilter.WashTradingWindow = 30 gün` olarak sabitlendi. 02 §14.1 "1 ay" diyor — calendar month yerine 30 gün seçildi (deterministik aritmetik).
- **CANCELLED_TIMEOUT PreviousStatus boşsa:** TransactionHistory'de bu Transaction için CANCELLED_TIMEOUT row'u yoksa veya PreviousStatus null ise transaction "no-effect" sayılır (denominator'a girmez). Production'da T44 state machine her transition için TransactionHistory yazacak.
- **Threshold provider cache yok:** Her çağrıda DB'ye gider. Read endpoint'leri için negligible.

## Notlar

- **Working tree (Adım -1):** temiz.
- **Main CI startup (Adım 0):** son 3 run hepsi `success` (25232475624, 25232475615, 25230739077).
- **Dış varsayım kontrolü:** Yok. Saf hesaplama + mevcut entity'ler + EF Core HasData seed mekanizması (T28+T26+T41).
- **Mimari hizalama:** Aggregator + threshold provider impl'leri doğru module'e yerleştirildi (Transactions ve Platform), Skinora.Users "leaf" pozisyonunu korudu. T34 IActiveTransactionCounter ve T26 SettingsBootstrapService pattern'leri devralındı.
- **CancelRate kanonikleşmesi:** 07 §5.1 `cancelRate: 0.04` örneği `successfulTransactionRate: 0.96`'nın komplemanı (M1 closure 2026-05-01 fraction kanonik). Implementation `1 - rate`.
- **Test isolation çözümü:** `CancelCooldownEvaluatorTests` ve `ReputationAggregatorTests` ilk drafta CHECK constraint `CK_Transactions_Cancel`'a takıldı (CancelledBy + CancelReason zorunluluğu). Helper'lara mapping eklendi.
- **F2 Gate Check:** Bu task F2 fazının son task'ı (T29-T43). Validator PASS sonrası F2 Gate Check tetiklenebilir.

