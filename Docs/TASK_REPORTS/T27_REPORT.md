# T27 — Performans İndeksleri ve Filtered İndeksler

**Faz:** F1 | **Durum:** ⏳ Devam ediyor (validate bekliyor) | **Tarih:** 2026-04-20

---

## Yapılan İşler

- **35 performans index envanteri:** 06 §5.2'deki tüm index'ler T18–T25 entity task'ları sırasında ilgili `IEntityTypeConfiguration<T>` sınıflarına eklenmiş. T27'de satır-satır doğrulandı (aşağıda tablo).
- **Filter predicate validity kontrolü:** 7 filtered index'in `HasFilter()` ifadeleri SQL Server'ın filtered index predicate kısıtlarına göre incelendi; tamamı geçerli.
- **06 §5.2 doc clarification:** "Not" bloğuna SQL Server filtered index predicate kısıtı eklendi — `NOT IN`, `BETWEEN`, function call, CASE desteklenmez. Semantic `NOT IN (A, B, C)` ifadelerinin `HasFilter()` içinde `[Col] <> 'A' AND [Col] <> 'B' AND ...` zinciri olarak yazılması gerektiği not edildi. Bu kısıt, task başlangıcında denenen "Transaction.Status filter'ını `NOT IN` formuna normalize et" edit'i sırasında runtime break ile somutlandı ve geri alındı.
- **Kod değişikliği yok:** 35 index'in tümü zaten doğru deklere edildiği ve filter predicate'leri geçerli olduğu için `IEntityTypeConfiguration` dosyalarına dokunulmadı.

## Etkilenen Modüller / Dosyalar

### Değişen Dosyalar
- `Docs/06_DATA_MODEL.md` — §5.2 sonundaki "Not" bloğu genişletildi (SQL Server filtered index predicate kısıtı).
- `Docs/TASK_REPORTS/T27_REPORT.md` (bu dosya — yeni).
- `Docs/IMPLEMENTATION_STATUS.md` — T27 satırı `⏳ Devam ediyor` olarak işaretlendi.

### Dokunulmayan ama Doğrulanan Dosyalar
Entity configuration dosyaları (HasIndex çağrıları):
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionConfiguration.cs`
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionHistoryConfiguration.cs`
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/BlockchainTransactionConfiguration.cs`
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/PaymentAddressConfiguration.cs`
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/SellerPayoutIssueConfiguration.cs`
- `backend/src/Modules/Skinora.Steam/Infrastructure/Persistence/TradeOfferConfiguration.cs`
- `backend/src/Modules/Skinora.Notifications/Infrastructure/Persistence/NotificationConfiguration.cs`
- `backend/src/Modules/Skinora.Fraud/Infrastructure/Persistence/FraudFlagConfiguration.cs`
- `backend/src/Modules/Skinora.Disputes/Infrastructure/Persistence/DisputeConfiguration.cs`
- `backend/src/Modules/Skinora.Users/Infrastructure/Persistence/UserConfiguration.cs`
- `backend/src/Modules/Skinora.Users/Infrastructure/Persistence/UserLoginLogConfiguration.cs`
- `backend/src/Modules/Skinora.Auth/Infrastructure/Persistence/RefreshTokenConfiguration.cs`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingConfiguration.cs`
- `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/AuditLogConfiguration.cs`
- `backend/src/Skinora.Shared/Persistence/Outbox/Configurations/OutboxMessageConfiguration.cs`

## 35 İndeks Envanteri (06 §5.2 ↔ kod)

| # | Entity | Field(s) | Tip | HasDatabaseName | Dosya:Satır |
|---|---|---|---|---|---|
| 1 | Transaction | Status | Filtered (aktif) | IX_Transactions_Status_Active | TransactionConfiguration.cs:211 |
| 2 | Transaction | SellerId | Standard | IX_Transactions_SellerId | TransactionConfiguration.cs:215 |
| 3 | Transaction | BuyerId | Standard | IX_Transactions_BuyerId | TransactionConfiguration.cs:218 |
| 4 | Transaction | CreatedAt | Standard | IX_Transactions_CreatedAt | TransactionConfiguration.cs:221 |
| 5 | Transaction | EscrowBotId | Standard | IX_Transactions_EscrowBotId | TransactionConfiguration.cs:224 |
| 6 | TransactionHistory | TransactionId | Standard | IX_TransactionHistory_TransactionId | TransactionHistoryConfiguration.cs:56 |
| 7 | BlockchainTransaction | TransactionId | Standard | IX_BlockchainTransactions_TransactionId | BlockchainTransactionConfiguration.cs:137 |
| 8 | BlockchainTransaction | Status | Filtered (Pending) | IX_BlockchainTransactions_Status_Pending | BlockchainTransactionConfiguration.cs:140 |
| 9 | BlockchainTransaction | FromAddress | Standard | IX_BlockchainTransactions_FromAddress | BlockchainTransactionConfiguration.cs:144 |
| 10 | TradeOffer | TransactionId | Standard | IX_TradeOffers_TransactionId | TradeOfferConfiguration.cs:59 |
| 11 | TradeOffer | PlatformSteamBotId | Standard | IX_TradeOffers_PlatformSteamBotId | TradeOfferConfiguration.cs:62 |
| 12 | Notification | UserId + IsRead | Composite | IX_Notifications_UserId_IsRead | NotificationConfiguration.cs:51 |
| 13 | Notification | CreatedAt | Standard | IX_Notifications_CreatedAt | NotificationConfiguration.cs:54 |
| 14 | FraudFlag | Status | Filtered (Pending) | IX_FraudFlags_Status_Pending | FraudFlagConfiguration.cs:55 |
| 15 | FraudFlag | TransactionId | Standard | IX_FraudFlags_TransactionId | FraudFlagConfiguration.cs:59 |
| 16 | FraudFlag | UserId | Standard | IX_FraudFlags_UserId | FraudFlagConfiguration.cs:62 |
| 17 | Dispute | TransactionId | Standard | IX_Disputes_TransactionId | DisputeConfiguration.cs:62 |
| 18 | Dispute | Status | Filtered (Open,Escalated) | IX_Disputes_Status_Active | DisputeConfiguration.cs:65 |
| 19 | UserLoginLog | UserId | Standard | IX_UserLoginLogs_UserId | UserLoginLogConfiguration.cs:49 |
| 20 | UserLoginLog | IpAddress | Standard | IX_UserLoginLogs_IpAddress | UserLoginLogConfiguration.cs:52 |
| 21 | UserLoginLog | DeviceFingerprint | Standard | IX_UserLoginLogs_DeviceFingerprint | UserLoginLogConfiguration.cs:55 |
| 22 | User | DefaultPayoutAddress | Standard | IX_Users_DefaultPayoutAddress | UserConfiguration.cs:82 |
| 23 | User | DefaultRefundAddress | Standard | IX_Users_DefaultRefundAddress | UserConfiguration.cs:85 |
| 24 | OutboxMessage | Status + CreatedAt | Filtered (Pending,Failed) | IX_OutboxMessages_Status_CreatedAt_Pending | OutboxMessageConfiguration.cs:63 |
| 25 | PaymentAddress | MonitoringStatus | Filtered (aktif) | IX_PaymentAddresses_MonitoringStatus_Active | PaymentAddressConfiguration.cs:71 |
| 26 | RefreshToken | UserId | Standard | IX_RefreshTokens_UserId | RefreshTokenConfiguration.cs:61 |
| 27 | SystemSetting | Category | Standard | IX_SystemSettings_Category | SystemSettingConfiguration.cs:58 |
| 28 | AuditLog | ActorId | Standard | IX_AuditLogs_ActorId | AuditLogConfiguration.cs:70 |
| 29 | AuditLog | UserId | Standard | IX_AuditLogs_UserId | AuditLogConfiguration.cs:73 |
| 30 | AuditLog | EntityType + EntityId | Composite | IX_AuditLogs_EntityType_EntityId | AuditLogConfiguration.cs:76 |
| 31 | AuditLog | Action | Standard | IX_AuditLogs_Action | AuditLogConfiguration.cs:79 |
| 32 | SellerPayoutIssue | TransactionId | Standard | IX_SellerPayoutIssues_TransactionId | SellerPayoutIssueConfiguration.cs:76 |
| 33 | SellerPayoutIssue | SellerId | Standard | IX_SellerPayoutIssues_SellerId | SellerPayoutIssueConfiguration.cs:79 |
| 34 | SellerPayoutIssue | VerificationStatus | Filtered (aktif) | IX_SellerPayoutIssues_VerificationStatus_Active | SellerPayoutIssueConfiguration.cs:82 |
| 35 | AuditLog | CreatedAt | Standard | IX_AuditLogs_CreatedAt | AuditLogConfiguration.cs:82 |

**Toplam: 35/35 ✓**

## Filter Predicate Validity Tablosu

| Index | HasFilter() ifadesi | SQL Server geçerli mi | Not |
|---|---|---|---|
| IX_Transactions_Status_Active | `[Status] <> 'COMPLETED' AND [Status] <> 'CANCELLED_TIMEOUT' AND [Status] <> 'CANCELLED_SELLER' AND [Status] <> 'CANCELLED_BUYER' AND [Status] <> 'CANCELLED_ADMIN'` | ✓ | `<>` zinciri (NOT IN workaround) |
| IX_BlockchainTransactions_Status_Pending | `[Status] = 'PENDING'` | ✓ | Tek değer eşitliği |
| IX_FraudFlags_Status_Pending | `[Status] = 'PENDING'` | ✓ | Tek değer eşitliği |
| IX_Disputes_Status_Active | `[Status] IN ('OPEN', 'ESCALATED')` | ✓ | `IN` desteklenir |
| IX_OutboxMessages_Status_CreatedAt_Pending | `[Status] IN ('PENDING', 'FAILED')` | ✓ | `IN` desteklenir |
| IX_PaymentAddresses_MonitoringStatus_Active | `[MonitoringStatus] IN ('ACTIVE','POST_CANCEL_24H','POST_CANCEL_7D','POST_CANCEL_30D')` | ✓ | `IN` desteklenir |
| IX_SellerPayoutIssues_VerificationStatus_Active | `[VerificationStatus] <> 'RESOLVED'` | ✓ | Tek değer `<>` |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 06 §5.2'deki tüm index'ler tanımlı (35 index) | ✓ | Yukarıdaki 35-satırlık envanter tablosu — her satır için dosya + satır + HasDatabaseName eşleştirmesi. |
| 2 | Filtered index'ler HasFilter() ile SQL Server'a özgü tanımlanmış | ✓ | 7 filtered index'in tümü `HasFilter(...)` çağrısıyla tanımlanmış — bkz. "Filter Predicate Validity Tablosu". |
| 3 | Composite index'ler doğru sırada | ✓ | Notification(UserId, IsRead) ✓ — okunmamış bildirim sorgularında UserId önce, IsRead sonra; Notifications sayfalaması `WHERE UserId = ? AND IsRead = ?` pattern'i. AuditLog(EntityType, EntityId) ✓ — entity audit geçmişi sorgularında EntityType önce (seçicilik); OutboxMessage(Status, CreatedAt) ✓ — dispatcher `WHERE Status IN (...) ORDER BY CreatedAt` pattern'i için Status önce. |
| 4 | 06 §5.2'deki her index migration'da var mı? | ? Doğrulanamadı | Bu kriter T28 (initial migration) kapsamında doğrulanır — HasIndex deklarasyonları mevcut olduğu için T28'in `dotnet ef migrations add` çıktısında 35 index `migrationBuilder.CreateIndex` çağrısı olarak düşecektir. |
| 5 | Filtered index koşulları doğru mu? | ✓ | 7 filtered index'in HasFilter ifadesi 06 §5.2 semantic gereksinimleriyle eşleşir (`<>` zinciri Transaction'da `NOT IN` semantic'inin SQL Server-uyumlu karşılığıdır). |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 Error, 0 Warning | `dotnet build --nologo` (lokal) — 24 proje başarıyla build. |
| Unit testler | — | T27 scope'unda yeni unit test yok; kod değişikliği yok. |
| Integration testler (lokal regresyon) | ✓ 68/68 | `dotnet test tests/Skinora.Transactions.Tests/...` — 68/68 PASS (Testcontainers SQL Server). T27 revert sonrası regresyon kontrolü; filter predicate breaking edit'i yakalamak için Transactions modülü seçildi çünkü kırılan filter bu modüldeydi. |
| Integration testler (CI) | ⏳ PR push'tan sonra | CI runner sonucu PR'da raporlanacak. |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ (validate chat'ine havale) |
| Bulgu sayısı | 0 kritik / 1 doc gap (aşağıda) |
| Düzeltme gerekli mi | Hayır — doc gap bu task içinde düzeltildi |

## Altyapı Değişiklikleri
- Migration: Yok (T28'in scope'u)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T27-performance-indexes`
- Commit: `2f4fab7` — "T27: Performans index envanter + 06 §5.2 filtered index NOT IN kısıtı notu"
- PR: [#41](https://github.com/turkerurganci/Skinora/pull/41)
- CI: ⏳ run `24675534278` in_progress

## Known Limitations / Follow-up
- **SQL Server filtered index predicate kısıtı:** `NOT IN`, `BETWEEN`, function call, CASE desteklenmez. Transaction.Status filter'ı bu nedenle `<>` zinciri olarak yazılmış. 06 §5.2 doc'u bu kısıtı açıklayacak şekilde genişletildi (bu PR).
- **T28'e devir:** Migration sonrası index'lerin fiziksel olarak oluştuğunun SQL tarafında doğrulanması T28 doğrulama kontrol listesine aittir (kriter #4).

## Notlar

### Task Başlangıç Kanıtları
- **Working tree hygiene check (Adım -1):** temiz — `git status --short` boş çıktı.
- **Main CI startup check (Adım 0):** son 3 run tamamı `conclusion=success`:
  - `24639119531` (chore: memory — status verification feedback + T11.3 PASS yansıt, #40) ✓
  - `24639119511` (aynı PR Docker Publish) ✓
  - `24638836634` (T11.3: Shared MsSqlContainer fixture, #39) ✓
- **Dış varsayımlar (Adım 4):**
  - EF Core `HasFilter()` SQL Server'a özgü (beklenen, zaten kullanımda) — T18–T25'in HasFilter kullanımı production-ready.
  - SQL Server filtered index predicate kısıtları (MS Docs — "Create Filtered Indexes"): `NOT IN`, `BETWEEN`, function call, CASE invalid. Geçerli: `=`, `<>`, `!=`, `>`, `>=`, `<`, `<=`, `IS NULL`, `IS NOT NULL`, `IN`, `AND`, `OR`, `NOT`. **Kanıt:** Task başında yapılan "`<>` zincirini `NOT IN`'e normalize et" edit'i `Microsoft.Data.SqlClient.SqlException: Incorrect syntax near 'NOT'.` ile kırıldı (`Skinora.Transactions.Tests` integration test suite — `IntegrationTestBase.InitializeAsync` → `EnsureCreatedAsync` fail). Edit geri alındı, 68/68 test tekrar PASS.

### Pivot Özeti
İlk scope: "Transaction.Status filter'ını `NOT IN`'e normalize ederek doc-kod tutarsızlığını kapat." Edit uygulandı, testte kırıldı, SQL Server kısıtı keşfedildi. Revert + doc clarification ile pivot yapıldı. Kod değişikliği netleşti: **sıfır**. Değer: bulgu + doc note.

### T26'dan Farklı Doğrulama Türü
T18–T25 task'ları kendi entity'lerinin HasIndex deklarasyonlarını yaparken, T27 **çapraz doğrulama** (cross-cutting verification) görevi gördü. Bu doğal ve istenen sonuç — F1 planının bir parçası olarak 35 index'in tek bir çatı altında audit'lenmesi, plan drift'i veya eksik kalmış index'leri yakalama sigortası.
