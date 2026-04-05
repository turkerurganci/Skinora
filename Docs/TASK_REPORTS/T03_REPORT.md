# T03 — Shared Kernel: base sınıflar, exception'lar, interface'ler

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-06

---

## Yapılan İşler
- BaseEntity, IAuditableEntity, ISoftDeletable, IDomainEvent tanımlandı
- Exception hiyerarşisi oluşturuldu (DomainException, BusinessRuleException, NotFoundException, IntegrationException)
- ApiResponse<T> ve PagedResult<T> modelleri tanımlandı
- IUnitOfWork ve IOutboxService interface'leri tanımlandı
- TransactionCreatedEvent ve PaymentReceivedEvent event contract'ları tanımlandı
- 06 §2'deki tüm 23 enum tanımlandı
- Skinora.Shared.Tests projesi oluşturuldu ve solution'a eklendi
- 37 unit test yazıldı (enum değer kontrolleri)
- .gitkeep placeholder dosyaları kaldırıldı

## Etkilenen Modüller / Dosyalar
- `backend/src/Skinora.Shared/Domain/BaseEntity.cs` — yeni
- `backend/src/Skinora.Shared/Domain/IAuditableEntity.cs` — yeni
- `backend/src/Skinora.Shared/Domain/ISoftDeletable.cs` — yeni
- `backend/src/Skinora.Shared/Domain/IDomainEvent.cs` — yeni
- `backend/src/Skinora.Shared/Exceptions/DomainException.cs` — yeni
- `backend/src/Skinora.Shared/Exceptions/BusinessRuleException.cs` — yeni
- `backend/src/Skinora.Shared/Exceptions/NotFoundException.cs` — yeni
- `backend/src/Skinora.Shared/Exceptions/IntegrationException.cs` — yeni
- `backend/src/Skinora.Shared/Models/ApiResponse.cs` — yeni
- `backend/src/Skinora.Shared/Models/PagedResult.cs` — yeni
- `backend/src/Skinora.Shared/Interfaces/IUnitOfWork.cs` — yeni
- `backend/src/Skinora.Shared/Interfaces/IOutboxService.cs` — yeni
- `backend/src/Skinora.Shared/Events/TransactionCreatedEvent.cs` — yeni
- `backend/src/Skinora.Shared/Events/PaymentReceivedEvent.cs` — yeni
- `backend/src/Skinora.Shared/Enums/*.cs` — 23 yeni enum dosyası
- `backend/tests/Skinora.Shared.Tests/` — yeni test projesi
- `backend/Skinora.sln` — güncellendi (Shared.Tests eklendi)

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | BaseEntity (Id, CreatedAt, UpdatedAt), IAuditableEntity, ISoftDeletable (IsDeleted, DeletedAt) tanımlı | ✓ | BaseEntity.cs: Guid Id, DateTime CreatedAt/UpdatedAt, byte[] RowVersion. IAuditableEntity: CreatedAt/UpdatedAt. ISoftDeletable: bool IsDeleted, DateTime? DeletedAt |
| 2 | IDomainEvent interface (EventId GUID + OccurredAt) tanımlı | ✓ | IDomainEvent.cs: `Guid EventId { get; }` + `DateTime OccurredAt { get; }` — get-only = zorunlu |
| 3 | Exception hiyerarşisi: DomainException, BusinessRuleException, NotFoundException, IntegrationException | ✓ | 4 dosya. DomainException(ErrorCode, msg) → 409, BusinessRuleException(ErrorCode, msg) → 422, NotFoundException(entityName, key) → 404, IntegrationException(ServiceName, msg) → 502. 09 §8.3 uyumlu |
| 4 | ApiResponse<T>, PagedResult<T> tanımlı | ✓ | ApiResponse<T>: Success, Data, Error(Code/Message/Details), TraceId. PagedResult<T>: Items, TotalCount, Page, PageSize |
| 5 | IUnitOfWork, IOutboxService interface tanımlı | ✓ | IUnitOfWork: SaveChangesAsync(CancellationToken). IOutboxService: PublishAsync(IDomainEvent, CancellationToken) |
| 6 | Shared enum'lar: 06 §2'deki tüm enum'lar | ✓ | 23/23 enum dosyası. `dotnet test` ile 37 test PASS — her enum'un değer sayısı ve varlığı doğrulandı |

## Doğrulama Kontrol Listesi
| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 06 §2'deki tüm enum'lar tanımlı mı? | ✓ | 23/23 enum dosyası mevcut. Assembly reflection testi (AllEnums_ShouldExistInSharedNamespace) 23 adet doğruladı. Değer isimleri 06 §2 ile birebir eşleşiyor |
| 2 | IDomainEvent'te EventId ve OccurredAt zorunlu mu (09 §6.4)? | ✓ | IDomainEvent interface'inde get-only property olarak tanımlı — implementer zorunlu olarak sağlamalı |
| 3 | Shared/Events altında modüller arası event contract'lar var mı? | ✓ | TransactionCreatedEvent(record, IDomainEvent), PaymentReceivedEvent(record, IDomainEvent) — her ikisi de EventId + OccurredAt içeriyor |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Unit | ✓ 37/37 passed | `dotnet test tests/Skinora.Shared.Tests/` — 23 enum count testi + 13 TransactionStatus value testi + 1 toplam enum sayısı testi |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

### Güvenlik Kontrolü
- [x] Secret sızıntısı: Temiz — `git diff` taramasında secret/password/apikey bulunamadı
- [x] Auth etkisi: Temiz — T03 auth bileşeni içermiyor
- [x] Input validation: Temiz — T03 kullanıcı girdisi almıyor
- [x] Yeni bağımlılık: Yok — yalnızca mevcut xUnit/FluentAssertions (test projesi)

### Build & Test
| Tür | Sonuç | Komut | Çıktı |
|---|---|---|---|
| Build | ✓ 0 warning, 0 error | `dotnet build --no-restore` | Build succeeded, 3.71s |
| Unit | ✓ 37/37 passed | `dotnet test --no-build` | Passed! 37 total, 114ms |

### Yapım Raporu Karşılaştırması
- Uyum: Tam uyumlu — yapım raporu ile validator bulguları arasında uyuşmazlık yok
- Yapım raporundaki tüm dosya listesi, kabul kriterleri ve test sonuçları doğrulandı

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T03-shared-kernel`
- Commit: `9be781c` — T03: Shared Kernel — base sınıflar, exception'lar, interface'ler, enum'lar

## Known Limitations / Follow-up
- Modüller arası event contract'lar (TransactionCreatedEvent, PaymentReceivedEvent) T03'te sadece yapı olarak tanımlı. Kullanımları F3'te (T44+) gerçekleşecek.
- BaseEntity.RowVersion, T04 (EF Core config) ile IsRowVersion() olarak yapılandırılacak.
- Ek event contract'lar (diğer modüller arası event'ler) ilgili task'larda Shared/Events'e eklenecek.

## Notlar
- Enum value convention: UPPER_SNAKE_CASE (09 §5.1 uyumlu)
- DomainException ve BusinessRuleException'da ErrorCode property: middleware'in (T05) hata mapping yapabilmesi için
- IntegrationException'da ServiceName property: hangi dış servisin hata verdiğini belirtmek için
- ApiResponse<T> yapısı 07 §2.4 envelope formatına birebir uyumlu (success, data, error, traceId)
