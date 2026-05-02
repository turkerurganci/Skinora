# T47 — Timeout Scheduling

**Faz:** F3 | **Durum:** ⏳ Yapım bitti (validate bekliyor) | **Tarih:** 2026-05-02

---

## Yapılan İşler

### Application paketi `Skinora.Transactions/Application/Timeouts/`

- **`ITimeoutSchedulingService` + `TimeoutSchedulingService`** — Per-transaction Hangfire timeout job orchestrator (05 §4.4 "Aşama ayrımı", 09 §13.3):
  - `SchedulePaymentTimeoutAsync(transactionId, ct)` — `ITEM_ESCROWED` durumundaki işlem için `ITimeoutExecutor.ExecutePaymentTimeoutAsync` schedule eder; `timeout_warning_ratio` SystemSetting'i set ise `IWarningDispatcher.DispatchWarningAsync` da schedule eder. Job ID'leri `Transaction.PaymentTimeoutJobId` + `TimeoutWarningJobId`'a yazar (caller `SaveChangesAsync`'i çağırır).
  - `CancelTimeoutJobsAsync(transactionId, ct)` — Hangfire'da iki job'u da silip Transaction üzerindeki ID'leri null'lar. Idempotent; eksik/silinmiş job toleranslı (cancel/complete/freeze pathleri aynı API'yi paylaşır).
  - `ReschedulePaymentTimeoutAsync(transactionId, remaining, newDeadline, ct)` — eski job'ları sil + yeniden schedule + yeni `PaymentDeadline` yaz. `TimeoutRemainingSeconds` field'ına dokunmaz (CK_Transactions_FreezePassive: `TimeoutFrozenAt IS NULL` iken `TimeoutRemainingSeconds` da NULL olmalı; freeze/resume yaşam döngüsü T50'nin sorumluluğu).
  - `WarningRatioKey = "timeout_warning_ratio"` const + invariant culture decimal okuma (07 §9.8 timeout kategori + 02 §3.4).
- **`ITimeoutExecutor` + `TimeoutExecutor`** — Per-tx payment timeout Hangfire job hedefi (09 §13.3 no-op pattern):
  - Defensive guard'lar: `Status != ITEM_ESCROWED` → no-op; `IsOnHold` → no-op; `TimeoutFrozenAt != null` → no-op; `PaymentDeadline > UtcNow` → no-op
  - Tüm guard'lar geçtiğinde `TransactionStateMachine.Fire(TransactionTrigger.Timeout)` (RowVersion guard + state machine domain primitive `CancelledAt` OnEntry, T44 pattern), sonra `SaveChangesAsync`.
  - `DomainException` (RowVersion mismatch / invalid state / on-hold) yakalanır, log'lanır, no-op'a düşülür — orphan/stale job'ların etkisi sıfırlanır.
  - Yan etkiler (refund flow, notification fan-out) **forward-deferred → T49** (refund) + T62 (SignalR) + T78–T80 (Email/Telegram/Discord).
- **`IWarningDispatcher` + `StubWarningDispatcher`** — Per-tx warning job hedefi:
  - Aynı no-op pattern + `TimeoutWarningSentAt != null` çift uyarı engeli (09 §13.3)
  - Happy path: `TimeoutWarningSentAt = UtcNow` set + log
  - Gerçek bildirim fan-out (Email/Telegram/Discord/SignalR) **forward-deferred → T48** (real warning impl `IWarningDispatcher`'i DI swap ile devralır, kontrat sabit).
- **`IDeadlineScannerJob` + `DeadlineScannerJob`** — Self-rescheduling Hangfire job (09 §13.4) — sub-minute granülarite için, OutboxDispatcher pattern'i:
  - Single-query EF Core: `IsOnHold=false AND TimeoutFrozenAt=null AND` 4 state'in ilgili deadline'ı `< UtcNow`. Frozen + held işlemler SQL seviyesinde elenir (no-op pattern budama).
  - Phase'ler: `CREATED → AcceptDeadline`, `TRADE_OFFER_SENT_TO_SELLER → TradeOfferToSellerDeadline`, `ITEM_ESCROWED → PaymentDeadline` (per-tx Hangfire job'a ek belt-and-suspenders), `TRADE_OFFER_SENT_TO_BUYER → TradeOfferToBuyerDeadline`.
  - Her overdue işlem için kendi state machine instance'ı (RowVersion guard) → `Fire(Timeout)` → batch sonunda tek `SaveChangesAsync`.
  - `try/catch + try/finally` ile self-reschedule garantisi (09 §13.4 "hata dayanıklılığı"); chain kırılma sebebi yok.
- **`TimeoutSchedulingOptions`** — Operasyonel tuning (`Timeouts:` appsettings section'ı): `DeadlineScannerIntervalSeconds=30`, `DeadlineScannerBatchSize=200`, `HeartbeatIntervalSeconds=30`, `RecoveryThresholdSeconds=60`. SystemSetting değil — `OutboxOptions`/`HangfireOptions` deseniyle uyumlu.

### Application paketi `Skinora.Platform/Application/Heartbeat/`

- **`IHeartbeatJob` + `HeartbeatJob`** — Self-rescheduling job (05 §4.4 heartbeat 30sn periyodik):
  - `SystemHeartbeats` singleton row'unun `LastHeartbeat` + `UpdatedAt`'ini `UtcNow`'a günceller (06 §3.23 + T26 seed)
  - Self-reschedule pattern (Outbox dispatcher + DeadlineScanner mirror); chain kırılma sebebi yok.
  - Singleton row'un yokluğunu (T26 seed kayıp) tolere eder — log + no-update + reschedule.
- **`HeartbeatOptions`** — `Heartbeat:IntervalSeconds=30` appsettings.

### API katmanı `Skinora.API/BackgroundJobs/Timeouts/`

- **`IRestartRecoveryService` + `RestartRecoveryService`** — Restart sonrası recovery (05 §4.4):
  - Outage window = `UtcNow - SystemHeartbeats.LastHeartbeat`
  - `outage < RecoveryThresholdSeconds` → no-op + `LastHeartbeat = UtcNow` (taze restart)
  - `outage ≥ threshold` → aktif işlemler taranır (`status ∈ {CREATED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, TRADE_OFFER_SENT_TO_BUYER} AND !IsOnHold AND TimeoutFrozenAt=null`); 4 deadline alanı outage kadar bump'lanır; `ITEM_ESCROWED` işlemler için ek olarak `ITimeoutSchedulingService.ReschedulePaymentTimeoutAsync` çağrılır (eski Hangfire job'lar silinir + yeni delay ile yeniden issue edilir, yeni `PaymentDeadline` ile uyumlu).
  - Sonunda `LastHeartbeat = UtcNow` (idempotent: ardışık çağrı no-op).
  - `RestartRecoveryResult` tip: outage + extension flag + extended count + rescheduled payment count → log + observability.
- **`TimeoutSchedulerStartupHook : IHostedService`** — Startup'ta zincir prime'lar (`OutboxStartupHook` desenini yansıtır):
  1. `IRestartRecoveryService.RunAsync` çalıştırılır (deadline extension)
  2. `IHeartbeatJob.TickAsync` Hangfire `Enqueue` ile prime'lanır → kendini reschedule eder
  3. `IDeadlineScannerJob.ScanAndRescheduleAsync` Hangfire `Enqueue` ile prime'lanır → kendini reschedule eder
  - Failure swallow + log; restart sonraki StartAsync zinciri tekrar prime'lar (host start'ı blocklamaz).

### DI ve modül kayıtları

- **`Skinora.API/Configuration/TransactionsModule.cs`** — `AddTransactionsModule(IConfiguration configuration)` overload'una geçildi (önceki arg-less imza yerine). Yeni 4 Scoped DI: `ITimeoutSchedulingService`, `ITimeoutExecutor`, `IDeadlineScannerJob`, `IWarningDispatcher`. `TimeoutSchedulingOptions` configuration binding (`Timeouts` section).
- **`Skinora.Platform/PlatformModule.cs`** — `IHeartbeatJob` Scoped DI eklendi. `HeartbeatOptions` binding'i Program.cs'de yapılır (Skinora.Platform `Microsoft.NET.Sdk` olduğu için `services.Configure<T>(IConfiguration)` extension'ı erişilebilir değil; Skinora.API `Microsoft.NET.Sdk.Web` ile gelir).
- **`Skinora.API/Program.cs`** — `AddTransactionsModule(builder.Configuration)` + `Configure<HeartbeatOptions>(...)` + `AddScoped<IRestartRecoveryService, RestartRecoveryService>()` + `AddHostedService<TimeoutSchedulerStartupHook>()` (Outbox hook'undan sonra).

### Migration / SystemSetting

- **Migration yok** — entity field'ları (PaymentTimeoutJobId, TimeoutWarningJobId, AcceptDeadline, TradeOfferToSellerDeadline, PaymentDeadline, TradeOfferToBuyerDeadline, IsOnHold, TimeoutFrozenAt, TimeoutRemainingSeconds, TimeoutWarningSentAt) T19/T44'ten beri mevcut.
- **SystemSetting yok** — `timeout_warning_ratio` zaten T26 seed'inde, operasyonel tuning (`Timeouts:`/`Heartbeat:`) appsettings'e gider (precedent: `OutboxOptions`, `HangfireOptions`, `RateLimitOptions`).

### Test paketi (`backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/` + Platform.Tests + API.Tests)

- **`TimeoutTestSupport.cs`** — `CapturingJobScheduler` (test double `IBackgroundJobScheduler`, expression+delay+jobId capture); `TimeoutTestFixtures.AddBuyerAsync` (User FK için); `NewTransaction` factory; `Options` IOptions builder.
- **`TimeoutSchedulingServiceTests.cs`** (8 integration, MsSqlContainer):
  - Happy path: payment + warning job'lar schedule edilir, jobId'ler entity'ye yazılır
  - Warning ratio konfigüre değil → yalnız payment job
  - Status guard: ITEM_ESCROWED dışındaysa throw
  - PaymentDeadline null → throw
  - Cancel: iki job da silinir, ID'ler null'lanır, `TimeoutWarningSentAt` da null'lanır
  - Cancel idempotent: hiç job yoksa no-op
  - Reschedule: eski job'lar silinir, yeni schedule edilir, `TimeoutRemainingSeconds` NULL kalır (CK_FreezePassive uyumu)
  - Reschedule warning skip: `TimeoutWarningSentAt != null` ise yeni warning schedule edilmez
- **`TimeoutExecutorTests.cs`** (7 integration, MsSqlContainer): happy path CANCELLED_TIMEOUT + 4 no-op state (state advanced, frozen, on-hold, future deadline); warning happy path + duplicate-warning no-op
- **`DeadlineScannerJobTests.cs`** (7 integration, MsSqlContainer): 3 phase happy path (CREATED, TRADE_OFFER_SENT_TO_SELLER, TRADE_OFFER_SENT_TO_BUYER), 2 no-op (frozen, on-hold), self-reschedule (45s interval), future deadline skip
- **`Skinora.Platform.Tests/Integration/HeartbeatJobTests.cs`** (3 integration, MsSqlContainer): tick updates LastHeartbeat + UpdatedAt; tick reschedules with configured interval; missing seed row → log + reschedule
- **`Skinora.API.Tests/Integration/Timeouts/RestartRecoveryServiceTests.cs`** (4 integration, MsSqlContainer): below-threshold no-op + heartbeat stamp; above-threshold extends 3 phase deadlines; above-threshold reschedules ITEM_ESCROWED Hangfire jobs (delete-and-reissue); frozen + on-hold işlemler skip

## Etkilenen Modüller / Dosyalar

**Yeni (12):**
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/ITimeoutSchedulingService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutSchedulingService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/ITimeoutExecutor.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutExecutor.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/IDeadlineScannerJob.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/DeadlineScannerJob.cs`
- `backend/src/Modules/Skinora.Platform/Application/Heartbeat/IHeartbeatJob.cs`
- `backend/src/Modules/Skinora.Platform/Application/Heartbeat/HeartbeatJob.cs`
- `backend/src/Modules/Skinora.Platform/Application/Heartbeat/HeartbeatOptions.cs`
- `backend/src/Skinora.API/BackgroundJobs/Timeouts/IRestartRecoveryService.cs`
- `backend/src/Skinora.API/BackgroundJobs/Timeouts/RestartRecoveryService.cs`
- `backend/src/Skinora.API/BackgroundJobs/Timeouts/TimeoutSchedulerStartupHook.cs`

**Test (5):**
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutTestSupport.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutSchedulingServiceTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/TimeoutExecutorTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Timeouts/DeadlineScannerJobTests.cs`
- `backend/tests/Skinora.Platform.Tests/Integration/HeartbeatJobTests.cs`
- `backend/tests/Skinora.API.Tests/Integration/Timeouts/RestartRecoveryServiceTests.cs`

**Değişen (4):**
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — `IConfiguration` parametre + 4 yeni DI + `TimeoutSchedulingOptions` bind
- `backend/src/Modules/Skinora.Platform/PlatformModule.cs` — `IHeartbeatJob` DI
- `backend/src/Skinora.API/Program.cs` — Heartbeat options bind + RestartRecoveryService DI + StartupHook hosted service + AddTransactionsModule(configuration)
- `backend/tests/Skinora.Platform.Tests/Skinora.Platform.Tests.csproj` — `Microsoft.Extensions.TimeProvider.Testing 9.0.0` package
- `backend/tests/Skinora.API.Tests/Skinora.API.Tests.csproj` — `Microsoft.Extensions.TimeProvider.Testing 9.0.0` package

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Her state geçişinde ilgili timeout Hangfire delayed job olarak schedule edilir | ~ Kısmi | `ITimeoutSchedulingService.SchedulePaymentTimeoutAsync` ITEM_ESCROWED için per-tx Hangfire job; non-payment phase'ler `IDeadlineScannerJob` ile enforce — 05 §4.4 "Aşama ayrımı" notu. Service hazır; caller wiring (state machine OnEntry hook'u) **forward-devir** caller (T49 timeout-execution / T64–T68 EscrowItem callback) tarafına düşer. T44 plan kabul kriteri #5 "OnEntry/OnExit handlers" zaten Kısmi olarak T47'ye devredilmişti — T47 servisi ürettik, OnEntry tetikleyicisi sonraki task'larda. |
| 2 | Job ID entity'ye kaydedilir | ✓ | `SchedulePaymentTimeoutAsync` `Transaction.PaymentTimeoutJobId` + `TimeoutWarningJobId` write-back; integration test `Happy path` `persisted.PaymentTimeoutJobId` assertion |
| 3 | İptal/tamamlanma/state değişikliğinde mevcut job temizlenir ve yeni schedule yapılır | ✓ | `CancelTimeoutJobsAsync` (delete + null) + `ReschedulePaymentTimeoutAsync` (delete-and-reissue); `Cancel_Deletes_Both_Jobs_And_Nulls_Ids` + `Reschedule_Deletes_Old_Issues_New_*` integration testleri |
| 4 | Deadline scanner/poller job: AcceptDeadline, TradeOfferToSellerDeadline, TradeOfferToBuyerDeadline enforce | ✓ | `DeadlineScannerJob` 4 phase tek query; `Scanner_Fires_Timeout_On_Overdue_*` (3 state); `IsOnHold` + `TimeoutFrozenAt` SQL seviyesinde elenir → `Scanner_Skips_Overdue_When_Frozen` + `Scanner_Skips_Overdue_When_OnHold` |
| 5 | Heartbeat job: 30sn periyodik, LastHeartbeat güncelleme | ✓ | `HeartbeatJob.TickAsync` self-reschedule (`Tick_Reschedules_Itself_With_Configured_Interval` 45s test); `Tick_Updates_LastHeartbeat_To_Utc_Now` SeedConstants.SystemHeartbeatId singleton row UPDATE; default `HeartbeatOptions.IntervalSeconds = 30` |
| 6 | Restart recovery: outage window hesaplama, aktif işlem timeout'larını uzatma | ✓ | `RestartRecoveryService.RunAsync` outage = `UtcNow - LastHeartbeat`; threshold üstü → `Above_Threshold_Outage_Extends_All_Active_Phase_Deadlines` (3 phase), `Above_Threshold_Outage_Reschedules_ITEM_ESCROWED_Payment_Jobs` (delete-and-reissue), `Frozen_And_Held_Transactions_Are_Skipped`; threshold altı → `Below_Threshold_Outage_Stamps_Heartbeat_And_Skips_Extension` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Integration TimeoutSchedulingService | ✓ 8/8 | MsSqlContainer; 4 phase + cancel + reschedule + warning skip |
| Integration TimeoutExecutor | ✓ 7/7 | 4 no-op state guard'ı + happy path + warning happy + duplicate guard |
| Integration DeadlineScanner | ✓ 7/7 | 3 phase happy + 2 no-op + self-reschedule + future skip |
| Integration HeartbeatJob | ✓ 3/3 | Tick + reschedule + missing-seed tolerance |
| Integration RestartRecovery | ✓ 4/4 | Below + above threshold + ITEM_ESCROWED reschedule + frozen/held skip |
| **T47 yeni testler** | **✓ 29/29** | 8 + 7 + 7 + 3 + 4 |
| Skinora.Transactions.Tests | ✓ 385/385 | T46: 363 → T47: 385 (+22 = 8+7+7) |
| Skinora.Platform.Tests | ✓ 136/136 | T46: 133 → T47: 136 (+3 HeartbeatJob) |
| Skinora.API.Tests | ✓ 265/265 | T46: 261 → T47: 265 (+4 RestartRecovery) |
| Tüm 12 test assembly | ✓ 1215/1215 | Users 16 + Payments 6 + Admin 20 + Disputes 11 + Fraud 17 + Notifications 63 + Auth 93 + Steam 21 + Shared 182 + Platform 136 + Transactions 385 + API 265 |

Komut: `dotnet test -c Release --no-build` (Release).

## Doğrulama

| Kontrol | Sonuç | Detay |
|---|---|---|
| Build Release 0W/0E | ✓ | `dotnet build -c Release` 22 proje yeşil, 0 warning, 0 error |
| Format verify | ✓ | `dotnet format --verify-no-changes --verbosity quiet` exit=0 |
| Migration zinciri | — | T47 migration üretmedi (entity şeması zaten mevcut) |
| Working tree hygiene check | ✓ | `git status --short` task başında temiz; uncommitted değişiklik yok |
| Main CI startup check | ✓ | 3 ardışık run: `25252671939` (T46) ✓ + `25252671951` (T46) ✓ + `25250478338` (T45) ✓ |
| Dış varsayım doğrulama | ✓ | (1) Hangfire `BackgroundJob.Schedule(TimeSpan)` mevcut & UTC default (T09 setup); (2) `IBackgroundJobScheduler` abstraction zaten bound; (3) `SystemHeartbeats` singleton CK_Id=1 + T26 seed mevcut; (4) Transaction entity timeout/freeze fields T19/T44'ten beri mevcut; (5) sub-minute granülarite için **self-rescheduling delayed job** pattern (09 §13.4 + OutboxDispatcher precedent) — Hangfire 5-field cron 1dk minimum. |
| 09 §13.3 atomicity disclaimer | ✓ | `ITimeoutSchedulingService` xmldoc + executor no-op pattern (her job handler `Status` + `IsOnHold` + `TimeoutFrozenAt` + `Deadline` validate eder) |

### Doğrulama kontrol listesi (plan §T47)

- [x] **02 §3 tüm timeout adımları schedule ediliyor mu?** — 4 phase: per-tx Hangfire (`PaymentDeadline` → ITEM_ESCROWED) + scanner-driven (3 phase: AcceptDeadline / TradeOfferToSellerDeadline / TradeOfferToBuyerDeadline). Ayrıca PaymentDeadline scanner'da belt-and-suspenders olarak da var.
- [x] **05 §4.4 heartbeat ve recovery pattern'ları uygulanmış mı?** — `HeartbeatJob` 30s self-reschedule (LastHeartbeat update); `RestartRecoveryService` startup'ta outage hesaplar + deadline uzatır + ITEM_ESCROWED Hangfire jobları reschedule eder; threshold-altı no-op.

## Altyapı Değişiklikleri

- **Migration**: yok
- **SystemSetting**: yok (operasyonel tuning appsettings'e: `Timeouts:` + `Heartbeat:`)
- **DI yeni Scoped**: `ITimeoutSchedulingService`, `ITimeoutExecutor`, `IDeadlineScannerJob`, `IWarningDispatcher`, `IHeartbeatJob`, `IRestartRecoveryService`
- **DI yeni HostedService**: `TimeoutSchedulerStartupHook` (Outbox hook'undan sonra)
- **Test paketleri**: `Microsoft.Extensions.TimeProvider.Testing 9.0.0` Skinora.Platform.Tests + Skinora.API.Tests'e eklendi (Notifications.Tests + Auth.Tests + Transactions.Tests'te zaten vardı)
- **Yeni dış bağımlılık**: yok (Hangfire + EFCore + Microsoft.Extensions.Options.* zaten mevcut transitive dependencies)

## Known Limitations / Forward-Devir

- **OnEntry caller wiring (Kabul kriteri #1 kısmi)**: T47 schedule API'si hazır ama **state machine OnEntry hook'u** kabul kriteri #1'i tam karşılayan tetikleyici değil — caller (T49 timeout-execution / T64–T68 EscrowItem trade offer callback) `Fire(EscrowItem)` sonrası `await scheduling.SchedulePaymentTimeoutAsync(tx.Id)` çağrılacak. T44 plan kabul kriteri #5 (OnEntry handlers) zaten Kısmi olarak T47'ye + T62'ye devredilmişti — T47 servisi ürettik, lifecycle caller'ları sonraki task'larda.
- **T48 — Timeout warning bildirim fan-out**: `IWarningDispatcher` port + `StubWarningDispatcher` (TimeoutWarningSentAt stamp + log) hazır. T48 gerçek implementasyon (Email/Telegram/Discord/SignalR template'leri) DI swap ile devralır. Kontrat sabit.
- **T49 — Timeout execution refund flow**: T47 yalnız `TransactionTrigger.Timeout` fire ediyor; refund/payout-side logic (item refund TradeOffer, payment refund blockchain transfer) T49 sorumluluğunda.
- **T50 — Timeout freeze/resume + maintenance mode**: `ITimeoutSchedulingService.ReschedulePaymentTimeoutAsync` API'si hazır — T50 freeze/resume akışı bu API'yi `TimeoutRemainingSeconds` lifecycle'ı (set during freeze, clear on resume) ile beraber kullanacak. T47 reschedule hot path'i `TimeoutRemainingSeconds`'a dokunmuyor (CK_FreezePassive: TimeoutFrozenAt NULL iken NULL olmalı).
- **T62 — SignalR fan-out**: `BuyerAcceptedEvent`, `TransactionCreatedEvent` outbox event'leri T45/T46'dan zaten emit ediliyor; T47 yeni event üretmiyor. Notification fan-out'un SignalR push'u T62 sorumluluğunda.

## Notlar

- **Working tree hygiene check**: temiz (task başında `git status --short` boş çıktı).
- **Main CI startup check**: 3/3 success ardışık (`25252671939` T46 ✓ + `25252671951` T46 ✓ + `25250478338` T45 ✓).
- **CK_Transactions_FreezePassive ders**: ilk implementasyonda `ReschedulePaymentTimeoutAsync` `TimeoutRemainingSeconds = (int)Math.Floor(remaining.TotalSeconds)` set ediyordu → 06 schema constraint `(TimeoutFrozenAt IS NOT NULL) OR (TimeoutFreezeReason IS NULL AND TimeoutRemainingSeconds IS NULL)` ile çakıştı (CHECK ihlali). Düzeltme: T47 reschedule path'i `TimeoutRemainingSeconds`'a dokunmuyor; freeze/resume yaşam döngüsü T50'nin sorumluluğu (set during freeze, clear on resume + reschedule). Doc-implementation tutarlılığı: 05 §4.4 "Otorite: Reschedule'ın kaynağı `TimeoutRemainingSeconds`" → bu yalnız freeze/resume hot path'inde geçerli; restart recovery sadece deadline absolute bump yapar (transactions zaten frozen değil).
- **CK_Transactions_FreezeActive ders**: test fixture'larda `TimeoutFrozenAt` set ediliyorsa **mutlaka** `TimeoutFreezeReason` + `TimeoutRemainingSeconds` da set edilmeli (tüm 4 frozen test'te düzeltildi).
- **CK_Transactions_FreezeHold_Reverse + CK_Transactions_Hold ders**: `IsOnHold = true` set ediliyorsa **mutlaka** `EmergencyHoldAt` + `EmergencyHoldReason` + `EmergencyHoldByAdminId` (FK Users.Id) set edilmeli + `TimeoutFreezeReason = EMERGENCY_HOLD` + `TimeoutFrozenAt` NOT NULL. Test fixture'larda `_seller.Id` admin Id olarak yeniden kullanıldı (FK rastgele Guid → constraint violation).
- **Scope architecture not (Sıkı Scope/Boundary kontrolü)**: `RestartRecoveryService` cross-modül (SystemHeartbeats ⊕ Transaction) olduğu için Skinora.API'de yaşıyor (`OutboxStartupHook` precedent); Skinora.Transactions/Platform sadece kendi domain'lerini orchestre ediyor. `IBackgroundJobScheduler` abstraction'ı zaten Skinora.Shared'de — Hangfire dependency Skinora.API hosting layer'da kalıyor (modüller direkt Hangfire.Core'a bağlanmıyor).

## Commit & PR

- **Branch**: `task/T47-timeout-scheduling`
- **Commit**: `f76e0d9`
- **PR**: [#77](https://github.com/turkerurganci/Skinora/pull/77)
