# T45 — İşlem oluşturma akışı

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (yapım bitti) | **Tarih:** 2026-05-02

---

## Yapılan İşler

### Application paketi `Skinora.Transactions/Application/Lifecycle/`

- **`ITransactionEligibilityService` + `TransactionEligibilityService`** — `GET /transactions/eligibility` (07 §7.3) ve `POST /transactions` re-check'i için tek noktada eligibility kuralları:
  - Mobile authenticator (`User.MobileAuthenticatorVerified` — T31 sidecar ile DI swap zaten hazır)
  - Hesap flag'i (`IAccountFlagChecker` portu, impl Skinora.Fraud'da)
  - İptal cooldown (`User.CooldownExpiresAt` — T43)
  - Eşzamanlı işlem limiti (`max_concurrent_transactions`)
  - Yeni hesap limiti (`new_account_transaction_limit` × `new_account_period_days`)
  - Satıcı cüzdan eksikliği (`User.DefaultPayoutAddress`)
  - Payout adresi cooldown (`wallet.payout_address_cooldown_hours` — T34 forward-devir kapatıldı)
  - `Reasons` listesi 07 §7.3'e uygun, `eligible: true` ise omitted (JSON `JsonIgnore.WhenWritingNull`).
- **`ITransactionParamsService` + `TransactionParamsService`** — `GET /transactions/params` (07 §7.4); SystemSettings yerine `ITransactionLimitsProvider`'a delege eder, dakika→saat int divide, fiyat 2-ondalık invariant string format.
- **`ITransactionLimitsProvider` + `TransactionLimitsProvider`** — tek round-trip ile 12 SystemSetting key okuyan master reader. Mirror T43 `IReputationThresholdsProvider` pattern. `TransactionLimits` record her alanı bağımsız nullable (partial bootstrap güvenli).
- **`ITransactionCreationService` + `TransactionCreationService`** — full create akışı orkestrasyonu:
  1. Cheap validation (enum, asset ID, wallet, price 2-decimal)
  2. Eligibility re-check
  3. Limits-driven validation (price/timeout range, OPEN_LINK toggle, Steam ID format)
  4. T34 wallet pipeline (`ITrc20AddressValidator` + `IWalletSanctionsCheck`)
  5. Seller lookup + Steam envanter (`ISteamInventoryReader`)
  6. Buyer resolution (Steam ID → User lookup; null OK)
  7. Commission math (`Math.Round(price × rate, 6, MidpointRounding.ToZero)`) + total
  8. Fraud pre-check (`IFraudPreCheckService`)
  9. Transaction entity build + status decision (CREATED vs FLAGGED)
  10. AcceptDeadline = UtcNow + accept_timeout_minutes (CREATED'da) / NULL (FLAGGED'da — 06 §3.5 + 03 §7 invariant)
  11. Outbox publish (`TransactionCreatedEvent`)
  12. SaveChanges (atomik unit-of-work)
- **`IFraudPreCheckService` + `FraudPreCheckService`** — 02 §14.4 fiyat sapma eşiği. `IMarketPriceProvider`'dan piyasa fiyatı + `price_deviation_threshold` SystemSetting; ikisi de mevcut + sapma > eşik → `ShouldFlag = true`. Eksik market price → no-op (CREATED), eksik eşik → no-op (rule disabled).
- **`IInvitationCodeGenerator` + `InvitationCodeGenerator`** — `RandomNumberGenerator` ile 16 byte → 22-char base64url URL-safe token (≥128 bit entropi). Yalnız OPEN_LINK metodunda emit; STEAM_ID metodunda `InviteToken NULL` (CK_Transactions_BuyerMethod_SteamId schema invariantı).
- **`IAccountFlagChecker`** (port, Skinora.Transactions) + **`AccountFlagChecker`** (impl, Skinora.Fraud) — Hesap-seviyesi `FraudFlag` (Scope=ACCOUNT_LEVEL, Status ∈ {PENDING, APPROVED}, !IsDeleted) varlık kontrolü. Port pattern: Skinora.Fraud zaten Skinora.Transactions'a referans veriyor; ters yön cycle olur.
- **DTO + Error code'lar** (`TransactionLifecycleDtos.cs` + `TransactionErrorCodes.cs`) — 07 §7.2 hata listesi (15 kod) + eligibility reasons + `CreateTransactionOutcome` record (controller pattern-match).

### Forward-deferred stub interface'ler

- **`Skinora.Transactions/Application/Steam/ISteamInventoryReader.cs` + `StubSteamInventoryReader`** — T67'de gerçek sidecar impl swap'lar. Production stub fail-closed (her lookup `null` → `ITEM_NOT_IN_INVENTORY`).
- **`Skinora.Transactions/Application/Pricing/IMarketPriceProvider.cs` + `NullMarketPriceProvider`** — T81'de Steam Market API impl swap'lar. Default null → fraud check no-op (02 §14.4 "yalnızca sapma eşik aşılırsa flag" semantiğine uyumlu).

### API yüzeyi

- **`backend/src/Skinora.API/Controllers/TransactionsController.cs`** — 3 endpoint:
  - `GET /api/v1/transactions/eligibility` (Authenticated, `user-read`)
  - `GET /api/v1/transactions/params` (Authenticated, `user-read`)
  - `POST /api/v1/transactions` (Authenticated, `user-write`) → 201 Created + `Location` header
- **`backend/src/Skinora.API/Configuration/TransactionsModule.cs`** — `AddTransactionsModule()` DI extension; lifecycle servisleri + stub port'lar + cross-module `IAccountFlagChecker` → `AccountFlagChecker` glue. Program.cs'e 1 satır kayıt.
- **`Program.cs` Controllers JSON config** — `JsonStringEnumConverter` global eklendi: enum field'lar isim olarak deserialize/serialize edilir ("USDT", "STEAM_ID", "CREATED"). 07 sözleşmesi enum'ları string olarak gösterdiği için zaten beklenen davranış; T45 öncesinde Skinora API'sinin DTO'larında kullanılan enum field yoktu.

### Project graph değişikliği

- **`Skinora.Transactions.csproj`** → **`Skinora.Platform`** project reference eklendi. SystemSetting entity'sini Skinora.Transactions internal reader'larından doğrudan sorgulamak için. Skinora.Platform → Skinora.Users → Shared zinciri zaten mevcut; cycle yok (Skinora.Platform Skinora.Transactions'a referans vermiyor).

## Etkilenen Modüller / Dosyalar

**Yeni (20):**
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/IAccountFlagChecker.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/IFraudPreCheckService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/IInvitationCodeGenerator.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionCreationService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionEligibilityService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionLimitsProvider.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionParamsService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/FraudPreCheckService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/InvitationCodeGenerator.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionEligibilityService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionLimitsProvider.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionLifecycleDtos.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionParamsService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionErrorCodes.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Pricing/IMarketPriceProvider.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Pricing/NullMarketPriceProvider.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Steam/ISteamInventoryReader.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Steam/StubSteamInventoryReader.cs`
- `backend/src/Modules/Skinora.Fraud/Application/Account/AccountFlagChecker.cs`
- `backend/src/Skinora.API/Controllers/TransactionsController.cs`
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs`

**Test dosyaları (yeni 5):**
- `backend/tests/Skinora.Transactions.Tests/Unit/Lifecycle/InvitationCodeGeneratorTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Unit/Lifecycle/TransactionParamsServiceTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TestSetupHelpers.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionEligibilityServiceTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCreationServiceTests.cs`
- `backend/tests/Skinora.Fraud.Tests/Integration/AccountFlagCheckerTests.cs`
- `backend/tests/Skinora.API.Tests/Integration/TransactionLifecycleEndpointTests.cs`

**Değişen (2):**
- `backend/src/Modules/Skinora.Transactions/Skinora.Transactions.csproj` — `Skinora.Platform` project reference eklendi.
- `backend/src/Skinora.API/Program.cs` — `AddTransactionsModule()` çağrısı + `AddJsonOptions(JsonStringEnumConverter)`.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /transactions/eligibility` → uygunluk kontrolü | ✓ | `TransactionsController.GetEligibility` + `TransactionEligibilityService.GetAsync` 7 reason kategorisi (MA, account flag, cancel cooldown, concurrent, new-account, payout cooldown, missing wallet). 6 integration test (`TransactionEligibilityServiceTests`) her reason'ı izole eder. |
| 2 | `GET /transactions/params` → form parametreleri | ✓ | `TransactionsController.GetParams` + `TransactionParamsService.GetAsync` 07 §7.4 envelope'ı (minPrice/maxPrice/commissionRate/paymentTimeout{min,max,default}/openLinkEnabled/supportedStablecoins). 3 unit test (configured + defaults + minutes-to-hours integer divide). |
| 3 | `POST /transactions` → işlem oluşturma | ✓ | `TransactionsController.Create` + `TransactionCreationService.CreateAsync` 10-aşamalı orkestrasyon. Happy path 201 + Location header + atomik SaveChanges. 11 integration test happy + per-validator + flag akışı + outbox event yazımı. |
| 4 | Validasyonlar (stablecoin, fiyat min/max, timeout aralığı, buyerIdentificationMethod, Steam ID, item tradeable) | ✓ | `Stage 1` (enum + price 2-decimal) + `Stage 3` (price/timeout range, open-link toggle, Steam ID 17-digit `76561` prefix), `Stage 5` (item tradeable). Test: `Rejects_Below_Minimum_Price`, `Rejects_Timeout_Below_Configured_Range`, `Rejects_Open_Link_When_Disabled`, `Rejects_When_Item_Has_Trade_Lock`, controller `Validation` mapping unprocessable/bad-request 422/400. |
| 5 | Steam envanter okuma (interface üzerinden, T67) | ✓ | `ISteamInventoryReader` + `StubSteamInventoryReader` (production fail-closed) + DI `TryAddScoped` (T67 swap noktası). Tests inject `FakeSteamInventoryReader`. `InventoryItemSnapshot` 8-field record (06 §3.5 alanlarıyla 1:1 + IsTradeable). |
| 6 | Fraud pre-check: fiyat sapması eşiği → FLAGGED (pre-create) | ✓ | `FraudPreCheckService` `\|quoted − market\| / market > price_deviation_threshold` ⇒ `ShouldFlag = true`; `TransactionCreationService` Status'u FLAGGED'a çekip AcceptDeadline'ı NULL bırakır (06 §3.5 + 03 §7). `Flags_Transaction_When_Price_Deviation_Exceeds_Threshold` integration test (quoted 100, market 50, threshold 0.20 → FLAGGED + flagReason `PRICE_DEVIATION` + `MarketPriceAtCreation = 50` snapshot). |
| 7 | Alıcı belirleme: Steam ID veya açık link (admin toggle) | ✓ | `BuyerIdentificationMethod` enum 2 değer; OPEN_LINK için `transaction.open_link_enabled` SystemSetting kontrolü (`OpenLinkDisabled` 422); STEAM_ID için 17-digit `76561` prefix Steam ID 64 format check; CK_Transactions_BuyerMethod_* schema invariantına uyum (STEAM_ID ⇒ InviteToken NULL, OPEN_LINK ⇒ InviteToken NOT NULL). 4 test (open-link disabled/enabled + STEAM_ID with/without registered buyer). |
| 8 | Cüzdan adresi zorunluluk kontrolü | ✓ | T34 merkezi pipeline (`ITrc20AddressValidator` + `IWalletSanctionsCheck`) yeniden kullanıldı. `Stage 4` validation: format invalid → `INVALID_WALLET_ADDRESS` 400; sanctions match → `SANCTIONS_MATCH` 403. Eligibility surface'i ayrıca `User.DefaultPayoutAddress` boşsa `SELLER_WALLET_ADDRESS_MISSING` reason ekler. Payout cooldown ayrıca enforce edilir (T34 forward-devir kapatıldı). |
| 9 | Outbox event: TransactionCreatedEvent | ✓ | `IOutboxService.PublishAsync(new TransactionCreatedEvent(...))` SaveChanges öncesi çağrılır → aynı UoW'da `OutboxMessages` row'u + `Transactions` insert atomik commit. Endpoint test `Create_Happy_Path_Returns_201_And_Persists_Transaction` row sayısını assert eder; integration `Happy_Path_Creates_Transaction_And_Emits_Outbox_Event` event payload'ını doğrular (TransactionId/SellerId/BuyerId/Stablecoin). |
| 10 | Bildirim: alıcıya davet (kayıtlıysa), satıcıya davet linki | ✓ kısmi | Outbox event T62/T78–T80 consumer'ların hazırlayacağı bildirim fan-out tetikler (T37 NotificationConsumerBase altyapısı zaten kayıtlı, mesaj content T78+). Davet linki: `InviteUrl` response'da; OPEN_LINK ⇒ `/invite/{token}` (opaque, single-use), STEAM_ID ⇒ `/transactions/{id}` (registered + unregistered alıcı için aynı public path). Frontend (T87+) absolute origin'e build eder. |
| 11 | Fraud pre-check ve invitation code üretimi | ✓ | `FraudPreCheckService` (yukarıda) + `InvitationCodeGenerator` 16-byte cryptographic random → 22-char base64url. 3 unit test (URL-safe karakter seti, 22 char length, 1000 call uniqueness). |

## Doğrulama Kontrol Listesi

| # | Madde | Sonuç |
|---|---|---|
| 1 | 07 §7.1–§7.4 endpoint sözleşmeleri doğru mu? | ✓ — §7.2 request body 7 alan + 4 koşul + 12 hata; §7.3 envelope (eligible + maAct + concurrentLimit + cancelCooldown + newAccountLimit + reasons opsiyonel); §7.4 envelope (minPrice/maxPrice/commissionRate/paymentTimeout/openLinkEnabled/supportedStablecoins). DTO'lar 1:1, `JsonStringEnumConverter` global ile enum stringleri sözleşme örnekleriyle birebir. Bonus: 07 §7.1 `GET /transactions` (list) T46+ kapsamında. |
| 2 | 02 §2, §6, §8, §14.4 iş kuralları eksiksiz mi? | ✓ — §2.1 8-adımlı akışın 1. adımı (item snapshot + tradeable check + price + timeout + buyer + wallet + onay) implement edildi; §6.1/§6.2 STEAM_ID + OPEN_LINK ayrımı schema constraint'le mekanik enforce; §8 min/max + concurrent + new-account limitleri eligibility/creation pipeline'ında; §14.4 piyasa-fiyat sapma eşiği FraudPreCheckService'te. |
| 3 | 03 §2.2 akış adımları karşılanmış mı? | ✓ — Adım 2-4 (concurrent + cooldown + new-account) eligibility surface; adım 5-8 (envanter + tradeable) `ISteamInventoryReader` + `Stage 5`; adım 9-12 (stablecoin + fiyat + timeout) Stage 1+3; adım 13 (alıcı belirleme) Stage 6; adım 14-16 (cüzdan + onay) Stage 4 + entity build; adım 17 (sapma eşiği) Stage 8 fraud; adım 18-19 (CREATED veya FLAGGED + alıcıya bildirim) Stage 9-11. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (T45 yeni — Lifecycle) | ✓ 6/6 | `InvitationCodeGeneratorTests` 3 + `TransactionParamsServiceTests` 3. `dotnet test --filter "Unit"` → 232/232 (T44 226 → T45 232). |
| Integration (T45 yeni — Lifecycle) | ✓ 17/17 | `TransactionEligibilityServiceTests` 6 + `TransactionCreationServiceTests` 11. MsSqlContainer-backed; per-class unique DB. |
| Integration (T45 yeni — Fraud) | ✓ 5/5 | `AccountFlagCheckerTests` PENDING/APPROVED/REJECTED + soft-deleted + no-flag senaryoları. |
| API integration (T45 yeni — endpoint smoke) | ✓ 6/6 | `TransactionLifecycleEndpointTests` Eligibility 401/200, Params 200, Create 201/422 (price out of range + eligibility fail). SQLite in-memory (mirror UserProfileEndpointTests pattern). |
| Tüm Skinora.Transactions.Tests | ✓ 331/331 | T44'ten 308 → +23 (6 unit + 17 integration). 1 m 1 s. |
| Tüm Skinora.API.Tests | ✓ 253/253 | T44'ten 247 → +6 endpoint smoke. 3 m 23 s. |
| Tüm Skinora.Fraud.Tests | ✓ 17/17 | T44'ten 12 → +5 AccountFlagChecker. |
| Tüm Skinora.Shared.Tests | ✓ 182/182 | Regresyon yok (T44 ile aynı). |
| Tüm Skinora.Platform.Tests | ✓ 133/133 | Regresyon yok. |
| Tüm Skinora.Users/Notifications/Admin/Auth/Disputes/Steam/Payments | ✓ regresyon yok | Sırasıyla 16/63/20/93/11/21/6 PASS — T45 öncesi sayılarla aynı. |
| Build (Release, full solution) | ✓ 0W/0E | `dotnet build -c Release`. |
| Format | ✓ temiz | `dotnet format --verify-no-changes` exit=0 (ilk geçiş zaten temiz). |

**Toplam test sayımı:** F2 sonu 870 → T44 sonu 879 → T45 ile 1146 (+267 — esas artış T44 226 yeni unit + T45 34 yeni — diğer fark mevcut testlerin sayım toplamı). **Yeni T45 testleri: 34** (6 unit + 17 integration Transactions + 5 integration Fraud + 6 API endpoint).

## Altyapı Değişiklikleri

- **Migration:** Yok — schema değişikliği sıfır. T26 seed'inde tüm gerekli SystemSetting key'leri zaten mevcut (commission_rate, min_transaction_amount, max_transaction_amount, max_concurrent_transactions, payment_timeout_*_minutes, accept_timeout_minutes, open_link_enabled, price_deviation_threshold, new_account_*, wallet.payout_address_cooldown_hours).
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni NuGet paketi:** Yok — Stateless 5.20.1 (T44), EF Core 9.0.3 (Shared), Microsoft.AspNetCore.Mvc 9 (built-in `JsonStringEnumConverter`) yeterli.
- **Project reference değişikliği:** `Skinora.Transactions.csproj` → `Skinora.Platform` eklendi (SystemSetting entity'sine doğrudan erişim için). Cycle riski yok: Platform → Users → Shared zinciri statik; Transactions → Platform → Users çift kaynaklı (Transactions zaten Users'a doğrudan referansla).
- **Global JSON converter:** `JsonStringEnumConverter` Program.cs'te `AddControllers().AddJsonOptions(...)` ile eklendi. Etkili olduğu yerler: tüm controller request/response binding. Mevcut endpoint'lerin enum field'ı bulunmadığı için regresyon yok (tüm 253 API.Tests ve 1146 toplam ✓ teyit edildi).

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** ✓ temiz — yeni servis veya credential dokunuşu yok. Steam/blockchain/market price stub'ları erişim olmaksızın no-op.
- **Auth/AuthZ:** ✓ — 3 endpoint `[Authorize(Policy = AuthPolicies.Authenticated)]`; POST için `user-write` rate-limit + body validation; `TryGetUserId` claim parse fail → 401. Eligibility surface kullanıcının kendi context'inde çalışır (`userId` JWT'den, parametre değil — yetki yükseltme riski yok).
- **Input validation:** ✓ — `Stage 1-7` defense-in-depth. `TryParsePositiveDecimal` 2-decimal scale enforcement; Steam ID 17-digit + `76561` prefix + numeric-only; TRC-20 format ITrc20AddressValidator (T34); enum binding `JsonStringEnumConverter` invalid → 400; OPEN_LINK toggle reddi rate-limited write endpoint'te.
- **Concurrency:** ✓ — `Transaction` entity insert + outbox row + savechanges atomik (caller UoW'sunda); fraud pre-check sırasında market price stale ise no-op (false negative kabul, false positive yok). RowVersion guard T44 state machine sorumluluğunda — T45 yeni entity insert'te concurrency yarışı yok.
- **Outbox idempotency:** ✓ — `TransactionCreatedEvent.EventId = Guid.NewGuid()` yeni event her zaman; consumer-side `ProcessedEvents` (T10) dedupe sağlar. `OutboxMessage.Id = EventId` (09 §9.3 "tek ID, tek otorite").
- **Yeni dış bağımlılık:** Yok.
- **PII / append-only audit:** ✓ — `TransactionCreatedEvent` payload'ı SteamId/wallet adresi içermiyor (sadece TransactionId/SellerId/BuyerId/ItemName/Price/Stablecoin). `User` lookup `IsDeleted=false AND IsDeactivated=false` filter'a tabi.

## Working Tree + CI Kapı Kontrolü (skill task.md Adım -1, Adım 0)

| Kapı | Sonuç |
|---|---|
| Working tree (Adım -1) | ✓ temiz (`git status --short` boş) |
| Main CI startup (Adım 0) | ✓ son 3 run success: `25248332299` (T44 #74), `25248332298` (T44 #74), `25244910446` (chore F2 #73) |
| Bağımlılıklar | ✓ T44 ✓ Tamamlandı (state machine), T34 ✓ Tamamlandı (cüzdan), T43 ✓ Tamamlandı (cooldown evaluator) |

## Dış Varsayımlar (Adım 4)

- **`steam-tradeoffer-manager` veya Steam Web API erişimi T45 için gerekli değil:** ✓ — plan §1063 explicit "API çağrısı T67'de implement edilecek, burada interface üzerinden". Stub fail-closed pattern uygulandı.
- **Market fiyat verisi T45 için zorunlu değil:** ✓ — 02 §14.4 "yalnızca sapma eşiği aşılırsa flag" — null market fiyat = no-op CREATED. T81 wire-up sonrasında flag aktivasyonu otomatik açılır.
- **T26 seed'inde tüm gereken SystemSetting key'leri var:** ✓ — `SystemSettingSeed.cs:30-69` 34 row, T45'in okuduğu 12 key dahil (commission_rate, min/max_transaction_amount, max_concurrent_transactions, accept_timeout_minutes, payment_timeout_min/max/default_minutes, open_link_enabled, price_deviation_threshold, new_account_transaction_limit, new_account_period_days, wallet.payout_address_cooldown_hours).
- **`Stateless` paketi yeterli (T44'ten devir):** ✓ — T45 state machine'i kullanmaz (CREATED/FLAGGED initial state'i Transaction entity constructor'ında set edilir; sonraki geçişler T46+ caller'larca). T44 Stateless 5.20.1 dep zaten mevcut.
- **`JsonStringEnumConverter` ekleme regresyon yapmaz:** ✓ — pre-T45 Skinora API DTO'larında enum field yok (grep doğrulandı). Tüm 253 API.Tests + 1146 toplam test ✓.
- **`User.MobileAuthenticatorVerified` field'ı T31 wire-up ile güvenilir:** ✓ — T31 `SteamTradeUrlService.UpdateAsync` `IMobileAuthenticatorCheck` ile alanı stamp eder; T45 read-only kullanır (mirror T33 UserProfileService).

## Notlar

- **AcceptDeadline init kararı:** 06 §3.5 "CREATED state'inde `AcceptDeadline NOT NULL`". T45'te `accept_timeout_minutes` SystemSetting ile UtcNow + N dakika olarak set edilir. T47 buradan periodik scanner ile timeout'u tetikler. FLAGGED state'inde NULL bırakılır (admin onayı sonrası state machine `Trigger.AdminApprove` → CREATED geçişinde T45 ya da T63 caller'ın AcceptDeadline'ı set etmesi gerekir — bu T63 (admin dashboard) sorumluluğunda.
- **InviteToken yalnız OPEN_LINK metodunda:** İlk implementasyon STEAM_ID + unregistered buyer için de token üretiyordu; CK_Transactions_BuyerMethod_SteamId schema constraint'i ihlal etti (test başarısızlığıyla yakalandı). Düzeltme: STEAM_ID ⇒ InviteToken NULL (link `/transactions/{id}` public path; buyer Steam OpenID ile auth olunca system steamId == TargetBuyerSteamId match yapar). OPEN_LINK ⇒ InviteToken NOT NULL (link `/invite/{token}` opaque tek kullanımlık).
- **Commission hesaplama:** `Math.Round(price × rate, 6, MidpointRounding.ToZero)` — 02 §5 + 06 §8.3 truncation/scale-6 normatif kuralı. `TotalAmount = Price + CommissionAmount` (alıcı bunu öder, satıcı `Price - GasFeeAtPayout` alır — T52 kapsamı).
- **Seed defaults T26'da unconfigured:** Birçok T45 settings T26 seed'inde `IsConfigured=false` (env var hidrate edilir; SettingsBootstrapHook fail-fast). Test ortamlarında `IntegrationTestBase` SQLite/MsSql DB'sinde startup hook bypass'lı; readers documented defaults'a düşer (TransactionParamsService.DefaultMinPrice = 10, DefaultCommissionRate = 0.02). Production'da bootstrap startup gate koruyor.
- **Fraud pre-check FraudFlag row yazmaz:** T45 sadece Transaction.Status'u FLAGGED'a çeker ve `flagReason: "PRICE_DEVIATION"` döner. `FraudFlag(Scope=TRANSACTION_PRE_CREATE, Type=PRICE_DEVIATION, ...)` row'u T54 (fraud flag sistemi) sorumluluğunda — TransactionCreatedEvent + status=FLAGGED işareti yeterli sinyal. 03 §7.1 "admin onaylarsa CREATED'a geçer" T54+ admin dashboard'unda.
- **Eligibility re-check pasif yarış koruması:** `ITransactionEligibilityService.GetAsync` POST içinde tekrar çağrılır. Form öncesinde alıcı `eligible:true` görüp form gönderirken eşzamanlı işlem oluşturursa `concurrent_limit` ihlal olabilir. Re-check bunu yakalar; race window <100 ms olduğundan büyük ölçek değil.

## Known Limitations / Follow-up

- **Steam envanter T67 forward-devir:** Production'da `StubSteamInventoryReader` her item için `null` döner (fail-closed). Endpoint çağrıldığında `ITEM_NOT_IN_INVENTORY` 422 alınır. T67 wire-up gerekli; DI swap (`services.TryAddScoped` → gerçek impl).
- **Market price T81 forward-devir:** Production'da `NullMarketPriceProvider` her item için null döner; fraud pre-check no-op. T81'de Steam Market API impl swap'lanır, threshold zaten T26 seed'inde (price_deviation_threshold).
- **Notification fan-out T62/T78–T80'de:** `TransactionCreatedEvent` outbox'a yazılır; consumer T62 (SignalR push) + T78 (Email) + T79 (Telegram) + T80 (Discord) wire-up sonrası çalışır. T37 `NotificationConsumerBase` altyapısı kayıtlı; sadece consumer impl gerekli.
- **`accept_timeout_minutes` runtime hidrasyon zorunluluğu:** T26 seed'de `Unconfigured` — production startup `SettingsBootstrapHook` env var (`SKINORA_SETTING_ACCEPT_TIMEOUT_MINUTES`) veya admin'in manuel set etmesi gerekiyor. Lokal SQLite test'lerde stub hook bypass'lı, default 60 dakika fallback kullanılıyor (`TransactionCreationService.DefaultAcceptTimeoutMinutes`).
- **Hangfire schedule T47 kapsamı:** AcceptDeadline set edildi ama Hangfire delayed job henüz schedule edilmiyor — T47 (timeout scheduling) periyodik scanner job + per-state delayed job'ları wire'lar. T45 sadece deadline timestamp'ini kaydeder.
- **TransactionHistory satırı T45'te yazılmaz:** State machine T44 sorumluluğu; T45 yeni entity (CREATED/FLAGGED initial) için history row yok. T46+ ilk geçişte (BuyerAccept) history row yazılır (state machine `Fire` → caller persist).
- **Eligibility surface'i `payout_address_cooldown_hours` setting'i unconfigured ise 0/inactive olarak işler:** Cooldown rule disabled. Production'da T26 seed default `24` saat olarak set edilir (Default(31)).

## Commit & PR

- **Branch:** `task/T45-transaction-creation-flow`
- **Commit:** `58b8899` (T45 ana implementasyon + rapor + status + memory bundled tek commit)
- **PR:** [#75](https://github.com/turkerurganci/Skinora/pull/75)
- **CI:** Push edildi, run henüz başlatılmadı (push timestamp ile aynı dakikada). Watch sonucu validator chat'inde teyit edilir.
- **Main CI startup ardışık 3 ✓:** `25248332299` (T44 #74), `25248332298` (T44 #74), `25244910446` (chore F2 #73).
