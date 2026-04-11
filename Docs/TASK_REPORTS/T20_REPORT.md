# T20 — PaymentAddress, BlockchainTransaction Entity'leri

**Faz:** F1 | **Durum:** ✓ Tamamlandı (düzeltme uygulandı) | **Tarih:** 2026-04-11

---

## Yapılan İşler
- PaymentAddress entity (06 §3.7): 11 field, ISoftDeletable, 1:1 Transaction navigation
- BlockchainTransaction entity (06 §3.8): 17 field + 2 navigation, type/status semantiği
- PaymentAddressConfiguration: 3 unique index (TransactionId, Address, HdWalletIndex), 1 filtered perf index (MonitoringStatus ACTIVE), FK → Transaction (1:1)
- BlockchainTransactionConfiguration: 5 type-dependent + 4 status-dependent CHECK constraint, 1 filtered unique (TxHash WHERE NOT NULL), 3 perf index (TransactionId, Status PENDING, FromAddress), FK → Transaction (N:1) + FK → PaymentAddress (N:1 optional)
- Transaction entity güncellendi: PaymentAddress? ve ICollection<BlockchainTransaction> navigation property'leri eklendi
- 35 integration test (CRUD, check constraint violation/satisfaction, unique constraint, FK enforcement, navigation)
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
| 5 | Check constraint'ler: BlockchainTransaction status-specific kurallar | ✓ | 4/4 CK: Confirmed, Detected, Failed, Pending. PENDING eklendi (spec §3.8 `ConfirmationCount < 20`), test: `CK_Status_Pending_Violated_WhenConfirmationCountHigh` |
| 6 | FK'ler: PaymentAddress→Transaction, BlockchainTransaction→Transaction, BlockchainTransaction→PaymentAddress | ✓ | Config dosyaları + test: PaymentAddress_FK_Transaction_EnforcedByDatabase, BlockchainTx_FK_Transaction_EnforcedByDatabase, BlockchainTx_FK_PaymentAddress_EnforcedByDatabase |
| 7 | Index'ler: BlockchainTransaction.TransactionId, Status (filtered PENDING), FromAddress; PaymentAddress.MonitoringStatus (filtered) | ✓ | BT index'leri tam. PA MonitoringStatus filtresi 4 status'lü `IN (ACTIVE, POST_CANCEL_24H, POST_CANCEL_7D, POST_CANCEL_30D)` olarak düzeltildi (spec §5.2). |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 warning, 0 error | `dotnet build` — 22 proje, 26.8s |
| Integration (yapım) | ✓ 35/35 passed | `dotnet test --filter PaymentBlockchainEntityTests` — düzeltme sonrası |
| Integration (validator) | ✓ 35/35 passed | Docker 29.2.1 + TestContainers MsSql 2022, `commit bd8d713`, süre 5m 38s |
| Transactions regresyon (T19+T20) | ✓ 58/58 passed | `dotnet test Skinora.Transactions.Tests` — 6m 45s |
| Full suite regresyon | ✓ 311/311 passed | Shared 154 + API 99 + Transactions 58, 0 fail/skip |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS (düzeltme sonrası) |
| Doğrulama tarihi | 2026-04-11 |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır — 2 S1 sapma düzeltildi |

### Düzeltme Kaydı
| # | Seviye | Açıklama | Etkilenen dosya | Uygulanan Düzeltme |
|---|---|---|---|---|
| 1 | S1 Sapma (düzeltildi) | MonitoringStatus filtered index spec §5.2 4 status'ü içeriyor, kod sadece `ACTIVE`'ti. POST_CANCEL izleme sorguları (T75) index'ten yararlanamıyordu. | `PaymentAddressConfiguration.cs:72` | Filter `[MonitoringStatus] IN ('ACTIVE','POST_CANCEL_24H','POST_CANCEL_7D','POST_CANCEL_30D')` olarak genişletildi |
| 2 | S1 Sapma (düzeltildi) | PENDING status check constraint eksikti. Spec §3.8 `PENDING: ConfirmationCount < 20` tanımlıyor. Yapım raporundaki "DB'de enforce edilemez" gerekçesi teknik olarak hatalı — SQL Server CHECK constraint'leri statement sonrasındaki final state'e karşı değerlendirir, atomik `UPDATE SET Status='CONFIRMED', ConfirmationCount=20` PENDING constraint'ini ihlal etmez. | `BlockchainTransactionConfiguration.cs` (status CK bloğu) | Yeni constraint: `CK_BlockchainTransactions_Status_Pending` = `(Status <> 'PENDING') OR (ConfirmationCount < 20)`. Yeni test: `CK_Status_Pending_Violated_WhenConfirmationCountHigh` |

## Altyapı Değişiklikleri
- Migration: Yok (EnsureCreated pattern, migration T28'de)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T20-payment-blockchain-entities`
- Commit: `bd8d713` (T20 fix: PaymentAddress filtered index + PENDING status CK), `6d3aaaa` (T20: PaymentAddress, BlockchainTransaction entity'leri)
- PR: (pending)
- CI: (pending)

## Known Limitations / Follow-up
- BlockchainTransaction, BaseEntity'den türemiyor (RowVersion/IAuditableEntity yok) — 06 §3.8 "Workflow Record" tanımına uygun: Status, ConfirmationCount, RetryCount güncellenir ama concurrency kontrolü transaction seviyesinde yapılır
- **CI Gate partial — T11 close-out debt (T11.1):** T20 PR #11 CI'da sadece Lint step'i ✓ PASS oldu. Build → Unit → Integration → Contract → Migration dry-run → Docker build chain'i T13'ten beri hiç yeşil olmamıştı — root cause'lar T11 CI workflow'daki stale sidecar placeholder (T14/T15 sonrası geçersiz, bu PR'ın `0a9463b` commit'inde düzeltildi) ve T13 dönemi frontend `@parcel/watcher` lockfile/platform sorunu (düzeltilmedi). T20 kodu lokal olarak TestContainers MsSql 2022 üzerinde 35/35 + full suite 311/311 PASS verdiği için validator verdict PASS; ancak CI migration dry-run adımı hiç çalışmadığından **T17-T20 şemalarının CI migration pipeline'ında doğruluğu henüz kanıtlanmadı** — bu bir blind spot. T11.1 task'ı açıldı (11_IMPLEMENTATION_PLAN.md, IMPLEMENTATION_STATUS.md F0 bölümü); F1 → F1 Gate Check öncesi kapatılacak, T21 başlamadan önce tamamlanmalı.

## Notlar
- T19 (TransactionEntityTests) regression testi ayrıca çalıştırıldı
- PaymentAddress ISoftDeletable implement ediyor (06 §3.7 IsDeleted + DeletedAt)
- BlockchainTransaction silme politikası: Workflow Record — delete yok, archive tabloya taşınabilir (§8.8)
