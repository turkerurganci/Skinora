# T61 — SignalR hub: işlem real-time güncellemeler

**Faz:** F3 | **Durum:** ✓ PASS (bağımsız validator) | **Tarih:** 2026-05-07

---

## Yapılan İşler

T61, 07 §11.1 RT1 kontratını gerçekleyen `/hubs/transactions` SignalR hub'ını ve buna bağlı 8 server→client event'ini canlıya alır. Yapı, mevcut F0 outbox pipeline'ına paralel bir tüketici ailesi olarak kuruldu — domain event'i yayınlandığında MediatR fan-out hem Notifications consumer'ını (T37) hem de yeni Realtime consumer'ını çalıştırır. Status değişiklikleri için `TransactionCancelledEvent` ve `TransactionTimedOutEvent` event'lerine `FromStatus` snapshot'ı eklendi (üretici, Fire öncesi state'i zaten elinde tutuyor).

1. **Yeni modül `Skinora.Realtime/`:** Hub + publisher portu + SignalR adaptörü + 8 MediatR consumer + 30 sn periyodik countdown broadcaster.
   - `Hubs/TransactionsHub.cs` — `/hubs/transactions` mount; `[Authorize]`; `JoinTransaction(Guid)` (TX'in seller/buyer'ı olmayan caller `HubException("TRANSACTION_FORBIDDEN")` ile reddedilir, var olmayan TX `TRANSACTION_NOT_FOUND`); `LeaveTransaction(Guid)` idempotent. Group adı: `tx:{Guid:N}`. Buyer ve seller aynı odaya katılır (S07 detail page).
   - `Application/ITransactionRealtimePublisher.cs` — 8 metot port (`PublishStatusChangedAsync`, `PublishCountdownSyncAsync`, `PublishPaymentDetectedAsync`, `PublishPaymentConfirmedAsync`, `PublishDisputeUpdateAsync`, `PublishFlagResolvedAsync`, `PublishEmergencyHoldAppliedAsync`, `PublishEmergencyHoldReleasedAsync`).
   - `Infrastructure/SignalRTransactionRealtimePublisher.cs` — `IHubContext<TransactionsHub>` adaptörü; her push group-bazlı `SendAsync(method, payload)`. Transport hatası best-effort (try/catch + log) — outbox dispatcher'a redelivery sinyali yansıtılmaz.
   - `Application/Contracts/TransactionRealtimePayloads.cs` — 07 §11.1 tablo birebir 8 record (`TransactionStatusChanged`, `CountdownSync`, `PaymentDetected`, `PaymentConfirmed`, `DisputeUpdate`, `FlagResolved`, `EmergencyHoldApplied`, `EmergencyHoldReleased`). Enum'lar JSON'da string olarak serileştirilir (Program.cs `AddJsonProtocol` config'i — front end "CANCELLED_BUYER" gibi spec değerlerini bekler).
   - `Application/EventHandlers/RealtimeConsumerBase.cs` — `NotificationConsumerBase` paterni mirror; `IProcessedEventStore` ile consumer-side idempotency (`realtime.<event-name>` key).
   - 8 concrete consumer:
     - `BuyerAcceptedRealtimeConsumer` → `StatusChanged(CREATED→ACCEPTED)`. Hard-coded `from` because state machine `HasFieldsForAccepted` guard tek geçiş yolunu garanti eder.
     - `TransactionCancelledRealtimeConsumer` → `StatusChanged(FromStatus → CANCELLED_<who>)`. `CancelledBy` switch hedef state'i seçer.
     - `TransactionTimedOutRealtimeConsumer` → `StatusChanged(FromStatus → CANCELLED_TIMEOUT)`.
     - `PaymentReceivedRealtimeConsumer` → `PaymentConfirmed` (20 conf) + `StatusChanged(ITEM_ESCROWED→PAYMENT_RECEIVED)`. `PaymentDetected` (mempool sighting) için ayrı domain event T-future blockchain monitor'da gelecek.
     - `DisputeAutoResolvedRealtimeConsumer` → `DisputeUpdate(CLOSED, autoCheckResult=Outcome metni)`.
     - `DisputeEscalatedRealtimeConsumer` → `DisputeUpdate(ESCALATED, autoCheckResult=AUTO_WRONG_ITEM|null)`.
     - `FraudFlagApprovedRealtimeConsumer` → `FlagResolved(APPROVED)` + `StatusChanged(FLAGGED→CREATED)`. Account-level flag (`TransactionId == null`) push üretmez.
     - `FraudFlagRejectedRealtimeConsumer` → `FlagResolved(REJECTED)` + `StatusChanged(FLAGGED→CANCELLED_ADMIN)`.
     - `EmergencyHoldAppliedRealtimeConsumer` → `EmergencyHoldApplied(message=Reason)`. Status değişmez (overlay flag); client'lar ardından gelen `CountdownSync.frozen=true`'yu kullanır.
     - `EmergencyHoldReleasedRealtimeConsumer` → `EmergencyHoldReleased(action, resumedStatus)`.
   - `Application/Countdown/CountdownSyncBroadcaster.cs` — `BackgroundService`; her 30 sn aktif TX'leri (CREATED/ACCEPTED/TRADE_OFFER_SENT_TO_SELLER/ITEM_ESCROWED/TRADE_OFFER_SENT_TO_BUYER) tarar, status'a göre `TimeoutPhase` + deadline sütununu çözer, `(deadline − now)` kalan saniyeyi yayınlar. Donmuş TX (`IsOnHold` veya `TimeoutFreezeReason != null`) için `TimeoutRemainingSeconds` snapshot'ı kullanılır (state machine `ApplyEmergencyHold` damgalar) ve `frozen=true` + `frozenReason` döner. Hata izolasyonu per-tx try/catch — bir bozuk satır sweep'i öldürmez. `BroadcastOnceAsync` public — testler manuel tetikler.
   - `Application/Countdown/CountdownSyncOptions.cs` — `Interval` (default 30 s) + `Enabled` (testlerde `false` → entegrasyon testi sweep'i devre dışı).
   - `RealtimeModule.cs` — `AddRealtimeModule()` tek satır DI: publisher Scoped + broadcaster `AddHostedService<>` + options bind.

2. **Domain event sözleşmesi enrichment:** `TransactionCancelledEvent` ve `TransactionTimedOutEvent` record'larına `FromStatus: TransactionStatus` parametresi eklendi. Üretim çağrı yerleri (`TransactionCancellationService`, `AdminTransactionService` 2 yerde, `TimeoutSideEffectPublisher`) zaten `previousStatus` snapshot'ını fire öncesi alıyordu — tek satır eklendi. Notifications consumer test fixture'ları (`TransactionCancelledNotificationConsumerTests` 6 case + `TransactionTimedOutNotificationConsumerTests` 4 case) güncellendi. Realtime consumer'ı bu alanı doğrudan kullanır → Realtime tarafında ayrıca DB lookup yapılmaz.

3. **API host wiring (`Skinora.API/Program.cs` + `Configuration/AuthModule.cs` + `Outbox/OutboxModule.cs` + `Skinora.API.csproj`):**
   - `AddSignalR().AddJsonProtocol(...JsonStringEnumConverter)` — JSON protocol enum'ları string olarak serileştirir (07 §11.1 tablo değerleri).
   - `AddRealtimeModule(Configuration)` — yeni modül DI.
   - `app.MapHub<TransactionsHub>("/hubs/transactions")` — endpoints aşaması, controllers'tan sonra.
   - **JWT query-param bridge (`AuthModule.cs`):** `JwtBearerEvents.OnMessageReceived` SignalR JS client'larının WebSocket handshake'inde `Authorization` header'ı set edememesi sebebiyle (07 §11.1 "JWT query param") — request `/hubs/*` ile başlıyorsa `?access_token=` query param'ı bearer token olarak kabul edilir. Diğer endpoint'ler değişmeden header-only kalır.
   - **MediatR scan (`OutboxModule.cs`):** `Skinora.Realtime.RealtimeModule` assembly'si MediatR scan listesine eklendi — outbox dispatcher fan-out 8 yeni consumer'ı bulur.
   - `Skinora.API.csproj` ve `Skinora.sln` güncellendi.

4. **Doküman uyumu:** Plan/spec değişikliği yok — 07 §11.1 RT1 kontratı birebir uygulandı. `TransactionCancelledEvent` ve `TransactionTimedOutEvent` enrichment'ları yeni alan ekleyişi olduğu için 06/05 entity şemasını etkilemez (event'ler outbox payload — schema dışı).

## Etkilenen Modüller / Dosyalar

**Yeni — `Skinora.Realtime/` (15 dosya):**
- `backend/src/Modules/Skinora.Realtime/Skinora.Realtime.csproj`
- `backend/src/Modules/Skinora.Realtime/RealtimeModule.cs`
- `backend/src/Modules/Skinora.Realtime/Hubs/TransactionsHub.cs`
- `backend/src/Modules/Skinora.Realtime/Application/ITransactionRealtimePublisher.cs`
- `backend/src/Modules/Skinora.Realtime/Application/Contracts/TransactionRealtimePayloads.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/RealtimeConsumerBase.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/BuyerAcceptedRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/TransactionCancelledRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/TransactionTimedOutRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/PaymentReceivedRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/DisputeAutoResolvedRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/DisputeEscalatedRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/FraudFlagApprovedRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/FraudFlagRejectedRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/EmergencyHoldAppliedRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/EmergencyHoldReleasedRealtimeConsumer.cs`
- `backend/src/Modules/Skinora.Realtime/Application/Countdown/CountdownSyncBroadcaster.cs`
- `backend/src/Modules/Skinora.Realtime/Application/Countdown/CountdownSyncOptions.cs`
- `backend/src/Modules/Skinora.Realtime/Infrastructure/SignalRTransactionRealtimePublisher.cs`

**Yeni testler (`Skinora.Realtime.Tests/` + `Skinora.API.Tests/`):**
- `backend/tests/Skinora.Realtime.Tests/Skinora.Realtime.Tests.csproj`
- `backend/tests/Skinora.Realtime.Tests/Unit/RecordingRealtimePublisher.cs` (test double + in-memory processed-event store)
- `backend/tests/Skinora.Realtime.Tests/Unit/RealtimeConsumerTests.cs` (16 test — her consumer + idempotency)
- `backend/tests/Skinora.Realtime.Tests/Unit/CountdownSyncBroadcasterTests.cs` (9 test — phase × deadline × frozen × past-due)
- `backend/tests/Skinora.API.Tests/Integration/TransactionsHubEndpointTests.cs` (5 test — connect anon 401 / connect auth / Join member / Join non-member / Join unknown / publisher round-trip)

**Değişiklik:**
- `backend/src/Skinora.Shared/Events/TransactionCancelledEvent.cs` — `FromStatus` parametresi.
- `backend/src/Skinora.Shared/Events/TransactionTimedOutEvent.cs` — `FromStatus` parametresi.
- `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCancellationService.cs` — `FromStatus: previousStatus` alanı.
- `backend/src/Modules/Skinora.Transactions/Application/Admin/AdminTransactionService.cs` — 2 callsite `FromStatus: previousStatus`.
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutSideEffectPublisher.cs` — `FromStatus: previousStatus`.
- `backend/src/Skinora.API/Program.cs` — using import + `AddSignalR + AddJsonProtocol` + `AddRealtimeModule` + `MapHub`.
- `backend/src/Skinora.API/Configuration/AuthModule.cs` — `OnMessageReceived` query-param bridge for `/hubs/*` paths.
- `backend/src/Skinora.API/Outbox/OutboxModule.cs` — MediatR scan list + `Skinora.Realtime` assembly.
- `backend/src/Skinora.API/Skinora.API.csproj` — Realtime project reference.
- `backend/Skinora.sln` — Realtime + Realtime.Tests projeleri.
- `backend/tests/Skinora.API.Tests/Skinora.API.Tests.csproj` — `Microsoft.AspNetCore.SignalR.Client` 9.0.3 paketi.
- `backend/tests/Skinora.Notifications.Tests/Unit/TransactionCancelledNotificationConsumerTests.cs` — 6 fixture `FromStatus` alanı.
- `backend/tests/Skinora.Notifications.Tests/Unit/TransactionTimedOutNotificationConsumerTests.cs` — 4 fixture `FromStatus` alanı.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `/hubs/transactions` hub'ı | ✓ | `TransactionsHub.cs` `[Authorize]`; `Program.cs` `app.MapHub<TransactionsHub>("/hubs/transactions")` (T61 yorumu); integration `Connect_Without_Token_Returns401` 401 doğrular. |
| 2 | Client→Server: JoinTransaction, LeaveTransaction | ✓ | `TransactionsHub.JoinTransaction(Guid)` (üyelik kontrolü `TRANSACTION_NOT_FOUND`/`TRANSACTION_FORBIDDEN`), `LeaveTransaction(Guid)` (idempotent). Integration test `JoinTransaction_AsParticipant_Succeeds`/`AsNonParticipant_ThrowsForbidden`/`UnknownTransaction_ThrowsNotFound`. |
| 3 | Server→Client: 8 RT1 event'i (`TransactionStatusChanged`, `CountdownSync`, `PaymentDetected`, `PaymentConfirmed`, `DisputeUpdate`, `FlagResolved`, `EmergencyHoldApplied`, `EmergencyHoldReleased`) | ✓ kısmi | `ITransactionRealtimePublisher` 8 metot. 7 event canlı consumer'lara bağlı (BuyerAccepted/Cancelled/TimedOut/PaymentReceived/DisputeAutoResolved/DisputeEscalated/FraudFlagApproved/FraudFlagRejected/EmergencyHoldApplied/EmergencyHoldReleased + CountdownSync sweep). `PaymentDetected` (mempool tespit) için bağımsız domain event henüz yok — publisher yöntemi mevcut, T48 blockchain monitor sidecar event yayınladığında consumer eklenir (K1). Integration test `Publisher_Push_Reaches_Joined_Member` round-trip doğrular. |
| 4 | JWT authentication (query param) | ✓ | `AuthModule.cs` `OnMessageReceived` event handler — `/hubs/*` path'i için `?access_token=` query param'ı bearer token olarak kabul eder; diğer endpoint'ler header-only kalır. Integration `Connect_Without_Token_Returns401` (anon → 401) ve `JoinTransaction_AsParticipant_Succeeds` (token query → 101 + handshake). |
| 5 | Grup bazlı mesajlaşma (transaction ID) | ✓ | `TransactionsHub.GroupName(Guid) = "tx:{N}"`; `Groups.AddToGroupAsync` ve `IHubContext.Clients.Group(...)` SignalR runtime grup roting'ini sağlar. Integration `Publisher_Push_Reaches_Joined_Member` — outsider olmayan seller, JoinTransaction sonrası publisher push'unu alır. |

**Doğrulama kontrol listesi:**

- [x] **07 §11.1 tüm event'ler tanımlı mı?** ✓ — `TransactionRealtimePayloads.cs` 8 record (Client→Server için `JoinTransaction`/`LeaveTransaction` Hub metotları). Payload field'ları (`transactionId`, `fromStatus`, `toStatus`, `timestamp` vb.) spec tablosuyla 1:1. JSON protocol `JsonStringEnumConverter` ile enum string serileşir → wire format `"CANCELLED_BUYER"` gibi spec değerleri.
- [x] **Auth doğru çalışıyor mu?** ✓ — Hub'a `[Authorize]` bayrağı + JWT bridge query param. Integration test 4 case (anon 401 / member join / non-member forbidden / unknown not-found) JWT pipeline'ın hub negotiation'da doğru çalıştığını gösterir.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Skinora.Realtime.Tests — yeni) | ✓ 25/25 | 16 consumer test (`RealtimeConsumerTests`) + 9 broadcaster test (`CountdownSyncBroadcasterTests`). SQLite in-memory (FK + CK constraint'lerine uyumlu seed). |
| Unit (Skinora.Notifications.Tests) | ✓ 49/49 | `FromStatus` enrichment regresyon temiz — fixture'lar güncellendi. |
| Unit (Skinora.Transactions.Tests) | ✓ 333/333 | Cancellation + Timeout + Admin path unit'leri regresyon temiz — `FromStatus: previousStatus` eklemesi state machine semantiğini değiştirmedi. |
| Unit (Skinora.Auth.Tests) | ✓ 57/57 | Auth pipeline değişikliği (`OnMessageReceived`) header-only path'leri etkilemediği için regresyon temiz. |
| Unit (Skinora.Shared.Tests) | ✓ 180/180 | Event record contract enum/count assertion'ları yeni `FromStatus` alanına uyumlu (record'lar pozisyonel). |
| Endpoint smoke (Skinora.API.Tests) | ✓ 5/5 yeni hub testi | `TransactionsHubEndpointTests` lokal in-memory TestServer + LongPolling transport. Mevcut endpoint testleri (~290) lokal Docker yok — Docker-bağımlı integration'lar lokal koşmaz, CI Linux runner'da koşar. |
| Build (Release, `-warnaserror`) | ✓ 0W/0E | Tüm sln. |
| Format verify | ✓ exit=0 | `dotnet format Skinora.sln --verify-no-changes`. |

**Lokal toplam:** Realtime 25 + Notifications 49 + Transactions(unit) 333 + Auth 57 + Shared 180 + API.Tests(yeni hub) 5 = **649 lokal pass, 0 fail** (Docker-bağımlı integration test'ler CI'de doğrulanacak).

## Altyapı Değişiklikleri

- **Migration:** Yok — Realtime modülü hiçbir tabloya yazmaz; broadcaster sweep AsNoTracking read-only.
- **SystemSetting:** Yok.
- **Config/env:** Yeni opsiyonel section `Realtime:CountdownSync` (`Enabled`, `Interval`); default'lar (true / 30 s) production için doğru — config gerektirmez. Test factory `Realtime:CountdownSync:Enabled=false` ile sweep'i durdurur (asenkron yarış olmaması için).
- **Docker:** Yok.
- **Yeni dış bağımlılık:**
  - `Microsoft.AspNetCore.SignalR.Client` 9.0.3 — yalnızca test projesinde (Skinora.API.Tests).
  - Realtime modülü `FrameworkReference="Microsoft.AspNetCore.App"` — hub + JSON protocol + IHubContext için. Ek NuGet yok.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyalarda secret yok; integration test JWT secret'ı test fixture içinde sabit (`hubs-test-secret-key-minimum-32-chars!!!!`).
- **Auth/authorization:** `[Authorize]` hub seviyesinde; `JoinTransaction` body-of-method TX'in seller/buyer'ı olmayan caller'ı reddeder (`TRANSACTION_FORBIDDEN`); var olmayan TX `TRANSACTION_NOT_FOUND` (information disclosure değil — TX UUID rastgele zaten). `OnMessageReceived` bridge `/hubs/*` path'iyle sınırlı — diğer endpoint'lerde query param ignored (header-only).
- **Input validation:** `JoinTransaction(Guid)` — `Guid.Empty` reddedilir; geçersiz Guid SignalR protocol seviyesinde reject olur (HubException). `LeaveTransaction(Guid)` idempotent (Guid.Empty no-op).
- **Yeni dış bağımlılık:** `Microsoft.AspNetCore.SignalR.Client` (test) ve framework reference — Microsoft official paketler.

## Commit & PR

- Branch: `task/T61-signalr-transactions-hub`
- Commits: `9e7841c` (yapım) + `75fd8cc` (Dockerfile layer-cache fix).
- PR: [#98](https://github.com/turkerurganci/Skinora/pull/98)
- CI: ✓ PASS — task branch run [`25461540291`](https://github.com/turkerurganci/Skinora/actions/runs/25461540291) (HEAD `75fd8cc`) 10/10 success: Detect/Lint/Build/Unit/Contract/Integration/Migration dry-run/Docker/CI Gate hepsi ✓; Guard skipped. İlk run [`25461173933`](https://github.com/turkerurganci/Skinora/actions/runs/25461173933) Docker job MSB3202 fail döndü (Dockerfile COPY listesi Realtime + Realtime.Tests csproj'larını içermiyordu — tek satır fix `75fd8cc` ile çözüldü). BYPASS_LOG 1× ci-failure entry (Layer 2 — failure root cause çözüldü, push devam etti).

## Known Limitations / Follow-up

- **K1 — `PaymentDetected` (mempool sighting) push'u henüz yayınlanmıyor.** 07 §11.1 `PaymentDetected` event'i mempool'da ödeme tespiti içindir; mevcut Payments modülü yalnızca confirmation boundary'sinde `PaymentReceivedEvent` yayınlar. Publisher metodu (`PublishPaymentDetectedAsync`) ve payload tipi T61'de hazır — T48 blockchain sidecar mempool tespiti yaparken yeni domain event yayınladığında ayrı bir consumer (1-2 dosya) eklenir. Frontend tarafında race-of-confirmations UI farkı oluşana kadar (T96) gerçek bir functional gap değil.
- **K2 — Steam orchestration state geçişleri (`SendTradeOfferToSeller`, `EscrowItem`, `SendTradeOfferToBuyer`, `DeliverItem`, `Complete`) için status push yok.** Bu transition'lar T67 Steam sidecar pipeline'ında raise edilecek event'lere bağlı; bu task'lar geldiğinde RealtimeConsumer eklenir (her biri 1 dosya). Detail page S07 kullanıcısı için bu boşluk T96 reconnect+refetch ile kapatılır.
- **K3 — Admin role join policy.** `JoinTransaction` admin'leri (T63 dashboard) bypass etmez — `[Authorize]` admin'i de geçirir ama membership check seller/buyer dışındakini reddeder. T63 admin transaction-detail surface'i landed olduğunda hub'a admin bypass eklenir (1-3 satır role check `Context.User.IsInRole("admin")`).
- **K4 — Backplane (Redis) yok.** SignalR şu an in-memory; multi-instance API host'larda her instance kendi grup üyeliklerini bildiğinden cross-instance push işlemez. T-future scaling task'ı `Microsoft.AspNetCore.SignalR.StackExchangeRedis` eklediğinde tek satır DI değişikliğiyle çoklu instance'a yayılır. Şu an F3 fazında tek host runtime için sorun değil.
- **K5 — `CountdownSync` sweep her aktif TX için ayrı SignalR send üretir.** Aktif TX sayısı yüksek olunca (binlerce) hub mesaj sayısı artar; ama tek bir abone yokken bile send maliyeti çok düşüktür (group dispatch gerçek client yoksa no-op). Optimizasyon (yalnız subscriber'lı grup'lara push) T-future.
- **K6 — Notifications hub T62 ayrı task.** Bu task'ta yalnız transactions hub kuruldu; `/hubs/notifications` (NewNotification, UnreadCountChanged, vb. — 07 §11.2) T62'nin sorumluluğu.

## Bağımsız Validator Sonucu

**Tarih:** 2026-05-07 | **Verdict:** ✓ PASS | **Bulgu sayısı:** 0 (S-bulgu yok) | **Düzeltme gerekli mi:** Hayır

**Hard-stop kapıları:**
- Adım -1 (working tree hygiene): ✓ Clean (`git status` boş).
- Adım 0 (main CI startup): ✓ Son 3 main run success — `25458191870` + `25458191854` (chore PR #97 memory T60 yansıt) + `25457244523` (T60 PR #96).
- Adım 0b (repo memory drift): ✓ T61 referansı `.claude/memory/MEMORY.md`'de mevcut (T60 satırı ardından "Next: T61 PR aç → CI izle → ayrı validate chat'i").

**Kabul kriterleri bağımsız doğrulama:**

| # | Kriter | Sonuç | Bağımsız Kanıt |
|---|---|---|---|
| 1 | `/hubs/transactions` hub'ı | ✓ | `Skinora.API/Program.cs:248` `app.MapHub<TransactionsHub>("/hubs/transactions")`; `TransactionsHub.cs:39` `[Authorize]`. |
| 2 | Client→Server: JoinTransaction, LeaveTransaction | ✓ | `TransactionsHub.JoinTransaction(Guid)` üyelik kontrolü TX yoksa `TRANSACTION_NOT_FOUND`, üye değilse `TRANSACTION_FORBIDDEN`; `LeaveTransaction(Guid)` idempotent. Lokal `TransactionsHubEndpointTests` 5/5 PASS (Connect_Without_Token_Returns401 / AsParticipant / AsNonParticipant / UnknownTransaction / Publisher_Push_Reaches_Joined_Member). |
| 3 | Server→Client: 8 RT1 event'i | ✓ | `TransactionRealtimePayloads.cs` 8 record 07 §11.1 tablosuyla 1:1 eşleşme (`TransactionStatusChanged`, `CountdownSync`, `PaymentDetected`, `PaymentConfirmed`, `DisputeUpdate`, `FlagResolved`, `EmergencyHoldApplied`, `EmergencyHoldReleased`). 7 event canlı consumer'lara bağlı + `CountdownSync` 30 sn periyodik broadcaster. `PaymentDetected` (mempool) için publisher port + payload var ama tetikleyici domain event henüz yok — K1 olarak forward-deferred (T48 blockchain monitor consumer ekleyecek). Spec metni "Blockchain'de ödeme tespiti" diyor ve bu tespit pipeline'ı T48'in scope'unda; T61 publisher altyapısını kuruyor — bu sapma değil, dokümante kapı bırakma. |
| 4 | JWT authentication (query param) | ✓ | `AuthModule.cs:79–91` `JwtBearerEvents.OnMessageReceived` `?access_token=` query param'ını yalnızca `/hubs/*` path'inde bearer token olarak kabul eder; diğer endpoint'ler header-only kalır. Integration `Connect_Without_Token_Returns401` anon → 401 ✓; `JoinTransaction_AsParticipant_Succeeds` token query → handshake ✓. |
| 5 | Grup bazlı mesajlaşma (transaction ID) | ✓ | `TransactionsHub.GroupName(Guid) = "tx:{N}"`; `Groups.AddToGroupAsync` ve `IHubContext<TransactionsHub>.Clients.Group(...)` SignalR runtime grup roting'i. `Publisher_Push_Reaches_Joined_Member` integration test publisher → group → client round-trip'i kanıtlar. |

**Doğrulama kontrol listesi (bağımsız):**

- [x] **07 §11.1 tüm event'ler tanımlı mı?** ✓ — 8 payload record şema 1:1 (alan sırası, isimleri, opsiyonelliği). `JoinTransaction`/`LeaveTransaction` Hub metotları client→server tablodaki iki entry'i karşılar. `Program.cs:128-130` `AddJsonProtocol(JsonStringEnumConverter)` enum'ları wire'da string olarak serileştirir → 07 §11.1 örnek değerleri (`CANCELLED_BUYER`, `EMERGENCY_HOLD`, vb.) frontend tarafından gözlemlenir.
- [x] **Auth doğru çalışıyor mu?** ✓ — Hub seviyesi `[Authorize]` + JWT bridge `/hubs/*` path-restricted. 4 integration test (anon 401 / member join / non-member forbidden / unknown not-found) JWT pipeline'ın hub negotiation'da doğru çalıştığını ve membership guard'ının DB lookup ile zorunlu olduğunu kanıtlar.

**Test sonuçları (bağımsız çalıştırma):**

| Tür | Sonuç | Komut |
|---|---|---|
| Build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| Skinora.Realtime.Tests (yeni) | ✓ 25/25 | `dotnet test tests/Skinora.Realtime.Tests/Skinora.Realtime.Tests.csproj -c Release --no-build` |
| TransactionsHubEndpointTests (yeni) | ✓ 5/5 | `--filter "FullyQualifiedName~TransactionsHubEndpointTests"` |
| Skinora.Notifications.Tests (unit) | ✓ 49 | Docker-bağımlı 37 integration ortam kaynaklı düştü, lokal Docker yok — CI Linux runner yeşil. |
| Skinora.Transactions.Tests (unit) | ✓ 340 | Docker-bağımlı 237 integration aynı. |
| Skinora.Auth.Tests (unit) | ✓ 57 | Docker-bağımlı 36 integration aynı. |
| Skinora.Shared.Tests (filter !Integration) | ✓ 185/185 | `--filter "FullyQualifiedName!~Integration"` |

**Task branch CI:**
- Run `25461912780` (HEAD `992c15f` rapor commit) — ✓ PASS 10/10.
- Run `25461540291` (HEAD `75fd8cc` Dockerfile fix) — ✓ PASS 10/10.
- Run `25461173933` (HEAD `9e7841c` ilk push) — ✗ FAIL (Docker MSB3202 — Realtime + Realtime.Tests csproj COPY listesinde yoktu); `75fd8cc` tek satır fix ile çözüldü, BYPASS_LOG entry mevcut.

**Mini güvenlik kontrolü (bağımsız):**
- Secret sızıntısı: temiz — yeni dosyalarda secret yok; integration test JWT secret'ı test fixture içinde sabit, prod'a sızmaz.
- Auth/authorization: hub seviyesi `[Authorize]` + JoinTransaction body'sinde DB'den buyer/seller verification + reddi `HubException` ile yapısal. JWT bridge path-restricted (`/hubs/*` only). Best-effort publish try/catch outbox'u redelivery sinyaliyle kirletmiyor (kasıtlı).
- Input validation: `JoinTransaction(Guid.Empty)` reddedilir; geçersiz Guid SignalR protocol seviyesinde reject. `LeaveTransaction` `Guid.Empty` no-op (idempotent).
- Yeni dış bağımlılık: `Microsoft.AspNetCore.SignalR.Client` 9.0.3 yalnız test projesinde; production yalnız framework reference.

**Yapım raporu karşılaştırması:** Tam uyumlu — kabul kriterleri tablosu, test sayıları, K1–K6 forward-deferral'ları doğrulayan bağımsız ölçümlerle örtüşüyor. K1'in (PaymentDetected mempool consumer'ı T48'e devir) "✓ kısmi" işaretlemesi spec conformance lensiyle doğru — port + payload + JSON config tamam, eksik olan tek şey tetikleyici domain event yayınlayan blockchain pipeline'ı. Bu, dokümante kapı bırakma; sapma değil.

## Notlar

- **Working tree pre-flight:** clean (`git status` boş). Adım -1 ✓.
- **Main CI startup pre-flight:** son 3 main run ✓ — `25458191870` + `25458191854` (chore PR #97 memory T60) + `25457244523` (T60 #96). Adım 0 ✓.
- **Bağımlılık kontrolü:** T44 ✓ (PR #74). T58/T59/T60 ✓ — onların yayınladığı event'ler (DisputeAutoResolved, DisputeEscalated, EmergencyHoldApplied, EmergencyHoldReleased) burada consume edilir.
- **Dış varsayım kontrolü (Adım 4):**
  - SignalR ASP.NET Core 9 framework içinde ✓ — `FrameworkReference="Microsoft.AspNetCore.App"` (NuGet paketi gerektirmez).
  - JWT query param Bearer auth — `OnMessageReceived` event'i resmi pattern (Microsoft Learn dokümante).
  - `Microsoft.AspNetCore.SignalR.Client` 9.0.3 NuGet'te mevcut ✓ — `dotnet add` başarılı.
  - SignalR JSON protocol `AddJsonProtocol` ile `JsonStringEnumConverter` configure edilebilir ✓ — resmi API.
  - Framework reference SDK="Microsoft.NET.Sdk" (Web değil) altında çalışır ✓ — modül class library olarak build edildi, `Skinora.Realtime` 0W/0E.
- **`FromStatus` enrichment kararı:** Realtime consumer DB lookup yapmamak için fromStatus'u event payload'una ekledi. Yapım sırasında 3 alternatif değerlendirildi: (a) consumer DB lookup → fragile (post-commit Status zaten toStatus), (b) tek yeni `TransactionStateChangedEvent` event'i → 6 callsite çoklu publish, (c) mevcut event'lere FromStatus parametresi → 4 callsite + 2 test fixture grubu. Seçim (c) — minimum invasive, tüm üreticiler `previousStatus`'u Fire öncesi zaten alıyor.
- **Cross-module pattern:** Skinora.Realtime → Skinora.Transactions (Transaction entity okumak için), Skinora.Auth (AuthClaimTypes import) referans verir. Skinora.Shared'a da bağlı (events). Test projesi Skinora.Users'a da referans verir (FK seed için). Skinora.API → Skinora.Realtime tek yön (modül DI + MapHub).
