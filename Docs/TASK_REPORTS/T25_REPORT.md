# T25 — Altyapı Entity'leri

**Faz:** F1 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-04-18

---

## Yapılan İşler
- **Yeni modül:** `Skinora.Platform` — platform-wide infrastructure entity'leri
- **SystemSetting** (06 §3.17): admin-yönetimli platform parametreleri, DataType CHECK (`int`/`decimal`/`bool`/`string`), unfiltered unique Key, Category index, FK User (UpdatedByAdminId, opt)
- **SystemHeartbeat** (06 §3.23): singleton (Id = 1 CHECK), platform uptime takibi
- **AuditLog** (06 §3.20): immutable audit trail, append-only (IAppendOnly), 5 performans index, FK User (ActorId, UserId opt)
- **ColdWalletTransfer** (06 §3.22): hot→cold wallet ledger, append-only, unique TxHash, FK User (InitiatedByAdminId)
- **SellerPayoutIssue** (06 §3.8a): satıcı payout sorunu takibi, state-dependent CHECK (ESCALATED/RESOLVED/RETRY_SCHEDULED), filtered unique (TransactionId WHERE != RESOLVED), FK User + Transaction
- **Outbox infrastructure (T10 pre-existing):** OutboxMessage, ProcessedEvent, ExternalIdempotencyRecord — 06 §3.18–§3.21'e uyumlu olarak zaten kayıtlı. T25 acceptance için DB-level CHECK + unique test coverage eklendi.
- **Append-only enforcement:** `IAppendOnly` marker interface + `AppDbContext.EnforceAppendOnly()` — AuditLog, ColdWalletTransfer ve TransactionHistory üzerinde `Modified`/`Deleted` ChangeTracker entry'leri `InvalidOperationException` ile reddedilir.
- **Platform / Payments modül kayıtları** Program.cs'e eklendi.
- **23 integration test** (Platform: 13, Payments: 6, Transactions: 10, Shared: 8 outbox) — hepsi TestContainers SQL Server'da çalışıyor (CI).

## Etkilenen Modüller / Dosyalar

### Yeni Dosyalar
- `backend/src/Modules/Skinora.Platform/Skinora.Platform.csproj`
- `backend/src/Modules/Skinora.Platform/Domain/Entities/SystemSetting.cs`
- `backend/src/Modules/Skinora.Platform/Domain/Entities/SystemHeartbeat.cs`
- `backend/src/Modules/Skinora.Platform/Domain/Entities/AuditLog.cs`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingConfiguration.cs`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemHeartbeatConfiguration.cs`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/AuditLogConfiguration.cs`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/PlatformModuleDbRegistration.cs`
- `backend/src/Modules/Skinora.Payments/Domain/Entities/ColdWalletTransfer.cs`
- `backend/src/Modules/Skinora.Payments/Infrastructure/Persistence/ColdWalletTransferConfiguration.cs`
- `backend/src/Modules/Skinora.Payments/Infrastructure/Persistence/PaymentsModuleDbRegistration.cs`
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/SellerPayoutIssue.cs`
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/SellerPayoutIssueConfiguration.cs`
- `backend/src/Skinora.Shared/Domain/IAppendOnly.cs`
- `backend/tests/Skinora.Platform.Tests/Skinora.Platform.Tests.csproj`
- `backend/tests/Skinora.Platform.Tests/Integration/SystemSettingEntityTests.cs`
- `backend/tests/Skinora.Platform.Tests/Integration/SystemHeartbeatEntityTests.cs`
- `backend/tests/Skinora.Platform.Tests/Integration/AuditLogEntityTests.cs`
- `backend/tests/Skinora.Payments.Tests/Integration/ColdWalletTransferEntityTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/SellerPayoutIssueEntityTests.cs`
- `backend/tests/Skinora.Shared.Tests/Integration/OutboxInfrastructureEntityTests.cs`

### Değişen Dosyalar
- `backend/Skinora.sln` — Skinora.Platform ve Skinora.Platform.Tests projeleri eklendi
- `backend/src/Modules/Skinora.Payments/Skinora.Payments.csproj` — Users ProjectReference eklendi
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/TransactionHistory.cs` — `IAppendOnly` marker eklendi (06 §4.2 uyumluluğu)
- `backend/src/Skinora.API/Skinora.API.csproj` — Skinora.Platform ProjectReference
- `backend/src/Skinora.API/Program.cs` — Platform + Payments module kayıtları
- `backend/src/Skinora.Shared/Persistence/AppDbContext.cs` — `EnforceAppendOnly()` + SaveChanges override
- `backend/tests/Skinora.Payments.Tests/Skinora.Payments.Tests.csproj` — Users ProjectReference eklendi
- `backend/tests/Skinora.Transactions.Tests/Skinora.Transactions.Tests.csproj` — Users ProjectReference eklendi

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | SystemSetting: Key, Value, DataType, Category, Description, IsConfigured, vb. Check: DataType IN ('int','decimal','bool','string') | ✓ | SystemSetting.cs — 7 domain field + BaseEntity. SystemSettingConfiguration.cs CK_SystemSettings_DataType_Valid CHECK constraint. |
| 2 | OutboxMessage: EventType, Payload, Status, ProcessedAt, ErrorMessage, vb. Check: status-specific | ✓ | `Skinora.Shared/Persistence/Outbox/OutboxMessage.cs` + config (T10) — status-specific CHECK constraint (PENDING/PROCESSED/FAILED/DEFERRED). Test coverage: OutboxInfrastructureEntityTests 4 test. |
| 3 | ProcessedEvent: EventId, ConsumerName. Unique: (EventId + ConsumerName) | ✓ | `Skinora.Shared/Persistence/Outbox/ProcessedEvent.cs` (T10) — UX_ProcessedEvents_EventId_ConsumerName unique index. Test coverage: 2 test. |
| 4 | ExternalIdempotencyRecord: ServiceName, IdempotencyKey, Status, LeaseExpiresAt, ResultPayload. Check: status-specific + Status IN (...) | ✓ | `Skinora.Shared/Persistence/Outbox/ExternalIdempotencyRecord.cs` (T10) — CK_ExternalIdempotencyRecords_Status_Invariants CHECK, UX_ExternalIdempotencyRecords_ServiceName_IdempotencyKey unique. Test coverage: 4 test. |
| 5 | AuditLog: ActorType, ActorId, Action, EntityType, EntityId, Detail, vb. FK: ActorId→User, UserId→User(opt). Append-only (immutable) | ✓ | AuditLog.cs — 11 field, IAppendOnly. AuditLogConfiguration.cs — 5 perf index (ActorId, UserId, EntityType+EntityId, Action, CreatedAt). Immutability: AppDbContext.EnforceAppendOnly() + test AuditLog_Update_Rejected_By_AppendOnlyGuard & AuditLog_Delete_Rejected_By_AppendOnlyGuard. |
| 6 | ColdWalletTransfer: TxHash (unique), Amount, vb. Append-only | ✓ | ColdWalletTransfer.cs — 8 field, IAppendOnly. Config UQ_ColdWalletTransfers_TxHash unique. Tests ColdWalletTransfer_Update_Rejected_By_AppendOnlyGuard & ColdWalletTransfer_Delete_Rejected_By_AppendOnlyGuard. |
| 7 | SystemHeartbeat: Id CHECK (Id = 1) singleton, LastHeartbeat | ✓ | SystemHeartbeat.cs — Id=1, LastHeartbeat, UpdatedAt. Config CK_SystemHeartbeats_Singleton CHECK. Test SystemHeartbeat_SecondRow_RejectedBy_CheckConstraint. |
| 8 | SellerPayoutIssue: 06 §3.8a. Check: status-specific. Unique: TransactionId (filtered WHERE != RESOLVED) | ✓ | SellerPayoutIssue.cs — 10 field + BaseEntity. Config CK_SellerPayoutIssues_Status_Invariants (ESCALATED/RESOLVED/RETRY_SCHEDULED) + UQ_SellerPayoutIssues_TransactionId_Active filtered unique. 10 test. |
| 9 | Tüm index'ler tanımlı (§5.2) | ✓ | SystemSetting.Category, AuditLog (ActorId, UserId, EntityType+EntityId, Action, CreatedAt), SellerPayoutIssue (TransactionId, SellerId, VerificationStatus filtered), ColdWalletTransfer (UQ TxHash), OutboxMessage (Status+CreatedAt filtered — T10) — 06 §5.2 tamamı. |
| 10 | AuditLog'a UPDATE/DELETE yapılamıyor mu? | ✓ | AppDbContext.EnforceAppendOnly() throws `InvalidOperationException` on Modified/Deleted entries for `IAppendOnly` entities. AuditLog_Update_Rejected_By_AppendOnlyGuard + AuditLog_Delete_Rejected_By_AppendOnlyGuard testleri. |
| 11 | SystemHeartbeat singleton garantisi var mı? | ✓ | CK_SystemHeartbeats_Singleton CHECK (Id = 1) — ikinci satır DB seviyesinde reddedilir. SystemHeartbeat_SecondRow_RejectedBy_CheckConstraint test. |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Unit | ✓ 150+15 passed | `dotnet test Skinora.Shared.Tests --filter !Integration` → 150/150 ✓, `dotnet test Skinora.API.Tests --filter !Integration` → 15/15 ✓ |
| Integration | ⏳ Pending (CI) | Docker daemon lokal sandbox'ta çalışmıyor (iptables kısıtı). 37+ entity testi CI'da TestContainers SQL Server ile koşulacak. |
| Build | ✓ 0 Error, 0 Warning | `dotnet build Skinora.sln` — 24 proje başarıyla build oldu (00:00:20) |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Validator chat'inde PASS/FAIL verilecek |
| Bulgu sayısı | 0 (yapım self-check) |
| Düzeltme gerekli mi | Hayır (yapım aşaması) |
| Main CI Check | ✓ 3/3 success — PR #28 `CI Gate` ✓ (T24, run 24611802347 — 12/12 job), önceki T23/chore PR'ları da yeşil |
| Task Branch CI | ⏳ Pending (push + PR sonrası) |
| Lokal Build | ✓ 0 Warning, 0 Error |
| Lokal Test | Docker unavailable → CI'da doğrulanacak |
| Güvenlik | Secret sızıntısı yok, auth etkisi yok (entity layer), input validation etkisi yok, yeni dış paket yok |
| Doküman uyumu | 06 §3.8a, §3.17–§3.23, §4.1, §4.2, §5.1, §5.2 — tüm field, CHECK, FK, cascade, index semantiği birebir uyumlu |

## Altyapı Değişiklikleri
- Migration: Yok (T28'de initial migration)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T25-infrastructure-entities`
- Commit: `0af4ac3` (code + doc) — squash merge öncesi
- PR: #29
- CI: ⏳ CI run bekleniyor

## Known Limitations / Follow-up
- **Integration test çalışması lokal engelli:** Cloud sandbox'ta Docker daemon kullanılamadığı için entity tests (TestContainers tabanlı) yalnızca CI'da çalıştırılıyor. Validator bu testleri CI run output'undan doğrulayacak.
- **TransactionHistory IAppendOnly yansıtması:** 06 §4.2 TransactionHistory'yi append-only olarak tanımlıyor; T19 döneminde enforcement eksikti. T25 scope'undaki IAppendOnly altyapısı introduce edilirken bu kayıp kapatıldı.

## Notlar
- **Working tree:** Temiz (task başlarken)
- **Main CI Startup Check:** ✓ PR #28 CI run 24611802347 — 11 success + 1 skipped (Guard, PR context doğru davranış); önceki 2 main run'ı için status tracker onayı.
- **Dış varsayım:** Yok — T04, T17, T18, T19 bağımlılıkları tamamlanmış durumda. Skinora.Users + Skinora.Shared BaseEntity/ISoftDeletable/IAuditableEntity sözleşmeleri hazır.
- **Modül dağıtımı:** SystemSetting/SystemHeartbeat/AuditLog yeni `Skinora.Platform` modülüne yerleşti (platform-wide infrastructure). ColdWalletTransfer wallet domain'ine ait olduğu için `Skinora.Payments`'e, SellerPayoutIssue transaction lifecycle'ına sıkıca bağlı olduğu için `Skinora.Transactions`'a yerleşti.
- **IAppendOnly desen:** Yeni `Skinora.Shared/Domain/IAppendOnly.cs` marker interface'i, `AppDbContext.EnforceAppendOnly()` ile her SaveChanges çağrısında Modified/Deleted entity'leri reddeder. Bu, 06 §4.2 "append-only entity'lerde DELETE/UPDATE tanımlı değildir" kuralının mekanik garantisidir.
- **Test dağıtımı:** Platform 13, Payments 6 (ColdWalletTransfer), Transactions 10 (SellerPayoutIssue), Shared 8 (outbox). Toplam yaklaşık 37 yeni integration test.
