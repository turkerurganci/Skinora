# Skinora — Integration Specifications

**Versiyon: v2.5** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_DESIGN.md` | **Son güncelleme:** 2026-03-22

---

## İçindekiler

1. [Genel Bakış](#1-genel-bakış)
2. [Steam Entegrasyonu](#2-steam-entegrasyonu)
3. [Tron Blockchain (TRC-20) Entegrasyonu](#3-tron-blockchain-trc-20-entegrasyonu)
4. [Email Servisi](#4-email-servisi)
5. [Telegram Bot API](#5-telegram-bot-api)
6. [Discord Bot API](#6-discord-bot-api)
7. [Piyasa Fiyat Verisi](#7-piyasa-fiyat-verisi)
8. [Bağımlılık Risk Matrisi](#8-bağımlılık-risk-matrisi)
9. [Ortam Konfigürasyonu](#9-ortam-konfigürasyonu)

---

## 1. Genel Bakış

Bu doküman, Skinora platformunun üçüncü parti servislerle olan entegrasyonlarını detaylandırır. Her entegrasyon için API detayları, limitleri, hata senaryoları, retry stratejisi ve bağımlılık riski tanımlanır.

**Kapsam:** Sadece dış servis entegrasyonları. İç servis iletişimi (sidecar ↔ backend) 05 §3.4'te tanımlıdır.

### 1.1 Entegrasyon Envanteri

| # | Entegrasyon | Runtime | Kullanım | Kritiklik |
|---|-------------|---------|----------|-----------|
| 1 | Steam OpenID | .NET Backend | Kullanıcı kimlik doğrulama | Kritik — giriş yapılamaz |
| 2 | Steam Web API | Node.js Sidecar | Profil bilgisi, MA kontrolü | Kritik — profil çekilemez |
| 3 | Steam Community (Inventory + Trade Offer) | Node.js Sidecar | Envanter okuma, trade offer gönderme/takip/onay | Kritik — işlem başlatılamaz, item transferi durur |
| 4 | Tron Blockchain (TronWeb) | Node.js Blockchain Service | Adres üretimi, transfer, imzalama | Kritik — ödeme durur |
| 5 | TronGrid API | Node.js Blockchain Service | Blockchain sorgulama, monitoring | Kritik — ödeme doğrulama durur |
| 6 | Email Servisi (Resend) | .NET Backend | Transactional email bildirimleri | Yüksek — bildirim gecikmesi |
| 7 | Telegram Bot API | .NET Backend | Kullanıcı + admin bildirimleri | Orta — alternatif kanallar var |
| 8 | Discord Bot API | .NET Backend | Kullanıcı bildirimleri | Orta — alternatif kanallar var |
| 9 | Steam Market Price API | .NET Backend | Fraud tespiti için fiyat verisi | Orta — cache ile tolere edilir |

### 1.2 Ortak Yaklaşımlar

Tüm entegrasyonlarda uygulanan ortak pattern'ler:

| Pattern | Uygulama |
|---------|----------|
| **Retry** | Exponential backoff — her entegrasyona özel parametreler (bu dokümanda tanımlı) |
| **Circuit breaker** | Ardışık hata eşiği aşıldığında entegrasyon devre dışı, periyodik deneme ile otomatik iyileşme |
| **Timeout** | Her dış çağrı için bağlantı ve okuma timeout'u tanımlı |
| **Logging** | Tüm dış çağrılar `correlationId` ile loglanır (05 §9.1) |
| **Health check** | Her entegrasyon `/health` endpoint'inde kontrol edilir (05 §9.5) |
| **Credential yönetimi** | Tüm secret'lar 05 §3.5'teki stratejiyle yönetilir |

### 1.3 Circuit Breaker Parametreleri

| Parametre | Varsayılan | Açıklama |
|-----------|-----------|----------|
| Failure threshold | 5 ardışık hata | Devreyi açar (trip) |
| Recovery timeout | 30 saniye | Devre açıkken bekleme süresi |
| Half-open probe | 1 istek | Recovery sonrası deneme |
| Success threshold | 2 ardışık başarı | Devreyi kapatır (reset) |

> **Not:** Değerler entegrasyon bazında farklılaştırılabilir. Kritik entegrasyonlar (Steam, Blockchain) için daha agresif recovery (daha kısa timeout) uygulanır.

---

## 2. Steam Entegrasyonu

Steam entegrasyonu üç ayrı bileşenden oluşur: OpenID (kimlik doğrulama), Web API (veri sorgulama) ve Trade Offer (item transferi). İlk ikisi farklı protokollerdir; üçüncüsü `steam-tradeoffer-manager` kütüphanesi üzerinden yönetilir.

### 2.1 Steam OpenID (Kimlik Doğrulama)

**Protokol:** OpenID 2.0 (OAuth değil — erişim token'ı vermez, sadece kimlik doğrular)

**Akış:**

```
1. Frontend → kullanıcıyı Steam login sayfasına yönlendirir
2. Kullanıcı Steam üzerinde onay verir
3. Steam → callback URL'e redirect (claimed_id içinde SteamID64)
4. Backend → Steam'e assertion doğrulama isteği gönderir
5. Doğrulama başarılı → SteamID64 çıkarılır
6. Backend → Sidecar üzerinden Steam Web API'den profil bilgilerini çeker (05 §3.2)
```

**Endpoint'ler:**

| Adım | URL | Method |
|------|-----|--------|
| Login yönlendirme | `https://steamcommunity.com/openid/login` | GET (redirect) |
| Assertion doğrulama | `https://steamcommunity.com/openid/login` | POST (backend → Steam) |

**OpenID Parametreleri (login yönlendirme):**

| Parametre | Değer |
|-----------|-------|
| `openid.ns` | `http://specs.openid.net/auth/2.0` |
| `openid.mode` | `checkid_setup` |
| `openid.return_to` | `https://skinora.com/api/v1/auth/steam/callback` |
| `openid.realm` | `https://skinora.com` |
| `openid.identity` | `http://specs.openid.net/auth/2.0/identifier_select` |
| `openid.claimed_id` | `http://specs.openid.net/auth/2.0/identifier_select` |

**Doğrulama sonrası elde edilen veri:**

| Veri | Kaynak | Örnek |
|------|--------|-------|
| SteamID64 | `claimed_id` URL'inden parse | `76561198012345678` |
| Profil bilgileri | Ayrı Web API çağrısı (§2.2) | Persona name, avatar, profil URL |

**Güvenlik kontrolleri:**

| Kontrol | Açıklama |
|---------|----------|
| Assertion doğrulama | Backend, her login'de Steam'e doğrulama isteği gönderir — client tarafından gelen `claimed_id` güvenilmez |
| Return URL kontrolü | `openid.return_to` sadece kendi domain'imizi kabul eder |
| Replay koruması | Nonce kontrolü — aynı assertion tekrar kullanılamaz |
| HTTPS zorunlu | Tüm OpenID iletişimi HTTPS üzerinden |

### 2.2 Steam Web API

**Base URL:** `https://api.steampowered.com/`

**Kimlik doğrulama:** API Key — tercih edilen yöntem `x-webapi-key` HTTP header'ı (log/proxy'lerde secret sızıntısını önler). Legacy fallback: query parameter `?key={API_KEY}`.

**API Key alma:** `https://steamcommunity.com/dev/apikey` — bir Steam hesabı ile kayıt

**Kullanılan endpoint'ler:**

| Endpoint | Amaç | Kullanım yeri |
|----------|-------|---------------|
| `ISteamUser/GetPlayerSummaries/v2` | Kullanıcı profil bilgileri (isim, avatar, profil URL, hesap görünürlüğü) | Login sonrası profil çekme, profil sayfası |
| `IEconService/GetTradeHoldDurations/v1` | Trade hold süresi — Mobile Authenticator aktifliğini doğrulama | Trade URL kaydı sonrası MA kontrolü |

**GetPlayerSummaries örnek istek:**

```
GET https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?steamids={STEAMID64}
Header: x-webapi-key: {API_KEY}
```

**GetPlayerSummaries response'tan kullanılan field'lar:**

| Field | Kullanım | 06 eşlemesi |
|-------|----------|-------------|
| `steamid` | SteamID64 | User.SteamId |
| `personaname` | Steam görünen ismi | User.SteamDisplayName |
| `avatarfull` | Profil fotoğrafı URL | User.SteamAvatarUrl |

**Mobile Authenticator kontrolü:**

Steam Web API'de doğrudan "Mobile Authenticator aktif mi?" endpoint'i yoktur. Bunun yerine trade hold süresi kontrol edilir:

| Durum | Trade Hold Süresi | Anlam |
|-------|-------------------|-------|
| MA aktif | 0 gün | Kullanıcı Mobile Authenticator kullanıyor |
| MA aktif değil | 15 gün | Kullanıcı escrow'a düşer — platform bunu kabul etmez |

**Yaklaşım:** `GetTradeHoldDurations` çağrısı, kullanıcının **trade URL'ini kaydettiği anda** yapılır (login sonrası değil). Sebep: bu endpoint arkadaş olmayan kullanıcılar için `trade_offer_access_token` parametresi gerektirir — bu token trade URL'den parse edilir. Hold süresi > 0 ise kullanıcıya "Mobile Authenticator aktif etmelisiniz" uyarısı gösterilir (03 §2.1). Sonuç `User.MobileAuthenticatorVerified` field'ına kaydedilir (06 §3.1).

**GetTradeHoldDurations parametreleri:**

| Parametre | Kaynak | Açıklama |
|-----------|--------|----------|
| `key` | Platform API key | Steam Web API key |
| `steamid_target` | Kullanıcının SteamID64 | Kontrol edilecek kullanıcı |
| `trade_offer_access_token` | Trade URL'den parse | Arkadaş olmayan kullanıcılar için zorunlu |

### 2.3 Steam Envanter Okuma

**Endpoint:** `https://steamcommunity.com/inventory/{steamId}/730/2`

| Parametre | Değer | Açıklama |
|-----------|-------|----------|
| `{steamId}` | Kullanıcının SteamID64 | Hedef envanter |
| `730` | CS2 App ID | CS2 oyunu |
| `2` | Context ID | CS2 item context |
| `l` | `english` | Dil (item açıklamaları) |
| `count` | `5000` | Sayfa başına item (max 5000) |

> **Önemli:** Bu endpoint resmi Steam Web API'nin parçası değildir — Steam Community endpoint'idir. Rate limiting daha agresiftir ve belgelenmemiştir. Envanter verisi node.js sidecar üzerinden `steamcommunity` kütüphanesi ile çekilir.

**Pagination (5000+ item envanterler için):**

| Parametre | Açıklama |
|-----------|----------|
| `start_assetid` | Bir önceki response'taki `last_assetid` değeri — sonraki sayfaya geçiş |
| `more_items` | Response'ta `1` ise daha fazla item var — `last_assetid` ile devam et |
| `last_assetid` | Sonraki sayfa isteğinde `start_assetid` olarak kullanılacak değer |

> **Not:** Sidecar, `more_items=1` olduğu sürece pagination döngüsünü sürdürür. Tüm sayfalar birleştirildikten sonra cache'e yazılır.

**Response modeli (assets + descriptions merge):**

Steam envanter response'u iki ayrı koleksiyon içerir:
- `assets[]` — her item'ın `assetid`, `classid`, `instanceid`, `amount` bilgilerini taşır
- `descriptions[]` — her `classid + instanceid` çifti için item metadata'sını (isim, ikon, tradable vb.) taşır

Item bilgileri, `assets[].classid + instanceid` ile `descriptions[].classid + instanceid` eşleştirilerek (join) elde edilir. Aynı classid+instanceid birden fazla asset'e karşılık gelebilir (örn: aynı skin'den birden fazla).

**Merge sonrası kullanılan veri:**

| Veri | Kaynak koleksiyon | 06 eşlemesi |
|------|-------------------|-------------|
| `assetid` | `assets[]` | Transaction.ItemAssetId |
| `classid` + `instanceid` | `assets[]` (merge key) | Transaction.ItemClassId, Transaction.ItemInstanceId |
| `market_hash_name` | `descriptions[]` | Transaction.ItemName |
| `icon_url` | `descriptions[]` | Transaction.ItemIconUrl |
| `tradable` | `descriptions[]` (0/1) | İşlem başlatma kontrolü (03 §2.2 adım 8) |

**Envanter cache stratejisi:**

| Konu | Karar |
|------|-------|
| Cache süresi | 2 dakika — envanter sık değişebilir |
| Cache yeri | Redis (05 §2.5) |
| Invalidation | İşlem başlatma ve trade offer sonrası cache temizlenir |
| Herkese açık envanter | Kullanıcının Steam profili public değilse envanter okunamaz — uyarı gösterilir |

### 2.4 Trade Offer Yönetimi

Trade offer yaşam döngüsü tamamen Node.js sidecar'da `steam-tradeoffer-manager` kütüphanesi ile yönetilir.

**Trade offer gönderme akışı:**

```
1. .NET backend → sidecar'a HTTP isteği (item detayları, hedef Steam ID)
2. Sidecar bot seçimi yapar (capacity-based — 05 §3.2)
3. Seçilen bot üzerinden TradeOffer nesnesi oluşturulur
4. addMyItems() veya addTheirItems() ile item eklenir
5. send() ile offer gönderilir
6. steam-totp ile mobile confirmation yapılır
7. Sidecar → .NET backend'e webhook callback
```

**Trade offer durumları (Steam tarafı):**

| Durum | Kod | Skinora karşılığı |
|-------|-----|-------------------|
| Active | 2 | Offer gönderildi, yanıt bekleniyor |
| Accepted | 3 | Kabul edildi → state geçişi tetiklenir |
| Countered | 4 | Karşı taraf counter offer yaptı — Skinora counter offer desteklemez. Orijinal offer iptal sayılır, kullanıcıya "Counter offer desteklenmiyor, işlem iptal edildi" bildirimi gönderilir, işlem iptal akışına yönlendirilir (03 §2.3/5, §3.5/5). |
| Declined | 7 | Reddedildi → iptal akışı (03 §2.3/5, §3.5/5) |
| Expired | 5 | Süresi doldu → timeout akışı |
| Canceled | 6 | Platform tarafından iptal edildi |
| InvalidItems | 8 | Item artık mevcut değil → hata akışı |
| CreatedNeedsConfirmation | 9 | Mobile confirmation bekleniyor |

**Polling stratejisi:**

| Konu | Değer |
|------|-------|
| Poll aralığı | 10 saniye |
| Mekanizma | `steam-tradeoffer-manager` built-in polling |
| Yedek mekanizma | Steam WebSocket event'leri (destekleniyorsa) |

**Mobile confirmation:**

| Konu | Karar |
|------|-------|
| Kütüphane | `steam-totp` |
| Yaklaşım | Offer gönderildikten sonra otomatik confirmation |
| Gerekli secret'lar | `identity_secret` (bot hesabının) |
| Zamanlama | Offer gönderiminden hemen sonra, otomatik |

### 2.5 Kütüphaneler ve Versiyonlama

| Kütüphane | Amaç | Minimum Versiyon |
|-----------|-------|-----------------|
| `steam-tradeoffer-manager` | Trade offer CRUD, polling, kabul/red | ^2.13.x (**Not:** npm'de 3.x yok, 2026-04-09 itibarıyla en güncel 2.13.0) |
| `steamcommunity` | Session yönetimi, login, envanter okuma, confirmation | ^3.x |
| `steam-totp` | 2FA kod üretimi, mobile confirmation | ^2.x |
| `steam-user` | Steam client bağlantısı (opsiyonel — sidecar'da gerekiyorsa) | ^5.x |

**Versiyon sabitleme politikası:**

| Kural | Gerekçe |
|-------|---------|
| `package-lock.json` commit edilir | Deterministic build |
| Major versiyon yükseltmeleri manuel | Breaking change riski — Steam kütüphaneleri community-maintained |
| Güvenlik yaması | Otomatik (dependabot veya benzeri) |

### 2.6 API Limitleri ve Rate Limiting

Steam resmi rate limit belgeleri yayınlamaz. Aşağıdaki değerler topluluk deneyimi ve pratik gözlemlere dayanır:

| Kaynak | Tahmini Limit | Platform Stratejisi |
|--------|--------------|-------------------|
| Steam Web API | ~100.000 istek/gün (API key başına) | Yeterli — günlük kullanıcı sayısı bu limiti zorlamaz |
| Steam Web API (burst) | ~1 istek/saniye önerilen | İstekler arası minimum 1 saniye bekleme |
| Envanter endpoint | ~10-20 istek/dakika (IP başına) | Cache + rate limiter |
| Trade offer gönderme | ~5/dakika (hesap başına) | Birden fazla bot ile dağıtım |
| Login denemesi | ~5/dakika | Session yönetimi ile minimize |

**Platform tarafı korumalar:**

| Koruma | Uygulama |
|--------|----------|
| Request queue | Sidecar'da tüm Steam istekleri kuyruğa alınır, rate limit'e uygun şekilde gönderilir |
| Bot dağıtımı | Birden fazla bot hesabı ile trade offer limiti dağıtılır (05 §3.2) |
| Envanter cache | 2 dakikalık cache ile gereksiz envanter çağrısı önlenir |
| Exponential backoff | 429/rate limit hatalarında artan bekleme |

### 2.7 Hata Senaryoları ve Retry

| Hata | HTTP Kodu | Aksiyon | Retry |
|------|----------|---------|-------|
| Rate limit aşıldı | 429 | Bekleme süresi uygula | Evet — exponential backoff (5s, 15s, 45s) |
| Steam API geçici hatası | 500, 502 | Log + retry | Evet — 3 deneme (5s, 15s, 45s) |
| Steam 503 | 503 | Aşağıdaki 503 karar ağacına göre işlenir | Karar ağacına bağlı |
| API key geçersiz | 403 | Admin alert | Hayır — manuel müdahale |

**503 karar ağacı:**

```
1. 503 alındı → retry (3 deneme, 5s/15s/45s)
2. Retry'lar başarısız → Steam health check endpoint'i kontrol et
   2a. Health check da başarısız → Steam bakımda kabul et → aktif işlemlerde timeout dondurma tetikle (03 §11.2), 60 saniye aralıkla health check tekrarla
   2b. Health check başarılı → izole geçici hata, log + admin alert
```
| Bot session expired | — | Otomatik re-login | Evet — sidecar otomatik yönetir |
| Trade offer — item artık tradeable değil | InvalidItems (8) | Kullanıcıya bilgi, işlem iptal | Hayır |
| Trade offer — karşı taraf bulunamadı | Hata | Log + admin alert | Hayır |
| Trade offer gönderim hatası | — | Exponential backoff ile retry (05 §3.2) | Evet — timeout süresi içinde |
| Envanter private | — | Kullanıcıya "Envanterinizi public yapın" uyarısı | Hayır — kullanıcı aksiyonu gerekli |

**Session yönetimi retry:**

| Durum | Aksiyon |
|-------|---------|
| Session expire | `steamcommunity` otomatik cookie yenileme |
| Cookie yenileme başarısız | Tam re-login (username + password + 2FA) |
| Re-login başarısız | Bot havuzdan çıkarılır, admin alert |
| Tüm botlar down | Yeni trade offer gönderilmez, admin critical alert (05 §3.2) |

### 2.8 Bağımlılık Riski — "Steam Çökerse Ne Olur?"

| Senaryo | Etki | Mitigasyon |
|---------|------|-----------|
| **Steam servislerinin tamamı down** | Tüm Steam bileşenleri (OpenID, Web API, Community/Trade) etkilenir: yeni giriş yapılamaz, profil/MA kontrolü yapılamaz, envanter okunamaz, trade offer gönderilemez. Bileşen bazlı etkileri §8 risk matrisinde ayrı tanımlanmıştır. | Aktif session'lar çalışmaya devam eder (JWT). Aktif işlemlerin Steam bağımlı adımlarında timeout dondurulur (03 §11.2). Ödeme kabul ve doğrulama etkilenmez (blockchain bağımsız). |
| **Steam planlanmış bakım** | Geçici kesinti (genelde 15-30 dakika, Salı günleri) | Health check ile otomatik tespit → timeout dondurma → normale dönünce devam |
| **Valve trade politikası değişikliği** | Trade offer API davranışı değişebilir | Sidecar izolasyonu sayesinde sadece sidecar güncellenir, ana uygulama etkilenmez |
| **Bot hesabı ban/kısıtlama** | İlgili bot üzerinden işlem yapılamaz | Birden fazla bot ile risk dağıtımı (05 §3.2). Kısıtlı bot havuzdan çıkar, diğerleri devam eder. |
| **steam-tradeoffer-manager kütüphanesi güncellenmezse** | Yeni Steam değişikliklerine uyumsuzluk | Community-maintained — fork hazırlığı, alternatif kütüphane değerlendirmesi |

---

## 3. Tron Blockchain (TRC-20) Entegrasyonu

Tüm blockchain işlemleri (adres üretimi, ödeme izleme, transfer, iade) Node.js Blockchain Service'te yönetilir. Tron ağı üzerinde USDT ve USDC (TRC-20) token'ları kullanılır.

### 3.1 TronWeb ve TronGrid API

**TronWeb:**

| Özellik | Detay |
|---------|-------|
| Kütüphane | `tronweb` (Tron Foundation resmi SDK) |
| Versiyon | ^5.x |
| Sorumluluk | Transaction oluşturma, imzalama, broadcasting, smart contract etkileşimi |
| Runtime | Node.js (Blockchain Service container) |

**TronGrid API:**

| Özellik | Detay |
|---------|-------|
| Sağlayıcı | Tron Foundation (resmi hosted API node) |
| Mainnet URL | `https://api.trongrid.io` |
| Testnet URL (Shasta) | `https://api.shasta.trongrid.io` |
| Testnet URL (Nile) | `https://nile.trongrid.io` |
| Kimlik doğrulama | API Key (header: `TRON-PRO-API-KEY`) |
| API Key alma | `https://www.trongrid.io/` — ücretsiz kayıt |

**Kullanılan TronGrid endpoint'leri:**

| Endpoint | Amaç | Kullanım sıklığı |
|----------|-------|-------------------|
| `POST /wallet/broadcasttransaction` | İmzalanmış transaction yayınlama | Her transfer işleminde |
| `GET /v1/accounts/{address}/transactions/trc20` | Adrese gelen TRC-20 transferlerini sorgulama | Ödeme monitoring (3 saniye aralıkla — 05 §3.3) |
| `POST /wallet/triggersmartcontract` | TRC-20 token transfer fonksiyonu çağrısı | Satıcıya ödeme, iade |
| `POST /wallet/triggerconstantcontract` | TRC-20 bakiye sorgulama (read-only) | Doğrulama |
| `POST /walletsolidity/gettransactioninfobyid` | Confirmed/solidified transaction detayı sorgulama (tx block numarası dahil) | Onay sayısı kontrolü — yalnızca solidified veri döner, mempool dahil edilmez |
| `GET /walletsolidity/getnowblock` | Mevcut solidified block yüksekliğini sorgulama | Finality hesabı: `currentSolidBlock - txBlock >= 20` ise PAYMENT_RECEIVED |
| `POST /wallet/createtransaction` | TRX transfer transaction oluşturma | Gas (Energy) yönetimi |
| `POST /wallet/delegateresource` | Deposit adresine geçici Energy delegation (sweep/refund öncesi) | Sweep ve doğrudan refund/payout akışlarında (05 §3.3) |
| `POST /wallet/undelegateresource` | Delegation geri alımı (sweep/refund sonrası) | Delegation kaynağını serbest bırakma |

### 3.2 HD Wallet (BIP-44) Yönetimi

Her işlem için benzersiz bir ödeme adresi üretilir. Tüm adresler tek bir master seed'den türetilir.

**Derivation path:** `m/44'/195'/0'/0/{index}`

| Segment | Değer | Açıklama |
|---------|-------|----------|
| `44'` | BIP-44 standardı | HD Wallet |
| `195'` | Tron coin type | SLIP-44 kayıtlı |
| `0'` | Account | Tek hesap (MVP) |
| `0` | Change | External (alım adresleri) |
| `{index}` | İşlem sıra numarası | Her işlem için artırılır |

**Adres üretim akışı:**

```
1. .NET backend → Blockchain Service'e "adres üret" isteği (Transaction ID ile)
2. Blockchain Service → DB'den son kullanılan index'i okur
3. Index + 1 ile yeni adres türetir (master seed + derivation path)
4. Adresi ve index'i DB'ye kaydeder
5. .NET backend'e adresi döner
```

> **Atomiklik garantisi:** `PaymentAddress.HdWalletIndex` üzerinde UNIQUE constraint tanımlıdır (06 §3.7). Monoton artan allocator kullanılır, arşivlenen index'ler asla reuse edilmez (05 §3.3). Eşzamanlı isteklerde constraint violation oluşursa → retry ile yeni index alınır. Bu mekanizma duplicate adres üretimini imkansız kılar.

**Güvenlik:**

| Konu | Uygulama |
|------|----------|
| Master seed saklama | Production: Docker Secrets veya vault. Development: environment variable (05 §3.5) |
| Private key kullanımı | Sadece imzalama anında memory'ye yüklenir, işlem sonrası temizlenir |
| Index yönetimi | DB'de saklanır (06 §3.7: PaymentAddress.HdWalletIndex), gap oluşmaması için sequential |
| Backup | Master seed'in güvenli yedeği — kaybedilirse tüm adresler ve fonlar erişilemez |

### 3.3 USDT ve USDC Token Kontrat Detayları

| Token | Ağ | Kontrat Adresi | Ondalık |
|-------|-----|---------------|---------|
| USDT | Tron Mainnet (TRC-20) | `TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t` | 6 |
| USDC | Tron Mainnet (TRC-20) | `TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8` | 6 |
| USDT | Nile Testnet | Testnet faucet'ten alınan test token kontratı | 6 |
| USDC | Nile Testnet | Testnet faucet'ten alınan test token kontratı | 6 |

> **Testnet standardı:** Development ve staging ortamlarında **Nile Testnet** kullanılır (§9.1 ile tutarlı). Shasta yalnızca izole birim testleri için opsiyoneldir. Her iki token (USDT/USDC) için test kontratları Nile faucet'ten alınır.

**TRC-20 transfer fonksiyonu:**

```
function transfer(address to, uint256 value) returns (bool)
```

| Parametre | Açıklama |
|-----------|----------|
| `to` | Hedef adres |
| `value` | Tutar (6 ondalık ile çarpılmış — 100 USDT = 100000000) |

**Energy ve Bandwidth gereksinimleri:**

| İşlem | Tahmini Energy | Tahmini TRX maliyeti (Energy yoksa) |
|-------|---------------|--------------------------------------|
| TRC-20 transfer | ~65.000 Energy | ~13-15 TRX |
| TRX transfer | Energy gerekmez | ~0.1 TRX (sadece bandwidth) |

**Hot wallet TRX bakiyesi:**

| Konu | Karar |
|------|-------|
| Zorunluluk | Hot wallet'ta her zaman yeterli TRX bulunmalı — Energy/Bandwidth için |
| Minimum TRX | Admin tarafından ayarlanabilir eşik |
| Eşik altı | Admin alert — TRX eklenmeli |
| Monitoring | Blockchain Service hot wallet TRX bakiyesini periyodik kontrol eder |

**Hot wallet token (USDT/USDC) bakiye limiti:**

| Konu | Karar |
|------|-------|
| Limit | Admin tarafından belirlenir (06 §3.17 SystemSetting: `hot_wallet_limit`) |
| Amaç | Operasyonel miktarda token tutulur — güvenlik riski azaltılır |
| Eşik aşıldığında | Admin alert — cold wallet'a manuel transfer gerekir (MVP) |
| Cold wallet transfer | MVP'de admin tarafından manuel başlatılır, ileride otomasyon eklenebilir (05 §3.3) |

**Deposit adresi resource modeli (sweep/refund):**

Deposit adreslerinden sweep veya doğrudan refund/payout yapabilmek için Energy/Bandwidth stratejisi:

| Konu | Karar | Kaynak |
|------|-------|--------|
| Sorun | Deposit adreslerinde yalnızca TRC-20 token var — TRX/Energy yoksa giden transfer başarısız | 05 §3.3 |
| Çözüm | Merkezi sweeper account + energy delegation | 05 §3.3 |
| Delegation akışı | Sweep öncesinde sweeper account'tan deposit adresine geçici energy delegation → sweep → delegation geri alınır | 05 §3.3 |
| Fallback | Delegation başarısızsa → deposit adresine minimum TRX transfer edilerek gas fee karşılanır | 05 §3.3 |
| Batch optimizasyon | Aynı blokta birden fazla deposit adresi için toplu delegation | 05 §3.3 |

### 3.4 Monitoring ve Polling Stratejisi

**Aktif ödeme izleme (işlem devam ederken):**

| Parametre | Değer | Kaynak |
|-----------|-------|--------|
| Polling aralığı | 3 saniye | 05 §3.3 |
| Minimum onay | 20 blok (~60 saniye) | 05 §3.3 |
| İzlenen token | İşlemde belirlenen (USDT veya USDC) | 02 §4.3 |
| İzleme yöntemi | TronGrid `trc20` transaction endpoint'i + aşağıdaki filtre/idempotency kuralları | |

**İzleme iki aşamalıdır:** Beklenen token sorgusu (birincil) ve yanlış token taraması (ikincil) ayrı çağrılardır.

**Aşama 1 — Beklenen token sorgusu (birincil):**

| Parametre | Değer | Amaç |
|-----------|-------|-------|
| `contract_address` | Beklenen token kontrat adresi (§3.3) | Yalnızca beklenen token transferlerini filtrele |
| `only_confirmed` | `true` | Yalnızca confirmed transaction'ları döndür |
| `limit` | `20` | Sayfa başına sonuç (MVP trafiğinde yeterli) |
| `fingerprint` | Önceki response'tan alınan cursor | Deterministic pagination — aynı veriyi tekrar taramayı önler |

**Aşama 2 — Yanlış token taraması (ikincil):**

| Parametre | Değer | Amaç |
|-----------|-------|-------|
| `contract_address` | **Belirtilmez** (filtresiz) | Tüm TRC-20 transferlerini döndür |
| `only_confirmed` | `true` | Yalnızca confirmed |
| `limit` | `20` | Sayfa başına sonuç |
| `fingerprint` | İkincil taramaya özel ayrı cursor (birincilden bağımsız) | Deterministic pagination — spam senaryosunda 20+ transfer kaçırılmaz |

Filtresiz sonuçtan, beklenen token kontratı dışında kalan transferler aşağıdaki kurala göre işlenir:

**Wrong-token işleme kuralı (spam/griefing koruması):**

| Koşul | Aksiyon |
|-------|---------|
| Token, desteklenen allowlist'te (USDT/USDC kontratları) ve beklenen tokenden farklı | `WRONG_TOKEN_INCOMING` → otomatik iade denemesi (§3.4 tutar doğrulama tablosu) |
| Token, desteklenen allowlist'te değil (bilinmeyen/spam token) | `SPAM_TOKEN_INCOMING` → ignore + log. Otomatik iade yapılmaz — TRX/Energy israfı önlenir. Admin dashboard'da görünür, manuel değerlendirme opsiyonel. |
| Transfer tutarı < minimum iade eşiği (2× gas fee) | Desteklenen token olsa bile iade yapılmaz — log + admin alert |

> **Gerekçe:** Deposit adresleri public olduğundan üçüncü taraflar spam/dust token gönderebilir. Tüm bilinmeyen token'ları otomatik refund'a sokmak TRX/Energy tüketir ve operasyonel DoS yüzeyi oluşturur. Yalnızca desteklenen token'lar (USDT/USDC) otomatik iade akışına girer.

> **Not:** İki ayrı çağrı yerine tek filtresiz çağrı + uygulama tarafı ayrıştırma da teknik olarak mümkündür. Ancak ayrı çağrılar tercih edilir: birincil sorgu `contract_address` filtresiyle daha az veri döner ve daha hızlıdır; wrong-token taraması yalnızca ek güvenlik katmanıdır.

**Kayıt türü ön filtresi:** TronGrid `trc20` endpoint'i yalnızca TRC-20 transfer kayıtları değil, TRC-721 transfer ve authorization kayıtları da döndürebilir. Her iki aşamada da response'tan gelen kayıtlar eşleştirme mantığına girmeden önce `type` alanına göre filtrelenir: yalnızca `Transfer` türündeki kayıtlar işlenir. Authorization, Approval ve TRC-721 kayıtları skip edilir (log seviyesi: debug).

**İdempotent işleme kuralı:** Her iki aşamada da eşleşen transfer, `txid` + `event_index` (aynı transaction'da birden fazla TRC-20 event olabilir) bileşik anahtarıyla işlenir. Bu anahtar `BlockchainTransaction` tablosunda unique olarak saklanır (06 §3.8). Daha önce işlenmiş txid+event_index tekrar görülürse skip edilir. Final kabul formülü: `walletsolidity/gettransactioninfobyid` ile tx'in `blockNumber`'ı alınır, `walletsolidity/getnowblock` ile mevcut solid block yüksekliği alınır. `currentSolidBlock - txBlock >= 20` ise transfer kesinleşmiş sayılır → PAYMENT_RECEIVED state geçişi tetiklenir.

**Gecikmeli ödeme izleme (iptal sonrası) — 05 §3.3 ile tutarlı:**

| Süre | Polling aralığı |
|------|----------------|
| İlk 24 saat | 30 saniye |
| 1-7 gün | 5 dakika |
| 7-30 gün | 1 saat |
| 30 gün sonra | İzleme durdurulur, admin alert |

**Tutar doğrulama (05 §3.3 ile tutarlı):**

| Senaryo | Aksiyon |
|---------|---------|
| Doğru tutar | Kabul → PAYMENT_RECEIVED state geçişi |
| Eksik tutar | Red → iade (gas fee düşülerek), timeout devam |
| Fazla tutar | Doğru tutar kabul + fazla iade (gas fee düşülerek) |
| Yanlış token (desteklenen) | Red → iade denemesi (USDT/USDC allowlist'te), başarısızsa admin alert |
| Spam/bilinmeyen token | Ignore + log → otomatik iade yapılmaz, admin dashboard'da görünür |

> **Not:** Yanlış token tespiti: izlenen adreste beklenen token dışında TRC-20 transfer tespit edildiğinde `BlockchainTransaction` kaydı `WRONG_TOKEN_INCOMING` type ile oluşturulur. `ActualTokenAddress` field'ında yanlış token'ın contract adresi saklanır. Otomatik iade denemesi `WRONG_TOKEN_REFUND` kaydı ile takip edilir (06 §3.8).

| Minimum iade eşiği | İade tutarı < 2× gas fee ise iade yapılmaz — gas fee iadenin büyük kısmını yutar. Admin alert gönderilir, manuel değerlendirme (05 §3.3) |

> **Refund politikası:** İade (underpayment, overpayment farkı, wrong token) her zaman **gönderim yapan kaynak adrese** (source address) gönderilir — blockchain transaction'ından parse edilir, kullanıcı ayrıca refund adresi belirtmez. Bu, standart blockchain iade pratiğidir.
>
> **Exchange riski:** Alıcı ödemeyi bir exchange'in hot wallet'ından gönderdiyse, iade o exchange adresine gider — kullanıcının exchange hesabına otomatik ulaşma garantisi yoktur. Bu risk UI katmanında "Exchange'den gönderim yapmayın" uyarısı ile azaltılır (02 §12.2). Platform tarafında teknik önlem yoktur.

### 3.5 Hata Senaryoları ve Retry

| Hata | Aksiyon | Retry |
|------|---------|-------|
| TronGrid rate limit / key suspension (429) | İkinci TronGrid API key'e geç (§3.6), backoff uygula | Evet — exponential (5s, 15s, 45s) |
| TronGrid provider-wide outage (tüm key'ler başarısız) | MVP: bekleme + admin alert. Büyüme: alternatif sağlayıcıya geç (§3.6) | Evet — 3 deneme (3s, 10s, 30s), sonra bekleme |
| Transaction broadcast başarısız | Log + retry | Evet — 3 deneme (5s, 15s, 45s) |
| Yetersiz Energy/TRX | Admin alert — hot wallet'a TRX eklenmeli | Hayır — manuel müdahale sonrası retry |
| Transaction onaylanmadı (timeout) | Log + admin alert | Evet — belirli süre sonra tekrar kontrol |
| İade transferi başarısız | Exponential backoff (05 §3.3) | Evet — 3 deneme (1dk, 5dk, 15dk) |
| Satıcıya ödeme başarısız | Exponential backoff (05 §3.3) | Evet — 3 deneme (1dk, 5dk, 15dk), sonra admin alert |
| Kontrat çağrısı revert | Log + hata analizi | Duruma göre — genelde hayır |

### 3.6 Bağımlılık Riski — "Blockchain Erişilemezse Ne Olur?"

| Senaryo | Etki | Mitigasyon |
|---------|------|-----------|
| **TronGrid rate limit / key suspension** | İstekler reddedilir | İkinci TronGrid API key ile ayrı rate limit havuzu (MVP). Büyümede Ankr/GetBlock. |
| **TronGrid provider-wide outage** | Ödeme izleme durur, transfer yapılamaz | MVP: bekleme + admin alert (alternatif provider yok). Büyüme: Ankr/GetBlock veya self-hosted Tron full node. |
| **Tron ağı congestion** | Transaction'lar gecikmeli onaylanır, Energy maliyeti artar | Onay bekleme süresini dinamik uzat. Energy artışı admin alert tetikler. |
| **USDT/USDC kontratı dondurulursa** | O token ile işlem yapılamaz | Diğer token aktif kalır (USDT donmuşsa USDC ile devam). Yeni işlem başlatma o token için devre dışı, aktif işlemler admin alert. |
| **Hot wallet compromise** | Fonlara yetkisiz erişim | Hot wallet limit mekanizması (05 §3.3) — operasyonel miktarda fon. Anomali tespitinde tüm giden transferler durdurulur. |
| **Master seed kaybı** | Tüm türetilmiş adreslere erişim kaybı | Güvenli backup zorunlu. Recovery prosedürü dokümante edilmeli. |

**Yedek node stratejisi (MVP sonrası):**

| Senaryo | MVP Fallback | Büyüme Fallback |
|---------|-------------|-----------------|
| **Rate limit / key suspension** | İkinci TronGrid API key (ayrı rate limit havuzu) | Ankr/GetBlock (ücretli, daha yüksek limit) |
| **Provider-wide outage** | Bekleme + admin alert — MVP'de alternatif provider yok | Ankr/GetBlock (farklı sağlayıcı) veya self-hosted Tron full node |

| Aşama | Strateji |
|-------|----------|
| MVP | TronGrid (primary) + ikinci TronGrid API key (rate limit fallback). Provider outage'de bekleme. |
| Büyüme | TronGrid + Ankr/GetBlock (hem rate limit hem outage fallback) |
| Ölçek | Self-hosted Tron full node (tam bağımsızlık) |

---

## 4. Email Servisi

### 4.1 Provider Seçimi

| Kriter | Resend | SendGrid |
|--------|--------|----------|
| API tasarımı | Modern, minimalist REST | Kapsamlı, kurumsal |
| .NET SDK | Community paketi (`Resend.net`) — basit HTTP wrapper da yeterli | Resmi Twilio SendGrid SDK |
| Ücretsiz plan | 3.000 email/ay, 100/gün | 100/gün (~3.000/ay) |
| Ücretli başlangıç | ~$20/ay (50K email) | ~$20/ay (50K email) |
| Deliverability | İyi — DKIM, SPF, DMARC desteği | Çok iyi — endüstri standardı |
| Olgunluk | 2023'te kurulan — yeni ama hızla büyüyen | 2009'dan beri — battle-tested |
| Template desteği | API üzerinden HTML gönderim | Gelişmiş template engine |

**Karar: Resend**

**Gerekçe:**
- MVP için yeterli ücretsiz plan
- Basit API — daha az kod, daha hızlı entegrasyon
- Skinora zaten .NET resource dosyaları (.resx) ile template yönetecek (05 §7.3) — provider'ın template engine'ine ihtiyaç yok
- Deliverability yeterli
- Geçiş maliyeti düşük — abstraction layer ile provider değişikliği birkaç saatlik iş

### 4.2 API Detayları

**Base URL:** `https://api.resend.com`

**Kimlik doğrulama:** API Key (header: `Authorization: Bearer {API_KEY}`)

**Kullanılan endpoint:**

| Endpoint | Method | Amaç |
|----------|--------|-------|
| `/emails` | POST | Email gönderimi |

**Örnek istek:**

```json
{
  "from": "Skinora <noreply@skinora.com>",
  "to": ["user@example.com"],
  "subject": "İşleminiz tamamlandı — Skinora",
  "html": "<h1>İşleminiz başarıyla tamamlandı...</h1>"
}
```

**DNS ayarları (zorunlu):**

| Kayıt | Tür | Amaç |
|-------|-----|-------|
| DKIM | TXT/CNAME | Email imzalama — spam filtrelerinden geçiş |
| SPF | TXT | Yetkili gönderici sunucu doğrulama |
| DMARC | TXT | DKIM + SPF politikası |
| Return-Path | MX/CNAME | Bounce yönetimi |

**Email türleri (tümü transactional):**

| Tür | Örnek | Şablon |
|-----|-------|--------|
| İşlem bildirimleri | "Item emanete alındı", "Ödeme doğrulandı" | .resx şablon + placeholder'lar |
| Güvenlik | "Cüzdan adresi değiştirildi", "Yeni cihazdan giriş" | .resx şablon |
| Hesap | "Hoş geldiniz", "Hesabınız silindi" | .resx şablon |
| Timeout uyarıları | "Ödeme süreniz dolmak üzere" | .resx şablon |

### 4.3 Hata Senaryoları ve Retry

| Hata | Aksiyon | Retry |
|------|---------|-------|
| API erişilemez (5xx) | Log + retry | Evet — 3 deneme (1dk, 5dk, 15dk) (05 §7.5) |
| Rate limit (429) | Backoff | Evet — exponential |
| Geçersiz email adresi (422) | Log, kullanıcıya bilgi | Hayır |
| API key geçersiz (401) | Admin alert | Hayır |
| Webhook olayları (async) | Aşağıdaki olay matrisine göre işlenir | Platform outbound retry yok; inbound redelivery Resend/Svix tarafından yapılır |
| Geçici hata — immediate retry'lar tükendi | Outbox kaydı `DEFERRED` state'e alınır — arka plan job'ı ile artan aralıklarla (30dk, 1sa, 4sa) yeniden denenir. Platform içi bildirim zaten gönderilmiş — email tek kanal değil. | Evet — arka plan job ile |
| Kalıcı hata (422, email.failed, email.suppressed) | Outbox kaydı `FAILED` olarak kapatılır — retry yapılmaz. Log + admin alert. | Hayır |

**Resend Webhook Olay Matrisi:**

| Event | Aksiyon |
|-------|---------|
| `email.bounced` | Email adresini invalid/suppressed olarak işaretle. Kullanıcıya platform-içi bildirim: "Email adresinize ulaşılamıyor, lütfen güncelleyin." |
| `email.delivery_delayed` | Adresi kapatma — geçici sorun. Log + monitoring seviyesi artır. Tekrarlayan delay'lerde admin alert. |
| `email.complained` | Spam şikayeti — email kanalını devre dışı bırak (kullanıcı tekrar aktif edebilir). Log + admin dashboard'da görünür. |
| `email.failed` | Gönderim kalıcı olarak başarısız (Resend tarafı). Log + admin alert. Aynı mesaj için retry yapılmaz — outbox kaydı `FAILED` olarak kapatılır. |
| `email.suppressed` | Adres Resend suppression listesinde — email kanalını devre dışı bırak, gönderim durdur. Kullanıcıya platform-içi bildirim: "Email adresiniz kara listede, lütfen farklı bir adres girin." |

**Bounce Webhook Güvenliği:**

| Konu | Karar |
|------|-------|
| Endpoint | `POST /webhooks/resend` (07 webhook endpoint'leri ile tutarlı) |
| İmza doğrulama | Resend, Svix altyapısı kullanır — `svix-id`, `svix-timestamp`, `svix-signature` header'ları doğrulanmalı |
| Raw body | İmza doğrulama için request body raw (unparsed) olarak okunmalı — middleware JSON parse'dan önce raw body'yi yakalamalı |
| Idempotency | `svix-id` header'ı event-id olarak kullanılır — aynı event-id ile gelen duplicate webhook ignore edilir (DB'de processed event tablosu) |
| Replay koruması | `svix-timestamp` ile 5 dakikadan eski event'ler reddedilir |
| Başarısız doğrulama | 401 dön, event işlenmez |
| Persistence başarısız | 2xx dönülmez (5xx dön) — Svix redelivery tetiklenir. Event-id idempotency ile duplicate koruması sağlanır. Transient DB hatalarında event kaybolmaz. |
| Signing secret | Resend dashboard'dan alınan webhook signing secret (§9.2'de credential envanterine eklenmiştir) |

### 4.4 Bağımlılık Riski

| Senaryo | Etki | Mitigasyon |
|---------|------|-----------|
| **Resend API down** | Email bildirimleri gitmez | Platform içi bildirimler her zaman aktif (05 §7.4). Telegram/Discord kanalları yedek. Outbox pattern ile email'ler kuyrukta bekler, servis dönünce gönderilir. |
| **Resend kapanırsa** | Provider değişikliği gerekir | Abstraction layer (IEmailSender interface) ile SendGrid'e geçiş birkaç saatte yapılabilir |
| **Domain kara listeye alınırsa** | Email'ler spam'e düşer | DKIM/SPF/DMARC yapılandırması + dedicated IP (ücretli planda) |

---

## 5. Telegram Bot API

### 5.1 Bot Kurulumu ve Bağlantı

**Bot oluşturma:** Telegram üzerinde `@BotFather` ile:
1. `/newbot` komutu → bot adı ve username belirlenir
2. Bot token alınır (format: `123456789:ABCdefGhIJKlmNoPQRsTUVwxYZ`)

**Kullanıcı-bot bağlantı akışı (Deep Link):**

```
1. Kullanıcı Skinora'da profil ayarlarından "Telegram Bildirimlerini Aç" tıklar
2. Skinora benzersiz bir bağlantı kodu üretir (örn: "abc123xyz")
3. Kullanıcı deep link'e yönlendirilir: https://t.me/{bot_username}?start=abc123xyz
4. Telegram açılır, kullanıcı /start butonuna basar
5. Bot, start parametresindeki kodu Skinora backend'e doğrulatır
6. Backend kodu User ile eşleştirir → chat_id kaydedilir (06: UserNotificationPreference)
7. Kullanıcıya "Bağlantı başarılı" mesajı gider (hem Telegram hem platform)
```

**Bağlantı kodu yaşam döngüsü:**

| Konu | Karar |
|------|-------|
| TTL | 10 dakika — süre aşımında kod otomatik geçersizleşir |
| Kullanım | Single-use — başarılı eşleşmeden sonra anında invalidate edilir |
| Session binding | Kod üretilirken aktif kullanıcı session'ına bağlanır — farklı session'dan kullanılamaz |
| Brute-force koruması | Aynı kullanıcı için 5 başarısız doğrulama denemesinden sonra kod geçersizleşir, yeni kod üretilmesi gerekir |
| Entropy | En az 122 bit efektif rastgelelik — UUIDv4 (122 bit random + 6 bit version/variant) veya 128+ bit CSPRNG tabanlı opaque token |

**Bot token yönetimi:**
- Production: Docker Secrets / vault (05 §3.5)
- Development: `.env` dosyası
- Token değişikliği: BotFather'da `/revoke` → yeni token → redeploy

### 5.2 API Detayları

**Base URL:** `https://api.telegram.org/bot{token}/`

**Kullanılan method'lar:**

| Method | Amaç |
|--------|-------|
| `sendMessage` | Bildirim gönderimi |
| `setWebhook` | Webhook URL ve `secret_token` ayarı (kurulum sırasında bir kez) |

**Webhook vs Polling:**

| Yaklaşım | Karar |
|----------|-------|
| MVP | **Webhook** — Telegram yeni mesajları Skinora endpoint'ine push eder |
| Webhook URL | `https://skinora.com/api/v1/webhooks/telegram` |
| Neden webhook? | Polling gereksiz yük oluşturur, webhook anlık teslimat sağlar |
| Endpoint | `POST /webhooks/telegram` (07 §5.11b W1) |
| Güvenlik | `setWebhook` çağrısında `secret_token` parametresi zorunlu. Handler, gelen her update'te `X-Telegram-Bot-Api-Secret-Token` header'ını doğrular — eşleşmezse 401 döner, update işlenmez (07 §5.11b). |
| Idempotency | Telegram başarısız webhook teslimatlarını yeniden dener. Handler, gelen her update'in `update_id` değerini kontrol eder — daha önce işlenmiş `update_id` tekrar gelirse no-op (skip). İşlenmiş `update_id`'ler kısa süreli cache'te (Redis, TTL: 24 saat) tutulur. Bu kural özellikle `/start` deep-link bağlama akışında aynı kodun tekrar işlenmesini önler. |

> **Not:** `getUpdates` yöntemi Telegram'da webhook ile karşılıklı dışlayıcıdır — webhook kuruluyken `getUpdates` çağrılamaz. MVP'de yalnızca webhook kullanılır. `getUpdates` yalnızca webhook kurulmadan önce test/debug amacıyla kullanılabilir.

**Mesaj formatı:**

| Konu | Karar |
|------|-------|
| Format | Markdown V2 (`parse_mode: "MarkdownV2"`) |
| Escaping | MarkdownV2 `_ * [ ] ( ) ~ \ > # + - = \| { } . !` karakterlerinin escape edilmesini zorunlu tutar. Tüm user-generated ve item-derived string'ler (item adı, kullanıcı adı, transaction notu) mesaja gömülmeden önce MarkdownV2 escape helper'ından geçirilir. |
| Dil | Kullanıcının platform dil tercihine göre (05 §7.3) |
| Maksimum uzunluk | 4096 karakter |

### 5.3 API Limitleri

| Limit | Değer |
|-------|-------|
| Mesaj gönderimi (aynı chat) | 1 mesaj/saniye |
| Mesaj gönderimi (farklı chat'ler) | 30 mesaj/saniye |
| Grup mesajı | 20 mesaj/dakika (per group) |
| Webhook eşzamanlı bağlantı | 1-100 arası (varsayılan 40). Skinora production'da `max_connections=40` kullanır — MVP trafiği için yeterli. |

**setWebhook kurulum parametreleri:**

| Parametre | Skinora değeri | Açıklama |
|-----------|---------------|----------|
| `url` | `https://skinora.com/api/v1/webhooks/telegram` | Webhook endpoint URL |
| `secret_token` | Production secret (§9.2) | `X-Telegram-Bot-Api-Secret-Token` header doğrulaması için |
| `max_connections` | `40` (varsayılan) | Eşzamanlı bağlantı limiti — MVP trafiği için yeterli |
| `allowed_updates` | `["message"]` | Yalnızca mesaj update'leri alınır — gereksiz update türleri filtrelenir |
| `drop_pending_updates` | `true` (ilk kurulumda) | Bot yeniden başlatıldığında eski update'ler atlanır |

**Platform stratejisi:** Bildirimler sıralı kuyrukta gönderilir, chat başına 1 mesaj/saniye limiti aşılmaz.

### 5.4 Hata Senaryoları ve Retry

| Hata | Kod | Aksiyon | Retry |
|------|-----|---------|-------|
| Rate limit | 429 | `retry_after` değerini bekle | Evet — Telegram'ın belirttiği süre kadar |
| Mesaj gönderilemedi | 403 (Forbidden) | Birden fazla nedeni olabilir (aşağıya bakınız) — `error_description` değerine göre ayrıştırılır | Hayır |
| Chat bulunamadı | 400 (Bad Request) | Chat ID geçersiz — bağlantı kopmuş | Hayır — kullanıcıya tekrar bağlanma uyarısı |
| Telegram API down | 5xx | Log + retry | Evet — 3 deneme (1dk, 5dk, 15dk) |

**403 neden ayrıştırma (Telegram `error_description` değerine göre):**

| Error description | Neden | Aksiyon |
|-------------------|-------|---------|
| `Forbidden: bot was blocked by the user` | Kullanıcı botu engelledi | Telegram kanalını devre dışı bırak, kullanıcıya platform-içi bildirim |
| `Forbidden: user is deactivated` | Telegram hesabı silinmiş/deaktif | Telegram kanalını devre dışı bırak |
| `Forbidden: bot can't send messages to bots` | Chat ID bir bota ait | Log + bağlantıyı sil (veri tutarsızlığı) |
| `Forbidden: bot can't initiate conversation with a user` | Kullanıcı botu hiç /start'lamadı veya conversation geçmişi silindi | Kullanıcıya "Telegram botunu tekrar başlatın" yönlendirmesi |
| Diğer 403 | Bilinmeyen neden | Log + admin alert, kanal geçici devre dışı |

### 5.5 Bağımlılık Riski

| Senaryo | Etki | Mitigasyon |
|---------|------|-----------|
| **Telegram API down** | Telegram bildirimleri gitmez | Platform içi bildirimler her zaman aktif. Email yedek kanal. Outbox pattern ile kuyrukta bekler. |
| **Bot token sızdırılırsa** | Bot kötüye kullanılabilir | Anında `/revoke` → yeni token → redeploy. Chat ID'ler güvende — yeni token ile bildirim devam eder. |

---

## 6. Discord Bot API

### 6.1 Bot Kurulumu ve Bağlantı

**Bot oluşturma:** Discord Developer Portal (`https://discord.com/developers/applications`):
1. Yeni Application oluştur
2. Bot sekmesinden bot ekle
3. Bot token al
4. OAuth2 scope: `identify` (minimum — yalnızca kullanıcı kimliği bağlama için)

**MVP Guild Install (Skinora Discord sunucusu):**

MVP'de bot DM gönderebilmesi için mutual guild önkoşulu kullanılır. Bunun için:

| Adım | Açıklama |
|------|----------|
| 1. Sunucu oluştur | Skinora resmi Discord sunucusu oluşturulur |
| 2. Bot invite | Bot, Skinora sunucusuna invite edilir. Invite URL scope'ları: `bot` |
| 3. Bot permissions | Permission seti: **yok (0)** — MVP'de bot yalnızca DM kullanır, guild permission gerekmez. DM gönderimi bot token ile Create DM + Send Message API çağrılarıyla yapılır ve guild permission'dan bağımsızdır. Sunucu içi mesajlaşma gerekirse ileride eklenir. |
| 4. Kullanıcı katılımı | Kullanıcılara platform üzerinden "Skinora Discord sunucusuna katılın" yönlendirmesi gösterilir (Discord bağlantısı kurulurken) |

> **Not:** Guild install, kullanıcı kimlik bağlama (OAuth2 identify) akışından ayrıdır. OAuth2 ile discord_user_id alınır; mutual guild ise bot'un o kullanıcıya DM açabilmesinin önkoşuludur. Büyüme aşamasında user-install desteği değerlendirilir (§6.1 DM önkoşulları tablosu).

**Kullanıcı-bot bağlantı akışı (OAuth2):**

```
1. Kullanıcı Skinora'da "Discord Bildirimlerini Aç" tıklar
2. Discord OAuth2 sayfasına yönlendirilir (scope: identify)
3. Kullanıcı izin verir
4. Callback'te Discord user ID alınır → UserNotificationPreference'e kaydedilir
5. Bot, kullanıcıya DM göndermeyi dener (aşağıdaki koşullara bağlı)
```

**Güvenlik:** Discord OAuth `state` parametresine server-side session correlation token yazılır. Backend callback'te state'i doğrular, içindeki user ID ile mevcut kullanıcıyı bağlar. Bu yaklaşım CSRF koruması sağlar ve refresh token cookie'sinin path kısıtlamasından (`Path=/api/v1/auth`) bağımsız çalışır (07 U10b).

> **Önemli — OAuth2 ve DM ayrımı:** OAuth2 `identify` scope'u yalnızca kullanıcı kimliğini bağlar, DM gönderim yetkisi vermez. Bot'un DM gönderebilmesi için aşağıdaki önkoşullardan en az biri gerekir:
>
> | Önkoşul | Açıklama |
> |---------|----------|
> | **Ortak sunucu (mutual guild)** | Bot ve kullanıcı aynı Discord sunucusunda bulunur — klasik yöntem |
> | **User-install** | Uygulama, Discord Developer Portal'da user-install destekli olarak yapılandırılır ve kullanıcı uygulamayı kendi hesabına kurar. Bu durumda ortak sunucu gerekmez. |
>
> **Skinora yaklaşımı:** MVP'de Skinora Discord sunucusu kurulur ve kullanıcıların katılması teşvik edilir (mutual guild yöntemi). User-install desteği büyüme aşamasında değerlendirilir. DM gönderimi her iki yöntemde de başarısız olabilir (kullanıcı DM'leri kapatmışsa) — bu durumda §6.4'teki hata tablosu geçerlidir ve fallback kanalları (email, platform-içi bildirim) kullanılır.

**Bot token yönetimi:** Email ve Telegram ile aynı strateji (05 §3.5).

### 6.2 API Detayları

**Base URL:** `https://discord.com/api/v10`

**Kullanılan endpoint'ler:**

| Endpoint | Method | Amaç |
|----------|--------|-------|
| `/oauth2/token` | POST | OAuth2 authorization code → access token. **Zorunlu:** `Content-Type: application/x-www-form-urlencoded` (JSON body desteklenmez). Gerekli alanlar: `client_id`, `client_secret`, `grant_type=authorization_code`, `code`, `redirect_uri`. |
| `/users/@me` | GET | OAuth2 token ile kullanıcı bilgisi alma (id, username) |
| `/users/@me/channels` | POST | DM kanalı oluşturma (bot token ile) |
| `/channels/{channel.id}/messages` | POST | Mesaj gönderme (bot token ile) |

**OAuth2 callback akışı (kimlik bağlama):**

```
1. Kullanıcı Discord'dan authorization code ile callback'e döner
2. POST /oauth2/token → code exchange → access_token alınır
3. GET /users/@me (Bearer: access_token) → discord_user_id alınır
4. discord_user_id → UserNotificationPreference'e kaydedilir
5. access_token atılır — sonraki işlemler bot token ile yapılır
```

**DM gönderim akışı (bot token ile):**

```
1. POST /users/@me/channels → body: { "recipient_id": "{discord_user_id}" }
2. Response'tan channel_id al (cache'lenir — Redis)
3. POST /channels/{channel_id}/messages → body: { "content": "...", "allowed_mentions": { "parse": [] } }
```

**Mesaj formatı:**

| Konu | Karar |
|------|-------|
| Format | Discord Markdown |
| Embed | Zengin bildirimler için Discord Embed kullanılır (opsiyonel — MVP'de düz metin yeterli) |
| Maksimum uzunluk | 2000 karakter |
| Mention koruması | Tüm outbound mesajlarda `allowed_mentions: { "parse": [] }` varsayılan — kullanıcı adı, item adı gibi user-generated string'lerde istenmeyen mention/ping önlenir. Bilinçli mention gereken şablonlarda explicit istisna tanımlanır. |

### 6.3 API Limitleri

**Bilinen tipik değerler (referans — hard-code edilmez):**

| Limit | Tipik Değer | Not |
|-------|-------------|-----|
| Global rate limit | ~50 istek/saniye | Discord tarafından değiştirilebilir |
| DM kanal oluşturma | Cacheable | Aynı kullanıcı için tekrar oluşturma gerekmez |
| Mesaj gönderimi (per channel) | ~5 mesaj/5 saniye | Endpoint bazında farklılık gösterebilir |

**Platform stratejisi (header-driven):** Yukarıdaki değerler sabit kabul edilmez. Tüm Discord API çağrılarında response header'ları parse edilir ve rate limit davranışı buna göre belirlenir:

| Header | Kullanım |
|--------|----------|
| `X-RateLimit-Limit` | Mevcut penceredeki maksimum istek sayısı |
| `X-RateLimit-Remaining` | Kalan istek hakkı |
| `X-RateLimit-Reset-After` | Pencere sıfırlanana kadar kalan saniye |
| `X-RateLimit-Bucket` | Rate limit bucket kimliği — aynı bucket'taki endpoint'ler limiti paylaşır |
| `Retry-After` (429 response) | Beklenmesi gereken süre (saniye) |

DM channel ID cache'lenir (Redis). Mesaj gönderimi sıralı kuyrukta, header-driven rate limit'e uygun şekilde throttle edilir.

### 6.4 Hata Senaryoları ve Retry

**OAuth2 callback hataları (bağlantı kurulumu):**

| Hata | Tetikleyen | Aksiyon |
|------|-----------|---------|
| `access_denied` | Kullanıcı Discord izin ekranında reddetti | Redirect: `/settings?discord=error&reason=denied`. Kullanıcıya "İzin verilmedi" mesajı, tekrar deneme butonu. |
| `invalid_grant` | Authorization code süresi dolmuş veya zaten kullanılmış | Redirect: `/settings?discord=error&reason=expired`. Kullanıcıya "Süre doldu, tekrar deneyin" mesajı. |
| State mismatch | `state` parametresi server-side correlation token ile eşleşmiyor (CSRF girişimi veya session timeout) | 403 dön, bağlantı yapılmaz, güvenlik logu. |
| Token exchange başarısız | Discord `/oauth2/token` 5xx döndü | Log + kullanıcıya "Geçici hata, tekrar deneyin" mesajı. Retry yok — kullanıcı yeniden başlatır. |
| `/users/@me` başarısız | Discord API erişilemez | Log + kullanıcıya "Geçici hata". access_token saklanmaz, bağlantı yapılmaz. |
| `already_linked` | discord_user_id zaten başka bir Skinora hesabına bağlı | Redirect: `/settings?discord=error&reason=already_linked`. Hesaplar arası sahiplik invariantı (06 §3.4). |

**DM gönderim hataları:**

| Hata | Kod | Aksiyon | Retry |
|------|-----|---------|-------|
| Bot token geçersiz / expired | 401 | Admin alert — bot token rotate edilmiş veya bozulmuş. Tüm DM gönderim kuyruğu duraklatılır. | Hayır — manuel müdahale |
| Rate limit | 429 | `retry_after` header'ını bekle | Evet |
| DM başarısız (erişim kısıtı) | 403 | Birden fazla nedeni olabilir (aşağıya bakınız) — neden ayrıştırılarak uygun aksiyon uygulanır | Hayır |
| Kullanıcı bulunamadı | 404 | Bağlantı kopmuş — Discord kanalını devre dışı bırak, kullanıcıya tekrar bağlanma uyarısı | Hayır |
| Discord API down | 5xx | Log + retry | Evet — 3 deneme (1dk, 5dk, 15dk) |

**403 neden ayrıştırma (yalnızca erişim kısıtı / policy kaynaklı 403 için):**

| Olası neden | Tespit | Aksiyon |
|-------------|--------|---------|
| Kullanıcı DM'leri kapatmış | Create DM başarılı ama mesaj gönderimde 403 | Discord kanalını geçici devre dışı bırak, kullanıcıya "DM ayarlarınızı açın" bildirimi (email/platform-içi) |
| Mutual guild yok ve user-install yok | Create DM'de 403 | Kullanıcıya "Skinora Discord sunucusuna katılın" yönlendirmesi (email/platform-içi) |

### 6.5 Bağımlılık Riski

| Senaryo | Etki | Mitigasyon |
|---------|------|-----------|
| **Discord API down** | Discord bildirimleri gitmez | Platform içi + email + Telegram yedek. Outbox kuyrukta bekler. |
| **Bot token sızdırılırsa** | Bot kötüye kullanılabilir | Token reset → Developer Portal → redeploy |
| **Discord politika değişikliği** | DM gönderimi kısıtlanabilir | OAuth2 scope güncellemesi veya alternatif yaklaşım (sunucu bazlı) |

---

## 7. Piyasa Fiyat Verisi

### 7.1 Veri Kaynağı

**Karar:** Steam Market Price API (unofficial) — ücretsiz

**Gerekçe:** MVP'de tüm entegrasyonlar ücretsiz tutulur. Steam Market API fraud tespiti için yeterli doğruluğu sağlar. İşlem hacmi düşükken rate limit sorunu oluşmaz. Büyüme aşamasında ücretli agregator'a (PriceEmpire vb.) geçiş abstraction layer ile kolaydır.

**Kullanım amacı:** Yalnızca fraud tespiti (02 §14.1, 03 §7.1). Kullanıcıya fiyat gösterilmez.

**Bilinen kısıtlamalar:**

| Kısıtlama | Etki | Mitigasyon |
|-----------|------|-----------|
| Resmi API değil — belgelenmemiş | Valve değiştirebilir/kaldırabilir | Abstraction layer ile alternatif kaynağa hızlı geçiş |
| ~20 istek/dakika rate limit | Toplu fetch yavaş | İşlem anında tek item sorgusu yeterli (MVP hacmi düşük) |
| $1800+ item'lar Steam Market'ta listelenemiyor | Bu aralıkta fiyat verisi yok | Yüksek tutarlı işlemler zaten admin dikkatini çeker. Cache'te fiyat yoksa kontrol atlanır, log'a yazılır |
| Tek kaynak — Buff163 fiyatıyla %20-30 sapma | Fraud eşiği buna göre ayarlanmalı | Admin eşiğini daha geniş tutmak (ör: %100 yerine %150) tek kaynak sapmasını tolere eder |

### 7.2 API Detayları

**Endpoint:**

```
GET https://steamcommunity.com/market/priceoverview/?appid=730&currency=1&market_hash_name={item_name}
```

| Parametre | Değer | Açıklama |
|-----------|-------|----------|
| `appid` | `730` | CS2 App ID |
| `currency` | `1` | USD |
| `market_hash_name` | URL-encoded item ismi | Örn: `AK-47%20%7C%20Redline%20(Field-Tested)` |

**Örnek response:**

```json
{
  "success": true,
  "lowest_price": "$12.50",
  "volume": "1,234",
  "median_price": "$13.10"
}
```

**Fiyat parse kuralı (canonical):**

| Öncelik | Alan | Koşul | Aksiyon |
|---------|------|-------|---------|
| 1 | `median_price` | Mevcut ve parse edilebilir | Kullan — en güvenilir değer |
| 2 | `lowest_price` | `median_price` yok veya parse edilemez | Fallback olarak kullan |
| 3 | Her ikisi de yok | `success: true` ama fiyat alanları boş | `no-price` olarak logla, fiyat kontrolü güvenli şekilde atla (§7.4 karar ağacı adım 3b) |

> **Not:** Response string formatındadır (currency sembolü ve virgüllü sayılar: `"$13.10"`, `"1,234"`). Parse sırasında: currency sembolü strip, binlik ayracı kaldır, nokta ondalık ayracı olarak kullan. Locale-aware dönüşümden kaçınılır — sabit format parse kuralı uygulanır.

**Kimlik doğrulama:** Yok — public endpoint, API key gerekmez.

### 7.3 Cache Stratejisi

| Konu | Karar |
|------|-------|
| Yaklaşım | **On-demand + cache** — işlem oluşturulduğunda ilgili item'ın fiyatı sorgulanır ve cache'lenir |
| Cache yeri | SQL Server (ItemPriceCache tablosu) |
| Cache süresi | 24 saat (normal), 48 saat (stale ama kullanılabilir), 48+ saat (expired) |
| Batch fetch | MVP'de yok — on-demand yeterli. Büyüme aşamasında batch job eklenebilir |

**On-demand fetch akışı:**

```
1. Satıcı işlem oluşturur (item: AK-47 | Redline FT)
2. Backend cache'te bu item'ın fiyatını arar
3a. Cache'te var ve ≤24 saat → cache'teki değer kullanılır
3b. Cache'te var ve 24-48 saat (stale) → cache kullanılır, arka planda yenilenir
3c. Cache'te yok veya >48 saat → Steam Market API'den çekilir
4. Rate limit'e takılırsa → fiyat kontrolü atlanır, log + devam
5. Fiyat alınırsa → cache güncellenir (upsert)
```

**Fraud kontrolünde kullanım:**

```
1. Satıcı işlem oluşturur (fiyat: 50 USDT, item: AK-47 | Redline FT)
2. Sistem cache'ten veya API'den median_price okur (örn: 13.10 USD)
3. Sapma hesaplar: |50 - 13.10| / 13.10 = %282
4. Admin eşiği (örn: %100) aşılıyor → işlem FLAGGED
```

> **Not:** Fraud sapma eşiği Steam Market'ın tek kaynak olmasını telafi etmek için agregator senaryosuna göre daha geniş tutulmalıdır. Admin tarafından ayarlanabilir (06 §3.17 SystemSetting).

> **Kapsam notu:** Fiyat API'si yalnızca piyasa fiyat sapması kontrolü için kullanılır. Kısa sürede yüksek hacim tespiti (02 §14.4) fiyat API'sinden bağımsızdır — iç transaction sayacı ile çalışır (05 §3.1 Fraud modülü).

### 7.4 Hata Senaryoları ve Fallback

**Canonical karar ağacı (fiyat kontrolü):**

```
1. Cache'te fiyat var ve ≤24 saat (fresh) → cache kullan
2. Cache'te fiyat var ve 24-48 saat (stale) → cache kullan, arka planda yenileme dene
3. Cache'te fiyat yok veya >48 saat (expired) → API'den çek
   3a. API başarılı → cache güncelle, fiyat kontrolü yap
   3b. API başarısız (down/rate limit/item yok) → fiyat kontrolü atla, log + devam
4. Fiyat kontrolü atlandığında → işlem oluşturulur, diğer fraud kontrolleri çalışır
```

| Hata | Aksiyon | Etkisi |
|------|---------|--------|
| API erişilemez (Steam down) | Karar ağacı adım 3b: cache ≤48 saat ise cache kullan, yoksa fiyat kontrolü atla + log | İşlem oluşturulur — diğer fraud kontrolleri devam eder |
| Rate limit (~20 req/dk aşıldı) | Bekleme, sonraki istek kuyruğa. Cache varsa kullan. | Kısa süreli gecikme — MVP hacminde nadir |
| Item cache'te yok + API'den alınamıyor | Fiyat kontrolü atlanır, log | Beklenen durum ($1800+ item'lar veya nadir item'lar) |
| Steam Market API kalıcı değişiklik | Alternatif kaynağa geçiş | Abstraction layer (IPriceService) ile ücretli agregator'a geçiş birkaç saatlik iş |

### 7.5 Bağımlılık Riski

| Senaryo | Etki | Mitigasyon |
|---------|------|-----------|
| **Steam Market API down** | Fraud fiyat kontrolü degraded | Cache ≤48 saat ise cache'teki fiyat kullanılır (§7.4 karar ağacı). Cache yoksa veya >48 saat ise fiyat kontrolü atlanır, diğer fraud kontrolleri bağımsız çalışmaya devam eder. Steam tamamen down ise zaten işlem başlatılamaz (envanter okunamaz). |
| **Steam Market API kaldırılırsa** | Kalıcı kaynak kaybı | Abstraction layer ile ücretli agregator'a (PriceEmpire vb.) geçiş. Bu geçiş önceden planlanmış — interface hazır. |
| **Fiyat manipülasyonu (tek kaynak)** | Yanlış flag'leme veya flag kaçırma | Sapma eşiği geniş tutulur (%100+). Admin son karar verici — otomatik flag sadece dikkat çeker, kararı admin alır. Büyüme aşamasında çoklu kaynak agregasyonuna geçiş planlanmıştır. |

**Büyüme yolu:**

| Aşama | Strateji |
|-------|----------|
| MVP | Steam Market API (ücretsiz, on-demand + cache) |
| Büyüme | Ücretli agregator (PriceEmpire vb.) — batch fetch + çoklu kaynak median |
| Ölçek | Birden fazla kaynak + kendi agregasyon mantığı |

---

## 8. Bağımlılık Risk Matrisi

Tüm entegrasyonların çökme senaryosunda platform davranışı:

| Entegrasyon | Çökme Etkisi | Kullanıcı Etkisi | SLA Beklentisi | Fallback |
|-------------|--------------|-------------------|---------------|----------|
| **Steam OpenID** | Yeni giriş yapılamaz | Mevcut session'lar çalışır (JWT) | %99.5+ (Steam geçmişi) | Bekleme — alternatif yok |
| **Steam Web API** | Profil çekilemez, MA kontrolü yapılamaz | Yeni kullanıcı profili eksik kalır, trade URL kaydı/MA doğrulaması bloke olur — yeni işlem başlatılamaz | %99.5+ | Cache'teki profil verisi geçici kullanılır. MA doğrulaması: trade URL kaydı pending state'e alınır, kullanıcıya "Steam servisleri geçici olarak erişilemez, trade URL kaydınız otomatik tamamlanacak" bildirimi gösterilir. API dönene kadar kullanıcı işlem başlatamaz. |
| **Steam Community (Inventory + Trade)** | Envanter okunamaz, item transferi durur | İşlem başlatılamaz, aktif işlemler timeout dondurulur (03 §11.2) | %99.5+ | Bekleme + timeout dondurma |
| **TronGrid API** | Ödeme izleme ve transfer durur | Ödeme doğrulama gecikmesi | %99.9+ (blockchain) | Rate limit: ikinci API key. Provider outage: bekleme (MVP), alternatif sağlayıcı (büyüme) — §3.6 |
| **Tron Ağı** | Tüm blockchain işlemleri durur | Ödeme ve iade durur | %99.99+ | Bekleme — blockchain kendi kendini düzeltir |
| **Email (Resend)** | Email bildirimleri gitmez | Hafif — diğer kanallar aktif | %99.9+ | Platform içi + Telegram/Discord |
| **Telegram API** | Telegram bildirimleri gitmez | Hafif — diğer kanallar aktif | %99.9+ | Platform içi + email |
| **Discord API** | Discord bildirimleri gitmez | Hafif — diğer kanallar aktif | %99.9+ | Platform içi + email + Telegram |
| **Steam Market Price API** | Fiyat kontrolü çalışmaz | Yok — arka plan süreci | %99.5+ (Steam) | Cache (48 saat), diğer fraud kontrolleri devam. Steam down ise zaten işlem başlatılamaz |

**Eşzamanlı çökme senaryoları:**

| Senaryo | Etki | Karar |
|---------|------|-------|
| Steam + Blockchain aynı anda down | Platform işlem yapamaz | Maintenance mode'a geç, kullanıcılara bildirim (03 §11.1) |
| Tüm bildirim kanalları down | Bildirimler gitmez | Platform içi bildirimler DB'ye yazılır (Redis bağımsız) — kullanıcı sayfayı yenilediğinde görür |
| Steam tamamen down (tüm servisler) | Giriş, envanter, trade offer, fiyat — hepsi durur | Yeni işlem başlatma devre dışı, mevcut işlemler timeout dondurulur |

---

## 9. Ortam Konfigürasyonu

### 9.1 Ortam Bazlı Entegrasyon Ayarları

| Entegrasyon | Development | Staging | Production |
|-------------|-------------|---------|------------|
| **Steam OpenID** | Steam test hesapları | Steam test hesapları | Gerçek Steam hesapları |
| **Steam Web API** | Gerçek API key (test hesaplarıyla) | Aynı | Aynı (ayrı key önerilir) |
| **Steam Sidecar** | 1 test bot hesabı | 2 test bot hesabı | N bot hesabı (05 §3.2) |
| **Tron Blockchain** | Nile Testnet (Shasta opsiyonel — izole birim testleri) | Nile Testnet | Tron Mainnet |
| **TronGrid** | Testnet API key | Testnet API key | Mainnet API key |
| **USDT/USDC Kontrat** | Test token kontratları | Test token kontratları | Gerçek kontrat adresleri (§3.3) |
| **Email (Resend)** | Sandbox mode veya test domain | Test domain | Production domain (skinora.com) |
| **Telegram** | Test bot | Test bot | Production bot |
| **Discord** | Test bot + test application | Test bot | Production bot + application |
| **Steam Market Price** | Mock data veya gerçek API (test item'lar) | Gerçek API (düşük sıklıkta) | Gerçek API (on-demand — §7.3) |

### 9.2 Credential Envanteri

Her ortam için gerekli credential'lar ve saklama yeri:

| Credential | Ortam | Saklama |
|------------|-------|---------|
| Steam API Key | Tümü | `.env` (dev), Docker Secrets (prod) |
| Steam Bot Credentials (×N) | Tümü | `.env` (dev), Docker Secrets (prod) |
| — username | | |
| — password | | |
| — shared_secret | | |
| — identity_secret | | |
| Tron HD Wallet Master Seed | Tümü | `.env` (dev), Vault/Docker Secrets (prod) |
| TronGrid API Key | Tümü | `.env` (dev), Docker Secrets (prod) |
| Resend API Key | Tümü | `.env` (dev), Docker Secrets (prod) |
| Telegram Bot Token | Tümü | `.env` (dev), Docker Secrets (prod) |
| Telegram Webhook Secret Token | Tümü | `.env` (dev), Docker Secrets (prod) |
| Discord Bot Token | Tümü | `.env` (dev), Docker Secrets (prod) |
| Discord Client ID | Tümü | `.env` (dev), Docker Secrets (prod) |
| Discord Client Secret | Tümü | `.env` (dev), Docker Secrets (prod) |
| Resend Webhook Signing Secret | Tümü | `.env` (dev), Docker Secrets (prod) |
> **Not:** Steam Market Price API credential gerektirmez (public endpoint). Internal servis credential'ları (sidecar API key, HMAC secret, JWT signing key, DB connection string) 05 §3.5'te tanımlıdır.
>
> **Telegram credential ayrımı:** Bot Token API çağrıları için kimlik doğrulama, Webhook Secret Token ise gelen webhook update'lerinin `X-Telegram-Bot-Api-Secret-Token` header'ı ile doğrulanması içindir — farklı amaçlara hizmet ederler ve ayrı saklanmalıdır.

### 9.3 Development Ortamı Kolaylıkları

| Kolaylık | Açıklama |
|----------|----------|
| Steam Market Price mock | Development'ta gerçek API yerine sabit fiyat listesi kullanılabilir (IPriceService mock implementasyonu) |
| Email sandbox | Resend sandbox mode — gerçek email gönderilmez, API yanıtı simüle edilir |
| Telegram test bot | Ayrı bir test bot token'ı ile gerçek Telegram API kullanılır (mesajlar test grubuna) |
| Blockchain testnet | Nile faucet'ten test TRX ve test token alınır — gerçek para riski yok (Shasta opsiyonel) |
| Steam sandbox | Test Steam hesapları — düşük değerli veya test item'larıyla çalışılır |

---

*Skinora — Integration Specifications v2.5*
