# T17 — Enum Tanımları (C# + EF Core)

**Faz:** F1 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-10

---

## Yapılan İşler
- EF Core global enum→string convention eklendi (`AppDbContext.ConfigureConventions`)
- `OutboxMessageConfiguration` int conversion kaldırıldı, string convention'a geçildi
- OutboxMessage CHECK constraint ve filtered index SQL literal'leri int→string güncellendi
- 22 enum'a `[Theory] [InlineData]` isim doğrulama testleri eklendi (önceden sadece TransactionStatus'ta vardı)
- Mevcut 23 enum tanımı 06 §2 ile birebir eşleşme doğrulandı

## Etkilenen Modüller / Dosyalar
- `backend/src/Skinora.Shared/Persistence/AppDbContext.cs` — ConfigureConventions'a global enum→string convention
- `backend/src/Skinora.Shared/Persistence/Outbox/Configurations/OutboxMessageConfiguration.cs` — HasConversion<int> kaldırıldı, SQL literal'ler string'e çevrildi
- `backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs` — 22 enum'a isim doğrulama testleri eklendi

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 23 enum tanımlı | ✓ | `AllEnums_ShouldExistInSharedNamespace` testi — `Assert.Equal(23, enumTypes.Count)` PASS |
| 2 | EF Core'da string olarak saklanıyor (HasConversion) | ✓ | `AppDbContext.ConfigureConventions` içinde `EnumToStringConverter<>` global convention. Integration test `OutboxMessages_TableExists_AndAcceptsInsert` PASS (SQL Server'da string column doğrulaması) |
| 3 | Her enum değeri 06 §2 ile birebir eşleşiyor | ✓ | 23 enum × `[Theory] [InlineData]` testleri — tüm değer isimleri ve sayıları doğrulandı |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Unit | ✓ 150/150 passed | `dotnet test Skinora.Shared.Tests` — 23 count test + 23 name theory (toplam ~127 case) + 1 cross-cutting |
| Integration | ✓ 4/4 passed | Testcontainers SQL Server — OutboxMessages tablosu string Status ile oluşturuldu |
| Full solution | ✓ 253/253 passed | `dotnet test Skinora.sln` — 154 Shared + 99 API |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |
| Doğrulama tarihi | 2026-04-10 |
| Doğrulama yöntemi | Bağımsız validator — 06 §2 spot-check, test çalıştırma, build kontrolü, güvenlik taraması |

## Altyapı Değişiklikleri
- Migration: Yok (T28'de initial migration oluşturulacak)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T17-enum-definitions`
- Commit: `33b60d6` (squash merge to main)
- PR: —
- CI: —

## Known Limitations / Follow-up
- Enum→string conversion runtime reflection kullanıyor (`Assembly.GetTypes`), ancak `ConfigureConventions` uygulama başlangıcında tek sefer çalıştığı için performans etkisi yok
- `ExternalIdempotencyStatus` farklı namespace'te (`Persistence.Outbox`) — global convention kapsamı dışında, kendi explicit `HasConversion<string>()` korunuyor

## Notlar
- 23 enum dosyası T03'te oluşturulmuştu. T17'nin ana katkısı: EF Core string convention + OutboxMessage int→string geçişi + kapsamlı isim doğrulama testleri
- OutboxMessage CHECK constraint ve filtered index SQL literal'leri SQL Server bracket notation'a (`[Status]`) çevrildi
