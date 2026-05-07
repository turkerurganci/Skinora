# T63 — Admin dashboard ve işlem yönetimi API

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (yapım bitti, validate chat'i bekliyor) | **Tarih:** 2026-05-07

---

## Yapılan İşler

T63, 07 §9 admin endpoint setinden T39/T41/T42/T54/T59 sonrası geriye kalan dört yeni okuma yüzeyini hayata geçirir:

- **AD1 `GET /admin/dashboard`** — özet kartlar + Steam bot snapshot + son 5 flag (07 §9.1).
- **AD6 `GET /admin/transactions`** — paginated işlem listesi, filtre + sort + arama (07 §9.6).
- **AD7 `GET /admin/transactions/:id`** — sekiz admin-özel bölümlü tam detay (07 §9.7).
- **AD10 `GET /admin/steam-accounts`** — platform Steam botlarının operasyonel snapshot'ı + warning banner (07 §9.10).

Bunlara ek olarak T39'un AD16b placeholder'ı (`GET /admin/users/:steamId/transactions` her zaman boş PagedResult dönüyordu) yeni `IAdminTransactionQueryService.ListForUserAsync` ile gerçek veriye bağlandı.

1. **Yeni port + DTO seti — `Skinora.Transactions/Application/Admin/`:**
   - `IAdminTransactionQueryService.cs` — 3 metot: `ListAsync(query)`, `ListForUserAsync(steamId, page, pageSize)`, `GetDetailAsync(id)`. `AdminTransactionListQuery` record'ı tüm filtre alanlarını taşır (status, stablecoin, date range, amount range, search, sort).
   - `AdminTransactionQueryDtos.cs` — AD6 list item DTO + AD7 detail DTO (sekiz alt-bölüm: `statusHistory`, `paymentDetail`, `sellerPayoutDetail`, `refundDetail`, `notificationHistory`, `disputeHistory`, `flagHistory`, `adminActions`).

2. **Implementasyon — `Skinora.API/Services/AdminTransactionQueryService.cs`:**
   API composition root'una konuldu çünkü AD7 detay'ı `Skinora.Notifications`, `Skinora.Disputes` ve `Skinora.Fraud` modüllerinden veri compose eder; `Skinora.Transactions` bu modülleri referans edemez (project cycle olur). Pattern: `PayoutEscalationAdminResolver` (T60) aynası.
   - Tüm sorgular `AsNoTracking`. AD6 list iki query (count + page slice) + tek dictionary join ile parties resolve eder; pattern `FraudFlagAdminQueryService` aynası.
   - AD7 detay 4 küçük takip query'sine dağılır (history / blockchain rows / notifications / disputes+flags) — her subset ayrı plan, SQL Server plan cache stabil.
   - **Search escape:** `%`/`_`/`[` bracket-wrapping ile escape edilir (`[%]`/`[_]`/`[[]`); SQL Server'da ESCAPE clause gerektirmez. T39'un advisory'si (admin search LIKE pattern escape T63 standardizasyonu) burada karşılanır.
   - **Admin cancel state set'i (AD7 `canCancel`):** `_adminCancellableStates` HashSet 7 değer (CREATED, ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, FLAGGED) — 07 §9.20 ile birebir; `IsOnHold = true` olduğunda canCancel = false. T59 `AdminTransactionService` aynı kısıtlamayı doğrudan state machine üzerinde enforce eder; AD7 sadece UI'a izin sinyali döner.
   - **Notification fan-out hesabı:** `IN_APP` her zaman implicit (Notification row'u kendi başına in-app delivery'dir); `NotificationDelivery.Status = SENT` rows external channel'ları (EMAIL/TELEGRAM/DISCORD) ekler.
   - **`UnknownParty()` placeholder:** anonimleştirilmiş user (02 §19) ile `Deleted User` etiketiyle stabil DTO döner; FK orphan toleransı.

3. **AD10 — `Skinora.Steam/Application/Admin/AdminSteamBotQueryService.cs` (yeni Application/Admin dizini):**
   - `IAdminSteamBotQueryService.cs` — `ListAsync(ct)` tek metot.
   - `AdminSteamBotDtos.cs` — `AdminSteamAccountsResponse` (accounts + warningMessage) + `AdminSteamAccountDto` 11 alan.
   - `SteamDailyTradeOfferLimit = 200` const — Steam ToS protokol limiti, SystemSetting değil.
   - Forward-deferred T69 alanları: `failoverStatus = "NONE"`, `recoveryTransactionCount = 0`, `restrictionReason = null`. Sidecar bot health pipeline T64–T69 devraldığında DI swap ile gerçek hesap girişi yapılır.
   - `BuildWarning`: ACTIVE olmayan bot var ise Türkçe özet mesajı (`"Sorunlu bot hesabı tespit edildi — RESTRICTED: 1, BANNED: 0..."`); aksi halde `null`.

4. **AD1 dashboard composer — `Skinora.API/Services/AdminDashboardService.cs`:**
   - `IAdminDashboardService.cs` interface composition root'ta.
   - `AdminDashboardDtos.cs` — `AdminDashboardResponse` (summaryCards + steamAccounts + recentFlags) + `AdminDashboardSummaryCardsDto` (4 sayaç) + `AdminDashboardRecentFlagDto` (5 alan).
   - 4 indexed count (`activeTransactions`/`pendingFlags`/`dailyCompleted`/`weeklyCompleted`) + son 5 flag (newest-first) + AD10 service delegate'i (steamAccounts bloğu 1:1 `/admin/steam-accounts` ile uyumlu, drift yok).
   - **Active = NOT terminal:** `_terminalStates = [COMPLETED, CANCELLED_TIMEOUT, CANCELLED_SELLER, CANCELLED_BUYER, CANCELLED_ADMIN]`. `EMERGENCY_HOLD` flag'i (entity'de `IsOnHold`) status değil — donmuş olsa da hesaba aktif olarak yansır. `TransactionDetailService.IsTerminal` aynası; intra-module Skinora.Transactions internal'ı yerine kopya tutuldu (composition root'tan internal sızdırma yok).
   - **Daily/weekly:** `CompletedAt >= UtcNow - 24h` / `>= UtcNow - 7d`. Saat dilimi yok — sistem UTC kalır (06 §6.1).

5. **DI wiring:**
   - `TransactionsModule.cs` — `IAdminTransactionQueryService` registration.
   - Yeni `Skinora.API/Configuration/SteamModule.cs` — `IAdminSteamBotQueryService` registration. Sidecar adapter'ları T64–T69 bu modüle eklenecek.
   - `Program.cs` — `builder.Services.AddSteamModule()` + `IAdminDashboardService` scoped registration + yeni `using Skinora.API.Services` import.

6. **Controller değişiklikleri:**
   - `AdminController.cs` — AD1 (`GET dashboard`, `AdminAccess` policy), AD10 (`GET steam-accounts`, `Permission:VIEW_STEAM_ACCOUNTS`) eklendi. AD16b artık `_txQueries.ListForUserAsync` çağırıyor (T39 placeholder'dan refactor).
   - `AdminTransactionsController.cs` — AD6 (`GET /admin/transactions`) + AD7 (`GET /admin/transactions/:id`), her ikisi de `Permission:VIEW_TRANSACTIONS` policy. Mevcut POST endpoint'leri (T59 cancel/hold/release) değişmedi.

7. **AdminUserService refactor:**
   - `IAdminUserService.GetTransactionsAsync` ve impl kaldırıldı. Çağrı tek nokta (`AdminController`) doğrudan yeni query service'i kullanıyor; placeholder dead code temizlendi.
   - `AdminUserService.GetDetailAsync` stats alanları (`TotalTransactions`/`CompletedTransactions`/`CancelledTransactions`/`FlaggedTransactions`/`TotalVolume`/`LastTransactionAt`) `User.CompletedTransactionCount` denormalized counter dışında hâlâ placeholder — **K1** olarak forward-deferred (T63 acceptance kriterinde AD16 stats yok; T-future task wire eder).

8. **Doküman uyumu:** 07 §9 / 02 §16 spec'iyle birebir; doc değişikliği yok.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Transactions/Application/Admin/IAdminTransactionQueryService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Admin/AdminTransactionQueryDtos.cs`
- `backend/src/Skinora.API/Services/AdminTransactionQueryService.cs`
- `backend/src/Skinora.API/Services/IAdminDashboardService.cs`
- `backend/src/Skinora.API/Services/AdminDashboardService.cs`
- `backend/src/Skinora.API/Services/AdminDashboardDtos.cs`
- `backend/src/Skinora.API/Configuration/SteamModule.cs`
- `backend/src/Modules/Skinora.Steam/Application/Admin/IAdminSteamBotQueryService.cs`
- `backend/src/Modules/Skinora.Steam/Application/Admin/AdminSteamBotQueryService.cs`
- `backend/src/Modules/Skinora.Steam/Application/Admin/AdminSteamBotDtos.cs`
- `backend/tests/Skinora.API.Tests/Integration/AdminT63EndpointTests.cs`

**Değişiklik:**
- `backend/src/Skinora.API/Controllers/AdminController.cs` — AD1/AD10 metotları + AD16b refactor + 3 yeni dependency injection.
- `backend/src/Skinora.API/Controllers/AdminTransactionsController.cs` — AD6/AD7 metotları + IAdminTransactionQueryService injection.
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — `IAdminTransactionQueryService` registration.
- `backend/src/Skinora.API/Program.cs` — `using Skinora.API.Services` + `AddSteamModule()` + `IAdminDashboardService` registration.
- `backend/src/Modules/Skinora.Admin/Application/Users/IAdminUserService.cs` — `GetTransactionsAsync` kaldırıldı.
- `backend/src/Modules/Skinora.Admin/Application/Users/AdminUserService.cs` — `GetTransactionsAsync` impl kaldırıldı.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /admin/dashboard` → özet (aktif işlem, flag sayısı, Steam hesap durumu) | ✓ | `AdminController.GetDashboard` (`AdminController.cs`) → `IAdminDashboardService.GetAsync`. Integration: `Dashboard_PopulatedSystem_AggregatesCountersAndSurfacesData` (4 counter + steam list + 2 flag), `Dashboard_EmptySystem_ReturnsZeroCountersAndEmptyArrays`, `Dashboard_MoreThanFiveFlags_RecentFlagsCappedAtFiveNewestFirst` (top 5 desc), `Dashboard_AnyAdmin_Returns200_NoSpecificPermissionRequired` (AdminAccess policy), `Dashboard_RegularUser_Returns403`, `Dashboard_Anonymous_Returns401`. |
| 2 | `GET /admin/transactions` → tüm işlem listesi (paginated, filtrelenebilir) | ✓ | `AdminTransactionsController.List` (`AdminTransactionsController.cs`) — 11 query param + PagedResult wrapper. Integration: `ListTransactions_NoFilters_ReturnsPaginatedItemsNewestFirst`, `ListTransactions_StatusFilter_NarrowsResults`, `ListTransactions_SearchByItemName_FindsMatch`, `ListTransactions_AmountAndDateRange_FilterCorrectly`, `ListTransactions_AdminWithoutPermission_Returns403`, `ListTransactions_Anonymous_Returns401`. |
| 3 | `GET /admin/transactions/:id` → tam admin görünümü (status history, payment, payout, refund, notification, dispute, flag history) | ✓ | `AdminTransactionsController.GetDetail` → `IAdminTransactionQueryService.GetDetailAsync`. Integration: `GetTransactionDetail_FullySeeded_ReturnsAllSections` 8 alt-bölümü doğrular (statusHistory 2 kayıt + paymentDetail TxHash + notificationHistory 1 + disputeHistory 1 + flagHistory 1 + adminActions). `GetTransactionDetail_PendingFlag_AdminActionsAllowApproveAndReject`, `GetTransactionDetail_TerminalStateOrOnHold_CannotCancel` (canCancel mantığı), `GetTransactionDetail_UnknownId_Returns404`. |
| 4 | `GET /admin/audit-logs` → audit log listesi (paginated, filtrelenebilir) | ✓ | T42 zaten implement etti (`AdminController.ListAuditLogs`, `AdminAuditLogEndpointTests` 12/12 PASS). T63 doğrulama listesi için yer envanteri. |
| 5 | `GET /admin/users/:steamId/transactions` → kullanıcının işlem geçmişi | ✓ | `AdminController.GetUserTransactions` artık `IAdminTransactionQueryService.ListForUserAsync` çağırıyor (T39 placeholder'dan refactor). Integration: `UserTransactions_UserHasTransactions_ReturnsThemAsListItems` (2 tx döner — biri seller, biri buyer; üçüncü taraf tx hariç tutulur), mevcut T39 testleri (`GetUserTransactions_Existing_ReturnsEmptyPagedResult`, `GetUserTransactions_UnknownSteamId_Returns404`) regresyon temiz. |
| 6 | `GET /admin/steam-accounts` → Steam bot hesapları durumu | ✓ | `AdminController.GetSteamAccounts` → `IAdminSteamBotQueryService.ListAsync`. Integration: `SteamAccounts_OnlyActiveBots_WarningNull` + `SteamAccounts_NonActiveBot_WarningMessageNonNull` + `SteamAccounts_AdminWithoutPermission_Returns403` + `SteamAccounts_Anonymous_Returns401`. Sabit alanlar (`dailyTradeOfferLimit=200`, `failoverStatus="NONE"`, `recoveryTransactionCount=0`) doğrulandı. |

**Doğrulama kontrol listesi:**

- [x] **07 §9.1–§9.19 admin endpoint'leri eksiksiz mi?** ✓ — 19 endpoint tam (T39 §9.11–§9.18 + T41 §9.8–§9.9 + T42 §9.19 + T54 §9.2–§9.5 + T59 §9.20–§9.22 + T63 §9.1, §9.6–§9.7, §9.10). T63 4 yeni read endpoint ile setin son boşluklarını kapatır.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Integration (Skinora.API.Tests) | ✓ 328/328 | T63 yeni 21 test + T42/T39/T41/T54/T59 regresyon temiz. |
| Unit (Skinora.Transactions.Tests) | ✓ 577/577 | Regresyon temiz. |
| Unit + integration (Skinora.Admin.Tests) | ✓ 20/20 | AdminUserService refactor sonrası temiz. |
| Unit (Skinora.Auth.Tests) | ✓ 93/93 | Regresyon temiz. |
| Unit (Skinora.Disputes.Tests) | ✓ 36/36 | Regresyon temiz. |
| Unit (Skinora.Fraud.Tests) | ✓ 64/64 | Regresyon temiz. |
| Unit (Skinora.Notifications.Tests) | ✓ 93/93 | Regresyon temiz. |
| Unit (Skinora.Payments.Tests) | ✓ 6/6 | Regresyon temiz. |
| Unit (Skinora.Platform.Tests) | ✓ 144/144 | Regresyon temiz. |
| Unit (Skinora.Realtime.Tests) | ✓ 25/25 | Regresyon temiz. |
| Unit (Skinora.Shared.Tests) | ✓ 201/201 | Regresyon temiz. |
| Unit (Skinora.Steam.Tests) | ✓ 21/21 | Regresyon temiz. |
| Unit (Skinora.Users.Tests) | ✓ 16/16 | Regresyon temiz. |
| Build (Release, `-warnaserror`) | ✓ 0W/0E | Tüm sln. |
| Format verify | ✓ exit=0 | `dotnet format Skinora.sln --verify-no-changes`. |

**Lokal toplam:** **1624 pass, 0 fail.** Yeni T63 testleri SQLite in-memory ile lokalde tamamlandı; CI Linux runner'da SQL Server tabanlı integration job'ları ayrıca koşacak.

## Altyapı Değişiklikleri

- **Migration:** Yok — T63 yeni tablo/kolon eklemiyor; tüm sorgular mevcut entity'ler üzerinden.
- **SystemSetting:** Yok.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni dış bağımlılık:** Yok — tüm sorgular EF Core ile (zaten kullanılıyor).
- **Plan/spec değişikliği:** Yok.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyalarda secret yok; integration test JWT secret'ı test fixture'ı içinde sabit.
- **Auth/authorization:** Tüm endpoint'ler policy gate'li. AD1 → `AdminAccess` (admin/super_admin role); AD6/AD7 → `Permission:VIEW_TRANSACTIONS`; AD10 → `Permission:VIEW_STEAM_ACCOUNTS`; AD16b refactor → `Permission:VIEW_USERS` (T39'dan miras). 6 negatif auth testi (anon 401 + admin without permission 403) per endpoint.
- **Input validation:**
  - Page/PageSize clamp (1–100, default 20) hem `AdminTransactionQueryService` hem `AdminSteamBotQueryService` (sonuncu paging desteklemiyor) hem service'lerde tutarlı.
  - **LIKE search escape:** `%`/`_`/`[` bracket-wrapping ile escape (`[%]`/`[_]`/`[[]`) — pattern injection ve "all rows" wildcard sürprizleri engellenir. T39'un open advisory'si burada giderildi.
  - Tarih/sayı parametreleri ASP.NET model binder ile parse — invalid değer ⇒ 400 (out of T63 scope).
- **Yeni dış bağımlılık:** Yok.
- **Audit log:** AD6/AD7/AD10/AD1 read-only — audit yazımı yok (06 §4 audit kuralları write-aksiyonları için).

## Commit & PR

- Branch: `task/T63-admin-dashboard-api`
- Commit: `6e3b400`
- PR: [#100](https://github.com/turkerurganci/Skinora/pull/100)
- CI: izleniyor (run aşağıda kayıt edilecek post-watch)

## Known Limitations / Follow-up

- **K1 — `AdminUserService.GetDetailAsync` stats placeholder'ları.** AD16 (`GET /admin/users/:steamId`) cevabındaki `TotalTransactions`/`CompletedTransactions`/`CancelledTransactions`/`FlaggedTransactions`/`TotalVolume`/`LastTransactionAt` alanları hâlâ `User.CompletedTransactionCount` denormalized counter dışında null/0 dönüyor. T63 acceptance kriterleri AD16b'yi (history) wire eder, AD16 (stats) ise kapsamda değil. T-future task `IAdminTransactionQueryService` üzerine `ComputeUserStatsAsync` ekleyip AdminUserService'i refactor edebilir; servis interface açık.
- **K2 — AD7 `sellerPayoutDetail` gas fee splits forward-deferred (T57 / T73).** `gasFeeFromCommission` / `gasFeeFromSeller` her zaman 0 döner — gerçek bölünme T57 (gas fee management) + T73 (Tron sidecar transfer) wire'ında oluşur. `grossAmount`/`commission`/`gasFee`/`netAmount`/`txHash`/`sentAt` mevcut `BlockchainTransaction(SELLER_PAYOUT)` kaydından doğru okunur.
- **K3 — AD10 forward-deferred sidecar alanları (T69).** `failoverStatus = "NONE"`, `recoveryTransactionCount = 0`, `restrictionReason = null` her satırda sabit. Bot health pipeline + failover orchestration T64–T69 kapsamında; ilgili alanlar `PlatformSteamBot` entity'sine kolon olarak eklendiğinde DI swap gerektirmeden mevcut query genişler.
- **K4 — AD7 `notificationHistory` retry/error durumu yansıtılmıyor.** Yalnız `DeliveryStatus.SENT` rows external channel olarak listelenir. PENDING/FAILED satırlar (retry, kalıcı başarısızlık) admin için görünür değil; gözlem T78–T80 (Email/Telegram/Discord) wire'ı sonrası anlamlı (mevcut T37 stub'lar hep SENT yazıyor). Future enhancement: AD7'a `notificationDeliveryFailures` accordion bölümü.
- **K5 — Active transaction sayacı `IsOnHold` status'ünü "active" sayar.** EMERGENCY_HOLD durumundaki tx kullanıcı tarafından donmuş ama "kapanmamış" olduğu için aktif sayılır. UI'da emergency-hold kuyruğunu ayrı kart olarak göstermek için T-future enhancement (07 §9.21/§9.22 hold lifecycle). Spec (07 §9.1) sayaç tanımını detaylandırmıyor; pratik admin beklentisi karşılanır.
- **K6 — AD6 search bracket-escape pattern T39'un advisory'sini kapatır.** Tüm admin LIKE-tabanlı search'lerde aynı escape kullanılması için T-future bir shared helper (`Skinora.Shared.Persistence.LikeEscape.Bracket`) çıkarılabilir; şu an T39 (`AdminUserService`) hâlâ raw `EF.Functions.Like("%term%")` kullanıyor (kullanıcı arama), T63 yalnız Transaction LIKE search'ini escape ediyor. Üretimde işlevsel impact düşük — admin search input'unu trusted operator yazıyor, ama defense-in-depth için future task çıkarılabilir.

## Notlar

- **Working tree (Adım -1):** temiz (session başında `git status` boş).
- **Main CI startup (Adım 0):** son 3 main run hepsi `success` — `25513910803`, `25513910844`, `25476326367` (T62 + T61 PR'ları).
- **Dış varsayım:** yok — tüm bağımlılıklar (T19 Transaction, T40 RBAC, T42 AuditLog, T54 Flag, T58 Dispute, T59 Admin tx) merged ve operasyonel.
- **Validator chat'inde doğrulanacak:** ayrı chat'te bağımsız validator (test çalıştırma + kabul kriteri kanıtı + spec uyum + güvenlik kontrolü).
