# GPT Cross-Review Raporu — 02_PRODUCT_REQUIREMENTS.md

**Tarih:** 2026-03-20
**Model:** OpenAI GPT (ChatGPT, manuel)
**Round:** 1
**Sonuç:** ⚠️ 6 bulgu (2 KRİTİK, 4 ORTA) + 1 Claude ek bulgu

---

## GPT Çıktısı

### BULGU-1: Otomatik iade taahhüdü bazı ödeme senaryolarında gerçekçi değil
- **Seviye:** KRİTİK
- **Kategori:** Teknik Doğruluk / Edge Case / Güvenlik
- **Konum:** §4.4, §4.6, §12.2
- **Sorun:** Yanlış token, custodial/exchange çıkışları, desteklenmeyen asset gibi durumlarda otomatik iade her zaman güvenli ve deterministik değil. Exchange uyarısı yalnızca iade adresi için var; gönderen kaynak adresin iade edilebilir olmaması riski ele alınmamış.
- **Öneri:** Senaryoları ayır. Doğrulanabilir transferler için otomatik iade, diğerleri manual review.

### BULGU-2: Flag/admin inceleme akışında timeout davranışı tanımsız ve çakışmalı
- **Seviye:** KRİTİK
- **Kategori:** Tutarlılık / Eksiklik
- **Konum:** §3.2, §3.3, §10.2, §10.3, §14.4, §16.2
- **Sorun:** Bir yandan timeout → iptal, diğer yandan flag → admin onayı bekle. Admin incelemesi sürerken timeout dolabilir. "İşlem durdurulur" ifadesi var ama timeout'un durup durmadığı tanımlı değil.
- **Öneri:** FLAGGED_REVIEW, ADMIN_HOLD gibi durumlarda timeout davranışı açıkça tanımlanmalı.

### BULGU-3: Satıcı için dispute yok denmesi fazla iddialı
- **Seviye:** ORTA
- **Kategori:** Eksiklik / Edge Case
- **Konum:** §4.5, §10.2, §12.1
- **Sorun:** Payout fail/stuck, yanlış adrese gönderim, satıcının "ödemeyi almadım" iddiası gibi senaryolar için satıcı tarafında hiçbir mekanizma tanımlı değil.
- **Öneri:** Seller payout issue sınıfı tanımlanmalı — inceleme, yeniden deneme, tx hash doğrulama, admin eskalasyon.

### BULGU-4: Blockchain doğrulama kriteri yetersiz tanımlı
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Belirsizlik
- **Konum:** §2.1 adım 4-5, §4.1, §10.1, §20.1
- **Sorun:** "Blockchain üzerinden otomatik doğrulama" deniyor ama confirmation sayısı, finality, chain reorg gibi detaylar yok.
- **Öneri:** Detected/confirmed/final statüleri, minimum confirmation, reconciliation davranışı tanımlanmalı.

### BULGU-5: "Platform cüzdanı yok" ifadesi akışla çelişiyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık / Belirsizlik
- **Konum:** §4.1, §4.5, §4.6
- **Sorun:** Platform ödeme adresi üretiyor, fon tutuyor, komisyon kesiyor, payout ve iade yapıyor — fiilen platform kontrolünde cüzdanlar var. İfade yanıltıcı.
- **Öneri:** "Kullanıcı bakiyesi tutulmaz; operasyonel adres altyapısı kullanılır" şeklinde netleştir.

### BULGU-6: Çoklu Steam hesapları için failover vaadi aşırı kesin
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk / Operasyonel Risk
- **Konum:** §2.1 adım 3 ve 6, §15
- **Sorun:** Kısıtlanan hesapta kilitli item'lar otomatik transfer edilemez. "Aktif işlemler diğer hesaplar üzerinden devam eder" vaadi her durumda doğru değil.
- **Öneri:** Yeni işlemler yönlendirilir, mevcut emanetler recovery/manual intervention.

---

## Claude Bağımsız Değerlendirmesi

> Claude, GPT'nin her bulgusunu proje bağlamı ve dokümanlarla çapraz kontrol ederek bağımsız değerlendirmiştir.
> Karar: ✅ KABUL (GPT haklı) | ❌ RET (GPT yanlış) | ⚠️ KISMİ (kısmen haklı, farklı çözüm)

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Otomatik iade taahhüdü | ⚠️ KISMİ | Yanlış token endişesi geçerli. Ancak exchange/custodial source endişesi büyük ölçüde geçersiz — §12.2'ye göre iadeler kaynak adrese değil alıcının belirlediği iade adresine yapılıyor. Gönderen adresin exchange olması iade mekanizmasını etkilemiyor. | §4.4'te yanlış token satırı ikiye ayrıldı: desteklenen TRC-20 → otomatik iade, desteklenmeyen token/kontrat → admin review. |
| 2 | Flag/timeout çakışması | ❌ RET | 05 §4.2 state machine'e göre FLAGGED durumu işlem oluşturma anında (CREATED öncesi) tetikleniyor. Timeout CREATED'dan sonra başlıyor, dolayısıyla çakışma yok. Dispute+timeout ilişkisi ise bilinçli tasarım kararı — timeout backstop görevi görüyor. GPT proje bağlamına erişimi olmadığı için bu yapıyı görememiş. | Düzeltme uygulanmadı. |
| 3 | Satıcı dispute | ⚠️ KISMİ | 06 BlockchainTransaction entity'sinde payout retry mekanizması (3 deneme, exponential backoff) + admin alert zaten tanımlı. Tam dispute mekanizması aşırı ama satıcının UI'dan payout sorununu bildirme kanalı gerçekten eksik. | §10.3 olarak "Satıcı Payout Sorunu" bölümü eklendi: bildirim → tx hash doğrulama → retry → admin eskalasyon. Eski §10.3 → §10.4 oldu. |
| 4 | Blockchain doğrulama | ⚠️ KISMİ | 05 §3.2'de 20 blok confirmation kuralı, polling interval, status transitions zaten tanımlı. 06'da ConfirmationCount ve ConfirmedAt alanları var. Teknik detay 05/08'in sorumluluğunda. Ancak 02'de ürün seviyesi referans eksik. | §4.1 doğrulama satırına "nihai (final) kabul edildikten sonra onaylanır" ve 05 §3.2 referansı eklendi. |
| 5 | "Platform cüzdanı yok" | ✅ KABUL | 05'te hot/cold wallet mimarisi açıkça tanımlı. İfade "kullanıcı cüzdanı tutulmaz" anlamında ama mevcut yazım yanıltıcı. | §4.1'deki ifade netleştirildi: "Platformda kullanıcı bakiyesi tutulmaz... operasyonel adres altyapısı kullanılır (detaylar 05 §3.2)". |
| 6 | Steam failover | ✅ KABUL | 06'da EscrowBotId ile hangi bot'un item'ı tuttuğu takip ediliyor. Kısıtlanan hesaptaki item'lar Steam trade sistemi gereği otomatik transfer edilemez. | §15'te "aktif işlemler devam eder" → "yeni işlemler yönlendirilir, mevcut emanetler recovery/manual intervention" olarak yumuşatıldı. |

### Claude'un Ek Bulguları

| # | Bulgu | Seviye | Konum | Uygulanan Aksiyon |
|---|-------|--------|-------|-------------------|
| EK-1 | §3.3'te "Steam kesintileri sırasında timeout dondurulur" deniyor ama kesintinin nasıl tespit edileceği (otomatik/manuel) tanımsız. | DÜŞÜK | §3.3, §23 | §3.3'e "Steam bot health check başarısız olduğunda otomatik algılanır; admin manuel olarak da tetikleyebilir" eklendi. |

---

## Özet

| Metrik | Değer |
|--------|-------|
| GPT bulgu sayısı | 6 (2 KRİTİK, 4 ORTA) |
| Claude kararları | 2 KABUL, 3 KISMİ, 1 RET |
| Claude ek bulgu | 1 (DÜŞÜK) |
| Toplam düzeltme | 6 (RET hariç tümü) |
| Doküman versiyonu | v1.5 → v1.6 |

---

## Kullanıcı Onayı

- [x] Kullanıcı değerlendirmeleri inceledi ve onayladı
- [x] Düzeltmeler uygulandı
- [ ] Round 2 tetiklendi
