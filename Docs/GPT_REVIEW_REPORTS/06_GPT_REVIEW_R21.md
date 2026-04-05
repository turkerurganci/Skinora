# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 21
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: ExternalIdempotencyRecord için eşzamanlı yarış durumu normatif değil
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Edge Case
- **Konum:** §3.21
- **Sorun:** Retry state machine var ama concurrency acquisition kuralı yok.
- **Öneri:** Atomik insert-or-read + conditional update kuralı yazılmalı.

### BULGU-2: SystemSetting "yapılandırıldı" ile "geçerli" ayrımı eksik
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Operasyonel Güvenlik
- **Konum:** §3.17, §8.9
- **Sorun:** IsConfigured=true ama Value geçersiz olabilir — tip/aralık doğrulaması yok.
- **Öneri:** İki katmanlı doğrulama: tip parse + alan aralığı/çapraz kurallar.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Idempotency concurrency | ✅ KABUL | State machine tamam ama aynı key + aynı anda → duplicate side effect riski | Atomik insert-or-read, conditional update (WHERE Status='failed'), kazanan/kaybeden davranışı yazıldı |
| 2 | SystemSetting doğrulama | ✅ KABUL | IsConfigured dolu ama Value=-0.5 mümkün — runtime bug | İki katmanlı doğrulama: tip parse + alan aralığı/çapraz kurallar + startup+update API enforcement |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] ExternalIdempotencyRecord: concurrency acquisition kuralı eklendi (atomik insert-or-read, conditional update, kazanan/kaybeden davranışı)
- [x] SystemSetting.DataType: CHECK IN ('int', 'decimal', 'bool', 'string') constraint
- [x] SystemSetting: iki katmanlı doğrulama kuralları (tip parse, alan aralığı, çapraz doğrulama, startup + API enforcement)
- [x] Versiyon v4.2 → v4.3

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 22 tetiklendi
