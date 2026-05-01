# T38 — Platform içi bildirim kanalı

**Faz:** F2 | **Durum:** ✓ PASS bağımsız validator | **Tarih:** 2026-05-01

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

---

## Doğrulama Sonucu — T38 Platform içi bildirim kanalı

**Tarih:** 2026-05-01 | **Branch:** `task/T38-notification-inbox` | **HEAD commit:** `f961122`

### Verdict: ✓ PASS (bağımsız validator)

### Hard-Stop Kapıları
- **Adım -1 — Working Tree:** `git status --short` boş → ✓ temiz.
- **Adım 0 — Main CI Startup Check:** Son 3 main run hepsi `success` (`25184605927` Docker, `25184605957` CI — T37 chore PR #62; `25017597051` CI — T37 squash) → ✓ geçti.
- **Adım 0b — Memory Drift:** `.claude/memory/MEMORY.md` satır 11 + 39–40 (T38 ⏳ + 2 tasarım notu) mevcut → ✓ geçti.

### Kabul Kriterleri (validator)
| # | Kriter (11 §T38) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /notifications` → bildirim listesi (paginated) | ✓ | `NotificationsController.List()` route `GET /api/v1/notifications`, `[Authorize(Authenticated)] + [RateLimit("user-read")]`. `INotificationInboxService.ListAsync` `AsNoTracking + Where(UserId==caller) + OrderByDescending(CreatedAt).ThenByDescending(Id) + Skip/Take` + `CountAsync` → `PagedResult<NotificationListItemDto>`. Clamp: page<1→1, pageSize<1→20, pageSize>100→100. Test (lokal Release 14/14 PASS — `GetList_*` 4 senaryo: 401, ownership+ordering, null txid → null target, pagination page=2/pageSize=2, clamp 500→100). |
| 2 | `GET /notifications/unread-count` → okunmamış sayı | ✓ | `GetUnreadCount()` route `GET /api/v1/notifications/unread-count`, `[Authorize][RateLimit("user-read")]`, `UnreadCountResponse(int UnreadCount)` döner. Test: `UnreadCount_Authenticated_ReturnsOnlyOwnUnread` (4-row seed: 2 unread own + 1 read own + 1 unread stranger → unreadCount=2) + `UnreadCount_Unauthenticated_Returns401`. |
| 3 | `POST /notifications/mark-all-read` → tümünü okundu | ✓ | `MarkAllRead()` route `POST /api/v1/notifications/mark-all-read`, `[Authorize][RateLimit("user-write")]`, `MarkAllReadResponse(int MarkedCount)`. Service yalnız `UserId==caller && !IsRead` row'ları çeker, tracking modunda flip eder (`UpdateAuditFields`'in `UpdatedAt` stamp'ini bypass etmemek için bilinçli karar — ExecuteUpdate bypass ederdi). Test: `MarkAllRead_FlipsOnlyUnreadAndReturnsCount` (3 own + 1 stranger seed → markedCount=2, stranger row dokunulmadı, all own ReadAt set), `MarkAllRead_NoUnread_ReturnsZero`. |
| 4 | `PUT /notifications/:id/read` → tek bildirim okundu | ✓ | `MarkRead(id)` route `PUT /api/v1/notifications/{id:guid}/read`, `[Authorize][RateLimit("user-write")]`. Akış: row yok → `MarkReadOutcome.NotFound` → 404 `NOTIFICATION_NOT_FOUND`; `UserId != caller` → 403 `FORBIDDEN`; `IsRead=true` → idempotent OK (timestamp preserve); aksi → flip + `ReadAt=UtcNow` + Save. Test: `MarkRead_OwnUnreadNotification_FlipsToRead`, `MarkRead_AlreadyRead_OkIdempotent` (`Assert.Equal(readAt, persisted.ReadAt, TimeSpan.FromSeconds(1))` preserve doğrulandı), `MarkRead_UnknownId_Returns404WithCode`, `MarkRead_OtherUsersNotification_Returns403`, `MarkRead_Unauthenticated_Returns401`. Hata envelope `success:false + error.code` doğru (`ApiResponseWrapperFilter` 2xx-only sarar; `ApiResponse<object>.Fail` controller'da elle inşa). |
| 5 | Notification tablosuna yazma | ✓ | `MarkAllReadAsync` ve `MarkReadAsync` `IsRead=true + ReadAt=UtcNow` ile satıra yazar; `SaveChangesAsync` çağırır; tracking pipeline `AppDbContext.UpdateAuditFields` `UpdatedAt`'i de stamp eder. Test seviye: `Assert.NotNull(n.ReadAt)` + `Assert.True(persisted.IsRead)` her iki path'te de doğrulanır. |

### Doğrulama Kontrol Listesi (11 §T38)
- [x] **07 §8.1–§8.4 endpoint sözleşmeleri doğru mu?**
  - **8.1 (`GET /notifications`):** Auth=Authenticated ✓; paginated default 20 ✓; `data.items[]` alan kümesi `id`/`type`/`message`/`targetType`/`targetId`/`isRead`/`createdAt` birebir (`NotificationListItemDto.cs:11–18`); `type` UPPER_SNAKE_CASE string (07 §2.8 K8) — proje genel `JsonStringEnumConverter` yok, DTO'da `r.Type.ToString()` ile inşa, test `Assert.Equal("PAYMENT_RECEIVED", first.GetProperty("type").GetString())` doğruladı; `targetType` derivation 07 §8.1 tablosu ile birebir (`NotificationTargetMapper.cs:18–31`: `ADMIN_FLAG_ALERT→"flag"`, `ADMIN_STEAM_BOT_ISSUE→null`, diğer 18 type `TransactionId` varsa `"transaction"`).
  - **8.2 (`GET /notifications/unread-count`):** Auth ✓; `data: { unreadCount: N }` → `UnreadCountResponse(int UnreadCount)` ✓.
  - **8.3 (`POST /notifications/mark-all-read`):** Auth ✓; `data: { markedCount: N }` → `MarkAllReadResponse(int MarkedCount)` ✓.
  - **8.4 (`PUT /notifications/:id/read`):** Auth ✓; `data: null` → `Ok((object?)null)` + `ApiResponseWrapperFilter` `IsAlreadyWrapped(null)=false` → `ApiResponse<object>.Ok(null, traceId)` → JSON `{success:true, data:null, error:null}` ✓; 404 `NOTIFICATION_NOT_FOUND` ✓; 403 `FORBIDDEN` ✓.

### Test Sonuçları (lokal Release validator)
| Tür | Sonuç | Detay |
|---|---|---|
| Build (Release) | ✓ 0W/0E | `Build succeeded. 0 Warning(s) 0 Error(s).` 15.5s |
| Unit (NotificationTargetMapper) | ✓ 12/12 | `Skinora.Notifications.Tests` `--filter "FullyQualifiedName~NotificationTargetMapper"` 149ms |
| Integration (NotificationInbox) | ✓ 14/14 | `Skinora.API.Tests` `--filter "FullyQualifiedName~NotificationInbox"` 3s (SQLite in-memory factory) |
| Task branch CI (Adım 8a) | ✓ 10/10 | PR #63 head SHA `f961122` için CI run [`25206683581`](https://github.com/turkerurganci/Skinora/actions/runs/25206683581) — Detect changed paths, Lint, Build, Unit, Integration, Contract, Migration dry-run, Docker build (backend), CI Gate hepsi `success`; Guard skipped (PR yolu, beklenen). Yapım raporundaki `25206507387` daha önceki commit'in CI'sıydı; head SHA için yeni run otomatik başladı, o da tamamen yeşil. |

### Güvenlik Kontrolü
- [x] **Secret sızıntısı:** Temiz. `git diff main..HEAD -- backend` üzerinde `password|secret|api.key|connection.string|TODO|HACK` regex grep'i 0 hit.
- [x] **Auth/authz etkisi:** 4/4 endpoint `[Authorize(Policy = AuthPolicies.Authenticated)]`; ownership filter (`UserId == caller`) DB sorgusunda zorunlu — IDOR kontrolü test ile doğrulandı (stranger 403, list yalnız own row). 401 yolu da test ile kapsandı.
- [x] **Input validation:** `page` + `pageSize` query param'ları server-side clamp (`page<1→1`, `pageSize<1→20`, `pageSize>100→100`); `id` route constraint `:guid` — invalid guid → routing'de 404. POST/PUT body almadığı için body sanitization N/A.
- [x] **Rate limiting:** `user-read` (60 req/dk) GET endpoint'lerinde, `user-write` (20 req/dk) POST/PUT'da; T07'den beri tanımlı policy'ler — yeni konfig yok.
- [x] **Yeni dış bağımlılık:** **Yok** — `INotificationInboxService` sadece `Microsoft.EntityFrameworkCore` (mevcut), `AppDbContext` (mevcut), `Notification` entity (T23). Controller `Microsoft.AspNetCore.Mvc` + `RateLimitAttribute` (T07) + `AuthPolicies` (T06) — hepsi mevcut. Test csproj'larda yeni paket eklenmedi.

### Bulgular
| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| M1 | minor advisory (S0) | T38_REPORT.md "Commit & PR" bloğu task branch CI run'ı olarak `25206507387`'yi cite ediyor; bu rapor commit'leri (`d31b7f6`/`c560d57`) zamanındaki CI'ydı. Sonraki rapor commit'i `f961122` (CI run id update) için yeni CI run `25206683581` tetiklendi ve aynı şekilde 10/10 success — head SHA için fonksiyonel sonuç değişmedi, yalnız rapor cite stale. T37'deki resx count drift M1 ile aynı kategoriye giriyor; PASS'i engellemiyor. | `Docs/TASK_REPORTS/T38_REPORT.md:127` |

### Yapım Raporu Karşılaştırması
- **Uyum:** Tam — 5/5 kabul kriteri ✓, 1/1 doğrulama listesi maddesi ✓, build 0W/0E, 14 integration + 12 unit test PASS, kod ↔ doc 07 §8.1–§8.4 birebir. Yapım raporunun Known Limitations bölümü (SignalR T62 / admin endpoint T39+ / i18n T97 / per-channel mute Post-MVP) doğru ve tam — validator de aynı sonuçlara ulaştı, sapma yok.
- **Karar gerekçesi:** 0 S1/S2/S3 bulgu; 1 minor advisory (M1, doc drift CI run id). Tüm spec maddeleri kanıt bazlı doğrulandı. Verdict ✓ PASS.
