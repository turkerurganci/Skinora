# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 14
**Sonuç:** 3 bulgu

---

## GPT Çıktısı

### BULGU-1: Arşivleme akışının atomikliği ve retry davranışı tarif edilmemiş
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Veri Bütünlüğü
- **Konum:** §8.4
- **Sorun:** "Birlikte taşınır" deniyor ama atomiklik ve retry stratejisi yok.
- **Öneri:** Tek DB transaction + idempotent retry kuralı yazılmalı.

### BULGU-2: ExternalIdempotencyRecord in_progress tamamlanmışlık kuralı eksik
- **Seviye:** ORTA
- **Kategori:** Veri Bütünlüğü / Teknik Doğruluk
- **Konum:** §3.21
- **Sorun:** in_progress iken CompletedAt dolu kalabilir.
- **Öneri:** in_progress → CompletedAt NULL + ResultPayload NULL.

### BULGU-3: OutboxMessage status-tamamlanmışlık kuralları yok
- **Seviye:** ORTA
- **Kategori:** Veri Bütünlüğü / Teknik Doğruluk
- **Konum:** §3.18
- **Sorun:** PROCESSED iken ProcessedAt NULL mümkün, PENDING iken ProcessedAt dolu mümkün.
- **Öneri:** Status-dependent constraint'ler eklenmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Arşivleme atomiklik | ✅ KABUL | Set ikiye bölünürse FK-free arşivde orphan riski | Tek DB transaction + idempotent retry (WHERE NOT EXISTS) kuralı eklendi |
| 2 | ExternalIdempotencyRecord in_progress | ✅ KABUL | completed ve failed var ama in_progress eksik — çelişkili kayıt mümkün | in_progress → CompletedAt NULL, ResultPayload NULL constraint eklendi |
| 3 | OutboxMessage status constraint | ✅ KABUL | Kritik altyapı tablosu, status-dependent pattern uygulanmamış | PENDING→ProcessedAt NULL, PROCESSED→ProcessedAt NOT NULL, FAILED→ProcessedAt NULL + ErrorMessage NOT NULL |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] §8.4 arşivleme atomiklik kuralı: tek DB transaction, idempotent retry stratejisi
- [x] ExternalIdempotencyRecord: in_progress → CompletedAt NULL, ResultPayload NULL constraint
- [x] OutboxMessage: PENDING→ProcessedAt NULL, PROCESSED→ProcessedAt NOT NULL, FAILED→ProcessedAt NULL + ErrorMessage NOT NULL
- [x] Versiyon v3.5 → v3.6

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 15 tetiklendi
