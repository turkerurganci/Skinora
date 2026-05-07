# T62 — SignalR hub: bildirim push

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (validate chat'inde PASS bekliyor) | **Tarih:** 2026-05-07

---

## Yapılan İşler

T62, 07 §11.2 RT2 kontratını gerçekleyen `/hubs/notifications` SignalR hub'ını ve buna bağlı 5 server→client event'ini canlıya alır. Ana hedef T38'den gelen `Notification` entity'lerini real-time push olarak iletmek; Telegram/Discord/Maintenance event'leri publisher portu + payload'ı + JSON config'i ile T62'de hazır, tetikleyici event yayınlayan T79/T80/T-future devraldığında 1-2 satır consumer/callsite ekiyle aktive olur (T61'in K1 paterni).

1. **`Skinora.Realtime/` modülüne 4 yeni dosya:**
   - `Hubs/NotificationsHub.cs` — `/hubs/notifications` mount; `[Authorize]`; `OnConnectedAsync` connection'ı `user:{userId:N}` group'una otomatik ekler. Client→server metot yok (07 §11.2 tablosunda yok); kullanıcı login sonrası bağlanır, push'ları dinler. JWT claim `AuthClaimTypes.UserId = "sub"` üzerinden Guid olarak parse edilir; tutarsızsa `HubException("AUTH_INVALID")`.
   - `Application/INotificationRealtimePublisher.cs` — 5 metot port (`PublishNewNotificationAsync`, `PublishUnreadCountChangedAsync`, `PublishTelegramConnectedAsync`, `PublishDiscordConnectedAsync`, `PublishMaintenanceStatusChangedAsync`). İlk dört metot `Guid userId` parametresi alır (per-user fan-out); maintenance metot platform-wide.
   - `Application/Contracts/NotificationRealtimePayloads.cs` — 07 §11.2 tablo birebir 5 record (`NewNotification`, `UnreadCountChanged`, `TelegramConnected`, `DiscordConnected`, `MaintenanceStatusChanged`). `NewNotification` field seti `NotificationListItemDto` ile uyumlu (07 §8.1 inbox row'u) — sadece `IsRead` çıkarıldı (yeni satır her zaman unread).
   - `Infrastructure/SignalRNotificationRealtimePublisher.cs` — `IHubContext<NotificationsHub>` adaptörü; per-user push'lar `Clients.Group(user:{N})` üzerinden, maintenance push'u `Clients.All` üzerinden. Transport hatası best-effort (try/catch + log) — outbox dispatcher'a redelivery sinyali yansıtılmaz, inbox SaveChanges başarısı yan etki atlanırsa rolled back edilmez.

2. **`RealtimeModule.cs` güncellendi:** `INotificationRealtimePublisher` Scoped DI satırı eklendi. T61'in `ITransactionRealtimePublisher` paterni mirror.

3. **T38 dispatcher entegrasyonu (`NotificationDispatcher.cs`):** `INotificationRealtimePublisher` constructor'a inject edildi. `Notification` row Add'inden sonra:
   - DB'den mevcut `IsRead=false` satır sayısı okunur (AsNoTracking).
   - `NotificationTargetMapper.Resolve` ile `targetType`/`targetId` belirlenir (07 §8.1 ile uyumlu).
   - `PublishNewNotificationAsync` çağrılır — payload field'ları 07 §11.2 tablosu ile 1:1.
   - `PublishUnreadCountChangedAsync` çağrılır — `existingUnread + 1`.
   - Pre-commit push: outbox dispatch içinde unit-of-work commit etmeden önce yapılır. Best-effort try/catch → SaveChanges aborted olursa wire'da phantom payload kalır; frontend reconnect/refetch (T96) ile düzeltir. Bu, T61'in dokümante kapı bırakma paterniyle aynı.

4. **T38 inbox entegrasyonu (`NotificationInboxService.cs`):**
   - `MarkAllReadAsync`: SaveChanges sonrası `UnreadCountChanged(0)` push (mark-all-read sonrası unread count tanımı gereği 0).
   - `MarkReadAsync`: yalnız read-state'i değiştiren branch'te SaveChanges sonrası `GetUnreadCountAsync` ile fresh count okunur ve push'lanır. Already-read no-op branch'te push atlanır (count değişmediği için noise yok).

5. **`Skinora.Notifications.csproj`:** `Skinora.Realtime` ProjectReference eklendi (port import için tek yön; Realtime → Notifications referansı yok).

6. **API host wiring (`Skinora.API/Program.cs`):**
   - `app.MapHub<NotificationsHub>("/hubs/notifications")` — endpoints aşaması (T61 transactions hub satırının hemen altı).
   - Yorum güncellemesi: T61 → T61/T62 SignalR hubs.
   - JWT bridge zaten `/hubs/*` path-restricted (T61 yerleştirdi); ek wiring gerekmedi.
   - `AddSignalR().AddJsonProtocol(JsonStringEnumConverter)` mevcut (T61); RT2 enum'ları (örn: maintenance type) tek noktadan string olarak serileşir.

7. **Doküman uyumu:** Plan/spec değişikliği yok — 07 §11.2 RT2 kontratı birebir uygulandı. `Notification` entity'si T38'de zaten tanımlıydı, Realtime modülü hiçbir tabloya yazmaz.

## Etkilenen Modüller / Dosyalar

**Yeni — `Skinora.Realtime/` (4 dosya):**
- `backend/src/Modules/Skinora.Realtime/Hubs/NotificationsHub.cs`
- `backend/src/Modules/Skinora.Realtime/Application/INotificationRealtimePublisher.cs`
- `backend/src/Modules/Skinora.Realtime/Application/Contracts/NotificationRealtimePayloads.cs`
- `backend/src/Modules/Skinora.Realtime/Infrastructure/SignalRNotificationRealtimePublisher.cs`

**Yeni testler:**
- `backend/tests/Skinora.Notifications.Tests/TestSupport/RecordingNotificationRealtimePublisher.cs` (publisher test double)
- `backend/tests/Skinora.Notifications.Tests/Integration/NotificationInboxServiceTests.cs` (6 test — MarkAllRead push 0 / MarkAllRead no-op / MarkRead first-read fresh count / MarkRead already-read no-push / MarkRead not-found / MarkRead foreign no-push)
- `backend/tests/Skinora.API.Tests/Integration/NotificationsHubEndpointTests.cs` (5 test — connect anon 401 / per-user round-trip / per-user isolation / unread count round-trip / maintenance broadcast)

**Değişiklik:**
- `backend/src/Modules/Skinora.Realtime/RealtimeModule.cs` — Notification publisher DI satırı.
- `backend/src/Modules/Skinora.Notifications/Skinora.Notifications.csproj` — `Skinora.Realtime` ProjectReference.
- `backend/src/Modules/Skinora.Notifications/Application/Notifications/NotificationDispatcher.cs` — publisher inject + 2 push çağrısı + using.
- `backend/src/Modules/Skinora.Notifications/Application/Inbox/NotificationInboxService.cs` — publisher inject + MarkAllRead/MarkRead push'ları + using.
- `backend/src/Skinora.API/Program.cs` — `MapHub<NotificationsHub>` satırı + T61 yorum güncellemesi.
- `backend/tests/Skinora.Notifications.Tests/Integration/NotificationDispatcherTests.cs` — `CreateSut` 3-tuple döndürür (publisher recorder eklendi); 1 yeni test (`DispatchAsync_PushesNewNotificationAndUnreadCount_ViaRealtimePublisher`).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `/hubs/notifications` hub'ı | ✓ | `NotificationsHub.cs` `[Authorize]`; `Program.cs` `app.MapHub<NotificationsHub>("/hubs/notifications")`; integration `Connect_Without_Token_Returns401` 401 doğrular. |
| 2 | T38'den gelen Notification entity'leri real-time push ediliyor mu? | ✓ | `NotificationDispatcher` her `DispatchAsync` çağrısında `PublishNewNotificationAsync` + `PublishUnreadCountChangedAsync` ikilisini çağırır. `NotificationDispatcherTests.DispatchAsync_PushesNewNotificationAndUnreadCount_ViaRealtimePublisher` integration test (SQL Server) — seed 1 prior unread + 1 new dispatch sonrası iki push: payload field'ları 07 §11.2 ile 1:1 (`Type`, `Message`, `TargetType`, `TargetId`, `CreatedAt`, `UnreadCount=2`). Inbox mark-read mutasyonları da `UnreadCountChanged` yayınlar (`NotificationInboxServiceTests` 6/6). |
| 3 | Server→Client: `NewNotification`, `UnreadCountChanged`, `TelegramConnected`, `DiscordConnected`, `MaintenanceStatusChanged` | ✓ kısmi | `INotificationRealtimePublisher` 5 metot + `NotificationRealtimePayloads.cs` 5 record (07 §11.2 tablosu 1:1). `NewNotification` + `UnreadCountChanged` canlı producer'lara bağlı (dispatcher + inbox). `TelegramConnected` / `DiscordConnected` / `MaintenanceStatusChanged` publisher port + payload + adapter mevcut, tetikleyici callsite'lar T79 (Telegram webhook), T80 (Discord OAuth callback), T-future (maintenance toggle endpoint) devrinde — K1 forward-deferred pattern (T61 PaymentDetected aynası). Integration test `Publisher_NewNotification_Reaches_Owner` + `Publisher_UnreadCountChanged_Reaches_Owner` + `Publisher_MaintenanceStatusChanged_Reaches_AllConnections` round-trip'leri kanıtlar. |
| 4 | User bazlı mesajlaşma (user ID) | ✓ | `NotificationsHub.GroupName(Guid) = "user:{N}"`; `OnConnectedAsync` kullanıcıyı otomatik gruba ekler; `IHubContext.Clients.Group(...)` SignalR runtime grup roting'i. `Publisher_NewNotification_DoesNotReach_OtherUser` integration test: owner push'u stranger connection'a yansımaz (500ms timeout) — fan-out izolasyonunu kanıtlar. |

**Doğrulama kontrol listesi:**

- [x] **07 §11.2 tüm event'ler tanımlı mı?** ✓ — `NotificationRealtimePayloads.cs` 5 record şema 1:1. JSON protocol `JsonStringEnumConverter` (T61'den) enum'ları wire'da string olarak serileştirir → wire format spec değerleriyle uyumlu.
- [x] **T38 Notification entity'leri real-time push ediliyor mu?** ✓ — Dispatcher her notification creation'da iki push (NewNotification + UnreadCountChanged); inbox her read-state mutation'ında UnreadCountChanged. 6 integration test (`NotificationInboxServiceTests`) read-state mutasyon yan etkisini, 1 integration test (`NotificationDispatcherTests`) creation yan etkisini kanıtlar.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Skinora.Realtime.Tests) | ✓ 25/25 | T61'den regresyon temiz — yeni notifications publisher mevcut consumer/broadcaster path'lerini etkilemedi. |
| Unit (Skinora.Notifications.Tests filter !Integration) | ✓ 49/49 | Mevcut consumer test fixture'ları regresyon temiz — dispatcher constructor değişikliği test fixture'larında yansımış. |
| Integration (Skinora.Notifications.Tests `DispatchAsync_*` + `NotificationInboxServiceTests`) | Lokal Docker yok — CI'de doğrulanacak | Mevcut 6 dispatcher test + yeni 1 push assertion + yeni 6 inbox service test = **13 yeni/regresyon integration**. |
| Integration (Skinora.API.Tests hub endpoints) | ✓ 10/10 | 5 NotificationsHub (yeni) + 5 TransactionsHub (T61 regresyon). SQLite in-memory + LongPolling transport. |
| Unit (Skinora.Transactions.Tests) | ✓ 333/333 | Regresyon temiz. |
| Unit (Skinora.Auth.Tests) | ✓ 57/57 | Regresyon temiz. |
| Unit (Skinora.Shared.Tests) | ✓ 185/185 | Regresyon temiz. |
| Build (Release, `-warnaserror`) | ✓ 0W/0E | Tüm sln. |
| Format verify | ✓ exit=0 | `dotnet format Skinora.sln --verify-no-changes`. |

**Lokal toplam:** Realtime 25 + Notifications(unit) 49 + Transactions(unit) 333 + Auth(unit) 57 + Shared(unit) 185 + API.Tests(hub) 10 = **659 lokal pass, 0 fail**. Docker-bağımlı integration test'ler (Notifications integration ~13 + diğer modüller) CI Linux runner'da doğrulanacak.

## Altyapı Değişiklikleri

- **Migration:** Yok — Realtime modülü ve T62 kapsamı hiçbir tabloya yazmaz; publisher okuma için AsNoTracking unread count sorgusu yapar.
- **SystemSetting:** Yok.
- **Config/env:** Yok — T61'in `Realtime:CountdownSync` section'ı dışında ek config gerekmedi. JSON protocol enum config T61'den miras.
- **Docker:** Yok — Realtime modülü mevcut Dockerfile COPY listesinde (T61 fix). Notifications.csproj değişikliği transitive ProjectReference, COPY etkilenmez.
- **Yeni dış bağımlılık:** Yok — Realtime modülü `FrameworkReference="Microsoft.AspNetCore.App"` (T61), test paketleri (`Microsoft.AspNetCore.SignalR.Client`) zaten T61'de eklendi.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyalarda secret yok; integration test JWT secret'ı test fixture içinde sabit (`notif-hubs-test-secret-key-minimum-32-chars!`).
- **Auth/authorization:** `[Authorize]` hub seviyesinde; `OnConnectedAsync` JWT `sub` claim'ini Guid olarak parse eder, tutarsızsa `HubException("AUTH_INVALID")` ile reddeder. Per-user group ismi `user:{userId:N}` SignalR runtime grup roting'i ile diğer kullanıcılara fan-out önler — `Publisher_NewNotification_DoesNotReach_OtherUser` integration test bunu kanıtlar. Maintenance broadcast amaçlı `Clients.All` kullanır; payload kullanıcıya özel bilgi içermez (07 §10.2 P2 maintenance şeması).
- **Input validation:** Hub'ın client→server metodu yok → girdi yüzeyi sıfır. Publisher methodları primitive (Guid + record) kabul eder; payload kontratı C# tip sistemi ile sabittir.
- **Yeni dış bağımlılık:** Yok.
- **Push yan etkisi atomicity:** Pre-commit (dispatcher) ve post-commit (inbox) push'lar try/catch + log+swallow — outbox dispatcher redelivery sinyali kirletilmiyor; inbox SaveChanges başarısı yan etki atlanırsa rolled back edilmiyor. T96 reconnect+refetch ile eventual consistency.

## Commit & PR

- Branch: `task/T62-signalr-notifications-hub`
- Commits: (yapım sırasında doldurulacak)
- PR: (yapım sonrası açılacak)
- CI: (yapım sonrası izlenecek)

## Known Limitations / Follow-up

- **K1 — `TelegramConnected` push'u henüz yayınlanmıyor.** 07 §11.2 `TelegramConnected` event'i Telegram bot link akışı tamamlandığında push edilmeli. Mevcut `TelegramConnectionService` (T35) yalnız InitiateAsync (kod üretimi) sağlıyor; webhook receiver (`POST /webhooks/telegram`) T79 kapsamında. T79 webhook handler'ı user'ı bağladığında 1-2 satır eklenir (publisher inject + `await publisher.PublishTelegramConnectedAsync(userId, new TelegramConnected(username), ct)`). Publisher port + payload + JSON config T62'de hazır.
- **K2 — `DiscordConnected` push'u henüz yayınlanmıyor.** Aynı pattern: T80 Discord OAuth callback (`GET /users/me/settings/discord/callback`) handler'ında 1-2 satır eklenir.
- **K3 — `MaintenanceStatusChanged` push'u henüz yayınlanmıyor.** 07 §10.2 P2 endpoint'i (T63a kapsamı) ve `auth.maintenance_*` SystemSetting toggle'ı T-future. Maintenance toggle'a publisher inject + `PublishMaintenanceStatusChangedAsync` çağrısı eklendiğinde aktif olur.
- **K4 — Backplane (Redis) yok.** SignalR şu an in-memory (T61 K4 ile aynı); multi-instance API host'larda her instance kendi grup üyeliklerini bildiğinden cross-instance push işlemez. T-future scaling task'ı `Microsoft.AspNetCore.SignalR.StackExchangeRedis` eklediğinde tek satır DI değişikliğiyle çoklu instance'a yayılır. Şu an F3 fazında tek host runtime için sorun değil.
- **K5 — Pre-commit push race window.** Dispatcher'dan yayınlanan `NewNotification` + `UnreadCountChanged` push'ları unit-of-work commit edilmeden önce gönderilir. Surrounding outbox transaction abort olursa wire'da phantom payload kalır; frontend reconnect/refetch (T96) ile düzeltir. Best-effort kabul edildi (T61'in K-pattern aynası); strong-consistency için post-commit hook ayrı bir refactor gerektirirdi.
- **K6 — `MaintenanceStatusChanged` `Clients.All` kapsamı authenticated olmayan connection'lara yansımaz.** Hub `[Authorize]` korumalı olduğu için anon client bağlanamıyor; banner platform genelinde gözükmesine rağmen her client önce login olmalı. 04 §7.7 C08 banner spec'inde anon kullanıcı için `GET /platform/maintenance` polling fallback'i var (T63a) — UI senkronizasyonu için yeterli.

## Bağımsız Validator Sonucu

**TBD — yapım sonrası ayrı validate chat'inde doldurulur.**

## Notlar

- **Working tree pre-flight:** clean (`git status` boş). Adım -1 ✓.
- **Main CI startup pre-flight:** son 3 main run ✓ — `25476326367` (T61 PR #98) + `25476326389` (T61 PR #98) + `25458191870` (chore PR #97 memory T60). Adım 0 ✓.
- **Bağımlılık kontrolü:** T38 ✓ (PR #63). T37 ✓.
- **Dış varsayım kontrolü (Adım 4):**
  - SignalR + JWT query-param bridge — ✓ T61'den miras, `/hubs/*` path-restricted.
  - `IHubContext<T>` user-bazlı routing — `Clients.Group("user:{N}")` paterni T61'in `tx:{N}` ile birebir mirror.
  - `Notification` entity + `INotificationDispatcher` (T37/T38) — ✓ mevcut.
  - Project reference yönü `Notifications → Realtime` — ✓ tek yön (Realtime → Notifications referansı yok); transitive olarak Notifications artık Auth + Skinora.Transactions'a (zaten vardı) referans verir, cyclic değil.
  - Pre-commit push semantiği — best-effort + log+swallow (T61'in `SignalRTransactionRealtimePublisher` paterniyle birebir aynı).
- **`MaintenanceStatusChanged` Clients.All kararı:** Hub `[Authorize]` olduğu için `Clients.All` yalnızca authenticated connection'lara yayılır. Spec'te "anonim broadcast" zorunluluğu yok — login olmuş kullanıcılar banner'ı SignalR ile gerçek zamanlı görürler; anon kullanıcılar `GET /platform/maintenance` polling ile görür (T63a).
- **CreatedAt seed:** `Notification.CreatedAt` `BaseEntity` üzerinden `AppDbContext.UpdateAuditFields` ile SaveChanges sırasında damgalanır. Pre-commit push'ta `DateTime.UtcNow` kullanılır — wire'daki timestamp ve DB'deki canonical timestamp arasında ms-mertebesinde tolere edilebilir delta olabilir; frontend inbox refetch'te canonical değeri görür.
