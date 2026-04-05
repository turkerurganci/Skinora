# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 17
**Sonuç:** 2 bulgu

---

## GPT Çıktısı

### BULGU-1: Soft delete yaşam döngüsü ile archive set kuralı tam uyumlu değil
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Veri yaşam döngüsü
- **Konum:** §1.3, §8.6
- **Sorun:** Bazı soft delete entity'ler arşivlemeye giriyor ama §1.3 bunu yansıtmıyor.
- **Öneri:** Soft delete grubunu kalıcı/arşivlenebilir olarak ikiye ayır.

### BULGU-2: Aynı transaction için birden fazla aktif payout issue engellenmiyor
- **Seviye:** ORTA
- **Kategori:** Edge Case / Operasyonel tutarlılık
- **Konum:** §3.8a, §5.1
- **Sorun:** SellerPayoutIssue için aktif kayıt tekil garantisi yok.
- **Öneri:** Filtered unique index ile tek aktif issue kuralı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Soft delete lifecycle ayrımı | ✅ KABUL | Transaction, PaymentAddress, Dispute, FraudFlag arşivleniyor ama §1.3'te ayrım yok | §1.3: Soft Delete (Kalıcı) + Soft Delete (Arşivlenebilir) olarak ikiye ayrıldı, lifecycle tablosu güncellendi |
| 2 | SellerPayoutIssue aktif tekil | ✅ KABUL | Bir transaction'da tek payout akışı var — çoklu aktif issue operasyonel karmaşa | Filtered unique index: UNIQUE(TransactionId) WHERE VerificationStatus != RESOLVED, §5.1'e eklendi |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] §1.3 Soft Delete → "Soft Delete (Kalıcı)" + "Soft Delete (Arşivlenebilir)" olarak ikiye ayrıldı
- [x] §1.3 lifecycle tablosu güncellendi — arşivlenebilir entity'ler ayrıca belirtildi
- [x] SellerPayoutIssue: tek aktif issue kuralı + filtered unique index (TransactionId WHERE != RESOLVED)
- [x] §5.1'e SellerPayoutIssue unique index eklendi
- [x] Versiyon v3.8 → v3.9

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 18 tetiklendi
