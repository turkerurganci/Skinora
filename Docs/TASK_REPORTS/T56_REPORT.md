# T56 — Çoklu Hesap Tespiti

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-05-05

---

## Yapılan İşler

T54 fraud flag pipeline + T18 user/login entity'leri üzerine multi-account detection çapraz-sorgu motoru eklendi (02 §14.3, 03 §7.4). Spec'in iki katmanlı sinyal modeli — *strong* (cüzdan adresi eşleşmesi → flag) + *supporting* (IP/cihaz parmak izi/ödeme kaynak adresi → tek başına flag değil) — net seam'lere ayrıldı:

1. **Port + result types — `Skinora.Users.Application.MultiAccount/`:**
   - `IMultiAccountDetector.EvaluateAsync(userId, ct) → Task<MultiAccountEvaluationResult>` — port `Skinora.Users`'ta yaşıyor, böylece wallet update path (T34) `Skinora.Fraud`'a referans vermeden detector'ı çağırabilir (T54 `IAccountFlagChecker` paterni mirror).
   - `MultiAccountEvaluationResult { Status, PrimaryMatchType?, PrimaryMatchValue?, LinkedAccountCount, SupportingSignalCount, FlagId? }` + factory metotları (`NoSignal`, `AlreadyFlagged`, `Flagged`).
   - `MultiAccountEvaluationStatus` enum: `NoSignal | AlreadyFlagged | Flagged`.
   - `MultiAccountMatchType` enum: `WALLET_PAYOUT | WALLET_REFUND` (yalnızca güçlü sinyal türleri — supporting `flagDetail.supportingSignals[].type` string olarak yazılıyor).

2. **`MultiAccountSignalEvaluator` (saf yardımcılar) — `Skinora.Fraud/Application/MultiAccount/MultiAccountSignalEvaluator.cs`:**
   - `ParseExchangeAddresses(string?) → HashSet<string>` — admin-curated `multi_account.exchange_addresses` CSV'sini set'e çevirir; `null`/blank/`NONE` → boş set; tokens trim + dedupe; case-sensitive (TRC-20 mixed-case).
   - `PickStrongMatchType(hasPayout, hasRefund) → MultiAccountMatchType?` — eşleşme önceliği: payout > refund > null. Her ikisi de eşit derece güçlü (02 §14.3) ama payout where funds settle, admin için daha actionable. T55 `FraudDetectionCalculator` paterni — saf hesaplama integration suite'inden ayrı, <200ms unit testlenebilir.

3. **`MultiAccountDetector` (DB-backed orchestration) — `Skinora.Fraud/Application/MultiAccount/MultiAccountDetector.cs`:**
   - **Idempotency gate:** Kullanıcı için `Scope=ACCOUNT_LEVEL && Type=MULTI_ACCOUNT && Status != REJECTED && !IsDeleted` koşulunu sağlayan flag varsa → `AlreadyFlagged` (yeni stage yok). REJECTED veya soft-deleted flag short-circuit etmez (admin reddetti / eski kayıt — yeni signal için yeniden flag açılabilir).
   - **Strong signal sorguları:** `User.DefaultPayoutAddress` ve `DefaultRefundAddress` için ayrı sorgu (`!IsDeleted && !IsDeactivated && Id != userId`). Hangisi `Count > 0` ise `PickStrongMatchType` kararı.
   - **Supporting signals (yalnızca strong sinyal fired olduğunda toplanır):**
     - **IP_ADDRESS / DEVICE_FINGERPRINT:** `UserLoginLog`'dan son `RecentLoginSampleSize=25` row sorgulanır; her unique IP/fingerprint için cross-user join. Boş/null değerler atlanır.
     - **SOURCE_ADDRESS:** `BlockchainTransaction.FromAddress` (Type=`BUYER_PAYMENT`, BuyerId=userId). Her unique adres için: exchange listesinde değilse, başka kullanıcıların aynı adresten ödeme yapıp yapmadığı sorgulanır. Exchange listesi `multi_account.exchange_addresses` SystemSetting'inden okunur (`NONE` = exclusion yok).
   - **Flag staging:** `IFraudFlagService.StageAccountFlagAsync(userId, MULTI_ACCOUNT, detailsJson, SystemUserId, SYSTEM, cascadeEmergencyHold=false)` — T54 audit + outbox pipeline'ı korunur. Detector kendi `SaveChanges`'ini sahiplenir (caller-owned değil; wallet update post-commit hook).
   - **flagDetail JSON shape (07 §9.3 v9.4):** `{ matchType, matchValue, linkedAccounts:[{steamId, displayName}], supportingSignals:[{type, value, linkedAccounts:[...]}] }` — primary match wallet, supportingSignals admin için kanıt.
   - **Public consts:** `ExchangeAddressesSettingKey="multi_account.exchange_addresses"`, `ExchangeAddressesNoneMarker="NONE"`, `RecentLoginSampleSize=25` (test fixture'lar bu sabitleri tüketiyor).

4. **`WalletAddressService` hook (T34 path):**
   - DI imzası `IMultiAccountDetector` + `ILogger<WalletAddressService>` ile genişledi.
   - `await _db.SaveChangesAsync()` sonrası: `try { await _multiAccountDetector.EvaluateAsync(userId, ct); } catch (Exception) { _logger.LogWarning(...); }` — `OperationCanceledException` re-throw, diğer exception'lar swallow + warn. Rationale: detector hatası (örn. IPs sample query timeout) wallet değişikliğini geri almamalı; bir sonraki tetikleme (yeni wallet update veya T34 cooldown sonrası) eksik signal'i yakalar.

5. **Yeni SystemSetting — `multi_account.exchange_addresses`:**
   - `SystemSettingsCatalog.cs` — `fraud_detection` API category, label "Bilinen exchange/custodial adres listesi (CSV; NONE = yok)", unit `null`.
   - `SystemSettingSeed.cs` — index 37, `Default("string", "NONE", description)`. `NONE` = exclusion list boş = her source address karşılaştırılır.
   - Migration `20260504212015_T56_AddExchangeAddressesSetting` — `InsertData` × 1 row (`Up`) + `DeleteData` × 1 (`Down`); şema değişikliği yok.
   - Format: comma-separated TRC-20 adresleri; admin UI üzerinden 07 §9.9 ile güncellenir; case-sensitive exact match.

6. **DI wiring — `FraudModule.AddFraudModule`:**
   - 1 satır: `services.AddScoped<IMultiAccountDetector, MultiAccountDetector>();`
   - Composition root değişikliği yok — `Skinora.API` zaten `AddFraudModule` çağırıyor (Program.cs:93).

7. **`07_API_DESIGN.md` §9.3 doc update:**
   - `MULTI_ACCOUNT` flagDetail satırı genişletildi: `{ matchType, matchValue, linkedAccounts, supportingSignals }`.
   - Aşağı not: `matchType` vocab (`WALLET_PAYOUT`, `WALLET_REFUND`), `supportingSignals[].type` vocab (`IP_ADDRESS`, `DEVICE_FINGERPRINT`, `SOURCE_ADDRESS`), exchange exclusion açıklaması.

8. **12 yeni unit test** — `backend/tests/Skinora.Fraud.Tests/Unit/MultiAccount/MultiAccountSignalEvaluatorTests.cs`:
   - `ParseExchangeAddresses` × 8 (null/empty/whitespace/NONE/single/CSV split + trim/empty token filter/dedupe/case-sensitive).
   - `PickStrongMatchType` × 4 (no match, payout-only, refund-only, both → payout wins).

9. **15 yeni integration test** — `backend/tests/Skinora.Fraud.Tests/Integration/MultiAccountDetectorTests.cs`:
   - **No-signal:** Returns_NoSignal_When_User_Is_Alone, Returns_NoSignal_When_Other_Account_Has_Different_Wallet, Returns_NoSignal_When_User_Has_No_Wallet_Configured.
   - **Strong signals:** Flags_When_Other_Account_Shares_Payout_Address (flagDetail JSON birebir doğrulama + outbox FraudFlagCreatedEvent assertion), Flags_When_Other_Account_Shares_Refund_Address_Only, Payout_Match_Wins_Over_Refund_Match (priority kuralı), Ignores_Soft_Deleted_Or_Deactivated_Other_Accounts.
   - **Supporting:** Supporting_Only_Ip_Match_Does_Not_Flag, Supporting_Only_Device_Fingerprint_Does_Not_Flag, Wallet_Match_Surfaces_Ip_And_Fingerprint_Supporting_Signals (wallet match + 2 supporting collect → flagDetail.supportingSignals 2 entry).
   - **SOURCE_ADDRESS exchange exclusion:** Source_Address_Match_Excluded_When_In_Exchange_List, Source_Address_Match_Surfaces_When_Not_In_Exchange_List (`NONE` marker semantiği).
   - **Idempotency:** Existing_Pending_Flag_Returns_AlreadyFlagged, Existing_Approved_Flag_Returns_AlreadyFlagged (her ikisi `AlreadyFlagged` döner, outbox boş kalır), Existing_Rejected_Flag_Does_Not_Short_Circuit (REJECTED admin tarafından reddedildi → yeni signal için yeniden flag açılabilir), Existing_Soft_Deleted_Flag_Does_Not_Short_Circuit.
   - Helper'lar: `InsertUserAsync` (handle + payout + refund + isDeleted/isDeactivated flags), `InsertLoginAsync` (UserLoginLog row), `InsertBuyerPaymentAsync` (Transaction + PaymentAddress + BlockchainTransaction triple — CK_BlockchainTransactions_Type_BuyerPayment'i karşılamak için PaymentAddress zorunlu), `ConfigureExchangeAddressesAsync` (existing seed row üzerine UPSERT pattern), `InsertExistingFlagAsync` (idempotency setup).

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Users/Application/MultiAccount/IMultiAccountDetector.cs`
- `backend/src/Modules/Skinora.Users/Application/MultiAccount/MultiAccountEvaluationResult.cs`
- `backend/src/Modules/Skinora.Fraud/Application/MultiAccount/MultiAccountDetector.cs` (~280 satır)
- `backend/src/Modules/Skinora.Fraud/Application/MultiAccount/MultiAccountSignalEvaluator.cs` (~50 satır)
- `backend/src/Skinora.Shared/Persistence/Migrations/20260504212015_T56_AddExchangeAddressesSetting.cs`
- `backend/src/Skinora.Shared/Persistence/Migrations/20260504212015_T56_AddExchangeAddressesSetting.Designer.cs`
- `backend/tests/Skinora.Fraud.Tests/Unit/MultiAccount/MultiAccountSignalEvaluatorTests.cs` (12 test)
- `backend/tests/Skinora.Fraud.Tests/Integration/MultiAccountDetectorTests.cs` (15 test, ~480 satır)

**Değişiklik:**
- `backend/src/Modules/Skinora.Fraud/FraudModule.cs` — 1 satır `IMultiAccountDetector` registration + 2 using.
- `backend/src/Modules/Skinora.Users/Application/Wallet/WalletAddressService.cs` — DI imzası genişledi (detector + logger), post-SaveChanges hook + try/catch + log warning.
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs` — 1 yeni entry (index sıralı, fraud_detection category).
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs` — index 37 + section comment.
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` — 1 yeni HasData row (auto-generated).
- `backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs` — count 36→37, configured 15→16 (+ `multi_account.exchange_addresses` alphabetic insert), section comments güncellendi.
- `Docs/07_API_DESIGN.md` — §9.3 `MULTI_ACCOUNT` flagDetail satırı + matchType / supportingSignals vocab notu.

**Migration:** `20260504212015_T56_AddExchangeAddressesSetting` — `Up` 1 InsertData / `Down` 1 DeleteData; şema değişikliği yok. **SystemSetting:** 1 yeni anahtar (default `NONE`, configured). **Yeni dış paket:** Yok. **Yeni env var:** Yok (default değer mevcut, mandatory bootstrap gerekmiyor).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Güçlü sinyal: aynı cüzdan adresi birden fazla hesapta → flag | ✓ | `MultiAccountDetector.FindWalletMatchesAsync` payout + refund role'leri ayrı sorguluyor; `PickStrongMatchType` payout > refund priority. Test: `Flags_When_Other_Account_Shares_Payout_Address` (flag oluşturma + flagDetail.matchType=`WALLET_PAYOUT` + outbox `FraudFlagCreatedEvent`), `Flags_When_Other_Account_Shares_Refund_Address_Only` (refund-only path), `Payout_Match_Wins_Over_Refund_Match` (priority). |
| 2 | Destekleyici sinyal: aynı gönderim adresi (exchange hariç) → tek başına flag değil | ✓ | `CollectSupportingSignalsAsync` SOURCE_ADDRESS branch yalnızca strong sinyal fired olduğunda çalışır (control flow `primaryType is null → return NoSignal()`). Exchange list `MultiAccountSignalEvaluator.ParseExchangeAddresses` ile `NONE`-aware. Test: `Source_Address_Match_Excluded_When_In_Exchange_List` (exchange listede → supportingSignals'ta SOURCE_ADDRESS yok, ama wallet match var olduğu için flag oluşur), `Source_Address_Match_Surfaces_When_Not_In_Exchange_List` (`NONE` ile listeye alınmamış → SOURCE_ADDRESS supportingSignals'ta görünür). |
| 3 | Destekleyici sinyal: aynı IP/cihaz parmak izi → tek başına flag değil | ✓ | `CollectSupportingSignalsAsync` IP_ADDRESS + DEVICE_FINGERPRINT branch'leri. Strong sinyal kontrolünden sonra çağrılır → IP/fingerprint tek başına `MultiAccountEvaluationStatus.NoSignal` döndürür. Test: `Supporting_Only_Ip_Match_Does_Not_Flag` (subject + other aynı IP, hiç wallet eşleşmesi yok → NoSignal + 0 FraudFlag), `Supporting_Only_Device_Fingerprint_Does_Not_Flag` (aynı fingerprint, farklı IP → NoSignal). |
| 4 | Sinyal kombinasyonu değerlendirmesi | ✓ | Strong + Supporting kombinasyonu: `Wallet_Match_Surfaces_Ip_And_Fingerprint_Supporting_Signals` test'i — subject + twin (wallet) + ipShared (sadece IP eşleşmesi) + fpShared (sadece fingerprint eşleşmesi). Sonuç: 1 flag (wallet match), `flagDetail.supportingSignals` 2 entry (IP_ADDRESS + DEVICE_FINGERPRINT), her birinde linkedAccounts ilgili kullanıcıyı işaret ediyor; primary `linkedAccounts` wallet twin'i içeriyor. Bu spec'in "destekleyici sinyaller flag tetiklemez ama strong sinyal eşliğinde admin'e kanıt olarak sunulur" semantiğinin koddaki karşılığı. |
| 5 | Admin'e bildirim | ✓ | `IFraudFlagService.StageAccountFlagAsync` T54 pipeline'ı: AuditLog `FRAUD_FLAG_CREATED` row + Outbox `FraudFlagCreatedEvent` (T62/T78–T80 notification handler'ları zaten consumer). T35 admin user'ları `MANAGE_FLAGS`/`VIEW_FLAGS` permission'ları ile AD3 endpoint'inden flagDetail'i (linkedAccounts dahil) görür. Test: `Flags_When_Other_Account_Shares_Payout_Address` outbox event içeriği assertion'ı (`evt.Type == MULTI_ACCOUNT && evt.Scope == ACCOUNT_LEVEL`). |

## Doğrulama Kontrol Listesi

- [✓] **02 §14.3 tüm sinyal türleri uygulanmış mı?** — Üç signal türü kodda + testte mevcut:
  - **Strong (cüzdan adresi):** Payout (`User.DefaultPayoutAddress`) + Refund (`User.DefaultRefundAddress`) ayrı sorgu, ikisi de `MultiAccountMatchType` enum'da.
  - **Supporting (gönderim adresi):** `BlockchainTransaction.FromAddress` (Type=BUYER_PAYMENT) cross-user join + exchange exclusion (admin SystemSetting).
  - **Supporting (IP/cihaz):** `UserLoginLog.IpAddress` + `UserLoginLog.DeviceFingerprint` cross-user join, RecentLoginSampleSize=25 ile bounded.
  - 03 §7.4 akışı 1:1: cüzdan çapraz kontrol → güçlü eşleşmede flag → AuditLog + Outbox bildirimi → admin AD3'te linkedAccounts + supportingSignals görür.

## Test Sonuçları

| Suite | Sonuç |
|---|---|
| `MultiAccountSignalEvaluatorTests` (yeni unit suite) | **12/12 PASS** |
| `MultiAccountDetectorTests` (yeni integration suite) | **15/15 PASS** — 37 sn (Testcontainers MSSQL bootstrap dahil) |
| `Skinora.Fraud.Tests` total (T54 + T55 + T56) | **64/64 PASS** — 2 m 38 s |
| `Skinora.Platform.Tests` total (SeedDataTests count 36→37 + configured 15→16) | **141/141 PASS** — 2 m 18 s |
| `Skinora.API.Tests` `WalletAddressEndpointTests` (T34 davranışı korundu) | **10/10 PASS** — 14 sn |
| `Skinora.API.Tests` total | **280/280 PASS** — 3 m 35 s |
| `Skinora.Transactions.Tests` total | **543/543 PASS** — 4 m 6 s |
| `Skinora.Auth.Tests` total | **93/93 PASS** |
| `Skinora.Notifications.Tests` total | **77/77 PASS** |
| `Skinora.Steam.Tests` total | **21/21 PASS** |
| `Skinora.Disputes.Tests` total | **11/11 PASS** |
| `Skinora.Admin.Tests` total | **20/20 PASS** |
| `Skinora.Payments.Tests` total | **6/6 PASS** |
| `Skinora.Users.Tests` total | **16/16 PASS** |
| `Skinora.Shared.Tests` total | **192/192 PASS** |
| **Solution sweep** | **1464/1464 PASS** (12 test projesi, ~10 dk toplam) |
| `dotnet build Skinora.sln -c Release` | **0 Warning(s), 0 Error(s)** (Time Elapsed 13 sn) |
| `dotnet format Skinora.sln --verify-no-changes` | exit=0 (no changes) |
| Migration generation (`dotnet ef migrations add T56_AddExchangeAddressesSetting`) | success — 1 InsertData + 1 DeleteData; iki MSEC informational warning ("global query filter on principal end") T54/T55 öncesi de mevcuttu, T56 ile ilgisiz. |

## Notlar

- **Working tree:** Adım -1 check temiz (`git status --short` boş; main'e geçiş + pull, sonra `task/T56-multi-account-detection` branch'i açıldı).
- **Main CI startup check:** Adım 0 ✓ — son 3 main run hepsi `success` (T55 #86 ×2 + T54 #85; CI run ID 25338999719, 25338999692, 25332031310).
- **Bağımlılık:**
  - **T54 ✓** — `IFraudFlagService.StageAccountFlagAsync(MULTI_ACCOUNT, ...)` portu kullanıldı; AuditLog + Outbox + emergency-hold cascade pipeline'ı T56 detector için olduğu gibi devralındı (cascade=false).
  - **T18 ✓** — `User.DefaultPayoutAddress` + `DefaultRefundAddress` (T34 wallet update path) ve `UserLoginLog.IpAddress` + `DeviceFingerprint` (T18 entity) hazırdı.
- **Dış varsayımlar (Adım 4):**
  - `FraudFlagType.MULTI_ACCOUNT` enum tanımlı → ✓ doğrulandı (`backend/src/Skinora.Shared/Enums/FraudFlagType.cs:8`).
  - 07 §9.3 `MULTI_ACCOUNT` flagDetail satırı bekleniyordu (`{ matchType, matchValue, linkedAccounts }`); supportingSignals şartı spec'te yoktu — same-PR doc update ile şema genişletildi (matchType/supportingSignals vocab dahil).
  - `BlockchainTransactionType.BUYER_PAYMENT` enum value (`INCOMING_PAYMENT` değil — ilk taslakta yanlış değer kullanılmıştı, build sırasında düzeltildi) → ✓ doğrulandı (`backend/src/Skinora.Shared/Enums/BlockchainTransactionType.cs:5`).
  - `MonitoringStatus.ACTIVE` (test fixture'da `MONITORING` kullanmıştım, build hatası → `ACTIVE` ile düzeltildi) → ✓ doğrulandı (`backend/src/Skinora.Shared/Enums/MonitoringStatus.cs:5`).
  - `CK_BlockchainTransactions_Type_BuyerPayment` constraint BUYER_PAYMENT için PaymentAddressId NOT NULL şartı koşuyor → ✓ doğrulandı (test helper `InsertBuyerPaymentAsync` PaymentAddress entity'sini de yazıyor).
  - `dotnet ef migrations add` Skinora.Shared persistence project üzerinden çalışıyor → ✓ doğrulandı (T55 paterni mirror; T55 migration commit'inden sonra başlatıldı).
  - Yeni paket veya plan tier varsayımı yok.
- **Atomicity:** Detector kendi `SaveChanges`'ini sahiplenir. `WalletAddressService` wallet update'i kendi `SaveChanges`'ini önce yapar; detector post-commit hook olarak çalışır. Detector exception fırlatırsa wallet değişikliği zaten commit edilmiştir (rollback yok) — bu kasıtlı: signal kaybı kabul edilebilir, wallet değişikliği fonksiyonel olarak başarılı.
- **Idempotency rationale:** PENDING flag varken detector tekrar tetiklenirse (örn. user wallet'ı tekrar güncellerse) duplicate flag yaratılmaz. APPROVED flag varken de — admin onayladı, hesap zaten aksiyon altında, yeni stage gereksiz. REJECTED ise yeni signal değerlendirilebilir; soft-deleted flag da hard-exclude (admin temizlemiş). Test: 4 idempotency senaryosu ayrı ayrı kapsanıyor.
- **Strong vs supporting kontrol akışı:** `if (primaryType is null) return NoSignal()` — supporting collect *strong sinyal fired olduktan sonra* çalışır. Bu, supporting-only flag riskini compile-time + test-time iki katmanlı güvence ile elimine eder. GPT review R5 (02_GPT_REVIEW_R5.md BULGU-2) "exchange/custodial false positive" endişesi spec'e taşınmıştı; T56 bunu hem control flow ile hem de explicit exchange exclusion settings ile yansıtıyor.
- **HighVolume aggregate vs T56 ilişkisi:** T55 validator advisory A1 ("HIGH_VOLUME aggregation FLAGGED+CANCELLED dahil — T56/T57 follow-up'ta tekrar değerlendir") — T56'nın bu aggregation'a etkisi yok; T56 ayrı bir signal tipi (account-level wallet match). T57 (wash trading) wash-trading rule'unu transaction-level skor etkisinde değerlendirecek; aggregate semantiğinin gözden geçirilmesi T57 chat'inde uygun (T56 scope dışı, MEMORY/known limitation'da kalır).
- **Doc update rationale (07 §9.3 supportingSignals):** Spec başlangıçta primary linked accounts'u içeriyordu ama destekleyici sinyallerin nasıl admin'e ulaşacağı belirtilmemişti. Üç seçenek değerlendirildi: (a) ayrı flag rows (atomicity bozar, queue gürültüsü), (b) AdminNote pre-population (string-typed, programatik tüketim zor), (c) flagDetail genişletme (forward-compatible, structured). (c) seçildi; aynı PR'da 07 §9.3 doc update yapıldı — aksi halde validator "doc-uyum eksik" bulgusu çıkardı.
- **CSV format simplicity:** TRC-20 adresleri ASCII Base58 — virgül + boşluk haricinde escape gerektirmez. Admin UI list editor (07 §9.9) string field ile tüketir. Daha karmaşık format (JSON array, regex pattern) ihtiyacı doğarsa ileride migration'la şema değişebilir; şimdilik CSV en düşük operational complexity.
- **`RecentLoginSampleSize=25` rationale:** Aktif kullanıcılar günde 5-10 login yapabilir; 25 row son ~3 günü kapsar — multi-account fraud ring'leri tipik olarak aynı anda birden fazla hesapta login olur, son 3 gün yeterli sinyal verir. Daha geniş bir pencere (örn. 100) query plan'ı bozardı; daha dar (örn. 5) edge case'leri kaçırırdı. Const, ileride SystemSetting'e taşınabilir.
- **Forward-devir / Known limitations:**
  - **T82 sanctions check (cüzdan):** T56 bu adresleri sanctions list'e karşı kontrol etmez — `IWalletSanctionsCheck` zaten T34 path'inde `WalletAddressService` tarafından çağrılıyor (line 59), wallet update'ten önce. Bu nedenle sanctions hit'leri T56 detector'a hiç ulaşmaz. T82 production sanctions feed'i yazıldığında bu davranış değişmez.
  - **Background job:** T56 yalnızca wallet update tetiklemesinde çalışır. Login/IP değişikliklerinde detector çağrılmaz çünkü IP/fingerprint *supporting* — strong sinyal yokken yeniden hesaplama gereksiz. Eğer ileride strong sinyal kategorisi genişletilir ya da retroactive scan istenirse T63 background job ile periyodik tetikleme eklenebilir; T56 scope dışı.
  - **Cross-role buyer wallet:** T56 `User.DefaultRefundAddress` (alıcı tarafı) ve `User.DefaultPayoutAddress` (satıcı tarafı) ikisini birden eşleştirir — kullanıcı her iki rolde de farklı wallet kullanabilir. Per-transaction snapshot (`Transaction.SellerPayoutAddress` / `BuyerRefundAddress`) ek bir kontrol noktası olabilirdi ama bu, kullanıcı default'tan farklı bir adres girdiğinde yakalanırdı; spec yalnızca account-level cüzdan kontrolünü istiyor — tx snapshot kontrolü scope dışı, ileride daha agresif fraud rule olarak T100+'a devir.
  - **Steam bot wallet hariç tutulmadı:** Platform Steam bot wallet'ları (T17/T18 PaymentAddress HD wallet) BUYER_PAYMENT.ToAddress olarak görünür ama FromAddress değildir. SOURCE_ADDRESS sorgusu yalnızca FromAddress'i karşılaştırır → platform wallet'ları yanlış pozitif yaratmaz. Migration sırasında ekstra exclusion gerekmedi.
- **MSEC migration warning:** `dotnet ef migrations add` sırasında 2 informational warning ("PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning" — Transaction → BlockchainTransaction + Transaction → TransactionHistory). Bu warning T54/T55 öncesi de mevcuttu, mevcut entity konfigürasyonundan kaynaklanıyor; T56 dokunmadı.

## Mini Güvenlik

- **Secret leak:** Yok — wallet adresleri (public blockchain identifiers), IP'ler, device fingerprint'ler PII tarafında ama secret değil; flagDetail'da admin'in zaten yetkili olduğu veriler.
- **Auth/authorization:** Detector internal, endpoint yok. Çağrılar yalnızca authenticated wallet update path'inde + SYSTEM aktör olarak. Flag review (AD3-AD5) zaten T54 permission'ları ile (`VIEW_FLAGS`/`MANAGE_FLAGS`) korunuyor.
- **Input validation:** Wallet adresleri zaten `ITrc20AddressValidator` ile valide edilmiş şekilde User entity'de bulunuyor. SystemSetting CSV'si server-side parse — bilinmeyen format noop (NONE eşdeğeri). LINQ→EF SQL — SQL injection riski yok.
- **Yeni dış bağımlılık:** Yok (Microsoft.Extensions.Logging zaten transitively mevcut, yeni NuGet paketi eklenmedi).
- **Query maliyeti:** Detector wallet update başına 1 kez çağrılır. Sorgu sayısı: idempotency check (1) + payout match (1) + refund match (1) + opsiyonel — strong fired olduğunda — login sample (1) + her unique IP/fingerprint için cross-user join (≤25) + source addresses (1) + her unique source için cross-user join. Worst case ~30 sorgu/event. EF query plan benchmark'ı (sln sweep 10 dk; T56 testleri 37 sn / 15 test ≈ 2.5 sn/test, kabul edilebilir).
- **Idempotency:** Tekrar tetiklemeler aynı kullanıcı için duplicate flag yaratmıyor; outbox event de yalnızca yeni flag'de fired.

## Commit & PR

- Branch: `task/T56-multi-account-detection`
- Commit: <pending>
- PR: <pending>
- CI: <pending>
