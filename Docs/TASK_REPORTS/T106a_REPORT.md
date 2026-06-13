# T106a — Escrow Trade-Offer Dispatch Engine

**Faz:** F5/F6 sınırı (T69-K1 resmileştirme) | **Durum:** ✓ Tamamlandı — bağımsız validator PASS | **Tarih:** 2026-06-13

---

## Bağlam

T103b ("Steam hesapları backend tamamlama") 2026-06-13'te Option C ile ertelendi. "Task 103b" yeniden ele alınırken yapılan kod incelemesi, ertelemenin (a) ön-koşulunun — "escrow akışına bot-atama wiring'i" — aslında **T69-K1 dispatch caller'ı** olduğunu ortaya çıkardı: T69 raporu (2026-05-16) bunu açıkça "plan'da ayrı bir task olarak tanımsız, T-future devir" olarak proje sahibi onayıyla ertelemişti. Doğrulama (iki çok-ajanlı workflow + manuel kod okuma) gösterdi ki **escrow→sidecar trade-offer dispatch motoru tümüyle kurulmamış**:

- `SendTradeOfferToSeller` / `SendTradeOfferToBuyer` state-machine trigger'ları **production'da hiçbir yerden fire edilmiyor** (yalnız test + state-machine tanımı).
- Backend'de sidecar `POST /api/trade-offers/send`'i çağıran **hiçbir client yok** (yalnız envanter-okuma client'ı vardı).
- `Transaction.EscrowBotId` / `EscrowBotAssetId` / `DeliveredBuyerAssetId` hiç set edilmiyor; `PlatformSteamBot.ActiveEscrowCount` ölü sayaç.
- `trade_offer.accepted` payload'u **asset id taşımıyordu** → `ITEM_ESCROWED` guard'ı (`EscrowBotAssetId` zorunlu) hiçbir zaman geçemiyor; `DomainException` yutuluyor → işlem sessizce takılıyor.
- Webhook `direction` sözleşmesi uyuşmazlığı: sidecar `SELLER_TO_BOT/...` yayınlıyor, backend `escrow/delivery` bekliyordu (hiç dispatch olmadığı için yüzeye çıkmamıştı).

Proje sahibi kararı (3 tur AskUserQuestion, 2026-06-13): **T106a olarak tanımla + tasarla + uygula**; kapsam = **3 yön (escrow + delivery + refund) + sidecar değişiklikleri dahil**; tetikleyici = **Hangfire per-minute scan**.

## Yapılan İşler

**Backend — `Skinora.Steam`**
- `ITradeOfferDispatchClient` + `HttpTradeOfferDispatchClient` — sidecar `POST /api/trade-offers/send` portu (envanter client deseninde; aynı `SteamSidecarOptions`, `X-Internal-Key`, daha uzun timeout). HTTP→sonuç: 200 sent/confirmed→Sent, 200 pending→Pending, 502→Failed(retryable), 400→Failed(non-retryable), 503/5xx/transport→Unavailable.
- `SteamConstants` (Cs2AppId=730, Cs2ContextId="2").
- `TradeOfferDispatchJob` (Hangfire recurring `* * * * *`, `OutgoingTransferDispatchJob` deseninde): **escrow bacağı** (`ACCEPTED` → `SelectAsync` ile bot seç + `EscrowBotId` persist + `SendTradeOfferToSeller` fire + `SELLER_TO_BOT` POST, botAccountName hint ile) ve **delivery bacağı** (`PAYMENT_RECEIVED` → aynı escrow botunu yeniden kullan + `SendTradeOfferToBuyer` fire + `BOT_TO_BUYER` POST). Idempotency = (a) state flip + (b) yön bazlı `TradeOffer` satır varlığı. Failed→FAILED satır + `TradeOfferDispatchFailedEvent`; Unavailable→sonraki tick'te retry (deadline ile sınırlı).
- `TradeOfferDispatchJobRegistrar` (IHostedService).
- `ItemRefundDispatchConsumer` (`INotificationHandler<ItemRefundToSellerRequestedEvent>`) — **refund bacağı**: timeout/user-cancel/admin-cancel'ın yayınladığı event'i tüketir, escrowlanmış işlem için `BOT_TO_SELLER_REFUND` dispatch eder; transient→throw (outbox retry), kalıcı→log.
- `SteamWebhookHandler`: (1) `ParseDirection` artık sidecar sözlüğünü (`SELLER_TO_BOT/BOT_TO_BUYER/BOT_TO_SELLER_REFUND`) çözer; (2) `HandleAcceptedAsync` yeniden yazıldı — 3 yön: escrow (`EscrowBotAssetId` set + `ActiveEscrowCount` **+1** + `EscrowItem`), delivery (`DeliveredBuyerAssetId` set + **−1** + `DeliverItem`), refund (**−1**, trigger yok — işlem terminal); asset id yoksa **sessizce ilerlemez, log + ack**; (3) `HandleFailedAsync` idempotency guard (aynı yön için FAILED satır varsa dedupe).
- `TradeOfferDispatchFailedEvent` (Shared.Events, `TransferDispatchFailedEvent` deseninde).
- `TradeOfferEventData`: `ReceivedAssetId` / `DeliveredAssetId` eklendi. DI: `SteamModule` (HttpClient + client + job + registrar + refund consumer); `Skinora.Steam.csproj` → `Skinora.Users` referansı.

**Sidecar — `sidecar-steam`**
- `SendTradeOfferRequest.botAccountName?` hint (`types.ts` + `routes.ts` parse); `BotManager.selectBot(preferredAccountName?)` — READY ise tercih edileni döner, değilse round-robin'e düşer; `TradeOfferService.sendOffer` hint'i geçirir.
- `TradeOfferMonitor`: Accepted (state 3) olduğunda `TradeOffer.getExchangeDetails` ile `receivedAssetId`/`deliveredAssetId` çekilip `trade_offer.accepted` payload'una eklenir; fetch hatası → asset id'siz yayınla (backend log+ack, sessiz takılma yok). `steam-tradeoffer-manager.d.ts` `getExchangeDetails` + `ExchangeDetailsItem` ile genişletildi.

## Etkilenen Modüller / Dosyalar

**Yeni (backend):** `Skinora.Steam/Application/Dispatch/{SteamConstants, ITradeOfferDispatchClient, HttpTradeOfferDispatchClient, TradeOfferDispatchJob, TradeOfferDispatchJobRegistrar, ItemRefundDispatchConsumer}.cs`, `Skinora.Shared/Events/TradeOfferDispatchFailedEvent.cs`. **Yeni (test):** `Skinora.Steam.Tests/Unit/HttpTradeOfferDispatchClientTests.cs`, `Skinora.Steam.Tests/Integration/{TradeOfferDispatchJobTests, ItemRefundDispatchConsumerTests}.cs`.
**Değişen (backend):** `SteamWebhookHandler.cs`, `SteamWebhookPayloads.cs`, `SteamModule.cs`, `Skinora.Steam.csproj`. **Değişen (test):** `SteamWebhookHandlerTests.cs` (+5 test, direction literal), `SteamWebhookEndpointTests.cs` (direction literal).
**Değişen (sidecar):** `trade/types.ts`, `trade/TradeOfferService.ts`, `trade/TradeOfferMonitor.ts`, `bot/BotManager.ts`, `api/routes.ts`, `webhook/WebhookPayloads.ts`, `types/steam-tradeoffer-manager.d.ts` (+ `TradeOfferMonitor.test.ts`, `BotManager.test.ts`).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| AC1 | Escrow dispatch: bot seç + `EscrowBotId` persist + `SendTradeOfferToSeller` + `SELLER_TO_BOT` POST (atomik) | ✓ | `TradeOfferDispatchJobTests.Escrow_HappyPath_AssignsBotAndAdvances` (request yön/partner/asset/bot-hint doğrular) |
| AC2 | Delivery dispatch: escrow botunu yeniden kullan + `SendTradeOfferToBuyer` + `BOT_TO_BUYER` POST | ✓ | `Delivery_HappyPath_ReusesEscrowBotAndAdvances` |
| AC3 | Asset-id yakalama → guard'lar geçer, sessiz takılma yok | ✓ | `TradeOfferAccepted_Escrow_SetsAssetIdFromPayloadAndIncrementsCount`, `..._MissingAssetId_DoesNotAdvance`, sidecar `TradeOfferMonitor` asset-id testleri |
| AC4 | `ActiveEscrowCount` +1 escrow / −1 delivery & refund, negatif olmaz, yalnız backend yazar | ✓ | webhook accepted (escrow/delivery/refund) testleri sayaç assert eder |
| AC5 | Refund bacağı `RETURN_TO_SELLER` dispatch eder | ✓ | `ItemRefundDispatchConsumerTests.EscrowedTransaction_DispatchesReturnToSeller` (+ NotEscrowed/Idempotent/Unavailable) |
| AC6 | Transient retry / kalıcı hata event'i | ✓ | `Escrow_FailedResponse_RecordsFailedRowAndPublishesEvent`, `Escrow_Unavailable_LeavesForRetry`, `HttpTradeOfferDispatchClientTests` (502/400/503/transport) |
| AC7 | Idempotency (replay / mevcut offer / çift FAILED) | ✓ | `Escrow_ExistingOffer_DoesNotDispatch`, `StatusChange_SameStatusReplay_IsIdempotent`, `TradeOfferFailed_DuplicateForDirection_IsIdempotent`, `ItemRefund...AlreadyDispatched_Idempotent` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend unit (dispatch client) | ✓ 7/7 | `dotnet test --filter HttpTradeOfferDispatchClientTests` |
| Backend integration (Steam.Tests) | ✓ 76/76 | dispatch job (6) + refund consumer (4) + webhook (5 yeni) + mevcutlar; gerçek SQL Server |
| Backend integration (API webhook endpoint) | ✓ 6/6 | `SteamWebhookEndpointTests` (direction reconciliation dahil) |
| Sidecar (vitest) | ✓ 145/145 | TradeOfferMonitor asset-id (3 yeni) + BotManager hint (2 yeni) dahil |
| Backend build | ✓ | `dotnet build Skinora.API` 0 hata; `dotnet format --verify-no-changes --severity error` exit 0 |
| Sidecar typecheck | ✓ | `tsc --noEmit` temiz |

## Doğrulama

**Bağımsız validator (ayrı chat, 2026-06-13) — VERDICT: ✓ PASS.** Yapan ≠ denetleyen; rapor okunmadan önce bağımsız verdict üretildi, sonra karşılaştırıldı (tam uyumlu).

| Alan | Sonuç |
|---|---|
| Adım -1 Working tree | ✓ Temiz (`git status --short` boş) |
| Adım 0 Main CI (son 3) | ✓ success — `27471077378` / `27471077361` / `27468789964` |
| Adım 0b Repo memory drift | ✓ T106a satırı mevcut (`.claude/memory/MEMORY.md`) |
| Task branch CI (#166, run `27475659788`) | ✓ Tüm job'lar success: Lint / Build / Unit / Integration / **Contract** / **Migration dry-run** / Docker×2 / Gate |
| Backend Steam.Tests | ✓ 76/76 (validator yeniden çalıştırdı, gerçek SQL Server, 49 s) |
| Backend API webhook endpoint | ✓ 6/6 (`SteamWebhookEndpointTests`) |
| Sidecar (vitest) | ✓ 145/145 + `tsc --noEmit` temiz (validator yeniden çalıştırdı) |
| Kabul kriterleri (plan 6 madde) | ✓ Hepsi kanıtlı (escrow/delivery/refund + asset-id + sayaç ±1 + transient/idempotency) |
| Direction sözleşmesi (backend↔sidecar↔webhook) | ✓ `SELLER_TO_BOT`/`BOT_TO_BUYER`/`BOT_TO_SELLER_REFUND` üç katmanda birebir (önceki kırık seam onarıldı) |
| Güvenlik | ✓ Temiz — secret yok; auth `X-Internal-Key` mevcut options'tan; yeni dış bağımlılık yok; migration yok; csproj yalnız iç `Skinora.Users` referansı |
| Mimari sapma (Steam'e yerleşim) | Kabul — cycle önleme; plan dokümanları (05 §3.2) modülü pinlemiyor; bloke-edici değil |
| Bulgular | 0 bloke-edici (S1/S2/S3 yok). K1–K5 owner-onaylı forward-deferral. |
| Yapım raporu karşılaştırması | Tam uyumlu (AC tablosu plan'ı yeniden sıralıyor ama 6 kriterin hepsi kapsanmış — kozmetik) |

## Altyapı Değişiklikleri

- **Migration:** Yok — `EscrowBotId`/`EscrowBotAssetId`/`DeliveredBuyerAssetId`/`ActiveEscrowCount` zaten mevcut; retry/idempotency mevcut `TradeOffer` satırları üzerinden (yeni kolon yok).
- **Config/env:** Yeni section yok — dispatch client mevcut `SteamSidecar` options'ını paylaşır.
- **Docker:** Değişiklik yok.
- **Yeni dış bağımlılık:** Yok.

## Commit & PR

- Branch: `task/T106a-escrow-dispatch-engine`
- Commit: `252185a` — feat(T106a): escrow trade-offer dispatch engine (T69-K1)
- PR: [#166](https://github.com/turkerurganci/Skinora/pull/166)
- CI: ⏳ Claude izliyor ([[feedback_claude_watches_ci_always]])

## Known Limitations / Follow-up

- **K1 — Webhook-doğrudan-decline refund yayınlamıyor:** Bir alıcı delivery offer'ını Steam'de doğrudan reddederse (`trade_offer.declined`, TO_BUYER) işlem `CANCELLED_BUYER`'a geçer ama `ItemRefundToSellerRequestedEvent` yayınlanmaz → item bot'ta kalır, sayaç düşmez. Baskın iptal yolları (timeout-scanner / user-cancel / admin-cancel) event'i yayınladığı için refund bacağı onlarda çalışır. Bu, K2 iptal-orkestrasyonu kapsamı (webhook handler'ın kendi K-not'u, `SteamWebhookHandler` "full cancellation orchestration → T69").
- **K2 — Refund offer seller'ca reddedilirse:** `RETURN_TO_SELLER` offer'ı seller reddeder/expire olursa item bot'ta kalır + sayaç düşmez (manuel/recovery — T103b-2/-3 recovery domain'i).
- **K3 — `getExchangeDetails` gerçek-Steam güvenilirliği:** Sidecar asset-id çekimi yalnız mock'la doğrulandı; gerçek Steam davranışı (gecikme/hata modları) **T107 E2E (staging)** ile doğrulanacak. Hata durumunda backend ilerletmez (log+ack) — sessiz takılma yok ama otomatik telafi yok.
- **K4 — `TradeOfferDispatchFailedEvent` admin-alert tüketicisi yok:** Event yayınlanır (outbox) ama notification consumer'ı yok (`TransferDispatchFailedEvent` emsali — yalnız yayınla). Admin alert ayrı notification task'ında.
- **K5 — Çift trade offer kenar durumu:** POST başarılı ama `SaveChanges` çökerse (nadir), bir sonraki tick'te `trade_offer.sent` webhook'u henüz inmemişse yeniden POST → çift offer riski. Blockchain dispatcher ile aynı sınıf kenar durum; generous timeout + state-flip ile hafifletildi. Sidecar idempotency anahtarı (transactionId) ileri-devir.

## Notlar

- **Working tree:** Session başında temiz.
- **Main CI (Adım 0):** Son 3 run `success` (27471077378 / 27471077361 / 27468789964).
- **Dış varsayımlar:** sidecar `POST /api/trade-offers/send` mevcut (routes.ts doğrulandı); `IBotSelectionService.SelectAsync` mevcut + test edilmiş (T69); `User.SteamId` mevcut; `Skinora.Steam`→`Skinora.Users` referansı eklendi (transitif zaten vardı).
- **Mimari sapma (tasarımdan):** Dispatch motoru `Skinora.Steam`'e konuldu (tasarım "Transactions" demişti) — `Transactions`, `Steam`'i referans edemez (cycle); `Steam` zaten `Transactions`'ı referans eder ve `IBotSelectionService` + sidecar client'lar Steam'de. Direction reconciliation salt-backend yapıldı (sidecar zaten kendi sözlüğünü yayınlıyordu). Sayaç +1 ITEM_ESCROWED'da (dispatch'te değil) → pre-escrow decline decrement kenar durumu elenir.
- **T103b durumu:** Bu task pre-koşul (a)'yı kapatır. (b) recovery/failover spec'i + recovery queue domain'i = **T103b-2 (discovery) / T103b-3 (impl)** olarak ertelenmiş kalır.
