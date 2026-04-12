# T22 — Dispute, FraudFlag Entity'leri

**Faz:** F1 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-12

---

## Yapılan İşler
- Dispute entity (06 §3.11): 14 field, soft delete, query filter
- Dispute EF Core config: 1 CHECK (CLOSED→ResolvedAt NOT NULL), unfiltered unique (TransactionId+Type), 3 FK (Transaction, User opener, User admin), 2 perf index (TransactionId, Status filtered OPEN/ESCALATED)
- FraudFlag entity (06 §3.12): 13 field, soft delete, query filter
- FraudFlag EF Core config: 4 CHECK (scope-based: ACCOUNT_LEVEL→TransactionId NULL, PRE_CREATE→TransactionId NOT NULL; review-based: APPROVED→ReviewedAt+AdminId NOT NULL, REJECTED→ReviewedAt+AdminId NOT NULL), 3 FK (Transaction opt, User, User reviewer), 3 perf index (Status filtered PENDING, TransactionId, UserId)
- DisputesModuleDbRegistration + FraudModuleDbRegistration + Program.cs kayıt
- Disputes.csproj + Fraud.csproj → Transactions + Users cross-module referans
- 23 integration test (11 Dispute + 12 FraudFlag)

## Etkilenen Modüller / Dosyalar
- `backend/src/Modules/Skinora.Disputes/Domain/Entities/Dispute.cs` (yeni)
- `backend/src/Modules/Skinora.Disputes/Infrastructure/Persistence/DisputeConfiguration.cs` (yeni)
- `backend/src/Modules/Skinora.Disputes/Infrastructure/Persistence/DisputesModuleDbRegistration.cs` (yeni)
- `backend/src/Modules/Skinora.Disputes/Skinora.Disputes.csproj` (güncellendi — Transactions + Users ref)
- `backend/src/Modules/Skinora.Fraud/Domain/Entities/FraudFlag.cs` (yeni)
- `backend/src/Modules/Skinora.Fraud/Infrastructure/Persistence/FraudFlagConfiguration.cs` (yeni)
- `backend/src/Modules/Skinora.Fraud/Infrastructure/Persistence/FraudModuleDbRegistration.cs` (yeni)
- `backend/src/Modules/Skinora.Fraud/Skinora.Fraud.csproj` (güncellendi — Transactions + Users ref)
- `backend/src/Skinora.API/Program.cs` (güncellendi — modül kayıtları)
- `backend/tests/Skinora.Disputes.Tests/Integration/DisputeEntityTests.cs` (yeni)
- `backend/tests/Skinora.Disputes.Tests/Skinora.Disputes.Tests.csproj` (güncellendi — Transactions + Users ref)
- `backend/tests/Skinora.Fraud.Tests/Integration/FraudFlagEntityTests.cs` (yeni)
- `backend/tests/Skinora.Fraud.Tests/Skinora.Fraud.Tests.csproj` (güncellendi — Transactions + Users ref)

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Dispute entity: 06 §3.11'e göre (Type, Status, SystemCheckResult, UserDescription, AdminNote, vb.) | ✓ | `Dispute.cs` — 14 field birebir |
| 2 | FraudFlag entity: 06 §3.12'ye göre (Type, Scope, ReviewStatus, Details, vb.) | ✓ | `FraudFlag.cs` — 13 field birebir |
| 3 | Unique: Dispute (TransactionId + Type) unfiltered | ✓ | `DisputeConfiguration.cs` — `UQ_Disputes_TransactionId_Type`, test: `Dispute_TransactionId_Type_Unique_Rejects_Duplicate` + `_Blocks_Even_SoftDeleted` |
| 4 | Check constraint'ler: Dispute CLOSED→ResolvedAt NOT NULL | ✓ | `CK_Disputes_Closed_ResolvedAt`, test: `Dispute_Closed_Without_ResolvedAt_Rejected` |
| 5 | Check constraint'ler: FraudFlag scope-specific | ✓ | `CK_FraudFlags_AccountLevel_TransactionId` + `CK_FraudFlags_PreCreate_TransactionId`, tests: `FraudFlag_AccountLevel_WithTransactionId_Rejected` + `FraudFlag_PreCreate_WithoutTransactionId_Rejected` |
| 6 | Check constraint'ler: FraudFlag review-specific | ✓ | `CK_FraudFlags_Approved_ReviewedAt` + `CK_FraudFlags_Rejected_ReviewedAt`, tests: `FraudFlag_Approved_Without_ReviewedAt_Rejected` + `FraudFlag_Rejected_Without_ReviewedAt_Rejected` |
| 7 | FK'ler: Dispute→Transaction, User(opener), User(admin) | ✓ | `DisputeConfiguration.cs` — 3 HasOne, tests: FK rejection |
| 8 | FK'ler: FraudFlag→Transaction(opt), User, User(reviewer) | ✓ | `FraudFlagConfiguration.cs` — 3 HasOne, tests: FK rejection |
| 9 | Index'ler: Dispute (TransactionId, Status filtered) | ✓ | `IX_Disputes_TransactionId`, `IX_Disputes_Status_Active` |
| 10 | Index'ler: FraudFlag (TransactionId, UserId, Status filtered) | ✓ | `IX_FraudFlags_TransactionId`, `IX_FraudFlags_UserId`, `IX_FraudFlags_Status_Pending` |
| 11 | Integration — CRUD + unique constraint (aynı türde tekrar dispute açılamaz) | ✓ | 11 Dispute test + 12 FraudFlag test = 23 total PASS |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration (Dispute) | ✓ 11/11 passed | `dotnet test tests/Skinora.Disputes.Tests` |
| Integration (FraudFlag) | ✓ 12/12 passed | `dotnet test tests/Skinora.Fraud.Tests` |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |
| Main CI startup check | 3/3 success (24314378328, 24314378323, 24312090370) |
| Branch CI | 11/11 job success (run 24315150854) |
| Validator notu | FraudFlag.UserId: 06 §3.12 field tablosu NULL, constraint notu "her iki scope'ta zorunlu" — impl NOT NULL, doğru çözüm |

## Altyapı Değişiklikleri
- Migration: Yok (EnsureCreated test-only)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T22-dispute-fraudflag-entities`
- Commit: `eed4dc7` — T22: Dispute, FraudFlag entity'leri
- PR: #22
- CI: ✓ PASS (run `24314888594` + `24315150854` success, 11/11 job)

## Known Limitations / Follow-up
- Dispute.SystemCheckResult `nvarchar(max)` (text) — JSON validation uygulama katmanında yapılacak (T58)
- FraudFlag.Details `nvarchar(max)` (text) — JSON validation uygulama katmanında yapılacak (T54)

## Notlar
- Working tree: temiz (CONTEXT.md T21 artığı discard edildi)
- Main CI startup check: 3 run success — `24314378328`, `24312090370`, `24312090356`
- Dış varsayım: yok (tüm bağımlılıklar internal — T04, T17, T18, T19)
- Dispute → Skinora.Disputes modülüne, FraudFlag → Skinora.Fraud modülüne yerleştirildi (mevcut iskelet yapısına uygun)
- Doğrulama kontrol listesi: 06 §3.12 FraudFlag scope constraint'leri doğru — ACCOUNT_LEVEL→TransactionId NULL, TRANSACTION_PRE_CREATE→TransactionId NOT NULL
