# GPT Cross-Review Raporu — 06_DATA_MODEL.md

**Tarih:** 2026-03-21
**Model:** ChatGPT (manuel)
**Round:** 2
**Sonuç:** 4 bulgu

---

## GPT Çıktısı

### BULGU-1: FraudFlag modeli ile Transaction.FLAGGED akışı artık birbirini desteklemiyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Teknik Doğruluk
- **Konum:** §1.2, §2.1, §3.12, §4.1, §5.2
- **Sorun:** Round 1'de yazılan CHECK constraint her iki scope'ta da TransactionId = NULL diyor, ama FLAGGED state'i Transaction kaydının var olduğunu gösteriyor. Transaction → FraudFlag ilişkisi fiilen karşılıksız.
- **Öneri:** Ya TransactionId kullanılmalı, ya da FLAGGED state ve ilişki kaldırılmalı.

### BULGU-2: Traceability matrix "tüm 24 entity izlenebilir" iddiasını karşılamıyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §1.1, §7.1, §7.2
- **Sorun:** ColdWalletTransfer, ExternalIdempotencyRecord ve SystemHeartbeat §7.1'de yok.
- **Öneri:** Eksik entity'ler §7.1'e eklenmeli.

### BULGU-3: Wrong-token senaryosu blockchain veri modelinde tam temsil edilemiyor
- **Seviye:** ORTA
- **Kategori:** Edge Case / Teknik Doğruluk
- **Konum:** §2.2, §2.5, §3.8
- **Sorun:** Token alanı StablecoinType (USDT/USDC) kabul ediyor ama WRONG_TOKEN_REFUND'da yanlış token temsil edilemiyor. Incoming wrong-token transferi için ayrı kayıt tipi yok.
- **Öneri:** Token alanı genişletilmeli veya incoming/refund ayrımı netleştirilmeli.

### BULGU-4: SYSTEM service account'ı User tablosunda modellemek doğal uyumlu değil
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Modelleme
- **Konum:** §3.1, §3.20, §8.5
- **Sorun:** User tablosu Steam tabanlı gerçek kullanıcıyı modelliyor; SYSTEM account sahte SteamId gerektiriyor. Sentinel değerler tanımlı değil.
- **Öneri:** Ayrı actor modeli düşünülmeli veya sentinel değerler açıkça yazılmalı.

---

## Claude Bağımsız Değerlendirmesi

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Önerilen Aksiyon |
|---|-------------|---------------|-------------------|------------------|
| 1 | FraudFlag/FLAGGED uyumsuzluğu | ✅ KABUL | Round 1 hatası — 02 §14.0 ve 03 §7'ye göre TRANSACTION_PRE_CREATE'te Transaction kaydı mevcut (FLAGGED state). TransactionId NOT NULL olmalı | CHECK constraint düzeltildi: TRANSACTION_PRE_CREATE → TransactionId NOT NULL |
| 2 | Traceability matrix eksikleri | ✅ KABUL | Doğrulandı — ColdWalletTransfer (05 §3.3), ExternalIdempotencyRecord (05 §5.1), SystemHeartbeat (05 §4.4) §7.1'de yok | 3 entity §7.1'e eklendi |
| 3 | Wrong-token veri modeli | ⚠️ KISMİ | Sorun gerçek ama model overhaul gereksiz. Token = beklenen stablecoin, ActualTokenAddress = gerçek yanlış token. Incoming wrong-token için kayıt tipi eksik | WRONG_TOKEN_INCOMING type eklendi, Token semantiği dokümante edildi, ActualTokenAddress kapsamı genişletildi |
| 4 | SYSTEM service account | ⚠️ KISMİ | Endişe geçerli ama sentinel User yaklaşımı pragmatik trade-off. Eksik olan sentinel değer tanımları | §8.5'te GUID, SteamId, DisplayName sentinel değerleri ve tasarım gerekçesi eklendi |

### Claude'un Ek Bulguları

Ek bulgu yok — GPT bu round'da kapsamlı tespit yapmış.

---

## Uygulanan Düzeltmeler

- [x] FraudFlag CHECK constraint düzeltildi: TRANSACTION_PRE_CREATE → TransactionId NOT NULL
- [x] Traceability matrix'e ColdWalletTransfer, ExternalIdempotencyRecord, SystemHeartbeat eklendi
- [x] BlockchainTransactionType'a `WRONG_TOKEN_INCOMING` eklendi
- [x] BlockchainTransaction Token semantiği dokümante edildi (beklenen vs gerçek token)
- [x] ActualTokenAddress kapsamı WRONG_TOKEN_INCOMING + WRONG_TOKEN_REFUND olarak genişletildi
- [x] SYSTEM service account sentinel değerleri tanımlandı (GUID, SteamId, DisplayName) + tasarım gerekçesi eklendi
- [x] Versiyon v2.3 → v2.4

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [x] Round 3 tetiklendi
