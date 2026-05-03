# T53 — Gas Fee Yönetimi

**Faz:** F3 | **Durum:** ⏳ Yapım bitti | **Tarih:** 2026-05-03 (yapım)

---

## Yapılan İşler

T52'de `FinancialCalculator` saf statik class olarak yazıldı; SystemSetting okumadan default `const`'larla çalışıyordu. Refund/payout caller wiring'i forward-devir bırakıldı. T53, T52'nin formüllerini production'a bağlayan glue layer'ı kurar (02 §4.6, §4.7, 09 §14.4):

1. **`IGasFeeSettingsProvider` + `GasFeeSettingsProvider`** — `Skinora.Transactions/Application/GasFee/`:
   - `gas_fee_protection_ratio` + `min_refund_threshold_ratio` SystemSetting okuma; `TransactionLimitsProvider` (T45) + `ReputationThresholdsProvider` (T43) paterni mirror — `AsNoTracking` + `IsConfigured` filter, dictionary tek round-trip.
   - Validator envelope mirror — out-of-range / malformed / unconfigured satır → `FinancialCalculator.DefaultGasFeeProtectionRatio` (0.10m) ve `DefaultMinimumRefundThresholdRatio` (2m) fallback. Poisoned row koruması (manuel SQL veya restore edilmiş backup'ı yakalar).
   - `GasFeeSettings` immutable record (`ProtectionRatio` + `MinRefundThresholdRatio`).

2. **`IRefundDecisionService` + `RefundDecisionService`** — `Skinora.Transactions/Application/GasFee/`:
   - `ResolveBuyerRefundAsync(totalPaid, gasFee, ct)` → `RefundDecision`. Tam iade akışı (02 §4.6); `net = totalPaid - gasFee`, `threshold = gasFee × min_refund_threshold_ratio`; `net ≥ threshold` ise `Refund`, değilse `Block` (`BelowMinimumThreshold`); `net < 0` ise `Block` + `NegativeAmount` reason.
   - `ResolveOverpaymentRefundAsync(expected, received, gasFee, ct)` → `RefundDecision`. Fazla ödeme akışı (09 §14.4); `overpayment = max(0, received - expected)` üzerinde aynı decision; underpayment / exact match → `overpayment = 0` → `Block` + `NegativeAmount`.
   - `ResolveSellerPayoutAsync(price, commissionAmount, gasFee, ct)` → `decimal`. Gas-fee-protection kuralı (02 §4.7); pure delegation `FinancialCalculator.CalculateSellerPayout` + live `ProtectionRatio`.
   - **Pure decision tier:** AuditLog yazmaz, outbox enqueue etmez, SaveChanges çağırmaz. Side effect ayrı katmanda (alert service). Bu split'in nedeni: eligibility/preview path'leri "iade kabul edilir mi?" sorusunu audit row üretmeden cevaplayabilir.

3. **`IRefundBlockedAlertService` + `RefundBlockedAlertService`** — `Skinora.Transactions/Application/GasFee/`:
   - `RaiseAsync(transactionId, decision, ct)`: Block kararı için side-effect tier.
   - `IAuditLogger.LogAsync(REFUND_BLOCKED)` — `ActorType.SYSTEM` + `SeedConstants.SystemUserId` (06 §8.6a invariant), `EntityType=Transaction`, `EntityId=Guid("D")`, `NewValue` = JSON serialize edilmiş `RefundDecision` detayları (reason + totalPaid + gasFee + netRefund + threshold).
   - `IOutboxService.PublishAsync(RefundBlockedAdminAlertEvent)` — admin notification fan-out trigger; consumer T78–T80 forward-devir.
   - **Unit-of-work disiplini:** `IAuditLogger` paterni mirror — sadece change tracker'a stage'ler, caller `SaveChangesAsync`'i atomik tek transaction'da çağırır (09 §13.3). İki yazım (audit + outbox) + business state aynı commit'te flush olur.
   - Defansif: `decision.Outcome != Block` → `InvalidOperationException`; `decision.Reason is null` → `InvalidOperationException`. Yanlış kullanımı sessizce yutmaz.

4. **`AuditAction.REFUND_BLOCKED`** (`Skinora.Shared/Enums/AuditAction.cs`) — yeni enum değer (string-stored `nvarchar(100)`, migration yok).
   - **`AuditLogCategoryMap`** ADMIN_ACTION'a eşleştirildi (07 §9.19). Gerekçe: REFUND_BLOCKED platform-driven (SYSTEM actor) ama operatörün eline bir karar düşürür — MANUAL_REFUND ile aynı kuyrukta görünmesi UX gereği. FUND_MOVEMENT'a yerleştirmek yüksek hacimli wallet satırları arasında alert'i görünmez kılardı.

5. **`RefundBlockedAdminAlertEvent`** (`Skinora.Shared/Events/`) — outbox event:
   - Alanlar: `EventId`, `TransactionId`, `Reason` (RefundBlockedReason enum), `TotalPaid`, `GasFee`, `NetRefund`, `MinimumThreshold`, `OccurredAt`.
   - `RefundBlockedReason` enum: `BelowMinimumThreshold`, `NegativeAmount`.
   - Consumer pattern T49 paralelinde (`PaymentRefundToBuyerRequestedEvent` ile aynı sınıf): T78–T80 admin notification fan-out forward-devir.

6. **`TransactionsModule` DI** — 3 yeni Scoped (`IGasFeeSettingsProvider`, `IRefundDecisionService`, `IRefundBlockedAlertService`).
   - `IAuditLogger` ve `IOutboxService` zaten Platform / Shared modüllerinde register edilmiş (sırası: `Program.cs:94 AddPlatformModule()` → `AddTransactionsModule()`).

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Transactions/Application/GasFee/IGasFeeSettingsProvider.cs` (interface + record)
- `backend/src/Modules/Skinora.Transactions/Application/GasFee/GasFeeSettingsProvider.cs` (impl, ~70 satır)
- `backend/src/Modules/Skinora.Transactions/Application/GasFee/IRefundDecisionService.cs` (interface + RefundOutcome enum + RefundDecision record)
- `backend/src/Modules/Skinora.Transactions/Application/GasFee/RefundDecisionService.cs` (impl, ~95 satır)
- `backend/src/Modules/Skinora.Transactions/Application/GasFee/IRefundBlockedAlertService.cs` (interface)
- `backend/src/Modules/Skinora.Transactions/Application/GasFee/RefundBlockedAlertService.cs` (impl, ~75 satır)
- `backend/src/Skinora.Shared/Events/RefundBlockedAdminAlertEvent.cs` (record + RefundBlockedReason enum)
- `backend/tests/Skinora.Transactions.Tests/Unit/GasFee/RefundDecisionServiceTests.cs` (17 unit test)
- `backend/tests/Skinora.Transactions.Tests/Unit/GasFee/RefundBlockedAlertServiceTests.cs` (7 unit test)
- `backend/tests/Skinora.Transactions.Tests/Integration/GasFee/GasFeeSettingsProviderTests.cs` (6 integration test)

**Değişiklik:**
- `backend/src/Skinora.Shared/Enums/AuditAction.cs` — `REFUND_BLOCKED` enum değeri eklendi (12 → 13 değer).
- `backend/src/Modules/Skinora.Platform/Application/Audit/AuditLogCategoryMap.cs` — REFUND_BLOCKED → ADMIN_ACTION mapping + xmldoc 12→13 güncellendi.
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — 3 yeni Scoped DI kaydı + using `Skinora.Transactions.Application.GasFee`.
- `backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs` — AuditAction count assertion 12→13 + REFUND_BLOCKED InlineData.
- `backend/tests/Skinora.Platform.Tests/Unit/Audit/AuditLogCategoryMapTests.cs` — REFUND_BLOCKED → ADMIN_ACTION assertion + ADMIN_ACTION count 6→7.

**Migration:** Yok (AuditAction enum string-stored `nvarchar(100)`, yeni değer ekleme migration gerektirmez). **SystemSetting:** Yok (gas_fee_protection_ratio + min_refund_threshold_ratio T26 seed'inde zaten mevcut). **Yeni dış paket:** Yok. **Yeni env var:** Yok.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Satıcıya gönderim: gas fee komisyondan karşılanır | ✓ | `RefundDecisionService.ResolveSellerPayoutAsync` → `FinancialCalculator.CalculateSellerPayout(price, commissionAmount, gasFee, settings.ProtectionRatio)`. Eşik altı (`gasFee ≤ commissionAmount × ProtectionRatio`) → `payout = price` (komisyon zaten alıcıdan toplandı, gas fee komisyondan düşülerek platform absorbe eder; net platform geliri = `commission - gasFee`). `SellerPayout_GasFeeBelowThreshold_PlatformAbsorbs` testi (0.15 ≤ 0.20 → 100). |
| 2 | Koruma eşiği aşılırsa: aşan kısım satıcı payından kesilir | ✓ | `gasFee > commissionAmount × ProtectionRatio` → `overage = gasFee - threshold`, `payout = price - overage`. `SellerPayout_GasFeeAboveThreshold_DeductsExcessFromSeller` testi (commission=2, ratio=0.10 → threshold=0.20; gasFee=0.50 → overage=0.30; payout=99.70). Live ratio değişikliği: `SellerPayout_AdminRaisesProtectionRatio_PlatformAbsorbsMore` testi (admin ratio'yu 0.10→0.50 yükseltirse aynı gasFee=0.50 artık eşik altı → payout=100). |
| 3 | İade: gas fee iade tutarından düşülür | ✓ | `RefundDecisionService.ResolveBuyerRefundAsync` → `net = FinancialCalculator.CalculateRefund(totalPaid, gasFee) = totalPaid - gasFee`. `BuyerRefund_AboveThreshold_ReturnsRefundOutcome` testi (totalPaid=102, gasFee=1 → net=101 → Refund outcome). 02 §4.6 "Alıcıya iade tutarı = Fiyat + komisyon - gas fee" + 09 §14.4 formülü birebir. Overpayment paralel: `ResolveOverpaymentRefundAsync` → `overpayment = received - expected`, sonra aynı `net = overpayment - gasFee`. |
| 4 | Minimum iade eşiği: tutar < 2× gas fee → iade yapılmaz, admin alert | ✓ | `threshold = gasFee × MinRefundThresholdRatio` (default 2.0); `net < threshold` → `RefundOutcome.Block` + `RefundBlockedReason.BelowMinimumThreshold`. `BuyerRefund_OneMicroUnitBelowThreshold_ReturnsBlock` testi (totalPaid=2.999999, gasFee=1 → net=1.999999, threshold=2 → Block). Boundary `≥` semantic: `BuyerRefund_ExactlyAtThreshold_ReturnsRefundOutcome` testi (totalPaid=3, gasFee=1 → net=2 == threshold → Refund). Negative refund: `BuyerRefund_NetGoesNegative_ReturnsBlockWithNegativeAmountReason` (totalPaid=3, gasFee=5 → net=-2 → Block + NegativeAmount). Admin alert: `RefundBlockedAlertService.RaiseAsync` → AuditLog `REFUND_BLOCKED` + `RefundBlockedAdminAlertEvent` outbox publish (notification fan-out T78–T80 forward-devir, T49 paterni mirror). |

## Test Sonuçları

| Suite | Sonuç |
|---|---|
| `RefundDecisionServiceTests` (unit) | **17/17 PASS** |
| `RefundBlockedAlertServiceTests` (unit) | **7/7 PASS** |
| `GasFeeSettingsProviderTests` (integration, MSSQL) | 6 test (CI runner'da çalışacak — T11.3 shared MSSQL) |
| `Skinora.Transactions.Tests` total (unit only, `Filter "Category!=Integration"`) | **495/495 PASS** |
| `Skinora.Platform.Tests` (AuditLogCategoryMap güncellemesi sonrası unit) | **87/87 PASS** |
| `Skinora.Shared.Tests` (EnumTests REFUND_BLOCKED + count 13 update) | **178/178 PASS** |
| `Skinora.sln` total unit (`Filter "Category!=Integration"`) | **1149/1149 PASS** (Users 16 + Auth 57 + Notifications 40 + Shared 178 + Fraud 5 + Platform 87 + Transactions 495 + API 271; Admin/Disputes/Payments/Steam integration-only) |
| `dotnet build Skinora.sln -c Release` | **0 Warning(s), 0 Error(s)** (Time Elapsed 18 sn) |
| `dotnet format Skinora.sln --verify-no-changes` | exit=0 (temiz) |

**Integration testleri:** `GasFeeSettingsProvider` 6 senaryo (seeded values, admin customised, missing → default, malformed protection ratio, malformed min refund ratio, both rows absent) Windows lokal Docker MSSQL Testcontainers'da çalışmaz; CI runner shared MSSQL services üzerinde T11.3 paterniyle yürütülecek.

## Notlar

- **Working tree:** Adım -1 check temiz (`git status --short` boş, branch `task/T52-financial-calculations`'dan main'e geçiş + pull, sonra `task/T53-gas-fee-management` branch'i açıldı).
- **Main CI startup check:** Adım 0 ✓ — son 3 main run hepsi `success` (T52 #83 ×2 + T51 #82; CI run ID 25287500662, 25287500799, 25277906092).
- **Bağımlılık:** T52 ✓ Tamamlandı (`FinancialCalculator` static class hazır, `gas_fee_protection_ratio = 0.10` + `min_refund_threshold_ratio = 2.0` SystemSetting seed T26'da mevcut).
- **Dış varsayımlar:**
  - `gas_fee_protection_ratio` + `min_refund_threshold_ratio` SystemSetting key'leri seed'de mevcut → ✓ doğrulandı (`SystemSettingSeed.cs:48,57`).
  - `Math.Round(decimal, int, MidpointRounding.ToZero)` overload .NET 9 mevcut → ✓ T52'de zaten kullanıldı.
  - `FinancialCalculator` static class T53 tarafından tüketilebilir → ✓ doğrulandı (`Skinora.Transactions.Domain.Calculations` namespace, public static).
  - `IAuditLogger` (T42) DI'da hazır + `LogAsync` change-tracker only (caller SaveChanges) → ✓ doğrulandı (`PlatformModule.cs:23`, `AuditLogger.cs:25`).
  - `IOutboxService.PublishAsync(IDomainEvent)` API + outbox MediatR fan-out → ✓ doğrulandı (`TimeoutSideEffectPublisher.cs` + `OutboxModule.cs`).
  - `AuditAction` enum string-stored (`HasMaxLength(100)` + `EnumToStringConverter`) → ✓ doğrulandı (`AuditLogConfiguration.cs:38-40`, `AppDbContext.cs:53-62`); yeni değer migration gerektirmiyor.
  - `AuditLogCategoryMap.Every_AuditAction_Has_A_Category` testi yeni enum eklenince fail eder → düzeltildi (REFUND_BLOCKED → ADMIN_ACTION mapping eklendi + InlineData + count assertion 6→7).
- **Doğrulama kontrol listesi:** 02 §4.7 gas fee kuralları eksiksiz mi? — ✓ tam (4 kural: (1) gönderim gas fee komisyondan karşılanır, (2) koruma eşiği %10 default, (3) eşik admin tarafından değiştirilebilir → SystemSetting `gas_fee_protection_ratio` live okuma, (4) eşik aşımı satıcı payından kesilir). Ek olarak 02 §4.6 + 09 §14.4 (refund formula + minimum threshold + admin alert) tam karşılandı.
- **Forward-devir (T57+ refund orchestrator, T73 blockchain sidecar consumer):** `IRefundDecisionService` ve `IRefundBlockedAlertService` çağıracak production code path henüz yok. T49 (TimeoutSideEffectPublisher) `PaymentRefundToBuyerRequestedEvent` outbox event'ini emit ediyor; T73 sidecar consumer eline aldığında `IRefundDecisionService.ResolveBuyerRefundAsync(totalPaid, gasFee)` ile karar alıp `Block` ise `IRefundBlockedAlertService.RaiseAsync(...)`'a delege edecek, `Refund` ise blockchain transferini gönderecek. T57 admin manual refund orchestrator'ı aynı pattern'i kullanır. Caller wiring'in T49 paterni gibi sonraki task'a forward-devirilmesi T52'de zaten dokümante edilmişti (`Refund/payout caller wiring YOK (kasıtlı)`).
- **Atomicity boundary (09 §13.3):** `RefundBlockedAlertService.RaiseAsync` audit row + outbox event'i change-tracker'a stage'ler; caller (T57+ orchestrator) iş mantığı state geçişiyle birlikte tek `SaveChangesAsync`'te commit eder. Audit + outbox + business state üçü atomik akışta.
- **AuditAction enum genişletme dersi:** `AuditLogCategoryMap.Every_AuditAction_Has_A_Category` testi yeni enum değer eklendiğinde unit test suite'i fail-fast yapar (sessiz gap'i engelleyen guard). Bu sefer ilk solution test run'ında yakaladı, mapping eklenip yeniden çalıştırıldı. Pattern T54+ task'larında AuditAction büyütüldükçe aynı şekilde işleyecek.
- **JsonSerializer + decimal:** `RefundBlockedAlertService` audit `NewValue`'sunda `decimal.ToString(CultureInfo.InvariantCulture)` ile string'e çevriliyor (System.Text.Json default decimal serialization invariant'tır ama defensive). Ham JSON object'i (örn. `{"totalPaid":2.5}`) doğrudan emit etmek SQL Server `NVARCHAR(MAX)` kolona düz metin olarak persist olur — T42 `SystemSettingsService` precedent'iyle aynı pattern.

## Commit & PR

- Commit: `49348ce` (branch `task/T53-gas-fee-management`)
- Rapor + status + memory commit: pending (yapım bitti, validate-PR-CI öncesi push)
- PR: pending
- Squash merge: (validate sonrası)
- Main CI (post-merge): (post-merge watch sonrası)

## Known Limitations

- **Caller wiring forward-devir:** T49 paterni — `IRefundDecisionService` ve `IRefundBlockedAlertService` production code path'inde tüketilmiyor. T57+ refund orchestrator + T73 blockchain sidecar consumer wire-up'ı yapacak. T53 servis tarafını production-ready yapar; consumer'lar ayrı task.
- **Notification fan-out:** `RefundBlockedAdminAlertEvent` outbox'a publish ediliyor ama notification template'i yok ve admin email/Telegram/Discord consumer wire-up'ı T78–T80 forward-devir (T49 `TransactionTimedOutEvent` ile aynı paralel). Şimdilik AuditLog row admin için tek görünür sinyal — `GET /admin/audit-logs?category=ADMIN_ACTION` filtresinde görünür.
- **Cache yok:** `GasFeeSettingsProvider` her çağrıda DB query yapar (no caching layer). T52 forward-devir notunda T53 cache stratejisi opsiyonel olarak bırakılmıştı; live admin update'lerin anında etkili olması daha kritik bulundu (refund decision'da 5 sn cache window admin'in eşiği değiştirip refund block'u açtıktan sonra hala block yiyebilir). T43/T45 paterni: aynı no-cache yaklaşımı.
