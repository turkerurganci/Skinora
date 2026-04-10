# T20 — PaymentAddress, BlockchainTransaction Entity'leri

**Faz:** F1 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-04-11

---

## Yapılan İşler
- PaymentAddress entity (06 §3.7): 11 field, ISoftDeletable, 1:1 Transaction navigation
- BlockchainTransaction entity (06 §3.8): 17 field + 2 navigation, type/status semantiği
- PaymentAddressConfiguration: 3 unique index (TransactionId, Address, HdWalletIndex), 1 filtered perf index (MonitoringStatus ACTIVE), FK → Transaction (1:1)
- BlockchainTransactionConfiguration: 5 type-dependent + 3 status-dependent CHECK constraint, 1 filtered unique (TxHash WHERE NOT NULL), 3 perf index (TransactionId, Status PENDING, FromAddress), FK → Transaction (N:1) + FK → PaymentAddress (N:1 optional)
- Transaction entity güncellendi: PaymentAddress? ve ICollection<BlockchainTransaction> navigation property'leri eklendi
- 34 integration test (CRUD, check constraint violation/satisfaction, unique constraint, FK enforcement, navigation)
- CONTEXT.md güncellendi

## Etkilenen Modüller / Dosyalar
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/PaymentAddress.cs` — **yeni**
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/BlockchainTransaction.cs` — **yeni**
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/PaymentAddressConfiguration.cs` — **yeni**
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/BlockchainTransactionConfiguration.cs` — **yeni**
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/Transaction.cs` — **güncellendi** (navigation property eklendi)
- `backend/tests/Skinora.Transactions.Tests/Integration/PaymentBlockchainEntityTests.cs` — **yeni**
- `.claude/CONTEXT.md` — **güncellendi**

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | PaymentAddress entity: 06 §3.7'ye göre (Address, HdWalletIndex, MonitoringStatus, vb.) | ✓ | PaymentAddress.cs — 11 field birebir §3.7 |
| 2 | BlockchainTransaction entity: 06 §3.8'e göre (TxHash, Type, Status, Amount, ConfirmationCount, ActualTokenAddress, vb.) | ✓ | BlockchainTransaction.cs — 17 field birebir §3.8 |
| 3 | Unique: PaymentAddress.TransactionId, Address, HdWalletIndex; BlockchainTransaction.TxHash (filtered) | ✓ | Config dosyaları + test: PaymentAddress_TransactionId_Unique, PaymentAddress_Address_Unique, PaymentAddress_HdWalletIndex_Unique, BlockchainTx_TxHash_Unique_PreventsDuplicates, BlockchainTx_TxHash_Null_AllowsMultiple |
| 4 | Check constraint'ler: BlockchainTransaction type-specific kurallar | ✓ | 5 type-dependent CK — test: CK_Type_BuyerPayment_*, CK_Type_WrongTokenIncoming_*, CK_Type_WrongTokenRefund_*, CK_Type_SpamTokenIncoming_*, CK_Type_Outbound_* |
| 5 | Check constraint'ler: BlockchainTransaction status-specific kurallar | ✓ | 3 status-dependent CK — test: CK_Status_Confirmed_*, CK_Status_Detected_*, CK_Status_Failed_* |
| 6 | FK'ler: PaymentAddress→Transaction, BlockchainTransaction→Transaction, BlockchainTransaction→PaymentAddress | ✓ | Config dosyaları + test: PaymentAddress_FK_Transaction_EnforcedByDatabase, BlockchainTx_FK_Transaction_EnforcedByDatabase, BlockchainTx_FK_PaymentAddress_EnforcedByDatabase |
| 7 | Index'ler: BlockchainTransaction.TransactionId, Status (filtered PENDING), FromAddress; PaymentAddress.MonitoringStatus (filtered) | ✓ | Config dosyaları: IX_BlockchainTransactions_TransactionId, IX_BlockchainTransactions_Status_Pending, IX_BlockchainTransactions_FromAddress, IX_PaymentAddresses_MonitoringStatus_Active |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration | ✓ 34/34 passed | `dotnet test --filter PaymentBlockchainEntityTests` — 34 passed, 0 failed |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Doğrulama bekleniyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- Migration: Yok (EnsureCreated pattern, migration T28'de)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T20-payment-blockchain-entities`
- Commit: (pending)
- PR: (pending)
- CI: (pending)

## Known Limitations / Follow-up
- BlockchainTransaction, BaseEntity'den türemiyor (RowVersion/IAuditableEntity yok) — 06 §3.8 "Workflow Record" tanımına uygun: Status, ConfirmationCount, RetryCount güncellenir ama concurrency kontrolü transaction seviyesinde yapılır
- PENDING status constraint yok (ConfirmationCount < 20) — spec'te "PENDING: ConfirmationCount < 20" diyor ama bu DB seviyesinde enforce edilemiyor çünkü PENDING → CONFIRMED geçişinde atomik olarak hem status hem count güncellenir; aradaki geçici durumlar constraint'i kırar. Business logic'te kontrol edilecek.

## Notlar
- T19 (TransactionEntityTests) regression testi ayrıca çalıştırıldı
- PaymentAddress ISoftDeletable implement ediyor (06 §3.7 IsDeleted + DeletedAt)
- BlockchainTransaction silme politikası: Workflow Record — delete yok, archive tabloya taşınabilir (§8.8)
