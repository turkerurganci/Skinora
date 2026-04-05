# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 5
**Sonuç:** 5 bulgu

---

## GPT Çıktısı

### BULGU-1: Arşivleme stratejisi FK grafiğiyle tarif edilmemiş
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §1.2, §4.1, §6.1, §8.4
- **Sorun:** "6 ay sonra arşiv tablolarına" tek satır, bağlı kayıtların nasıl taşınacağı belirsiz.
- **Öneri:** Arşivleme scope'u ve bağlı entity'ler açıkça tanımlanmalı.

### BULGU-2: BuyerIdentificationMethod alan bağımlılıkları DB seviyesinde korunmuyor
- **Seviye:** ORTA
- **Kategori:** Edge Case / Teknik Doğruluk
- **Konum:** §2.3, §3.5, §5.1
- **Sorun:** STEAM_ID/OPEN_LINK yöntemleri için TargetBuyerSteamId ve InviteToken karşılıklı dışlayıcılığı garanti edilmiyor.
- **Öneri:** Method-dependent CHECK constraint tanımlanmalı.

### BULGU-3: BlockchainTransaction type-dependent veri bütünlüğü eksik
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Edge Case
- **Konum:** §2.5, §3.8
- **Sorun:** PaymentAddressId ve ActualTokenAddress alanlarının type'a bağlı zorunlulukları constraint'siz.
- **Öneri:** Type-dependent CHECK constraint'ler eklenmeli.

### BULGU-4: SellerPayoutIssue state-dependent constraint eksik
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §2.22, §3.8a, §5.2
- **Sorun:** State machine tanımlı ama RESOLVED'da ResolvedAt, ESCALATED'da EscalatedToAdminId zorunluluğu yok.
- **Öneri:** Transaction'daki pattern burada da uygulanmalı.

### BULGU-5: SteamTradeOfferId unique index eksik
- **Seviye:** DÜŞÜK
- **Kategori:** Veri Bütünlüğü
- **Konum:** §3.9, §5.1
- **Sorun:** TxHash için unique index var ama SteamTradeOfferId için yok.
- **Öneri:** WHERE NOT NULL unique index eklenmeli.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | Arşivleme stratejisi | ✅ KABUL | Transaction'ın 7+ FK bağımlılığı var, tek satırlık arşivleme notu yetersiz | Archive set tanımı, arşivlenen/arşivlenmeyen ayrımı, operasyon kuralları eklendi |
| 2 | BuyerIdentificationMethod constraint | ✅ KABUL | STEAM_ID iken InviteToken dolu veya OPEN_LINK iken TargetBuyerSteamId dolu olabilir | Method-dependent CHECK constraint eklendi |
| 3 | BlockchainTransaction type-dependent | ✅ KABUL | PaymentAddressId ve ActualTokenAddress type'a bağlı zorunlu/yasak ama constraint yok | Type-dependent CHECK constraint'ler eklendi |
| 4 | SellerPayoutIssue state constraint | ✅ KABUL | Transaction için yapılan pattern burada da uygulanmalı — tutarlılık | RESOLVED→ResolvedAt, ESCALATED→EscalatedToAdminId, RETRY_SCHEDULED→RetryCount>0 |
| 5 | SteamTradeOfferId unique index | ✅ KABUL | TxHash ile aynı dış sistem kimlik koruma pattern'i | WHERE NOT NULL unique index §5.1'e eklendi |

### Claude'un Ek Bulguları

Ek bulgu yok.

---

## Uygulanan Düzeltmeler

- [x] §8.4 arşivleme stratejisi detaylandırıldı: archive set tanımı, arşivlenen/arşivlenmeyen entity listesi, operasyon kuralları
- [x] Transaction'a BuyerIdentificationMethod-dependent CHECK constraint eklendi (STEAM_ID↔TargetBuyerSteamId, OPEN_LINK↔InviteToken)
- [x] BlockchainTransaction'a type-dependent CHECK constraint'ler eklendi (BUYER_PAYMENT, WRONG_TOKEN_*, giden transferler)
- [x] SellerPayoutIssue'ya state-dependent CHECK constraint'ler eklendi (RESOLVED, ESCALATED, RETRY_SCHEDULED)
- [x] TradeOffer.SteamTradeOfferId için WHERE NOT NULL unique index §5.1'e eklendi
- [x] Versiyon v2.6 → v2.7

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 6 tetiklendi
