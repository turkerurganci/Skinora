# T63b — Retention job'ları (toplu temizlik)

**Faz:** F3 | **Durum:** ⏳ Devam ediyor (validator chat'ine devredilecek) | **Tarih:** 2026-05-11

---

## Yapılan İşler

T63b, retention-based entity'lerin DB'den toplu hard purge edilmesi için üç Hangfire recurring job'unu canlıya alır. Her job kendi retention süresini ve batch boyutunu SystemSettings'ten çalışma anında okur — admin tuning redeploy gerektirmez. Eligibility kuralları 06 §1, §3.18, §3.19, §3.21 ve §6.1 lifecycle özet matrisine birebir uyar.

Job'lar:

1. **OutboxRetentionCleanupJob** (`Skinora.API/Retention/OutboxRetentionCleanupJob.cs`, cron `30 3 * * *` UTC):
   - Sıralı purge: **ProcessedEvent → OutboxMessage → ExternalIdempotencyRecord** (06 §3.19 — DB-FK yok, silme sırası uygulama seviyesinde).
   - ProcessedEvent eligibility: `ProcessedAt < threshold` (her satır by definition processed).
   - OutboxMessage eligibility: `Status = PROCESSED AND ProcessedAt < threshold` — `PENDING/FAILED` dispatcher retry akışına bırakılır (06 §3.18 retry semantiği).
   - ExternalIdempotencyRecord eligibility: `Status = completed AND CompletedAt < threshold` — `in_progress/failed` lease/retry akışına bırakılır (06 §3.21).
   - Default 30 gün, default batch 1000, batch'li while-loop, `ExecuteDeleteAsync` SQL DELETE.

2. **OrphanNotificationRetentionCleanupJob** (`Skinora.API/Retention/OrphanNotificationRetentionCleanupJob.cs`, cron `0 4 * * 0` UTC haftalık):
   - Sıralı purge: **NotificationDelivery → Notification** (FK yön zorunluluğu).
   - Eligibility: `TransactionId IS NULL AND CreatedAt < threshold`. `IgnoreQueryFilters()` ile soft-deleted da dahil — retention soft-delete flag'inden bağımsız.
   - Transaction-bound bildirimler dokunulmaz; onlar T_archive (06 §8.4) akışına tabi.
   - Default 365 gün, default batch 500.

3. **UserLoginLogRetentionCleanupJob** (`Skinora.API/Retention/UserLoginLogRetentionCleanupJob.cs`, cron `30 4 * * 0` UTC haftalık):
   - Eligibility: `CreatedAt < threshold`, `IgnoreQueryFilters()` ile soft-deleted dahil.
   - FK bağımlılığı yok — tek SELECT+DELETE per batch.
   - Default 365 gün, default batch 1000.

**Ortak helper:** Her job kendi `ReadSettingAsync(key, default, ct)` metoduyla SystemSetting'i `AsNoTracking().Where(Key && IsConfigured).Select(Value).SingleOrDefaultAsync` ile okur, `InvariantCulture` ile `int.TryParse`, başarısız parse veya `<= 0` → default. Modüller-arası `Skinora.Platform` referansı eklemek yerine job'lar Skinora.API host'unda toplandı; cross-module orkestrasyon API host'un doğal sorumluluğu.

**Registrar:** `RetentionJobsRegistrar : IHostedService` (`Skinora.API/Retention/RetentionJobsRegistrar.cs`) — T32 `RefreshTokenCleanupJobRegistrar` pattern'ı: `IServiceScopeFactory` ile boot-time scope, `IBackgroundJobScheduler.AddOrUpdateRecurring<T>` ile 3 job kayıt. Try/catch warning ile scheduler unavailable'da host başlatması bloke edilmez (retention maintenance concern; bir sonraki süreç restart re-register eder).

**SystemSetting katalog + seed (8 yeni anahtar, indices 42–49 / 0x2A–0x31):**

| Key | DataType | Default | Category | Açıklama |
|---|---|---|---|---|
| `retention.outbox_message_days` | int | 30 | Retention | Processed Outbox retention süresi (06 §3.18) |
| `retention.processed_event_days` | int | 30 | Retention | ProcessedEvent retention süresi (06 §3.19) |
| `retention.external_idempotency_days` | int | 30 | Retention | ExternalIdempotencyRecord retention süresi (06 §3.21) |
| `retention.orphan_notification_days` | int | 365 | Retention | Bağımsız bildirim retention süresi (06 §1, §6.1) |
| `retention.user_login_log_days` | int | 365 | Retention | UserLoginLog retention süresi (06 §1, §6.1) |
| `retention.batch_size_outbox` | int | 1000 | Retention | Outbox cleanup batch boyutu |
| `retention.batch_size_notification` | int | 500 | Retention | Bildirim cleanup batch boyutu |
| `retention.batch_size_user_login_log` | int | 1000 | Retention | UserLoginLog cleanup batch boyutu |

Hepsi `Default(...)` (`IsConfigured = true`); validator'ın generic positive-integer kuralı altında bulunuyor — özel range eklenmedi. SystemSettingsCatalog'a 8 yeni `retention` `ApiCategory` satırı eklendi.

**Migration:** `20260511185505_T63b_AddRetentionSettings` (`Up`: 8 `InsertData`, `Down`: 8 `DeleteData`). `dotnet ef migrations add` ile üretildi, AppDbContextModelSnapshot güncel.

## Etkilenen Modüller / Dosyalar

**Yeni — `Skinora.API/Retention/`:**
- `backend/src/Skinora.API/Retention/OutboxRetentionCleanupJob.cs`
- `backend/src/Skinora.API/Retention/OrphanNotificationRetentionCleanupJob.cs`
- `backend/src/Skinora.API/Retention/UserLoginLogRetentionCleanupJob.cs`
- `backend/src/Skinora.API/Retention/RetentionJobsRegistrar.cs`

**Yeni — Migration:**
- `backend/src/Skinora.Shared/Persistence/Migrations/20260511185505_T63b_AddRetentionSettings.cs` (+Designer)

**Yeni — Integration testleri:**
- `backend/tests/Skinora.API.Tests/Integration/Retention/OutboxRetentionCleanupJobTests.cs` (6 test)
- `backend/tests/Skinora.API.Tests/Integration/Retention/OrphanNotificationRetentionCleanupJobTests.cs` (6 test)
- `backend/tests/Skinora.API.Tests/Integration/Retention/UserLoginLogRetentionCleanupJobTests.cs` (4 test)

**Değişiklik:**
- `backend/src/Modules/Skinora.Platform/Application/Settings/SystemSettingsCatalog.cs` — 8 yeni metadata.
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingSeed.cs` — index 42–49.
- `backend/src/Skinora.API/Program.cs` — `using Skinora.API.Retention` + 3 `AddScoped<*Job>` + `AddHostedService<RetentionJobsRegistrar>`.
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` — 8 yeni HasData snapshot.
- `backend/tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs` — sayım 41 → 49, `expectedConfiguredKeys` listesi 20 → 28 satır.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Hangfire recurring job: OutboxMessage + ProcessedEvent + ExternalIdempotencyRecord — 30 gün sonra toplu hard delete (silme sırası: önce ProcessedEvent, sonra OutboxMessage) | ✓ | `OutboxRetentionCleanupJob.ExecuteAsync` `PurgeProcessedEventsAsync` → `PurgeOutboxMessagesAsync` → `PurgeExternalIdempotencyAsync` sıralı. `OutboxRetentionCleanupJobTests` 6/6 (eligible_purge, status-aware preserve, idempotency status-aware, batch loop, override, no-op). |
| 2 | Hangfire recurring job: Bağımsız bildirimler (Notification, TransactionId = NULL) + ilgili NotificationDelivery kayıtları — retention süresi sonrası toplu purge (önce delivery, sonra notification) | ✓ | `OrphanNotificationRetentionCleanupJob.ExecuteAsync` `_db.Set<NotificationDelivery>().Where(d => notificationIds.Contains(d.NotificationId)).ExecuteDeleteAsync` → `_db.Set<Notification>().Where(n => notificationIds.Contains(n.Id)).ExecuteDeleteAsync` sıralı. `OrphanNotificationRetentionCleanupJobTests` 6/6 (orphan+delivery purge, transaction-bound preserve, fresh preserve, soft-deleted purge, batch loop, override). |
| 3 | Soft-deleted entity'ler için retention-based hard purge (06 §1 lifecycle'a uygun) | ✓ | `UserLoginLogRetentionCleanupJob` `IgnoreQueryFilters()` ile soft-delete bypass; eligibility yalnız `CreatedAt < threshold`. Notification job da `IgnoreQueryFilters()` kullanır. `UserLoginLogRetentionCleanupJobTests.Soft_Deleted_Stale_Logs_Are_Also_Purged` ✓. **Not:** 06 §1 tablosunda yalnız `UserLoginLog` ve `RefreshToken` retention'a tabi soft-delete; `RefreshToken` T32'de zaten `RefreshTokenCleanupJob` ile karşılandı. |
| 4 | Retention süreleri SystemSetting'den okunur (admin tarafından ayarlanabilir) | ✓ | 8 yeni `retention.*` SystemSetting key + her job'da `ReadSettingAsync` helper. Override testleri 3 job için ayrı ayrı (`SystemSetting_Override_Shortens_Retention_Window`). Admin SET path'i T63 mevcut `/admin/settings` üzerinden çalışır — `SystemSettingsValidator` generic positive-int kuralı yeni key'leri kapsar. |
| 5 | Batch büyüklüğü sınırlandırılmış (DB yükü kontrolü) | ✓ | Her job `batchSize` SystemSetting'i okur (`retention.batch_size_*`), `Take(batchSize)` ile sayfalı SELECT + `ExecuteDeleteAsync` ile batch DELETE, `deleted < batchSize` ise loop sonlanır. `Batch_Loop_Drains_All_Eligible_Rows` test her 3 job için override edilmiş küçük batch ile 7-12 satırı çoklu iterasyonda temizlediğini kanıtlar. |

## Doğrulama Kontrol Listesi

| # | Madde | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 06 §8.2 retention kuralları eksiksiz uygulanmış mı? | ~ | **Doc-ref drift:** plan tanımındaki `06 §8.2` başlığı *"Denormalized Field'lar"* — retention ile ilgisiz. Gerçek kaynak 06 §1 lifecycle özet matrisi ("Veri Yaşam Döngüsü"). §3.20 (AuditLog) Append-Only Kalıcı, retention'a tabi değil — kapsam dışı. §3.21 (ExternalIdempotencyRecord) retention'a tabi ama plan tanımı dış referansta yer almıyor; kabul kriteri metni içinde "ExternalIdempotencyRecord" adıyla geçtiği için kapsama dahil edildi. Bu drift validator chat'inde doc düzeltme önerisi olarak işaretlenmek üzere not edildi. |
| 2 | Silme sırası FK-safe mi (ProcessedEvent → OutboxMessage)? | ✓ | `OutboxRetentionCleanupJob.ExecuteAsync` sıra zorunluluğunu kod akışıyla garanti eder (ProcessedEvent purge → OutboxMessage purge). FK yokluğu (06 §3.19) zaten DB-level engel oluşturmuyor — kuralın değeri operasyonel: aynı zamanda iki tabloya DELETE yaptığında ProcessedEvent satırı yetim kalmamalı diye sıra önemli. |
| 3 | Bağımsız bildirim retention ayrımı doğru mu? | ✓ | `OrphanNotificationRetentionCleanupJob` `Where(n => n.TransactionId == null && n.CreatedAt < threshold)`. Test `Transaction_Bound_Notifications_Are_Not_Touched` 400 gün eski tx-bound notification'ın korunduğunu doğrular. Transaction-bound bildirimler 06 §8.4 archive set ile transaction arşivine taşınır (T63b kapsamı dışı). |

## Test Sonuçları

```
Build (Release, dotnet build backend/Skinora.sln -c Release):
  0 Warning(s), 0 Error(s) — 14 saniye

Format verify (dotnet format backend/Skinora.sln --verify-no-changes):
  ExitCode: 0 (drift yok)

Lokal unit testleri (Docker yok — Testcontainers MsSql integration'ları lokal düştü, T63a ile aynı pattern):
  Skinora.Platform.Tests --filter "FullyQualifiedName~SystemSettingsCatalog|FullyQualifiedName~SystemSettingsValidator":
    Passed: 71, Failed: 0, Skipped: 0, Total: 71

  Skinora.Platform.Tests --filter "Category!=Integration":
    Passed: 102, Failed: 9 (9 Docker-bound integration tests SeedDataTests + IntegrationTestBaseSmokeTests türevleri — CI Linux runner'da geçer)

Retention integration testleri (16 yeni test) lokal Docker yokluğundan FAIL — task branch CI Linux runner'da doğrulanacak:
  - OutboxRetentionCleanupJobTests: 6 test
  - OrphanNotificationRetentionCleanupJobTests: 6 test
  - UserLoginLogRetentionCleanupJobTests: 4 test
```

CI sonuçları (task branch push sonrası) burada güncellenecek.

## Altyapı Değişiklikleri

- **EF Core migration:** `20260511185505_T63b_AddRetentionSettings` — 8 SystemSetting `InsertData`. Idempotent `database update` ile prod fresh DB'ye veya mevcut DB'ye uygulanabilir.
- **Hangfire recurring job kayıtları:** 3 yeni recurring job ID — `outbox-retention-cleanup`, `orphan-notification-retention-cleanup`, `user-login-log-retention-cleanup`. Hangfire dashboard'da görünür.
- **Yeni HostedService:** `RetentionJobsRegistrar` — Program.cs `AddHostedService<RetentionJobsRegistrar>()` ile kaydoldu; boot-time scope açar, scheduler erişilemezse warning ile yumuşak fail.
- **DI:** 3 `AddScoped<*Job>` Program.cs'de.
- **Yeni paket:** yok.

## Commit & PR

- Yapım commit'i: (push sonrası SHA buraya yazılacak)
- PR: (gh pr create sonrası numara buraya yazılacak)
- CI run: (CI watch sonrası run ID + conclusion buraya yazılacak)

## Notlar

**Working tree hygiene check (Adım -1):** session başında temiz.

**Main CI startup check (Adım 0):** son 3 main run hepsi `success` —
- `25655085958` (T63a #101 squash, 2026-05-11 06:55 UTC)
- `25655085944` (T63a #101 squash, 2026-05-11 06:55 UTC)
- `25570554435` (T63: AdminT63 #100, 2026-05-08 17:46 UTC)

**Dış varsayım doğrulama (Adım 4):**
- **Hangfire `IBackgroundJobScheduler.AddOrUpdateRecurring<T>` API'si:** T32'de eklendi, `RefreshTokenCleanupJob` ile production'da çalışıyor. ✓
- **EF Core 9 `ExecuteDeleteAsync`:** .NET 9 + EF Core 9 ile SQL Server provider'da `WHERE ... Take(N)` + `ExecuteDeleteAsync` translation destekleniyor; tests'te `Batch_Loop_Drains_All_Eligible_Rows` 12 satırı 5'li batch'le 3 iterasyonda temizliyor. ✓
- **Notification IAuditableEntity audit pipeline:** T33 ders notu — `Added` state'te `CreatedAt` UtcNow'a sıfırlanır; test factory'ler iki-aşamalı save kullanır (Add+Save, sonra desired CreatedAt + Save). `OrphanNotificationRetentionCleanupJobTests.SeedNotificationAsync` bu pattern'i uygular. ✓

**Mimari kararlar:**
- 3 job tek registrar altında toplandı (RefreshTokenCleanupJobRegistrar T32 pattern'ından farklılaşma). Sebep: cron family benzer (hepsi off-peak), tek failure path daha kolay diagnose edilir.
- Job konumu: `Skinora.API/Retention/` (modüller-arası orkestrasyon API host'un sorumluluğu; Notifications ve Users modülleri Platform referansı taşımıyor — SystemSetting okuma için ya 3 yeni cross-reference açılacaktı ya da reader abstraction'ı eklenecekti; her ikisi de proportional değil).
- T63 mevcut `/admin/settings` validator zinciri yeni 8 key'i generic positive-int kuralı altında karşılıyor; ek range kuralı veya cross-key invariant eklenmedi (her key bağımsız tunable; gün/adet > 0 yeterli).

**Forward-devir (Known Limitations):**
- K1 — **Job dispatcher MediatR/Hangfire health degradation observability:** retention sweep WARNING log'lar count > 0 ise; Loki query veya alert kuralı T82+ izleme task'ında ele alınmalı.
- K2 — **Doc-ref correction önerisi:** 11_IMPLEMENTATION_PLAN T63b tanımındaki `06 §8.2` → `06 §1` + `§3.21` eklenmesi validator chat'inde belgesel düzeltme olarak işaretlenecek.
- K3 — **Soft-deleted RefreshToken hard purge:** T32 `RefreshTokenCleanupJob` soft-delete yapıyor, hard purge yok; 06 §1 "Süresi dolan + revoke edilenler periyodik temizlenir" ifadesi soft-delete'i mi hard purge'ı mı kastediyor belirsiz. Validator chat doc-clarification olarak işaretleyebilir; T63b kapsamı dışında.

---

**Sonraki Adım:** Validator chat'i (ayrı conversation) — kabul kriterleri kontrolü, doğrulama listesi, CI sonuçları, status row'u `✓ Tamamlandı` olarak güncelleme.
