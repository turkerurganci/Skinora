# Audit Raporu — 08_INTEGRATION_SPEC.md

**Tarih:** 2026-03-19
**Hedef:** 08_INTEGRATION_SPEC.md (v1.0) — Entegrasyon Spesifikasyonları
**Bağlam:** 02, 03, 05, 06, 07, 10
**Odak:** Tam denetim

---

## Envanter Özeti

| Kaynak | Toplam Öğe | ✓ | ⚠ | ✗ |
|---|---|---|---|---|
| 05 — Teknik Mimari | 28 | 25 | 2 | 1 |
| 02 — Ürün Gereksinimleri | 32 | 30 | 1 | 1 |
| 03 — Kullanıcı Akışları | 18 | 17 | 1 | 0 |
| 06 — Veri Modeli | 14 | 12 | 2 | 0 |
| 07 — API Tasarımı | 12 | 11 | 1 | 0 |
| 10 — MVP Kapsamı | 8 | 8 | 0 | 0 |
| 08 (iç) — Hedef doküman | 22 | 20 | 1 | 1 |
| **Toplam** | **134** | **123** | **8** | **3** |

---

## Envanter ve Eşleştirme Detayı

### Envanter — 05_TECHNICAL_ARCHITECTURE

| # | ID | Öğe Özeti | Hedef Bölüm | Durum |
|---|---|---|---|---|
| 1 | 05-§3.2-01 | Steam Sidecar: steam-tradeoffer-manager, steamcommunity, steam-totp kütüphaneleri | §2.5 | ✓ |
| 2 | 05-§3.2-02 | Bot health check: 60 saniye aralıkla session kontrolü | — | ✗ |
| 3 | 05-§3.2-03 | Trade offer retry: exponential backoff, timeout süresi içinde | §2.7 | ✓ |
| 4 | 05-§3.2-04 | Tüm botlar down → yeni trade offer engellenir, admin critical alert | §2.8 | ✓ |
| 5 | 05-§3.2-05 | Bot seçimi: capacity-based (en düşük ActiveEscrowCount) | §2.4 | ✓ |
| 6 | 05-§3.2-06 | .NET → Sidecar HTTP REST, webhook callback | §2.4 | ✓ |
| 7 | 05-§3.2-07 | Bot crash sonrası pending offer'lar restart'ta tekrar kontrol | §2.7 | ✓ |
| 8 | 05-§3.3-01 | Blockchain Service: TronWeb, TronGrid API, HD Wallet (BIP-44) | §3.1, §3.2 | ✓ |
| 9 | 05-§3.3-02 | Ödeme onayı: minimum 20 blok (~60 saniye), polling 3 saniye | §3.4 | ✓ |
| 10 | 05-§3.3-03 | Gecikmeli izleme: 30s → 5dk → 1saat → 30 gün sonra stop | §3.4 | ✓ |
| 11 | 05-§3.3-04 | İade hedefi: alıcının iade adresi (kabul sırasında belirtilen) | §3.4 | ✓ |
| 12 | 05-§3.3-05 | Minimum iade eşiği: iade < 2× gas fee → iade yapılmaz, admin alert | — | ⚠ |
| 13 | 05-§3.3-06 | Eksik tutar: red → iade, timeout devam | §3.4 | ✓ |
| 14 | 05-§3.3-07 | Fazla tutar: doğru tutar kabul, fazla iade | §3.4 | ✓ |
| 15 | 05-§3.3-08 | Satıcıya ödeme retry: 3 deneme (1dk, 5dk, 15dk), sonra admin alert | §3.5 | ✓ |
| 16 | 05-§3.3-09 | Master seed: production vault/Docker Secrets, dev env var | §3.2 | ✓ |
| 17 | 05-§3.3-10 | Hot wallet limit: operasyonel miktar, fazlası cold wallet, admin belirler | — | ⚠ |
| 18 | 05-§3.3-11 | Private key: memory'de sadece imzalama anında, sonra temizlenir | §3.2 | ✓ |
| 19 | 05-§3.5-01 | Tron private key: Key Vault, memory'de sadece imzalama anında | §3.2 | ✓ |
| 20 | 05-§6.1-01 | Kimlik doğrulama: Steam OpenID | §2.1 | ✓ |
| 21 | 05-§7.2-01 | Email: Resend veya SendGrid, HTML template, retry 3×(1dk,5dk,15dk) | §4.1, §4.3 | ✓ |
| 22 | 05-§7.2-02 | Telegram/Discord: Bot API, retry 3×(1dk,5dk,15dk) | §5.4, §6.4 | ✓ |
| 23 | 05-§7.3-01 | Email template: .resx dosyaları, 4 dil desteği | §4.2 | ✓ |
| 24 | 05-§9.1-01 | Tüm dış çağrılar correlationId ile loglanır | §1.2 | ✓ |
| 25 | 05-§9.3-01 | Admin alerting: Telegram bot (primary), Email (critical) | §5.5, §4.4 | ✓ |
| 26 | 05-§9.5-01 | Health endpoint: DB, Redis, Steam API, Tron node bağlantı kontrolü | §1.2 | ✓ |
| 27 | 05-§8.2-01 | Development: testnet, test bot, sandbox | §9.1, §9.3 | ✓ |
| 28 | 05-§8.2-02 | Production: mainnet, gerçek hesaplar | §9.1 | ✓ |

**Toplam: 28 öğe (25 ✓, 2 ⚠, 1 ✗)**

---

### Envanter — 02_PRODUCT_REQUIREMENTS (entegrasyon ilişkili öğeler)

| # | ID | Öğe Özeti | Hedef Bölüm | Durum |
|---|---|---|---|---|
| 1 | 02-§2.1-01 | Steam envanter okuma, item seçimi | §2.3 | ✓ |
| 2 | 02-§2.1-02 | Platform satıcıya trade offer gönderir | §2.4 | ✓ |
| 3 | 02-§2.1-03 | Platform blockchain üzerinden ödeme doğrular | §3.4 | ✓ |
| 4 | 02-§2.1-04 | Platform alıcıya trade offer gönderir | §2.4 | ✓ |
| 5 | 02-§2.1-05 | Platform satıcıya ödeme gönderir (komisyon kesilerek) | §3.5 | ✓ |
| 6 | 02-§4.1-01 | USDT ve USDC destekli | §3.3 | ✓ |
| 7 | 02-§4.1-02 | Tron TRC-20 ağı | §3.1 | ✓ |
| 8 | 02-§4.1-03 | Her işlem için benzersiz ödeme adresi | §3.2 | ✓ |
| 9 | 02-§4.1-04 | Dış cüzdan modeli | §3.2 | ✓ |
| 10 | 02-§4.4-01 | Eksik tutar: red + iade | §3.4 | ✓ |
| 11 | 02-§4.4-02 | Fazla tutar: doğru kabul + fazla iade | §3.4 | ✓ |
| 12 | 02-§4.4-03 | Yanlış token: red + iade | §3.4 | ✓ |
| 13 | 02-§4.4-04 | Gecikmeli ödeme: 30 gün izleme, otomatik iade | §3.4 | ✓ |
| 14 | 02-§4.6-01 | Tam iade (komisyon dahil), gas fee düşülür | §3.4 | ✓ |
| 15 | 02-§4.7-01 | Gas fee koruma eşiği: %10 varsayılan, admin ayarlanabilir | §3.5 referans | ✓ |
| 16 | 02-§11-01 | Steam ile giriş | §2.1 | ✓ |
| 17 | 02-§11-02 | Steam Mobile Authenticator zorunlu | §2.2 | ✓ |
| 18 | 02-§14.4-01 | Piyasa fiyat verisi çekme (fraud detection) | §7 | ✓ |
| 19 | 02-§14.4-02 | Sapma eşiği admin ayarlanabilir, aşıldığında FLAGGED | §7.3 | ✓ |
| 20 | 02-§15-01 | Birden fazla Steam hesabı, risk dağıtımı | §2.4, §2.8 | ✓ |
| 21 | 02-§15-02 | Kısıtlanan hesap havuzdan çıkar, diğerleri devam | §2.8 | ✓ |
| 22 | 02-§15-03 | Admin panelinden Steam hesap durumu izleme | §8 risk matrisi | ✓ |
| 23 | 02-§18.1-01 | Bildirim kanalları: platform içi, email, Telegram/Discord | §4, §5, §6 | ✓ |
| 24 | 02-§3.3-01 | Timeout freeze: platform bakımı | §2.8, §8 | ✓ |
| 25 | 02-§3.3-02 | Timeout freeze: Steam kesintisi | §2.8 | ✓ |
| 26 | 02-§12.1-01 | Cüzdan adresi değişikliğinde Steam ek doğrulama | §2.1 referans | ✓ |
| 27 | 02-§12.3-01 | Adres format doğrulama: geçerli TRC-20 adresi | §3.2 | ✓ |
| 28 | 02-§9-01 | Envanter public olmalı, private ise uyarı | §2.3 | ✓ |
| 29 | 02-§9-02 | Item tradeable kontrolü | §2.3 | ✓ |
| 30 | 02-§4.2-01 | Tek stablecoin per işlem (satıcı seçer) | §3.3, §3.4 | ✓ |
| 31 | 02-§14.4-03 | Kısa sürede yüksek hacim tespiti (admin eşikleri) | §7.3 referans | ⚠ |
| 32 | 02-§12.2-05 | Exchange'den gönderim uyarısı | — | ✗ |

**Toplam: 32 öğe (30 ✓, 1 ⚠, 1 ✗)**

---

### Envanter — 03_USER_FLOWS (entegrasyon ilişkili öğeler)

| # | ID | Öğe Özeti | Hedef Bölüm | Durum |
|---|---|---|---|---|
| 1 | 03-§2.1-01 | Steam ile giriş akışı: yönlendirme → onay → callback | §2.1 | ✓ |
| 2 | 03-§2.1-02 | MA kontrolü: trade hold süresi > 0 ise uyarı | §2.2 | ✓ |
| 3 | 03-§2.2-01 | Envanter okuma: Steam Community endpoint | §2.3 | ✓ |
| 4 | 03-§2.3-01 | Trade offer gönderimi: sidecar üzerinden | §2.4 | ✓ |
| 5 | 03-§2.3-02 | Trade offer retry: API hatası veya item durumu değişirse | §2.7 | ✓ |
| 6 | 03-§2.3-03 | Trade offer reddi: işlem CANCELLED_SELLER | §2.4, §2.7 | ✓ |
| 7 | 03-§3.4-01 | Ödeme adresi üretimi ve gösterimi | §3.2 | ✓ |
| 8 | 03-§3.4-02 | Blockchain'de ödeme tespiti ve doğrulama | §3.4 | ✓ |
| 9 | 03-§3.5-01 | Alıcıya trade offer: teslim | §2.4 | ✓ |
| 10 | 03-§2.4-01 | Satıcıya ödeme gönderimi + retry | §3.5 | ✓ |
| 11 | 03-§5.1-01 | Eksik tutar: red, iade, timeout devam | §3.4 | ✓ |
| 12 | 03-§5.2-01 | Fazla tutar: kabul + fazla iade | §3.4 | ✓ |
| 13 | 03-§5.3-01 | Yanlış token: iade | §3.4 | ✓ |
| 14 | 03-§5.4-01 | Gecikmeli ödeme: iptal sonrası izleme + iade | §3.4 | ✓ |
| 15 | 03-§11.1-01 | Platform bakımı: timeout dondurma | §2.8, §8 | ✓ |
| 16 | 03-§11.2-01 | Steam kesintisi: timeout dondurma | §2.8 | ✓ |
| 17 | 03-§12-01 | Bildirim tetikleyicileri (satıcı/alıcı/admin) | §5, §6 referans | ✓ |
| 18 | 03-§3.4-03 | Exchange'den gönderim uyarısı (ödeme ekranında) | — | ⚠ |

**Toplam: 18 öğe (17 ✓, 1 ⚠, 0 ✗)**

---

### Envanter — 06_DATA_MODEL (entegrasyon ilişkili entity/field'lar)

| # | ID | Öğe Özeti | Hedef Bölüm | Durum |
|---|---|---|---|---|
| 1 | 06-§3.1-01 | User.SteamId: SteamID64 | §2.1, §2.2 | ✓ |
| 2 | 06-§3.1-02 | User.SteamDisplayName: Steam profil adı | §2.2 | ⚠ |
| 3 | 06-§3.1-03 | User.SteamAvatarUrl: profil fotoğrafı | §2.2 | ✓ |
| 4 | 06-§3.1-04 | User.MobileAuthenticatorVerified: MA durumu | §2.2 | ⚠ |
| 5 | 06-§3.7-01 | PaymentAddress entity: adres, monitoring status | §3.2, §3.4 | ✓ |
| 6 | 06-§3.8-01 | BlockchainTransaction entity: 7 tür, 5 durum, hash, tutar | §3.4, §3.5 | ✓ |
| 7 | 06-§3.9-01 | TradeOffer entity: status enum (6 durum) | §2.4 | ✓ |
| 8 | 06-§3.10-01 | PlatformSteamBot entity: status, ActiveEscrowCount | §2.4 | ✓ |
| 9 | 06-§3.4-01 | UserNotificationPreference: channel, ExternalId | §5.1, §6.1 | ✓ |
| 10 | 06-§2.8-01 | TradeOfferStatus enum: PENDING,SENT,ACCEPTED,DECLINED,EXPIRED,FAILED | §2.4 | ✓ |
| 11 | 06-§2.15-01 | PlatformSteamBotStatus enum: ACTIVE,RESTRICTED,BANNED,OFFLINE | §2.8 | ✓ |
| 12 | 06-§2.17-01 | PaymentAddressMonitoringStatus: 6 durum | §3.4 | ✓ |
| 13 | 06-§3.8-02 | BlockchainTransaction.RetryCount, ErrorMessage | §3.5 | ✓ |
| 14 | 06-§3.17-01 | SystemSetting.hot_wallet_limit | §3.3 | ✓ |

**Toplam: 14 öğe (12 ✓, 2 ⚠, 0 ✗)**

---

### Envanter — 07_API_DESIGN (entegrasyon ilişkili endpoint'ler)

| # | ID | Öğe Özeti | Hedef Bölüm | Durum |
|---|---|---|---|---|
| 1 | 07-§4.2-01 | A1 GET /auth/steam → Steam OpenID redirect | §2.1 | ✓ |
| 2 | 07-§4.3-01 | A2 GET /auth/steam/callback → token oluşturma | §2.1 | ✓ |
| 3 | 07-§4.8-01 | A7 POST /auth/check-authenticator → MA kontrolü | §2.2 | ✓ |
| 4 | 07-§6.1-01 | S1 GET /steam/inventory → envanter çekme | §2.3 | ✓ |
| 5 | 07-§5.9-01 | U9 POST /users/me/settings/telegram/connect | §5.1 | ✓ |
| 6 | 07-§5.12-01 | U10 POST /users/me/settings/discord/connect | §6.1 | ✓ |
| 7 | 07-§10.2-01 | P2 GET /platform/maintenance → Steam/platform durumu | §8 | ✓ |
| 8 | 07-§9.10-01 | AD10 GET /admin/steam-accounts → bot durumu | §2.8, §8 | ✓ |
| 9 | 07-§11.1-01 | RT1 PaymentDetected, PaymentConfirmed events | §3.4 referans | ✓ |
| 10 | 07-§11.1-02 | RT1 CountdownSync: frozen, frozenReason (downtime) | §2.8 referans | ✓ |
| 11 | 07-§11.2-01 | RT2 TelegramConnected, DiscordConnected events | §5.1, §6.1 referans | ✓ |
| 12 | 07-Genel | Telegram webhook endpoint (/webhooks/telegram) 07'de yok | §5.2 | ⚠ |

**Toplam: 12 öğe (11 ✓, 1 ⚠, 0 ✗)**

---

### Envanter — 10_MVP_SCOPE

| # | ID | Öğe Özeti | Hedef Bölüm | Durum |
|---|---|---|---|---|
| 1 | 10-§2.2-01 | USDT ve USDC (ikisi de), Tron TRC-20 | §3.1, §3.3 | ✓ |
| 2 | 10-§2.2-02 | Dış cüzdan, otomatik doğrulama, gas fee yönetimi | §3.2, §3.3, §3.4 | ✓ |
| 3 | 10-§2.9-01 | Steam envanter okuma, item doğrulama | §2.3 | ✓ |
| 4 | 10-§2.10-01 | Birden fazla bot, failover, admin izleme | §2.4, §2.8 | ✓ |
| 5 | 10-§2.13-01 | Platform içi + email + Telegram/Discord bildirimleri | §4, §5, §6 | ✓ |
| 6 | 10-§2.14-01 | Timeout freeze: bakım ve Steam kesintisi | §2.8 | ✓ |
| 7 | 10-§2.8-01 | Fraud detection: piyasa fiyatı çekme | §7 | ✓ |
| 8 | 10-§4-01 | CS2 only (appid 730) | §2.3, §7.2 | ✓ |

**Toplam: 8 öğe (8 ✓, 0 ⚠, 0 ✗)**

---

### Envanter — 08_INTEGRATION_SPEC (iç tutarlılık)

| # | ID | Öğe Özeti | Hedef Bölüm | Durum |
|---|---|---|---|---|
| 1 | 08-§1.1-01 | 9 entegrasyon envanteri tutarlı | §2-§7 | ✓ |
| 2 | 08-§1.2-01 | Ortak pattern'ler (retry, circuit breaker, timeout, logging, health, cred) | §2-§7 | ✓ |
| 3 | 08-§1.3-01 | Circuit breaker parametreleri (failure:5, recovery:30s, probe:1, success:2) | Tüm bölümler | ✓ |
| 4 | 08-§2.1-01 | OpenID akışı: 6 adım, parametreler, güvenlik kontrolleri | İç | ✓ |
| 5 | 08-§2.2-01 | Web API: 2 endpoint, response field mapping | İç | ⚠ |
| 6 | 08-§2.2-02 | profileurl → User.SteamProfileUrl referansı | 06 ile | ✗ |
| 7 | 08-§2.3-01 | Envanter endpoint: parametreler, response, cache stratejisi | İç | ✓ |
| 8 | 08-§2.4-01 | Trade offer: 7 durum, polling 10s, mobile confirmation | İç | ✓ |
| 9 | 08-§2.5-01 | Kütüphaneler: 4 kütüphane, versiyon pinning | İç | ✓ |
| 10 | 08-§2.6-01 | Rate limit tahminleri ve platform korumaları | İç | ✓ |
| 11 | 08-§2.7-01 | 10 hata senaryosu + session retry | İç | ✓ |
| 12 | 08-§2.8-01 | 5 bağımlılık riski + mitigasyon | İç | ✓ |
| 13 | 08-§3.1-01 | TronWeb ve TronGrid API detayları | İç | ✓ |
| 14 | 08-§3.2-01 | HD Wallet (BIP-44) detayları, güvenlik | İç | ✓ |
| 15 | 08-§3.3-01 | USDT/USDC kontrat adresleri, Energy gereksinimleri | İç | ✓ |
| 16 | 08-§3.4-01 | Monitoring: aktif + gecikmeli polling, tutar doğrulama | İç | ✓ |
| 17 | 08-§3.5-01 | 8 hata senaryosu + retry | İç | ✓ |
| 18 | 08-§3.6-01 | 5 bağımlılık riski + yedek node stratejisi | İç | ✓ |
| 19 | 08-§7.1-01 | Steam Market Price API: ücretsiz, unofficial, kısıtlamalar | İç | ✓ |
| 20 | 08-§8-01 | Bağımlılık risk matrisi: 9 entegrasyon + eşzamanlı senaryolar | İç | ✓ |
| 21 | 08-§9.1-01 | Ortam konfigürasyonu: 10 entegrasyon × 3 ortam | İç | ✓ |
| 22 | 08-§9.2-01 | Credential envanteri: 10 credential türü | İç | ✓ |

**Toplam: 22 öğe (20 ✓, 1 ⚠, 1 ✗)**

---

## Bulgular

| # | Envanter ID | Tür | Seviye | Bulgu | Öneri |
|---|---|---|---|---|---|
| 1 | 08-§2.2-02 | Tutarsızlık | **High** | §2.2 response mapping'de `profileurl → User.SteamProfileUrl` referansı var, ancak 06 §3.1 User entity'sinde `SteamProfileUrl` alanı **mevcut değil**. | Mapping tablosundan `profileurl` satırını **kaldır**. Profil URL'si `https://steamcommunity.com/profiles/{SteamId}` formülüyle runtime'da türetilebilir — ayrı field gerekmez. |
| 2 | 06-§3.1-02 | Tutarsızlık | **High** | §2.2 response mapping'de `personaname → User.SteamPersonaName` yazılmış, ancak 06 §3.1'de alan adı `SteamDisplayName`. İki doküman arasında **isimlendirme uyumsuzluğu** var. | §2.2 mapping'i `User.SteamDisplayName` olarak **düzelt**. |
| 3 | 05-§3.3-05 | Eksik | **Medium** | §3.4 Tutar doğrulama tablosunda eksik/fazla/yanlış token senaryoları var ama **minimum iade eşiği** (05 §3.3: "iade < 2× gas fee → iade yapılmaz, admin alert") belirtilmemiş. | §3.4 tutar doğrulama tablosuna veya §3.5 hata senaryolarına minimum iade eşiği kuralını ekle. |
| 4 | 05-§3.3-10 | Eksik | **Medium** | §3.3 sadece hot wallet TRX bakiyesini (Energy/Bandwidth için) kapsar. 05 §3.3 ve 06 §3.17 SystemSetting'deki `hot_wallet_limit` — USDT/USDC token bakiye limiti ve cold wallet transfer mekanizması — 08'de hiç geçmiyor. | §3.3'e "Hot wallet token bakiye limiti" satırı ekle: admin eşiği, aşıldığında alert, cold wallet transfer (MVP'de manuel). |
| 5 | 06-§3.1-04 | Eksik | **Low** | §2.2'de MA kontrolü açıklanıyor (GetTradeHoldDurations → hold süresi > 0 ise uyarı) ama sonucun `User.MobileAuthenticatorVerified` field'ına **nasıl/ne zaman yazıldığı** belirtilmiyor. | §2.2 MA kontrolü bölümüne "Sonuç `User.MobileAuthenticatorVerified` field'ına kaydedilir (06 §3.1)" notu ekle. |
| 6 | 02-§14.4-03 | Kısmi | **Low** | §7.3 fraud kontrolünde fiyat sapması detaylı açıklanmış ama 02 §14.4'teki **kısa sürede yüksek hacim tespiti** 08'de sadece dolaylı referans var. Fiyat API'si yüksek hacim tespiti için kullanılmaz — bu ayrı bir iç mekanizma. | §7.3'e kısa bir not ekle: "Yüksek hacim tespiti fiyat API'sinden bağımsızdır — iç transaction sayacı ile çalışır (05 §3.1 Fraud modülü)." Bu sayede 08'in fiyat bölümünün kapsamı netleşir. |
| 7 | 07-Genel | Çapraz referans | **Low** | §5.2 Telegram webhook URL'si (`/api/v1/webhooks/telegram`) tanımlı ama 07_API_DESIGN'da bu endpoint listelenmemiş. | İki seçenek: (a) 07'ye webhook endpoint ekle, veya (b) 08 §5.2'ye "Bu endpoint 07'ye eklenmelidir" notu ekle. Önerilen: 07'ye eklenmesi (ama bu 07'nin güncellemesi — sonraki checkpoint'te ele alınabilir). |
| 8 | 02-§12.2-05 | Eksik | **Low** | 02 §12.2 ve 03 §3.4'te "Exchange'den gönderim yapmayın" uyarısı var. Bu uyarı 08'in kapsamı dışında (UI/UX konusu) ama §3.4 ödeme izleme bölümünde exchange gönderiminin **teknik riski** (iade adresinin exchange hot wallet olması) belirtilebilir. | §3.4 tutar doğrulama tablosuna isteğe bağlı not: "Exchange'den gönderimde iade, exchange'in hot wallet'ına gider — kullanıcıya ulaşmayabilir. Uyarı UI katmanında gösterilir (02 §12.2)." |
| 9 | 03-§3.4-03 | Yineleme | **Low** | Bulgu 8 ile aynı konu — 03 §3.4'teki exchange uyarısı 08'de teknik risk olarak belirtilebilir. | Bulgu 8 ile birlikte çözülür. |

---

## Aksiyon Planı

**High (08'de düzeltilmeli):**
- [ ] Bulgu 1: §2.2'den `profileurl → User.SteamProfileUrl` satırını kaldır
- [ ] Bulgu 2: §2.2'de `User.SteamPersonaName` → `User.SteamDisplayName` olarak düzelt

**Medium (08'de düzeltilmeli):**
- [ ] Bulgu 3: §3.4 veya §3.5'e minimum iade eşiği kuralı ekle (iade < 2× gas fee → iade yapılmaz, admin alert)
- [ ] Bulgu 4: §3.3'e hot wallet USDT/USDC bakiye limiti mekanizması ekle

**Low (farkındalık / opsiyonel iyileştirme):**
- [ ] Bulgu 5: §2.2'ye MobileAuthenticatorVerified field güncellemesi notu ekle
- [ ] Bulgu 6: §7.3'e yüksek hacim tespiti kapsam notu ekle
- [ ] Bulgu 7: Telegram webhook endpoint → 07'ye eklenmesi checkpoint'te ele alınacak
- [ ] Bulgu 8-9: §3.4'e exchange iade riski notu ekle (opsiyonel)

---

*Audit tamamlandı — 134 öğe denetlendi, 9 bulgu üretildi (2 High, 2 Medium, 5 Low)*
