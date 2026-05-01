# T38 — Platform içi bildirim kanalı

**Faz:** F2 | **Durum:** ⏳ Yapım bitti | **Tarih:** 2026-05-01

---

## Yapılan İşler

- **`INotificationInboxService` (07 §8.1–§8.4 / 05 §7.2):** `Skinora.Notifications/Application/Inbox/`. Tek arayüz dört operasyonu sarmalar — `ListAsync` (paginated bildirim listesi, 07 §8.1), `GetUnreadCountAsync` (07 §8.2), `MarkAllReadAsync` (07 §8.3), `MarkReadAsync` (07 §8.4). Hepsi `userId` parametresine sıkı sahiplik (ownership) filtresi uygular; başka kullanıcının bildiriminde 403, yoksa 404 (`MarkReadOutcome` enum'u ile discriminated outcome). Implementasyon `AppDbContext` üzerinden `Notification` tablosunu doğrudan okur/yazar — global query filter'ı `IsDeleted` rows'u zaten gizliyor.
- **Listeleme + sayfalama (07 §2.6 K6):** `ListAsync(page, pageSize)` → varsayılan `page=1`/`pageSize=20`, geçersiz değerler clamp edilir (`page<1→1`, `pageSize<1→20`, `pageSize>100→100`). `OrderByDescending(CreatedAt).ThenByDescending(Id)` deterministik kronolojik sıralama. `Skip/Take` + `CountAsync` paralel hesap → `PagedResult<T>` envelope'u döner (07 §2.6 spec ile birebir).
- **Mark-as-read mantığı:** `MarkAllReadAsync` sadece `IsRead=false` row'ları çeker ve `IsRead=true`/`ReadAt=UtcNow` ile flag'ler. **Tracking modunda** çalışır (ExecuteUpdate yerine) çünkü `AppDbContext.UpdateAuditFields` Modified state için `UpdatedAt`'i pipeline'dan stamp etmesi gerekir; bu T35/T36'da yerleşmiş kuraldır. `MarkReadAsync(notificationId)` → 1) row yoksa NotFound, 2) `UserId != callerId` Forbidden, 3) zaten `IsRead=true` ise idempotent OK (timestamp güncellenmez), 4) aksi halde flip + `ReadAt=UtcNow` + Save.
- **`NotificationTargetMapper` (07 §8.1 derived field):** `Skinora.Notifications/Application/Inbox/NotificationTargetMapper.cs` — `NotificationType` enum + `TransactionId?` çiftini `(targetType, targetId)` çiftine dönüştürür. 07 §8.1'deki tablo birebir kodlu: `ADMIN_FLAG_ALERT` → `"flag"` (admin queue link, T39+ admin endpoint'leri devralır), `ADMIN_STEAM_BOT_ISSUE` → `(null, null)` (platform-wide alert, hedef yok), diğer 18 type → `TransactionId` varsa `"transaction"`, yoksa `(null, null)`.
- **`NotificationsController` (07 §8.1–§8.4):** `Skinora.API/Controllers/NotificationsController.cs`. Dört endpoint (`GET /`, `GET /unread-count`, `POST /mark-all-read`, `PUT /{id:guid}/read`); ilk üçü `[RateLimit("user-read")]` ya da `[RateLimit("user-write")]` dekoratörü, hepsi `[Authorize(Policy = AuthPolicies.Authenticated)]`. JWT claim'inden `UserId` çıkarılamadığında `Unauthorized()` (403/401 ayrımı T29 paterni mirror). Hata kodları stable: `NOTIFICATION_NOT_FOUND`, `FORBIDDEN` (07 §8.4 "404 NOTIFICATION_NOT_FOUND, 403 FORBIDDEN" ile birebir).
- **DTO + envelope:** `NotificationListItemDto` 07 §8.1 örneğine birebir alan kümesi (`id`, `type`, `message`, `targetType`, `targetId`, `isRead`, `createdAt`). `type` alanı **string** (UPPER_SNAKE_CASE, 07 §2.8 K8) — dispatcher entity'deki enum'u `.ToString()` ile yazar; proje genel-amaçlı `JsonStringEnumConverter` kullanmıyor, bu yüzden DTO seviyesinde stringify ettim. `UnreadCountResponse(unreadCount:int)`, `MarkAllReadResponse(markedCount:int)`. PUT/read başarılı response → `data: null` (07 §8.4 ile birebir).
- **DI + module wiring:** `NotificationsModule.AddNotificationsModule()` → tek satır `services.AddScoped<INotificationInboxService, NotificationInboxService>()` ekleme. Program.cs'te `AddNotificationsModule()` zaten T37'de çağrılıyor; ek registrasyon yok.

## Known Limitations (dokümansal devirler)

- **Real-time push (SignalR) → T62.** T38 sadece DB read/write yapar; client polling ile sayaç + listeyi okur. SignalR hub T62'de `Notifications` row insert event'ini front-end'e push edecek.
- **`ADMIN_FLAG_ALERT` `targetType="flag"` admin endpoint integration → T39+.** Mapper bu type için "flag" döner ama `Notification.TransactionId` kolonu admin için `FlagId` olarak yorumlanır (06 §3.13 entity şu an ek `FlagId` taşımıyor). T39 admin notification listesi açtığında ya `Notification.TransactionId`'yi flag id olarak kullanacak ya da entity'yi extend edecek. T38 user-only inbox yazdığı için bu admin path bu task'ta exercise edilmiyor.
- **i18n bildirim metni → T97.** `NotificationListItemDto.message` zaten `Notification.Title` (T37 dispatcher tarafından `User.PreferredLanguage` ile rendered). T97 frontend i18n'i tamamlayacak.
- **Bildirim tipi bazlı per-channel mute → Post-MVP (05 §7.4).** T38 sadece platform içi kanalı listeler; tip bazlı kontrol tüm kanallar için Post-MVP scope'u, T38 dışı.

## Etkilenen Modüller / Dosyalar

**Yeni — `backend/src/Modules/Skinora.Notifications/Application/Inbox/`:**
- `INotificationInboxService.cs` — interface (4 operasyon).
- `NotificationInboxService.cs` — EF Core impl (AppDbContext üstüne kurulu).
- `NotificationListItemDto.cs` — 07 §8.1 row DTO.
- `NotificationInboxResponses.cs` — `UnreadCountResponse`, `MarkAllReadResponse`, `MarkReadOutcome` enum.
- `NotificationInboxErrorCodes.cs` — `NOTIFICATION_NOT_FOUND`, `FORBIDDEN` sabitleri.
- `NotificationTargetMapper.cs` — `(targetType, targetId)` derivation helper.

**Yeni — `backend/src/Skinora.API/Controllers/`:**
- `NotificationsController.cs` — 4 endpoint, route `api/v1/notifications`.

**Değişiklik — `backend/src/Modules/Skinora.Notifications/NotificationsModule.cs`:**
- `using Skinora.Notifications.Application.Inbox;` import.
- `services.AddScoped<INotificationInboxService, NotificationInboxService>();` kaydı.

**Yeni — `backend/tests/Skinora.API.Tests/Integration/`:**
- `NotificationInboxEndpointTests.cs` — **14 integration test** (auth, ownership, ordering, pagination, clamp, unread-count, mark-all-read, mark-read başarılı/idempotent/404/403/401).

**Yeni — `backend/tests/Skinora.Notifications.Tests/Unit/`:**
- `NotificationTargetMapperTests.cs` — **12 unit test** (transaction types × var/yok TransactionId, admin flag alert, admin steam bot issue).

**Migration:** **yok**. T23'te `Notification` tablosu zaten yerleşik; T38 yeni kolon/tablo eklemez.

**Package reference:** **yok**. Yeni `using` yok ki ek paket gerektirsin; dosyalar mevcut bağımlılıklar üstünde durur.

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T38) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /notifications` → bildirim listesi (paginated) | ✓ | `NotificationsController.List()` route `GET api/v1/notifications`, `[Authorize][RateLimit("user-read")]`, `PagedResult<NotificationListItemDto>` döner. Test: `GetList_Authenticated_ReturnsOnlyOwnNotificationsNewestFirst`, `GetList_PaginatesCorrectly` (page=2, pageSize=2 → 2/5 row), `GetList_PageSizeOver100_ClampsTo100`. |
| 2 | `GET /notifications/unread-count` → okunmamış sayı | ✓ | `GetUnreadCount()` route `GET api/v1/notifications/unread-count`, `UnreadCountResponse(unreadCount)` döner. Test: `UnreadCount_Authenticated_ReturnsOnlyOwnUnread` (4 row seed → 2 unread own, doğru sayı). |
| 3 | `POST /notifications/mark-all-read` → tümünü okundu | ✓ | `MarkAllRead()` route `POST api/v1/notifications/mark-all-read`, sadece `UserId == caller && !IsRead` row'ları flip eder, `markedCount` döner. Test: `MarkAllRead_FlipsOnlyUnreadAndReturnsCount` (3 own seed: 2 unread + 1 read → markedCount=2, stranger's row dokunulmadı), `MarkAllRead_NoUnread_ReturnsZero`. |
| 4 | `PUT /notifications/:id/read` → tek bildirim okundu | ✓ | `MarkRead(id)` route `PUT api/v1/notifications/{id:guid}/read`, ownership + idempotent + 404/403 mantığı tam. Test: `MarkRead_OwnUnreadNotification_FlipsToRead`, `MarkRead_AlreadyRead_OkIdempotent` (timestamp preserve), `MarkRead_UnknownId_Returns404WithCode`, `MarkRead_OtherUsersNotification_Returns403`, `MarkRead_Unauthenticated_Returns401`. |
| 5 | Notification tablosuna yazma | ✓ | `MarkReadAsync` ve `MarkAllReadAsync` `IsRead=true` + `ReadAt=UtcNow` ile satıra yazar, `SaveChangesAsync` çağırır. Tracking mode ile `AppDbContext.UpdateAuditFields` `UpdatedAt`'i de stamp eder. Test: `MarkAllRead_FlipsOnlyUnreadAndReturnsCount` `Assert.NotNull(n.ReadAt)` ve `MarkRead_OwnUnreadNotification_FlipsToRead` aynı doğrulamayı yapar. |

## Doğrulama Kontrol Listesi (11 §T38)

- [x] **07 §8.1–§8.4 endpoint sözleşmeleri doğru mu?**
  - 8.1 (`GET /notifications`): Auth=Authenticated ✓, paginated (varsayılan 20) ✓, items[] alan kümesi (id, type, message, targetType, targetId, isRead, createdAt) ✓, `type` UPPER_SNAKE_CASE string ✓, `targetType` derivation 07 §8.1 tablosu ile birebir ✓.
  - 8.2 (`GET /notifications/unread-count`): Auth=Authenticated ✓, response `{ unreadCount: N }` ✓.
  - 8.3 (`POST /notifications/mark-all-read`): Auth=Authenticated ✓, response `{ markedCount: N }` ✓.
  - 8.4 (`PUT /notifications/:id/read`): Auth=Authenticated ✓, response `data: null` ✓, hata 404 `NOTIFICATION_NOT_FOUND` + 403 `FORBIDDEN` envelope ✓.

## Test Sonuçları

**Build (lokal, Release):**
```bash
dotnet build -c Release
```
→ Build succeeded. **0 Warning(s). 0 Error(s).** ~7 sn.

**Format verify (lokal):**
```bash
dotnet format Skinora.sln --verify-no-changes
```
→ exit 0, değişiklik yok.

**API integration testler (lokal, Debug, no-Docker — SQLite in-memory factory):**
```bash
dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName~NotificationInbox"
```
→ **14/14 PASS** — 6,6 sn.

**Notifications unit testler (lokal, Debug, no-Docker):**
```bash
dotnet test tests/Skinora.Notifications.Tests --filter "Category=Unit"
```
→ **26/26 PASS** — 0,9 sn (T37'den 14 + T38 yeni 12).

**Tam backend test koşumu (lokal, Docker yok):** SQL Server / Testcontainers gerektiren integration testler lokalde Docker olmadan FAIL eder (beklenen davranış, T11.3 paterni). API.Tests'te Docker-bağımsız 194 test PASS; sadece 6 `InitialMigrationTests` Docker-bağımlı failure. CI Linux runner SQL Server service container kullanır → CI'da tüm testler koşar.

## Altyapı Değişiklikleri

- **Migration:** **yok**. `Notification` entity'si T23'te tanımlı, T28 InitialCreate migration'ında zaten yer alıyor.
- **Package reference:** **yok**. Yeni dış paket gerektirmedi.
- **DI kayıtları:** 1 yeni scoped — `INotificationInboxService` (`NotificationsModule.cs` içinde).
- **Rate-limit policy etkisi:** `user-read` ve `user-write` policy'leri T07'den beri tanımlı; T38 yeni endpoint'leri bu policy'leri kullanır, ek konfigürasyon yok.
- **Hangfire / SignalR / Redis / outbox:** T38 hiçbirini etkilemez. Synchronous request-response, side-effect yok.

## Notlar

- **Working tree (Adım -1):** Temiz. `git status --short` boş.
- **Startup CI check (Adım 0):** Son 3 main run `25184605927` ✓ (T37 chore Docker), `25184605957` ✓ (T37 chore CI), `25017597051` ✓ (T37 squash CI) — hepsi success.
- **Dış varsayımlar (Adım 4):**
  - **Yeni paket gerekmiyor:** T38 yalnız EF Core + ASP.NET Core mevcut bağımlılıkları kullanır. `dotnet build` 0W/0E doğruladı.
  - **`Notification` entity şeması:** T23 entity (Title, Body, IsRead, ReadAt, TransactionId, UserId, BaseEntity Id+CreatedAt) T38 endpoint'lerinin tüm alan ihtiyaçlarını karşılar; ek kolon yok. Manuel doğrulandı (`Domain/Entities/Notification.cs`).
  - **`NotificationType` enum stringification:** Proje genel `JsonStringEnumConverter` kullanmıyor; DTO seviyesinde `.ToString()` ile UPPER_SNAKE_CASE string yazıyorum. 07 §2.8 K8 ile birebir uyumlu (test: `GetList_Authenticated_ReturnsOnlyOwnNotificationsNewestFirst` `Assert.Equal("PAYMENT_RECEIVED", first.GetProperty("type").GetString())`).
- **Test seed FK zorluğu:** `Notification.TransactionId` `Transactions` tablosuna FK. Geçerli `Transaction` row'u oluşturmak için T19'un `~50+` field şeması + 8 CHECK constraint gerekiyor. Bu integration test için aşırı maliyetli. Çözüm:
  1. Integration testler `TransactionId=null` ile seed (mevcut FK violation'ı bypass).
  2. `(targetType, targetId)` mapping logic'i `NotificationTargetMapperTests` ile **12 unit test** ile kapsıyor (transaction types × var/null TransactionId, admin flag alert, admin steam bot issue) — end-to-end "transaction" string assertion unit seviyesinde verifie.
  3. Integration testlerde `targetType=null` happy-path kontrol edildi (`GetList_NullTransactionId_TargetTypeIsNull`).
- **`MarkAllRead` tracking modu kararı:** İlk implementasyonda `ExecuteUpdateAsync` cazipti ama `AppDbContext.UpdateAuditFields` Modified state için `UpdatedAt`'i pipeline'da stamp ediyor — `ExecuteUpdate` bu pipeline'ı bypass eder. T35/T36 entity update'leri tracking modunda; aynı paterne uydum. Performans etkisi minimal (kullanıcı başına unread row N≈10–100 maksimum).
- **Bundled-PR check (Bitiş Kapısı):** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` (commit henüz yok). PR push'tan sonra tekrar kontrol — sadece T38 commit'leri görünmeli.
- **Post-merge CI watch:** Validate chat'i merge sonrası main CI'yi izler (validate.md Adım 18).

## Commit & PR

- **Branch:** `task/T38-notification-inbox`
- **Commit (kod):** `4f3d95c`
- **Commit (rapor+status+memory):** `c560d57`
- **PR:** [#63](https://github.com/turkerurganci/Skinora/pull/63)
- **CI run (task branch):** [`25206507387`](https://github.com/turkerurganci/Skinora/actions/runs/25206507387) ✓ — 9/9 job (Lint + Build + Unit + Integration + Contract + Migration + Docker build + CI Gate; Guard skipped + paths detected). İlk run `25206499968` rapor commit'i tarafından concurrency cancellation ile durduruldu — son tamamlanmış run baz alınır (T11.2 concurrency notu).
