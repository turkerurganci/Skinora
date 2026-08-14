# T123 — SELLER_CONFIRMED + POST /transactions/:id/confirm-ready

**Faz:** F7 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-13 (doğrulama 2026-08-14)

---

## Yapılan İşler

**Yeni uç: `POST /transactions/:id/confirm-ready` (07 §7.6a, 03 §2.3).** Satıcının "göndermeye hazırım" adımı. Üç kapıyı geçince `ACCEPTED → SELLER_CONFIRMED` geçişi yapılır, ödeme penceresi armlanır ve ödeme adresi alıcıya açılır.

- `TransactionReadinessService` + `ITransactionReadinessService` — üç kapı sırayla: (1) item hâlâ satıcının envanterinde ve tradeable mı, (2) alıcının Mobile Authenticator'ı hâlâ aktif mi, (3) alıcının envanterinden teslimat baseline'ı. İlk ikisi **bloklayıcı**, üçüncüsü **değil**.
- Geçiş + baseline + `PaymentDeadline` + Hangfire job ID'leri + history satırı + outbox mesajı **tek `SaveChanges`** içinde (09 §13.3 atomiklik sözleşmesi).
- `TransactionStatusChangedEvent(ACCEPTED → SELLER_CONFIRMED)` yayınlanıyor — WP19'un `EscrowedAndTradeOfferNotificationConsumer`'ı T117'den beri **üreticisiz** bekliyordu; alıcının `PAYMENT_WINDOW_OPEN` bildirimi bu event'e biniyor.
- `SchedulePaymentTimeoutAsync`'in **ilk üretim çağıranı** — ödeme penceresini armlayan custodial bacak T117'de silinmişti.

**Envanter portu genişletildi (AC: "envanter önbelleksiz okunur").**
- `InventoryReadFreshness { Cached, Fresh }` enum'u porta eklendi; `Fresh` → sidecar'a `?refresh=true`. Bayrak sidecar'da T120'den beri vardı ama **backend hiç göndermiyordu** — her backend okuması 120 sn'lik cache'ten geliyordu.
- `ISteamInventoryReader.CaptureClassBaselineAsync` — 06 §3.5 baseline'ı: `(ClassId, InstanceId)` çifti için **sayım** + asset ID listesi. Varlık kontrolü değil sayım, çünkü 02 §9.2 sayım kuralıdır (T122: bir envanterde tek sınıfın 9 kopyası ölçüldü).
- Parametre opsiyonel yapılmadı: mevcut iki üretim çağıranı (`TransactionCreationService`, `WrongItemDisputeAutoChecker`) derleme hatasıyla bilinçli seçim yapmaya zorlandı; ikisi de gerekçesiyle `Cached` bıraktı.

**Ödeme adresi ifşası (AC4).** `TransactionDetailService`'te sabit `Payment: null` kaldırıldı. Blok artık `SellerReadyConfirmedAt` damgası varsa **ve** çağıran taraflardan biriyse dönüyor.

**SystemSetting yeniden adlandırma (AC5 — proje sahibi kararı: seçenek a).**
`trade_offer_seller_timeout_minutes` → `seller_confirm_timeout_minutes`, `trade_offer_buyer_timeout_minutes` → `delivery_timeout_minutes`.

**Plan boşluğu kapatıldı: `SellerConfirmDeadline` armlanıyor.** Kabul geçişinde (`TransactionAcceptanceService`) yazılıyor.

## Etkilenen Modüller / Dosyalar

**Yeni**
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionReadinessService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionReadinessService.cs`
- `backend/src/Skinora.Shared/Persistence/Migrations/20260813192334_T123_RenameTimeoutSettings.cs` (+ Designer; `AppDbContextModelSnapshot.cs` yeniden üretildi)
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionReadinessServiceTests.cs`

**Değişen — üretim**
- `Skinora.Transactions`: `TransactionAcceptanceService` (deadline arm + ayar okuma), `TransactionCreationService` (freshness), `TransactionDetailService` (payment bloğu), `TransactionErrorCodes` (2 kod), `TransactionLifecycleDtos` (ConfirmReady DTO/outcome/enum), `Application/Steam/ISteamInventoryReader` + `StubSteamInventoryReader`
- `Skinora.Steam`: `ISteamSidecarInventoryClient`, `HttpSteamSidecarInventoryClient` (`?refresh=true`), `SidecarSteamInventoryReader` (baseline + freshness), `SteamInventoryQueryService`
- `Skinora.Disputes`: `WrongItemDisputeAutoChecker` (freshness = Cached, gerekçeli)
- `Skinora.Platform`: `SystemSettingSeed`, `SystemSettingsCatalog`
- `Skinora.API`: `TransactionsController` (uç + status eşlemesi), `Configuration/TransactionsModule` (DI)

**Değişen — konfig / FE / doküman**
- `.env.example`, `docker-compose.yml`, `docker-compose.e2e.yml` (env var adları)
- `frontend/src/i18n/messages/{en,tr,es,zh}.json` (ayar etiketleri)
- `Docs/07_API_DESIGN.md` v3.1→**v3.2**, `Docs/06_DATA_MODEL.md` v6.3→**v6.4**, `Docs/04_UI_SPECS.md` v4.1→**v4.2**, `Docs/11_IMPLEMENTATION_PLAN.md` v0.6→**v0.7**, `Docs/DEPLOY_RUNBOOK.md`

**Değişen — test double'ları (port imzası değişimi)**
`TestSetupHelpers`, `TransactionAcceptanceServiceTests`, `TransactionDetailServiceTests`, `TransactionLifecycleEndpointTests`, `DisputesEndpointTests`, `PayoutIssueEndpointTests`, `DisputeServiceTests`, `SettingsBootstrapTests`, `HttpSteamSidecarInventoryClientTests`, `SidecarSteamInventoryReaderTests`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Item envanterden çıkmış satıcı `ITEM_NO_LONGER_AVAILABLE` alıyor | ✓ | `Item_Gone_From_Inventory_Returns_ItemNoLongerAvailable`, `Item_No_Longer_Tradeable_Returns_ItemNoLongerAvailable`, HTTP: `ConfirmReady_Item_Gone_Returns_409_ItemNoLongerAvailable`. Üç değerli ayrım ayrıca sabitlendi: `Seller_Inventory_Private_Is_Not_Reported_As_Item_Gone`, `The_Three_Unreadable_Outcomes_Map_To_Three_Distinct_Codes` |
| 2 | Alıcı MA kontrolü yapılıyor | ✓ | `Buyer_Mobile_Authenticator_Inactive_Returns_Its_Own_Code`, `Trade_Hold_Probe_Unavailable_Fails_Closed`, `Trade_Hold_Probe_Uses_The_Buyers_Own_Stored_Trade_Url_Token` (probe alıcının SteamID'si + kabul anında sabitlenen URL'in token'ı ile çağrılıyor) |
| 3 | Baseline yazılıyor; alıcı envanteri gizliyse işlem bloklanmıyor | ✓ | `Baseline_Counts_Existing_Copies_Of_The_Item_Class` (2 kopya → count 2 + asset listesi), `Baseline_Of_Zero_Is_Recorded_As_Evidence_Not_As_Absence`, `Unreadable_Buyer_Inventory_Does_Not_Block_The_Transaction(Private/Unavailable)` → 200 + `buyerInventoryVisible: false` + üç kolon NULL |
| 4 | Ödeme adresi ancak bu adımdan sonra ifşa ediliyor | ✓ | `Payment_Block_Is_Hidden_Before_The_Seller_Confirms_Readiness(CREATED/ACCEPTED)`, `Payment_Block_Is_Disclosed_Once_The_Seller_Has_Confirmed_Readiness`, `Payment_Block_Is_Never_Shown_To_A_Public_Viewer`, `Payment_Block_Stays_Hidden_On_A_Cancellation_From_Before_The_Window`, uçtan uca: `Payment_Address_Is_Only_Disclosed_After_ConfirmReady` |
| 5 | SellerConfirmDeadline'ı besleyen SystemSetting'e karar verildi; açıklama/etiket v3.0 fazını anlatıyor | ✓ | Karar (a) uygulandı — migration `T123_RenameTimeoutSettings` (2 × `UpdateData`), seed açıklamaları, `SystemSettingsCatalog` etiketleri, 4 dil i18n, 06 §8 + 04 §16 + DEPLOY_RUNBOOK §A. Karar 11 §P3'e **kabul kriteri olarak** yazıldı (T122 dersi) |

**Ek (AC5'in gerçekten tüketilmesi için — plan boşluğu, proje sahibi onaylı):** `SellerConfirmDeadline` armlanıyor — `Accept_Arms_The_SellerConfirmDeadline_From_The_SystemSetting`, `Accept_Falls_Back_To_The_Documented_Default_For_An_Unusable_Setting(null/0/-5/garbage)`.

**Ek (AC1–AC3'ün önkoşulu):** önbelleksiz okuma — `Item_Check_And_Baseline_Both_Bypass_The_Sidecar_Cache`, `Freshness_Is_Threaded_Down_To_The_Sidecar_Cache_Flag`, `GetInventoryAsync_Sends_Refresh_True_When_Cache_Is_Bypassed`, `GetInventoryAsync_Omits_The_Refresh_Flag_On_The_Cached_Path`.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 error / 0 warning | `dotnet build Skinora.sln -c Debug` |
| Format (CI "1. Lint" paritesi) | ✓ temiz | `dotnet format Skinora.sln --verify-no-changes --severity error` |
| **Tam backend suite** | **✓ 2553/2553** *(doğrulama turunda düzeltildi — yapım turu 2525/11 proje yazmıştı, `Users.Tests` 22 + `Payments.Tests` 6 enumerasyona girmemişti; ikisi de 0 fail)* | 13 assembly: Shared 399 · Steam 39 · Platform 189 · Auth 120 · Admin 22 · Users 22 · Payments 6 · Realtime 40 · Fraud 91 · Disputes 60 · Notifications 171 · Transactions 862 · API 532 |
| FE | ✓ | `npx tsc --noEmit` 0 · `npm run lint` 0 · `npx prettier --check src/i18n/messages/*.json` temiz · `npm run test` **33/33** |
| FE i18n parity | ✓ | `npm run i18n:check` → 4 locale × 1300 anahtar, aynı anahtar kümesi; 15 advisory uyarı **önceden vardı** (hepsi "Gas fee"/"Mobile Authenticator" untranslatable, dokunulan anahtarlarla ilgisiz) |
| Migration rehearsal | CI'ye bırakıldı | "6. Migration dry-run" job'ı |

**Yeni test sayısı: 50 metot.** `TransactionReadinessServiceTests` 24 metot (Theory genişlemesiyle **28 vaka**) · `TransactionLifecycleEndpointTests` +11 (HTTP statü eşlemesi + uçtan uca adres ifşası) · `TransactionDetailServiceTests` +6 (payment bloğu kapısı) · `SidecarSteamInventoryReaderTests` +5 (freshness + baseline) · `TransactionAcceptanceServiceTests` +2 (deadline arm + fallback) · `HttpSteamSidecarInventoryClientTests` +2 (`refresh` bayrağı wire üzerinde).

> **Ölçüm notu — tam suite iki kez koşuldu.** İlk koşu (`dotnet test Skinora.sln`, 11 assembly paralel) Notifications'ta 41, sonraki koşuda 3 kırılma verdi; **aynı proje izole koşuda iki kez 171/171** geçti. Sebep tek bir SQL Server'a 11 assembly'nin eşzamanlı yüklenmesi (`IntegrationTestBase` test **başına** DB create/drop yapıyor), değişikliklerle ilgisi yok. Yukarıdaki 2525 rakamı **projeler sırayla** koşularak alınmıştır. Son iki proje (Transactions + API), yalnız XML-doc içeren son düzenlemeden **sonra** yeniden derlenip tekrar koşuldu — yani tablo commit edilen ağacın ölçümüdür.

> **Yol boyunca bulunan iki gerçek kusur (ikisi de düzeltildi, ikisi de yalnız tam koşuda görünürdü):** (1) `TransactionLifecycleEndpointTests.Reset()` `PaymentAddresses` satırlarını temizlemiyordu — `PaymentAddress` FK'si `Transaction`'a NO ACTION olduğu için yeni testin bıraktığı satır **sonraki** testin `Reset()`'inde `FOREIGN KEY constraint failed` üretiyor ve sınıfın **26 testini birden** (çoğu T123 dışı) düşürüyordu; `IgnoreQueryFilters().ExecuteDelete()` ile kapatıldı (`ISoftDeletable` olduğu için `RemoveRange` yalnız `IsDeleted` damgalar, FK yerinde kalırdı). (2) `dotnet format` ihlalleri — CI'nin "1. Lint" adımı PR'ı kırardı, açılmadan yakalandı.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **✓ PASS** (2026-08-14, ayrı chat, yapım raporu görülmeden) |
| Bulgu sayısı | **2 — ikisi de minör, bloke edici değil** (biri düzeltildi, biri follow-up) |
| Düzeltme gerekli mi | Hayır (merge'i bloklamıyor) |

### Validator ön kapıları

| Kapı | Sonuç |
|---|---|
| Adım -1 working tree | ✓ temiz (`git status --short` boş) |
| Adım 0 main CI (son 3 run) | ✓ `31733188607` · `31733188616` · `31524132478` — üçü de `success` |
| Adım 0b repo memory drift | ✓ `.claude/memory/MEMORY.md:37` T123 satırı mevcut |
| Adım 8a task branch CI | ✓ HEAD `daef4ab` → run [`31744444820`](https://github.com/turkerurganci/Skinora/actions/runs/31744444820) **CI Gate `success`**; önceki run `31743413400` da success |

### Validator bağımsız kanıtı

| Kriter | Validator sonucu | Bağımsız kanıt (yapım raporuna bakılmadan üretildi) |
|---|---|---|
| 1 — `ITEM_NO_LONGER_AVAILABLE` | ✓ | `TransactionReadinessService.cs:134-144` — yalnız `Visibility` switch'i `Private`/`Unavailable`'ı eledikten sonra "item yok"/"tradeable değil" üretiliyor, yani 409 gerçekten bir **okuma kanıtına** dayanıyor. Controller eşlemesi `TransactionsController.cs` → `Conflict` |
| 2 — Alıcı MA kontrolü | ✓ | `TransactionReadinessService.cs:161-186` — token kabul anında normalize edilip saklanan `BuyerTradeUrl`'den ayrıştırılıyor (07 §7.6a'da gövde yok, kaynak doğru); `hold.Available == false` → 503 fail-closed, `hold.Active == false` → 403 ayrı kod. `ITradeHoldChecker` sözleşmesi accept ucuyla birebir aynı |
| 3 — Baseline + gizli envanter bloklamıyor | ✓ | `TransactionReadinessService.cs:198-223` — baseline **Stage 6**'da, üç kapının sonuncusu ve `return` yok; `Visibility != Public` yalnız `LogInformation` üretiyor ve akış geçişe devam ediyor |
| 4 — Ödeme adresi ifşası | ✓ | `TransactionDetailService.cs:590-635` — kapı `HasReachedPaymentWindow` = `SellerReadyConfirmedAt.HasValue`, status kümesi değil; `role is null` → blok yok. Bağımsız leak taraması: `PaymentAddress` okuyan diğer yollar admin (`AdminTransactionQueryService`) ve iç servisler; kullanıcıya dönen başka yüzey yok. `PAYMENT_WINDOW_OPEN` bildirimi `EscrowedAndTradeOfferNotificationConsumer.cs:57` ile yalnız `SELLER_CONFIRMED`/`PAYMENT_RECEIVED`'da tetikleniyor |
| 5 — Önbelleksiz okuma | ✓ | Her iki çağrı `InventoryReadFreshness.Fresh`; `SidecarSteamInventoryReader.BypassCache` → `HttpSteamSidecarInventoryClient.cs:53-62` `?refresh=true`. Sidecar tarafı bağımsız teyit: `sidecar-steam/src/api/routes.ts:108` `parseRefreshParam` bayrağı kabul ediyor |
| 6 — Gizli satıcı envanteri → 422 | ✓ | `TransactionReadinessService.cs:124-131` + controller `UnprocessableEntity`; kodlar `TransactionErrorCodes` üzerinden §7.2 ile ortak |
| 7 — Baseline okunamazsa üç kolon NULL | ✓ | `TransactionReadinessService.cs:205-223` — `else` dalında hiçbir kolona yazılmıyor. Tip düzeyinde de garanti: `InventoryClassBaselineResult.Private/Unavailable` sayı taşıyan bir örnek üretemiyor |
| 8 — SystemSetting kararı + v3.0 etiketi | ✓ | Seed Id'leri `IdFor(2)`/`IdFor(6)` → migration'daki `0aa51010-…-0002`/`-0006` **GUID'leri birebir eşleşiyor** → `UpdateData` gerçekten admin `Value`'sunu koruyor. Eski anahtar/env adı için repo taraması: canlı kod, compose, `.env.example`, FE i18n (4 dil) ve doküman **temiz**; kalan izler yalnız tarihsel `Migrations/*.Designer.cs` snapshot'ları (beklenen) ve changelog kayıtları |
| 9 — `SellerConfirmDeadline` fiilen armlanıyor | ✓ | `TransactionAcceptanceService.cs:243-258` yazıyor; tüketici zinciri doğrulandı: `DeadlineScannerJob.cs:106` ACCEPTED dalı → `TransactionTrigger.Timeout`. **Regresyon kontrolü:** FLAGGED invariant (`TransactionStateMachine.cs:349-355`) tüm deadline'ların NULL olmasını şart koşuyor; FLAGGED yalnız oluşturma anında set edildiği ve ACCEPTED'dan FLAGGED'a geçiş **olmadığı** için yeni yazılan kolon bu invariantı kıramıyor |

**Validator test koşusu (lokal, `daef4ab` üzerinde):** tam backend suite `dotnet test Skinora.sln -c Release` → **2553/2553 passed, 0 failed, 0 skipped — 13 assembly.** Shared 399 · Steam 39 · Platform 189 · Auth 120 · Admin 22 · **Users 22** · **Payments 6** · Realtime 40 · Fraud 91 · Disputes 60 · Notifications 171 · Transactions 862 · API 532. Ayrıca filtre bazında ayrı koşuldu: Integration filtresi ✓ (Transactions 376/376, API 467/467 dahil), Unit filtresi ✓, Steam unit 39/39.

> **Yapım raporundaki 2525/11 proje rakamı eksik sayımdır (düzeltildi):** `Skinora.Users.Tests` (22) ve `Skinora.Payments.Tests` (6) enumerasyona girmemiş; 2525 + 28 = **2553**. Test sonucu etkilenmiyor — her iki assembly de **0 fail**. Raporlama kusuru, kod kusuru değil.

### Validator bulguları

| # | Seviye | Açıklama | Etkilenen dosya | Durum |
|---|---|---|---|---|
| 1 | S1 (minör, doküman) | Rename sonrası **canlı** bir runbook hâlâ artık var olmayan `trade_offer_buyer_timeout_minutes` anahtarını adlandırıyor ve `SystemSettingSeed.cs:37`'ye atıf yapıyor — o satır artık açıklama yorumu. §7 maddesi T125'in okuyacağı **ileriye dönük** yönlendirmedir, tarihsel kayıt değil; T123'ün `KARAR` kapsam listesinde bu dosya sayılmamıştı | `Docs/INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md:316-318` | **Düzeltildi** (doğrulama turunda, yalnız anahtar adı + satır atfı; ölçüm iddiası değişmedi) |
| 2 | S1 (minör, dayanıklılık) | **Eşzamanlı çift confirm-ready** korunmamış. Kardeş servis `TransactionAcceptanceService.cs:304-330` bu yarışı açıkça yakalayıp `DbUpdateConcurrencyException`'ı 409'a çeviriyor; `TransactionReadinessService` çevirmiyor → satıcının çift tıkı **500** üretir (409 `INVALID_STATE_TRANSITION` yerine). Ayrıca kaybeden istek `SchedulePaymentTimeoutAsync`'i `SaveChanges`'ten **önce** çağırdığı için Hangfire'da job ID'si hiç kalıcılaşmayan **yetim bir job** bırakır | `Application/Lifecycle/TransactionReadinessService.cs:286` | **Follow-up** — para güvenliği etkisi **yok**: kaybeden `UPDATE` sıfır satır eşlediği için geçiş de baseline de outbox da kalıcılaşmıyor, yetim job ise `TimeoutExecutor.cs:55-58`'deki status + `IsOnHold` + `TimeoutFrozenAt` + deadline guard'larından geçemeyip no-op oluyor. Sıralı ikinci çağrı zaten 409 (`Second_Call_Is_Rejected_Rather_Than_Re_Opening_The_Window`) |

### Yapım raporu karşılaştırması

- **Uyum: yüksek.** Yapım raporunun AC tablosu, bilinen sınırları (400 karakter kırpma · 02 §10.1 ↔ 06 §3.5 gerilimi → T130 · idempotent olmama · Hangfire sözleşmesi) ve E2E baseline analizi validator'ın bağımsız bulgularıyla örtüşüyor. E2E iddiası ayrıca doğrulandı: 8 advisory leg **T121 merge run'ında da** (`31524132478`) aynı şekilde `failure`; sahiplik T137 → T138 planda mevcut.
- **Uyuşmazlık 1:** test toplamı 2525/11 proje → gerçek **2553/13 assembly** (yukarıda düzeltildi).
- **Uyuşmazlık 2:** raporun "Known Limitations" bölümü idempotentliği **sıralı** çağrı üzerinden ele alıyor; **eşzamanlı** çağrı yarışı (bulgu 2) kapsanmamış.
- Bunun dışında rapor kanıt seviyesi ve dürüstlük bakımından validator standardını karşılıyor.

## Altyapı Değişiklikleri

- **Migration:** Var — `20260813192334_T123_RenameTimeoutSettings`. İki `UpdateData` (SystemSettings `Key` + `Description`), satır `Id`'leri sabit → admin UI'dan girilmiş `Value` korunur. `Down` tersine çeviriyor. Şema değişikliği **yok**.
- **Config/env değişikliği:** Var — `SKINORA_SETTING_TRADE_OFFER_SELLER_TIMEOUT_MINUTES` → `SKINORA_SETTING_SELLER_CONFIRM_TIMEOUT_MINUTES`, `SKINORA_SETTING_TRADE_OFFER_BUYER_TIMEOUT_MINUTES` → `SKINORA_SETTING_DELIVERY_TIMEOUT_MINUTES`. Env adı anahtardan türetildiği için (`SettingsBootstrapService`) **eski adlar artık hiçbir şeyi doldurmaz**; eski adla gelen bir ortam startup'ta fail-fast eder. `.env.example` + iki compose dosyası güncellendi, DEPLOY_RUNBOOK §A'ya uyarı yazıldı.
- **Docker değişikliği:** Yok (yalnız compose env adları).

## Commit & PR

- Branch: `task/T123-seller-confirm-ready` (main `ec5a05e` üzerinden açıldı)
- Commit: `e0cae05` — T123: SELLER_CONFIRMED + POST /transactions/:id/confirm-ready
- PR: [#231](https://github.com/turkerurganci/Skinora/pull/231)
- CI: run [`31743413400`](https://github.com/turkerurganci/Skinora/actions/runs/31743413400) — **✓ PASS**, CI Gate `success`
- Branch izolasyon check: `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+...'` → yalnız **T123** ✓

**Bloke edici job'ların hepsi `success`:** Detect changed paths · 1. Lint · 2. Build · 3. Unit test · 3b. JS test (vitest) · 4. Integration test · 5. Contract test · **6. Migration dry-run** · 7. Docker build (backend) · 7. Docker build (frontend) · CI Gate. (`0. Guard (direct push)` beklendiği gibi `skipped`.)

**8 advisory E2E leg'i kırmızı — T117'den beri beklenen, T123 kaynaklı değil.** T120/T121'in yöntemiyle bağımsız ölçüldü: kodun değiştiği son main run'ı (**T121 merge, [`31524132478`](https://github.com/turkerurganci/Skinora/actions/runs/31524132478)**) baseline alındı — orada da **aynı 8 leg** `failure`. İmza karşılaştırması: T123 run'ının failure loglarında `Invalid object name 'PlatformSteamBots'` **tam 8 kez** (leg başına bir) geçiyor ve T123'ün yüzeylerinden (`confirm-ready`, `InventoryReadFreshness`, `refresh=true`, `seller_confirm_timeout_minutes`, `delivery_timeout_minutes`, `ITEM_NO_LONGER_AVAILABLE`, `BUYER_MOBILE_AUTHENTICATOR_INACTIVE`, `CaptureClassBaseline`, `SellerConfirmDeadline`, `buyerInventoryVisible`) **sıfır iz** var → yeni kırılma yok. Sahiplik T137 (`sidecar-fake` sürülebilir envanter) → T138 (E2E spec yeniden yazımı).

> **Baseline seçimi notu:** main'in *en son* run'ı (T122 merge, `31733188607`) karşılaştırma için **kullanılamaz** — T122 doküman-only olduğu için path filtresi 8 E2E leg'ini de `skipped` bırakmıştı, yani orada kıyaslanacak bir sonuç yok. T122 raporunun kendisi bu durumu "doküman-only task'larda o karşılaştırma konusuzdur" diye kaydetmişti; burada kod değiştiği için doğru baseline kodun değiştiği son main run'ıdır.

## Known Limitations / Follow-up

- **`payment.status` / `txHash` / `confirmedAt` null.** Blok adres + beklenen tutar + ağ ile dönüyor; ödeme bacakları T124+ ile geliyor (07 §7.5 zaten `payment.txHash`'i `PAYMENT_RECEIVED`+ kapsamına alıyor).
- **`BuyerBaselineAssetIds` 400 karakter taşmasında kırpılıyor.** 06 §3.5 kolon sınırı. Kırpma **yalnız** `WRONG_ITEM` dispute'unda "sonradan gelen asset"i ayırma yeteneğini zayıflatır (02 §10.1); teslimat doğrulamasını belirleyen `BuyerBaselineClassCount` **her zaman gerçek sayıdır** (02 §9.2 sayım kuralı). Kırpma `LogWarning` üretir. Gerçekçi envanterlerde ~28 asset sığıyor, T122'nin ölçtüğü en kalabalık sınıf 9 kopyaydı.
- **02 §10.1 ile 06 §3.5 arasında bir gerilim var (T123 kapsamı dışı, T130'un konusu).** 02 §10.1 "referans anlık görüntüden sonra **giren item'ları** tespit eder" diyor — bu envanterin **tamamının** baseline'ını gerektirir; 06 §3.5 ise `BuyerBaselineAssetIds`'i 400 karakterle sınırlayarak **sınıf kapsamlı** bir baseline tanımlıyor (T122: 199–219 asset'lik gerçek envanterler bu kolona sığmaz). T123 06 §3.5'i uyguladı. Farklı sınıftan gelen bir item'ın tespiti, `WRONG_ITEM` auto-checker'ını yeniden yazan **T130**'un çözmesi gereken bir tasarım sorusudur.
- **Uç idempotent değil** (07 §7.6b confirm-receipt'in aksine, bilinçli). İkinci çağrı 409 döner: yeniden onay `PaymentDeadline`'ı yeniden armlayıp alıcıya hak etmediği taze bir pencere verir, ayrıca satıcı zaten göndermişse baseline'ı **teslimattan sonra** alıp delta'yı yutar. Test: `Second_Call_Is_Rejected_Rather_Than_Re_Opening_The_Window`.
- **Hangfire scheduling hatası geçişi geri alır.** 09 §13.3'ün "job ID'leri + state tek `SaveChanges`" sözleşmesi gereği. `DeadlineScannerJob`'ın `SELLER_CONFIRMED` dalı zaten yedek olsa da, sözleşmeden sapmak yerine sözleşmeye uyuldu.

## Notlar

### Working tree hygiene (task.md Adım -1)
Working tree: **temiz** (session başında `git status --short` boş).

### Main CI startup check (task.md Adım 0)
`gh run list --branch main --limit 5` — son üç tamamlanmış run'ın hepsi `success`:
`31733188607` ✓ · `31733188616` ✓ · `31524132478` ✓ (T122 PR #230 merge edilmiş, main `ec5a05e`).

### Dış varsayımlar (task.md Adım 4)

| Varsayım | Kanıt | Sonuç |
|---|---|---|
| Sidecar `?refresh=` cache bypass'ı destekliyor | `sidecar-steam/src/api/routes.ts:108-120` (`parseRefreshParam`), `src/trade/InventoryService.ts:187-222` | ✓ var |
| Backend istemcisi bu bayrağı gönderiyor | `HttpSteamSidecarInventoryClient.cs:55` — `api/inventory/{steamId}`, query yok | ✗ **kırık — T123 kapsamına alındı** |
| Baseline için `classId`/`instanceId` sidecar yanıtında var | `HttpSteamSidecarInventoryClient.SidecarInventoryItem` (`classId`, `instanceId`) | ✓ var |
| Canlı MA probu (`ITradeHoldChecker`) kullanılabilir | `TransactionAcceptanceService` T119a'da kullanıyor | ✓ hazır |
| `SchedulePaymentTimeoutAsync` çağrılabilir durumda | `TimeoutSchedulingService.cs:32-68`; üretimde çağıranı **yoktu** (T117'de silindi) | ✓ hazır, T123 ilk tüketici |
| Ayar anahtarı rename'i env adını da değiştirir | `SettingsBootstrapService.cs:116` — `EnvPrefix + key.ToUpperInvariant()` | ✓ otomatik (doküman/compose elle güncellendi) |

Kırık varsayım **bir** taneydi ve scope'u etkiledi (envanter istemcisine `refresh` desteği eklendi); BLOCKED gerektirmedi çünkü düzeltme aynı görevin kabul kriterinin (03 §2.3 "önbelleksiz") zaten talep ettiği şeydi.

### Proje sahibi kararları (2026-08-13, yapım öncesi)

1. **AC5 adlandırma → (a) rename.** Gerekçe: admin panelinde satıcının teslimat penceresini "Alıcı trade offer timeout süresi" adlı kutu yönetiyordu; rename maliyeti (henüz tüketici yokken) en düşük seviyedeydi.
2. **AC4 → payment bloğu fiilen implement edilsin.** Aksi hâlde AC4 boşta doğru olurdu ama adres hiçbir yerde görünmezdi ve T135'in (FE ödeme ekranı) sahipsiz bağımlılığı kalırdı.
3. **Plan boşluğu → `SellerConfirmDeadline` T123'te armlansın.** Aksi hâlde adı düzeltilen ayar yine "üretimde hiç okunmayan ayar" olarak kalırdı.
4. **07 §7.6a hata listesine 422 `INVENTORY_PRIVATE` eklensin.** Gizli satıcı envanterini `ITEM_NO_LONGER_AVAILABLE`'a çöktürmek T121'in sildiği çöktürmenin aynısı; `STEAM_UNAVAILABLE`'a çöktürmek ise 08 §2.7 uyarınca retry ile düzelmeyecek bir duruma retry önerir.

### Mini güvenlik kontrolü (INSTRUCTIONS §3.6 Katman 1)

- **Secret sızıntısı:** Yok. Yeni secret/anahtar eklenmedi; env var **adları** değişti, değerler `.env`'de kalıyor.
- **Auth/authorization:** Yeni uç `[Authorize(Policy = AuthPolicies.Authenticated)]` + `[RateLimit("user-write")]` ile korumalı; ayrıca servis içinde **yalnız satıcı** kapısı var (`SellerId != caller` → 403 `NOT_A_PARTY`). Party guard state guard'dan **önce** çalışıyor, böylece yabancı bir çağıran rastgele id'lerin durumunu öğrenemiyor. Ödeme adresi ifşası taraf-özel (`role is null` → blok yok).
- **Input validation:** Uç **gövdesiz** (07 §7.6a) — kullanıcıdan gelen yeni girdi yok. Kullanılan tek serbest metin, kabul anında normalize edilip saklanmış `BuyerTradeUrl`'dir ve gerçek U17 parser'ından geçiriliyor; parse edilemezse 503 (fail-closed), asla "MA kapalı" olarak raporlanmıyor.
- **Yeni dış bağımlılık:** Yok.
