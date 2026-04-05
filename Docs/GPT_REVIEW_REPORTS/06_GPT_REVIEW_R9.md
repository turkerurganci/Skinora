# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 9
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: Timeout freeze / emergency hold modeli DB seviyesinde tam karşılıklı korunmuyor
- **Seviye:** ORTA
- **Kategori:** Veri Bütünlüğü / Edge Case
- **Konum:** §2.20, §3.5, §8.1
- **Sorun:** Constraint'ler tek yönlü — yarım freeze, hold+freeze uyumsuzluğu mümkün.
- **Öneri:** Karşılıklı constraint eklenmeli.

### BULGU-2: TransactionHistory aktör modeli kendi içinde tam tutarlı değil
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Audit Doğruluğu
- **Konum:** §2.18, §3.6, §8.5
- **Sorun:** ActorId nullable ama SYSTEM seed account var — aktörsüz audit kaydı mümkün.
- **Öneri:** ActorId NOT NULL + SYSTEM sentinel kullanımı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Freeze/hold karşılıklı constraint | ✅ KABUL | Yarım freeze ve hold-freeze uyumsuzluğu DB'de mümkün | Freeze pasif→alanlar NULL, EMERGENCY_HOLD⇔IsOnHold=1 karşılıklı constraint'ler eklendi |
| 2 | TransactionHistory ActorId | ✅ KABUL | Immutable audit trail'de aktörsüz kayıt olmamalı — SYSTEM sentinel tam bu amaçla var | ActorId NOT NULL yapıldı, açıklama güncellendi, §4.1 FK opsiyonel→zorunlu |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] Freeze pasif constraint: TimeoutFrozenAt NULL ise FreezeReason+RemainingSeconds de NULL
- [x] Freeze-hold karşılıklı: EMERGENCY_HOLD ⇒ IsOnHold=1; IsOnHold=1 ⇒ TimeoutFrozenAt NOT NULL + EMERGENCY_HOLD
- [x] TransactionHistory.ActorId: NULL → NOT NULL, açıklama "kullanıcı/admin/system ID" olarak güncellendi
- [x] §4.1 FK: TransactionHistory.ActorId opsiyonel → zorunlu
- [x] Versiyon v3.0 → v3.1

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 10 tetiklendi
