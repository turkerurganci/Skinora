# T55 — AML Kontrolü (fiyat sapması, yüksek hacim, dormant hesap)

**Faz:** F3 | **Durum:** ⏳ Yapım bitti — validator bekliyor | **Tarih:** 2026-05-04

---

## Yapılan İşler

T54 fraud flag write-port + T45 transaction creation pipeline üzerine üç AML kuralı bağlandı (02 §14.4 + §14.3, 03 §7.1–§7.2):

1. **`FraudDetectionCalculator` (saf hesaplama, T52 `FinancialCalculator` paterni)** — `Skinora.Transactions/Domain/Calculations/FraudDetectionCalculator.cs`:
   - `CalculatePriceDeviation(quoted, market) → decimal?` — `|quoted-market|/market`; null/0/negatif market price → null (rule disabled).
   - `IsHighVolume(recentCount, recentTotal, countThreshold, amountThreshold) → bool` — strict greater-than her iki eşikte; `null`/0/negatif eşik = ilgili branch disabled (defansif: admin yanlış değer girerse her transaction flag'lenmez).
   - `IsDormantAnomaly(completedCount, ageDays, attemptedAmount, minAgeDays, valueThreshold) → bool` — üç koşul birlikte: `completedCount==0` AND `ageDays>=minAgeDays` AND `attemptedAmount>valueThreshold`. `valueThreshold<=0` → rule disabled.

2. **`IFraudPreCheckService` interface refactor** — `Skinora.Transactions/Application/Lifecycle/IFraudPreCheckService.cs`:
   - `EvaluateAsync` artık `Guid sellerId` + `DateTime nowUtc` alır (per-seller volume sorgu + dormant age hesabı için).
   - `FraudPreCheckOutcome` shape'i değişti: eski `(ShouldFlag, MarketPrice, DeviationRatio?, ThresholdRatio?)` → yeni `(ShouldFlag, MarketPrice, FraudFlagType? FlagType, string? FlagDetailsJson)`. Pre-check service tüm rule-specific JSON shaping'i içeride yapıyor; orchestrator sadece relay ediyor.

3. **`FraudPreCheckService` 3-rule pipeline** — `Skinora.Transactions/Application/Lifecycle/FraudPreCheckService.cs`:
   - Sıra: PRICE_DEVIATION → HIGH_VOLUME → ABNORMAL_BEHAVIOR; ilk eşleşen kazanır (single FraudFlag per FLAGGED transaction — admin queue tek atomik karar görür).
   - Market price snapshot up-front (`Transaction.MarketPriceAtCreation` rule trip etmese bile dolar).
   - HIGH_VOLUME: `_db.Set<Transaction>().Where(SellerId==X && !IsDeleted && CreatedAt>=cutoff).GroupBy(_=>1).Select(g=> new {Count, Sum(TotalAmount)})` tek round-trip; FLAGGED + CANCELLED_* rows dahil çünkü "sudden burst of cancellations" da signal değerinde.
   - ABNORMAL_BEHAVIOR (dormant): `_db.Set<User>().Where(Id==X).Select(new {CreatedAt, CompletedTransactionCount})` projection; `accountAgeDays = max(0, (nowUtc - user.CreatedAt).TotalDays)`.
   - 6 yeni `public const string` setting key (`DeviationThresholdKey`, `HighVolumeAmountThresholdKey`, `HighVolumeCountThresholdKey`, `HighVolumePeriodHoursKey`, `DormantMinAgeDaysKey`, `DormantValueThresholdKey`).
   - `ReadDecimalSettingAsync` / `ReadIntSettingAsync` private helpers — `IsConfigured=false` row → `null` (rule disabled).
   - JSON payload shape'leri 07 §9.3 ile birebir: `PriceDeviationFlagDetail` (inputPrice, marketPrice, deviationPercent), `HighVolumeFlagDetail` (periodHours, transactionCount, totalVolume), `AbnormalBehaviorFlagDetail` (pattern: `"DORMANT_ACCOUNT_HIGH_VALUE"`, description Türkçe).

4. **`TransactionCreationService` wire-up** — `Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs`:
   - `nowUtc` 1 satır yukarı taşındı; `_fraudPreCheck.EvaluateAsync(sellerId, ..., nowUtc, ct)` çağrısı.
   - Stage 9 simplify edildi: anonymous-object JSON build kaldırıldı, doğrudan `outcome.FlagType.Value + outcome.FlagDetailsJson` ile `_flagWriter.StagePreCreateFlagAsync`.
   - `CreateTransactionResponse.FlagReason` artık hard-coded `"PRICE_DEVIATION"` değil, `outcome.FlagType?.ToString()` (HIGH_VOLUME / ABNORMAL_BEHAVIOR / null).
   - `using System.Text.Json;` kaldırıldı (artık kullanılmıyor).

5. **2 yeni SystemSetting** (T55):
   - `dormant_account_min_age_days` (int, default `30`, configured) — minimum hesap yaşı dormant kuralı için. Bu eşiğin altında hesap "yeni hesap" sayılır → T39 yeni hesap limitleri uygulanır; bu eşiğin üzerinde 0 işlemli hesabın yüksek tutarlı denemesi flag tetikler.
   - `dormant_account_value_threshold` (decimal, **unconfigured** — admin tarafından risk profiline göre belirlenir).
   - `SystemSettingsCatalog.cs` 2 yeni entry (`fraud_detection` API category, Türkçe label).
   - `SystemSettingSeed.cs` index 35 + 36.
   - Migration: `20260504172426_T55_AddDormantAccountFraudSettings` — sadece `InsertData` × 2 row (`Up`) + `DeleteData` × 2 (`Down`); şema değişikliği yok.

6. **27 yeni unit test** — `backend/tests/Skinora.Transactions.Tests/Unit/Calculations/FraudDetectionCalculatorTests.cs`:
   - PriceDeviation × 7 (zero, double-market, half-market, null/zero/negative market, theory 4 known pair).
   - HighVolume × 9 (both null disabled, count above/at threshold strict, amount above/at threshold strict, OR semantic, zero-threshold disabled both).
   - DormantAnomaly × 8 (all conditions met, has prior trades, new account, amount at/just-above threshold, age at min threshold, zero/negative valueThreshold).

7. **5 yeni integration test** — `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCreationServiceTests.cs`:
   - `Flags_Transaction_When_High_Volume_Count_Threshold_Exceeded` (6 prior tx within 24h, threshold 5).
   - `Flags_Transaction_When_High_Volume_Amount_Threshold_Exceeded` (3 × 5,000 USDT = 15K, threshold 10K).
   - `Does_Not_Flag_For_High_Volume_When_Prior_Transactions_Outside_Window` (5 tx 48h ago, window 24h).
   - `Flags_Transaction_For_Dormant_Account_Anomaly` (90-day-old seller + 0 completed + 100 USDT attempt, threshold 50).
   - `Does_Not_Flag_New_Account_For_Dormant_Anomaly` (fresh seller, age below min threshold).
   - 3 helper: `ConfigureHighVolumeAsync` (3 setting writes), `SeedRecentTransactionsAsync` (two-stage save: Add+Save → Modify CreatedAt+Save; T33 yapım notu pattern), `BackdateSellerAsync` (Modified state ile audit pipeline'ı bypass).
   - Backfill transactions COMPLETED status'ünde (T39 max_concurrent eligibility check'i tetiklemesin diye; high-volume rolling window status-agnostic).

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Transactions/Domain/Calculations/FraudDetectionCalculator.cs` (~95 satır)
- `backend/src/Skinora.Shared/Persistence/Migrations/20260504172426_T55_AddDormantAccountFraudSettings.cs`
- `backend/src/Skinora.Shared/Persistence/Migrations/20260504172426_T55_AddDormantAccountFraudSettings.Designer.cs`
- `backend/tests/Skinora.Transactions.Tests/Unit/Calculations/FraudDetectionCalculatorTests.cs` (27 test)

**Değişiklik:**
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/IFraudPreCheckService.cs` — outcome shape + signature.
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/FraudPreCheckService.cs` — full rewrite (3-rule pipeline; ~200 satır).
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs` — `using` cleanup + nowUtc reorder + EvaluateAsync params + Stage 9 simplify + FlagReason mapping.
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs` — 2 yeni entry.
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs` — index 35 + 36 + section comment.
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` — 2 yeni HasData row (auto-generated).
- `backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs` — count 34→36, unconfigured 20→21, configured key list (+ `dormant_account_min_age_days` alphabetic), section comments.
- `backend/tests/Skinora.Platform.Tests/Integration/SettingsBootstrapTests.cs` — `SKINORA_SETTING_DORMANT_ACCOUNT_VALUE_THRESHOLD` env var, "20→21 mandatory rows" comment.
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCreationServiceTests.cs` — 5 yeni @Fact + 3 helper.
- `Docs/02_PRODUCT_REQUIREMENTS.md` — §14.4 (high-volume detail clarification + dormant rule paragrafı + öncelik sırası) + §16.2 (dormant satırı + yüksek hacim satırı detaylandırma) + version v2.5 → v2.6.

**Migration:** `20260504172426_T55_AddDormantAccountFraudSettings` — InsertData × 2 SystemSetting row. Şema değişikliği yok. **SystemSetting:** 2 yeni anahtar. **Yeni dış paket:** Yok. **Yeni env var:** `SKINORA_SETTING_DORMANT_ACCOUNT_VALUE_THRESHOLD` (mandatory; admin UI veya env ile bootstrap zorunlu — fail-fast).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Piyasa fiyatından sapma eşiği kontrolü (işlem oluşturma anında) | ✓ | `FraudPreCheckService.EvaluatePriceDeviationAsync` `FraudDetectionCalculator.CalculatePriceDeviation` ile + `price_deviation_threshold` SystemSetting okuması. T54'ten korunan davranış: `Flags_Transaction_When_Price_Deviation_Exceeds_Threshold` (quoted 100, market 50 → 100% deviation > 20% threshold → FLAGGED). |
| 2 | Eşik aşılırsa → FLAGGED (pre-create), timeout başlamaz | ✓ | `TransactionCreationService` Stage 8 `Status = fraud.ShouldFlag ? FLAGGED : CREATED`; Stage 9 `AcceptDeadline = status==CREATED ? deadline : null`. Test: `persisted.AcceptDeadline` null assertion her FLAGGED test'te (PRICE_DEVIATION + HIGH_VOLUME + ABNORMAL_BEHAVIOR). |
| 3 | Kısa sürede yüksek hacim tespiti → flag | ✓ | `FraudPreCheckService.EvaluateHighVolumeAsync` `high_volume_period_hours` window içinde GroupBy(Count, Sum) sorgusu + `FraudDetectionCalculator.IsHighVolume` (count veya amount strict > threshold). Test: `Flags_Transaction_When_High_Volume_Count_Threshold_Exceeded` (6 tx > 5) + `Flags_Transaction_When_High_Volume_Amount_Threshold_Exceeded` (15K > 10K) + `Does_Not_Flag_For_High_Volume_When_Prior_Transactions_Outside_Window` (cutoff guard). |
| 4 | Dormant hesap anomali tespiti: hesap yaşı vs işlem hacmi orantısızlığı | ✓ | `FraudPreCheckService.EvaluateDormantAnomalyAsync` `User.CompletedTransactionCount` + `(nowUtc - User.CreatedAt).Days` + `FraudDetectionCalculator.IsDormantAnomaly`. Test: `Flags_Transaction_For_Dormant_Account_Anomaly` (90 gün, 0 tx, 100 USDT > 50 USDT eşik → ABNORMAL_BEHAVIOR) + `Does_Not_Flag_New_Account_For_Dormant_Anomaly` (yeni hesap, dormant age altı → CREATED). |
| 5 | Eşikler admin tarafından SystemSetting'den okunur | ✓ | 6 setting key (`price_deviation_threshold`, `high_volume_amount_threshold`, `high_volume_count_threshold`, `high_volume_period_hours`, `dormant_account_min_age_days`, `dormant_account_value_threshold`); hepsi `SystemSettingsCatalog`'a kayıtlı (07 §9.8 admin UI'da görünür) + `SystemSettingSeed`'de seeded; T55 öncesi 4 anahtar zaten vardı, T55 dormant için 2 ekledi. `IsConfigured=false` rule disabled semantiği `Read*SettingAsync` helper'larında. |

## Doğrulama Kontrol Listesi

- [✓] **02 §14.4 AML kuralları eksiksiz mi?** — `Docs/02_PRODUCT_REQUIREMENTS.md` §14.4 güncellendi (v2.6): high-volume için "tutar veya işlem sayısı; hangisi önce aşılırsa flag", dormant rule için ayrı bullet (min hesap yaşı + tek işlem tutar eşiği + T39 ayrımı), öncelik sırası açıklaması (PRICE_DEVIATION → HIGH_VOLUME → ABNORMAL_BEHAVIOR, ilk match wins). §16.2 admin parametreleri tablosunda yüksek hacim eşikleri detaylandırıldı + dormant satırı eklendi.
- [✓] **Dormant hesap anomali tespiti çalışıyor mu?** — Unit: `FraudDetectionCalculatorTests.DormantAnomaly_*` 8 test (all conditions, has prior trades, new account, threshold edges, disabled rule). Integration: `Flags_Transaction_For_Dormant_Account_Anomaly` + `Does_Not_Flag_New_Account_For_Dormant_Anomaly` (90 gün vs 0 gün hesap, aynı 100 USDT denemesi → biri FLAGGED biri CREATED).

## Test Sonuçları

| Suite | Sonuç |
|---|---|
| `FraudDetectionCalculatorTests` (yeni unit suite) | **27/27 PASS** — 165 ms |
| `TransactionCreationServiceTests` (T45 + T54 + T55 birlikte) | **16/16 PASS** (5 yeni T55 test + 11 mevcut) — 13 sn |
| `Skinora.Transactions.Tests` total | **543/543 PASS** — 1 m 10 s |
| `Skinora.Platform.Tests` total | **141/141 PASS** (SeedDataTests count + SettingsBootstrapTests env var update) — 46 sn |
| `Skinora.Fraud.Tests` total | **34/34 PASS** (T54 davranış korundu) — 1 m 7 s |
| `Skinora.API.Tests` total | **280/280 PASS** — 3 m 34 s |
| `Skinora.Auth.Tests` total | **93/93 PASS** |
| `Skinora.Notifications.Tests` total | **77/77 PASS** |
| `Skinora.Steam.Tests` total | **21/21 PASS** |
| `Skinora.Disputes.Tests` total | **11/11 PASS** |
| `Skinora.Admin.Tests` total | **20/20 PASS** |
| `Skinora.Payments.Tests` total | **6/6 PASS** |
| `Skinora.Users.Tests` total | **16/16 PASS** |
| `Skinora.Shared.Tests` total | **192/192 PASS** |
| **Solution sweep** | **1434/1434 PASS** (12 test projesi) |
| `dotnet build Skinora.sln -c Release` | **0 Warning(s), 0 Error(s)** (Time Elapsed 10 sn) |
| Migration generation (`dotnet ef migrations add`) | success — 2 InsertData + 2 DeleteData; iki MSEC informational warning ("global query filter on principal end") T54 öncesi de mevcuttu, T55 ile ilgisiz. |

## Notlar

- **Working tree:** Adım -1 check temiz (`git status --short` boş; main'e geçiş + pull, sonra `task/T55-aml-control` branch'i açıldı).
- **Main CI startup check:** Adım 0 ✓ — son 3 main run hepsi `success` (T54 #85 ×2 + T53 #84; CI run ID 25332031332, 25332031310, 25289592150).
- **Bağımlılık:** T54 ✓ (FraudFlag staging port + ITransactionFraudFlagWriter adapter). T39 (eligibility) korundu; high-volume aggregation ve dormant rule yeni hesap limitleri ile çakışmaz çünkü `dormant_account_min_age_days` minimum yaş gate'i ekler.
- **Dış varsayımlar (Adım 4):**
  - `IMarketPriceProvider` interface T54'ten mevcut + `NullMarketPriceProvider` stub kullanılıyor (T81 Steam Market entegrasyonu real provider'ı DI swap ile devralır) → ✓ doğrulandı (T55 yeni provider yazmaz).
  - `IFraudFlagService.StageTransactionFlagAsync` + `ITransactionFraudFlagWriter` adapter T54'te yazılmış → ✓ doğrulandı.
  - `FraudFlagType` enum'da `HIGH_VOLUME` + `ABNORMAL_BEHAVIOR` tanımlı → ✓ doğrulandı (`backend/src/Skinora.Shared/Enums/FraudFlagType.cs`).
  - 07 §9.3 `HighVolumeFlagDetail` + `AbnormalBehaviorFlagDetail` DTO shape'leri T54'te yazılmış → ✓ doğrulandı (`Skinora.Fraud/Application/Flags/FraudFlagDtos.cs:96-105`).
  - `User.CompletedTransactionCount` + `User.CreatedAt` (BaseEntity) field'ları → ✓ doğrulandı (`Skinora.Users/Domain/Entities/User.cs:43`, `Skinora.Shared/Domain/BaseEntity.cs:6`).
  - `dotnet ef migrations add` Skinora.Shared persistence project üzerinden çalışıyor → ✓ doğrulandı (T43 paterni mirror).
- **Atomicity:** Pre-check service okur (read-only DB queries); orchestrator stage 8'de Transaction.Add → stage 9'da `_flagWriter.StagePreCreateFlagAsync` (caller-owned SaveChanges) → stage 10'da outbox publish → stage SaveChanges. Transaction insert + FraudFlag insert + outbox + audit log tek commit'te. T54 atomicity contract korunur.
- **Priority order rationale:** PRICE_DEVIATION en spesifik (item-level numerical deviation, false positive düşük); HIGH_VOLUME mid-grain (rolling window, periyot bağımlı); ABNORMAL_BEHAVIOR en geniş (behavioral pattern, false positive en yüksek). İlk match wins → admin queue tek atomik karar görür; çoklu flag persist'i 02 §14.0 + 06 §3.12 invariantı bozardı.
- **HighVolume status-agnostic aggregation:** Rolling window CANCELLED_* + FLAGGED rows dahil. Rasyonel: bir satıcının ardı ardına iptal etmesi de bir signal değerinde (T57 wash-trading + T56 multi-account ile birlikte değerlendirilecek future detection patterns); IsDeleted hard-exclude.
- **Dormant rule "complete transactions" semantic:** `User.CompletedTransactionCount` (T43 denormalized field) kullanılıyor — sayım: yalnızca `Status==COMPLETED` rows. Henüz tamamlanmamış (CREATED/ACCEPTED/etc.) işlemleri "trade" saymıyoruz çünkü dormant rule'un mantığı "hiçbir işlemi tamamlamamış hesap" semantiğine dayanıyor. Edge case: kullanıcının tamamlanmış işlemi yok ama açık birden fazla CREATED'ı varsa dormant olarak değerlendirilir — bu kasıtlı, çünkü gerçek trade tamamlanmamış (potansiyel "hızlı yığma" senaryosu).
- **Dormant rule edge case — `dormant_account_min_age_days < auth.min_steam_account_age_days`:** Auth gate (T30) zaten 30 gün altı Steam hesabını login'de bloklar. Default `dormant_account_min_age_days = 30` aynı eşiği yansıtıyor — login'i geçen her hesap dormant kontrolüne tabi. Admin daha yüksek bir eşik isterse (örn. 60 gün) login geçip dormant rule kapsamı dışı kalan 30-60 günlük hesaplar olur (kasıtlı: yeni hesap T39 limit'i bu pencerede aktif, çakışma yok).
- **Dormant rule message dil seçimi:** `description` field'ı Türkçe ("Hesap N gündür açık ve hiç tamamlanmış işlemi yok; denenen işlem tutarı X eşik Y üzerinde."). Admin UI 07 §9.3 bu field'ı doğrudan render ediyor (`flagDetail.description`). T97 i18n forward-devir.
- **Forward-devir / Known limitations:**
  - **T56** Çoklu hesap tespiti — `IFraudFlagService.StageAccountFlagAsync(MULTI_ACCOUNT)` ile (T55 sadece pre-create transaction-level flag; account-level signal generators T56+).
  - **T57** Wash trading — fraud flag mekanizması dışında (skor etkisi kaldırılır, flag oluşturmaz).
  - **T81** Steam Market real price provider — `NullMarketPriceProvider` swap. PRICE_DEVIATION rule effective olabilmek için bu task'ı bekliyor; T55 pipeline hazır, market `null` döndüğü sürece bu rule no-op çalışır (HIGH_VOLUME + ABNORMAL_BEHAVIOR iki rule etkin).
  - **HIGH_VOLUME pre-create only:** Şu an yalnızca işlem flag'i (transaction-level pre-create). 02 §14.4 yüksek hacimi pre-create kategorisine yerleştirmiş; "kullanıcı yeni işlem başlatamaz" semantiği account-level block gerekirse T56+ veya AD-7 EMERGENCY_HOLD ile gelirse o ayrı task. T55 mevcut spec ile uyumlu.
  - **Çoklu flag persist edilmiyor:** İlk match wins → tek FraudFlag per FLAGGED transaction. Spec'te çoklu flag istenmedi; ileride istenirse FraudPreCheckOutcome shape'i `IReadOnlyList<(Type, Json)>` olarak genişletilebilir, orchestrator döngü ile yazar.
  - **`high_volume_period_hours` unconfigured ise rule disabled:** Defansif — admin yanlışlıkla diğer iki eşiği seteyip period'u boş bırakırsa her transaction window=0 saat içinde sonsuz sayılır. Period yoksa rule no-op.
  - **Account age fractional handling:** `(nowUtc - User.CreatedAt).TotalDays` `int` cast; `Math.Max(0, ...)` clock skew defansı. 30 günü dolu eden hesap ekleme saatine bağlı olarak gün-bazında değil saat bazında dahil edilir (TotalDays.Floor → 29 ya da 30). Bu kasıtlı — belirsizlik admin için "yeni hesap" tarafına meyleder.
- **MSEC migration warning:** `dotnet ef migrations add` sırasında 2 informational warning ("PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning" — Transaction → BlockchainTransaction + Transaction → TransactionHistory). Bu warning T54 + T55 öncesi de mevcuttu, mevcut entity konfigürasyonundan kaynaklanıyor; T55 dokunmadı.

## Commit & PR

- Branch: `task/T55-aml-control`
- Commit: `f67418b`
- PR: [#86](https://github.com/turkerurganci/Skinora/pull/86)
