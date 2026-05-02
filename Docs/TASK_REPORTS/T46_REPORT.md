# T46 — Alıcı kabul akışı

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (yapım bitti, validator bekleniyor) | **Tarih:** 2026-05-02

---

## Yapılan İşler

### Application paketi `Skinora.Transactions/Application/Lifecycle/`

- **`ITransactionAcceptanceService` + `TransactionAcceptanceService`** — `POST /transactions/:id/accept` (07 §7.6, 03 §3.2) tam pipeline:
  1. Request validation (`refundWalletAddress` whitespace-strict — `REFUND_ADDRESS_REQUIRED`)
  2. Transaction load + buyer load (404 / 401 ayrımı)
  3. State guard — yalnız `CREATED`'tan kabul (FLAGGED/diğer state'ler `INVALID_STATE_TRANSITION`, ACCEPTED `ALREADY_ACCEPTED`)
  4. Party guard — Yöntem 1 (Steam ID match `StringComparison.Ordinal`) veya Yöntem 2 (OPEN_LINK; satıcı kendi listesini kabul edemez)
  5. T34 wallet pipeline (`ITrc20AddressValidator` → `IWalletSanctionsCheck`)
  6. Refund-address cooldown (`wallet.refund_address_cooldown_hours` SystemSetting + `User.RefundAddressChangedAt` — 02 §12.3 alıcı tarafı kapatıldı, T45 forward-devir kapanış notu kaldırıldı)
  7. State transition `CREATED → ACCEPTED` — `Transaction.BuyerId` + `BuyerRefundAddress` 06 §3.5 invariant öncesi set, sonra `TransactionStateMachine.Fire(BuyerAccept)` (RowVersion guard + state machine domain primitive `AcceptedAt` OnEntry)
  8. `User.DefaultRefundAddress` + `RefundAddressChangedAt` snapshot (yeni adres ise; aynı adres ise cooldown timer reset etmez)
  9. Outbox publish (`BuyerAcceptedEvent`)
  10. SaveChanges (atomik unit-of-work)
- **`ITransactionDetailService` + `TransactionDetailService`** — `GET /transactions/:id` (07 §7.5) public + authenticated varyantları:
  - **Role resolution:** seller (UserId match) / buyer (UserId match veya STEAM_ID method'da Steam ID match — "target buyer pre-acceptance" — 03 §3.2 step 1) / non-party 403 / public (anonim)
  - **Public varyant:** id + status + minimal item + price + stablecoin + seller display name + `availableActions{ canAccept=false, requiresLogin=true }` (07 §7.5 örnek)
  - **Authenticated varyant:** tam DTO 07 §7.5 sözleşmesi; T46-erişilebilir alanlar (buyer ACCEPTED+, timeout active, inviteInfo seller+CREATED+OPEN_LINK, flagInfo FLAGGED, holdInfo EMERGENCY_HOLD, paymentEvents [] from ITEM_ESCROWED+) doldurulur; gerisi (payment, sellerPayout, refund, dispute, cancelInfo) state-blocked → null suppress (T47/T49/T51/T54/T58/T59/T70+ forward-devir)
  - **Reputation read-path:** `SuccessfulTransactionRate × 5` `Math.Round(..., 1, ToZero)` (06 §3.1 + T43 closure); rate null → reputationScore null (T33 contract uyumlu)
  - **availableActions:** EMERGENCY_HOLD'da hepsi false (07 §7.5 explicit kural); canAccept=role=buyer + CREATED + BuyerId null; canCancel=party + active state + ödeme öncesi; canDispute=buyer + ITEM_ESCROWED..ITEM_DELIVERED + active dispute yok; canEscalate=false (T58 forward)
  - **Timeout sub-DTO:** active deadline state'ine göre seçim (accept/trade_offer_seller/payment/trade_offer_buyer); freeze metadata (T50 forward) sızdırılır
- **`TransactionDetailDto.cs`** — top-level + 11 sub-record (TransactionItemDto, TransactionPartyDto, TransactionTimeoutDto, TransactionPaymentDto, SellerPayoutDto, RefundDto, CancelInfoDto, FlagInfoDto, HoldInfoDto, DisputeSummaryDto, InviteInfoDto, PaymentEventDto, AvailableActionsDto). Optional alanlar `JsonIgnore.WhenWritingNull` ile suppress.
- **`AcceptTransactionRequest/Response/Outcome`** + `AcceptTransactionStatus` enum (`TransactionLifecycleDtos.cs`'e eklendi)
- **Hata kodları** (`TransactionErrorCodes.cs`'e eklendi): `TRANSACTION_NOT_FOUND`, `NOT_A_PARTY`, `STEAM_ID_MISMATCH`, `ALREADY_ACCEPTED`, `INVALID_STATE_TRANSITION`, `WALLET_CHANGE_COOLDOWN_ACTIVE`, `REFUND_ADDRESS_REQUIRED` — 07 §7.6 hata listesi 1:1.

### Domain event

- **`backend/src/Skinora.Shared/Events/BuyerAcceptedEvent.cs`** — Outbox event (T62 SignalR + T78–T80 Email/Telegram/Discord forward-devir consumer'larca dispatch). EventId/TransactionId/SellerId/BuyerId/ItemName/AcceptedAt/OccurredAt alanları.

### API yüzeyi

- **`TransactionsController.cs`** — 2 yeni endpoint:
  - `GET /api/v1/transactions/{id:guid}` (`AllowAnonymous` + `RateLimit("public")`) — public/authenticated branching servis tarafından, JWT varlığına göre
  - `POST /api/v1/transactions/{id:guid}/accept` (`Authorize(Authenticated)` + `RateLimit("user-write")`) — outcome pattern-match → 200/400/401/403/404/409
- **`TransactionsModule.cs`** — `ITransactionDetailService` + `ITransactionAcceptanceService` DI kayıtları (Scoped).
- Migration yok — yeni alan/seed yok; T26 + T28 + T34 seed'leri yeterli (`wallet.refund_address_cooldown_hours` zaten T34'ten beri seed'li, default 24).

### Test yüzeyi

- **`TransactionAcceptanceServiceTests.cs`** (12 integration, MsSqlContainer): happy path STEAM_ID + outbox + `RefundAddressChangedAt` snapshot, Steam ID mismatch, OPEN_LINK first-comer + ALREADY_ACCEPTED, OPEN_LINK seller self-accept reddi, refund address whitespace, TRC-20 invalid, sanctions match, cooldown active, cooldown expired (allowed), 404, FLAGGED → INVALID_STATE_TRANSITION, ALREADY_ACCEPTED preflight, same-address cooldown timer reset etmez.
- **`TransactionDetailServiceTests.cs`** (10 integration, MsSqlContainer): public anonymous variant, buyer view CREATED, seller view OPEN_LINK + invite info, buyer block ACCEPTED+, non-party 403, 404, accept timeout remainingSeconds, EMERGENCY_HOLD freeze actions, FLAGGED flag info, target buyer Steam ID match pre-acceptance.
- **`TransactionAcceptanceUnitTests.cs`** (5 unit): DTO JSON serialization (status enum string, public availableActions suppression, authenticated availableActions suppression), Steam ID match Theory (5 InlineData — case sensitivity, trim guard), error code constant 07 §7.6 contract assert.
- **`TransactionLifecycleEndpointTests.cs`** (8 yeni HTTP-level): Detail anonymous public, Detail authenticated buyer full, Detail non-party 403, Detail 404, Accept unauth 401, Accept happy + outbox row, Accept Steam ID mismatch 403, Accept invalid wallet 400.

## Etkilenen Modüller / Dosyalar

**Yeni (8):**
- `backend/src/Skinora.Shared/Events/BuyerAcceptedEvent.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionAcceptanceService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionAcceptanceService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionDetailService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionDetailService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionDetailDto.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionAcceptanceServiceTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionDetailServiceTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Unit/Lifecycle/TransactionAcceptanceUnitTests.cs`

**Değişen (5):**
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionErrorCodes.cs` — 7 yeni hata kodu
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionLifecycleDtos.cs` — Accept request/response/outcome + status enum
- `backend/src/Skinora.API/Controllers/TransactionsController.cs` — 2 yeni endpoint + outcome pattern-match
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — 2 yeni DI kaydı
- `backend/tests/Skinora.API.Tests/Integration/TransactionLifecycleEndpointTests.cs` — 8 yeni endpoint testi + `SeedTransactionAsync` helper

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /transactions/:id` → işlem detay (public/authenticated, role bazlı varyant) | ✓ | `TransactionDetailService` + 10 integration + 4 endpoint testi (anonymous, buyer, seller, non-party 403, 404) |
| 2 | `POST /transactions/:id/accept` → alıcı kabulü | ✓ | `TransactionAcceptanceService` + 12 integration + 4 endpoint testi |
| 3 | Steam ID eşleşme kontrolü (Yöntem 1) veya açık link (Yöntem 2, ilk gelen) | ✓ | Yöntem 1: `Steam_Id_Mismatch_Rejects_With_403` + `Happy_Path_SteamId_Method_*`; Yöntem 2: `Open_Link_Method_First_Comer_Wins_*` + `Open_Link_Seller_Cannot_Accept_Own_Listing` |
| 4 | İade adresi zorunlu (TRC-20 format + sanctions) | ✓ | `Refund_Address_Required_When_Empty` (whitespace) + `Refund_Address_Format_Invalid_Returns_400` (TRC-20) + `Sanctions_Match_Rejects_With_403` |
| 5 | Alıcı refund-address cooldown kontrolü | ✓ | `Wallet_Cooldown_Active_Rejects_With_403` + `Wallet_Cooldown_Expired_Allows_Acceptance` (`wallet.refund_address_cooldown_hours` SystemSetting + `User.RefundAddressChangedAt` 02 §12.3) |
| 6 | State geçişi: CREATED → ACCEPTED | ✓ | `Happy_Path_*` test'inde `persisted.Status == ACCEPTED` + `AcceptedAt NOT NULL`; FLAGGED → INVALID_STATE_TRANSITION test'i invariantı korur |
| 7 | Outbox event: `BuyerAcceptedEvent` | ✓ | `Happy_Path_SteamId_Method_*` test'i `_outbox.Published[0]` typed assertion (TransactionId, SellerId, BuyerId); endpoint test'i `OutboxMessage` row sayar |
| 8 | Bildirim: satıcıya "alıcı kabul etti" | ~ Kısmi | `BuyerAcceptedEvent` outbox'a publish ediliyor; consumer T62 (SignalR) + T78–T80 (Email/Telegram/Discord) **forward-devir** — 02 §6 + 03 §3.2 son adımı consumer'a düştüğünde fan-out tamamlanır. T45 ile aynı pattern (TransactionCreatedEvent forward-devir). |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit Lifecycle (T46) | ✓ 5/5 | `TransactionAcceptanceUnitTests` 5 fact (1 Theory × 5 InlineData = 5 test sayımı) |
| Integration Acceptance | ✓ 12/12 | `TransactionAcceptanceServiceTests` MsSqlContainer |
| Integration Detail | ✓ 10/10 | `TransactionDetailServiceTests` MsSqlContainer |
| Endpoint (API.Tests) | ✓ 8/8 | `TransactionLifecycleEndpointTests` T46 yeni: 4 detail + 4 accept |
| Skinora.Transactions.Tests | ✓ 363/363 | T45: 331 → T46: 363 (+32) |
| Skinora.API.Tests | ✓ 261/261 | T45: 253 → T46: 261 (+8) |
| Tüm 12 test assembly | ✓ 1186/1186 | Users 16 + Payments 6 + Admin 20 + Disputes 11 + Fraud 17 + Notifications 63 + Auth 93 + Steam 21 + Shared 182 + Platform 133 + Transactions 363 + API 261 |

Komut: `dotnet test Skinora.sln -c Release --no-build` (Release).

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Validator bekleniyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Yok**. T34 + T28 seed'leri yeterli (`wallet.refund_address_cooldown_hours` default 24 saat seed).
- Config/env değişikliği: Yok.
- Docker değişikliği: Yok.
- Yeni dış bağımlılık: Yok.

## Commit & PR

- Branch: `task/T46-buyer-acceptance`
- Commit: (push sonrası)
- PR: (push sonrası)
- CI: bekleniyor

## Known Limitations / Follow-up

- **Bildirim fan-out:** `BuyerAcceptedEvent` consumer T62 (SignalR) + T78–T80 (Email/Telegram/Discord) forward-devir. T45 ile aynı pattern.
- **Reputation read-path:** Threshold gating (account age + completed-tx count) `IReputationThresholdsProvider` üzerinden T33 user-profile kontratında uygulanıyor; detail endpoint'te basitleştirilmiş read-path — eligible olmayan kullanıcılar için `User.SuccessfulTransactionRate` zaten null olur (T43 aggregator sadece eligible kullanıcılar için yazar). Eğer ek threshold check gerekirse sonraki doc-pass'te netleştirilir; minor.
- **Detail endpoint state coverage:** payment, sellerPayout, refund, dispute alanları null suppress; T47/T49/T51/T54/T58/T70+ task'ları ilgili state'leri açtığında dolar. 07 §7.5 sözleşmesinin tüm alanları DTO'da mevcuttur — yalnızca data unavailable (state-blocked) iken suppress edilir.
- **`paymentEvents` bootstrapping:** ITEM_ESCROWED'dan önce `null` (suppress); ITEM_ESCROWED+'da empty array. Gerçek olaylar T70+ blockchain monitoring'inden gelir.

## Notlar

- **Adım -1 Working Tree Hygiene:** task öncesi `git status` boş — temiz.
- **Adım 0 Main CI Startup:** son 3 main run ✓✓✓ (`25250478338`, `25250478269`, `25248332299`).
- **Dış varsayımlar:** TRC-20 validator + IWalletSanctionsCheck (T34 ✓), BuyerAcceptedEvent consumer wiring (forward-devir, T45 pattern), refund cooldown SystemSetting (T34 seed 24h default), Steam ID 64 format helper (TransactionCreationService'te mevcut, inline replicated). 0 kırık varsayım.
- **`ITransactionDetailService.GetAsync` signature** `callerSteamId` parametresi alır — JWT'den extract edilir; "target buyer Steam ID match pre-acceptance" rolünü çözmek için zorunlu (03 §3.2 step 1: alıcı işlemi kabul etmeden önce detay sayfasını görür).
- **Same-address cooldown semantik:** alıcı `refundWalletAddress` request'i kullanıcının mevcut `User.DefaultRefundAddress`'i ile aynı ise `RefundAddressChangedAt` timer'ı **reset edilmez** — gereksiz cooldown kayıpsız işlem. Test: `Same_Refund_Address_Does_Not_Reset_Cooldown_Timer`.
- **EMERGENCY_HOLD action freeze:** detail endpoint'te `IsOnHold=true` durumunda tüm `availableActions` false (07 §7.5 explicit kural); test: `Emergency_Hold_Forces_All_Actions_False`.
- **Format verify:** `dotnet format --verify-no-changes` exit=0.
- **Lokal Release build:** 0W/0E.
