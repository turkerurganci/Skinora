# T23 — Notification, NotificationDelivery, UserNotificationPreference Entity'leri

**Faz:** F1 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-15

---

## Yapılan İşler
- Notification entity (06 §3.13): platform içi bildirim, soft delete, FK → User + Transaction(opt)
- NotificationDelivery entity (06 §3.13a): dış kanal teslimat kaydı, workflow record (soft delete yok), 2 CHECK constraint
- UserNotificationPreference entity (06 §3.4): kanal tercihleri + dış hesap bağlantıları, soft delete, 2 filtered unique index
- NotificationsModuleDbRegistration + Program.cs module kaydı
- 25 integration test (6 Notification + 9 NotificationDelivery + 10 UserNotificationPreference)

## Etkilenen Modüller / Dosyalar

### Yeni Dosyalar
- `backend/src/Modules/Skinora.Notifications/Domain/Entities/Notification.cs`
- `backend/src/Modules/Skinora.Notifications/Domain/Entities/NotificationDelivery.cs`
- `backend/src/Modules/Skinora.Notifications/Domain/Entities/UserNotificationPreference.cs`
- `backend/src/Modules/Skinora.Notifications/Infrastructure/Persistence/NotificationConfiguration.cs`
- `backend/src/Modules/Skinora.Notifications/Infrastructure/Persistence/NotificationDeliveryConfiguration.cs`
- `backend/src/Modules/Skinora.Notifications/Infrastructure/Persistence/UserNotificationPreferenceConfiguration.cs`
- `backend/src/Modules/Skinora.Notifications/Infrastructure/Persistence/NotificationsModuleDbRegistration.cs`
- `backend/tests/Skinora.Notifications.Tests/Integration/NotificationEntityTests.cs`
- `backend/tests/Skinora.Notifications.Tests/Integration/NotificationDeliveryEntityTests.cs`
- `backend/tests/Skinora.Notifications.Tests/Integration/UserNotificationPreferenceEntityTests.cs`

### Değişen Dosyalar
- `backend/src/Modules/Skinora.Notifications/Skinora.Notifications.csproj` — Users + Transactions referans eklendi
- `backend/src/Skinora.API/Program.cs` — NotificationsModuleDbRegistration using + registration call
- `backend/tests/Skinora.Notifications.Tests/Skinora.Notifications.Tests.csproj` — Users + Transactions referans eklendi

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Notification entity: 06 §3.13'e göre (Type, UserId, TransactionId, Message, IsRead, vb.) | ✓ | Notification.cs — 8 domain field + BaseEntity (Id, CreatedAt, UpdatedAt, RowVersion) + ISoftDeletable (IsDeleted, DeletedAt) |
| 2 | NotificationDelivery entity: 06 §3.13a'ya göre (Channel, DeliveryStatus, TargetExternalId, LastError, RetryCount, vb.) | ✓ | NotificationDelivery.cs — 6 domain field (AttemptCount = RetryCount) + BaseEntity |
| 3 | UserNotificationPreference entity: 06 §3.4'e göre (Channel, IsEnabled, ExternalId, vb.) | ✓ | UserNotificationPreference.cs — 4 domain field + BaseEntity + ISoftDeletable |
| 4 | Unique: NotificationDelivery (NotificationId + Channel) | ✓ | UQ_NotificationDeliveries_NotificationId_Channel — unfiltered unique |
| 5 | Unique: UserNotificationPreference (UserId + Channel, filtered) | ✓ | UQ_UserNotificationPreferences_UserId_Channel — `WHERE IsDeleted = 0` |
| 6 | Unique: UserNotificationPreference (Channel + ExternalId, filtered) | ✓ | UQ_UserNotificationPreferences_Channel_ExternalId — `WHERE IsDeleted = 0 AND ExternalId IS NOT NULL` |
| 7 | Check constraint'ler: DeliveryStatus-specific | ✓ | CK_NotificationDeliveries_Sent_SentAt + CK_NotificationDeliveries_Failed_LastError |
| 8 | FK'ler: Notification→User, Transaction(opt) | ✓ | NotificationConfiguration.cs — HasOne<User>, HasOne<Transaction> |
| 9 | FK'ler: NotificationDelivery→Notification | ✓ | NotificationDeliveryConfiguration.cs — HasOne<Notification> |
| 10 | FK'ler: UserNotificationPreference→User | ✓ | UserNotificationPreferenceConfiguration.cs — HasOne<User> |
| 11 | Index'ler: Notification (UserId + IsRead) composite, CreatedAt | ✓ | IX_Notifications_UserId_IsRead + IX_Notifications_CreatedAt |
| 12 | Soft delete: UserNotificationPreference (kalıcı) | ✓ | HasQueryFilter(p => !p.IsDeleted) |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration | ✓ PASS | 25 test — CI TestContainers SQL Server, run `24474741835` (12/12 job success) |
| Build | ✓ 0 Error, 0 Warning | `dotnet build backend/Skinora.sln` — full solution build başarılı |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |
| Validator | Claude Opus 4.6 (ayrı chat, 2026-04-15) |
| Main CI Check | 3/3 success (PR #24 run 24367447405, PR #25 run 24367596947, PR #26 run 24471236084) |
| Task Branch CI | PR #27 run 24474741835 — 12/12 job success, CI Gate ✓ |
| Lokal Build | ✓ 0 Warning, 0 Error |
| Lokal Test | Docker daemon unavailable (cloud env) — CI evidence kullanıldı |
| Güvenlik | Secret sızıntısı yok, auth etkisi yok, yeni bağımlılık yok |
| Doküman uyumu | 06 §3.4, §3.13, §3.13a, §4.1, §5.1, §5.2 — tüm field, FK, index, constraint birebir uyumlu |
| Rapor karşılaştırması | Uyumlu — test sayıları düzeltildi (rapor 22 → gerçek 25) |

## Altyapı Değişiklikleri
- Migration: Yok (T28'de initial migration)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T23-notification-entities`
- Commit: `b11a2cc` — T23: Notification, NotificationDelivery, UserNotificationPreference entities
- PR: #27
- CI: ✓ PASS (run `24474741835` — 12/12 job success, integration test PASS)

## Known Limitations / Follow-up
- Yok

## Notlar
- **Working tree:** Temiz
- **Main CI Startup Check:** 3/3 success (PR #24 run 24367447405, PR #25 run 24367596947, PR #26 run 24471236084)
- **Dış varsayım:** Yok — tüm bağımlılıklar (T04, T17, T18, T19) tamamlanmış, enum'lar (NotificationType, NotificationChannel, DeliveryStatus) T17'de oluşturulmuş
- **Test sayıları (validator doğrulaması):** Notification 6 (CRUD 2 + soft delete 1 + FK 2 + null TransactionId 1), NotificationDelivery 9 (CRUD 2 + unique 2 + CHECK 4 + FK 1), UserNotificationPreference 10 (CRUD 2 + soft delete 1 + UserId+Channel unique 3 + Channel+ExternalId unique 3 + FK 1)
