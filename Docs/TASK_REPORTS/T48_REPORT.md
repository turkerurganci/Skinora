# T48 — Timeout warning

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-05-02

---

## Yapılan İşler

T47'nin `IWarningDispatcher` portunda forward-deferred bırakılan stub gerçek implementasyona DI swap edildi. Per-transaction Hangfire warning job'u tetiklendiğinde:

1. `WarningDispatcher` (`Skinora.Transactions/Application/Timeouts/WarningDispatcher.cs`) atomik bir transaction içinde:
   - 09 §13.3 no-op guard'larını uygular (state ≠ ITEM_ESCROWED, IsOnHold, TimeoutFrozenAt, TimeoutWarningSentAt, BuyerId null, PaymentDeadline geçmiş → silent no-op).
   - `Transaction.TimeoutWarningSentAt = UtcNow` damgalar.
   - `TimeoutWarningEvent` (yeni `Skinora.Shared/Events/TimeoutWarningEvent.cs`) outbox'a publish eder (RecipientUserId = BuyerId; RemainingMinutes = `Math.Floor((PaymentDeadline − UtcNow).TotalMinutes)`).
   - Tek `SaveChangesAsync` çağrısı ile damga + outbox satırı tek transaction'da commit olur.
2. `TimeoutWarningNotificationConsumer` (`Skinora.Notifications/Application/EventHandlers/`) — `NotificationConsumerBase<TimeoutWarningEvent>` türevi:
   - ConsumerName: `notifications.timeout-warning`
   - Olayı tek bir `NotificationRequest{Type=TIMEOUT_WARNING, UserId=RecipientUserId, TransactionId, Parameters={ItemName, RemainingMinutes}}` haline çevirir.
   - `INotificationDispatcher` (T37) buradaki istemi alıp platform-in-app `Notification` satırı + her enabled external kanal için `NotificationDelivery` satırı yazar; `NotificationDeliveryJob`'lar Hangfire'a enqueue edilir → email/Telegram/Discord fan-out'u T78–T80 channel handler swap'larıyla devreye girer.
3. `TIMEOUT_WARNING` template'i (`NotificationTemplates.tr.resx`) zaten T37'de tanımlıydı: "Süre dolmak üzere" başlığı + `{RemainingMinutes}` placeholder.
4. MediatR scan asembly listesi (`OutboxModule.GetMediatRScanAssemblies`) `Skinora.Notifications.NotificationsModule.Assembly`'i içerecek şekilde genişletildi — T48 onwards consumer'ların production handler bulunabilir olması için.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Skinora.Shared/Events/TimeoutWarningEvent.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/WarningDispatcher.cs`
- `backend/src/Modules/Skinora.Notifications/Application/EventHandlers/TimeoutWarningNotificationConsumer.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/WarningDispatcherTests.cs`
- `backend/tests/Skinora.Notifications.Tests/Unit/TimeoutWarningNotificationConsumerTests.cs`

**Değişiklik:**
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutExecutor.cs` — `StubWarningDispatcher` sınıfı kaldırıldı.
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — `IWarningDispatcher` kaydı `StubWarningDispatcher` → `WarningDispatcher` swap.
- `backend/src/Skinora.API/Outbox/OutboxModule.cs` — MediatR scan listesi Notifications module assembly'sini içerecek.
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutExecutorTests.cs` — Stub'a referans veren 2 warning testi kaldırıldı, test sınıfı docstring'i `WarningDispatcherTests` referansını içerir.

**Diff:** 4 değişiklik + 5 yeni dosya. `git diff --stat main`: 13 insertions / 91 deletions değiştirilenlerde + ~370 satır yeni dosya.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Timeout süresi dolmadan önce uyarı (admin tarafından ayarlanabilir oran) | ✓ | Eşik T47'nin `TimeoutSchedulingService.SchedulePaymentTimeoutAsync`'inde `timeout_warning_ratio` SystemSetting değerine göre Hangfire delayed job olarak schedule ediliyor (T47 PR #77, `SchedulePaymentTimeout_Schedules_Both_Payment_And_Warning_Jobs` testi 0.75 × 60 = 45 dk hesaplamayı doğruluyor). T48 bu job tetiklendiğinde gerçek dispatch'i yapan handler'ı bağladı. |
| 2 | TimeoutWarningEvent üretimi | ✓ | `WarningDispatcher.DispatchWarningAsync` `IOutboxService.PublishAsync(new TimeoutWarningEvent(...))` çağırır. Test: `WarningDispatcherTests.DispatchWarning_Stamps_SentAt_And_Publishes_Event` event publish'ini ve alan değerlerini (TransactionId/RecipientUserId/ItemName/RemainingMinutes/OccurredAt) doğrular. |
| 3 | Bildirim: ilgili tarafa tüm kanallarda "süreniz dolmak üzere" | ✓ | `TimeoutWarningNotificationConsumer` event'i `NotificationRequest{UserId=BuyerId, Type=TIMEOUT_WARNING}` haline çevirir; T37 `NotificationDispatcher` platform-in-app row + her enabled `UserNotificationPreference` için `NotificationDelivery` row + `NotificationDeliveryJob` enqueue eder (kanallar T78–T80 ile EMAIL/TELEGRAM/DISCORD swap'ları üzerinden gerçeklenmektedir; in-app her zaman aktif). Test: `TimeoutWarningNotificationConsumerTests.Handle_TranslatesEventToTimeoutWarningRequest` request shape'ini doğrular. Türkçe template: `NotificationTemplates.tr.resx` `TIMEOUT_WARNING_Title` = "Süre dolmak üzere". |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Notifications consumer) | ✓ 3/3 | `TimeoutWarningNotificationConsumerTests` — request shape, idempotency replay, ProcessedEvent consumer name. `dotnet test tests/Skinora.Notifications.Tests`. |
| Integration (WarningDispatcher) | ✓ 8/8 | `WarningDispatcherTests` — happy path (stamp + event publish), 6 no-op guard (already sent, state advanced, frozen, on-hold, deadline passed, transaction missing), RemainingMinutes floor. `dotnet test tests/Skinora.Transactions.Tests`. |
| Regresyon — tüm test asembly'leri | ✓ 1224/1224 | Skinora.Users 16 + Fraud 17 + Payments 6 + Disputes 11 + Steam 21 + Shared 182 + Platform 136 + Transactions 391 + API 265 + Auth 93 + Notifications 66 + Admin 20 = 1224. T47'nin 1215 → T48 1224 (+9 = 8 yeni dispatcher + 3 yeni consumer − 2 silinen stub). |
| Build (Release) | ✓ 0W/0E | `dotnet build -c Release` — `Build succeeded. 0 Warning(s) 0 Error(s)`. |
| Format verify | ✓ exit=0 | `dotnet format --verify-no-changes` clean. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (bağımsız validate chat'i) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok — `TimeoutWarningSentAt`/`TimeoutWarningJobId` alanları T19'dan beri var, `timeout_warning_ratio` SystemSetting'i T26 seed'inde mevcut.
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni dış bağımlılık:** Yok (MediatR + Hangfire + Outbox infra T10/T37'den var).

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyaların hiçbiri secret içermiyor.
- **Auth/authorization etkisi:** Yok — yeni endpoint yok; arka plan iş hattı.
- **Input validation:** İşlem yok — event payload'u tamamen sunucu kontrolünde (Transaction snapshot + clock); kullanıcı girdisi yok.
- **PII fan-out:** RemainingMinutes ve ItemName template'e geçiyor. ItemName Steam item adı (PII değil); buyer'a gönderilen bildirim sadece "süreniz dolmak üzere" tipinde, ek hassas veri yok. Notification body T37 tarafından `Notification` row'a string olarak yazılır; T63b retention job (kullanıcı silimde anonimize) zaten `NotificationAccountAnonymizer` ile kapsanmış.

## Commit & PR

- Branch: `task/T48-timeout-warning`
- Commit: `9d56719` — `T48: Timeout warning (02 §3.4, 05 §4.4)`
- PR: [#78](https://github.com/turkerurganci/Skinora/pull/78)
- CI: ⏳ İzleniyor (sonuç eklenecek)

## Known Limitations / Follow-up

- **Email/Telegram/Discord channel handler'ları stub** — T37 ship edilen `EmailNotificationChannelHandler`/`TelegramNotificationChannelHandler`/`DiscordNotificationChannelHandler` halen log-only stub. Gerçek HTTP send T78/T79/T80'de devralacak. T48 yalnızca **dispatch yolunu** bağlıyor (stub handler'lar `NotificationDelivery` row'u SUCCESS işaretler veya log'lar — fan-out kontratı sağlam, gerçek SDK çağrıları forward-defer).
- **SignalR push** — Platform-in-app realtime push T62'de eklenecek; T48 `Notification` row'u yazıyor, T62 onu hub üzerinden client'a iletecek. T46 raporundaki notification fan-out devir notu ile aynı paralelde.
- **Diğer aşamalar (Accept / TradeOfferToSeller / TradeOfferToBuyer) için warning** — Spec (06 §3.5 + 09 §13.3) "warning yalnız ITEM_ESCROWED ödeme aşaması için" diyor; per-tx Hangfire job'u sadece ödeme adımında çalışıyor (diğer aşamalar `DeadlineScannerJob` ile poller-based). Bu task scope'u dışında, mevcut tasarım korunur.

## Notlar

- **Working tree pre-flight:** temiz (`git status --short` boş çıktı).
- **Main CI startup pre-flight:** son 3 main run ✓ — 25255400779 (T47 #77), 25255400763 (T47 #77), 25252671939 (T46 #76).
- **Dış varsayım:** yok — internal task, no paid features, no external SDK, all infrastructure already exists from T10/T19/T26/T37/T47.
- **Dependency check:** T47 ✓ (PR #77 squash `e00f97a`) + T37 ✓ (PR pending squash `b383983`+`7767fc7`).
- **Mimari karar — neden outbox event yerine inline dispatch değil?** T37 `NotificationConsumerBase` docstring'i "T44+ event handlers için recommended pattern" diyor; T45 (`TransactionCreatedEvent`) + T46 (`BuyerAcceptedEvent`) zaten aynı pattern'i kullanıyor. Inline dispatch consumer-idempotency contract'ını (replayed Hangfire job → çift bildirim) atlar; outbox + ProcessedEvent kombinasyonu 09 §9.3 garanti eder. Üstelik 05 §4.4 spec'i `TimeoutWarningEvent` üretimini explicit talep ediyor.
- **`CK_Transactions_FreezePassive` invariantı:** Dispatcher `TimeoutFrozenAt is not null` durumunda no-op çıkar — bu "freeze sırasında damga atma" guard'ı; freeze yaşam döngüsü T50'nin sorumluluğu, T48 sadece freeze görüldüğünde geri çekilir.
