# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 12
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: PaymentAddress adres tekrar üretilemez varsayımı DB seviyesinde garanti edilmiyor
- **Seviye:** ORTA
- **Kategori:** Veri Bütünlüğü / Teknik Doğruluk
- **Konum:** §3.7, §5.1, §8.4
- **Sorun:** HdWalletIndex NOT NULL ama UNIQUE değil — aynı index tekrar allocate edilebilir.
- **Öneri:** HdWalletIndex UNIQUE + reuse edilmez kuralı tanımlanmalı.

### BULGU-2: Timeout/Hangfire modeli tüm aşamalar için aynı netlikte tanımlı değil
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §3.5, §7.1, §8.1
- **Sorun:** Sadece payment aşaması için Hangfire job var — diğer deadline'ların enforcement mekanizması belirsiz.
- **Öneri:** Per-job vs scanner/poller ayrımı açıkça yazılmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | HdWalletIndex benzersizlik | ✅ KABUL | Index reuse = aynı adres = kritik güvenlik açığı. UNIQUE constraint şart | HdWalletIndex UNIQUE yapıldı, §5.1'e eklendi, §8.4 arşivleme notu güncellendi |
| 2 | Timeout enforcement modeli | ✅ KABUL | İki farklı mekanizma (per-job vs poller) var ama ayrım yazılmamış | Ödeme aşaması Hangfire delayed job, diğer aşamalar periyodik scanner/poller olarak netleştirildi |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] PaymentAddress.HdWalletIndex: NOT NULL → UNIQUE, NOT NULL + "monoton artan, asla reuse" açıklaması
- [x] §5.1'e HdWalletIndex unique index eklendi
- [x] §8.4 arşivleme benzersizlik notunda HdWalletIndex monoton artan allocator gerekçesi eklendi
- [x] Timeout enforcement mekanizması netleştirildi: ödeme = per-transaction Hangfire job, diğerleri = periyodik scanner/poller
- [x] Versiyon v3.3 → v3.4

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 13 tetiklendi
