# T52 — Komisyon ve finansal hesaplamalar

**Faz:** F3 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-03 (yapım + bağımsız validator)

---

## Yapılan İşler

T19 Transaction entity'sinde tanımlı `Price`/`CommissionRate`/`CommissionAmount`/`TotalAmount` snapshot field'ları üzerinde çalışan ve T45'te `TransactionCreationService` içinde inline yer alan komisyon formülü, T53 (gas fee yönetimi) ve sonrası iade/payout/payment-validation modüllerinin de tek noktadan tüketebileceği saf, deterministik bir hesaplama servisine taşındı (02 §5, §4.6-§4.7, 06 §8.3, 09 §14):

1. **`FinancialCalculator` static class** — `Skinora.Transactions/Domain/Calculations/`:
   - `RoundMoney(decimal)` → `Math.Round(value, 6, MidpointRounding.ToZero)`. Tek yuvarlama noktası; geri kalan tüm formüller bu helper'ı kullanır ya da sadece toplama/çıkarma yaparak `decimal` precision'ı korur (09 §14.3).
   - `CalculateCommission(price, rate)` → `RoundMoney(price * rate)`. Snapshot için `Transaction.CommissionAmount` üretir (06 §8.3 sıra 1).
   - `CalculateTotal(price, commissionAmount)` → `price + commissionAmount`. `PaymentAddress.ExpectedAmount` (T62 forward) bu değerle eşleşecek (06 §8.3 sıra 2-3).
   - `CalculateRefund(totalPaid, gasFee)` → `totalPaid - gasFee`. Tam iptal iadesi (02 §4.6, 09 §14.4).
   - `CalculateMinimumRefundThreshold(gasFee, ratio)` + `IsRefundAboveMinimum(totalPaid, gasFee, ratio)` → `gasFee × ratio` (default `2`); refund threshold altında ise admin alert path'i (09 §14.4). `DefaultMinimumRefundThresholdRatio = 2m` const'ı SystemSetting `min_refund_threshold_ratio = 2.0` ile birebir.
   - `CalculateSellerPayout(price, commissionAmount, gasFee, gasFeeProtectionRatio)` — gas fee koruma kuralı:
     - `threshold = commissionAmount × gasFeeProtectionRatio`
     - `gasFee ≤ threshold` → `payout = price` (platform absorbe eder)
     - `gasFee > threshold` → `payout = price - (gasFee - threshold)` (aşan kısım satıcıdan kesilir)
     - `DefaultGasFeeProtectionRatio = 0.10m` SystemSetting `gas_fee_protection_ratio = 0.10` ile birebir.
   - `CalculateOverpayment(expected, received)` → `Math.Max(0, received - expected)` + `CalculateOverpaymentRefund(overpayment, gasFee)` → `overpayment - gasFee` (09 §14.4 fazla ödeme akışı).
   - `IsPaymentExact(expected, received)` → `expected == received`. 02 §5 + 06 §8.3 "tolerance yok" kuralı; "fuzzy" overload kasıtlı olarak yok.
   - Tüm public API negatif input için `ArgumentOutOfRangeException` fırlatır (defansif sınır — calculator escrow finansal layer'ında saf kalır, caller'ın bug'ı sessizce negatif tutar üretmez).

2. **`TransactionCreationService` refactor** (Stage 7):
   - Inline `Math.Round(price * rate, 6, ToZero)` + `price + commissionAmount` → `FinancialCalculator.CalculateCommission(...)` + `FinancialCalculator.CalculateTotal(...)` çağrıları.
   - `CommissionScale = 6` private const kaldırıldı; `MoneyScale` calculator'da single-source-of-truth.
   - Snapshot davranışı (09 §9.5) bozulmadı — `commissionRate` hâlâ `limits.CommissionRate ?? TransactionParamsService.DefaultCommissionRate`'ten alınıyor, daha sonra admin değişikliği aktif tx'i etkilemiyor.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Transactions/Domain/Calculations/FinancialCalculator.cs` — pure static class, ~210 satır (xmldoc dahil), 9 public method + 3 const.
- `backend/tests/Skinora.Transactions.Tests/Unit/Calculations/FinancialCalculatorTests.cs` — 36 test, BVA scenarios per 09 §14.5 (normal, boundary, gas fee edge, overpayment, precision, exact-match).

**Değişiklik:**
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs` — `using Skinora.Transactions.Domain.Calculations;` + Stage 7 refactor (3 satır → 4 satır) + `CommissionScale` const kaldırıldı + Stage 7 üstüne T52 reference comment eklendi.

**Migration:** Yok. **SystemSetting:** Yok — `commission_rate=0.02`, `gas_fee_protection_ratio=0.10`, `min_refund_threshold_ratio=2.0` zaten T26 seed'inde mevcut. **Yeni dış paket:** Yok. **Yeni env var:** Yok. **DI değişikliği:** Yok (static class).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `CommissionAmount = ROUND(Price × CommissionRate, 6, MidpointRounding.ToZero)` | ✓ | `FinancialCalculator.CalculateCommission` → `RoundMoney(price * rate)` → `Math.Round(value, 6, MidpointRounding.ToZero)`. `Commission_TruncatesTowardZero_NotBankersRound` testi `33.333333 × 0.02 = 0.666666` (banker's `0.666667` değil) doğrular. `Commission_KnownPairs_MatchSpec` Theory 4 satır (10/0.02→0.2, 50000/0.02→1000, 123.45/0.02→2.469, 99.99/0.025→2.49975). |
| 2 | `TotalAmount = Price + CommissionAmount` | ✓ | `CalculateTotal(price, commissionAmount)` → `price + commissionAmount` (re-rounding YOK — 09 §14.3 "yalnızca son adımda yuvarlama"). `Total_HandlesSixDecimalCommission` testi `33.333333 + 0.666666 = 33.999999` (precision korunur). `CreationFlow_RoundTrip_CommissionAndTotalLineUp` round-trip testi `TransactionCreationService` Stage 7 davranışını mirror'lar. |
| 3 | İade tutarı = Price + CommissionAmount - GasFee | ✓ | `CalculateRefund(totalPaid, gasFee)` — caller `totalPaid = TotalAmount = Price + CommissionAmount` geçer. `Refund_FullCancellation_DeductsGasFee` testi `102 - 1 = 101`. `Refund_GasFeeExceedsTotal_ReturnsNegative` (`-4`) caller'ın `IsRefundAboveMinimum` ile gate'lediğini garantiler. 09 §14.4 minimum eşik: `IsRefundAboveMinimum_RefundEqualsThreshold_ReturnsTrue` (boundary `≥` semantic) + `RefundOneMicroUnitBelowThreshold_ReturnsFalse` + `NegativeRefund_ReturnsFalse`. |
| 4 | Gas fee koruma eşiği: gas fee > komisyon × %10 → aşan kısım satıcı payından kesilir | ✓ | `CalculateSellerPayout(price, commissionAmount, gasFee, gasFeeProtectionRatio)` — eşik `commissionAmount × gasFeeProtectionRatio`; `gasFee ≤ threshold` → `price` (platform absorbe), `gasFee > threshold` → `price - (gasFee - threshold)`. `SellerPayout_GasFeeBelowThreshold_PlatformAbsorbs` (0.15 ≤ 0.20 → 100), `SellerPayout_GasFeeAtThreshold_PlatformAbsorbs` (boundary `≤`), `SellerPayout_GasFeeOverThreshold_DeductsExcessFromSeller` (0.50 > 0.20 → 99.70). `DefaultGasFeeProtectionRatio = 0.10m` const'ı 02 §4.7 + SystemSetting ile birebir. |
| 5 | decimal kullanımı zorunlu, ara adımda yuvarlama yok | ✓ | Tüm method imzaları `decimal` (`float`/`double` import yok). `RoundMoney` tek yuvarlama noktası; `CalculateTotal`, `CalculateRefund`, `CalculateSellerPayout`, `CalculateOverpayment*` toplama/çıkarmadan ibaret. `Total_HandlesSixDecimalCommission` (`33.333333 + 0.666666 = 33.999999`) ara adım precision korumasını doğrular. 09 §14.1 + §14.3 ile uyumlu. |
| 6 | Payment validation: gelen tutar beklenenle tam eşleşme (tolerance yok) | ✓ | `IsPaymentExact(expected, received)` → `expected == received`. "Fuzzy" overload yok. `IsPaymentExact_OneMicroUnitDelta_ReturnsFalse` (`102 vs 102.000001` → false), `IsPaymentExact_OverpaymentByOneCent_ReturnsFalse`, `IsPaymentExact_UnderpaymentByOneCent_ReturnsFalse`. 02 §5 + 06 §8.3 birebir. |

## Test Sonuçları

| Suite | Sonuç |
|---|---|
| `FinancialCalculator` unit (`Filter "FullyQualifiedName~FinancialCalculator"`) | **36/36 PASS** (Duration: 26 ms) |
| `Skinora.Transactions.Tests` total (unit only, `Filter "!~Integration"`) | **282/282 PASS** (önceki 246 + 36 yeni) |
| `Skinora.sln` total unit (`Filter "!~Integration"`) | **658/658 PASS** (Auth 57 + Notifications 40 + Users 16 + Transactions 282 + API 15 + Platform 77 + Shared 171) |
| `dotnet build Skinora.sln -c Release` | **0 Warning(s), 0 Error(s)** (Time Elapsed 00:00:18.85) |
| `dotnet format Skinora.sln --verify-no-changes` | **0 değişiklik** (temiz) |

**Integration testleri (TransactionCreationServiceTests):** Lokal SQL Server / Docker MSSQL container yok — CI runner'da çalışacak (T11.3 shared MSSQL services ile). Refactor sadece pure formula'ya delege; davranışsal değişiklik yok, regression beklenmiyor.

## Notlar

- **Working tree:** Adım -1 check temiz (`git status --short` boş).
- **Main CI startup check:** Adım 0 ✓ — son 3 main run hepsi `success` (T51 ×2 #82 + T50 #81; CI run ID 25277906092, 25277906096, 25275894773).
- **Bağımlılık:** T19 ✓ Tamamlandı (Transaction entity'de `Price decimal(18,6)`, `CommissionRate decimal(5,4)`, `CommissionAmount decimal(18,6)`, `TotalAmount decimal(18,6)` zaten tanımlı, T26 seed'inde `commission_rate=0.02` + `gas_fee_protection_ratio=0.10` + `min_refund_threshold_ratio=2.0` mevcut).
- **Dış varsayımlar:**
  - `MidpointRounding.ToZero` — .NET 9 mevcut (.NET Core 3.0+ overload). `Math.Round(decimal, int, MidpointRounding)` overload kullanıldı.
  - `decimal(18,6)` SQL Server tipi — `TransactionConfiguration.cs:117-131` `HasPrecision(18, 6)` ile `Price`/`CommissionAmount`/`TotalAmount`/`MarketPriceAtCreation` üzerinde set'li. T19'da onaylanmış.
  - SystemSetting bootstrap — `SystemSettingSeed.cs:39, 48, 57` üç anahtarın seed'lendiğini doğruladı.
- **Settings provider extend YOK (kasıtlı):** `TransactionLimitsProvider` halen sadece `commission_rate` okuyor; `gas_fee_protection_ratio` ve `min_refund_threshold_ratio` okuma → T53 (Gas fee yönetimi) scope'una bırakıldı. T52'nin scope'u "saf hesaplama formülleri + commission usage refactor". `FinancialCalculator` const default'ları (T53 hazırlanırken DI swap edilmek üzere) saf formul tarafında varsayılanı korur.
- **Refund/payout caller wiring YOK (kasıtlı):** `CalculateRefund` / `CalculateSellerPayout` / `CalculateOverpayment*` çağıracak sahipsiz code path henüz yok — T53 (gas fee yönetimi), T62 (payment webhook), T57+ (cancel/refund orchestration) bu fonksiyonları forward-devir alır. Calculator pure + statik olduğu için DI swap'a gerek yok; değişiklik kapsamı büyümeden kullanıma açıldı.
- **Doğrulama kontrol listesi:** 09 §14 hesaplama kuralları (commission, refund, gas fee koruma, overpayment, precision, payment exact) tam karşılandı; 06 §8.3 sıra (1. commission rounding → 2. total → 3. expected) `CreationFlow_RoundTrip` testinde mirror'landı, formüller birebir.
- **09 §14.3 minor sapma açıklaması:** "Yalnızca son adımda yuvarlama" prensibi — `CalculateCommission` round içerir çünkü `CommissionAmount` snapshot olarak DB'ye kaydedilir (kalıcı son nokta); 06 §8.3 sıra 1 bunu açıkça mandatory yapar. Sonraki tüm adımlar (Total, Refund, Payout, Overpayment) yuvarlamasız `decimal` aritmetiği ile yapılır.

## Doğrulama (bağımsız validator — 2026-05-03)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 (S1/S2/S3) |
| Düzeltme gerekli mi | Hayır |
| Yapım raporu uyumu | Tam — bağımsız bulgular birebir uyumlu |

**HARD STOP kontrolleri:**
- Adım -1 (working tree) ✓ — `git status --short` boş
- Adım 0 (main CI startup) ✓ — son 3 main run hepsi `success` (run ID 25277906092 / 25277906096 / 25275894773; T51 #82 ×2 + T50 #81)
- Adım 0b (memory drift) ✓ — `MEMORY.md`'de T52 satırı mevcut (4 referans)

**Kabul kriterleri (validator bağımsız doğrulama):**
- #1 (commission rounding) ✓ — `Commission_TruncatesTowardZero_NotBankersRound` (`33.333333 × 0.02 = 0.666666`, banker's `0.666667` değil) + `Commission_KnownPairs_MatchSpec` Theory 4 satır
- #2 (total = price + commission, no re-rounding) ✓ — `Total_HandlesSixDecimalCommission` (`33.333333 + 0.666666 = 33.999999`)
- #3 (refund = price + commission - gas) ✓ — `Refund_FullCancellation_DeductsGasFee` (`102 - 1 = 101`) + `IsRefundAboveMinimum_RefundEqualsThreshold` boundary `≥` semantic + sub-threshold + negative refund
- #4 (gas fee koruma %10 default) ✓ — `SellerPayout_GasFeeAtThreshold_PlatformAbsorbs` (boundary `≤`) + `_DeductsExcessFromSeller` (0.50 → 99.70) + `DefaultGasFeeProtectionRatio = 0.10m`
- #5 (decimal, ara yuvarlama yok) ✓ — tüm method imzaları `decimal`, `float`/`double` import yok, tek yuvarlama `RoundMoney`'de
- #6 (payment exact match, tolerance yok) ✓ — `IsPaymentExact_OneMicroUnitDelta_ReturnsFalse` + over/under cent

**Doğrulama kontrol listesi:**
- [x] 09 §14 hesaplama kuralları eksiksiz mi? — Commission, Refund, MinThreshold, SellerPayout (gas koruma), Overpayment, OverpaymentRefund, IsPaymentExact tam karşılandı. `platform_geliri = komisyon - gönderim_gas_fee` derived/reporting metriği — T52 kabul kriterinde yok, ileride raporlama servisi türetebilir, eksik sayılmaz.
- [x] 06 §8.3 formüller birebir eşleşiyor mu? — Sıra (1. commission ROUND → 2. total → 3. ExpectedAmount) `CreationFlow_RoundTrip_CommissionAndTotalLineUp` testi mirror'lar. `decimal(18,6)` `MoneyScale = 6` const ile, `MidpointRounding.ToZero` `RoundMoney`'de, payment validation tolerance `IsPaymentExact == ` ile birebir.

**Bağımsız test çalıştırması (validator):**
| Suite | Komut | Sonuç |
|---|---|---|
| FinancialCalculator unit | `dotnet test tests/Skinora.Transactions.Tests --filter "FinancialCalculatorTests"` | **36/36 PASS** (Duration: 30 ms) |
| Skinora.Transactions full | `dotnet test tests/Skinora.Transactions.Tests` | **481/481 PASS** (Duration: 1m 36s) |
| Skinora.sln total (unit + integration) | `dotnet test Skinora.sln` | **1336/1336 PASS** (Auth 93 + Notifications 77 + Users 16 + Transactions 481 + API 271 + Platform 136 + Shared 187 + Admin 20 + Disputes 11 + Fraud 17 + Steam 21 + Payments 6) |
| Release build | `dotnet build -c Release` | **0 Warning, 0 Error** (Time Elapsed 19.45 s) |
| Task branch CI | `gh run list --branch task/T52-financial-calculations` | run **25282373931** ✓ `success` (CI workflow, 2026-05-03 14:53Z) |

**Mini güvenlik kontrolü:**
- Secret sızıntısı: Temiz — pure-math static class, hiçbir secret yok
- Auth/authorization etkisi: Yok — domain-katmanı utility, endpoint açmıyor
- Input validation: Temiz — tüm public method negatif input için `ArgumentOutOfRangeException` fırlatır
- Yeni dış bağımlılık: Yok — sadece `System` (`Math.Round` + `MidpointRounding`)

**Doküman uyumu:**
- 09 §14.1 (decimal zorunlu) ✓ | 09 §14.2 (decimal(18,6)) ✓ MoneyScale = 6
- 09 §14.3 (ToZero, ara yuvarlama yok) ✓ — `RoundMoney` tek noktada; rapor §14.3 minor sapma açıklaması doğru çerçevelendi (CommissionAmount snapshot olduğu için 06 §8.3 sıra 1'de mandatory, sapma değil mandatory adım)
- 09 §14.4 formüller ✓ birebir | 09 §14.5 BVA test senaryoları ✓ tüm 6 senaryo (normal, boundary, gas edge, fazla, eksik, precision) kapsanmış
- 02 §5 (komisyon alıcıdan, %2 default, tolerance yok) ✓ | 02 §4.6 (iade tutarı = price + commission - gas) ✓ | 02 §4.7 (gas fee koruma %10 default + admin esnek) ✓ const + SystemSetting çift kayıt
- 06 §8.3 (sıra + tolerance yok) ✓ | 06 §8.3 vs 09 §14.4 layering: 06 "SellerPayout = Price (komisyon düşülmüş, gas ayrı)" — calculator hem `CalculateTotal` hem `CalculateSellerPayout` ayrımını verir, aynı saf abstraction iki katmana hizmet eder, çelişki yok

**Forward-devir notları (validator onayladı):**
- `CalculateRefund` / `CalculateSellerPayout` / `CalculateOverpayment*` çağıracak code path henüz yok — T53 (gas fee yönetimi), T57+ (cancel/refund orchestration), T62 (payment webhook) tüketici. Calculator pure + statik, DI swap'a gerek yok.
- `TransactionLimitsProvider`'a `gas_fee_protection_ratio` + `min_refund_threshold_ratio` okuma → T53 scope. T52 `FinancialCalculator` const default'ları fallback olarak duruyor, T53'te live SystemSetting okuma devreye girer.

## Commit & PR

- Commit: `a4e7e9a` (branch `task/T52-financial-calculations`)
- Rapor finalize commit: (validate sonrası)
- PR: [#83](https://github.com/turkerurganci/Skinora/pull/83)
- Squash merge: (validate sonrası)
- Main CI (post-merge): (post-merge watch sonrası)
