# T83a — Kullanıcı işlem listesi endpoint'i (T1)

**Faz:** F4 (retro kurtarma) | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-21

---

## Yapılan İşler

- `GET /api/v1/transactions` endpoint'i (T1, 07 §7.1) backend'de implement edildi — F4 retro kurtarma. T45 doc-ref'i §7.1–§7.4 yazıyor ama kabul kriterleri §7.2–§7.4'ü implement etmiş; §7.1 (T1 list) hiçbir F0–F4 task'ında üretilmemişti. T88 BLOCKED bulgusunun PLAN_CORRECTION_REQUIRED düzeltmesi.
- Yeni `Skinora.Transactions/Application/Lifecycle/` paketi: `ITransactionListService` (port) + `TransactionListService` (implementation) + `TransactionListDtos.cs` (4 DTO + 1 enum + 1 query record).
- `TransactionsController` — `[HttpGet("")]` action eklendi (Authenticated + RateLimit("user-read")), `ParseTab` string→enum normaliser (`null`/whitespace/bilinmeyen → Active default).
- DI: `TransactionsModule.AddTransactionsModule` — `ITransactionListService` Scoped kayıt (T46 detail/accept satırının altına).
- Tab → status mapping: `active` = 8 status (CREATED..ITEM_DELIVERED + FLAGGED), `completed` = COMPLETED, `cancelled` = 4 CANCELLED_* (07 §7.1 tablosu birebir).
- EMERGENCY_HOLD projection: `IsOnHold=true` → response `status: "EMERGENCY_HOLD"` (computed string, 07 §7.1 nota uygun). TransactionStatus enum'a yeni değer eklenmedi.
- activeTimeout resolver: 06 §3.5 state→active-deadline matrix; `TimeoutFreezeService.GetActiveDeadline` ve `TransactionDetailService.BuildTimeout` ile birebir uyumlu. Frozen rows persisted remainder kullanır; live rows `(deadline − now)` hesabı, negatifler 0'a clamp.
- Pagination: `page` (default 1, min 1), `pageSize` (default 20, clamp 1–100). Standard `Skinora.Shared.Models.PagedResult<T>` envelope.
- Order: `CreatedAt DESC` + `Id` tie-breaker (deterministik).
- Counterparty resolver: tek query'lik dictionary join (`AdminTransactionQueryService.ListAsync` paterni), `BuyerId` null ise `null`.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionListService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionListService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionListDtos.cs`
- `backend/tests/Skinora.Transactions.Tests/Unit/Lifecycle/TransactionListServiceTests.cs` (31 unit test, SQLite in-memory)
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionListServiceTests.cs` (5 integration test, SQL Server)

**Değişen:**
- `backend/src/Skinora.API/Controllers/TransactionsController.cs` — List endpoint + ParseTab helper + ITransactionListService DI
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — `ITransactionListService` DI kaydı (1 satır)
- `backend/tests/Skinora.API.Tests/Integration/TransactionLifecycleEndpointTests.cs` — 5 yeni endpoint smoke test + `SeedTransactionAsync` CK_Transactions_Cancel guard ekleme

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | GET /api/v1/transactions endpoint'i, Authenticated, RateLimit("user-read") | ✓ | `TransactionsController.List` `[HttpGet("")]` + `[Authorize(Policy = AuthPolicies.Authenticated)]` + `[RateLimit("user-read")]`; `List_Unauthenticated_Returns_401` (endpoint test) PASS |
| 2 | Query param: tab ∈ {active, completed, cancelled} (zorunlu; tanımsızsa varsayılan active) | ✓ | `ParseTab(string?)` — null/whitespace/bilinmeyen → `TransactionListTab.Active`; `List_Default_Tab_Is_Active_When_Query_Param_Omitted` PASS |
| 3 | Tab → status mapping (07 §7.1) | ✓ | `ResolveStatusFilter` — active = 8 status (FLAGGED dahil), completed = COMPLETED, cancelled = 4 CANCELLED_*; `Active_Tab_Returns_Only_Active_Statuses` + `Completed_Tab_Returns_Only_Completed` + `Cancelled_Tab_Returns_All_Cancelled_Variants` PASS |
| 4 | Yalnız çağıranın taraf olduğu işlemler (SellerId/BuyerId = caller) | ✓ | Service `t.SellerId == callerId \|\| t.BuyerId == callerId` + `Excludes_Transactions_Where_Caller_Is_Not_A_Party` + `Includes_Transactions_Where_Caller_Is_Buyer` + endpoint test `List_Excludes_Other_Users_Transactions` PASS |
| 5 | Response satırı tüm alanlar (id, itemName, itemImageUrl, status EMERGENCY_HOLD projection, price, stablecoin, counterparty\|null, userRole, activeTimeout\|null, createdAt) | ✓ | `TransactionListItemDto` 10 alan + `ProjectStatus` IsOnHold projection + DTO serialization unit test (`Price_Serialized_As_String_With_Two_Decimals` + WhenWritingNull suppress) PASS |
| 6 | activeTimeout 06 §3.5 state→active deadline matrix; aktif olmayan tab'larda null | ✓ | `BuildActiveTimeout` 6 phase mapping + frozen branch + clamp; `ActiveTimeout_Maps_Phase_Per_Status` [Theory 6] + `ActiveTimeout_Is_Null_For_Item_Delivered_And_Flagged` + `ActiveTimeout_Is_Null_For_Completed_And_Cancelled` + `ActiveTimeout_Frozen_Uses_TimeoutRemainingSeconds` + `ActiveTimeout_RemainingSeconds_Clamped_To_Zero_When_Deadline_Past` PASS |
| 7 | Pagination: standart PagedResult<T> envelope (page, pageSize, total, items) | ✓ | Return type `PagedResult<TransactionListItemDto>` (Skinora.Shared.Models); `List_Authenticated_Returns_PagedResult_Envelope` PASS; clamping `Pagination_Inputs_Are_Clamped_To_Safe_Range` [InlineData 5] + `Pagination_Returns_Distinct_Pages_Across_Calls` PASS |
| 8 | Order: createdAt DESC (en yeni en üstte) | ✓ | `OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)`; `Items_Ordered_By_CreatedAt_Descending` PASS |

## Doğrulama Kontrol Listesi

- ✓ 07 §7.1 sözleşmesi (query param, tab→status, response shape, EMERGENCY_HOLD projection) — implement edildi, DTO + service + endpoint testleriyle doğrulandı
- ✓ Authenticated guard + party filter (yalnız caller'ın taraf olduğu işlemler) — controller `[Authorize]` + service WHERE clause; `List_Unauthenticated_Returns_401` + `Excludes_Transactions_Where_Caller_Is_Not_A_Party` PASS
- ✓ activeTimeout 06 §3.5 deadline matrix ile uyumlu — `BuildActiveTimeout` mirrors `TimeoutFreezeService.GetActiveDeadline` 6 phase

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Skinora.Transactions.Tests) | ✓ 417/417 PASS (Unit only) | `dotnet test --filter "FullyQualifiedName!~Integration"` (T83a yeni: 31) |
| Unit (Skinora.Shared.Tests) | ✓ 357/357 PASS | Regresyon yok |
| HTTP smoke (Skinora.API.Tests, SQLite-backed) | ✓ TransactionLifecycleEndpointTests 25/25 PASS | T45/T46/T51/T60 mevcut 20 + T83a yeni 5 |
| Integration (Skinora.Transactions.Tests SQL Server) | ⏳ CI'ye devir | 5 yeni TransactionListServiceTests; lokal Windows Docker Desktop yokluğu (F4 envelope, T78/T82 paterni) — CI Linux runner `INTEGRATION_TEST_SQL_SERVER` (T11.3) ile koşar |
| dotnet format --verify-no-changes | ✓ Δ=0 | `dotnet format backend/Skinora.sln --verify-no-changes --severity warn` exit 0 |
| Build Release | ✓ 0W/0E | `dotnet build src/Skinora.API -c Release` 0 warning, 0 error |

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** yok — kod hiçbir secret içermez.
- **Auth/authorization:** endpoint `Authorize(Policy = AuthPolicies.Authenticated)` ile korunmuş; service `t.SellerId == callerId \|\| t.BuyerId == callerId` party filter ile başka kullanıcıların transaction'larını sızdırmaz; `userRole` server-side resolve (client manipülasyonu mümkün değil).
- **Input validation:** `tab` query param `ParseTab` ile whitelisted set (`active`/`completed`/`cancelled`); bilinmeyen değer → Active default (fail-safe); `page`/`pageSize` int — `ClampPaging` ile 1–100 / min 1 enforce.
- **Yeni dış bağımlılık:** yok.

## Altyapı Değişiklikleri

- Migration: **Yok** — `Transaction` entity'sinin tüm okuduğumuz alanları (deadline'lar, IsOnHold, TimeoutFrozen* trio, SellerId/BuyerId, ItemName, ItemIconUrl, Price, StablecoinType, CreatedAt) T19/T44'ten beri mevcut.
- Config/env değişikliği: **Yok**.
- Docker değişikliği: **Yok**.
- SystemSetting: **Yok** — `DefaultTimeoutWarningPercent = 75` private const (TransactionDetailService'le aynı pattern). SystemSetting-backed reader T-future.

## Dış Varsayımlar (Ön-uçuş)

- **Plan tier/feature:** yok — backend-only SQL query + endpoint.
- **Paket sürüm:** yok — yeni NuGet eklenmedi.
- **Platform/OS:** SQLite in-memory (unit) + SQL Server (integration) — her ikisi de mevcut altyapıda kanıtlanmış pattern.
- **API/sözleşme:** 07 §7.1 stabil (12 doc tamamlanmış, F4 Gate Check ✓ sealed).
- **Repo/ortam:** main CI son 3 run ✓ (26186153921, 26186153991, 26181175543); working tree temiz.

## Commit & PR

- Branch: `task/T83a-user-transaction-list-endpoint`
- Commit: `f3699a1` (kod) + `b1b91a1` (PR ref reflection)
- PR: [#133](https://github.com/turkerurganci/Skinora/pull/133)
- CI: ✓ SUCCESS — run [`26243629346`](https://github.com/turkerurganci/Skinora/actions/runs/26243629346) 10/10 (initial koşum integration testlerinde xUnit parallel test-class scheduling race condition'ı tetikledi → `Skinora.Fraud.Tests.Integration` 16 test `"Cannot create a DbSet for 'SystemSetting'/'AuditLog'"` ile FAIL — pre-existing latent race, T83a kod değişikliklerinden bağımsız: backend kodu T83 merge (3e71172) ile identical, T84-T87 path-filter ile Integration job'unu atlamış; `gh run rerun --failed` ile aynı SHA üzerinde re-run → 10/10 SUCCESS. T84_REPORT `gh run rerun --failed` audit trail paterni). Önceki koşum [`26243390920`](https://github.com/turkerurganci/Skinora/actions/runs/26243390920) push concurrency ile cancel oldu (`b1b91a1` doc push'u higher priority).

## Known Limitations / Follow-up

- **K1 — Integration tests SQL Server bağımlı:** Lokal Windows Docker Desktop yokluğunda çalıştırılamadı; CI Linux runner `INTEGRATION_TEST_SQL_SERVER` (T11.3 shared mssql) ile koşar. Unit + endpoint smoke (SQLite) lokal PASS.
- **K2 — `DefaultTimeoutWarningPercent` private const (75):** `TransactionDetailService` ile birebir aynı pattern. SystemSetting-backed reader hem T46 hem T83a için T-future refactor adayı (tek noktada değişebilir).
- **K3 — Active tab FLAGGED dahil:** Plan §T83a "active = CREATED..ITEM_DELIVERED + FLAGGED" diyor; satıcı price-deviation rows pending admin review'ı kendi dashboard'unda görebilsin (07 §7.1 normatif). EMERGENCY_HOLD ayrıca projection ile aktif tab'ta görünür (IsOnHold=true her zaman aktif state üzerine overlay).
- **K4 — Counterparty `null` durumları:** OPEN_LINK pre-acceptance ve seller-side CREATED rows (BuyerId null) — DTO `WhenWritingNull` ile field tamamen suppress edilir, frontend kontrolü `if (item.counterparty)` paterniyle yapacak (S05 dashboard T88).
- **K5 — Ordering tie-breaker `Id ASC`:** Aynı milisaniyede iki tx oluşursa Id ASC ile deterministik; CreatedAt scale 7 (datetime2) milli/mikro precision SQL Server'da kazanır, tie nadir.
- **K6 — Pagination cap 100:** AdminTransactionQueryService ile aynı clamp; UI tipik sayfa 20, max 100 hard limit DoS koruması.
- **K7 — Skinora.Fraud.Tests parallel race condition (pre-existing):** İlk CI koşumunda 16 Fraud Integration testi `"Cannot create a DbSet for 'SystemSetting'/'AuditLog'"` ile FAIL verdi. Root cause: `Skinora.Fraud.Tests/Integration/` altındaki 4 sınıf (`AccountFlagCheckerTests`, `FraudFlagAdminQueryServiceTests`, `FraudFlagEntityTests`, `PriceServiceTests`) static ctor'da `PlatformModuleDbRegistration.RegisterPlatformModule()` çağırmıyor; xUnit'in test class load sırası random olduğundan bu sınıflardan biri ilk `EnsureCreatedAsync` ile EF Core model'i SystemSetting/AuditLog olmadan freeze edebiliyor → `FraudFlagServiceTests`/`MultiAccountDetectorTests` (Platform registration'lı) çalıştığında model zaten cached. Re-run ile farklı load order → temiz pass. Pre-existing latent bug: T83 merge (`3e71172`) ile backend kod identical, T84-T87 frontend PR'ları path-filter ile Integration job'unu skip ettiği için manifest olmamıştı. T83a kod değişikliklerinden bağımsız. Kalıcı çözüm T-future chore PR: 4 sınıfa Platform registration ekleme — T83a scope dışı.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (bağımsız validator, 2026-05-21) |
| Bulgu sayısı | 0 S-bulgu; 1 advisory (T46 BuildTimeout matrix subset — T83a scope dışı, validator-side gözlem) |
| Düzeltme gerekli mi | Hayır |
| Kabul kriteri sonuç | 8/8 ✓ (07 §7.1 normatif tablosundan türetilen 12 atomik kontrol kanıtlandı) |
| Doğrulama kontrol listesi | 3/3 ✓ |
| Validator Adım -1 (working tree) | ✓ temiz |
| Validator Adım 0 (main CI startup) | ✓ 3/3 success — `26186153921`, `26186153991`, `26181175543` |
| Validator Adım 0b (memory drift) | ✓ T83a satırı `MEMORY.md:209`'da mevcut |
| Validator Adım 7 (testler) | Unit (Lifecycle.TransactionListServiceTests) 31/31 ✓ + Unit (Transactions full Unit folder) 417/417 ✓ + API endpoint (TransactionLifecycleEndpointTests) 25/25 ✓ + dotnet format Δ=0 + build Release 0W/0E (27.99s); integration CI-devir (lokal Docker yokluğu, K1) |
| Validator Adım 8a (task branch CI) | ✓ run [`26244846522`](https://github.com/turkerurganci/Skinora/actions/runs/26244846522) HEAD `fe41c26` 10/10 SUCCESS (Detect+Lint+Build+Unit+Integration+Contract+Migration+Docker backend+CI Gate; Guard skipped PR) + önceki [`26243629346`](https://github.com/turkerurganci/Skinora/actions/runs/26243629346) `b1b91a1` 10/10 ✓ |
| Validator Adım 9 (mini güvenlik) | ✓ temiz — secret yok, Authenticated+RateLimit("user-read"), party filter IDOR'u kapatır, tab whitelisted parse + page/pageSize clamp (DoS), yeni dış dep yok |
| Validator Adım 10 (doc uyumu) | ✓ 07 §7.1 endpoint/tab/status/response shape birebir; 06 §3.5 EMERGENCY_HOLD invariant (overlay ≠ state) korunur; 06 §3.5 state→deadline matrix 6/6 phase (CREATED/ACCEPTED/TRADE_OFFER_SENT_TO_SELLER/ITEM_ESCROWED/PAYMENT_RECEIVED/TRADE_OFFER_SENT_TO_BUYER) mapping `TimeoutFreezeService.GetActiveDeadline` canonical mirror |
| Yapım raporu uyumu | Tam — 8 kabul kriteri + 7 K1-K7 + test sonuçları + mini güvenlik + altyapı validator bağımsız bulgularıyla 1:1 |

**Validator-side advisory (FAIL değil, gözlem):**
- **A1 — T46 BuildTimeout matrix subset:** `TransactionDetailService.BuildTimeout` ACCEPTED + PAYMENT_RECEIVED state'lerini içermiyor; T83a 06 §3.5 normatif matrise ve `TimeoutFreezeService.GetActiveDeadline` canonical reference'a daha sadık (6/6 phase). T83a doğru pozisyonda; T46 ayrı bir incelemeye konu olabilir. T83a scope dışı.

## Notlar

- **Working tree (Adım -1):** temiz ✓.
- **Main CI startup (Adım 0):** son 3 run ✓ — 26186153921, 26186153991, 26181175543.
- **Bağımlılık kontrolü:** T44 ✓ (Transaction State Machine, F3), T19 ✓ (Transaction entity, F1).
- **Branch oluşturma:** T88 branch'i `task/T88-dashboard` BLOCKED raporuyla durur (commit `dee3a4d` + `b5e27ab`); T83a `task/T83a-user-transaction-list-endpoint` main HEAD'inden açıldı.
- **Scope onayı:** Proje sahibi AskUserQuestion ile 3 karar onayladı (branch=main'den, pagination=20 clamp 1-100, status=string projection). T88 BLOCKED rapordaki Seçenek 1 (yeni backend task) `2026-05-21` tarihli proje sahibi kararına uygun.
- **Mimari karar (notlar):**
  - `TransactionListService` Transactions modülünde — Admin counterpart `AdminTransactionQueryService` API katmanında çünkü Notifications/Disputes/Fraud cross-module bağımlılığı vardı; T83a yalnız Users dictionary join kullanıyor, modül içinde temiz.
  - `ProjectStatus` saf string projection — EMERGENCY_HOLD'u TransactionStatus enum'a eklemekten kaçınıldı (06 §3.5 invariant: EMERGENCY_HOLD ≠ state, overlay).
  - `price` formatı `ToString("F2", InvariantCulture)` — `TransactionDetailService` ile aynı pattern; locale-aware decimal separator riski (TR `,` vs US `.`) defansif olarak elimine.
  - Counterparty dictionary join paterni `AdminTransactionQueryService.ListAsync` ile birebir — N+1 query'den kaçınma + plan cache stability.
- **Yapım sonrası kapatılan minor gözlemler:** Yok.
- **Faz konumu:** F4 retro kurtarma — F4 Gate Check (`phase/F4-pass`, `3e71172`) sonrası eklenen task; F5 (T84+) bağımsız ilerleyebilir, T88 PASS sonrası unblock olur.
