# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 10
**Sonuç:** 3 bulgu

---

## GPT Çıktısı

### BULGU-1: ACCEPTED sonrası alıcı kimliği ve refund snapshot bütünlüğü eksik
- **Seviye:** ORTA
- **Kategori:** Veri Bütünlüğü / Edge Case
- **Konum:** §2.1, §3.5
- **Sorun:** ACCEPTED sonrası BuyerId ve BuyerRefundAddress zorunluluğu yok.
- **Öneri:** ACCEPTED ve sonrası için BuyerId NOT NULL, BuyerRefundAddress NOT NULL.

### BULGU-2: State → aktif deadline/job ilişkisi normatif olarak tanımlanmamış
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §2.1, §3.5, §8.1
- **Sorun:** Hangi state'te hangi deadline/job dolu olmalı normatif değil.
- **Öneri:** State → deadline/job matrisi tanımlanmalı.

### BULGU-3: WRONG_TOKEN_INCOMING için PaymentAddressId zorunlu değil
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Edge Case
- **Konum:** §2.5, §3.7, §3.8
- **Sorun:** Yanlış token gelen bir transfer ödeme adresine bağlanmadan kaydedilebilir.
- **Öneri:** WRONG_TOKEN_INCOMING → PaymentAddressId NOT NULL.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | ACCEPTED sonrası BuyerId/RefundAddress | ✅ KABUL | Alıcı kabul anında sabitlenir — constraint olmalı | ACCEPTED ve sonrası: BuyerId NOT NULL, BuyerRefundAddress NOT NULL |
| 2 | State → deadline/job matrisi | ✅ KABUL | DB CHECK ile tam enforce pratik değil ama normatif kural gerekli | State → deadline/job matrisi normatif tablo olarak eklendi |
| 3 | WRONG_TOKEN_INCOMING PaymentAddressId | ✅ KABUL | Yanlış token belirli bir adrese gelir — reconciliation için bağ şart | WRONG_TOKEN_INCOMING→PaymentAddressId NOT NULL, WRONG_TOKEN_REFUND→NULL (ayrıştırıldı) |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] ACCEPTED ve sonrası: BuyerId NOT NULL, BuyerRefundAddress NOT NULL constraint eklendi
- [x] State → aktif deadline/job normatif matrisi eklendi (FLAGGED→NULL, CREATED→AcceptDeadline, ..., terminal→NULL)
- [x] WRONG_TOKEN_INCOMING ve WRONG_TOKEN_REFUND constraint'leri ayrıştırıldı (incoming→PaymentAddressId NOT NULL, refund→NULL)
- [x] Versiyon v3.1 → v3.2

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 11 tetiklendi
