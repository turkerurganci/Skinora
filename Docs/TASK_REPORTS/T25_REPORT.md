# T25 — Altyapı Entity'leri

**Faz:** F1 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-19

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
| Unit (API) | ✓ 99/99 passed | Validator: `dotnet test backend/tests/Skinora.API.Tests` → 99/99 ✓ (lokal sandbox, Docker gerektirmeyen suit) |
| Unit (Shared non-integration) | ✓ 150/154 passed | Validator lokal: 150 PASS + 4 DockerUnavailableException (TestContainers — CI'de koşuldu) |
| Integration (CI) | ✓ PASS | GitHub Actions task branch run 24613696778 — `4. Integration test` job `conclusion=success`, TestContainers SQL Server ortamında 37+ yeni test dahil tüm integration suite PASS. |
| Build | ✓ 0 Error, 0 Warning | Validator: `dotnet build Skinora.sln --nologo` — 24 proje başarıyla build (00:00:42) |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |
| Main CI Check (Adım 0) | ✓ 3/3 success — son 3 main commit: T24 (`0042e65`, run 24611802347 — 12/12 job), T23 (`5eb1c3c`, 12/12 job), chore skill sync #26 (`f6629dc`, docs-only) |
| Task Branch CI (Adım 8a) | ✓ PASS — PR #29 run 24613696778, `CI Gate ✓`, 11/11 job success (0. Guard skipped — PR context, doğru davranış) |
| Lokal Build | ✓ 0 Warning, 0 Error (validator) |
| Lokal Test | ✓ API 99/99, Shared 150/154 non-integration; 4 fail = DockerUnavailableException (sandbox limit — CI'de PASS) |
| Güvenlik | ✓ Secret sızıntısı yok, auth etkisi yok (entity layer), input validation etkisi yok, yeni dış paket yok (Skinora.Platform csproj yalnızca ProjectReference) |
| Doküman uyumu | ✓ 06 §3.8a, §3.17–§3.23, §4.1, §4.2, §5.1, §5.2 — tüm field, CHECK, FK, cascade, index semantiği birebir uyumlu |

## Altyapı Değişiklikleri
- Migration: Yok (T28'de initial migration)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T25-infrastructure-entities`
- Commit tip: `ba766b9` (Dockerfile COPY fix) ← `e44cccd` (report PR ref) ← `0af4ac3` (entities + tests)
- PR: #29
- CI: ✓ PASS (run 24613696778 — 11/11 job success)
- Validator branch: `claude/validate-t25-klavD`

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
