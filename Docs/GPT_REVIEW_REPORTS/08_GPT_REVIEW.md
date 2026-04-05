# GPT Cross-Review Raporu — 08_INTEGRATION_SPEC.md

**Tarih:** 2026-03-22
**Model:** OpenAI o3 (ChatGPT, manuel)
**Round:** 1
**Sonuç:** ⚠️ 13 bulgu (3 KRİTİK, 9 ORTA, 1 DÜŞÜK)

---

## GPT Çıktısı

### BULGU-1: Steam entegrasyon sınırları doküman içinde çelişkili
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §1.1, §2.1, §2.3, §8
- **Sorun:** §1.1'de "Steam Web API" runtime'ı Node.js Sidecar ve kullanımı "profil bilgisi, envanter okuma" olarak tanımlı. Ancak §2.1 akışında login sonrası profil bilgisini .NET backend çekiyor; §2.3'te envanter okuma resmi Web API değil Community endpoint üzerinden yapılıyor. §8 risk matrisinde envanter okunamazlık "Steam Web API" çökmesine bağlanmış.
- **Öneri:** Entegrasyon envanterini üçe ayır: Steam OpenID (.NET), Steam Web API (.NET veya sidecar), Steam Community Inventory/Trade (Node.js sidecar).

### BULGU-2: GetTradeHoldDurations çağrısı eksik parametre tanımıyla verilmiş
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk
- **Konum:** §2.2
- **Sorun:** Login sonrası `GetTradeHoldDurations` çağrısı yapılacak denmiş ama bu endpoint `trade_offer_access_token` parametresi gerektirir — login anında bu token mevcut değil.
- **Öneri:** Kontrolü trade URL alındıktan sonra yap veya parametre koşulunu netleştir.

### BULGU-3: Steam API key query string'de sızıntı riski
- **Seviye:** ORTA
- **Kategori:** Güvenlik
- **Konum:** §2.2
- **Sorun:** API key query parameter olarak normatif şekilde yazılmış. Log'larda, proxy'lerde secret sızıntı riski.
- **Öneri:** `x-webapi-key` header'ını standart yap.

### BULGU-4: TRON onay kontrolü için endpoint solidified durumla hizalı değil
- **Seviye:** ORTA
- **Kategori:** Teknik Doğruluk
- **Konum:** §3.1, §3.4
- **Sorun:** `/v1/transactions/{txId}` henüz confirmed olmayan veriyi de içerebilir. Solidified/confirmed-only endpoint kullanılmıyor.
- **Öneri:** `walletsolidity/gettransactioninfobyid` veya eşdeğer confirmed-only kaynak kullan.

### BULGU-5: HD wallet derivation index akışı yarış durumuna açık
- **Seviye:** KRİTİK
- **Kategori:** Edge Case
- **Konum:** §3.2
- **Sorun:** Adres üretim akışı "son index'i oku → +1 → üret → kaydet" — atomic increment veya unique constraint tanımı yok.
- **Öneri:** DB sequence / atomic counter, unique constraint, idempotency ekle.

### BULGU-6: TronGrid outage fallback stratejisi kendi içinde çelişkili
- **Seviye:** ORTA
- **Kategori:** Tutarlılık
- **Konum:** §3.6, §8
- **Sorun:** MVP fallback "ikinci TronGrid API key" — provider-wide outage'de bu işe yaramaz.
- **Öneri:** Rate limit ve provider outage senaryolarını ayrı tanımla.

### BULGU-7: Resend bounce webhook güvenlik ve idempotency tanımı yok
- **Seviye:** ORTA
- **Kategori:** Eksiklik
- **Konum:** §4.3
- **Sorun:** Bounce webhook'u tanımlanmış ama endpoint, signing secret, idempotency yok.
- **Öneri:** Webhook endpoint'ini ayrı tanımla; signing verification ve event-id idempotency ekle.

### BULGU-8: Telegram webhook doğrulaması eksik
- **Seviye:** KRİTİK
- **Kategori:** Güvenlik
- **Konum:** §5.2
- **Sorun:** `secret_token` ve `X-Telegram-Bot-Api-Secret-Token` header doğrulaması yok. Sahte update spoofing'e açık.
- **Öneri:** `setWebhook(secret_token=...)` zorunlu; header doğrulama olmadan update işlenmesin.

### BULGU-9: Telegram getUpdates ve webhook aynı anda anlatılmış
- **Seviye:** DÜŞÜK
- **Kategori:** Teknik Doğruluk
- **Konum:** §5.2
- **Sorun:** `getUpdates` ve webhook karşılıklı dışlayıcı ama ikisi birden listelenmiş.
- **Öneri:** Webhook-only yaz; getUpdates'i alternatif olarak not et.

### BULGU-10: Telegram deep-link bağlama kodunun yaşam döngüsü tanımsız
- **Seviye:** ORTA
- **Kategori:** Eksiklik
- **Konum:** §5.1
- **Sorun:** Kodun TTL'i, tek kullanımlık olup olmadığı, brute-force koruması tanımlanmamış.
- **Öneri:** Kısa ömürlü, single-use yap; rate limit ve session binding ekle.

### BULGU-11: Discord DM modeli OAuth2 ile bot DM yetkisini karıştırıyor
- **Seviye:** KRİTİK
- **Kategori:** Teknik Doğruluk
- **Konum:** §6.1-§6.2
- **Sorun:** `identify` scope ile "OAuth2 izni ile DM garantisi" iddia ediliyor ama Discord API'de bu garanti yok.
- **Öneri:** OAuth2 = kimlik bağlama, Bot DM = ayrı operasyon olarak ayır; garanti ifadesini çıkar.

### BULGU-12: Discord scope/permission seti least-privilege'e uymuyor
- **Seviye:** ORTA
- **Kategori:** Güvenlik
- **Konum:** §6.1
- **Sorun:** `guilds.members.read` ve guild permission'ları DM-only akışta gereksiz.
- **Öneri:** Minimum scope `identify`; guild install gerekiyorsa ayrı akışta tanımla.

### BULGU-13: Discord mesaj içeriğinde mention injection önlemi yok
- **Seviye:** ORTA
- **Kategori:** Güvenlik
- **Konum:** §6.2
- **Sorun:** `allowed_mentions` veya sanitization politikası tanımlanmamış.
- **Öneri:** `allowed_mentions: { parse: [] }` varsayılanını zorunlu yap.

---

## Claude Bağımsız Değerlendirmesi

> Claude, GPT'nin her bulgusunu proje bağlamı ve dokümanlarla çapraz kontrol ederek bağımsız değerlendirmiştir.
> Karar: ✅ KABUL (GPT haklı) | ❌ RET (GPT yanlış) | ⚠️ KISMİ (kısmen haklı, farklı çözüm)

| # | GPT Bulgusu | Claude Kararı | Bağımsız Gerekçe | Uygulanan Aksiyon |
|---|-------------|---------------|-------------------|-------------------|
| 1 | Steam entegrasyon sınırları çelişkili | ✅ KABUL | 05 §3.2 sidecar'ın TÜM Steam etkileşimlerini yönettiğini söylüyor. Ancak §2.1 adım 6 "Backend → Steam Web API'den profil bilgilerini çeker" diyor — bu 05 §3.2 ile çelişir: backend doğrudan Steam API çağırmıyor, sidecar üzerinden çağırıyor. §1.1'de envanter okuma "Steam Web API" altında listelenmiş ama §2.3'te bunun Community endpoint olduğu açık. Ayrım net değil. | §1.1 entegrasyon envanteri düzeltildi: Steam Web API (sidecar) profil+MA kontrolü, Steam Community (sidecar) envanter okuma olarak ayrıldı. §2.1 adım 6 "sidecar üzerinden" ifadesiyle netleştirildi. §8 risk matrisi güncellendi. |
| 2 | GetTradeHoldDurations eksik parametre | ✅ KABUL | `IEconService/GetTradeHoldDurations/v1` arkadaş olmayan kullanıcılar için `trade_offer_access_token` gerektirir. Login anında kullanıcı trade URL'ini henüz girmemiş olduğundan bu çağrı güvenilir yapılamaz. MA kontrolünün trade URL kayıt adımına taşınması gerekiyor. | §2.2 MA kontrolü akışı güncellendi: login sonrası değil, trade URL kaydedildiğinde yapılacağı belirtildi. 03 §2.1 cross-ref notu eklendi. |
| 3 | Steam API key query string sızıntı riski | ⚠️ KISMİ | GPT'nin güvenlik kaygısı geçerli — query string log'larda görünür. Ancak bu server-to-server (sidecar → Steam) bir çağrı: proxy log'ları ve APM trace'leri platform kontrolünde. Risk gerçek ama seviye abartılı. `x-webapi-key` header desteği Steam'in tüm endpoint'lerinde garanti değil (undocumented endpoint'ler için). | Tercih olarak header yöntemi eklenip query param "legacy fallback" olarak not edildi. Sidecar'ın log masking kuralına (05 §3.5) cross-ref eklendi. |
| 4 | TRON onay endpoint'i solidified değil | ✅ KABUL | TronGrid `/v1/transactions/{txId}` mempool dahil tüm transaction'ları döndürebilir. 20 blok bekleme iş kuralı olarak var ama onay sayısını doğru sorgulamak için `walletsolidity` endpoint'leri gerekli. Platform payout/refund tetiklemesinde yanlış onay bilgisi ciddi risk. | §3.1 endpoint tablosunda onay kontrolü `walletsolidity/gettransactioninfobyid` olarak güncellendi. 20 blok kuralı ikinci seviye iş kuralı olarak netleştirildi. |
| 5 | HD wallet derivation index yarış durumu | ⚠️ KISMİ | GPT'nin tespiti haklı — 08'deki akış açıklamasında atomiklik yok. **Ancak** 06 §3.7'de `PaymentAddress.HdWalletIndex` üzerinde UNIQUE constraint tanımlı ve 05 §3.3'te "monoton artan allocator, asla reuse edilmez" kuralı var. Yani koruma mekanizması projede mevcut ama 08 bunu referans vermiyor. En kötü senaryo: constraint violation hatası → retry (duplicate adres değil). | §3.2 adres üretim akışına 06 §3.7 UNIQUE constraint ve 05 §3.3 monoton allocator cross-reference'ı eklendi. Constraint violation durumunda retry davranışı belirtildi. |
| 6 | TronGrid fallback çelişkisi | ✅ KABUL | "İkinci TronGrid API key" yalnızca rate limit sorununu çözer, provider-wide outage'de işe yaramaz. §3.6 ve §8 arasında bu ayrım net yapılmamış. | §3.6 ve §8'de iki senaryo açıkça ayrıldı: (1) Rate limit / key suspension → ikinci API key, (2) Provider outage → alternatif sağlayıcı veya self-hosted node. |
| 7 | Resend bounce webhook eksik | ✅ KABUL | Bounce webhook'tan bahsediliyor ama endpoint tanımı, signing doğrulaması, idempotency yok. Sahte bounce event'i ile adres suppress edilebilir. Resend webhook signing dokümanı bunu zorunlu kılıyor. | §4.3'e "Bounce Webhook Güvenliği" alt bölümü eklendi: endpoint tanımı, Resend signing secret (Svix) doğrulaması, raw body gereksinimi, `webhook-id` bazlı idempotent işleme. 07 webhook endpoint cross-ref eklendi. |
| 8 | Telegram webhook doğrulaması eksik | ⚠️ KISMİ | GPT'nin güvenlik kaygısı geçerli — 08'de `secret_token` tanımı yok. **Ancak** 07 §5.11b'de bu koruma zaten tanımlı: "Telegram `X-Telegram-Bot-Api-Secret-Token` header'ı ile doğrulama (webhook set edilirken belirtilen secret ile eşleşme kontrolü)". Sorun 08'in 07'yi referans vermemesi, korumanın hiç olmaması değil. | §5.2'ye `setWebhook` çağrısında `secret_token` parametresi eklendi ve 07 §5.11b'deki güvenlik tanımına cross-reference verildi. |
| 9 | getUpdates ve webhook aynı anda | ✅ KABUL | Telegram API'de getUpdates ve webhook karşılıklı dışlayıcı. İkisinin aynı tabloda listelenmesi kafa karıştırıcı. | §5.2 method tablosunda getUpdates "yalnızca webhook kurulmadan önce test/debug için" notu eklendi. MVP kararının webhook-only olduğu vurgulandı. |
| 10 | Telegram deep-link kod yaşam döngüsü | ✅ KABUL | Doğrulama kodunun TTL, single-use ve brute-force koruması tanımlanmamış. Ele geçirilmiş kod ile yanlış chat bağlanabilir. | §5.1'e kod yaşam döngüsü tanımı eklendi: 10 dakika TTL, single-use, session-bound, 5 başarısız deneme sonrası invalidation. |
| 11 | Discord OAuth2 vs Bot DM karışıklığı | ✅ KABUL | Discord API'de `identify` scope DM yetkisi vermez. Bot DM göndermek için kullanıcıyla ortak sunucu olması veya kullanıcının DM'lerinin açık olması gerekir — OAuth2 bunu garanti etmez. Doküman §6.4'te "DM kapalı → 403" hata senaryosunu zaten tanımlıyor ama §6.1'deki "OAuth2 izni ile DM gönderebilir" ifadesi yanıltıcı. | §6.1'den "OAuth2 izni ile DM gönderebilir" ifadesi kaldırıldı. Akış ikiye ayrıldı: OAuth2 = kimlik bağlama, Bot DM = ayrı operasyon (başarısız olabilir — §6.4 hata tablosu geçerli). Başarısız DM durumunda fallback kanalları (email, platform-içi) notu eklendi. |
| 12 | Discord scope least-privilege | ✅ KABUL | `guilds.members.read` DM-only akışta gereksiz. `Send Messages` ve `Use Slash Commands` guild permission'ları bot scope ile guild install bağlamında anlamlı — DM akışında değil. | §6.1 scope listesi `identify` olarak daraltıldı. Guild install gerekirse ayrı akışta tanımlanacağı notu eklendi. Gereksiz permission'lar kaldırıldı. |
| 13 | Discord mention injection | ✅ KABUL | `allowed_mentions` olmadan user-generated string'ler (item adı, kullanıcı adı) mesaj içeriğine gömülürse istenmeyen mention/ping oluşabilir. Discord varsayılan olarak mention parsing'i aktif tutar. | §6.2 mesaj gönderim tanımına `allowed_mentions: { parse: [] }` varsayılanı eklendi. Bilinçli mention gereken şablonlar için istisna mekanizması notu eklendi. |

### Claude'un Ek Bulguları

| # | Bulgu | Seviye | Konum | Uygulanan Aksiyon |
|---|-------|--------|-------|-------------------|
| C1 | §2.4 trade offer durumları tablosunda `Countered (4)` durumu eksik — Steam API bu durumu döndürebilir, Skinora'nın bunu nasıl yöneteceği tanımlanmamış | ORTA | §2.4 | `Countered (4)` durumu eklendi: "Platform tarafından counter offer yapılmaz — bu durum alıcı/satıcı arasında oluşamaz, ignore edilir" notu konuldu. |
| C2 | §9.2 credential envanterinde Resend webhook signing secret (Svix key) eksik — BULGU-7 düzeltmesiyle uyumlu olarak eklenmeli | DÜŞÜK | §9.2 | `Resend Webhook Signing Secret` credential envanterine eklendi. |

### Özet

| Karar | Sayı |
|-------|------|
| ✅ KABUL | 10 |
| ⚠️ KISMİ | 3 |
| ❌ RET | 0 |
| Claude ek bulgu | 2 |
| **Toplam düzeltme** | **15** |

---

**Sonraki adım:** Dokümanı R1 bulgularına göre güncelleyip v1.4 yapılacak. Güncelleme sonrası GPT'ye R2 gönderilecek.
