# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 6
**Sonuç:** 5 bulgu

---

## GPT Çıktısı

### BULGU-1: ExternalIdempotencyRecord unique key scope
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Edge Case
- **Konum:** §3.21
- **Sorun:** IdempotencyKey tek başına UNIQUE — farklı servisler aynı key'i üretirse çakışır.
- **Öneri:** UNIQUE(ServiceName, IdempotencyKey) yapılmalı.

### BULGU-2: FraudFlag ve Dispute state-dependent constraint eksik
- **Seviye:** ORTA
- **Kategori:** Veri Bütünlüğü / Teknik Doğruluk
- **Konum:** §3.11, §3.12
- **Sorun:** Dispute CLOSED iken ResolvedAt, FraudFlag APPROVED/REJECTED iken ReviewedAt/ReviewedByAdminId zorunluluğu yok.
- **Öneri:** State-dependent CHECK constraint'ler eklenmeli.

### BULGU-3: TradeOffer status-timestamp uyumu garanti edilmiyor
- **Seviye:** DÜŞÜK
- **Kategori:** Edge Case / Teknik Doğruluk
- **Konum:** §3.9
- **Sorun:** SENT iken SentAt, ACCEPTED/DECLINED/EXPIRED iken RespondedAt zorunluluğu yok.
- **Öneri:** State-dependent constraint eklenmeli.

### BULGU-4: SystemHeartbeat singleton garantisi yok
- **Seviye:** ORTA
- **Kategori:** Eksiklik / Teknik Doğruluk
- **Konum:** §3.23, §8.5
- **Sorun:** "Tek satır" deniyor ama DB'de bunu zorlayan kural yok.
- **Öneri:** CHECK (Id = 1) + seed kaydı tanımlanmalı.

### BULGU-5: §6.1'de ExternalIdempotencyRecord atlanmış
- **Seviye:** DÜŞÜK
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §6.1
- **Sorun:** Retention-based entity ama saklama politikası tablosunda yok.
- **Öneri:** §6.1'e eklenmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | ExternalIdempotencyRecord unique scope | ✅ KABUL | İki farklı servis aynı EventId kullanabilir — global unique gereksiz coupling | UNIQUE(ServiceName, IdempotencyKey) olarak değiştirildi |
| 2 | Dispute/FraudFlag state constraint | ✅ KABUL | Transaction, SellerPayoutIssue için yapılan pattern burada eksik | Dispute: CLOSED→ResolvedAt; FraudFlag: APPROVED/REJECTED→ReviewedAt+ReviewedByAdminId |
| 3 | TradeOffer status-timestamp | ✅ KABUL | Aynı pattern — immutable audit kaydında timestamp eksikliği olay sırasını bozar | SENT→SentAt; ACCEPTED/DECLINED/EXPIRED→SentAt+RespondedAt |
| 4 | SystemHeartbeat singleton | ✅ KABUL | "Tek satır" iddiası DB seviyesinde garanti edilmeli | CHECK (Id = 1) + seed kaydı §8.5'e eklendi |
| 5 | §6.1 ExternalIdempotencyRecord | ✅ KABUL | Retention-based entity, saklama tablosunda eksik | Outbox/ProcessedEvent satırı genişletildi |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] ExternalIdempotencyRecord: UNIQUE(IdempotencyKey) → UNIQUE(ServiceName, IdempotencyKey), §5.1'e eklendi
- [x] Dispute: CLOSED→ResolvedAt NOT NULL state-dependent constraint eklendi
- [x] FraudFlag: APPROVED/REJECTED→ReviewedAt NOT NULL + ReviewedByAdminId NOT NULL constraint eklendi
- [x] TradeOffer: SENT→SentAt; ACCEPTED/DECLINED/EXPIRED→SentAt+RespondedAt constraint'leri eklendi
- [x] SystemHeartbeat: CHECK (Id = 1) constraint + §8.5 seed kaydı eklendi
- [x] §6.1 saklama politikası: ExternalIdempotencyRecord eklendi
- [x] Versiyon v2.7 → v2.8

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 7 tetiklendi
