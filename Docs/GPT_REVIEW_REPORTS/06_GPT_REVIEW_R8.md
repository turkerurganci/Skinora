# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 8
**Sonuç:** 4 bulgu

---

## GPT Çıktısı

### BULGU-1: Arşivleme stratejisi global benzersizlik garantilerini zayıflatıyor
- **Seviye:** ORTA
- **Kategori:** Veri Bütünlüğü / Edge Case
- **Konum:** §3.7, §3.8, §3.9, §5.1, §8.4
- **Sorun:** PaymentAddress.Address, TxHash, SteamTradeOfferId canlı tabloda unique ama arşivleme sonrası cross-table uniqueness yok.
- **Öneri:** Global benzersizlik registry'si veya reuse-impossible kuralı dokümante edilmeli.

### BULGU-2: FLAGGED → CREATED akışında timeout/deadline semantiği eksik
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Edge Case
- **Konum:** §2.21, §3.5, §8.1
- **Sorun:** FLAGGED state'te deadline'lar, Hangfire job'ları ve CreatedAt semantiği tanımsız.
- **Öneri:** FLAGGED için açık kural yazılmalı.

### BULGU-3: TradeOffer.FAILED ile SentAt NOT NULL kuralı çelişiyor
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk
- **Konum:** §2.8, §3.9
- **Sorun:** FAILED = "gönderilemedi" ama constraint SentAt NOT NULL zorunlu kılıyor.
- **Öneri:** FAILED için SentAt zorunluluğu kaldırılmalı.

### BULGU-4: ExternalIdempotencyRecord.Status serbest string
- **Seviye:** DÜŞÜK
- **Kategori:** Veri Bütünlüğü / Teknik Doğruluk
- **Konum:** §3.21
- **Sorun:** Status string(20) ama enum/check constraint yok.
- **Öneri:** CHECK constraint ile sınırlandırılmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Arşivleme + benzersizlik | ✅ KABUL | Pratik risk düşük (HD wallet, blockchain hash, Steam ID) ama dokümante edilmeli | Her alan için reuse-impossible gerekçesi §8.4'e eklendi |
| 2 | FLAGGED timeout semantiği | ✅ KABUL | 03 §7'de "timeout henüz başlamamıştır" — 06'da karşılıksız | FLAGGED state kuralları eklendi: deadline NULL, job NULL, onay anında initialization |
| 3 | TradeOffer FAILED + SentAt | ✅ KABUL | Round 6 constraint hatası — pre-send failure'da SentAt yok | FAILED için SentAt zorunluluğu kaldırıldı, semantik notu eklendi |
| 4 | ExternalIdempotencyRecord.Status | ✅ KABUL | Diğer entity'lerdeki enum pattern ile tutarsız | CHECK IN ('in_progress', 'completed', 'failed') eklendi |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] §8.4'e global benzersizlik analizi eklendi — PaymentAddress.Address, TxHash, SteamTradeOfferId için reuse-impossible gerekçeleri
- [x] Transaction state-dependent constraint'lere FLAGGED state kuralları eklendi (deadline NULL, job NULL, CreatedAt semantiği)
- [x] TradeOffer FAILED constraint düzeltildi — SentAt zorunluluğu kaldırıldı, pre-send failure semantiği eklendi
- [x] ExternalIdempotencyRecord.Status: CHECK IN ('in_progress', 'completed', 'failed') constraint eklendi
- [x] Versiyon v2.9 → v3.0

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 9 tetiklendi
