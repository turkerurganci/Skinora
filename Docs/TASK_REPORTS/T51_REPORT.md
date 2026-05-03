# T51 — İptal akışı

**Faz:** F3 | **Durum:** ⏳ Yapım bitti | **Tarih:** 2026-05-03

---

## Yapılan İşler

T44 state machine + T46 acceptance + T47–T50 timeout altyapısı sonrası, **kullanıcı kaynaklı iptal akışı** (satıcı/alıcı `POST /transactions/:id/cancel`) bağlandı (07 §7.7, 02 §7, 03 §2.5 / §3.3, 09 §13.3):

1. **`ItemRefundToSellerRequestedEvent` generalize edildi** (T49 → T51 ortak surface):
   - `Trigger` alanının tipi `TimeoutPhase` → yeni `Skinora.Shared.Enums.ItemRefundTrigger` enum'u (`TimeoutPayment`, `TimeoutDelivery`, `SellerCancel`, `BuyerCancel`).
   - T49 `TimeoutSideEffectPublisher.cs`: `Trigger: phase` → `Trigger: ItemRefundTrigger.TimeoutPayment` / `TimeoutDelivery` mapping (2 satır).
   - T49 testleri: `TimeoutPhase.Payment/Delivery` referansı `ItemRefundTrigger.TimeoutPayment/TimeoutDelivery` ile değiştirildi (3 dosya, 3 satır).
   - **Gerekçe:** Steam sidecar (T64–T68) consumer'ı tek event tipi üzerinden hem timeout hem cancel kaynaklı item iadelerini handle edebilir; alternatif olarak duplicate `ItemRefundToSellerOnCancelEvent` event'i bakım yükü yaratırdı. Enum count test'i 25 → 26 + yeni 4-value Theory + 1 fact eklendi.

2. **Yeni outbox event** `Skinora.Shared/Events/TransactionCancelledEvent.cs`:
   - `(EventId, TransactionId, CancelledByType CancelledBy, SellerId, Guid? BuyerId, ItemName, CancelReason, OccurredAt)`.
   - T49 `TransactionTimedOutEvent` ile paralel desen — notification fan-out trigger'ı; refund event'i ayrı (yukarıdaki `ItemRefundToSellerRequestedEvent`).

3. **Cancellation servisi** `Skinora.Transactions/Application/Lifecycle/`:
   - `ITransactionCancellationService` + `TransactionCancellationService` — ~200 satır.
   - 7-aşamalı pipeline: Stage 1 load tx (`!IsDeleted`, 404 `TRANSACTION_NOT_FOUND`) → Stage 2 party guard (caller `SellerId`/`BuyerId`, 403 `NOT_A_PARTY`) → Stage 3 reason validation (trim sonrası min 10 char, 400 `CANCEL_REASON_REQUIRED`) → Stage 4 state guard (post-payment 422 `PAYMENT_ALREADY_SENT` + role×state→trigger map; mismatch 409 `INVALID_STATE_TRANSITION`) → Stage 5 state machine `Fire(trigger, CancellationContext(reason))` → Stage 6 side effects (Hangfire job cancel + `ItemRefundToSellerRequestedEvent` if `EscrowBotAssetId` set + `TransactionCancelledEvent` always) → Stage 7 atomic commit + denormalized projection (BeginTransactionAsync ile state save → ReputationAggregator/CancelCooldownEvaluator AsNoTracking üzerinden komute edilen state'i okur → user updates save → tek transaction commit).
   - **Role × State → Trigger haritası:**
     - Seller × {CREATED, ACCEPTED, ITEM_ESCROWED} → `SellerCancel`
     - Seller × TRADE_OFFER_SENT_TO_SELLER → `SellerDecline` (state machine SellerCancel'i bu state'te permit etmiyor; aynı `CANCELLED_SELLER` sonu)
     - Buyer × {CREATED, ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED} → `BuyerCancel`
     - Diğer → null → `INVALID_STATE_TRANSITION`
   - **Post-payment guard:** PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED → 422 `PAYMENT_ALREADY_SENT` (state machine `BuyerDecline` permit etse de T51 endpoint'i bu yolu kapatır; T58 dispute / T59 admin orchestrator forward-devirli).

4. **Notification consumer** `Skinora.Notifications/Application/EventHandlers/TransactionCancelledNotificationConsumer.cs`:
   - `NotificationConsumerBase<TransactionCancelledEvent>` türevi, `notifications.transaction-cancelled` consumer name.
   - Role-aware fan-out: `CancelledBy = SELLER` → buyer'a (`"İşlem satıcı tarafından iptal edildi"`, 03 §2.5 step 9), `CancelledBy = BUYER` → seller'a (`"İşlem alıcı tarafından iptal edildi"`, 03 §3.3 step 8). İptal eden taraf bildirim almaz (response envelope yeterli). BuyerId null ise (pre-accept seller cancel) consumer no-op + processed-event marker.
   - T37 `TRANSACTION_CANCELLED` template + `{ItemName, Reason}` parameters yeniden kullanılır; yeni resx eklenmedi.

5. **Endpoint** `Skinora.API/Controllers/TransactionsController.cs`:
   - `[HttpPost("{id:guid}/cancel")] [Authorize(AuthPolicies.Authenticated)] [RateLimit("user-write")]` Cancel action eklendi (07 §7.7).
   - Outcome → HTTP eşleme: `Cancelled` 200, `NotFound` 404, `NotAParty` 403, `PaymentAlreadySent` 422, `InvalidStateTransition` 409, `ValidationFailed` 400.
   - `ITransactionCancellationService` constructor injection (6. parametre).

6. **DI** `Skinora.API/Configuration/TransactionsModule.cs`:
   - `services.AddScoped<ITransactionCancellationService, TransactionCancellationService>();` (T46 bloğunun yanına).

7. **DTO + error codes** `Skinora.Transactions/Application/Lifecycle/`:
   - `TransactionLifecycleDtos.cs`: `CancelTransactionRequest(Reason)` + `CancelTransactionResponse(Status, CancelledAt, ItemReturned, PaymentRefunded)` + `CancelTransactionOutcome` + `CancelTransactionStatus` enum (Cancelled, NotFound, NotAParty, PaymentAlreadySent, InvalidStateTransition, ValidationFailed).
   - `TransactionErrorCodes.cs`: `PaymentAlreadySent = "PAYMENT_ALREADY_SENT"` + `CancelReasonRequired = "CANCEL_REASON_REQUIRED"`.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Skinora.Shared/Enums/ItemRefundTrigger.cs` — 4-value enum (TimeoutPayment/TimeoutDelivery/SellerCancel/BuyerCancel).
- `backend/src/Skinora.Shared/Events/TransactionCancelledEvent.cs` — outbox event, notification fan-out trigger.
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/ITransactionCancellationService.cs` — 1-method interface.
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCancellationService.cs` — pipeline impl.
- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/TransactionCancelledNotificationConsumer.cs` — role-aware counter-party notify.
- `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCancellationServiceTests.cs` — 21 test (happy path × role × state, reason validation, NotAParty, NotFound, post-payment guard 3 InlineData, terminal/non-cancellable 6 InlineData, reputation recompute, cooldown stamp, Hangfire cancel, reason trim).
- `backend/tests/Skinora.Notifications.Tests/Unit/TransactionCancelledNotificationConsumerTests.cs` — 4 unit test (seller→buyer, buyer→seller, null buyer no-op, idempotency).

**Değişiklik:**
- `backend/src/Skinora.Shared/Events/ItemRefundToSellerRequestedEvent.cs` — `Trigger` alanı tipi `TimeoutPhase` → `ItemRefundTrigger`, xmldoc güncellendi.
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutSideEffectPublisher.cs` — Trigger mapping (2 satır).
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionLifecycleDtos.cs` — T51 cancel DTO/outcome/enum eklendi.
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionErrorCodes.cs` — 2 yeni constant (`PaymentAlreadySent`, `CancelReasonRequired`).
- `backend/src/Skinora.API/Controllers/TransactionsController.cs` — Cancel action + envelope helper + DI parametresi (xmldoc T51 referansı eklendi).
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — 1 satır DI kaydı.
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutSideEffectPublisherTests.cs` — 2 assertion `ItemRefundTrigger.*` enum'una çekildi.
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/DeadlineScannerJobSideEffectsTests.cs` — 1 assertion güncellendi.
- `backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs` — `ItemRefundTrigger_ShouldHave4Values` Fact + 4-value Theory + `AllEnums_ShouldExistInSharedNamespace` count 25 → 26.
- `backend/tests/Skinora.API.Tests/Integration/TransactionLifecycleEndpointTests.cs` — 6 yeni endpoint test (T51 §7.7: 401, happy path, 403, 400, 422, 404).

**Migration:** Yok. **SystemSetting:** Yok (cancel cooldown thresholds T43 seed'inde mevcut). **Yeni dış paket:** Yok.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `POST /transactions/:id/cancel` → satıcı/alıcı iptali | ✓ | Endpoint `TransactionsController.Cancel` action; `Authorize(Authenticated)` + `user-write` rate-limit. Servis `ResolveRole` ile caller'ı `SellerId`/`BuyerId`'den belirler. `Cancel_Happy_Path_Returns_200_And_Persists_CancelledSeller` endpoint testi + `Seller_Cancel_From_Created_*` / `Buyer_Cancel_From_ItemEscrowed_*` integration testleri. |
| 2 | Kontroller: ödeme gönderilmişse iptal engeli, taraf, state | ✓ | `IsPostPaymentState` guard PAYMENT_RECEIVED/TRADE_OFFER_SENT_TO_BUYER/ITEM_DELIVERED → 422 `PAYMENT_ALREADY_SENT`. `ResolveRole` null → 403 `NOT_A_PARTY`. `ResolveTrigger` null → 409 `INVALID_STATE_TRANSITION`. Theory testleri her 3 post-payment state + 6 terminal/non-cancellable state için doğrular. |
| 3 | İptal sebebi zorunlu (min 10 karakter) | ✓ | `Stage 3` reason validation: `(request.Reason ?? "").Trim().Length < 10` → 400 `CANCEL_REASON_REQUIRED`. `Reason_Below_Minimum_Length_Returns_400_Validation` + `Reason_Whitespace_Padding_Counts_Trimmed_Length` testleri (9-char trimmed reddedilir). |
| 4 | Item platformdaysa → satıcıya iade tetikleme | ✓ | `itemWasOnPlatform = previousStatus == ITEM_ESCROWED` (pre-fire capture). True ise `ItemRefundToSellerRequestedEvent` (Trigger=Seller/BuyerCancel) outbox publish edilir; Steam sidecar T64–T68 forward-devirli. `Seller_Cancel_From_ItemEscrowed_Emits_Item_Refund_With_Seller_Cancel_Trigger` + `Buyer_Cancel_From_ItemEscrowed_Emits_Item_Refund_With_Buyer_Cancel_Trigger` testleri doğrular. **Ön-ITEM_ESCROWED state'lerde** (CREATED/ACCEPTED/TRADE_OFFER_SENT_TO_SELLER) item zaten satıcıda, refund event yok — `Seller_Cancel_From_TradeOfferSentToSeller_Maps_To_SellerDecline_Trigger` doğrular. |
| 5 | Ödeme alınmışsa → alıcıya iade tetikleme (fiyat + komisyon - gas fee) | ~ Kısmi (forward-devir) | T51 user-cancel post-payment state'leri 422 ile reddediyor (02 §7 "Alıcı ödemeyi gönderdiyse hiçbir taraf tek taraflı iptal edemez"); ödeme iadeli iptal yolları: **timeout** (T49 ✓ — `PaymentRefundToBuyerRequestedEvent` Delivery phase), **admin cancel** (T59 forward), **dispute** (T58 forward). T51 response'unda `paymentRefunded: false` her zaman; alan post-payment cancel için reserved slot olarak korunur. T59'da iadenin gerçek tutar hesabı (T52 commission + T53 gas fee) bağlanır. |
| 6 | CANCELLED_SELLER / CANCELLED_BUYER / CANCELLED_TIMEOUT / CANCELLED_ADMIN state'leri | ✓ | T51 SELLER + BUYER state geçişlerini kapsar (state machine triggers: SellerCancel/SellerDecline/BuyerCancel). TIMEOUT zaten T49 ✓. ADMIN T59 forward-devirli (TransactionStateMachine'de `AdminCancel` trigger'ı T44'te tanımlı, endpoint orchestrator T59'da). |
| 7 | İptal kaydı itibar skoruna yansıtılır | ✓ | `Stage 7a`: `IReputationAggregator.RecomputeAsync(SellerId)` + (BuyerId varsa) `RecomputeAsync(BuyerId)`. Aggregator 06 §3.1 responsibility map ile sorumlu tarafın rate'ini yeniden hesaplar (CANCELLED_SELLER → seller, CANCELLED_BUYER → buyer); wash filter (02 §14.1) denominator'a uygulanır. `Successful_Seller_Cancel_Recomputes_Reputation_For_Both_Parties` testi seller rate=0 + buyer rate=null doğrular. |
| 8 | İptal cooldown hesaplama | ✓ | `Stage 7b`: `IUserCancelCooldownEvaluator.EvaluateAsync(callerUserId)`. Sorumlu tarafın 02 §14.2 rolling window'da CANCELLED_SELLER/BUYER/TIMEOUT-by-user count'u limit aşarsa `User.CooldownExpiresAt` damgalar. `Successful_Buyer_Cancel_Stamps_Cooldown_When_Threshold_Exceeded` testi limit=1 + 1 prior cancel + 1 yeni cancel senaryosunda cooldown stamp'ı doğrular. |
| 9 | Bildirimler: karşı tarafa iptal bildirimi | ✓ | `TransactionCancelledEvent` outbox publish + `TransactionCancelledNotificationConsumer` role-aware counter-party fan-out. SELLER cancel → buyer notify; BUYER cancel → seller notify; null buyer no-op (pre-accept seller cancel). 4 unit test consumer'ı doğrular; integration test outbox row sayısını doğrular. |

**Doğrulama kontrol listesi:**

- [x] **02 §7 tüm iptal kuralları uygulanmış mı?** ✓ — Ödeme öncesi seller/buyer cancel ✓; ödeme sonrası iki taraflı yasak ✓ (PAYMENT_ALREADY_SENT); reason zorunlu ✓ (min 10 char trim); cooldown ✓ (T43 evaluator); admin direct cancel + emergency hold + ITEM_DELIVERED kısıtı T59 forward-devirli (T51 user-endpoint'i kapsamı dışı, doğru ele alınmış).
- [x] **07 §7.7 sözleşmesi doğru mu?** ✓ — Request `{ reason }` min 10 char ✓; Response `{ status, cancelledAt, itemReturned, paymentRefunded }` 4-alan envelope ✓; hata kodları `PAYMENT_ALREADY_SENT` 422 + `INVALID_STATE_TRANSITION` 409 + `NOT_A_PARTY` 403 + `VALIDATION_ERROR`/`CANCEL_REASON_REQUIRED` 400 ✓; auth Authenticated ✓. Endpoint testleri 6/6 envelope + status code + error code'u doğrular.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (TransactionCancelledNotificationConsumer) | ✓ 4/4 | Seller→buyer fan-out, buyer→seller fan-out, null buyer no-op, idempotency. |
| Integration (TransactionCancellationService) | ✓ 21/21 | Happy path × role × state (Seller CREATED/ITEM_ESCROWED/TRADE_OFFER_SENT_TO_SELLER + Buyer ITEM_ESCROWED), reason validation (kısa + whitespace trim), NotAParty + NotFound, post-payment 3 InlineData (PAYMENT_RECEIVED/TRADE_OFFER_SENT_TO_BUYER/ITEM_DELIVERED), terminal/non-cancellable 6 InlineData (COMPLETED + CANCELLED_* + FLAGGED), reputation recompute, cooldown stamp, Hangfire job cancel, reason trim. |
| API Endpoint (CancelTransaction §7.7) | ✓ 6/6 | 401 unauthenticated, 200 happy path + outbox row, 403 NotAParty, 400 reason too short, 422 PaymentAlreadySent, 404 NotFound. |
| Regresyon — Skinora.Transactions.Tests | ✓ 445/445 | T50: 424 → T51: 445 (+21 = 21 yeni TransactionCancellationService). |
| Regresyon — Skinora.API.Tests | ✓ 271/271 | T50: 265 → T51: 271 (+6). |
| Regresyon — Skinora.Notifications.Tests | ✓ 77/77 | T50: 73 → T51: 77 (+4). |
| Regresyon — Skinora.Shared.Tests | ✓ 187/187 | T50: 182 → T51: 187 (+5 = `ItemRefundTrigger_ShouldHave4Values` Fact + `ItemRefundTrigger_ShouldContainExpectedValue` 4-InlineData Theory + cross-cutting count assertion 25→26). |
| Toplam — 12 test assembly (isolated) | ✓ 1300/1300 | Skinora.Users 16 + Fraud 17 + Payments 6 + Disputes 11 + Steam 21 + Shared 187 + Platform 136 + Transactions 445 + API 271 + Auth 93 + Notifications 77 + Admin 20 = **1300**. T50: 1264 → T51: 1300 (+36 net). |
| Build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release --nologo` — `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| Format verify | ✓ exit=0 | `dotnet format --verify-no-changes` clean (1× otomatik düzeltme tek pass'te kapandı, sonraki verify boş). |
| Solution-paralel çalıştırma flake'i | — | `dotnet test Skinora.sln` paralel modunda 6 modülde toplam 17 fail; **isolated re-run'da 12 modül de 100% PASS**. Cause: Testcontainers Docker saturation when 12 MsSql containers spin up at once on Windows Docker Desktop. CI'da shared mssql + per-class DB izolasyonu (T11.3) sorun değil. |

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok. CancelReason kullanıcı input'u; outbox event + AuditLog'a düşmeyecek hassas data değil (DB'de `Transaction.CancelReason` kolonu zaten string(500) saklanıyor — 06 §3.5).
- **Auth/authorization etkisi:** Endpoint `Authenticated` policy + party guard servis katmanında (caller `SellerId`/`BuyerId` mı?); non-party 403. Stranger DBcl auth flow ile geçemez (claim'siz JWT zaten 401).
- **Input validation:** Reason min 10 char trim ✓; null body → 400 (controller level); state guards 4 katmanlı (post-payment + role+state mismatch + state machine guard + DB CK_Transactions_FreezeActive/Passive constraint'leri kuruluymuş kalıyor).
- **Concurrency:** State machine constructor'da opsiyonel `expectedRowVersion` desteği var; bu şu an cancel'da kullanılmıyor (T44'te tanımlı kontrat). RowVersion otomatik EF Core optimistic concurrency ile DB-level korumalı (06 §3.5 RowVersion alan). T59 admin cancel'da explicit RowVersion guard eklenebilir; T51 normal kullanıcı yolunda iki paralel cancel zaten state machine guard ile kapanır (ikinci `CanFire` false, `INVALID_STATE_TRANSITION`).
- **Yeni dış bağımlılık:** Yok.

## Mimari Kararlar (Notlar)

1. **`ItemRefundToSellerRequestedEvent.Trigger` enum'unun generalize edilmesi (TimeoutPhase → ItemRefundTrigger):** T49 zaten merge edilmiş, bu kararı T49 sonrası almak T49'a dokunma riski getiriyordu. Alternatif olarak yeni `TransactionCancelledItemReturnRequestedEvent` ayrı event yaratabilirdim ama Steam sidecar (T64–T68) consumer'ı tek eylemi (item return to seller) iki event'ten dinlemek zorunda kalırdı + her yeni cancel kaynağı (T59 admin) için yeni event. Generalize ederek tek event/tek consumer modelini koruyoruz; T49 callers + 3 test dosyası mekanik update (Trigger field assertion). EnumTests count assertion 25 → 26 + ItemRefundTrigger explicit Theory + Fact eklendi.

2. **`paymentRefunded` her zaman false (T51 user-cancel scope'u):** 02 §7 "Alıcı ödemeyi gönderdiyse hiçbir taraf tek taraflı iptal edemez" — post-payment state'lerde T51 endpoint'i 422 ile reddeder, dolayısıyla ödeme iade yolu T51'de tetiklenmez. Ödeme iadeli cancel yolları: **timeout** (T49 ✓), **admin** (T59 forward), **dispute** (T58 forward). Response'taki `paymentRefunded` field'ı 07 §7.7 contract'ında zorunlu — false olarak doldurulur ve yapım raporu plan kabul kriteri #5 için "kısmi" + forward-devir notu içerir.

3. **State machine'de `BuyerDecline` permit edilmiş (TRADE_OFFER_SENT_TO_BUYER) ama T51 endpoint'inde kapalı:** T44 state machine `TRADE_OFFER_SENT_TO_BUYER + BuyerDecline → CANCELLED_BUYER` permit ediyor (alıcı teslim trade offer'ını reddederse). Bu ödeme sonrası bir state olduğu için 02 §7 yasağı kapsamında — T51 user-endpoint'i bu yolu açmaz, `IsPostPaymentState` 422 ile reddeder. `BuyerDecline` trigger'ı T58 dispute orchestrator'ı veya T59 admin akışı tarafından tüketilir (T51 raporunun "Known Limitations" notu).

4. **Role × State → Trigger haritalama (`SellerCancel` vs `SellerDecline`):** State machine'de SellerCancel **sadece pre-trade-offer** state'lerde permit edilmiş (CREATED/ACCEPTED/ITEM_ESCROWED); TRADE_OFFER_SENT_TO_SELLER state'inde SellerDecline trigger'ı kullanılır (semantik fark: "satıcı trade offer'ı göndermek istemiyor"). Servis bu nüansı kullanıcıdan saklar — endpoint tek tip "cancel" arzısını alır, servis state'e göre doğru trigger'ı seçer. Sonuç state aynı (`CANCELLED_SELLER` + `CancelledBy=SELLER`). `Seller_Cancel_From_TradeOfferSentToSeller_Maps_To_SellerDecline_Trigger` testi doğrular.

5. **Atomic commit'in iki saveChanges + DB transaction ile çözülmesi:** `IReputationAggregator.RecomputeAsync` ve `IUserCancelCooldownEvaluator.EvaluateAsync` iç sorgularda `AsNoTracking()` kullanıyor (canonical state read pattern, T43 kararı). Bu nedenle in-flight cancel state'i (henüz SaveChange'lenmemiş) recompute query'sinde görünmüyor. Çözüm: state flip + outbox publish'i ilk SaveChanges ile commit et; ardından recompute → ikinci SaveChanges; her iki save'i tek `BeginTransactionAsync` scope'unda kapsa. İlk save commit edilse bile transaction commit'i ikinci save sonrası olduğundan DB-level atomicity korunur. Ek complexity yerine alternatif (örn. tracked-aware reputation aggregator) T43'te seçilmemişti; T51 tek rebid tüketici olarak transaction wrap'i dayanıklı çözüm.

6. **Pre-cancel state capture (`previousStatus`/`itemWasOnPlatform`):** State machine OnEntry hook'ları `CancelledAt`'ı stamp'lediği için `transaction.Status` flip sonrası anlık geçmiş bilgi kaybolur. Servis `Fire`'dan ÖNCE `previousStatus = transaction.Status`, `itemWasOnPlatform = previousStatus == ITEM_ESCROWED` snapshot'larını alır. Bu yapı T49 publisher'ında da var (`previousStatus` parametresi caller'dan geçer); T51 servis-içi capture eder.

7. **Cooldown evaluator caller-side vs both-sides:** `_cooldown.EvaluateAsync(callerUserId, ct)` sadece iptal eden tarafa çağrılır. Aggregator zaten responsibility map ile non-responsible counter-party'yi filtreler (CANCELLED_SELLER + buyer caller → no-op). Buyer'ın cancel'ı durumunda seller'a cooldown stamp'lemek mantıksız (seller iptale neden olmadı). T59 admin cancel için cooldown caller-side **uygulanmaz** (admin actor responsible değildir + 02 §13 CANCELLED_ADMIN responsibility map'inde dışlanmış).

## Known Limitations

- **`paymentRefunded` her zaman false (T51 scope'u):** Plan kabul kriteri #5 "Ödeme alınmışsa → alıcıya iade tetikleme" T51'de tetiklenmez (post-payment cancel kapalı). Ödeme iadeli cancel yolları forward-devir: T49 timeout (Delivery phase ✓ implemented), T59 admin cancel (T59 plan kabul kriteri içinde), T58 dispute. T51 response field'ı reserved slot.
- **Bildirim metni Türkçe verbatim:** `TransactionCancelledNotificationConsumer.BuildRequest` `Reason` parametresine "İşlem satıcı/alıcı tarafından iptal edildi" Türkçe literal yazıyor (03 §2.5/§3.3 step metni); locale coverage T97 i18n full coverage'da resx'e taşınacak (T49 timeout reason metinleriyle birlikte).
- **`BuyerDecline` (TRADE_OFFER_SENT_TO_BUYER) yolu kapalı:** State machine permit ediyor ama T51 endpoint'i 02 §7 nedeniyle 422 ile reddediyor. Bu yol T58 dispute (alıcı teslim itirazı) veya T59 admin manual akışları tarafından tüketilecek.
- **Concurrency token (RowVersion) cancel'da explicit kullanılmıyor:** State machine constructor opsiyonel `expectedRowVersion` parametresi alıyor ama T51 servis bunu inject etmiyor (kullanıcı UI'sinden RowVersion döndürmüyor). EF Core otomatik optimistic concurrency yine de aktif (`Transaction.RowVersion` IsRowVersion). İki paralel cancel: ikinci `CanFire` false → `INVALID_STATE_TRANSITION` (state machine guard); EF Core concurrency exception ayrıca DbUpdateConcurrencyException olarak yükselebilir (production'da global error handler'a düşer).

## Working Tree Hygiene

Session başında: `git status --short` **temiz** (T50 merge sonrası clean).

## Main CI Startup Check

`gh run list --branch main --limit 3`:
- `25275894773` ✓ (T50 #81 — 2026-05-03 09:49)
- `25275894766` ✓ (T50 #81 — 2026-05-03 09:49)
- `25259885322` ✓ (chore F2 status fix #80 — 2026-05-02 19:23)

Üç run da `success`. Adım 0 geçti.

## Dış Varsayımlar

Yok — T51 yalnız mevcut altyapıya ekleme yapar (T44 state machine + T46 acceptance + T47–T50 timeout + T43 reputation/cooldown). Yeni paket yok, yeni external API yok, plan tier varsayımı yok.

## Commit & PR

- Branch: `task/T51-cancellation-flow`
- Commit: `fb301c8`
- PR: [#82](https://github.com/turkerurganci/Skinora/pull/82)
- Task branch CI: Run [`25277455577`](https://github.com/turkerurganci/Skinora/actions/runs/25277455577) ✓ success (9/9 + Guard skipped)
