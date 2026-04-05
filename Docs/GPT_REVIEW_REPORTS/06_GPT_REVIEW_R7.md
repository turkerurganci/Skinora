# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 7
**Sonuç:** 4 bulgu

---

## GPT Çıktısı

### BULGU-1: RefreshToken rotation zinciri veri modeli seviyesinde eksik
- **Seviye:** ORTA
- **Kategori:** Veri Bütünlüğü / Teknik Doğruluk
- **Konum:** §3.3, §4.1
- **Sorun:** ReplacedByTokenId FK olarak tanımlı değil, dangling reference riski.
- **Öneri:** Self-reference FK tanımlanmalı.

### BULGU-2: BlockchainTransaction status semantiği DB seviyesinde korunmuyor
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Edge Case
- **Konum:** §2.6, §3.8
- **Sorun:** CONFIRMED iken ConfirmationCount=0, ConfirmedAt=NULL mümkün.
- **Öneri:** Status-dependent CHECK constraint'ler eklenmeli.

### BULGU-3: Transaction yaşam döngüsü timestamp tamamlanmışlığı garanti edilmiyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Veri Bütünlüğü
- **Konum:** §2.1, §3.5
- **Sorun:** COMPLETED iken CompletedAt=NULL, PAYMENT_RECEIVED iken PaymentReceivedAt=NULL mümkün.
- **Öneri:** Milestone timestamp constraint'leri eklenmeli.

### BULGU-4: Soft delete + retention lifecycle netleştirilmemiş
- **Seviye:** DÜŞÜK
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §1.3, §6.1, §6.2
- **Sorun:** UserLoginLog, RefreshToken soft delete ama ayrıca retention-based hard purge var — lifecycle belirsiz.
- **Öneri:** İki aşamalı lifecycle (soft delete → hard purge) açıkça yazılmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | RefreshToken rotation FK | ✅ KABUL | ReplacedByTokenId field var ama FK tanımı ve §4.1 listesinde yok | Self-reference FK eklendi, §4.1'e işlendi |
| 2 | BlockchainTransaction status constraint | ✅ KABUL | CONFIRMED/PENDING/DETECTED semantiği DB'de garanti edilmiyor | Status-dependent CHECK constraint'ler eklendi |
| 3 | Transaction milestone timestamp | ✅ KABUL | State-dependent constraint pattern'i ana yaşam döngüsüne uygulanmamıştı | Kümülatif milestone timestamp constraint'leri eklendi |
| 4 | Soft delete + retention lifecycle | ✅ KABUL | İki farklı operasyon (soft delete + hard purge) belirsiz | §1.3'e iki aşamalı lifecycle tablosu eklendi |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] RefreshToken.ReplacedByTokenId → self-reference FK olarak tanımlandı, §4.1'e eklendi
- [x] BlockchainTransaction: CONFIRMED→ConfirmationCount≥20+ConfirmedAt, PENDING→<20, DETECTED→0 constraint'leri
- [x] Transaction: kümülatif milestone timestamp constraint'leri (ACCEPTED→AcceptedAt, ..., COMPLETED→CompletedAt)
- [x] §1.3'e soft delete + retention lifecycle açıklaması ve entity bazlı hard purge tablosu eklendi
- [x] Versiyon v2.8 → v2.9

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 8 tetiklendi
