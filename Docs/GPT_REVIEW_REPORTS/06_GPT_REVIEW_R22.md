# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 22
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: ExternalIdempotencyRecord stale in_progress recovery kuralı yok
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Edge Case
- **Konum:** §3.21
- **Sorun:** Servis çökmesinde in_progress süresiz stuck kalır.
- **Öneri:** Lease/timeout mekanizması eklenmeli.

### BULGU-2: Transaction constraint dili "ve sonrası" implementasyon hatasına açık
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §3.5
- **Sorun:** CANCELLED_* farklı aşamalardan gelebilir, ordinal karşılaştırma yanlış sonuç verir.
- **Öneri:** Explicit status set matrisi + "ordinal yasak" notu.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Stale in_progress recovery | ✅ KABUL | in_progress + servis ölümü = süresiz kilit | LeaseExpiresAt field + lease mekanizması + stale reclaim kuralı eklendi |
| 2 | Explicit status set matrisi | ✅ KABUL | "ve sonrası" → ordinal karşılaştırma riski + CANCELLED_* farklı milestone'larla geliyor | 9 field × explicit status set tablosu + CANCELLED_* kuralı + ordinal yasağı + FLAGGED notu |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] ExternalIdempotencyRecord: `LeaseExpiresAt` field eklendi, in_progress constraint güncellendi (LeaseExpiresAt NOT NULL)
- [x] Stale recovery: lease dolunca in_progress→failed conditional update, yeni istek lease kontrolü
- [x] Transaction constraint'leri: "ve sonrası" prose → 9 field'lık explicit status set matrisi
- [x] CANCELLED_* kuralı: iptal öncesi milestone'lar korunur, sonrası NULL — state machine guard
- [x] "Ordinal karşılaştırma yasaktır" notu
- [x] Versiyon v4.3 → v4.4

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 23 tetiklendi
