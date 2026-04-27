# T37 — Bildirim altyapı servisi

**Faz:** F2 | **Durum:** ⏳ Yapım bitti, validator bekliyor | **Tarih:** 2026-04-27

---

## Yapılan İşler

- **`INotificationDispatcher` orchestration (05 §7.1):** `Skinora.Notifications/Application/Notifications/NotificationDispatcher.cs` tek giriş noktası. Çağrıldığında 1) `User.PreferredLanguage` (default `en`) ile template'i `INotificationTemplateResolver`'dan alır, 2) `Notification` row'unu (platform içi — her zaman, 05 §7.4) `AppDbContext`'e ekler, 3) `UserNotificationPreference` filter'ı ile **enabled + ExternalId boş değil** kanallar için `NotificationDelivery` (PENDING) row'u açar, 4) her delivery için `NotificationDeliveryJob` Hangfire enqueue eder. **`SaveChangesAsync` çağırmaz** — caller (Outbox dispatcher MediatR fan-out) kendi batch transaction'ında commit eder. Hangfire enqueue commit-öncesidir; job'lar runtime'da delivery row'unu yeniden yükler ve yoksa no-op (09 §13.3 atomicity boundary).
- **`ResxNotificationTemplateResolver` (05 §7.3):** `Microsoft.Extensions.Localization.IStringLocalizer<NotificationTemplates>` üstüne kurulu. 4 dil için `Resources/NotificationTemplates.{neutral=en, tr, zh, es}.resx` embedded — neutral set `NotificationType` enum'undaki 20 değer için `_Title` + `_Body` (40 entry); tr/zh/es alt-set + native fallback chain. `CultureInfo.GetCultureInfo(locale)` invalid kültür → `InvariantCulture` (neutral). Placeholder substitution permissive: parameter map'te yoksa `{Key}` literal kalır (05 §7.3 placeholder semantiği). `MissingKey` → log warn + key adı (fail-safe, MVP-OUT-016 final metinler için iskelet).
- **Per-channel sender pipeline (05 §7.2):** `INotificationChannelHandler` interface (`Channel` property + `SendAsync(target, rendered, ct)`). `Skinora.Notifications/Infrastructure/Channels/` altında 3 stub impl — `EmailNotificationChannelHandler`, `TelegramNotificationChannelHandler`, `DiscordNotificationChannelHandler`. **T37 stub davranışı: log + success** (gerçek HTTP/SMTP yok). T78 (Resend), T79 (Telegram Bot), T80 (Discord webhook) bu interface'i DI swap ile devralır — `NotificationDispatcher` ve `NotificationDeliveryJob` değişmez.
- **`NotificationDeliveryJob` Hangfire job + retry (05 §7.5):** `[AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900], OnAttemptsExceeded = Fail)]` class-level attribute. `Execute(deliveryId, PerformContext?)` Hangfire entry point (T32 `RefreshTokenCleanupJob` paterni — sync wrapper); attempt sayısını `PerformContext.GetJobParameter<int>("RetryCount") + 1` ile çıkarır ve `RunAsync(deliveryId, attemptNumber, ct)` async core'una iletir (test için doğrudan invoke imkânı). Job:
  1. Delivery yoksa → no-op (producer transaction commit etmedi).
  2. Delivery zaten `SENT` → no-op (idempotent — duplicate enqueue / replay sessions).
  3. Notification + handler resolve → `AttemptCount += 1` → `handler.SendAsync` çağır.
  4. Success → `Status=SENT`, `SentAt=NOW`, `LastError=null`, SaveChanges.
  5. Exception → `Status=FAILED`, `LastError` (1000 char truncate), SaveChanges; `attemptNumber > MaxRetryAttempts` ise `INotificationAdminAlertSink.RaiseDeliveryExhaustedAsync` çağır; ardından **rethrow** (Hangfire AutomaticRetry → 60s/300s/900s exponential).
- **Admin alert sink (05 §7.5 "Tüm kanallar başarısız → Log + admin alert"):** `INotificationAdminAlertSink` interface + `LoggingNotificationAdminAlertSink` default impl — `LogWarning` ile DeliveryId / NotificationId / Channel / AttemptCount / LastError yapısal log entry. Post-MVP'de Slack/PagerDuty/AuditLog impl'i DI swap ile devralır.
- **Generic consumer base (`NotificationConsumerBase<TEvent>`):** `Skinora.Notifications/Application/EventHandlers/`. `INotificationHandler<TEvent>` (MediatR) + `IDomainEvent` constraint. Boilerplate sağlar: 1) `IProcessedEventStore.ExistsAsync(EventId, ConsumerName)` ile dedup → varsa skip, 2) `BuildRequestsAsync(event)` abstract → derived class event payload'ından `NotificationRequest` koleksiyonu üretir (multi-recipient OK), 3) her request'i `INotificationDispatcher.DispatchAsync` ile dispatch, 4) `MarkAsProcessedAsync` → ProcessedEvents row Add (commit caller'da). T44+ concrete handler'ları bu base'i extend eder; T37'de production handler **yok** (transaction event'leri henüz tanımlı değil).
- **Lokalizasyon kapsamı:** Neutral (`en`) tüm 20 `NotificationType` enum değerini kapsar (40 string entry). `tr` 11 NotificationType (Title+Body, 22 entry); `zh` 7 (14 entry); `es` 7 (14 entry). Eksik key'ler için fallback chain `<locale> → neutral en → key adı` çalışır (test edildi).
- **Module DI + Program.cs registration:** `Skinora.Notifications/NotificationsModule.cs` → `services.AddNotificationsModule()` extension. `services.AddLocalization()` + 4 scoped (dispatcher, resolver, alert sink, delivery job) + 3 scoped channel handler (`AddScoped<INotificationChannelHandler, ...>` — `IEnumerable<INotificationChannelHandler>` resolution doğru çalışır). Program.cs'te `AddUsersModule` sonrasında çağrılır (T37 sırası modül grafiğine uyumlu — Notifications → Users → Shared).

## Known Limitations (dokümansal devirler)

- **Email/Telegram/Discord gerçek gönderimi → T78 / T79 / T80.** T37 stub'ları `log + success` döner; gerçek transport DI swap ile devralır. Stub'lar production'da false-positive `SENT` üretir; T37'nin scope'u 02 §18.2 tetikleyici envanteri + altyapı pipeline'ıdır, gerçek gönderim devirde.
- **Domain event sınıfları → T44+ (Transaction state machine), T58 (Dispute), vb.** T37 hiç concrete `INotificationHandler<TEvent>` implement etmez; `NotificationConsumerBase` derived class'ları event'lerin tanımlandığı task'larda yazılır. Test'te placeholder event yok — dispatcher direkt `DispatchAsync` ile çağrılır.
- **SignalR push (real-time) → T62.** T37 platform içi notification'ı yalnızca DB'ye yazar (`Notifications` tablosu). T62 SignalR consumer'ı bu row değişikliğini tüketip front-end'e push edecek; hub henüz yok.
- **Final mesaj metinleri (polished, müşteri-dostu) → MVP-OUT-016 (Post-MVP).** T37 placeholder set yapılandırması: kısa, parametrik İngilizce + 11/7/7 örnek lokalizasyon. Final tonlama, marka voice, A/B varyantları MVP sonrası.
- **AuditLog'a notification dispatch failure yazımı → Post-MVP.** `LoggingNotificationAdminAlertSink` şu an Serilog warning. Gerçek admin notification kanalı (Slack webhook / pager bridge / `AuditLog` row) Post-MVP veya T42 (AuditLog servisi) sonrası DI swap.
- **Per-NotificationType kanal granülaritesi → Post-MVP.** 05 §7.4 "Bildirim tipi bazlı granüler kontrol MVP'de yok, ileride eklenebilir." T37 dispatcher kanal-seviyesi (UserNotificationPreference.IsEnabled) kontrolü yapar; type-bazlı per-channel mute MVP scope dışı.

## Etkilenen Modüller / Dosyalar

**Yeni — Skinora.Notifications/Application/Notifications/:**
- `NotificationRequest.cs` — input DTO (UserId, Type, TransactionId?, Parameters dict).
- `INotificationDispatcher.cs` — orchestration entry point.
- `NotificationDispatcher.cs` — default impl (rendering + entity ekleme + Hangfire enqueue).

**Yeni — Skinora.Notifications/Application/Templates/:**
- `INotificationTemplateResolver.cs` — locale + parameter → rendered tuple.
- `RenderedNotificationTemplate.cs` — record(Title, Body).
- `ResxNotificationTemplateResolver.cs` — `IStringLocalizer<NotificationTemplates>` üstüne kurulu impl.

**Yeni — Skinora.Notifications/Application/Channels/:**
- `INotificationChannelHandler.cs` — per-channel sender contract.
- `INotificationAdminAlertSink.cs` — final-failure escape hatch contract.

**Yeni — Skinora.Notifications/Application/EventHandlers/:**
- `NotificationConsumerBase.cs` — generic INotificationHandler<TEvent> base + IProcessedEventStore idempotency.

**Yeni — Skinora.Notifications/Infrastructure/Channels/:**
- `EmailNotificationChannelHandler.cs` — stub (log + success). T78 swap.
- `TelegramNotificationChannelHandler.cs` — stub. T79 swap.
- `DiscordNotificationChannelHandler.cs` — stub. T80 swap.
- `LoggingNotificationAdminAlertSink.cs` — default Serilog warning impl.

**Yeni — Skinora.Notifications/Infrastructure/DeliveryJobs/:**
- `NotificationDeliveryJob.cs` — Hangfire job class + AutomaticRetry attribute (3 retry × [60,300,900] s).

**Yeni — Skinora.Notifications/Resources/:**
- `NotificationTemplates.cs` — marker class for IStringLocalizer<T>.
- `NotificationTemplates.resx` — neutral (en), 40 string entry.
- `NotificationTemplates.tr.resx` — 22 entry.
- `NotificationTemplates.zh.resx` — 14 entry.
- `NotificationTemplates.es.resx` — 14 entry.

**Yeni — Skinora.Notifications/`:**
- `NotificationsModule.cs` — `AddNotificationsModule(IServiceCollection)` DI extension.

**Değişiklik — Skinora.Notifications/Skinora.Notifications.csproj:**
- 5 yeni `PackageReference`: `Hangfire.Core 1.8.18`, `MediatR 12.4.1`, `Microsoft.Extensions.Localization 9.0.3`, `Microsoft.Extensions.Logging.Abstractions 9.0.3`, `Microsoft.EntityFrameworkCore 9.0.3`. `RootNamespace=Skinora.Notifications`.

**Değişiklik — Skinora.API/Program.cs:**
- `using Skinora.Notifications;` import.
- `builder.Services.AddNotificationsModule();` çağrısı (T35 AddUsersModule paterni mirror).

**Yeni — backend/tests/Skinora.Notifications.Tests/:**
- `TestSupport/FakeBackgroundJobScheduler.cs` — IBackgroundJobScheduler test double.
- `TestSupport/SpyNotificationChannelHandler.cs` — channel handler spy + ExceptionFactory injection.
- `TestSupport/SpyNotificationAdminAlertSink.cs` — admin alert sink spy.
- `Unit/ResxNotificationTemplateResolverTests.cs` — **6 unit test** (locale fallback, placeholder substitute, invariant fallback).
- `Integration/NotificationDispatcherTests.cs` — **6 integration test** (platform-in-app her zaman, channel filter, job enqueue, locale, English fallback, no-SaveChanges contract).
- `Integration/NotificationDeliveryJobTests.cs` — **6 integration test** (missing row no-op, already-SENT short-circuit, success, transient failure, final-attempt admin alert, channel selection).

**Değişiklik — backend/tests/Skinora.Notifications.Tests/Skinora.Notifications.Tests.csproj:**
- 3 yeni `PackageReference`: `Microsoft.Extensions.DependencyInjection 9.0.3`, `Microsoft.Extensions.Localization 9.0.3`, `Microsoft.Extensions.Logging 9.0.3`.

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T37) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Domain event → Notification entity dönüşümü | ✓ | `NotificationDispatcher.DispatchAsync` event handler'ından çağrıldığında `Notification` entity oluşturur ve `AppDbContext` change tracker'ına ekler. `NotificationConsumerBase<TEvent>` MediatR `INotificationHandler<TEvent>` patern'i + `IProcessedEventStore` idempotency'i sarmalayan base — T44+ event handler'ları için iskelet. Test: `DispatchAsync_AlwaysWritesPlatformInAppNotification`. |
| 2 | Kanal dispatching: kullanıcı tercihlerine göre belirlenir | ✓ | `NotificationDispatcher` `UserNotificationPreference` tablosunu `IsEnabled && ExternalId != null && ExternalId != ""` filter'ı ile okur, eşleşen her kanal için bir delivery row'u açar. Disabled veya null/empty external id → skip. Test: `DispatchAsync_OnlyEnabledChannelsWithExternalId_GetDeliveryRows` (3 preference seed, sadece 1 enabled+external olan delivery üretir). |
| 3 | Bildirim retry stratejisi: exponential backoff, 3 deneme, başarısızlıkta admin alert | ✓ | `NotificationDeliveryJob` class-level `[AutomaticRetry(Attempts=3, DelaysInSeconds=[60,300,900])]`. Hangfire semantiği: 3 retry + 1 initial = 4 toplam attempt. `attemptNumber > MaxRetryAttempts` → `INotificationAdminAlertSink.RaiseDeliveryExhaustedAsync` çağrı + rethrow. Test: `RunAsync_TransientFailure_MarksFailedAndThrowsForRetry` (early attempt → no alert) + `RunAsync_FinalAttemptFailure_RaisesAdminAlertAndThrows` (attempt MaxRetryAttempts+1 → alert sink çağrılır). |
| 4 | NotificationDelivery kaydı oluşturma (kanal bazlı teslimat takibi) | ✓ | Dispatcher her enabled kanal için `NotificationDelivery` (PENDING + AttemptCount=0 + Channel + TargetExternalId) ekler. Job çalıştığında `AttemptCount++ + Status SENT/FAILED + SentAt + LastError` günceller. Test: `RunAsync_Success_MarksSentAndIncrementsAttempt`. |
| 5 | Lokalizasyon altyapısı: .resx, 4 dil, kanal bazlı format (placeholder metinler) | ✓ | `Resources/NotificationTemplates.{en,tr,zh,es}.resx` embedded resource family. `IStringLocalizer<NotificationTemplates>` ResourceManager culture chain ile fallback (en neutral). Test: `Resolve_TurkishLocale_ReturnsTurkishStrings`, `Resolve_ChineseLocale_ReturnsChineseStrings`, `Resolve_LocaleMissingForKey_FallsBackToEnglish` (tr.resx'te tanımsız `TRANSACTION_FLAGGED` → en'e düşer), `Resolve_UnknownLocaleString_FallsBackToInvariant`. **Kanal bazlı format** placeholder metinlerde 05 §7.3'te belirtildiği gibi tek-şablon; ileride HTML/Markdown variant'ları çoklu .resx ailesi (`...Email.resx`, `...Telegram.resx`) ile eklenir — T78–T80'in kapsamı. |

## Doğrulama Kontrol Listesi (11 §T37)

- [x] **02 §18.2 tüm bildirim tetikleyicileri tanımlı mı?** — Evet. Neutral (`en`) `NotificationTemplates.resx` `NotificationType` enum'undaki 20 değerin tümünü `_Title` + `_Body` ile kapsar. 02 §18.2 tablosundaki Satıcı (4) + Alıcı (5) + Her iki taraf (2) + Admin (4 — `ADMIN_FLAG_ALERT`/`ADMIN_ESCALATION`/`ADMIN_PAYMENT_FAILURE`/`ADMIN_STEAM_BOT_ISSUE`) + edge case (`PAYMENT_INCORRECT`, `LATE_PAYMENT_REFUNDED`, `ITEM_RETURNED`, `PAYMENT_REFUNDED`, `DISPUTE_RESULT`, `FLAG_RESOLVED`) — toplam 20 entry birebir.
- [x] **05 §7.5 retry stratejisi uygulanmış mı?** — Evet. `NotificationDeliveryJob` class-level `[AutomaticRetry(Attempts = 3, DelaysInSeconds = [60, 300, 900])]` → 1dk + 5dk + 15dk Hangfire-managed exponential backoff. Final attempt → `INotificationAdminAlertSink` invocation. Test: `RunAsync_FinalAttemptFailure_RaisesAdminAlertAndThrows`.

## Test Sonuçları

**Build (lokal, Release):**
```bash
dotnet build -c Release
```
→ Build succeeded. **0 Warning(s). 0 Error(s).** ~9 sn.

**Format verify (lokal):**
```bash
dotnet format --verify-no-changes
```
→ exit 0, değişiklik yok.

**Notifications unit testler (lokal, Release, no-Docker):**
```bash
dotnet test tests/Skinora.Notifications.Tests --filter "FullyQualifiedName~ResxNotificationTemplateResolverTests"
```
→ **6/6 PASS** — 929 ms.

**Notifications integration testler (lokal):** Lokal Docker çalışmadığı için (`docker_engine` named pipe inactive) `Testcontainers.MsSql` integration testleri **lokalde skip**. CI runner Linux Docker + SQL Server 2022 service container kullanır (T11.3 paterni); integration testler PR CI'da koşar.

**Tam backend test koşumu (lokal, Release, partial Docker):**

| Test projesi | Geçen | Bilgi |
|---|---|---|
| `Skinora.Notifications.Tests` (sadece unit) | **6** (Resx) | 18 toplam (12 integration Docker bekler) |

**Beklenen tam test envanteri (CI'da):**
- 6 unit + 12 integration (Notifications) = 18 yeni T37 test
- Mevcut 628 test (T36 baseline) + 18 = 646 toplam beklenen.

## Altyapı Değişiklikleri

- **Migration:** **yok**. `Notification`, `NotificationDelivery`, `UserNotificationPreference` entity'leri T23'te tanımlı; T37 yeni tablo/kolon eklemez.
- **Package reference (Skinora.Notifications):** 5 yeni — `Hangfire.Core 1.8.18`, `MediatR 12.4.1`, `Microsoft.Extensions.Localization 9.0.3`, `Microsoft.Extensions.Logging.Abstractions 9.0.3`, `Microsoft.EntityFrameworkCore 9.0.3`. Sürümler API host ile uyumlu (transitive collision yok). `RootNamespace=Skinora.Notifications` eklendi (folder `Resources/` namespace'i `Skinora.Notifications.Resources` ile çakışsın).
- **Package reference (Skinora.Notifications.Tests):** 3 yeni — `Microsoft.Extensions.DependencyInjection 9.0.3`, `Microsoft.Extensions.Localization 9.0.3`, `Microsoft.Extensions.Logging 9.0.3` (test SUT'unda IStringLocalizer<T> resolve için ServiceCollection kurmak zorunlu).
- **DI kayıtları (`NotificationsModule.cs`):** 7 scoped — `INotificationDispatcher`, `INotificationTemplateResolver`, `INotificationAdminAlertSink`, `NotificationDeliveryJob`, 3× `INotificationChannelHandler`. Microsoft.Extensions.Localization standard `AddLocalization()` çağrısı.
- **Hangfire global retry filter etkisi:** T09'daki global `AutomaticRetryAttribute` `Attempts` field'ı `HangfireOptions.DefaultRetryAttempts` ile mutate ediliyor. `NotificationDeliveryJob` class-level `[AutomaticRetry]` attribute'u global'den daha specific (Hangfire pipeline filter'ı specific-first uygular) — class attribute öncelik kazanır.

## Notlar

- **Working tree (Adım -1):** Temiz. `git status --short` boş.
- **Startup CI check (Adım 0):** Son 3 main run `24875329770` ✓, `24875329788` ✓, `24853931995` ✓ — hepsi success.
- **Dış varsayımlar (Adım 4):**
  - **`Microsoft.Extensions.Localization 9.0.3`:** .NET 9 release train ile yayımlandı; `Microsoft.EntityFrameworkCore 9.0.3` ile aynı tren. `dotnet restore` lokalde başarılı (kanıt: `Restored Skinora.Notifications.csproj (in 2.48 sec)`).
  - **`Hangfire.Core 1.8.18` `AutomaticRetryAttribute.DelaysInSeconds`:** 1.8.x'te property mevcut (zaten Skinora.API/HangfireModule'de `Attempts` field'ı set ediliyor). Class-level attribute uygulaması Hangfire pipeline filter convention'ı.
  - **`MediatR 12.4.1` `INotificationHandler<TEvent>`:** API host'ta zaten kullanımda (Outbox dispatcher `IPublisher.Publish`). Notifications module'üne sadece `MediatR` paketi eklendi (Contracts içinde `INotificationHandler` yok).
  - **.NET 9 .resx native EmbeddedResource convention:** `Microsoft.NET.Sdk` default davranışı `.resx` dosyalarını otomatik `EmbeddedResource` olarak işler (csproj'da explicit ItemGroup gerek değil) — kanıt: `dotnet build` sırasında resource'lar `Skinora.Notifications.dll`'e gömülü, `IStringLocalizer<T>` runtime resolution çalışır (test ile doğrulandı).
  - **CultureInfo fallback chain:** `tr-TR → tr → InvariantCulture` zinciri standart .NET ResourceManager davranışı. Test: `Resolve_LocaleMissingForKey_FallsBackToEnglish` doğruladı.
- **"3 deneme" yorumu (05 §7.5):** Plan tablosu "Exponential (1dk, 5dk, 15dk)" üç backoff değeri verir → 3 retry yorumu (initial + 3 retry = 4 attempt). Hangfire `Attempts` field'ı **retry sayısını** ifade eder (initial dahil değil), bu yorumla `Attempts = 3` doğru. Final attempt fail (attempt 4) → admin alert. Bu yorum T37 `NotificationDeliveryJob.MaxRetryAttempts` const + class-level `AutomaticRetry` ile kodlu; raporda açıkça not.
- **Hangfire async-method workaround (T32 mirror):** Hangfire `Expression<Action<T>>` overload'ı senkron method bekler. T32 `RefreshTokenCleanupJob` paternini izledim: `Execute(...)` sync wrapper `RunAsync(...).GetAwaiter().GetResult()` çağırır. Production'da Hangfire worker thread'i async core'u senkron şekilde sürdürür — kabul edilebilir çünkü her job bir delivery + bir HTTP call (saniyeler).
- **MediatR scan assembly registration:** T37 hiç concrete `INotificationHandler<TEvent>` register etmiyor (production handler yok), bu yüzden `OutboxModule.GetMediatRScanAssemblies()` listesine Notifications assembly'sini eklemedim. T44+'da ilk concrete handler eklendiğinde `OutboxModule.cs`'in `GetMediatRScanAssemblies()` metodu güncellenmeli. Bu T37'nin kapsamı dışında; T44 raporu bu noktayı vurgulamalıdır.
- **Bundled-PR check (Bitiş Kapısı):** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+(\.[0-9]+)?[a-z]?'` → sadece `T37` (commit henüz yok, branch sıfırlandı). Yabancı task commit'i girmesi imkânsız.
- **Post-merge CI watch:** Validate chat'i merge sonrası main CI'yi izler (validate.md Adım 18).

## Commit & PR

- **Branch:** `task/T37-notification-infrastructure`
- **Commit (kod):** TBD
- **Commit (rapor+status+memory):** TBD
- **PR:** TBD
- **CI run:** TBD
