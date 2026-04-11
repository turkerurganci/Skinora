# T19 — Transaction, TransactionHistory entity'leri

**Faz:** F1 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-10

---

## Yapılan İşler
- Transaction entity: ~50+ field, 06 §3.5 birebir uyumlu
- TransactionHistory entity: append-only audit trail, 06 §3.6 birebir uyumlu
- 8 check constraint (cancel, hold, freeze active/passive, freeze-hold mutual binding, buyer identification method)
- FK'ler: Seller→User, Buyer→User, HoldAdmin→User, TransactionHistory→Transaction, TransactionHistory→User (Actor)
- Unique filtered index: InviteToken (WHERE NOT NULL)
- Filtered performance index: Status (aktif durumlar)
- Standart performance indexleri: SellerId, BuyerId, CreatedAt, EscrowBotId, TransactionHistory.TransactionId
- Komisyon hesaplama alanları: decimal(18,6) precision, §8.3 uyumlu
- RowVersion optimistic concurrency: §8.7 uyumlu
- TransactionsModuleDbRegistration + Program.cs kaydı
- 23 integration test (gerçek SQL Server — TestContainers)

## Etkilenen Modüller / Dosyalar
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/Transaction.cs` — yeni
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/TransactionHistory.cs` — yeni
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionConfiguration.cs` — yeni
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionHistoryConfiguration.cs` — yeni
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionsModuleDbRegistration.cs` — yeni
- `backend/src/Modules/Skinora.Transactions/Skinora.Transactions.csproj` — Users referansı eklendi
- `backend/src/Skinora.API/Program.cs` — TransactionsModuleDbRegistration kaydı
- `backend/tests/Skinora.Transactions.Tests/Integration/TransactionEntityTests.cs` — yeni (23 test)

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Transaction entity: tüm field'lar 06 §3.5'e göre | ✓ | Transaction.cs — Status, SellerId, BuyerId, item bilgileri, fiyat/komisyon, timeout, hold, freeze, cancel, dispute, bot, timestamps |
| 2 | TransactionHistory entity: 06 §3.6'ya göre (append-only) | ✓ | TransactionHistory.cs — Id (IDENTITY), TransactionId, PreviousStatus, NewStatus, Trigger, ActorType, ActorId, AdditionalData, CreatedAt |
| 3 | Check constraint'ler: iptal, hold, freeze, buyer identification | ✓ | TransactionConfiguration.cs — 8 check constraint: CK_Cancel, CK_Hold, CK_FreezeActive, CK_FreezePassive, CK_FreezeHold_Forward, CK_FreezeHold_Reverse, CK_BuyerMethod_SteamId, CK_BuyerMethod_OpenLink |
| 4 | FK'ler: Transaction→User (seller, buyer, holdAdmin), History→Transaction, History→User | ✓ | TransactionConfiguration.cs + TransactionHistoryConfiguration.cs |
| 5 | Unique: Transaction.InviteToken (filtered, WHERE NOT NULL) | ✓ | UQ_Transactions_InviteToken, test: InviteToken_Unique_PreventsDuplicates + InviteToken_Null_AllowsMultiple |
| 6 | Index'ler: Status (filtered), SellerId, BuyerId, CreatedAt, EscrowBotId; TransactionHistory.TransactionId | ✓ | TransactionConfiguration.cs — IX_Transactions_Status_Active (filtered), IX_Transactions_SellerId/BuyerId/CreatedAt/EscrowBotId; IX_TransactionHistory_TransactionId |
| 7 | Optimistic concurrency: RowVersion | ✓ | BaseEntity.RowVersion + test: RowVersion_ConcurrentUpdate_ThrowsDbUpdateConcurrencyException |
| 8 | Computed field'lar: CommissionAmount, TotalAmount (06 §8.3) | ✓ | decimal(18,6) precision, test: CommissionFields_StoredWithCorrectPrecision |
| 9 | Integration test: CRUD + check constraint + RowVersion concurrency | ✓ | 23 test PASS |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration | ✓ 23/23 passed | `dotnet test tests/Skinora.Transactions.Tests` — CRUD (3), TransactionHistory (2), Check constraint (8), Unique index (2), RowVersion (1), Commission (1), FK (3), Valid state (3) |
| Regresyon | ✓ 150/150 passed | Mevcut testler (Shared+API) kırılmadı |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Doğrulama tarihi | 2026-04-10 |
| Doğrulayıcı | Claude (bağımsız doğrulama chat'i) |
| Bulgu sayısı | 0 (minor rapor düzeltmesi: "9 constraint" → "8 constraint") |
| Düzeltme gerekli mi | Hayır |

### Doğrulama Kontrol Listesi
- [x] 06 §3.5 tüm field'lar ve check constraint'ler var mı? — 50+ field birebir, 8 check constraint eksiksiz
- [x] RowVersion (§8.7) optimistic concurrency çalışıyor mu? — BaseEntity.RowVersion + global IsRowVersion() + test kanıtı
- [x] Komisyon hesaplama formülü doğru mu (§8.3)? — decimal(18,6)/(5,4) precision, MidpointRounding.ToZero test kanıtı

### Güvenlik Kontrolü
- [x] Secret sızıntısı: Temiz
- [x] Auth etkisi: Yok
- [x] Input validation: Yok (entity katmanı)
- [x] Yeni dış bağımlılık: Yok

### Yapım Raporu Karşılaştırması
- Uyum: Tam uyumlu (1 minor düzeltme — constraint sayısı 9→8)

## Altyapı Değişiklikleri
- Migration: Yok (EnsureCreated ile test, initial migration T28'de)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok
- Proje referansı: Skinora.Transactions → Skinora.Users eklendi

## Commit & PR
- Branch: `task/T19-transaction-entities`
- Commit: `1237235` — T19: Transaction, TransactionHistory entity'leri
- PR: Henüz açılmadı
- CI: Push sonrası bekleniyor

## Known Limitations / Follow-up
- `EscrowBotId → PlatformSteamBot` FK constraint'i T21'de oluşturulacak (entity henüz yok). Şu an sadece field + index tanımlı.
- `TransactionHistory.ActorId` — SYSTEM aksiyonlarında §8.9 sentinel GUID kullanılacak (uygulama katmanı, T44+ state machine)
- Status→zorunlu field matrisi ve State→aktif deadline/job matrisi uygulama katmanında enforce edilecek (T44+ state machine guard'ı)

## Notlar
- SQL Server filtered index'lerde `NOT IN` desteklenmiyor, `<>` zincirleme ile çözüldü
- Check constraint'lerde enum değerleri string olarak yazıldı (AppDbContext EnumToStringConverter uyumlu)
- PreviousStatusBeforeHold field'ı `int?` olarak tanımlandı (TransactionStatus enum'ı ile doğrudan ilişkilendirilmedi — audit amaçlı, operasyonel kullanım yok per 06 §3.5)
