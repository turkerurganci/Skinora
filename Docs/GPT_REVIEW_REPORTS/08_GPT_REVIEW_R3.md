# GPT Cross-Review Raporu — 08_INTEGRATION_SPEC.md

**Tarih:** 2026-03-22
**Model:** OpenAI o3 (ChatGPT, manuel)
**Round:** 3
**Sonuç:** ⚠️ 6 bulgu (1 KRİTİK, 4 ORTA, 1 DÜŞÜK)

---

## GPT Çıktısı

### BULGU-1: Energy delegation endpoint'leri §3.1'de eksik
- **Seviye:** ORTA
- **Kategori:** Eksiklik
- **Konum:** §3.1, §3.3
- **Sorun:** Energy delegation akışı §3.3'te tanımlı ama gerekli `delegateresource` ve `undelegateresource` endpoint'leri §3.1 listesinde yok.
- **Öneri:** Endpoint'leri ekle; akış ve fallback'i netleştir.

### BULGU-2: HD wallet index alan adı iki farklı isimle geçiyor
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §3.2
- **Sorun:** Aynı bölümde `PaymentAddress.HdWalletIndex` ve `PaymentAddress.DerivationIndex` geçiyor — aynı alan farklı isimlerle anılıyor.
- **Öneri:** Tek canonical isim kullan.

### BULGU-3: Refund adresi davranışı çelişkili
- **Seviye:** KRİTİK
- **Kategori:** Tutarlılık
- **Konum:** §3.4
- **Sorun:** "İade o exchange adresine gider" ve "iade adresi alıcının belirttiği adrestir" ifadeleri çelişiyor — refund source address'e mi gidiyor yoksa user-specified adrese mi?
- **Öneri:** Tek cümleyle netleştir: "refund source address'e gider" veya "user-specified adrese gider".

### BULGU-4: §3.5 hata tablosu §3.6 ile hizalı değil
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §3.5, §3.6
- **Sorun:** Hata tablosunda "yedek node'a geç" aksiyonu var ama §3.6 MVP'de yedek node olmadığını söylüyor.
- **Öneri:** Hata tablosunu §3.6 ile birebir hizala.

### BULGU-5: Discord DM install-context önkoşulu tanımlanmamış
- **Seviye:** ORTA
- **Kategori:** Eksiklik
- **Konum:** §6.1, §6.2
- **Sorun:** DM göndermek için mutual guild veya user-install gerekiyor ama doküman hangi yaklaşımın kullanılacağını tanımlamıyor.
- **Öneri:** Tek DM önkoşulu seç ve tanımla.

### BULGU-6: Telegram webhook max_connections sabit 100 yazılmış
- **Seviye:** DÜŞÜK
- **Kategori:** Teknik Doğruluk
- **Konum:** §5.3
- **Sorun:** Varsayılan 40, ayarlanabilir 1-100 arasında. setWebhook parametreleri tanımlanmamış.
- **Öneri:** max_connections, allowed_updates, drop_pending_updates değerlerini tablolaştır.

---

## Claude Bağımsız Değerlendirmesi

> Claude, GPT'nin her bulgusunu proje bağlamı ve dokümanlarla çapraz kontrol ederek bağımsız değerlendirmiştir.
> Karar: ✅ KABUL (GPT haklı) | ❌ RET (GPT yanlış) | ⚠️ KISMİ (kısmen haklı, farklı çözüm)

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Energy delegation endpoint'leri eksik | ✅ KABUL | §3.3'te energy delegation akışı (sweep öncesi delegation → sweep → delegation geri alımı) açıkça tanımlı ve 05 §3.3'e referans veriyor. Ancak §3.1'deki endpoint listesinde `delegateresource` ve `undelegateresource` yok. TRON resmi dokümanında bu iki endpoint ayrı API'ler olarak tanımlı. Entegrasyon spesifikasyonu kullanılan tüm API yüzeyini listelemelidir. | §3.1 endpoint tablosuna `POST /wallet/delegateresource` ve `POST /wallet/undelegateresource` eklendi, kullanım bağlamı ve sıklığı belirtildi. |
| 2 | HdWalletIndex vs DerivationIndex | ✅ KABUL | 06 §3.7'de canonical alan adı `HdWalletIndex` olarak tanımlı — `DerivationIndex` hiçbir yerde yok. 08 §3.2'deki güvenlik tablosunda R1'de eklenen atomiklik notunda `HdWalletIndex` yazarken, eski index yönetimi satırında `DerivationIndex` kalmış. Açık tutarsızlık. | `PaymentAddress.DerivationIndex` → `PaymentAddress.HdWalletIndex` olarak düzeltildi (06 §3.7 ile canonical). |
| 3 | Refund adresi çelişkisi | ✅ KABUL | Eski metin iki farklı model ima ediyordu: "iade o exchange adresine gider" (source address modeli) ve "iade adresi alıcının belirttiği adrestir" (user-specified modeli). Blockchain iade pratiğinde Skinora'nın kullanıcıdan ayrı refund adresi almadığı 05 §3.3'ten doğrulanabilir — iade her zaman gönderim yapan kaynak adrese gider. | §3.4 refund politikası tek canonical kuralla netleştirildi: "İade her zaman gönderim yapan kaynak adrese (source address) gönderilir." Exchange riski notu bu kuralla hizalandı, çelişkili "alıcının belirttiği adres" ifadesi kaldırıldı. |
| 4 | §3.5 hata tablosu §3.6 ile hizalı değil | ✅ KABUL | R1'de §3.6 strateji tablosu düzeltildi ama §3.5 hata tablosundaki ilk satır hala "Yedek node'a geç (§3.6)" diyordu — MVP'de yedek node yok. | §3.5 ilk satırı ikiye ayrıldı: rate limit/key suspension → ikinci API key, provider-wide outage → MVP bekleme, büyüme alternatif sağlayıcı. Terminoloji §3.6 ile birebir hizalandı. |
| 5 | Discord DM install-context | ✅ KABUL | Discord API'de bot DM gönderebilmesi için mutual guild veya user-install gerekiyor. Doküman "DM ayarları açıksa veya ortak sunucu varsa" diyordu ama hangi yöntemin seçildiğini tanımlamıyordu. User-install güncel Discord app modelinde ayrı yapılandırma gerektiriyor. | §6.1-6.2'ye DM önkoşulları tablosu eklendi: MVP'de mutual guild (Skinora Discord sunucusu), büyüme aşamasında user-install değerlendirmesi. Her iki durumda da DM başarısız olabilir — fallback kanalları notu korundu. |
| 6 | Telegram webhook max_connections | ✅ KABUL | Telegram'da max_connections varsayılanı 40, 1-100 arası ayarlanabilir — sabit 100 yazmak yanlış. Ayrıca setWebhook parametreleri (allowed_updates, drop_pending_updates) tanımlanmamıştı. | §5.3'te sabit "100" değeri düzeltildi, setWebhook kurulum parametreleri tablosu eklendi (url, secret_token, max_connections=40, allowed_updates=["message"], drop_pending_updates=true). |

### Claude'un Ek Bulguları

Ek bulgu yok.

### Özet

| Karar | Sayı |
|-------|------|
| ✅ KABUL | 6 |
| ⚠️ KISMİ | 0 |
| ❌ RET | 0 |
| Claude ek bulgu | 0 |
| **Toplam düzeltme** | **6** |

---

**Sonraki adım:** v1.6 → GPT'ye R4 gönderilecek.
