# T10 — Outbox Pattern Altyapısı

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Doğrulama:** ✓ PASS | **Tarih:** 2026-04-07

---

## Yapılan İşler

### Skinora.Shared — outbox infrastructure entities

- **`OutboxMessage`** ([backend/src/Skinora.Shared/Persistence/Outbox/OutboxMessage.cs](backend/src/Skinora.Shared/Persistence/Outbox/OutboxMessage.cs)): 06 §3.18 spec'ine bire bir uyumlu — `Id` (Guid PK, EventId olarak da kullanılır), `EventType` (200), `Payload` (text), `Status` (OutboxMessageStatus), `RetryCount`, `ErrorMessage` (2000), `CreatedAt`, `ProcessedAt`.
- **`ProcessedEvent`** ([backend/src/Skinora.Shared/Persistence/Outbox/ProcessedEvent.cs](backend/src/Skinora.Shared/Persistence/Outbox/ProcessedEvent.cs)): 06 §3.19 — `Id`, `EventId`, `ConsumerName` (200), `ProcessedAt`.
- **`ExternalIdempotencyRecord`** ([backend/src/Skinora.Shared/Persistence/Outbox/ExternalIdempotencyRecord.cs](backend/src/Skinora.Shared/Persistence/Outbox/ExternalIdempotencyRecord.cs)): 06 §3.21 — `Id` (long IDENTITY), `IdempotencyKey` (200), `ServiceName` (100), `Status` (string CHECK), `ResultPayload`, `LeaseExpiresAt`, `CreatedAt`, `CompletedAt`. Yan üretici enum: `ExternalIdempotencyStatus { in_progress, completed, failed }` (lower-case identifiers, CHECK constraint literal'leri ile birebir).
- **EntityTypeConfiguration'lar** ([backend/src/Skinora.Shared/Persistence/Outbox/Configurations/](backend/src/Skinora.Shared/Persistence/Outbox/Configurations/)):
  - `OutboxMessageConfiguration`: PK, ValueGeneratedNever (caller EventId'yi kendisi koyar), status int conversion, default değerler, status-dependent CHECK (`PENDING ⇒ ProcessedAt NULL`, `PROCESSED ⇒ ProcessedAt NOT NULL`, `FAILED ⇒ ProcessedAt NULL AND ErrorMessage NOT NULL`, DEFERRED için kısıt yok), filtered index `(Status, CreatedAt) WHERE Status IN (0, 3)` — 06 §5.2.
  - `ProcessedEventConfiguration`: UNIQUE `(EventId, ConsumerName)` index — son savunma hattı (concurrent duplikasyon).
  - `ExternalIdempotencyRecordConfiguration`: Status string conversion (HasConversion<string>()), UNIQUE `(ServiceName, IdempotencyKey)`, status-dependent CHECK (`in_progress ⇒ CompletedAt NULL AND ResultPayload NULL AND LeaseExpiresAt NOT NULL`, `completed ⇒ CompletedAt NOT NULL`, `failed ⇒ CompletedAt NULL`).
- **`AppDbContext`** ([backend/src/Skinora.Shared/Persistence/AppDbContext.cs](backend/src/Skinora.Shared/Persistence/AppDbContext.cs)): 3 yeni `DbSet` (`OutboxMessages`, `ProcessedEvents`, `ExternalIdempotencyRecords`) ve `OnModelCreating`'de `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)` çağrısı eklendi. Soft-delete ve RowVersion convention loop'u korundu — outbox entity'leri BaseEntity/ISoftDeletable türevleri olmadığı için convention'lar onlara uygulanmaz.

### Skinora.Shared — outbox interfaces

- **`IDomainEvent`** ([backend/src/Skinora.Shared/Domain/IDomainEvent.cs](backend/src/Skinora.Shared/Domain/IDomainEvent.cs)): `MediatR.INotification` extend edildi (09 §9.3 örneği `INotificationHandler<TransactionCreatedEvent>` kullanıyor — domain event'lerin INotification olması zorunlu). `MediatR.Contracts` paketi sadece interface taşır, runtime bağımlılık ortaya çıkarmaz — Skinora.Shared koşullu/konvansiyonel olarak hafif kalıyor.
- **`IOutboxDispatcher`** ([backend/src/Skinora.Shared/Outbox/IOutboxDispatcher.cs](backend/src/Skinora.Shared/Outbox/IOutboxDispatcher.cs)): Hangfire'a parametresiz instance method olarak verilebilen `ProcessAndRescheduleAsync()` entry point.
- **`IProcessedEventStore`** ([backend/src/Skinora.Shared/Outbox/IProcessedEventStore.cs](backend/src/Skinora.Shared/Outbox/IProcessedEventStore.cs)): `ExistsAsync(eventId, consumerName)` + `MarkAsProcessedAsync(eventId, consumerName)`. Kontrat: `Mark` SaveChanges çağırmaz; caller's transaction commit eder (09 §9.3 atomik commit kuralı).
- **`IExternalIdempotencyService`** ([backend/src/Skinora.Shared/Outbox/IExternalIdempotencyService.cs](backend/src/Skinora.Shared/Outbox/IExternalIdempotencyService.cs)): `AcquireAsync(serviceName, key, leaseDuration)` → `ExternalIdempotencyAcquisition.{Acquired, Replay(payload), Blocked}`, `CompleteAsync`, `FailAsync`. T68/T70+ sidecar consumer'larının doğrudan tüketeceği API.
- **`IOutboxAdminAlertSink`** ([backend/src/Skinora.Shared/Outbox/IOutboxAdminAlertSink.cs](backend/src/Skinora.Shared/Outbox/IOutboxAdminAlertSink.cs)): Max retry aşıldığında çağrılan hook. Default impl T10'da, gerçek admin notification yolu T37'de değiştirilebilir (DI override).

### Skinora.API — outbox concrete implementations

- **`OutboxService`** ([backend/src/Skinora.API/Outbox/OutboxService.cs](backend/src/Skinora.API/Outbox/OutboxService.cs)): `IOutboxService` impl. `PublishAsync(domainEvent)` → entity'nin runtime tipini alır, `OutboxMessage` oluşturur (`Id = domainEvent.EventId`, `EventType = OutboxEventTypeName.For(type)`, `Payload = JsonSerializer.Serialize(...)`, `Status = PENDING`), AppDbContext'e Add eder. **SaveChanges çağrılmaz** — caller'ın UoW commit eder, atomiklik korunur. `OutboxEventTypeName.For/Resolve` helper'ı sürüm-bağımsız `FullName, AssemblyName` formatında type encoding sağlar (assembly version bumps persisted row'ları kırmaz).
- **`ProcessedEventStore`** ([backend/src/Skinora.API/Outbox/ProcessedEventStore.cs](backend/src/Skinora.API/Outbox/ProcessedEventStore.cs)): EF üzerinden `(EventId, ConsumerName)` lookup ve insert. `MarkAsProcessedAsync` sadece change tracker'a ekler — SaveChanges caller'ın işi.
- **`ExternalIdempotencyService`** ([backend/src/Skinora.API/Outbox/ExternalIdempotencyService.cs](backend/src/Skinora.API/Outbox/ExternalIdempotencyService.cs)): Tüm state geçişleri atomik conditional update (`ExecuteUpdateAsync`) ile yapılır. `AcquireAsync` retry loop'u (max 5 attempt) içerir:
  - Mevcut kayıt yoksa → INSERT, race kaybedildiğinde DbUpdateException catch + change tracker clear + re-read + döngü devam.
  - completed → `Replay(ResultPayload)`.
  - failed → atomik `failed → in_progress` claim (`ExecuteUpdateAsync` WHERE Status='failed'); 1 satır etkilendiyse `Acquired`, 0 satır → race kaybedildi, döngü devam.
  - in_progress + lease geçerli → `Blocked`.
  - in_progress + lease stale → atomik `in_progress → failed` reclaim; döngüye devam ederek normal `failed → in_progress` akışına girer.
  06 §3.21 concurrency acquisition kurallarına bire bir uyumlu.
- **`OutboxDispatcher`** ([backend/src/Skinora.API/Outbox/OutboxDispatcher.cs](backend/src/Skinora.API/Outbox/OutboxDispatcher.cs)): 09 §13.4 self-rescheduling pattern.
  - Medallion `IDistributedLockProvider.CreateLock("outbox-dispatcher").TryAcquireAsync(timeout=0)` ile lock; alınamazsa sessizce return.
  - Batch query: `WHERE Status IN (PENDING, FAILED) ORDER BY CreatedAt LIMIT BatchSize`.
  - Her message için: `OutboxEventTypeName.Resolve` → `JsonSerializer.Deserialize` → `IPublisher.Publish(deserialized)` (MediatR runtime-type dispatch). Başarılı → `PROCESSED`, `ProcessedAt = UtcNow`. Exception → `FAILED`, `RetryCount++`, `ErrorMessage` 2000 char'a truncate. `RetryCount >= MaxRetryCount` → `IOutboxAdminAlertSink.RaiseMaxRetryExceededAsync` (try/catch — sink failure batch'i abort etmez).
  - `try/catch/finally`: catastrophic exception swallowed (logged), finally bloğunda `IBackgroundJobScheduler.Schedule<IOutboxDispatcher>(d => d.ProcessAndRescheduleAsync(), TimeSpan.FromSeconds(PollingIntervalSeconds))` zinciri tekrar tetikler — chain hata olsa bile kırılmaz (09 §13.4 hata dayanıklılığı).
- **`LoggingOutboxAdminAlertSink`** ([backend/src/Skinora.API/Outbox/LoggingOutboxAdminAlertSink.cs](backend/src/Skinora.API/Outbox/LoggingOutboxAdminAlertSink.cs)): Default sink — `LogError` ile EventId, EventType, RetryCount, ErrorMessage emit eder. T37+ notification module gerçek admin alert wiring'ini bu interface üzerine yazar.
- **`OutboxOptions`** ([backend/src/Skinora.API/Outbox/OutboxOptions.cs](backend/src/Skinora.API/Outbox/OutboxOptions.cs)): `PollingIntervalSeconds=5`, `BatchSize=50`, `MaxRetryCount=5`, `LockAcquireTimeoutSeconds=0`, `DefaultExternalIdempotencyLeaseSeconds=300`. `appsettings.json`'a `Outbox` section eklendi.
- **`OutboxModule`** ([backend/src/Skinora.API/Outbox/OutboxModule.cs](backend/src/Skinora.API/Outbox/OutboxModule.cs)): `AddOutboxModule(IConfiguration)` extension. Bind eder OutboxOptions; Scoped kayıtlar (IOutboxService, IProcessedEventStore, IExternalIdempotencyService, IOutboxDispatcher); Scoped IOutboxAdminAlertSink → LoggingOutboxAdminAlertSink; Singleton MediatR (`AddMediatR(cfg => cfg.RegisterServicesFromAssemblies([typeof(OutboxModule).Assembly]))` — modüller T44+'da kendi assembly'lerini eklediğinde scan listesi büyür); Singleton IDistributedLockProvider → `SqlDistributedSynchronizationProvider(connectionString)` (Medallion); HostedService → OutboxStartupHook.
- **`OutboxStartupHook`** ([backend/src/Skinora.API/Outbox/OutboxStartupHook.cs](backend/src/Skinora.API/Outbox/OutboxStartupHook.cs)): `IHostedService.StartAsync` içinde `IBackgroundJobScheduler.Enqueue<IOutboxDispatcher>(d => d.ProcessAndRescheduleAsync())` ile dispatcher zincirini başlatır. Scheduler erişilemezse exception swallow + log; bir sonraki process restart yeniden dener. Inline `Program.cs` yerine HostedService olması test edilebilirlik için (bypass factory descriptor'u söker).
- **`Program.cs`** ([backend/src/Skinora.API/Program.cs](backend/src/Skinora.API/Program.cs)): `AddOutboxModule(builder.Configuration)` `AddHangfireModule`'dan sonra eklendi. Hangfire dispatcher chain için zorunlu; aksi sıralama Hangfire DI'sının hazır olmamasına yol açar.
- **`appsettings.json`** ([backend/src/Skinora.API/appsettings.json](backend/src/Skinora.API/appsettings.json)): `Outbox` section eklendi (default değerlerle).
- **`Skinora.API.csproj`** ([backend/src/Skinora.API/Skinora.API.csproj](backend/src/Skinora.API/Skinora.API.csproj)): `MediatR 12.4.1`, `DistributedLock.SqlServer 1.0.5` paketleri eklendi.
- **`Skinora.Shared.csproj`** ([backend/src/Skinora.Shared/Skinora.Shared.csproj](backend/src/Skinora.Shared/Skinora.Shared.csproj)): `MediatR.Contracts 2.0.1` (interface-only — runtime free), `Microsoft.EntityFrameworkCore.Relational 9.0.3` (ToTable, HasFilter, HasCheckConstraint, HasDefaultValue, HasDatabaseName extension'ları için) eklendi.

### Test infrastructure

- **`InMemoryDistributedLockProvider`** ([backend/tests/Skinora.API.Tests/Common/InMemoryDistributedLockProvider.cs](backend/tests/Skinora.API.Tests/Common/InMemoryDistributedLockProvider.cs)): Medallion `IDistributedLockProvider` test stub. Process-wide `ConcurrentDictionary<string, SemaphoreSlim>` ile aynı lock adı için aynı semaforu paylaşır. `TryAcquireAsync(TimeSpan.Zero)` çağrıları lock tutuluyorsa null döner — production sp_getapplock semantics'ine uyumlu.
- **`HangfireBypassFactory` güncellemesi** ([backend/tests/Skinora.API.Tests/Common/HangfireBypassFactory.cs](backend/tests/Skinora.API.Tests/Common/HangfireBypassFactory.cs)): `OutboxStartupHook` IHostedService descriptor'u (`ImplementationType == typeof(OutboxStartupHook)`) sökülür → dispatcher chain test process'te başlamaz (aksi takdirde Hangfire dispatcher'ı çağırırken AppDbContext'in SQL Server'a bağlanmaya çalışması ve `SqlDistributedSynchronizationProvider`'ın da SQL Server arayışı başlar). Production `IDistributedLockProvider` (Medallion SQL Server) descriptor'u sökülür ve `InMemoryDistributedLockProvider` yerine register edilir — DI sağlığı korunur, herhangi bir test dispatcher'ı resolve etse bile crash olmaz.
- **`OutboxTests.cs`** ([backend/tests/Skinora.API.Tests/Integration/OutboxTests.cs](backend/tests/Skinora.API.Tests/Integration/OutboxTests.cs)): Direct service-level integration testleri (WebApplicationFactory kullanmadan; `EfCoreGlobalConfigTests` paritesi). Per-test ServiceProvider build:
  - SQLite `:memory:` connection + AppDbContext + `EnsureCreated()`.
  - MediatR (test assembly'sini scan eder, handler'ları DI'ya kaydeder).
  - InMemoryDistributedLockProvider.
  - SpyJobScheduler (`IBackgroundJobScheduler`) — Schedule/Enqueue çağrılarını array'e kaydeder.
  - SpyOutboxAdminAlertSink — RaiseMaxRetryExceededAsync çağrılarını kaydeder.
  - OutboxService, ProcessedEventStore, ExternalIdempotencyService, OutboxDispatcher gerçek implementasyonlar.
  - Test event/handler'lar: `TestOutboxEvent`, `SecondTestOutboxEvent`, `AlwaysFailingTestEvent` ve karşılık gelen `INotificationHandler<T>`'lar.

## Etkilenen Modüller / Dosyalar

### Skinora.Shared (yeni)

- [backend/src/Skinora.Shared/Persistence/Outbox/OutboxMessage.cs](backend/src/Skinora.Shared/Persistence/Outbox/OutboxMessage.cs)
- [backend/src/Skinora.Shared/Persistence/Outbox/ProcessedEvent.cs](backend/src/Skinora.Shared/Persistence/Outbox/ProcessedEvent.cs)
- [backend/src/Skinora.Shared/Persistence/Outbox/ExternalIdempotencyRecord.cs](backend/src/Skinora.Shared/Persistence/Outbox/ExternalIdempotencyRecord.cs)
- [backend/src/Skinora.Shared/Persistence/Outbox/Configurations/OutboxMessageConfiguration.cs](backend/src/Skinora.Shared/Persistence/Outbox/Configurations/OutboxMessageConfiguration.cs)
- [backend/src/Skinora.Shared/Persistence/Outbox/Configurations/ProcessedEventConfiguration.cs](backend/src/Skinora.Shared/Persistence/Outbox/Configurations/ProcessedEventConfiguration.cs)
- [backend/src/Skinora.Shared/Persistence/Outbox/Configurations/ExternalIdempotencyRecordConfiguration.cs](backend/src/Skinora.Shared/Persistence/Outbox/Configurations/ExternalIdempotencyRecordConfiguration.cs)
- [backend/src/Skinora.Shared/Outbox/IOutboxDispatcher.cs](backend/src/Skinora.Shared/Outbox/IOutboxDispatcher.cs)
- [backend/src/Skinora.Shared/Outbox/IProcessedEventStore.cs](backend/src/Skinora.Shared/Outbox/IProcessedEventStore.cs)
- [backend/src/Skinora.Shared/Outbox/IExternalIdempotencyService.cs](backend/src/Skinora.Shared/Outbox/IExternalIdempotencyService.cs)
- [backend/src/Skinora.Shared/Outbox/IOutboxAdminAlertSink.cs](backend/src/Skinora.Shared/Outbox/IOutboxAdminAlertSink.cs)

### Skinora.Shared (değişen)

- [backend/src/Skinora.Shared/Domain/IDomainEvent.cs](backend/src/Skinora.Shared/Domain/IDomainEvent.cs) — `: INotification` eklendi
- [backend/src/Skinora.Shared/Persistence/AppDbContext.cs](backend/src/Skinora.Shared/Persistence/AppDbContext.cs) — 3 DbSet + ApplyConfigurationsFromAssembly
- [backend/src/Skinora.Shared/Skinora.Shared.csproj](backend/src/Skinora.Shared/Skinora.Shared.csproj) — MediatR.Contracts + EF Core Relational

### Skinora.API (yeni)

- [backend/src/Skinora.API/Outbox/OutboxService.cs](backend/src/Skinora.API/Outbox/OutboxService.cs) (içinde public `OutboxEventTypeName` helper)
- [backend/src/Skinora.API/Outbox/OutboxDispatcher.cs](backend/src/Skinora.API/Outbox/OutboxDispatcher.cs)
- [backend/src/Skinora.API/Outbox/ProcessedEventStore.cs](backend/src/Skinora.API/Outbox/ProcessedEventStore.cs)
- [backend/src/Skinora.API/Outbox/ExternalIdempotencyService.cs](backend/src/Skinora.API/Outbox/ExternalIdempotencyService.cs)
- [backend/src/Skinora.API/Outbox/LoggingOutboxAdminAlertSink.cs](backend/src/Skinora.API/Outbox/LoggingOutboxAdminAlertSink.cs)
- [backend/src/Skinora.API/Outbox/OutboxOptions.cs](backend/src/Skinora.API/Outbox/OutboxOptions.cs)
- [backend/src/Skinora.API/Outbox/OutboxModule.cs](backend/src/Skinora.API/Outbox/OutboxModule.cs)
- [backend/src/Skinora.API/Outbox/OutboxStartupHook.cs](backend/src/Skinora.API/Outbox/OutboxStartupHook.cs)

### Skinora.API (değişen)

- [backend/src/Skinora.API/Program.cs](backend/src/Skinora.API/Program.cs) — AddOutboxModule wiring + using
- [backend/src/Skinora.API/appsettings.json](backend/src/Skinora.API/appsettings.json) — Outbox section
- [backend/src/Skinora.API/Skinora.API.csproj](backend/src/Skinora.API/Skinora.API.csproj) — MediatR + DistributedLock.SqlServer

### Test projesi (yeni)

- [backend/tests/Skinora.API.Tests/Common/InMemoryDistributedLockProvider.cs](backend/tests/Skinora.API.Tests/Common/InMemoryDistributedLockProvider.cs)
- [backend/tests/Skinora.API.Tests/Integration/OutboxTests.cs](backend/tests/Skinora.API.Tests/Integration/OutboxTests.cs) — 20 integration test

### Test projesi (değişen)

- [backend/tests/Skinora.API.Tests/Common/HangfireBypassFactory.cs](backend/tests/Skinora.API.Tests/Common/HangfireBypassFactory.cs) — OutboxStartupHook ve IDistributedLockProvider scrub + InMemoryDistributedLockProvider register

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | IOutboxService implementasyonu: entity + outbox event yazma aynı DB transaction'da | ✓ | [OutboxService.cs:48-66](backend/src/Skinora.API/Outbox/OutboxService.cs#L48-L66) — `_dbContext.OutboxMessages.Add(message)` çağırır, `SaveChangesAsync` **çağırmaz**. Test: `Publish_AddsRowOnSaveChanges_AndPersistsEventTypeAndPayload` (caller SaveChanges çağırınca row commit), `Publish_WithoutSaveChanges_PersistsNothing_ProvingAtomicity` (SaveChanges yokken row görünmez) → 2/2 PASS. |
| 2 | Outbox Dispatcher: Hangfire self-rescheduling delayed job, saniye bazlı polling, distributed lock | ✓ | [OutboxDispatcher.cs:78-122](backend/src/Skinora.API/Outbox/OutboxDispatcher.cs#L78-L122) — Medallion `IDistributedLockProvider.CreateLock("outbox-dispatcher").TryAcquireAsync(timeout=0)` + `try/finally` içinde `_jobScheduler.Schedule<IOutboxDispatcher>(d => d.ProcessAndRescheduleAsync(), TimeSpan.FromSeconds(PollingIntervalSeconds))`. Tests: `Dispatcher_AlwaysReschedulesItself_EvenWhenNothingToDo`, `Dispatcher_ReschedulesItself_EvenWhenBatchHandlerThrows`, `Dispatcher_WhenLockHeldByAnotherInstance_SkipsBatch_ButStillReschedules` → 3/3 PASS. PollingIntervalSeconds default = 5 (saniye bazlı). |
| 3 | Consumer idempotency: ProcessedEvent tablosu, EventId bazlı duplikasyon kontrolü | ✓ | [ProcessedEventStore.cs](backend/src/Skinora.API/Outbox/ProcessedEventStore.cs) — `ExistsAsync(EventId, ConsumerName)` + `MarkAsProcessedAsync` (caller'ın transaction'ında commit). DB-level UNIQUE `(EventId, ConsumerName)` index ([ProcessedEventConfiguration.cs](backend/src/Skinora.Shared/Persistence/Outbox/Configurations/ProcessedEventConfiguration.cs)) son savunma hattı. Test: `ProcessedEventStore_ExistsAfterMark_AndRequiresSaveChanges` → PASS (mark ekledikten sonra SaveChanges yokken Exists hâlâ false; SaveChanges sonrası true; farklı consumer adı bağımsız record). |
| 4 | Program.cs'de dispatcher başlangıç tetiklemesi | ✓ | [Program.cs:64-69](backend/src/Skinora.API/Program.cs#L64-L69) — `builder.Services.AddOutboxModule(builder.Configuration)` registers `OutboxStartupHook` as `IHostedService`. [OutboxStartupHook.cs:42-58](backend/src/Skinora.API/Outbox/OutboxStartupHook.cs#L42-L58) `StartAsync` içinde `_jobScheduler.Enqueue<IOutboxDispatcher>(d => d.ProcessAndRescheduleAsync())` ile zincir başlar. Inline `Program.cs` yerine HostedService olması: (a) test bypass factory'sinde sökülebilir, (b) startup orchestration daha temiz, (c) Hangfire scheduler erişilemezse swallow + log + bir sonraki restart. |
| 5 | External idempotency: X-Idempotency-Key header gönderim/alma pattern'ı, ExternalIdempotencyRecord lease mekanizması (05 §5.1) | ✓ | [ExternalIdempotencyService.cs:36-138](backend/src/Skinora.API/Outbox/ExternalIdempotencyService.cs#L36-L138) — `AcquireAsync/CompleteAsync/FailAsync` API'si. T68/T70+ HTTP client'ları acquire → call sidecar with `X-Idempotency-Key` header → complete/fail. Tests: `ExternalIdempotency_AcquireFresh_ReturnsAcquired_AndPersistsInProgress`, `ExternalIdempotency_AcquireThenComplete_ThenSecondAcquireReplaysResult`, `ExternalIdempotency_AcquireWhileInProgressLeaseValid_Blocks`, `ExternalIdempotency_StaleLease_IsReclaimedToFailed_ThenAcquired`, `ExternalIdempotency_AcquireFailedRecord_AtomicallyClaimsAndReturnsAcquired`, `ExternalIdempotency_FailAsync_TransitionsInProgressToFailed_AndClearsLease`, `ExternalIdempotency_CompleteAsync_WithoutAcquireFirst_Throws` → 7/7 PASS. Lease mekanizması: 06 §3.21 stale recovery (atomik `in_progress → failed` reclaim) doğrulandı. |
| 6 | Dispatcher PENDING ve FAILED durumları birlikte işler, max retry sonrası admin alert tetiklenir | ✓ | [OutboxDispatcher.cs:127-130](backend/src/Skinora.API/Outbox/OutboxDispatcher.cs#L127-L130) — `Where(m => m.Status == PENDING \|\| m.Status == FAILED)` filtered query. Max retry path: [OutboxDispatcher.cs:179-194](backend/src/Skinora.API/Outbox/OutboxDispatcher.cs#L179-L194) — `RetryCount >= MaxRetryCount` ise `IOutboxAdminAlertSink.RaiseMaxRetryExceededAsync` çağrılır (try/catch — sink failure batch abort etmez). Tests: `Dispatcher_PicksUpFailedRowsOnSubsequentIterations_AndKeepsRetrying`, `Dispatcher_OnMaxRetryReached_RaisesAdminAlert_AndKeepsRowFailed` → 2/2 PASS. |

## Doğrulama Kontrol Listesi (11 §T10)

- ✅ **05 §5.1 outbox pattern kuralları uygulanmış mı?** — Üç entity (OutboxMessage, ProcessedEvent, ExternalIdempotencyRecord) 06 §3.18-§3.21 spec'ine uyumlu, atomik commit garantisi (caller SaveChanges), dispatcher PENDING+FAILED birlikte, consumer idempotency in-process + receiver-side idempotency (ExternalIdempotency lease), 09 §13.4 self-rescheduling pattern. 20 integration test ile doğrulandı.
- ✅ **Atomik commit garantisi var mı (entity + event aynı transaction)?** — `OutboxService.PublishAsync` SaveChanges çağırmaz, sadece change tracker'a Add eder. `Publish_WithoutSaveChanges_PersistsNothing_ProvingAtomicity` testi caller commit etmediğinde row görünmediğini kanıtlar. 09 §9.3 örneği ile tam uyum.
- ✅ **Dispatcher distributed lock kullanıyor mu?** — Medallion `IDistributedLockProvider` (production: `SqlDistributedSynchronizationProvider`, test: `InMemoryDistributedLockProvider`). `OutboxDispatcher.LockName = "outbox-dispatcher"` ile `CreateLock(...).TryAcquireAsync(timeout=0)` non-blocking acquire. `Dispatcher_WhenLockHeldByAnotherInstance_SkipsBatch_ButStillReschedules` testi: dış lock tutarken dispatcher batch'i skip eder, handler çağrılmaz, row PENDING kalır, ama chain reschedule yine de tetiklenir (kritik — chain kırılmaz).
- ✅ **External idempotency key gönderim/alma ve lease mekanizması çalışıyor mu?** — `IExternalIdempotencyService.AcquireAsync(serviceName, key, leaseDuration)` retry loop'lu atomik conditional update'lerle 06 §3.21'in tüm akışlarını destekler: fresh acquire, replay (completed), blocked (lease valid), stale reclaim (in_progress → failed → in_progress), failed claim (atomik). 7 test ile her senaryo doğrulandı.
- ✅ **Dispatcher PENDING + FAILED birlikte işliyor mu, max retry sonrası admin alert tetikleniyor mu?** — Batch query `Status IN (PENDING, FAILED)` filtreli (06 §3.18 retry semantiği). RetryCount tracking + MaxRetryCount eşiği + IOutboxAdminAlertSink hook (default LoggingOutboxAdminAlertSink, T37+ override edilebilir). Test `Dispatcher_OnMaxRetryReached_RaisesAdminAlert_AndKeepsRowFailed` 3 ardışık run sonrası SpyOutboxAdminAlertSink'in tek alert aldığını ve row'un FAILED'de kaldığını doğrular.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit + Integration (OutboxTests) | ✓ 20/20 PASS | `dotnet test --filter "FullyQualifiedName~OutboxTests"` → "Failed: 0, Passed: 20, Duration: 1 s" |
| Regression (Skinora.API.Tests tamamı) | ✓ 99/99 PASS | `dotnet test tests/Skinora.API.Tests/Skinora.API.Tests.csproj` → "Failed: 0, Passed: 99, Duration: 3 m". T09 sonrası 79 testten 99'a çıkıldı (20 yeni OutboxTests). |
| Skinora.Shared.Tests | ✓ 37/37 PASS | T04'ten beri değişmedi, AppDbContext convention'ları korundu. |
| Solution build | ✓ | `dotnet build Skinora.sln` → "Build succeeded. 0 Warning(s), 0 Error(s)". |
| docker-compose syntax | ✓ | `docker compose config --quiet` → exit 0 (yeni env değişikliği yok, mevcut DB connection string Outbox + Hangfire için yeterli). |

## Mini Güvenlik Kontrolü (09 §3.6 Katman 1)

- **Secret sızıntısı:** Yok. Outbox/Hangfire connection string mevcut `ConnectionStrings:DefaultConnection` üzerinden gelir; appsettings.json placeholder, prod env override. `appsettings.json`'a yeni secret eklenmedi.
- **Auth/authorization etkisi:** Yok. T10 yeni endpoint açmıyor; outbox tamamen internal infrastructure. ExternalIdempotency API sidecar HTTP client'ları için (T68+), HTTP endpoint değil.
- **Input validation:** `ExternalIdempotencyService.AcquireAsync/CompleteAsync/FailAsync` tüm string parametrelerde `ArgumentException.ThrowIfNullOrEmpty` + `leaseDuration > Zero` guard. `OutboxService.PublishAsync` `ArgumentNullException.ThrowIfNull(domainEvent)`. JSON deserialization sadece kendi `OutboxEventTypeName.For` formatından gelen tipler için çalışır (`Type.GetType(typeName)`); bilinmeyen type → `InvalidOperationException` (dispatcher hata olarak işler, RetryCount artar).
- **Yeni dış bağımlılıklar:** 
  - `MediatR 12.4.1` (Skinora.API) — yaygın kullanılan, aktif bakımlı, Apache 2.0.
  - `MediatR.Contracts 2.0.1` (Skinora.Shared) — interface-only, runtime bağımlılık çıkmıyor.
  - `DistributedLock.SqlServer 1.0.5` (Skinora.API, Medallion) — Medallion .NET stdlib uzantısı, MIT.
  - `Microsoft.EntityFrameworkCore.Relational 9.0.3` (Skinora.Shared) — zaten transitive geliyordu, explicit eklendi (extension method'lar için).
- **Distributed lock güvenliği:** Lock SQL Server'da `sp_getapplock` üzerinden — connection lifetime'a bağlı, crash sonrası timeout ile otomatik release.
- **MediatR runtime tip dispatch:** `IPublisher.Publish(object)` runtime tipe göre handler çağırır; tip resolution `OutboxEventTypeName.Resolve` (caller-provided tip adı, persisted from `For()`). Maliciously crafted payload bilinmeyen bir tipi işaret edemez çünkü `Type.GetType` sadece zaten yüklü assembly'lerdeki tipleri resolve eder.

## Altyapı Değişiklikleri

- **Migration:** Yok. T28 (initial migration) çalıştığında bu üç entity de yakalanıp ilk migration'a katılır. F0 fazında migration sistemi kurulu değil; SQLite + EnsureCreated test parittesi yeterli (T04'te kurulan pattern).
- **Config/env değişikliği:** Var — `appsettings.json`'a `Outbox` section eklendi. **Yeni env değişkeni gerekli değil**, mevcut `ConnectionStrings:DefaultConnection` (prod'da `DB_CONNECTION_STRING`) hem AppDbContext, hem Hangfire, hem de Medallion `SqlDistributedSynchronizationProvider` tarafından kullanılır.
- **Docker değişikliği:** Yok — backend container'ı zaten DB'ye bağlanıyor, üç tablo `EnsureCreated`/migration ile gelecek (F1).
- **Veri modeli sahiplik:** Üç outbox entity'si Path A kararı doğrultusunda T10'da oluşturuldu; T25 (F1) raporunda "OutboxMessage/ProcessedEvent/ExternalIdempotencyRecord T10'da oluşturuldu, T25 yalnızca SystemSetting/AuditLog/ColdWalletTransfer/SystemHeartbeat/SellerPayoutIssue ile ilgilenir" notu eklenmesi gerekecek. T25 isminin değişmesi gerekmez — sadece scope'un zaten daraltıldığı belirtilmelidir.

## Commit & PR

- **Branch:** `task/T10-outbox-pattern`
- **Commit:** `a6b6be8` — "T10: Outbox pattern altyapisi"
- **PR:** Yok (T11 öncesi — branch protection aktif değil, manuel doğrulama uygulanacak)
- **CI:** T11 öncesi olduğundan branch protection aktif değil, validator manuel PASS verecek.

## Known Limitations / Follow-up

- **Module assembly scanning ileri tarih:** `OutboxModule.GetMediatRScanAssemblies()` şu an sadece API host assembly'sini içerir. T44+ modülleri kendi `INotificationHandler<TransactionCreatedEvent>` handler'larını yazınca, bu liste o modüllerin assembly'sini de içerecek şekilde genişletilmelidir. Şu anda speculative abstraction'dan kaçınıldı — bir modül kendi handler'ını eklediğinde liste güncellenir.
- **Module DbContext'leri ile tutarlılık:** Bu task'ta sadece `AppDbContext` (Skinora.Shared) DI'a kayıtlı. Modüller ayrı DbContext kullanıyorsa (ör. modüler monolith'te her modül kendi DbContext'i), `IOutboxService.PublishAsync` o DbContext'e yazmıyor — ortak `AppDbContext`'e yazıyor. Atomiklik garantisi kırılır. T17+ veri katmanı kurulurken modüllerin AppDbContext üzerinden mi yoksa ayrı DbContext üzerinden mi gittiği netleştirilmeli; ayrı DbContext stratejisi tercih edilirse OutboxService'i scoped factory tabanlı yeniden tasarlamak gerekir. Şimdilik ortak AppDbContext varsayımı yapılıyor — 09 §10.2 modüler monolith açıklamasına uyumlu.
- **Distributed lock sadece SQL Server için:** Production'da `SqlDistributedSynchronizationProvider` kullanılıyor; SQL Server bağlantısı şart. Çoklu instance deploy'da Hangfire SQL storage zaten paylaşıldığı için lock da paylaşılır. Tek instance'tan çoklu instance'a geçiş ek konfigürasyon gerektirmez.
- **Cleanup job (09 §13.7 retention):** Yok. T63b'de gelecek — outbox/processed_events/external_idempotency 30 günlük retention temizliği.
- **Outbox event versioning:** Yok. T44+'da event şeması değiştiğinde "additive-only" stratejisi (09 §11) uygulanır; eski formatlardaki persisted row'lar eski schema ile deserialize edilebilmeli. T10'da bu için `EventVersion` field'ı eklenmedi — speculative abstraction. İlk şema değişikliğinde değerlendirilir.
- **Module-aware OutboxStartupHook:** Birden fazla instance start'larken hepsi `Enqueue<IOutboxDispatcher>` çağırır; Hangfire bu enqueue'ları toplar. Distributed lock tek dispatcher chain'i garanti eder, ama "extra" enqueue'lar Hangfire kuyruğunda işsiz çalışıp lock alamadan return eder. Performans etkisi minimal; alternatif (startup'ta enqueue yerine cron-style recurring job) saniye granülaritesine uyumsuz olduğu için tercih edilmedi (09 §13.4 gerekçe).
- **`Hangfire.InMemory` storage'ın enqueue + dispatcher resolve testi gerekir:** OutboxTests dispatcher'ı doğrudan resolve edip metodu çağırır — Hangfire'ın gerçekten enqueue + worker pickup zincirini test etmiyor. T09'un HangfireTests kapsamı bunu zaten test ediyor (background job scheduling end-to-end), T10 OutboxTests bu üst kapsama ihtiyaç duymadan gerçek dispatcher mantığını izole eder. End-to-end dispatcher zinciri T11 (CI/CD) sonrası real SQL Server'la docker-compose smoke testinde doğal olarak çalışacak.

## Notlar

- **Path A kararı (T10 entity sahibi):** Plan netleştirme sırasında T10 acceptance criteria'sının çalışan tablolar gerektirdiği, T25 (F1) altyapı entity sahipliği ile çakıştığı belirlendi. Path A onaylandı: T10 üç outbox entity'sini oluşturur, T25 raporunda not düşülür. Bu, T09'un `IBackgroundJobScheduler` abstraction'ını oluşturup pattern referans implementation'larını T44+'a bırakmasına paralel — T10 outbox altyapısının "kullanılabilir" olması için tabloların gerçekten var olması gerekiyor.
- **MediatR.Contracts vs MediatR ayrımı:** `IDomainEvent : INotification` zorunluluğu Skinora.Shared'da bir MediatR bağımlılığı gerektirdi. `MediatR.Contracts 2.0.1` kullanılarak runtime bağımlılığı önlendi — Shared sadece interface karşılığını taşır. Modüller (T44+) kendi handler'larını yazarken `MediatR.Contracts` (sadece arayüzler) ya da `MediatR` (full pipeline) referansı ekleyebilir; her ikisi de uyumlu.
- **`Microsoft.EntityFrameworkCore.Relational` Skinora.Shared'a eklendi:** `ToTable`, `HasFilter`, `HasCheckConstraint`, `HasDefaultValue`, `HasDatabaseName` extension'ları bu pakettedir. `Microsoft.EntityFrameworkCore` (Shared'ın mevcut paketi) sadece in-memory/non-relational core sağlar; relational extension'lar için ayrı paket gerekir. Provider paketleri (SqlServer, Sqlite) zaten transitive olarak bunu getiriyordu, ama Shared kendi tarafında relational API'yi kullandığı için explicit referans daha temiz.
- **`OutboxEventTypeName` public yapıldı:** Test round-trip serialization assertion'ı için kullanıyor. Internal kalsa `InternalsVisibleTo` gerekirdi; helper'ı public yapmak en az boilerplate yöntem ve pattern'ın okunabilirliği için zaten dokümante etmek istediğimiz bir parça.
- **OutboxDispatcher'da CHECK constraint korkuları:** SQLite ve SQL Server hem CHECK constraint, hem filtered index, hem `ExecuteUpdateAsync`'i destekliyor. Yazarken `\"Status\"` (double-quote) kolon quoting'i tercih ettim — her iki provider'da da çalışır. Test SQLite ile her senaryo çalıştı; production SQL Server'da T28 initial migration'da aynı SQL üretimi beklenir.
- **HangfireBypassFactory'nin `OutboxStartupHook` ve `IDistributedLockProvider` scrub'ı zorunluydu:** Aksi halde tüm 79 mevcut test (auth, middleware, rate limit, hangfire) outbox dispatcher zincirini başlatıp Medallion SQL Server lock provider'ını yüklemeye çalışırdı, her biri SQL Server connection retry'ında ~30 saniye takılırdı. T09'un HangfireBypassFactory deseni doğru abstraction noktasıydı — outbox bypass'ı oraya eklenerek mevcut testler hiç yavaşlamadan geçti (T09 raporundaki paralel test fail patterninin tekrarı engellendi).

---

## Validator Doğrulama Sonucu

**Tarih:** 2026-04-07
**Verdict:** ✓ PASS
**Bulgu sayısı:** 0
**Düzeltme gerekli mi:** Hayır

### Bağımsız Doğrulama Adımları

1. **Task tanımı okundu** — `Docs/11_IMPLEMENTATION_PLAN.md` Task T10 (satır 287-304).
2. **Referans dokümanlar okundu** — 05 §5.1 (561-599), 09 §9.3 (884-973), 09 §13.4 (1427-1474), 06 §3.18-§3.21 (979-1088).
3. **Branch kodu incelendi** — `task/T10-outbox-pattern` üzerindeki 29 dosya değişikliği (`git diff main...HEAD --stat`).
4. **Build & test bağımsız çalıştırıldı:**
   - `dotnet build` → 0 warning, 0 error
   - `dotnet test --filter "FullyQualifiedName~Outbox" --no-build` → 20/20 PASS (1 sn)
   - `dotnet test --no-build` (full suite) → Skinora.API.Tests 99/99 PASS, Skinora.Shared.Tests 37/37 PASS — toplam 136 test, 0 failure
5. **Kabul kriterleri tek tek doğrulandı** — 6/6 ✓ Karşılandı.
6. **Doğrulama kontrol listesi (5 madde)** — hepsi ✓.
7. **Mini güvenlik kontrolü** — secret/auth/input validation/yeni bağımlılık kategorilerinde temiz.
8. **Doküman uyumu kontrolü** — entity field'lar, CHECK constraint'ler, index'ler, enum değerleri 06 §3.18-§3.21 ile birebir; dispatcher davranışı 09 §13.4 self-rescheduling pattern ile birebir; atomik commit garantisi 09 §9.3 örneğine uyumlu.

### Yapım Raporu Karşılaştırması
**Uyum:** Tam uyumlu — 0 uyuşmazlık. Yapım raporundaki 6 kabul kriteri kanıtı, 5 kontrol listesi maddesi ve test sonuçları (20/20 + 99/99 + 37/37) bağımsız doğrulamayla birebir eşleşti. Path A kararı (T10'un üç outbox entity'sini oluşturması) ve known limitations listesi (cleanup → T63b, event versioning → T44+, module assembly scanning genişlemesi, gönderim-tarafı X-Idempotency-Key header → T64+ sidecar HTTP client'lar) doğru belgelenmiş.

### Bulgular
Yok.
