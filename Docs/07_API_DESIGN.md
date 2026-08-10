# Skinora — API Design

**Versiyon: v3.1** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-08-10 (T119a — §7.6 accept ucu v3.0 alanları: `steamTradeUrl` sahiplik doğrulaması (partner ↔ alıcının kendi SteamID64'ü) ve Steam erişilemediğinde fail-closed 503 `STEAM_UNAVAILABLE` hata listesine eklendi; §5.1 `GET /users/me` yanıtına salt-okunur `steamTradeUrl` eklendi — §7.6 ön-doldurma kaynağı.)

---

## İçindekiler

1. [Genel Bakış](#1-genel-bakış)
2. [Konvansiyonlar](#2-konvansiyonlar)
3. [Traceability Matrix](#3-traceability-matrix)
4. [Auth Endpoints](#4-auth-endpoints)
5. [Users Endpoints](#5-users-endpoints)
6. [Steam Endpoints](#6-steam-endpoints)
7. [Transaction Endpoints](#7-transaction-endpoints)
8. [Notification Endpoints](#8-notification-endpoints)
9. [Admin Endpoints](#9-admin-endpoints)
10. [Platform Endpoints](#10-platform-endpoints)
11. [SignalR Hubs](#11-signalr-hubs)
12. [GAP Kararları](#12-gap-kararları)

---

## 1. Genel Bakış

Bu doküman, Skinora platformunun frontend-backend API iletişimini tanımlar. Tüm endpoint'ler UI spesifikasyonlarından (04) ve kullanıcı akışlarından (03) türetilmiştir.

### 1.1 Özet

| Kategori | Endpoint Sayısı |
|----------|----------------|
| Auth | 9 |
| Users | 17 |
| Webhooks | 1 |
| Steam | 1 |
| Transactions | 11 |
| Notifications | 4 |
| Admin | 22 |
| Platform | 2 |
| **Toplam REST** | **67** |
| SignalR Hub | 2 |
| **Genel Toplam** | **69** |

### 1.2 Base URL

```
https://skinora.com/api/v1/
```

Versioning: URL prefix tabanlı (05 §2.2).

---

## 2. Konvansiyonlar

### 2.1 URL Yapısı (K1)

| Kural | Örnek |
|-------|-------|
| Küçük harf, kebab-case | `/steam-accounts`, `/audit-logs` |
| Collection isimleri çoğul | `/transactions`, `/notifications` |
| Nested resource max 2 seviye | `/transactions/:id/disputes/:disputeId/escalate` |
| Aksiyon endpoint'leri fiil ile, POST method | `/accept`, `/cancel`, `/approve`, `/reject` |
| ID formatı GUID | `/transactions/550e8400-e29b-41d4-...` |
| Steam ID parametresi string | `/users/76561198012345678` |

### 2.2 HTTP Method Kullanımı (K2)

| Method | Kullanım |
|--------|----------|
| GET | Veri okuma, listeleme |
| POST | Kaynak oluşturma, iş aksiyonu tetikleme |
| PUT | Kaynak güncelleme |
| DELETE | Kaynak silme, bağlantı koparma |

PATCH kullanılmaz — MVP için PUT yeterli.

### 2.3 Authentication (K3)

| Konu | Karar |
|------|-------|
| Access token | JWT, `Authorization: Bearer <token>` header'ında |
| Refresh token | HttpOnly + Secure + SameSite=Strict cookie |
| Access token ömrü | 15 dakika |
| Token yenileme | `POST /api/v1/auth/refresh` — cookie'den refresh token okunur |
| Access token storage | JavaScript belleğinde (memory), cookie'ye yazılmaz |

**Public endpoint'ler (auth gerektirmeyen):** P1, P2, A1, A2, T5 (public varyant), U5.

### 2.4 Response Envelope (K4)

Tüm response'lar aynı yapıda sarmalanır:

**Başarılı:**
```json
{
  "success": true,
  "data": { ... },
  "error": null,
  "traceId": "00-abc123..."
}
```

**Hatalı:**
```json
{
  "success": false,
  "data": null,
  "error": {
    "code": "VALIDATION_ERROR",
    "message": "Giriş verileri geçersiz.",
    "details": {
      "price": ["İşlem tutarı minimum 10 USDT olmalıdır"]
    }
  },
  "traceId": "00-def456..."
}
```

| Field | Tip | Her zaman var | Açıklama |
|-------|-----|---------------|----------|
| `success` | boolean | Evet | `true` / `false` |
| `data` | object \| null | Evet | Başarılıysa veri, hatalıysa `null` |
| `error` | object \| null | Evet | Başarılıysa `null`, hatalıysa hata detayı |
| `error.code` | string | Hata varsa | Makine-okunabilir hata kodu |
| `error.message` | string | Hata varsa | İnsan-okunabilir mesaj (lokalize edilebilir) |
| `error.details` | object \| null | Hata varsa | Validation: field → mesaj listesi. Diğerlerinde `null` |
| `traceId` | string | Evet | İstek takip ID'si |

Backend: `ApiResponse<T>` generic wrapper + global action filter.

### 2.5 HTTP Status Kodları (K5)

| Kod | Kullanım |
|-----|----------|
| 200 | Başarılı GET, PUT, aksiyon POST'ları |
| 201 | Kaynak oluşturma (POST) — `Location` header ile |
| 400 | Validation hatası |
| 401 | Kimlik doğrulama gerekli |
| 403 | Yetki yok |
| 404 | Kaynak bulunamadı |
| 409 | Conflict (geçersiz state geçişi, duplicate) |
| 422 | İş kuralı ihlali |
| 429 | Rate limit aşıldı |
| 500 | Sunucu hatası |

**Not:** 204 kullanılmaz — tutarlılık için 200 + boş envelope döner.

### 2.6 Pagination (K6)

**Request:** `?page=1&pageSize=20`

| Param | Varsayılan | Min | Max |
|-------|-----------|-----|-----|
| `page` | 1 | 1 | — |
| `pageSize` | 20 | 1 | 100 |

**Response (`data` içinde):**
```json
{
  "items": [ ... ],
  "totalCount": 142,
  "page": 1,
  "pageSize": 20
}
```

### 2.7 Filtering & Sorting (K7)

| Konu | Karar |
|------|-------|
| Filtre | Query param: `?status=COMPLETED&stablecoin=USDT` |
| Çoklu değer | Virgülle: `?status=COMPLETED,CANCELLED_TIMEOUT` (OR) |
| Çoklu filtre | AND birleşim |
| Tarih aralığı | `dateFrom`, `dateTo` — ISO 8601 |
| Tutar aralığı | `minAmount`, `maxAmount` |
| Metin arama | `search` |
| Sıralama | `?sortBy=createdAt&sortOrder=desc` |
| Varsayılan sıralama | `createdAt desc` |
| Geçersiz param | Yok sayılır (hata dönmez) |

### 2.8 JSON Naming (K8)

| Konu | Karar |
|------|-------|
| Property isimleri | camelCase (`transactionId`, `createdAt`) |
| Enum değerleri | UPPER_SNAKE_CASE (`CANCELLED_ADMIN`, `PRICE_DEVIATION`) — 06 ile birebir |
| Tarih formatı | ISO 8601 UTC (`2026-03-16T14:32:00Z`) |
| Null handling | `null` döner, field gizlenmez |
| Para tutarları | String, 2 ondalık (`"100.00"`) |

### 2.9 Rate Limiting (K9)

**Response header'ları (her istekte):**
```
X-RateLimit-Limit: 60
X-RateLimit-Remaining: 58
X-RateLimit-Reset: 1710600000
```

**Aşıldığında:** 429 + `Retry-After` header.

**Limitler:**

| Grup | Pencere | Limit |
|------|---------|-------|
| Auth (login, refresh) | 1 dk | 10 |
| Okuma (GET) — kullanıcı | 1 dk | 60 |
| Yazma (POST/PUT/DELETE) — kullanıcı | 1 dk | 20 |
| Steam inventory | 1 dk | 5 |
| Admin okuma | 1 dk | 120 |
| Admin yazma | 1 dk | 30 |
| Public | 1 dk | 30 |

### 2.10 İç Tutarlılık (K10)

| Kural | Açıklama |
|-------|----------|
| Tarih field isimleri | 06 ile birebir: `createdAt`, `updatedAt`, `completedAt`, `cancelledAt` |
| ID field isimleri | 06 FK ile birebir: `transactionId`, `sellerId`, `buyerId` |
| Enum değerleri | 06 §2 ile birebir aynı string |
| Cüzdan adresleri | Her zaman tam adres, maskeleme frontend'in işi |
| Para tutarları | Her zaman 2 ondalık: `"100.00"` |

> **İstisna:** API response field isimleri kullanıcı perspektifinden anlamlı isimler kullanabilir, doğrudan DB entity field adlarını yansıtmak zorunda değildir. Örneğin 06'daki `DefaultPayoutAddress` API'de `sellerWalletAddress` olarak döner. Eşleştirme semantik düzeyde yapılır — aynı kavramı temsil eden field'lar farklı isimlenebilir.

---

## 3. Traceability Matrix

### 3.1 İleri İzlenebilirlik: Ekranlar → Endpoint'ler

| Ekran | Veri (GET) | Aksiyon (POST/PUT/DELETE) |
|-------|-----------|--------------------------|
| S01 | P1, P2 | — |
| S02 | A1, A2 | A3 |
| S03 | — | U17 (A7 otomatik tetiklenir) |
| S05 | T1, U2, N2 | — |
| S06 | S1, T3, T4 | T2 |
| S07 | T5 | T6, T7, T8, T9, T10, T11 |
| S08 | U1 | U3, U4, A5, A6 |
| S09 | U5 | — |
| S10 | U6 | U7, U8, U9, U10, U10b, U11, U12, U13, U14, U15, U16, U17 |
| S11 | N1 | N3, N4 |
| S12 | AD1 | — |
| S13 | AD2 | — |
| S14 | AD3 | AD4, AD5 |
| S15 | AD6 | — |
| S16 | AD7 | AD4, AD5, AD19, AD19b, AD19c |
| S17 | AD8 | AD9 |
| S18 | AD10 | — |
| S19 | AD11, AD15 | AD12, AD13, AD14, AD17 |
| S20 | AD16, AD16b | — |
| S21 | AD18 | — |

**Ekrana bağlı olmayan endpoint'ler:**

| Endpoint | Açıklama |
|----------|----------|
| A8 | Logout — tüm authenticated sayfalardaki header'dan tetiklenir |
| A9 | Token refresh — auth interceptor tarafından otomatik çağrılır |
| W1 | Telegram webhook — Telegram sunucuları tarafından çağrılır, kullanıcı ekranıyla ilişkisi yok |

### 3.2 İleri İzlenebilirlik: Akışlar → Endpoint'ler

| Akış (03) | Endpoint'ler |
|-----------|-------------|
| §2.1 Satıcı giriş/kayıt | A1, A2, A3, A4, A8, A9 |
| §2.1 Trade URL kaydı + MA kontrolü | U17, A7 |
| §2.2 İşlem başlatma | T3, T4, S1, T2 |
| §2.3 Item emaneti | T5 (real-time: RT1) |
| §2.4 Satıcıya ödeme | T5, T11 (real-time: RT1) |
| §2.5 Satıcı iptal | T7 |
| §3.1 Alıcı giriş | A1, A2, A3 |
| §3.2 İşlemi kabul | T6 |
| §3.3 Alıcı iptal | T7 |
| §3.4 Ödeme gönderme | T5 (real-time: RT1) |
| §3.5 Item teslim alma | T5 (real-time: RT1) |
| §4.1-4.5 Timeout akışları | T5 (real-time: RT1) |
| §5.1-5.4 Ödeme edge case | T5 (real-time: RT1) |
| §6.1-6.3 Dispute | T8, T9 |
| §6.4 Admin eskalasyonu | T10 |
| §7.1-7.4 Fraud/flag | T5 (FLAGGED state), AD2, AD3, AD4, AD5 |
| §8.1 Admin giriş | AD1 |
| §8.2 Flag inceleme | AD2, AD3, AD4, AD5 |
| §8.3 İşlem listesi | AD6, AD7 |
| §8.4 Parametre yönetimi | AD8, AD9 |
| §8.5 Steam hesapları | AD10 |
| §8.6 Rol yönetimi | AD11, AD12, AD13, AD14, AD15, AD17 |
| §9.1-9.2 Cüzdan yönetimi | U3, U4, A5, A6 |
| §9.3 Profil görüntüleme | U1, U5 |
| §10.1-10.2 Hesap yönetimi | U13, U14 |
| §11a.3 Sanctions screening | AD19b, AD19c (otomatik tetikleme), AD22, AD23, AD24 (admin liste yönetimi) |
| §12 Bildirimler | N1, N2, N3, N4 (real-time: RT2) |
| Telegram webhook | W1 (dış tetikleme — 08 §5.2) |

### 3.3 Geri İzlenebilirlik: Endpoint → Kaynaklar

Tüm endpoint'lerin en az bir ekran (04), akış (03) veya dış tetikleme kaynağı mevcuttur. Kaynaksız endpoint yoktur.

---

## 4. Auth Endpoints

### 4.1 Genel Auth Akışı

```
Kullanıcı                Frontend              Backend                Steam
  │                        │                      │                     │
  │── "Giriş Yap" tıkla ─→│                      │                     │
  │                        │── GET /auth/steam ──→│                     │
  │                        │                      │── 302 redirect ───→│
  │                        │                      │←── callback ────────│
  │                        │←── redirect + cookie │                     │
  │                        │── POST /auth/refresh →│                     │
  │                        │←── accessToken ───────│                     │
```

### 4.2 A1 — `GET /auth/steam`

**Amaç:** Steam OpenID authentication başlatma.

| Konu | Değer |
|------|-------|
| Auth | Public |
| Davranış | 302 redirect → Steam OpenID |

**Query Params:**

| Param | Zorunlu | Açıklama |
|-------|---------|----------|
| `returnUrl` | Hayır | Login sonrası frontend URL. Varsayılan: `/dashboard` |

**`returnUrl` güvenlik kuralları:**
- Yalnızca relative path kabul edilir (`/dashboard`, `/transactions/guid`). Absolute URL reddedilir.
- Protocol-relative (`//evil.com`) ve dış domain URL'leri reddedilir.
- Geçersiz değer → varsayılan `/dashboard` kullanılır, hata dönmez.

Backend Steam OpenID URL'ini oluşturur, `returnUrl`'i doğruladıktan sonra state'e kaydeder, 302 redirect.

**Hatalar:**
- Steam erişilemezse → redirect: `/auth/callback?error=steam_unavailable`
- Brute force koruması (05 §6.3): Belirli sayıda başarısız login denemesi sonrası geçici kilitleme → redirect: `/auth/callback?error=temporarily_locked&retryAfter=300`

### 4.3 A2 — `GET /auth/steam/callback`

**Amaç:** Steam callback. Token üretir, frontend'e yönlendirir.

| Konu | Değer |
|------|-------|
| Auth | Public (Steam callback) |
| Davranış | Doğrula → cookie set → redirect |

**Akış:**

1. Steam OpenID yanıtını doğrular
2. Kullanıcı arar:
   - **Yeni** → Hesap oluştur → redirect: `/auth/callback?status=new_user`
   - **Mevcut** → redirect: `/auth/callback?status=success`
3. Refresh token üretir, HttpOnly cookie set eder
4. `returnUrl` varsa query param olarak ekler

**Cookie:**
```
Set-Cookie: refreshToken=...; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth; Max-Age=604800
```

**Frontend `/auth/callback` sayfası:**
- `status=success` → `POST /auth/refresh` → `returnUrl` veya `/dashboard`
- `status=new_user` → `POST /auth/refresh` → access token → ToS modal → `POST /auth/tos/accept` → dashboard
- `error=*` → Hata mesajı + "Tekrar Dene"

**Hatalar:** Redirect ile: `?error=auth_failed`, `?error=account_banned`

### 4.4 A3 — `POST /auth/tos/accept`

**Amaç:** Terms of Service kabul + 18+ yaş beyanı (ilk kayıt **veya versiyon değişiminde yeniden kabul**). Tek adımda ToS kabul ve soft yaş gate self-attestation (02 §21.1, 03 §11a.2).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{ "tosVersion": "1.0", "ageOver18": true }
```

| Field | Açıklama |
|-------|----------|
| `tosVersion` | Kabul edilen ToS versiyonu (maks. 20 karakter) |
| `ageOver18` | 18+ yaş self-attestation — `false` veya eksik ise 400 |

**Response (200) `data`:**
```json
{ "accepted": true, "acceptedAt": "2026-03-16T14:32:00Z" }
```

**Versiyon davranışı (WP11 — T30 reprompt):** İstenen `tosVersion` kullanıcının kayıtlı `tosAcceptedVersion`'ından **farklıysa** (ilk kabul veya versiyon yükseltme) kabul kaydedilir/güncellenir; `tosAcceptedVersion` + `tosAcceptedAt` yeniden damgalanır, ilk kabuldeki 18+ beyanı (`ageConfirmedAt`) **korunur**. İstenen versiyon **aynı** ise 409 döner (gerçek mükerrer). İstemci, mevcut sürümle eşleşmeyen kabulde re-prompt eder (`tosAcceptedVersion` `/auth/me`'den okunur, §4.5).

**Hatalar:** 409 `TOS_ALREADY_ACCEPTED` (yalnızca **aynı** versiyon yeniden gönderildiğinde), 400 `VALIDATION_ERROR` (ageOver18 false/eksik veya tosVersion eksik)

### 4.5 A4 — `GET /auth/me`

**Amaç:** Mevcut oturum bilgisi. Frontend sayfa yüklemesinde çağırır.

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "id": "guid",
  "steamId": "76561198012345678",
  "displayName": "PlayerOne",
  "avatarUrl": "https://steamcdn.../abc.jpg",
  "mobileAuthenticatorActive": true,
  "tosAccepted": true,
  "tosAcceptedVersion": "1.0",
  "role": "user",
  "language": "tr",
  "hasSellerWallet": true,
  "hasRefundWallet": false,
  "createdAt": "2026-03-10T08:00:00Z",
  "isSuspended": false
}
```

| Field | Açıklama |
|-------|----------|
| `role` | `"user"` veya `"admin"` — routing kararı |
| `mobileAuthenticatorActive` | İşlem başlatma kontrolü |
| `tosAccepted` | `false` → ToS modal |
| `tosAcceptedVersion` | Kabul edilen ToS versiyonu (`null` = hiç kabul edilmemiş). İstemci mevcut sürümle karşılaştırır; uyuşmazsa re-prompt (WP11/T30, §4.4) |
| `isSuspended` | `true` → kısıtlı oturum (SuspendedHeader + S03d), fon-akışı mutation'ları reddedilir (T105a, 02 §14.0, 03 §2.1) |

### 4.6 A5 — `POST /auth/steam/re-verify`

**Amaç:** Güvenlik-kritik işlemler için Steam re-auth başlatma (cüzdan değişikliği).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{ "purpose": "wallet_change", "returnUrl": "/profile" }
```

**Response (200) `data`:**
```json
{ "steamAuthUrl": "https://steamcommunity.com/openid/login?..." }
```

Frontend `window.location.href` ile yönlendirir.

### 4.7 A6 — `GET /auth/steam/re-verify/callback`

**Amaç:** Re-verify callback. ReAuth token üretir.

| Konu | Değer |
|------|-------|
| Auth | Public (Steam callback) |
| Davranış | Doğrula → reAuthToken üret → redirect |

**Akış:**
1. Steam yanıtını doğrular
2. Steam ID mevcut oturumla eşleşiyor mu kontrol eder
3. ReAuth token üretir (5 dk TTL, tek kullanımlık)
4. Redirect: `{returnUrl}?reAuthToken=xyz123` — `returnUrl` A5 request'indeki değer (aynı güvenlik kuralları: yalnızca relative path, varsayılan `/profile`)

Frontend wallet update'te header'a ekler: `X-ReAuth-Token: xyz123`

**Güvenlik mitigasyonları (query param token taşıma):**
- Frontend callback sonrası `history.replaceState()` ile URL'den token'ı anında temizler
- Token backend'de kullanıldıktan sonra anında invalidate edilir (tek kullanımlık)
- `Referrer-Policy: same-origin` header zorunlu — token dış sitelere sızmaz

**Hatalar:** Redirect ile: `?error=re_verify_failed`, `?error=steam_id_mismatch`

### 4.8 A7 — `POST /auth/check-authenticator`

**Amaç:** Steam Mobile Authenticator durumu kontrolü.

| Konu | Değer |
|------|-------|
| Auth | Authenticated |
| Çağrı zamanı | **Trade URL kaydı sırasında** (login'de değil). `GetTradeHoldDurations` endpoint'i `trade_offer_access_token` gerektirir — bu token trade URL'den parse edilir (08 §2.2). Bu nedenle A7, trade URL kayıt endpoint'i (U17) içinde otomatik tetiklenir. |

**Request body:**
```json
{ "tradeOfferAccessToken": "abc123xyz" }
```

**Response (200) `data`:**
```json
{ "active": true }
```

```json
{ "active": false, "setupGuideUrl": "https://help.steampowered.com/..." }
```

Steam sidecar üzerinden yapılır (05 §3.2). Steam API yanıt vermezse trade URL kaydı pending state'e alınır (08 §8 fallback kuralı).

### 4.9 A8 — `POST /auth/logout`

**Amaç:** Oturum sonlandırma.

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:** `null`

Davranış: Refresh token silinir, cookie temizlenir (`Set-Cookie: refreshToken=; Max-Age=0`).

### 4.10 A9 — `POST /auth/refresh`

**Amaç:** Access token yenileme.

| Konu | Değer |
|------|-------|
| Auth | Refresh cookie (HttpOnly) |

**Response (200) `data`:**
```json
{ "accessToken": "eyJhbGciOiJIUzI1NiIs...", "expiresIn": 900 }
```

| Field | Açıklama |
|-------|----------|
| `expiresIn` | Token ömrü (saniye) — 900 = 15 dk |

**Hatalar:** 401 `REFRESH_TOKEN_MISSING`, 401 `REFRESH_TOKEN_INVALID`, 401 `REFRESH_TOKEN_EXPIRED`

---

## 5. Users Endpoints

### 5.1 U1 — `GET /users/me`

**Amaç:** Kendi profil sayfası verisi (S08).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "id": "guid",
  "steamId": "76561198012345678",
  "displayName": "PlayerOne",
  "avatarUrl": "https://steamcdn.../abc.jpg",
  "accountAge": "6 ay",
  "createdAt": "2025-09-16T08:00:00Z",
  "reputationScore": 4.8,
  "completedTransactionCount": 24,
  "successfulTransactionRate": 0.96,
  "cancelRate": 0.04,
  "sellerWalletAddress": "TXyz1234567890abcdef1234567890ab",
  "refundWalletAddress": "TAbcdef1234567890abcdef12345678cd",
  "mobileAuthenticatorActive": true,
  "steamTradeUrl": "https://steamcommunity.com/tradeoffer/new/?partner=123456789&token=AbCdEfGh"
}
```

`sellerWalletAddress` / `refundWalletAddress`: Tam adres, `null` ise tanımlanmamış.

`steamTradeUrl` *(v3.0 — T119a)*: §5.16a ile kaydedilmiş normalize trade URL; kaydedilmemişse `null`. Salt okunur — yazma yolu yalnız §5.16a'dır. §7.6 kabul formundaki zorunlu `steamTradeUrl` alanının ön-doldurma kaynağıdır.

### 5.2 U2 — `GET /users/me/stats`

**Amaç:** Dashboard hızlı istatistikleri (S05).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "completedTransactionCount": 24,
  "successfulTransactionRate": 0.96,
  "reputationScore": 4.8
}
```

### 5.3 U3 — `PUT /users/me/wallet/seller`

**Amaç:** Satıcı ödeme adresi kaydet/güncelle (S08).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |
| Ek Auth | Mevcut adres varsa `X-ReAuth-Token` header zorunlu |

**Request:**
```json
{ "walletAddress": "TNewAddress1234567890abcdef123456" }
```

**Doğrulama:** `walletAddress` merkezi doğrulama pipeline'ından geçer: (1) TRC-20 format geçerliliği (`T` ile başlar, 34 karakter), (2) sanctions screening (02 §12.3). Geçersiz veya yaptırımlı adres → ilgili hata.

**Response (200) `data`:**
```json
{
  "walletAddress": "TNewAddress1234567890abcdef123456",
  "updatedAt": "2026-03-16T14:32:00Z",
  "activeTransactionsUsingOldAddress": 2
}
```

`activeTransactionsUsingOldAddress`: Eski adresle devam eden işlem sayısı (03 §9.2/6).

**Hatalar:** 400 `VALIDATION_ERROR`, 400 `INVALID_WALLET_ADDRESS`, 403 `SANCTIONS_MATCH`, 403 `RE_AUTH_REQUIRED`, 403 `RE_AUTH_TOKEN_INVALID`

### 5.4 U4 — `PUT /users/me/wallet/refund`

**Amaç:** Alıcı iade adresi kaydet/güncelle (S08). U3 ile aynı yapı (aynı doğrulama pipeline'ı: format + sanctions screening).

### 5.5 U5 — `GET /users/:steamId`

**Amaç:** Public profil (S09, S07 C04 user card).

| Konu | Değer |
|------|-------|
| Auth | Public |

**Response (200) `data`:**
```json
{
  "steamId": "76561198012345678",
  "displayName": "PlayerOne",
  "avatarUrl": "https://steamcdn.../abc.jpg",
  "accountAge": "6 ay",
  "reputationScore": 4.8,
  "completedTransactionCount": 24,
  "successfulTransactionRate": 0.96
}
```

**API'de döndürülmez:** cüzdan adresi, iptal oranı, ayarlar. **Frontend'de gösterilmez:** tam Steam ID (URL path parametresi olarak zaten biliniyor, API response'ta döner ancak frontend UI'da göstermez — 04 §7.5).

**Hatalar:** 404 `USER_NOT_FOUND`

### 5.6 U6 — `GET /users/me/settings`

**Amaç:** Hesap ayarları sayfası (S10).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "language": "tr",
  "notifications": {
    "email": { "enabled": true, "address": "user@example.com", "verified": true },
    "telegram": { "enabled": true, "connected": true, "username": "@playerone" },
    "discord": { "enabled": false, "connected": true, "username": "PlayerOne#1234" },
    "platform": { "enabled": true, "canDisable": false }
  }
}
```

`platform.canDisable`: Her zaman `false` — kapatılamaz (04 §7.6).

### 5.7 U15 — `POST /users/me/settings/email/send-verification`

**Amaç:** Email doğrulama kodu gönderme (S10). Email adresi kaydedildikten sonra doğrulama gereklidir.

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "sentTo": "u***@example.com",
  "expiresIn": 600
}
```

`expiresIn`: Kodun geçerlilik süresi (saniye) — 10 dk.

**Hatalar:** 422 `NO_EMAIL_SET` (email adresi henüz tanımlanmamış), 429 `VERIFICATION_COOLDOWN` (çok sık istek)

### 5.8 U16 — `POST /users/me/settings/email/verify`

**Amaç:** Email doğrulama kodunu onaylama (S10).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{ "code": "482910" }
```

**Response (200) `data`:**
```json
{ "verified": true, "verifiedAt": "2026-03-16T14:35:00Z" }
```

**Hatalar:** 400 `INVALID_VERIFICATION_CODE`, 422 `VERIFICATION_CODE_EXPIRED`, 422 `NO_EMAIL_SET`

### 5.9 U7 — `PUT /users/me/settings/notifications`


**Amaç:** Bildirim tercihleri güncelleme (S10).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{
  "email": { "enabled": true, "address": "new@example.com" },
  "telegram": { "enabled": false },
  "discord": { "enabled": true }
}
```

Sadece değiştirilen kanallar gönderilir.

**Response (200):** U6 ile aynı yapıda güncel settings.

**Hatalar:** 400 `VALIDATION_ERROR`, 422 `CHANNEL_NOT_CONNECTED`

### 5.10 U8 — `PUT /users/me/settings/language`

**Amaç:** Dil tercihi güncelleme (S10).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{ "language": "en" }
```

Geçerli değerler: `en`, `zh`, `es`, `tr`

**Response (200) `data`:**
```json
{ "language": "en" }
```

### 5.11 U9 — `POST /users/me/settings/telegram/connect`

**Amaç:** Telegram bağlantısı başlatma (S10).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "verificationCode": "SKN-482910",
  "botUrl": "https://t.me/SkinoraBot",
  "expiresIn": 300
}
```

Bot doğruladığında SignalR (RT2) ile `TelegramConnected` event'i push edilir.

### 5.11b W1 — `POST /webhooks/telegram`

**Amaç:** Telegram Bot API webhook — Telegram'dan gelen update'leri alır (08 §5.2).

| Konu | Değer |
|------|-------|
| Auth | Telegram imzası doğrulaması (secret token header) |
| Kullanım | Telegram `/start` komutu ile kullanıcı-bot bağlantısını tamamlama |
| Çağıran | Telegram sunucuları (dış → platform) |

**Davranış:** Telegram update'i alınır → `/start {verificationCode}` komutu parse edilir → kod doğrulanır → kullanıcıya bağlanır → SignalR `TelegramConnected` push edilir.

**Güvenlik:** Telegram `X-Telegram-Bot-Api-Secret-Token` header'ı ile doğrulama (webhook set edilirken belirtilen secret ile eşleşme kontrolü).

### 5.12 U10 — `POST /users/me/settings/discord/connect`

**Amaç:** Discord OAuth bağlantısı başlatma (S10).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{ "discordAuthUrl": "https://discord.com/api/oauth2/authorize?..." }
```

Frontend bu URL'e yönlendirir.

### 5.13 U10b — `GET /users/me/settings/discord/callback`

**Amaç:** Discord OAuth callback.

| Konu | Değer |
|------|-------|
| Auth | OAuth state correlation (aşağıda açıklanmıştır) |
| Davranış | Discord token al → kullanıcıya bağla → redirect |

**Auth detayı:** Refresh token cookie'si `Path=/api/v1/auth` ile sınırlı olduğundan bu path'e gönderilmez. Bunun yerine Discord OAuth `state` parametresine server-side session correlation token yazılır. Backend callback'te state'i doğrular, içindeki user ID ile mevcut kullanıcıyı bağlar. Bu yaklaşım aynı zamanda CSRF koruması sağlar.

Başarı: redirect `/settings?discord=connected` + SignalR `DiscordConnected` push.

**Hatalar:** Redirect: `?discord=error&reason=denied`, `?discord=error&reason=already_linked`

### 5.14 U11 — `DELETE /users/me/settings/telegram`

**Amaç:** Telegram bağlantısını kaldırma (S10).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:** `null`

Telegram bildirim tercihi otomatik `enabled: false` olur.

### 5.15 U12 — `DELETE /users/me/settings/discord`

**Amaç:** Discord bağlantısını kaldırma (S10). U11 ile aynı yapı.

### 5.16a U17 — `PUT /users/me/settings/steam/trade-url`

**Amaç:** Steam trade URL kaydetme + Mobile Authenticator doğrulaması (03 §2.1 adım 8, 08 §2.2).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{ "tradeUrl": "https://steamcommunity.com/tradeoffer/new/?partner=123456&token=abc123xyz" }
```

**Davranış:**
1. Trade URL parse edilir → `partner` ve `token` çıkarılır
2. `trade_offer_access_token` ile sidecar üzerinden `GetTradeHoldDurations` çağrısı yapılır (A7 otomatik tetiklenir)
3. MA aktif → trade URL kaydedilir, `User.MobileAuthenticatorVerified = true` (06 §3.1)
4. MA aktif değil → trade URL kaydedilir ama `MobileAuthenticatorVerified = false`, kullanıcıya uyarı döner — işlem başlatamaz

**Response (200) `data`:**
```json
{
  "tradeUrl": "https://steamcommunity.com/tradeoffer/new/?partner=123456&token=abc123xyz",
  "mobileAuthenticatorActive": true
}
```

```json
{
  "tradeUrl": "https://steamcommunity.com/tradeoffer/new/?partner=123456&token=abc123xyz",
  "mobileAuthenticatorActive": false,
  "setupGuideUrl": "https://help.steampowered.com/..."
}
```

**Steam API erişilemezse:** Trade URL kaydedilir ama MA doğrulaması pending state'e alınır. Kullanıcıya "MA doğrulaması bekliyor" bilgisi döner. API dönene kadar işlem başlatma bloke (08 §8 fallback kuralı).

**Hatalar:** 422 `INVALID_TRADE_URL` (parse edilemez), 503 `STEAM_API_UNAVAILABLE` (MA kontrolü pending)

### 5.17 U13 — `POST /users/me/deactivate`

**Amaç:** Hesap deaktif etme (S10, 03 §10.1).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "deactivatedAt": "2026-03-16T14:32:00Z",
  "message": "Hesabınız deaktif edildi. Tekrar giriş yaparak aktif edebilirsiniz."
}
```

Oturum sonlandırılır.

**Hatalar:** 422 `HAS_ACTIVE_TRANSACTIONS`

### 5.17 U14 — `DELETE /users/me`

**Amaç:** Hesap kalıcı silme (S10, 03 §10.2).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{ "confirmation": "SİL" }
```

**Response (200) `data`:**
```json
{
  "deletedAt": "2026-03-16T14:32:00Z",
  "message": "Hesabınız silindi. Kişisel verileriniz temizlendi."
}
```

Kişisel veriler temizlenir, işlem geçmişi + AuditLog anonim korunur (03 §10.2). Oturum sonlandırılır.

**Hatalar:** 422 `HAS_ACTIVE_TRANSACTIONS`, 400 `VALIDATION_ERROR`

---

## 6. Steam Endpoints

### 6.1 S1 — `GET /steam/inventory`

**Amaç:** Satıcının Steam envanteri (S06 item picker).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |
| Rate Limit | 5/dk |

**Response (200) `data`:**
```json
{
  "items": [
    {
      "assetId": "27348562891",
      "name": "AK-47 | Redline",
      "type": "Rifle",
      "imageUrl": "https://steamcdn.../abc.png",
      "wear": "Field-Tested",
      "tradeable": true
    }
  ],
  "totalCount": 87,
  "tradeableCount": 62
}
```

`tradeable: false` → S06'da gri/devre dışı. `wear`: varsa string, yoksa `null`.

**Hatalar:** 503 `STEAM_UNAVAILABLE`, 422 `INVENTORY_PRIVATE`

---

## 7. Transaction Endpoints

### 7.1 T1 — `GET /transactions`

**Amaç:** Kullanıcının işlem listesi (S05).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |
| Paginated | Evet |

**Query Params:**

| Param | Açıklama |
|-------|----------|
| `tab` | `active`, `completed`, `cancelled` |

**Tab → Status:**

| Tab | Status'ler |
|-----|-----------|
| `active` | CREATED, ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, FLAGGED, EMERGENCY_HOLD |
| `completed` | COMPLETED |
| `cancelled` | CANCELLED_TIMEOUT, CANCELLED_SELLER, CANCELLED_BUYER, CANCELLED_ADMIN |

> **EMERGENCY_HOLD projection notu:** Backend'de EMERGENCY_HOLD ayrı bir transaction state değildir — herhangi bir aktif state üzerine uygulanan overlay mekanizmasıdır (`IsOnHold` flag + `TimeoutFreezeReason`, 06 §3.5). API response'ta ise `status: "EMERGENCY_HOLD"` olarak **computed status** şeklinde sunulur. Bu projection, frontend'in hold durumunu ayrı bir state gibi işlemesini sağlar. Backend gerçek state `PreviousStatusBeforeHold` field'ında korunur (03 satır 38, 05 §4.5).

**Response (200) `data.items[]`:**
```json
{
  "id": "guid",
  "itemName": "AK-47 | Redline",
  "itemImageUrl": "https://steamcdn.../abc.png",
  "status": "ITEM_ESCROWED",
  "price": "100.00",
  "stablecoin": "USDT",
  "counterparty": {
    "steamId": "76561198099999999",
    "displayName": "BuyerPlayer",
    "avatarUrl": "https://steamcdn.../xyz.jpg"
  },
  "userRole": "seller",
  "activeTimeout": {
    "type": "payment",
    "expiresAt": "2026-03-16T18:00:00Z",
    "remainingSeconds": 7200,
    "warningThresholdPercent": 75
  },
  "createdAt": "2026-03-16T10:00:00Z"
}
```

`counterparty`: Karşı taraf, henüz alıcı yoksa `null`. `activeTimeout`: Aktif countdown, yoksa `null`.

### 7.2 T2 — `POST /transactions`

**Amaç:** Yeni işlem oluşturma (S06, 03 §2.2).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{
  "itemAssetId": "27348562891",
  "stablecoin": "USDT",
  "price": "100.00",
  "paymentTimeoutHours": 24,
  "buyerIdentificationMethod": "STEAM_ID",
  "buyerSteamId": "76561198099999999",
  "sellerWalletAddress": "TXyz1234567890abcdef1234567890ab"
}
```

| Field | Zorunlu | Açıklama |
|-------|---------|----------|
| `itemAssetId` | Evet | Steam asset ID |
| `stablecoin` | Evet | `USDT` veya `USDC` |
| `price` | Evet | String, 2 ondalık |
| `paymentTimeoutHours` | Evet | Admin min-max aralığında |
| `buyerIdentificationMethod` | Evet | `STEAM_ID` veya `OPEN_LINK` |
| `buyerSteamId` | Koşullu | Method=STEAM_ID ise zorunlu |
| `sellerWalletAddress` | Evet | TRC-20 adresi |

**Response (201) `data`:**
```json
{
  "id": "guid",
  "status": "CREATED",
  "inviteUrl": "/transactions/guid",
  "createdAt": "2026-03-16T14:32:00Z"
}
```

> **`inviteUrl` formatı:** STEAM_ID yönteminde `/transactions/{id}`, OPEN_LINK yönteminde opaque token'lı `/invite/{token}` döner (04 §7.2 — enumeration koruması). OPEN_LINK linki §7.5a `GET /transactions/by-invite/:token` ile çözülür. Backend host-agnostic relative path döndürür; mutlak origin'i frontend ekler.

FLAGGED olursa `status: "FLAGGED"` + `flagReason: "PRICE_DEVIATION"` döner.

Response header: `Location: /api/v1/transactions/guid`

**Doğrulama:** `sellerWalletAddress` merkezi doğrulama pipeline'ından geçer: (1) TRC-20 format geçerliliği, (2) sanctions screening (02 §12.3).

**Hatalar:** 400 `VALIDATION_ERROR`, 400 `INVALID_WALLET_ADDRESS`, 403 `SANCTIONS_MATCH`, 422 `CONCURRENT_LIMIT_REACHED`, 422 `CANCEL_COOLDOWN_ACTIVE`, 422 `NEW_ACCOUNT_LIMIT_REACHED`, 422 `MOBILE_AUTHENTICATOR_REQUIRED`, 422 `ITEM_NOT_TRADEABLE`, 422 `PRICE_OUT_OF_RANGE`, 422 `TIMEOUT_OUT_OF_RANGE`, 422 `OPEN_LINK_DISABLED`, 422 `BUYER_STEAM_ID_NOT_FOUND`

### 7.3 T3 — `GET /transactions/eligibility`

**Amaç:** İşlem başlatma uygunluk kontrolü (S06 form öncesi).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "eligible": true,
  "mobileAuthenticatorActive": true,
  "concurrentLimit": { "current": 3, "max": 5 },
  "cancelCooldown": { "active": false, "expiresAt": null },
  "newAccountLimit": { "isNewAccount": false, "current": null, "max": null }
}
```

Uygun değilse `eligible: false` + `reasons: ["CONCURRENT_LIMIT_REACHED"]`. Her zaman 200 döner.

### 7.4 T4 — `GET /transactions/params`

**Amaç:** İşlem oluşturma form parametreleri (S06).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "minPrice": "10.00",
  "maxPrice": "50000.00",
  "commissionRate": 0.02,
  "paymentTimeout": { "minHours": 6, "maxHours": 72, "defaultHours": 24 },
  "openLinkEnabled": false,
  "supportedStablecoins": ["USDT", "USDC"]
}
```

### 7.5 T5 — `GET /transactions/:id`

**Amaç:** İşlem detay (S07). Platformun en karmaşık endpoint'i — state × role'e göre farklı veri döner.

| Konu | Değer |
|------|-------|
| Auth | Public (sınırlı) / Authenticated (tam) |

**Response (200) `data` — authenticated, tam:**
```json
{
  "id": "guid",
  "status": "ITEM_ESCROWED",
  "userRole": "buyer",

  "item": {
    "assetId": "27348562891",
    "name": "AK-47 | Redline",
    "type": "Rifle",
    "imageUrl": "https://steamcdn.../abc.png",
    "wear": "Field-Tested"
  },

  "price": "100.00",
  "stablecoin": "USDT",
  "commissionRate": 0.02,
  "commissionAmount": "2.00",
  "totalAmount": "102.00",

  "seller": {
    "steamId": "76561198012345678",
    "displayName": "SellerPlayer",
    "avatarUrl": "https://steamcdn.../abc.jpg",
    "reputationScore": 4.8,
    "completedTransactionCount": 24
  },
  "buyer": {
    "steamId": "76561198099999999",
    "displayName": "BuyerPlayer",
    "avatarUrl": "https://steamcdn.../xyz.jpg",
    "reputationScore": 4.2,
    "completedTransactionCount": 8
  },

  "timeout": {
    "type": "payment",
    "expiresAt": "2026-03-16T18:00:00Z",
    "remainingSeconds": 7200,
    "warningThresholdPercent": 75,
    "frozen": false,
    "frozenReason": null,
    "frozenAt": null
  },

  "payment": {
    "address": "TPaymentAddr1234567890abcdef1234",
    "expectedAmount": "102.00",
    "stablecoin": "USDT",
    "network": "Tron (TRC-20)",
    "status": null,
    "txHash": null,
    "confirmedAt": null
  },

  "sellerPayout": null,
  "refund": null,
  "cancelInfo": null,
  "flagInfo": null,
  "holdInfo": null,
  "dispute": null,
  "inviteInfo": null,
  "paymentEvents": [],

  "escrowBotAssetId": null,
  "deliveredBuyerAssetId": null,
  "steamTradeOfferUrl": null,

  "availableActions": {
    "canAccept": false,
    "canCancel": true,
    "canDispute": false,
    "canEscalate": false
  },

  "createdAt": "2026-03-16T10:00:00Z",
  "updatedAt": "2026-03-16T12:30:00Z"
}
```

**Koşullu bölümler (state'e göre dolar veya `null`):**

| Bölüm | Ne zaman dolar |
|--------|---------------|
| `buyer` | ACCEPTED'dan itibaren |
| `timeout` | Aktif timeout varsa. Terminal state'lerde `null`. Freeze durumunda `frozen: true` + `frozenReason` + `frozenAt` dolar |
| `payment` | ITEM_ESCROWED'dan itibaren |
| `payment.txHash` | PAYMENT_RECEIVED'dan itibaren |
| `sellerPayout` | COMPLETED'da (satıcı view) |
| `refund` | CANCELLED_* + ödeme iadesi varsa |
| `cancelInfo` | CANCELLED_* state'lerde |
| `flagInfo` | FLAGGED state'te |
| `dispute` | Aktif dispute varsa |
| `holdInfo` | EMERGENCY_HOLD state'inde |
| `inviteInfo` | CREATED, satıcı, alıcı kayıtlı değilse |
| `paymentEvents` | ITEM_ESCROWED'dan itibaren — ödeme edge case olayları (eksik/fazla/yanlış tutar, gecikmeli ödeme) |
| `escrowBotAssetId` | ITEM_ESCROWED'dan itibaren — bot envanterine alınan asset ID |
| `deliveredBuyerAssetId` | COMPLETED'da — alıcıya teslim edilen asset ID |
| `steamTradeOfferUrl` | TRADE_OFFER_SENT_TO_SELLER / TRADE_OFFER_SENT_TO_BUYER state'lerinde — kullanıcının "Steam'e git" CTA'sı için Steam trade offer URL'i (04 §7.3). Diğer state'lerde + public view'de `null` (WP12) |

> **Not:** Steam trade sonrası asset ID değişir — `escrowBotAssetId` ve `deliveredBuyerAssetId` field'ları audit ve dispute doğrulaması için döndürülür (06 §8.4).

**`paymentEvents` (ITEM_ESCROWED+, 04 §7.3 S07 banner'ları):**
```json
[
  {
    "type": "INCORRECT_AMOUNT",
    "receivedAmount": "50.00",
    "expectedAmount": "102.00",
    "refundTxHash": "abc123...",
    "occurredAt": "2026-03-16T15:10:00Z"
  }
]
```

| `type` değerleri | Açıklama | S07 banner |
|-----------------|----------|-----------|
| `INCORRECT_AMOUNT` | Eksik tutar gönderildi, iade edildi | Uyarı banner |
| `EXCESS_AMOUNT` | Fazla tutar gönderildi, fazlası iade edildi | Bilgi banner |
| `WRONG_TOKEN` | Yanlış token gönderildi, iade edildi | Uyarı banner |
| `LATE_PAYMENT` | İptal sonrası gecikmeli ödeme, iade edildi | Bilgi banner (CANCELLED state) |

Olay yoksa boş array `[]` döner.

**`sellerPayout` (COMPLETED):**
```json
{
  "grossAmount": "100.00",
  "gasFee": "0.50",
  "gasFeeFromCommission": "0.20",
  "gasFeeFromSeller": "0.30",
  "netAmount": "99.70",
  "walletAddress": "TXyz1234567890abcdef1234567890ab",
  "txHash": "abc123def456...",
  "sentAt": "2026-03-16T17:00:00Z"
}
```

**`refund` (CANCELLED_* + ödeme iadesi):**
```json
{
  "originalAmount": "102.00",
  "gasFee": "0.30",
  "netRefundAmount": "101.70",
  "refundAddress": "TAbcdef1234567890abcdef12345678cd",
  "txHash": "def789ghi012...",
  "refundedAt": "2026-03-16T19:00:00Z"
}
```

**`cancelInfo` (CANCELLED_*):**
```json
{
  "cancelledBy": "SELLER",
  "reason": "Fiyat konusunda anlaşamadık",
  "cancelledAt": "2026-03-16T15:00:00Z",
  "itemReturned": true,
  "paymentRefunded": false
}
```

**`flagInfo` (FLAGGED):**
```json
{
  "flagType": "PRICE_DEVIATION",
  "message": "İşleminiz incelemeye alındı. Sonuç size bildirilecektir."
}
```

**`holdInfo` (EMERGENCY_HOLD):**
```json
{
  "previousStatus": "PAYMENT_RECEIVED",
  "reason": "Sanctions eşleşmesi tespit edildi",
  "frozenAt": "2026-03-20T10:00:00Z",
  "message": "İşleminiz güvenlik incelemesi nedeniyle donduruldu. Süreç admin tarafından yönetilmektedir."
}
```

EMERGENCY_HOLD state'inde `timeout.frozen: true`, `timeout.frozenReason: "EMERGENCY_HOLD"` döner. Tüm `availableActions` `false` olur — kullanıcı hiçbir aksiyon alamaz.

**Kanonik `frozenReason` değerleri (timeout freeze):**

| Değer | Tetikleyici | Freeze kalktığında |
|-------|-------------|-------------------|
| `MAINTENANCE` | Aktif platform bakımı (P2 `type: PLATFORM_MAINTENANCE`) | Bakım sona erdiğinde otomatik — `expiresAt` bakım süresi kadar ileri kaydırılır |
| `STEAM_OUTAGE` | Steam kesintisi (P2 `type: STEAM_OUTAGE`) | Steam servisleri düzeldiğinde otomatik — `expiresAt` kesinti süresi kadar ileri kaydırılır |
| `BLOCKCHAIN_DEGRADATION` | Blockchain altyapısı degradasyonu (node/indexer erişim kaybı) | Altyapı düzeldiğinde otomatik — yalnızca ödeme adımındaki işlemlerin timeout'ları etkilenir |
| `EMERGENCY_HOLD` | Admin emergency hold (AD19b) | Admin release-hold (AD19c) — `expiresAt` hold öncesi kalan süre kadar ileri kaydırılır |

> **Not:** Enum değerleri 06 §2.20 `TimeoutFreezeReason` ile birebir aynıdır (K10).

Freeze semantiği: Freeze süresince `remainingSeconds` azalmaz. Freeze kalktığında `expiresAt` freeze süresi kadar ileri kaydırılır (kullanıcının kalan süresi korunur). RT1 `CountdownSync` event'i freeze başlangıcında ve bitişinde push edilir.

**`dispute` (aktif):**
```json
{
  "id": "dispute-guid",
  "type": "PAYMENT",
  "status": "OPEN",
  "autoCheckResult": "Blockchain üzerinde ödeme bulunamadı",
  "canSubmitTxHash": true,
  "canEscalate": true,
  "createdAt": "2026-03-16T16:00:00Z"
}
```

**`inviteInfo` (CREATED, satıcı):**
```json
{
  "inviteUrl": "/invite/{token}",
  "buyerRegistered": false,
  "buyerNotified": false
}
```

> `inviteUrl` relative path'tir: OPEN_LINK'te `/invite/{token}` (§7.5a ile çözülür), STEAM_ID'de `/transactions/{id}`. Mutlak origin frontend tarafından eklenir.

**`availableActions` kuralları:**

| Aksiyon | Koşul |
|---------|-------|
| `canAccept` | buyer + CREATED + Steam ID eşleşme (veya açık link) |
| `canConfirmReady` | **(v3.0)** seller + ACCEPTED — satıcı hazırlık onayı (T6a) |
| `canConfirmReceipt` | **(v3.0)** buyer + PAYMENT_RECEIVED — alıcı teslim onayı (T6b) |
| `canCancel` | seller: aktif state (PAYMENT_RECEIVED dâhil — 02 §7) · buyer: aktif state **ve** ödeme gönderilmemiş |
| `canDispute` | buyer + {SELLER_CONFIRMED, PAYMENT_RECEIVED, ITEM_DELIVERED} + aktif dispute yok + aynı tür daha önce açılmamış |
| `disputableTypes` | (WP5) buyer + aktif dispute yok iken işlemin mevcut state'inde açılabilen dispute türleri: `DisputeType[]` (PAYMENT→{SELLER_CONFIRMED, PAYMENT_RECEIVED}, DELIVERY→{PAYMENT_RECEIVED, ITEM_DELIVERED}, WRONG_ITEM→{PAYMENT_RECEIVED, ITEM_DELIVERED}). `canDispute` = `disputableTypes` boş değil. Public/prospective/hold zarflarında omit. |
| `canEscalate` | dispute var + otomatik kontrol tamamlanmış + henüz eskalasyon yok |

> **v3.0 — `canCancel` artık role göre asimetriktir.** Ödeme gönderildikten sonra alıcı iptal edemez ama satıcı edebilir (item göndermekten vazgeçme hakkı). Önceki tek koşullu kural her iki tarafı da bloklardı.
>
> **v3.0 — `WRONG_ITEM` dispute'una `PAYMENT_RECEIVED` eklendi.** Satıcı farklı bir item gönderirse beklenen sınıfın sayısı artmayacağı için işlem `ITEM_DELIVERED`'a hiç ulaşmaz; alıcının bu durumda da yanlış item itirazı açabilmesi gerekir (02 §10.1, 03 §6.3).

> **EMERGENCY_HOLD kısıtlaması:** EMERGENCY_HOLD state'inde tüm `availableActions` `false` döner. Kullanıcı hiçbir aksiyon alamaz — işlem admin tarafından yönetilir.

**Public varyant (unauthenticated):**
```json
{
  "id": "guid",
  "status": "CREATED",
  "userRole": null,
  "item": { "name": "AK-47 | Redline", "imageUrl": "https://steamcdn.../abc.png" },
  "price": "100.00",
  "stablecoin": "USDT",
  "seller": { "displayName": "SellerPlayer" },
  "availableActions": { "canAccept": false, "requiresLogin": true }
}
```

**Hatalar:** 404 `TRANSACTION_NOT_FOUND`, 403 `NOT_A_PARTY`

### 7.5a T5a — `GET /transactions/by-invite/:token`

**Amaç:** OPEN_LINK davet linkini (opaque token) çözer ve S07 public-invite kabul yüzeyini döndürür (04 §7.3 public varyant, 03 §3.2). Token enumeration-safe olduğundan link işlem ID'sini sızdırmaz. Literal `by-invite` segmenti `{id:guid}` detay route'u ile çakışmaz.

**Yetki:** Public + authenticated (token erişim anahtarıdır).

**Davranış (rol çözümü §7.5'ten farklı):**
- **Unauthenticated:** §7.5 ile aynı trimlenmiş public shape (`userRole: null`, `availableActions.requiresLogin: true`). "Giriş Yap ve Kabul Et" CTA aynı `/invite/:token`'a döner (giriş sonrası locale eklenir).
- **Authenticated, taraf değil, davet hâlâ açık (CREATED + alıcı yok):** *prospective buyer* — tam kabul yüzeyi (`userRole: "buyer"`, `availableActions.canAccept: true`; `canCancel`/`canDispute` null, çünkü bunlar gerçek taraflara aittir). Kabul yine ID-bazlı `POST /transactions/:id/accept` ile yapılır; 02 §6.2 ilk-gelen-alır kuralını acceptance servisi uygular.
- **Authenticated satıcı:** satıcı görünümü ("alıcı bekleniyor" + davet linki bloğu).
- **Harcanmış / kabul edilmiş davet, taraf değil:** trimlenmiş public shape (FE "davet artık geçerli değil" gösterir); taraf olan alıcı/satıcı canonical `/transactions/:id`'e yönlendirilir.

**Response (200):** §7.5 ile aynı `TransactionDetail` şeması.

**Hatalar:** 404 `TRANSACTION_NOT_FOUND` (geçersiz/bilinmeyen/boş token)

### 7.6 T6 — `POST /transactions/:id/accept`

**Amaç:** Alıcı işlemi kabul eder (S07, 03 §3.2).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{
  "refundWalletAddress": "TAbcdef1234567890abcdef12345678cd",
  "steamTradeUrl": "https://steamcommunity.com/tradeoffer/new/?partner=123456789&token=AbCdEfGh"
}
```

`steamTradeUrl`: **Zorunlu (v3.0)**. Satıcı item'ı doğrudan bu adrese göndereceği için gereklidir (02 §2.2 adım 6). Kullanıcının profilinde kayıtlı trade URL'i varsa istemci tarafından ön-doldurulur — kaynak §5.1 `GET /users/me` yanıtındaki `steamTradeUrl` alanıdır. Sunucuda profil fallback'i **yoktur**: değer yalnız istekten okunur ve normalize edilmiş biçimiyle `Transaction.BuyerTradeUrl`'e yazılır (06 §3.5).

**Response (200) `data`:**
```json
{ "status": "ACCEPTED", "acceptedAt": "2026-03-16T14:45:00Z" }
```

**Doğrulama (sıra bağlayıcıdır — MA kontrolü tek dış çağrı olduğu için en sondadır):**
1. `refundWalletAddress` merkezi doğrulama pipeline'ından geçer: TRC-20 format geçerliliği + sanctions screening (02 §12.3)
2. `steamTradeUrl` format doğrulaması (partner + token parametreleri ayrıştırılabilmeli) **ve sahiplik doğrulaması**: `partner` değeri kabul eden alıcının kendi SteamID64'üne çözülmelidir (`partner = SteamID64 − 76561197960265728`). Gerekçe: P2P'de item'ın hedefini belirleyen tek alan budur — başkasının trade URL'i verilirse satıcı item'ı yabancıya gönderir, para yine satıcıya akar. İki durum da 400 `INVALID_TRADE_URL` döner
3. **Alıcının Mobile Authenticator'ı doğrulanır** (v3.0, 02 §9.1) — Steam `GetTradeHoldDurations` ile hold süresi 0 değilse kabul reddedilir. Gerekçe: MA aktif değilse satıcının göndereceği trade 15 gün Steam escrow'una düşer. Sorgu için gereken `trade_offer_access_token` **isteğin gövdesindeki URL'den** ayrıştırılır (profilden değil). Steam'e ulaşılamazsa **fail-closed** davranılır (08 §2.2): kabul edilmez, 503 `STEAM_UNAVAILABLE` döner — 403 `MOBILE_AUTHENTICATOR_REQUIRED` değil, çünkü alıcının MA'sında bir sorun olmayabilir ve düzeltemeyeceği bir işe yönlendirilmemelidir. §7.6a aynı durumu aynı kodla karşılar

**Hatalar:** 409 `INVALID_STATE_TRANSITION`, 403 `STEAM_ID_MISMATCH`, 409 `ALREADY_ACCEPTED`, 400 `VALIDATION_ERROR`, 400 `INVALID_WALLET_ADDRESS`, 400 `INVALID_TRADE_URL` *(v3.0 — format veya sahiplik)*, 403 `MOBILE_AUTHENTICATOR_REQUIRED` *(v3.0)*, 503 `STEAM_UNAVAILABLE` *(v3.0 — MA doğrulanamadı, tekrar denenebilir)*, 403 `SANCTIONS_MATCH`, 403 `WALLET_CHANGE_COOLDOWN_ACTIVE`, 403 `ACCOUNT_FLAGGED` (hesap-flag accept gate, 02 §14.0 — WP4a), 403 `NOT_A_PARTY` (OPEN_LINK'te satıcı kendi listesini kabul edemez, 02 §6.2)

> **Not (T119a):** `INVALID_TRADE_URL` bu uçta **400**, §5.16a (U17 profil kaydı) ucunda **422** döner. İki ucun statüsü bilinçli olarak farklı bırakılmıştır (her biri kendi bölümünde tanımlıdır); ortaklaştırma T133a doküman turunun konusudur.

### 7.6a T6a — `POST /transactions/:id/confirm-ready` *(v3.0 — yeni)*

**Amaç:** Satıcı item'ı göndermeye hazır olduğunu onaylar; ödeme adresi alıcıya bu adımdan sonra açılır (S07, 03 §2.3).

| Konu | Değer |
|------|-------|
| Auth | Authenticated (yalnız satıcı) |

**Request:** gövde yok.

**Response (200) `data`:**
```json
{ "status": "SELLER_CONFIRMED", "sellerReadyConfirmedAt": "2026-03-16T14:50:00Z", "paymentDeadline": "2026-03-16T16:50:00Z" }
```

**Doğrulama (üçü de geçmeden ilerlenmez, 03 §2.3):**
1. Item hâlâ satıcının envanterinde ve tradeable mı — envanter **önbelleksiz** okunur
2. Alıcının Mobile Authenticator'ı hâlâ aktif mi
3. Alıcının envanteri okunabiliyorsa teslimat doğrulaması için referans anlık görüntü (baseline) alınır. Okunamıyorsa işlem bloklanmaz; envanter kanıtı yolu kapanır ve yanıtta `buyerInventoryVisible: false` döner (02 §9.2)

**Hatalar:** 409 `INVALID_STATE_TRANSITION`, 403 `NOT_A_PARTY`, 409 `ITEM_NO_LONGER_AVAILABLE` *(item envanterde yok veya artık tradeable değil)*, 403 `BUYER_MOBILE_AUTHENTICATOR_INACTIVE`, 503 `STEAM_UNAVAILABLE`

### 7.6b T6b — `POST /transactions/:id/confirm-receipt` *(v3.0 — yeni)*

**Amaç:** Alıcı item'ı teslim aldığını onaylar (S07, 03 §3.5).

| Konu | Değer |
|------|-------|
| Auth | Authenticated (yalnız alıcı) |

**Request:** gövde yok.

**Response (200) `data`:**
```json
{ "status": "ITEM_DELIVERED", "deliveryVerifiedAt": "2026-03-16T15:10:00Z", "evidence": ["BUYER_CONFIRMED"] }
```

**Notlar:**
- Onay alıcının kendi aleyhinedir (onaylayınca ödeme satıcıya gider), bu yüzden tek başına yeterli kanıttır (06 §2.24)
- **İdempotenttir** — zaten `ITEM_DELIVERED` olan bir işlemde tekrar çağrılırsa 200 ve mevcut durum döner
- Onay sonrası ödeme, bekleme penceresi dolduğunda gönderilir (02 §4.5)

**Hatalar:** 409 `INVALID_STATE_TRANSITION`, 403 `NOT_A_PARTY`

### 7.7 T7 — `POST /transactions/:id/cancel`

**Amaç:** İşlem iptali — satıcı veya alıcı (S07, 03 §2.5, §3.3).

| Konu | Değer |
|------|-------|
| Auth | Authenticated (taraf) |

**Request:**
```json
{ "reason": "Fiyat konusunda anlaşamadık" }
```

`reason`: Zorunlu, min 10 karakter.

**Response (200) `data`:**
```json
{
  "status": "CANCELLED_SELLER",
  "cancelledAt": "2026-03-16T15:00:00Z",
  "paymentRefunded": false
}
```

> **v3.0:** `itemReturned` alanı kaldırıldı — item hiçbir zaman platformda bulunmadığı için iade edilecek eşya yoktur (02 §9).

**İptal yetkisi (v3.0, 02 §7):**

| Durum | Satıcı | Alıcı |
|---|---|---|
| CREATED / ACCEPTED / SELLER_CONFIRMED | ✓ | ✓ |
| PAYMENT_RECEIVED | ✓ — item göndermekten vazgeçer, para alıcıya iade edilir, itibar cezası uygulanır | ✗ `PAYMENT_ALREADY_SENT` |

**Hatalar:** 422 `PAYMENT_ALREADY_SENT` *(yalnız alıcı için)*, 409 `INVALID_STATE_TRANSITION`, 403 `NOT_A_PARTY`, 400 `VALIDATION_ERROR`

### 7.8 T8 — `POST /transactions/:id/disputes`

**Amaç:** Dispute açma — sadece alıcı (S07, 03 §6.1-6.3).

| Konu | Değer |
|------|-------|
| Auth | Authenticated (alıcı) |

**Request:**
```json
{ "type": "PAYMENT" }
```

`type`: `PAYMENT`, `DELIVERY`, `WRONG_ITEM`

**Response (200) `data`:**
```json
{
  "id": "dispute-guid",
  "type": "PAYMENT",
  "status": "OPEN",
  "autoCheckResult": {
    "resolved": false,
    "message": "Blockchain üzerinde ödeme bulunamadı",
    "canSubmitTxHash": true,
    "canEscalate": true
  },
  "createdAt": "2026-03-16T16:00:00Z"
}
```

Sistem otomatik kontrol yapar ve sonucu döner. `autoCheckResult.resolved: true` ise dispute anında çözülmüş demektir.

> **Lokalizasyon (WP17):** `autoCheckResult.message` (§7.8), `checkResult.message` (§7.9) ve escalate `message` (§7.10) örneklerde Türkçe gösterilir ama alanlar **itiraz açan alıcının `PreferredLanguage`'ine göre** lokalize edilir (en/tr/es/zh, EN fallback — `DisputeAutoCheckMessages`).

**Hatalar:** 403 `NOT_BUYER`, 409 `INVALID_STATE_TRANSITION`, 409 `DUPLICATE_DISPUTE`

> **Not (WP5):** `ACTIVE_DISPUTE_EXISTS` bu listeden kaldırıldı — 03 §6 farklı türde eşzamanlı aktif dispute'a bilinçli izin verir, dolayısıyla bu kod tasarım gereği erişilemezdir (yalnızca aynı türün tekrarı `DUPLICATE_DISPUTE` ile engellenir).

### 7.9 T9 — `POST /transactions/:id/disputes/:disputeId/submit-txhash`

**Amaç:** Ödeme itirazında TX hash ile yeniden doğrulama (S07, 03 §6.1/4).

| Konu | Değer |
|------|-------|
| Auth | Authenticated (alıcı) |

**Request:**
```json
{ "txHash": "abc123def456789..." }
```

**Response (200) `data`:**
```json
{
  "checkResult": {
    "resolved": true,
    "message": "Ödemeniz doğrulandı, işlem devam ediyor"
  }
}
```

**Hatalar:** 422 `NOT_PAYMENT_DISPUTE`, 400 `VALIDATION_ERROR`, 409 `DISPUTE_CLOSED`

### 7.10 T10 — `POST /transactions/:id/disputes/:disputeId/escalate`

**Amaç:** Dispute'u admin'e iletme (S07, 03 §6.4).

| Konu | Değer |
|------|-------|
| Auth | Authenticated (alıcı) |

**Request:**
```json
{ "detail": "Ödemeyi gönderdim ama sistem hala görmüyor..." }
```

`detail`: Zorunlu, min 10 karakter.

**Response (200) `data`:**
```json
{
  "status": "ESCALATED",
  "escalatedAt": "2026-03-16T16:30:00Z",
  "message": "İtirazınız admin ekibine iletildi"
}
```

**Hatalar:** 409 `ALREADY_ESCALATED`, 409 `DISPUTE_CLOSED`, 400 `VALIDATION_ERROR`

### 7.11 T11 — `POST /transactions/:id/report-payout-issue`

**Amaç:** Satıcı payout sorununu bildirme (02 §10.3). İşlem COMPLETED state'inde olmalı.

| Konu | Değer |
|------|-------|
| Auth | Authenticated (satıcı) |

**Request:**
```json
{ "detail": "İşlem tamamlandı ancak ödeme cüzdanıma ulaşmadı" }
```

`detail`: Zorunlu, min 10 karakter.

**Response (201) `data`:**
```json
{
  "issueId": "guid",
  "status": "REPORTED",
  "createdAt": "2026-03-20T14:00:00Z",
  "message": "Payout sorununuz kaydedildi. Sistem tx hash doğrulaması yapacak."
}
```

`status` değeri `PayoutIssueStatus` enum'unu takip eder (06 §2.22):

| Değer | Açıklama |
|-------|----------|
| `REPORTED` | Satıcı bildirdi, henüz doğrulama başlamadı |
| `VERIFYING` | Sistem blockchain tx hash doğrulaması yapıyor |
| `RETRY_SCHEDULED` | Payout retry planlandı |
| `ESCALATED` | Otomatik çözüm başarısız, admin'e eskalasyon |
| `RESOLVED` | Sorun çözüldü |

**Otomatik akış:** Bildirim sonrası sistem payout tx hash'ini blockchain üzerinden doğrular. Blockchain'de onaylıysa satıcıya tx hash gösterilir. Sorun tespit edilirse admin'e eskale edilir (03 §2.4a Senaryo A).

> **Not:** Bu endpoint yalnızca COMPLETED işlemler içindir. ITEM_DELIVERED state'inde stuck payout durumunda sistem otomatik retry yapar (exponential backoff, 3 deneme — 06 §3.8). Satıcının ayrıca bildirim yapmasına gerek yoktur (03 §2.4a Senaryo B).

**Hatalar:** 409 `TRANSACTION_NOT_COMPLETED`, 409 `ISSUE_ALREADY_REPORTED`, 403 `NOT_SELLER`, 400 `VALIDATION_ERROR`

---

## 8. Notification Endpoints

### 8.1 N1 — `GET /notifications`

**Amaç:** Bildirim listesi (S11).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |
| Paginated | Evet (varsayılan 20) |

**Response (200) `data.items[]`:**
```json
{
  "id": "notif-guid",
  "type": "BUYER_ACCEPTED",
  "message": "Alıcı işlemi kabul etti",
  "targetType": "transaction",
  "targetId": "transaction-guid",
  "isRead": false,
  "createdAt": "2026-03-16T14:45:00Z"
}
```

**Bildirim `type` değerleri (06 §2.13 ile birebir):**

| type | Hedef | Açıklama | targetType |
|------|-------|----------|------------|
| `TRANSACTION_INVITE` | Alıcı | Yeni işlem daveti | transaction |
| `BUYER_ACCEPTED` | Satıcı | Alıcı kabul etti | transaction |
| `ITEM_ESCROWED` | Alıcı | Item emanete alındı | transaction |
| `PAYMENT_RECEIVED` | Satıcı | Ödeme doğrulandı | transaction |
| `TRADE_OFFER_SENT_TO_BUYER` | Alıcı | Item gönderildi, trade offer'ı kabul et | transaction |
| `TRANSACTION_COMPLETED` | Her ikisi | İşlem tamamlandı | transaction |
| `SELLER_PAYMENT_SENT` | Satıcı | Ödeme cüzdana gönderildi | transaction |
| `TIMEOUT_WARNING` | İlgili taraf | Süre dolmak üzere | transaction |
| `TRANSACTION_CANCELLED` | Her ikisi | İşlem iptal oldu | transaction |
| `ITEM_RETURNED` | Satıcı | İptal/timeout sonrası item iade edildi | transaction |
| `PAYMENT_REFUNDED` | Alıcı | İptal/timeout sonrası ödeme iade edildi | transaction |
| `PAYMENT_INCORRECT` | Alıcı | Eksik/fazla/yanlış ödeme | transaction |
| `LATE_PAYMENT_REFUNDED` | Alıcı | Gecikmeli ödeme iade edildi | transaction |
| `TRANSACTION_FLAGGED` | Satıcı | İşlem incelemeye alındı | transaction |
| `FLAG_RESOLVED` | Satıcı | Flag sonuçlandı (onay veya red) | transaction |
| `DISPUTE_RESULT` | Alıcı | Dispute sonucu | transaction |
| `ADMIN_FLAG_ALERT` | Admin | Flag'lenmiş işlem | flag |
| `ADMIN_ESCALATION` | Admin | Yeni dispute eskalasyonu | transaction |
| `ADMIN_PAYMENT_FAILURE` | Admin | Satıcıya ödeme gönderim hatası | transaction |
| `ADMIN_STEAM_BOT_ISSUE` | Admin | Platform Steam hesabı sorunu | null |

`targetType`: Frontend route mapping için. `null` → tıklama yönlendirmez.

### 8.2 N2 — `GET /notifications/unread-count`

**Amaç:** Okunmamış bildirim sayısı (S05 header badge).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{ "unreadCount": 3 }
```

### 8.3 N3 — `POST /notifications/mark-all-read`

**Amaç:** Tümünü okundu işaretle (S11).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{ "markedCount": 3 }
```

### 8.4 N4 — `PUT /notifications/:id/read`

**Amaç:** Tek bildirim okundu (S11).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:** `null`

**Hatalar:** 404 `NOTIFICATION_NOT_FOUND`, 403 `FORBIDDEN`

---

## 9. Admin Endpoints

Tüm admin endpoint'leri `Authenticated + Admin rolü` gerektirir. Her endpoint kendi permission'ını kontrol eder.

**Permission listesi:**

| Permission | Açıklama |
|-----------|----------|
| `VIEW_FLAGS` | Flag'leri görüntüle |
| `MANAGE_FLAGS` | Flag onayla/reddet |
| `VIEW_TRANSACTIONS` | İşlemleri görüntüle |
| `MANAGE_SETTINGS` | Parametreleri yönet (AD9) + bakım/kesinti kontrolü (AD30, AD31) |
| `VIEW_STEAM_ACCOUNTS` | Steam hesaplarını görüntüle |
| `VIEW_USERS` | Kullanıcı detay görüntüle |
| `MANAGE_ROLES` | Rolleri yönet (süper admin) |
| `VIEW_AUDIT_LOG` | Audit log görüntüle |
| `CANCEL_TRANSACTIONS` | İşlemleri iptal et |
| `EMERGENCY_HOLD` | İşlemleri acil dondurma/kaldırma (AD19b, AD19c) |
| `VIEW_DISPUTES` | İtiraz kuyruğunu görüntüle (AD27, AD28) |
| `MANAGE_DISPUTES` | İtirazları çöz (AD29) |

### 9.1 AD1 — `GET /admin/dashboard`

**Amaç:** Admin dashboard özet (S12). Permission: herhangi bir admin.

**Response (200) `data`:**
```json
{
  "summaryCards": {
    "activeTransactions": 42,
    "pendingFlags": 5,
    "dailyCompleted": 18,
    "weeklyCompleted": 128
  },
  "steamAccounts": [
    {
      "id": "guid",
      "name": "Platform Hesap 1",
      "status": "ACTIVE",
      "escrowedItemCount": 12,
      "dailyTradeOfferCount": 45
    }
  ],
  "recentFlags": [
    {
      "id": "flag-guid",
      "transactionId": "tx-guid",
      "type": "PRICE_DEVIATION",
      "reviewStatus": "PENDING",
      "createdAt": "2026-03-16T13:00:00Z"
    }
  ]
}
```

`steamAccounts[].status`: `ACTIVE`, `RESTRICTED`, `BANNED`, `OFFLINE` (06 §2.15). `recentFlags`: Son 5 flag.

### 9.2 AD2 — `GET /admin/flags`

**Amaç:** Flag listesi (S13). Permission: `VIEW_FLAGS`. Paginated.

**Query Params:** `scope`, `type`, `reviewStatus`, `dateFrom`, `dateTo`, `sortBy`, `sortOrder`

`scope` (04 §8.2 "Flag kategorisi" filtresi — T100): `ACCOUNT_LEVEL` | `TRANSACTION_PRE_CREATE` (06 §2.21). Boş bırakılırsa tüm kategoriler döner; sunucu tarafı filtre olduğu için sayfalama + `totalCount` kategori seçiminde tutarlı kalır.

**Response (200) `data.items[]`:**
```json
{
  "id": "flag-guid",
  "transactionId": "tx-guid",
  "type": "PRICE_DEVIATION",
  "reviewStatus": "PENDING",
  "seller": { "steamId": "...", "displayName": "...", "avatarUrl": "..." },
  "itemName": "AK-47 | Redline",
  "price": 100.00,
  "stablecoin": "USDT",
  "marketPrice": 50.00,
  "createdAt": "2026-03-16T13:00:00Z"
}
```

Ek field: `pendingCount` — bekleyen flag sayısı (badge).

> **Hesap-flag kolonları (T100a — 04 §8.2):** `scope = ACCOUNT_LEVEL` satırlarında üç ek alan dolar (işlem flag'lerinde `null`): `signalSummary` (eşleşen ham tanımlayıcı — MULTI_ACCOUNT için cüzdan adresi, ABNORMAL_BEHAVIOR için patern; çevrilebilir değildir, frontend yalnız kolonu etiketler; tam IP/cihaz kanıtı AD3 `supportingSignals`'tedir), `linkedAccountCount` (MULTI_ACCOUNT eşleşen hesap sayısı; tipte yoksa `null`), `activeTransactionCount` (kullanıcının aktif işlem sayısı — AD3 `activeTransactions` ile aynı predikat).

> **Para alanları (T100 netleştirme):** AD2/AD3 flag yüzeyindeki para alanları (`price`, `marketPrice`, `flagDetail` sayısal alanları) JSON **number** olarak serialize olur (`decimal` DTO, kayıtlı `flagDetail` JSON'u zaten number). Bu, işlem (S07/S15) DTO'larındaki `string Price` (scale-6 string) konvansiyonundan **farklıdır** — flag fiyatları 2 ondalıklı item fiyatları olduğundan double precision riski ihmal edilebilir; flag yüzeyi kendi içinde tutarlıdır.

### 9.3 AD3 — `GET /admin/flags/:id`

**Amaç:** Flag detay (S14). Permission: `VIEW_FLAGS`.

**Response (200) `data`:**
```json
{
  "id": "flag-guid",
  "userId": "flagged-user-guid",
  "type": "PRICE_DEVIATION",
  "reviewStatus": "PENDING",
  "createdAt": "2026-03-16T13:00:00Z",

  "flagDetail": {
    "inputPrice": 100.00,
    "marketPrice": 50.00,
    "deviationPercent": 100.0
  },

  "transaction": {
    "id": "tx-guid",
    "status": "FLAGGED",
    "itemName": "AK-47 | Redline",
    "itemImageUrl": "https://steamcdn.../abc.png",
    "price": 100.00,
    "stablecoin": "USDT",
    "paymentTimeoutHours": 24,
    "createdAt": "2026-03-16T12:55:00Z"
  },

  "seller": {
    "steamId": "...", "displayName": "...", "avatarUrl": "...",
    "reputationScore": 4.8, "completedTransactionCount": 24, "accountAge": "6 ay"
  },
  "buyer": null,

  "historicalTransactionCount": 2,

  "activeTransactions": [
    {
      "id": "tx-guid",
      "status": "PAYMENT_RECEIVED",
      "itemName": "AWP | Asiimov",
      "price": 80.00,
      "stablecoin": "USDT",
      "role": "SELLER",
      "isOnHold": false,
      "createdAt": "2026-03-16T11:00:00Z"
    }
  ],

  "reviewedBy": null,
  "reviewedAt": null,
  "adminNote": null
}
```

> **`activeTransactions` (T100a — 04 §8.3 hesap-flag madde 4):** Flag'lenen kullanıcının aktif (terminal-olmayan) işlemleri; sayı = liste uzunluğu. "Aktif" tanımı AD19d (§9.22a) ile birebir: her iki taraf (`role` ∈ `SELLER` | `BUYER`), beş terminal durum (`COMPLETED`, `CANCELLED_TIMEOUT`/`_SELLER`/`_BUYER`/`_ADMIN`) hariç, `FLAGGED` dahil. `isOnHold = true` satırlar hâlâ aktiftir (listede kalır) ama bir sonraki toplu Hold'un (idempotent) atlayacağı satırları gösterir. Tüm flag türleri için döner; öncelikle hesap-flag S14 varyantı tüketir.

**`flagDetail` türe göre:**

| Tür | Yapı |
|-----|------|
| PRICE_DEVIATION | `{ inputPrice, marketPrice, deviationPercent }` |
| HIGH_VOLUME | `{ periodHours, transactionCount, totalVolume }` |
| ABNORMAL_BEHAVIOR | `{ pattern, description }` |
| MULTI_ACCOUNT | `{ matchType, matchValue, linkedAccounts: [{steamId, displayName}], supportingSignals: [{type, value, linkedAccounts: [{steamId, displayName}]}] }` |

`MULTI_ACCOUNT.matchType` değerleri (güçlü sinyal — flag tetikleyici, 02 §14.3, 03 §7.4): `WALLET_PAYOUT`, `WALLET_REFUND`.
`MULTI_ACCOUNT.supportingSignals[].type` değerleri (destekleyici sinyal — tek başına flag tetiklemez, güçlü sinyal eşliğinde admin kanıtı olarak listelenir): `IP_ADDRESS`, `DEVICE_FINGERPRINT`, `SOURCE_ADDRESS`. Bilinen exchange/custodial adresleri admin tarafından `multi_account.exchange_addresses` SystemSetting'inde tutulur ve `SOURCE_ADDRESS` karşılaştırmasından hariç bırakılır (`NONE` = hariç adres yok).

### 9.4 AD4 — `POST /admin/flags/:id/approve`

**Amaç:** Flag onayla (S14). Permission: `MANAGE_FLAGS`.

**Request:**
```json
{ "note": "Fiyat makul, geçmişi temiz" }
```

`note`: Opsiyonel. Maksimum 2000 karakter (06 §3.12 `AdminNote` kolon genişliği); aşılırsa 400 `VALIDATION_ERROR`.

**Response (200) `data`:**
```json
{ "reviewStatus": "APPROVED", "transactionStatus": "CREATED", "reviewedAt": "..." }
```

**Hatalar:** 409 `ALREADY_REVIEWED`, 404 `FLAG_NOT_FOUND`, 400 `VALIDATION_ERROR` (note > 2000 karakter)

> **UI terminoloji notu:** API endpoint `/approve` kullanır, UI'da bu aksiyonun karşılığı **"İşleme Devam Et"** butonudur (flag false positive). Frontend mapping: `approve` → "İşleme Devam Et" (04 §S14).

### 9.5 AD5 — `POST /admin/flags/:id/reject`

**Amaç:** Flag reddet — işlem CANCELLED_ADMIN olur (S14). Permission: `MANAGE_FLAGS`.

> **UI terminoloji notu:** API endpoint `/reject` kullanır, UI'da bu aksiyonun karşılığı **"İptal Et"** butonudur (fraud doğrulanmış). Frontend mapping: `reject` → "İptal Et" (04 §S14).

**Request:**
```json
{ "note": "Fiyat manipülasyonu şüphesi" }
```

`note`: Opsiyonel. Maksimum 2000 karakter; aşılırsa 400 `VALIDATION_ERROR`.

**Response (200) `data`:**
```json
{ "reviewStatus": "REJECTED", "transactionStatus": "CANCELLED_ADMIN", "reviewedAt": "..." }
```

**Hatalar:** AD4 ile aynı (404 `FLAG_NOT_FOUND`, 409 `ALREADY_REVIEWED`, 400 `VALIDATION_ERROR`).

### 9.6 AD6 — `GET /admin/transactions`

**Amaç:** Tüm işlem listesi (S15). Permission: `VIEW_TRANSACTIONS`. Paginated.

**Query Params:** `status`, `statusGroup`, `stablecoin`, `dateFrom`, `dateTo`, `minAmount`, `maxAmount`, `search`, `sortBy`, `sortOrder`

`statusGroup` — 04 §8.4 "Durum" filtresinin çok-durumlu gruplarını sunucu tarafında ifade eder (tek `status` paramı yetmediği için). Değerler:

| `statusGroup` | Kapsadığı durumlar |
|---|---|
| `ACTIVE` | Terminal olmayan tüm durumlar (CREATED…ITEM_DELIVERED + FLAGGED) — AD1 dashboard `activeTransactions` sayacı ile birebir (`?tab=active` deep-link tutarlılığı). |
| `COMPLETED` | `COMPLETED` |
| `CANCELLED` | `CANCELLED_TIMEOUT`, `CANCELLED_SELLER`, `CANCELLED_BUYER`, `CANCELLED_ADMIN` |
| `FLAGGED` | `FLAGGED` (ACTIVE'in daralan alt kümesi) |

`status` ve `statusGroup` birlikte verilirse ikisi de uygulanır (AND); S15 UI yalnızca birini gönderir.

**Response (200) `data.items[]`:**
```json
{
  "id": "tx-guid",
  "itemName": "AK-47 | Redline",
  "itemImageUrl": "https://steamcdn.../abc.png",
  "price": "100.00",
  "stablecoin": "USDT",
  "status": "COMPLETED",
  "seller": { "steamId": "...", "displayName": "...", "avatarUrl": "..." },
  "buyer": { "steamId": "...", "displayName": "...", "avatarUrl": "..." },
  "createdAt": "2026-03-16T10:00:00Z",
  "completedAt": "2026-03-16T17:00:00Z",
  "cancelledAt": null
}
```

`completedAt` / `cancelledAt`: işlemin terminal durumuna göre biri dolu, diğeri null (04 §8.4 "Tamamlanma/İptal" kolonu).

### 9.7 AD7 — `GET /admin/transactions/:id`

**Amaç:** İşlem tam admin görünümü (S16). Permission: `VIEW_TRANSACTIONS`.

T5'teki tüm alanlar + admin'e özel bölümler:

**Ek bölümler:**

| Bölüm | Açıklama |
|--------|----------|
| `statusHistory` | Her state geçişi: `[{ fromStatus, toStatus, changedAt, trigger }]` |
| `paymentDetail` | Blockchain detay: `{ paymentAddress, receivedAmount, receivedTxHash, blockConfirmations, confirmedAt }` |
| `sellerPayoutDetail` | Satıcı ödeme: `{ grossAmount, commission, gasFee, gasFeeFromCommission, gasFeeFromSeller, netAmount, txHash, sentAt }` |
| `refundDetail` | İade: `{ originalAmount, gasFee, netRefundAmount, refundAddress, txHash, refundedAt }` |
| `notificationHistory` | Gönderilen bildirimler: `[{ type, recipient, channels, sentAt, content }]` (`content` = gönderilen bildirim gövdesi, 04 §8.5 "içerik") |
| `disputeHistory` | Dispute'lar: `[{ id, type, status, autoCheckResult, escalatedAt, closedAt }]` |
| `flagHistory` | Flag'ler: `[{ id, type, reviewStatus, adminNote, reviewedAt }]` |
| `adminActions` | `{ canApproveFlag, canRejectFlag, canCancel }` |

### 9.8 AD8 — `GET /admin/settings`

**Amaç:** Platform parametreleri (S17). Permission: `MANAGE_SETTINGS`.

**Response (200) `data`:**
```json
{
  "settings": [
    {
      "key": "commission_rate",
      "value": "0.02",
      "category": "commission",
      "label": "Komisyon oranı",
      "description": "Komisyon oranı (%2)",
      "unit": "oran",
      "valueType": "number"
    }
  ]
}
```

**Kategoriler (API lehçesi):** `timeout`, `commission`, `gas_fee`, `transaction_limits`, `new_account`, `cancel_rules`, `fraud_detection`, `buyer_identification`, `geo_blocking`, `age_verification`, `blockchain_health`, `wallet_security`, `reputation`, `platform_maintenance`, `retention`

> **Notlar:**
> - Yalnızca `SystemSettingsCatalog` (kod) içindeki anahtarlar döner (58 anahtar). `category`, DB `Category` kolonunun (06 §3.17, daha kaba) ince API lehçesidir — eşleme kataloğda tanımlıdır.
> - `valueType` ∈ `number` (int/decimal) | `boolean` | `string`. `value`, henüz yapılandırılmamış anahtarlarda `null` döner (06 §3.17 `IsConfigured = false`).
> - DTO **etki-kapsamı** alanı taşımaz; S17 UI etkiyi (yeni işlem / runtime) kategoriden türetir (04 §8.6).
> - Sanctions taraması (yaptırımlı adres listesi) ayrı bir admin yüzeyinden yönetilir (T82) — SystemSetting değildir; bu yüzden kategori listesinde `sanctions_screening` yoktur.

### 9.9 AD9 — `PUT /admin/settings/:key`

**Amaç:** Tek parametre güncelleme (S17). Permission: `MANAGE_SETTINGS`.

**Request:**
```json
{ "value": "3" }
```

**Response (200) `data`:**
```json
{ "key": "commission_rate", "value": "3", "updatedAt": "..." }
```

**Hatalar:** 404 `SETTING_NOT_FOUND`, 400 `VALIDATION_ERROR`

### 9.10 AD10 — `GET /admin/steam-accounts`

**Bu endpoint kaldırılmıştır (v3.0, P2P geçişi).**

Platform Steam hesabı işletmediği için listelenecek bot hesabı, izlenecek emanet item sayısı veya günlük trade offer kotası yoktur (02 §15, 05 §3.2, 06 §3.10). `VIEW_STEAM_ACCOUNTS` yetkisi, S18 admin ekranı ve `PlatformSteamBotStatus` enum'u da kaldırılmıştır.

Steam tarafındaki tek izleme noktası salt okunur API çağrılarının sağlığıdır; bu, genel platform sağlık göstergeleri içinde raporlanır.

> Alt bölüm numarası bilinçli korundu — §9.11 ve sonrası referanslarının kayması engellendi.

### 9.11 AD11 — `GET /admin/roles`

**Amaç:** Rol listesi (S19). Permission: `MANAGE_ROLES`.

**Response (200) `data`:**
```json
{
  "roles": [
    {
      "id": "role-guid",
      "name": "Flag Yöneticisi",
      "description": "Flag'leri görüntüleyebilir ve yönetebilir",
      "permissions": ["VIEW_FLAGS", "MANAGE_FLAGS"],
      "assignedUserCount": 3,
      "createdAt": "2026-03-01T10:00:00Z"
    }
  ],
  "availablePermissions": [
    { "key": "VIEW_FLAGS", "label": "Flag'leri görüntüle" },
    { "key": "MANAGE_FLAGS", "label": "Flag'leri yönet" },
    { "key": "VIEW_TRANSACTIONS", "label": "İşlemleri görüntüle" },
    { "key": "MANAGE_SETTINGS", "label": "Parametreleri yönet" },
    { "key": "VIEW_STEAM_ACCOUNTS", "label": "Steam hesaplarını görüntüle" },
    { "key": "MANAGE_STEAM_RECOVERY", "label": "Steam recovery yönet" },
    { "key": "VIEW_USERS", "label": "Kullanıcı detay görüntüle" },
    { "key": "MANAGE_ROLES", "label": "Rolleri yönet" },
    { "key": "VIEW_AUDIT_LOG", "label": "Audit log görüntüle" },
    { "key": "CANCEL_TRANSACTIONS", "label": "İşlemleri iptal et" },
    { "key": "EMERGENCY_HOLD", "label": "İşlemleri acil dondurma/kaldırma" },
    { "key": "VIEW_DISPUTES", "label": "İtirazları görüntüle" },
    { "key": "MANAGE_DISPUTES", "label": "İtirazları çöz" },
    { "key": "MANAGE_SANCTIONS", "label": "Sanctions listesi yönet" }
  ]
}
```

> **Not:** `MANAGE_STEAM_RECOVERY` 04 §8.8 "Steam recovery yönet" satırının string identifier'ıdır — S18 Manual Recovery Başlat / not düşme / sorumlu admin atama akışlarını kapsar (fon/item güvenliği etkili, salt-okunur `VIEW_STEAM_ACCOUNTS` yetkisinden ayrı). T103 (S18) wire eder; T39 yalnızca katalog girişini sağlar.
>
> **Not:** `MANAGE_SANCTIONS` 04 §8.8 "Sanctions listesi yönet" satırının string identifier'ıdır — 02 §21.1 sanctions screening listesinin admin CRUD'unu (AD22/AD23/AD24) kapsar. `MANAGE_SETTINGS`'ten ayrıdır (least-privilege): sanctions listesi yöneten admin sistem ayarlarına dokunmaz. T82 wire eder; T39 yalnızca katalog girişini sağlar.

### 9.12 AD12 — `POST /admin/roles`

**Amaç:** Yeni rol oluşturma (S19). Permission: `MANAGE_ROLES`.

**Request:**
```json
{
  "name": "İşlem Denetçisi",
  "description": "İşlemleri görüntüleyebilir",
  "permissions": ["VIEW_TRANSACTIONS", "VIEW_FLAGS"]
}
```

**Response (201) `data`:**
```json
{ "id": "role-guid", "name": "İşlem Denetçisi", "permissions": [...], "createdAt": "..." }
```

**Hatalar:** 409 `ROLE_NAME_EXISTS`, 400 `VALIDATION_ERROR`

### 9.13 AD13 — `PUT /admin/roles/:id`

**Amaç:** Rol güncelleme (S19). Permission: `MANAGE_ROLES`. AD12 ile aynı request/response yapısı.

**Hatalar:** AD12 + 404 `ROLE_NOT_FOUND`

### 9.14 AD14 — `DELETE /admin/roles/:id`

**Amaç:** Rol silme (S19). Permission: `MANAGE_ROLES`.

**Response (200) `data`:** `null`

**Hatalar:** 404 `ROLE_NOT_FOUND`, 422 `ROLE_HAS_USERS`

### 9.15 AD15 — `GET /admin/users`

**Amaç:** Admin kullanıcı listesi (S19 rol atama). Permission: `MANAGE_ROLES`. Paginated.

**Query Params:** `search`, `roleId`

**Response (200) `data.items[]`:**
```json
{
  "id": "user-guid",
  "steamId": "76561198012345678",
  "displayName": "AdminUser1",
  "avatarUrl": "https://steamcdn.../abc.jpg",
  "role": { "id": "role-guid", "name": "Flag Yöneticisi" }
}
```

### 9.16 AD16 — `GET /admin/users/:steamId`

**Amaç:** Kullanıcı detay (S20). Permission: `VIEW_USERS`.

**Response (200) `data`:**
```json
{
  "profile": {
    "id": "user-guid",
    "steamId": "76561198012345678",
    "displayName": "PlayerOne",
    "avatarUrl": "https://steamcdn.../abc.jpg",
    "accountStatus": "ACTIVE",
    "accountAge": "6 ay",
    "createdAt": "2025-09-16T08:00:00Z",
    "reputationScore": 4.8,
    "isSuspended": false,
    "suspendedAt": null,
    "suspensionReason": null,
    "suspensionExpiresAt": null,
    "activeTransactionCount": 1,
    "hasTransactionOnHold": false,
    "completedTransactionCount": 24,
    "successfulTransactionRate": 0.80,
    "cancelRate": 0.20
  },
  "stats": {
    "totalTransactions": 30,
    "completedTransactions": 24,
    "cancelledTransactions": 4,
    "flaggedTransactions": 2,
    "successfulTransactionRate": 0.80,
    "totalVolume": "5420.00",
    "lastTransactionAt": "2026-03-15T18:00:00Z"
  },
  "walletHistory": [
    { "type": "seller", "address": "TXyz...", "setAt": "2026-03-01T00:00:00Z", "current": true },
    { "type": "seller", "address": "TPrev...", "setAt": "2025-11-01T00:00:00Z", "current": false }
  ],
  "flagHistory": [
    { "id": "...", "type": "PRICE_DEVIATION", "transactionId": "...", "reviewStatus": "APPROVED", "createdAt": "..." }
  ],
  "disputeHistory": [
    { "id": "...", "type": "PAYMENT", "transactionId": "...", "status": "CLOSED", "createdAt": "..." }
  ],
  "frequentCounterparties": [
    { "steamId": "...", "displayName": "...", "transactionCount": 3, "lastTransactionAt": "..." }
  ]
}
```

`accountStatus`: `ACTIVE`, `SUSPENDED`, `DEACTIVATED`, `DELETED`. `flagHistory[].transactionId` ACCOUNT_LEVEL flag'lerde `null` (06 §3.12). `stats.totalVolume` tamamlanan işlemlerin toplamı (invariant 2-ondalık string); tamamlanan işlem yoksa `null`. `frequentCounterparties` en sık işlem yapılan en fazla 10 karşı taraf (wash-trading sinyali, 04 §8.9.7). `profile.activeTransactionCount` / `hasTransactionOnHold` → 04 §8.9.1 koşullu durum badge'leri (terminal-olmayan işlem sayısı + EMERGENCY_HOLD; AD1/AD19d "aktif" tanımıyla birebir). `profile.completedTransactionCount` / `successfulTransactionRate` / `cancelRate` → 04 §8.9.1 itibar skoru breakdown'u (skoru oluşturan denormalize sayaçlar; `cancelRate = 1 − successfulTransactionRate`, ikisi de 0..1 kesir, oran `null` ise ikisi de `null` — 07 §5.1 deseni). `walletHistory` mevcut adresleri (`current: true`, User kaydından) + her değişimde kaydedilen önceki adresleri (`current: false`, en yeni önce; 04 §8.9.3, WalletAddressHistory T105b) içerir. İşlem geçmişi bu response'a dahil değil — AD16b.

### 9.17 AD16b — `GET /admin/users/:steamId/transactions`

**Amaç:** Kullanıcının işlem geçmişi (S20 tablo). Permission: `VIEW_USERS`. Paginated.

Response: AD6 ile aynı yapı, bu kullanıcıya filtrelenmiş.

### 9.18 AD17 — `PUT /admin/users/:id/role`

**Amaç:** Kullanıcıya rol ata/değiştir (S19). Permission: `MANAGE_ROLES`.

**Request:**
```json
{ "roleId": "role-guid" }
```

`roleId: null` → rol kaldırır.

**Response (200) `data`:**
```json
{ "userId": "user-guid", "role": { "id": "...", "name": "..." }, "assignedAt": "..." }
```

**Hatalar:** 404 `USER_NOT_FOUND`, 404 `ROLE_NOT_FOUND`

### 9.19 AD18 — `GET /admin/audit-logs`

**Amaç:** Audit log listesi (S21). Permission: `VIEW_AUDIT_LOG`. Paginated.

**Query Params:** `category`, `dateFrom`, `dateTo`, `search`, `transactionId`

**Response (200) `data.items[]`:**
```json
{
  "id": "log-guid",
  "category": "FUND_MOVEMENT",
  "action": "WALLET_ESCROW_RELEASE",
  "actor": { "steamId": "...", "displayName": "System" },
  "subject": { "steamId": "...", "displayName": "SellerPlayer" },
  "transactionId": "tx-guid",
  "detail": { "amount": "99.70", "stablecoin": "USDT", "txHash": "abc123..." },
  "createdAt": "2026-03-16T17:00:00Z"
}
```

`category`: `FUND_MOVEMENT`, `ADMIN_ACTION`, `SECURITY_EVENT`. `subject`: Opsiyonel.

`search`: serbest metin filtresi — hem `EntityId` (ayar anahtarı, işlem/varlık ID) hem de ilgili kullanıcının (actor veya subject) Steam ID'si / görünen adı üzerinde eşleşir; böylece 04 §8.10 "Kullanıcı: Steam ID veya kullanıcı adı" filtresi gerçek kişilere çözümlenir (EntityId bir Guid taşısa bile). Kullanıcı eşleşmesi soft-delete query filter'ını yok sayar (anonimleştirilmiş kullanıcı kimliği silindiği için eşleşmeyi durdurur — 02 §19). (T106)

### 9.20 AD19 — `POST /admin/transactions/:id/cancel`

**Amaç:** Admin doğrudan işlem iptali. Permission: `CANCEL_TRANSACTIONS`.

**Request:**
```json
{ "reason": "Yasal talep nedeniyle işlem iptal edildi" }
```

`reason`: Zorunlu, min 10 karakter.

**Response (200) `data`:**
```json
{
  "status": "CANCELLED_ADMIN",
  "cancelledAt": "2026-03-16T15:00:00Z",
  "itemReturned": true,
  "paymentRefunded": true
}
```

**İptal edilebilir state'ler:** CREATED, ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, FLAGGED.

**İptal edilemez:** ITEM_DELIVERED (item alıcıya teslim edilmiş — standart iptal/iade uygulanamaz, yalnızca exceptional resolution), COMPLETED, CANCELLED_*, EMERGENCY_HOLD.

**İade kuralları:** Item emanetteyse → satıcıya, ödeme alındıysa → alıcıya (fiyat + komisyon - gas fee).

**Hatalar:** 409 `INVALID_STATE_TRANSITION`, 422 `CANNOT_CANCEL_AT_DELIVERY_STAGE` (ITEM_DELIVERED+), 404 `TRANSACTION_NOT_FOUND`, 400 `VALIDATION_ERROR`

### 9.21 AD19b — `POST /admin/transactions/:id/emergency-hold`

**Amaç:** Aktif işlemi acil dondurma (sanctions, hesap ele geçirme vb.). Permission: `EMERGENCY_HOLD`.

> **Otomatik tetikleme:** Sanctions screening eşleşmesi tespit edildiğinde sistem bu endpoint'i kullanıcının tüm aktif işlemleri için otomatik olarak çağırır (03 §11a.3). Admin panelinde otomatik hold'lar "Auto-Hold — Sanctions Match" etiketi ile gösterilir.

**Request:**
```json
{ "reason": "Sanctions eşleşmesi tespit edildi — cüzdan adresi OFAC listesinde" }
```

`reason`: Zorunlu, min 10 karakter.

**Response (200) `data`:**
```json
{
  "status": "EMERGENCY_HOLD",
  "frozenAt": "2026-03-20T10:00:00Z",
  "previousStatus": "PAYMENT_RECEIVED"
}
```

**Hold uygulanabilir state'ler:** Tüm aktif state'ler (CREATED → ITEM_DELIVERED + FLAGGED).

**Hatalar:** 409 `ALREADY_ON_HOLD`, 409 `INVALID_STATE_TRANSITION` (COMPLETED, CANCELLED_*), 404 `TRANSACTION_NOT_FOUND`, 403 `INSUFFICIENT_PERMISSION`

### 9.22 AD19c — `POST /admin/transactions/:id/release-hold`

**Amaç:** Emergency hold kaldırma — işlem kaldığı yerden devam eder. Permission: `EMERGENCY_HOLD`.

**Request:**
```json
{ "action": "RESUME", "note": "Sanctions kontrolü temiz — hold kaldırıldı" }
```

`action`: `RESUME` (devam et) veya `CANCEL` (iptal et). `note`: Zorunlu.

**RESUME response (200) `data`:**
```json
{
  "status": "PAYMENT_RECEIVED",
  "releasedAt": "2026-03-20T12:00:00Z",
  "action": "RESUME"
}
```

İşlem `previousStatus`'a döner. Timeout freeze kalkar, kalan süre korunarak `expiresAt` ileri kaydırılır.

**CANCEL response (200) `data`:**
```json
{
  "status": "CANCELLED_ADMIN",
  "releasedAt": "2026-03-20T12:00:00Z",
  "action": "CANCEL",
  "itemReturned": true,
  "paymentRefunded": true
}
```

**CANCEL dalı kuralları:**

| `previousStatus` | CANCEL izinli mi | İade kuralları |
|-------------------|------------------|----------------|
| CREATED, ACCEPTED | Evet | Item emanette değil → iade yok |
| TRADE_OFFER_SENT_TO_SELLER | Evet | Trade offer iptal edilir, item satıcıda kalır |
| ITEM_ESCROWED | Evet | Item satıcıya iade edilir |
| PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER | Evet | Item satıcıya iade + ödeme alıcıya iade (fiyat + komisyon - gas fee) |
| FLAGGED | Evet | Hold öncesi duruma göre yukarıdaki kurallar uygulanır |
| ITEM_DELIVERED | **Hayır** | Item zaten alıcıda — standart iptal/iade uygulanamaz. CANCEL reddedilir, yalnızca RESUME izinli. Exceptional durumlar admin tarafından manuel çözülür (AD19 §İptal edilemez ile tutarlı) |

> **Tasarım kararı:** ITEM_DELIVERED → EMERGENCY_HOLD → CANCEL zinciri yasaktır. Bu, AD19'daki "ITEM_DELIVERED'da standart iptal uygulanamaz" kuralıyla tutarlıdır. Admin bu durumda yalnızca RESUME yapabilir; exceptional resolution (ör. yanlış item teslimi) ayrı bir süreçle ele alınır. **(WP5)** Bu yetkilendirilmiş "ayrı süreç" admin dispute çözümüdür (AD29 §9.30): ITEM_DELIVERED'da açılan WRONG_ITEM/DELIVERY dispute'u buyer-favor çözüldüğünde işlem `AdminResolveRefund` ile `REFUNDED`'a geçer (alıcıya iade). Fiziksel item geri-alma WP6/manuel kapsamındadır.

**Hatalar:** 409 `NOT_ON_HOLD`, 400 `VALIDATION_ERROR`, 422 `CANNOT_CANCEL_DELIVERED_HOLD` (ITEM_DELIVERED hold'unda CANCEL denemesi)

### 9.22a AD19d — `POST /admin/transactions/hold-by-user/:userId`

**Amaç:** Bir kullanıcının **tüm aktif işlemlerine** toplu EMERGENCY_HOLD uygulama (04 §8.3 hesap-flag "Hold" aksiyonu, 03 §8.8 — T100). Permission: `EMERGENCY_HOLD` (AD19b/c ile aynı yetki). `:userId`, flag detayında (AD3 `userId`) dönen flag'lenmiş kullanıcının iç Guid'idir.

**Request:**
```json
{ "reason": "Çoklu hesap tespiti — tüm aktif işlemler donduruldu" }
```

`reason`: Zorunlu, ≥ 10 karakter (AD19b ile tutarlı).

**Response (200) `data`:**
```json
{
  "heldCount": 3,
  "appliedAt": "2026-03-20T12:00:00Z",
  "heldTransactionIds": ["tx-guid-1", "tx-guid-2", "tx-guid-3"]
}
```

**Davranış:** Kullanıcının (satıcı **veya** alıcı olduğu) silinmemiş, hold'da olmayan, terminal olmayan işlemleri seçilir; her biri için AD19b ile birebir aynı sıra uygulanır (T50 freeze pre-pass → state machine `ApplyEmergencyHold` → `EmergencyHoldAppliedEvent` bildirim fan-out → `EMERGENCY_HOLD_APPLIED` audit), tek `SaveChanges` ile atomik commit. Zaten hold'da olan işlemler `!IsOnHold` filtresiyle atlanır → çağrı **idempotent**'tir (tekrar koşumu `heldCount: 0` döner). Aktif işlem yoksa 200 + `heldCount: 0` (no-op). Mevcut T54 `FraudFlagService` sanctions otomatik cascade'i ile aynı seçim mantığını paylaşır.

**Hatalar:** 400 `VALIDATION_ERROR` (reason < 10 karakter)

> **Not:** Hesap-flag varyantının diğer iki aksiyonu — "Flag Kaldır" (AD4 approve) ve "Askıya Al" (hesap askıya alma) — bu endpoint kapsamı dışındadır. "Askıya Al" için kullanıcı durum modeli + auth pipeline enforcement gerektiğinden ayrı bir görevde (S20 / kullanıcı yönetimi, AD20) ele alınır.

### 9.23 AD22 — `GET /admin/sanctions/addresses`

**Amaç:** Sanctions listesindeki cüzdan adreslerini sayfalı görüntüleme (02 §21.1, 03 §11a.3, 06 §3.25). Permission: `MANAGE_SANCTIONS`. Paginated.

**Query Params:** `network` (`TRC-20`, default `TRC-20`), `source` (`OFAC` | `EU` | `UN` | `MANUAL`), `search` (adres substring eşleşmesi), `isActive` (default `true` — admin paneli aktif satırları gösterir, deaktif arşiv görmek için `false`), `sortBy` (`listedAt` default, `address`), `sortOrder` (`asc`, `desc` default), `page`, `pageSize` (max 100).

**Response (200) `data.items[]`:**
```json
{
  "id": "guid",
  "address": "TXyz1234567890abcdef1234567890abcd",
  "network": "TRC-20",
  "source": "MANUAL",
  "reason": "FBI bildirim no. 2026-04-12 / sahtekarlık şikayeti",
  "listedAt": "2026-04-12T10:00:00Z",
  "addedBy": { "id": "admin-guid", "displayName": "AdminUser1" },
  "isActive": true,
  "createdAt": "2026-04-12T10:00:00Z",
  "updatedAt": "2026-04-12T10:00:00Z"
}
```

`addedBy`: MANUAL kaynak için admin objesi (`null` döndürülmez); OFAC/EU/UN auto-sync için `null` (SYSTEM aktör — post-MVP).

**Hatalar:** 403 `INSUFFICIENT_PERMISSION`, 400 `VALIDATION_ERROR` (sayfalama parametreleri)

### 9.24 AD23 — `POST /admin/sanctions/addresses`

**Amaç:** Listeye yeni yaptırımlı adres ekleme. Permission: `MANAGE_SANCTIONS`.

**Request:**
```json
{
  "address": "TXyz1234567890abcdef1234567890abcd",
  "network": "TRC-20",
  "source": "MANUAL",
  "reason": "FBI bildirim no. 2026-04-12 / sahtekarlık şikayeti"
}
```

`address`: Zorunlu. MVP'de TRC-20 base58 format doğrulanır (`T` + 33 karakter, case-sensitive — 02 §12.3 ile aynı validator). `network`: Zorunlu, MVP'de yalnız `'TRC-20'`. `source`: Zorunlu, `'OFAC' | 'EU' | 'UN' | 'MANUAL'` — MVP'de admin yalnız `'MANUAL'` set'ler; OFAC/EU/UN auto-sync job (post-MVP) için reserved. `reason`: Opsiyonel, max 500 karakter.

**Response (201) `data`:** AD22 satır formatı (`isActive: true`).

**Yan etkiler:**
- 06 §3.25 `SanctionedAddress` satırı yazılır (`ListedAt = CreatedAt`, `AddedByAdminId = caller`).
- `AuditLog` SECURITY_EVENT kategorisinde yeni `SANCTIONS_LIST_ADDRESS_ADDED` aksiyon yazılır (06 §3.20).
- **Retroaktif eşleşme kontrolü:** Yeni adres mevcut bir kullanıcının `DefaultPayoutAddress` veya `DefaultRefundAddress` ile eşleşirse → o kullanıcı için `FraudFlagType.SANCTIONS_MATCH` (`cascadeEmergencyHold = true`) tetiklenir; tüm aktif işlemleri EMERGENCY_HOLD'a alınır (03 §11a.3 ile aynı kural — admin tarafından adres eklemek de retro pipeline'ı tetikler).

**Hatalar:** 400 `VALIDATION_ERROR` (format / network / source / reason uzunluğu), 400 `INVALID_WALLET_ADDRESS` (TRC-20 format başarısız), 409 `SANCTIONS_ADDRESS_ALREADY_LISTED` (aktif olarak listede), 403 `INSUFFICIENT_PERMISSION`

### 9.25 AD24 — `DELETE /admin/sanctions/addresses/:id`

**Amaç:** Adresi listeden çıkarma (soft deactivate). Permission: `MANAGE_SANCTIONS`.

**Davranış:** Hard delete uygulanmaz — satırın `IsActive = false` olur, `UpdatedAt` güncellenir. Audit izi korunur; aynı adres daha sonra tekrar eklenebilir (06 §3.25 filtered UQ izin verir).

**Response (200) `data`:**
```json
{
  "id": "guid",
  "address": "TXyz...",
  "isActive": false,
  "deactivatedAt": "2026-04-20T15:00:00Z"
}
```

**Yan etkiler:**
- 06 §3.25 satırı `IsActive = false`, `UpdatedAt` set edilir.
- `AuditLog` SECURITY_EVENT kategorisinde yeni `SANCTIONS_LIST_ADDRESS_REMOVED` aksiyon yazılır.
- **Mevcut hold'lar kalkmaz:** Deactivation, daha önce sanctions match nedeniyle uygulanmış EMERGENCY_HOLD'ları otomatik kaldırmaz. Admin AD19c (`/release-hold`) ile hold'u manuel kaldırır (incelemeyi tamamladıktan sonra).

**Hatalar:** 404 `SANCTIONS_ADDRESS_NOT_FOUND` (kayıt yok), 409 `SANCTIONS_ADDRESS_ALREADY_INACTIVE` (zaten deaktif), 403 `INSUFFICIENT_PERMISSION`

### 9.30 AD27 / AD28 / AD29 — Admin dispute çözümü (`/admin/disputes`, WP5)

**Amaç:** ESCALATED dispute çıkmaz sokağını kapatma (02 §10.4, 03 §6.4). Admin eskalasyon kuyruğunu listeler, itirazı inceler ve **satıcı lehine** (işlem onaylanır) veya **alıcı lehine** (işlem `REFUNDED` + iade) çözer. SLA/atama/şablon-kural MVP-dışıdır (minimal çıkmaz-sokak-açıcı).

#### AD27 — `GET /admin/disputes`
Permission: `VIEW_DISPUTES`. Query: `status` (DisputeStatus, **default ESCALATED**), `type` (DisputeType), `page`, `pageSize`. Response: `PagedResult` — `items[]`: `{ id, transactionId, type, status, itemName, transactionStatus, openedBy { userId, steamId, displayName }, createdAt }`.

#### AD28 — `GET /admin/disputes/:id`
Permission: `VIEW_DISPUTES`. Response: `{ id, type, status, systemCheckResult?, userDescription?, adminId?, adminNote?, resolvedAt?, createdAt, updatedAt, transaction { id, status, itemName, price, stablecoin, isOnHold, hasActiveDispute, seller, buyer? } }`. **Hatalar:** 404 `DISPUTE_NOT_FOUND`.

#### AD29 — `POST /admin/disputes/:id/resolve`
Permission: `MANAGE_DISPUTES`. Body: `{ outcome: "SELLER_FAVOR" | "BUYER_FAVOR", adminNote (1..2000) }`.
- **SELLER_FAVOR:** dispute → `RESOLVED_FOR_SELLER`; `HasActiveDispute` temizlenir (başka aktif dispute yoksa) → WP1 satıcı payout ITEM_DELIVERED'da devam eder. Transaction state geçişi yok.
- **BUYER_FAVOR:** dispute → `RESOLVED_FOR_BUYER`; işlem `AdminResolveRefund` tetikleyicisiyle `REFUNDED`'a geçer; alıcı ödediyse `PaymentRefundToBuyerRequestedEvent` (WP2), item platformdaysa `ItemRefundToSellerRequestedEvent` yayınlanır. ITEM_DELIVERED'da item alıcıdadır → fiziksel geri-alma ayrı manuel/WP6 sürecidir.
- Her iki sonuç: `AdminId`/`AdminNote`/`ResolvedAt` set; `DISPUTE_RESOLVED` audit; `DisputeResolvedEvent` → buyer + seller `DISPUTE_RESULT` bildirimi. Tüm yan etkiler tek `SaveChanges` ile atomik.

**Response (200) `data`:** `{ id, status, transactionStatus, resolvedAt, buyerRefunded }`.

**Hatalar:** 400 `VALIDATION_ERROR` (adminNote eksik / > 2000 / geçersiz outcome), 404 `DISPUTE_NOT_FOUND`, 409 `DISPUTE_NOT_ESCALATED` (yalnız ESCALATED çözülebilir), 409 `TRANSACTION_ON_HOLD` (önce AD19c ile hold release), 409 `INVALID_STATE_TRANSITION`.

### 9.26 AD20 — `POST /admin/users/:userId/suspend`

**Amaç:** Hesabı askıya alma (02 §14.0/§16.2, 03 §8.3 — T105a). Permission: `MANAGE_FLAGS` (flag'lenmiş hesap yönetimi kapsamı). `:userId` kullanıcının iç Guid'idir.

**Enforcement modeli — kısıtlı oturum:** Askıya alma login'i engellemez (`IsDeactivated`'dan farklı). Suspended kullanıcı giriş yapar + aktif işlemlerini salt-okunur görür, ancak fon-akışı mutation'ları (işlem oluştur/kabul + cüzdan adresi değiştir) reddedilir; `/auth/me` `isSuspended=true` döner → istemci SuspendedHeader + S03d gösterir.

**Request:**
```json
{ "reason": "Çoklu hesap tespiti", "durationDays": 7 }
```

`reason`: Zorunlu, ≥10 karakter. `durationDays`: `null` = kalıcı; pozitif sayı = geçici blok (süre dolunca `AutoUnsuspendJob` otomatik kaldırır, 6 saatte bir tarar).

**Response (200) `data`:**
```json
{ "userId": "guid", "suspendedAt": "2026-06-05T12:00:00Z", "reason": "Çoklu hesap tespiti", "expiresAt": "2026-06-12T12:00:00Z" }
```

**Yan etkiler:** `User.IsSuspended/SuspendedAt/SuspensionReason/SuspensionExpiresAt` set edilir; `AuditLog` ADMIN_ACTION kategorisinde `USER_BANNED`; `AccountSuspendedEvent` → kullanıcıya `ACCOUNT_SUSPENDED` bildirimi. Tek `SaveChanges` ile atomik.

**Hatalar:** 400 `VALIDATION_ERROR` (reason <10 veya durationDays ≤0), 404 `USER_NOT_FOUND`, 409 `ALREADY_SUSPENDED`, 403 `INSUFFICIENT_PERMISSION`

> **Not:** Askıya alma, otomatik EMERGENCY_HOLD uygulamaz (S14 "Hold" ayrı aksiyon — AD19d). SignalR canlı force-restrict ertelendi; kullanıcının sonraki isteği/login'i suspended durumu algılar.

### 9.27 AD21 — `DELETE /admin/users/:userId/suspend`

**Amaç:** Askıyı kaldırma. Permission: `MANAGE_FLAGS`. (Geçici blok süresi dolduğunda aynı yol `AutoUnsuspendJob` tarafından SYSTEM aktörü ile otomatik çağrılır.)

**Response (200) `data`:**
```json
{ "userId": "guid", "unsuspendedAt": "2026-06-08T09:00:00Z" }
```

**Yan etkiler:** Suspension alanları temizlenir; `AuditLog` `USER_UNBANNED`; `AccountUnsuspendedEvent` → `ACCOUNT_UNSUSPENDED` bildirimi.

**Hatalar:** 404 `USER_NOT_FOUND`, 409 `NOT_SUSPENDED`, 403 `INSUFFICIENT_PERMISSION`

---

### 9.28 AD25 — `GET /admin/steam-accounts/:botId/recovery-queue`

**Bu endpoint kaldırılmıştır (v3.0, P2P geçişi).** Platform item tutmadığı için bir bota mahsur kalabilecek item ve dolayısıyla recovery kuyruğu yoktur (02 §15, 06 §3.10a). `MANAGE_STEAM_RECOVERY` yetkisi de kaldırılmıştır. Aşağıdaki sözleşme tarihsel referans olarak bırakılmıştır.

**~~Eski amaç~~:** Kısıtlı/banned bir botun emanetinde stuck kalan item'ların recovery kuyruğu (S18, 04 §8.7, 02 §15, 03 §11.2a). Permission: `VIEW_STEAM_ACCOUNTS`.

**Response (200) `data`:**
```json
{
  "botId": "guid",
  "botStatus": "RESTRICTED",
  "items": [
    {
      "id": "recovery-guid",
      "transactionId": "tx-guid",
      "itemName": "AK-47 | Redline",
      "itemIconUrl": "https://…",
      "sellerSteamId": "765…",
      "sellerDisplayName": "Seller",
      "buyerSteamId": "765…",
      "buyerDisplayName": "Buyer",
      "currentStatus": "ITEM_ESCROWED",
      "statusAtRestriction": "ITEM_ESCROWED",
      "isOnHold": true,
      "recoveryStatus": "PENDING",
      "responsibleAdminId": null,
      "responsibleAdminName": null,
      "adminNote": null,
      "createdAt": "2026-06-13T20:00:00Z",
      "resolvedAt": null
    }
  ]
}
```

`recoveryStatus`: `PENDING` / `IN_REVIEW` / `RESOLVED` (06 §3.10a). `statusAtRestriction` kısıtlama anındaki işlem state snapshot'ı; `currentStatus` işlemin güncel state'i. Liste oldest-first (en uzun süredir stuck üstte).

**Hatalar:** 404 `STEAM_ACCOUNT_NOT_FOUND`, 403 `INSUFFICIENT_PERMISSION`

### 9.29 AD26 — `PATCH /admin/steam-accounts/recovery/:id`

**Bu endpoint kaldırılmıştır (v3.0, P2P geçişi)** — AD25 ile aynı gerekçe: recovery kuyruğu diye bir kavram kalmamıştır.

**~~Eski amaç~~:** Recovery item triage güncellemesi. Permission: `MANAGE_STEAM_RECOVERY`.

**Request (PATCH semantiği — null/eksik alan = değiştirme):**
```json
{ "recoveryStatus": "IN_REVIEW", "responsibleAdminId": "admin-guid", "adminNote": "Steam support ticket açıldı." }
```

**Response (200) `data`:** Güncellenmiş `BotRecoveryQueueItem` (AD25 satır şekli).

**Kurallar:** RESOLVED terminaldir (değiştirilemez → 409). `responsibleAdminId` mevcut bir kullanıcıya işaret etmeli. En az bir alan gerekli. `RESOLVED`'e geçişte `resolvedAt` damgalanır. `BOT_RECOVERY_UPDATED` audit satırı yazılır.

**Hatalar:** 404 `RECOVERY_ITEM_NOT_FOUND`, 409 `RECOVERY_ALREADY_RESOLVED`, 400 `VALIDATION_ERROR` / `RESPONSIBLE_ADMIN_NOT_FOUND` / `NO_CHANGE`, 403 `INSUFFICIENT_PERMISSION`

---

### 9.31 AD30 / AD31 — Admin bakım/kesinti kontrolü (`/admin/maintenance`, WP7)

**Amaç:** Platform bakımı veya Steam/blockchain kesintisi penceresini admin tarafından başlatma/bitirme (02 §3.3, 05 §4.4). Tek bir atomik işlemde: (1) dört `platform.maintenance.*` ayarını yazar (P2 `GET /platform/maintenance` banner read-model'i), (2) tipe göre aktif işlemlerin timeout'larını topluca dondurur/çözer (`TimeoutFreezeService.FreezeManyAsync`/`ResumeManyAsync`), (3) 30 sn public cache'i invalidate eder, (4) RT2 `MaintenanceStatusChanged` push'unu yayınlar. Permission: **`MANAGE_SETTINGS`** (bakım bir platform-ayar işlemidir; AD9 ile aynı yetki).

> **Tip → freeze eşlemesi (07 §10.2 birebir):** `PLANNED_MAINTENANCE` → freeze YOK (yalnız banner); `PLATFORM_MAINTENANCE` → tüm aktif timeout'lar (`MAINTENANCE`); `STEAM_OUTAGE` → Steam-bağımlı state'ler (`STEAM_OUTAGE`); `BLOCKCHAIN_DEGRADATION` → ödeme adımı (`BLOCKCHAIN_DEGRADATION`). Freeze enum değerleri 06 §2.20 `TimeoutFreezeReason` ile birebir.

> **MVP notu (WP7):** Otomatik tespit (Steam/blockchain health check → otomatik freeze, 02 §3.3) bu sürümde **manuel-only**; health-probe altyapısı WP16'da kurulunca bu endpoint'lerin üstüne biner. `suspend-signalr` canlı force-restrict MVP-dışı (§9.22a).

#### AD30 — `POST /admin/maintenance/freeze`

**Request:**
```json
{ "type": "PLATFORM_MAINTENANCE", "message": "Platform şu an bakımda.", "plannedEnd": "2026-07-01T18:00:00Z" }
```

| Alan | Zorunlu | Açıklama |
|------|---------|----------|
| `type` | Evet | `PLANNED_MAINTENANCE` \| `PLATFORM_MAINTENANCE` \| `STEAM_OUTAGE` \| `BLOCKCHAIN_DEGRADATION`. `NONE` reddedilir (çıkış için AD31). |
| `message` | Hayır | Kullanıcıya gösterilecek mesaj. Boş/eksik → `"NONE"` sentinel (banner mesajsız). |
| `plannedEnd` | Hayır | ISO-8601 UTC. Boş/eksik → `"NONE"`. |

**Response (200) `data`:**
```json
{ "active": true, "type": "PLATFORM_MAINTENANCE", "message": "Platform şu an bakımda.", "plannedEnd": "2026-07-01T18:00:00Z", "affectedTransactions": 12 }
```
`affectedTransactions` = dondurulan işlem sayısı (`PLANNED_MAINTENANCE`'te 0).

**Hatalar:** 400 `VALIDATION_ERROR` (geçersiz `type` veya `plannedEnd`), 403 `INSUFFICIENT_PERMISSION`, 401.

#### AD31 — `POST /admin/maintenance/resume`

**Amaç:** Aktif bakım/kesinti penceresini bitir: mevcut tipe ait dondurulmuş timeout'ları çöz (kalan süre korunarak ileri kaydırılır, 05 §4.4), dört ayarı `active=false`/`type=NONE` yap, cache invalidate + push. Bakım aktif değilken **idempotent** (no-op, 200).

**Request:** Gövde yok.

**Response (200) `data`:**
```json
{ "active": false, "type": null, "message": null, "plannedEnd": null, "affectedTransactions": 8 }
```
`affectedTransactions` = çözülen işlem sayısı.

**Hatalar:** 403 `INSUFFICIENT_PERMISSION`, 401.

> **Audit:** AD30/AD31 her ikisi de `MAINTENANCE_MODE_CHANGED` audit satırı yazar (`EntityType=Maintenance`, `EntityId`=yeni tip, eski/yeni dört ayar + işlem sayısı). 05 §4.4 "maintenance mode giriş/çıkış AuditLog'a kaydedilir".

> **Doğrudan ayar düzenleme:** `platform.maintenance.*` anahtarları AD9 (`PUT /admin/settings/:key`) ile de düzenlenebilir; bu yolda da cache invalidate + push yapılır (banner stale kalmaz) ama **freeze tetiklenmez** (freeze AD30/AD31'e özgü). `active=true` + `type=NONE` kombinasyonu cross-key invariant ile reddedilir → bakıma giriş AD30 üzerinden yapılır.

---

## 10. Platform Endpoints

### 10.1 P1 — `GET /platform/stats`

**Amaç:** Landing page güven göstergeleri (S01).

| Konu | Değer |
|------|-------|
| Auth | Public |
| Cache | 15 dk TTL |

**Response (200) `data`:**
```json
{
  "totalCompletedTransactions": 12480,
  "platformUptimePercent": 99.9
}
```

### 10.2 P2 — `GET /platform/maintenance`

**Amaç:** Platform bakım/kesinti durumu (04 C08 Maintenance Banner, 03 §11.1-11.2).

| Konu | Değer |
|------|-------|
| Auth | Public |
| Cache | 30 sn TTL |

**Response (200) `data`:**
```json
{
  "active": true,
  "type": "PLATFORM_MAINTENANCE",
  "message": "Platform şu an bakımda. İşlem süreleri donduruldu.",
  "plannedEnd": "2026-03-16T18:00:00Z"
}
```

**Bakım/kesinti yoksa:**
```json
{
  "active": false,
  "type": null,
  "message": null,
  "plannedEnd": null
}
```

**`active` + `type` kombinasyonları:**

| `active` | `type` | Anlam | C08 varyantı | Timeout freeze |
|----------|--------|-------|-------------|----------------|
| `true` | `PLANNED_MAINTENANCE` | Planlı bakım yaklaşıyor, platform çalışıyor | Sarı banner | Hayır — işlemler normal devam eder |
| `true` | `PLATFORM_MAINTENANCE` | Aktif bakım, platform kısıtlı | Kırmızı banner | Evet — tüm timeout'lar dondurulur |
| `true` | `STEAM_OUTAGE` | Steam servisleri çalışmıyor | Turuncu banner | Evet — Steam bağımlı timeout'lar dondurulur |
| `true` | `BLOCKCHAIN_DEGRADATION` | Blockchain altyapısı sorunlu | Turuncu banner | Evet — ödeme adımındaki timeout'lar dondurulur |
| `false` | `null` | Herhangi bir durum yok | Banner gösterilmez | Hayır |

> **Semantik:** `active: true` "frontend'in kullanıcıya göstermesi gereken bir durum var" anlamına gelir. `type` değeri durumun ciddiyetini ve etkisini belirler. `PLANNED_MAINTENANCE`'te platform tam işlevseldir, yalnızca bilgilendirme amaçlı banner gösterilir.

Frontend sayfa yüklemesinde P2'yi çağırır. Anlık değişiklikler RT2 ile push edilir.

---

## 11. SignalR Hubs

### 11.1 RT1 — `/hubs/transactions`

**Amaç:** İşlem detay sayfası (S07) real-time güncellemeleri.

| Konu | Değer |
|------|-------|
| Auth | JWT query param: `?access_token=eyJ...` |
| Bağlantı | S07 açılışında join, ayrılışta leave |

**Client → Server:**

| Method | Param | Açıklama |
|--------|-------|----------|
| `JoinTransaction` | `transactionId` | İşlem odasına katıl |
| `LeaveTransaction` | `transactionId` | İşlem odasından ayrıl |

**Server → Client:**

| Event | Payload | Tetikleyici |
|-------|---------|-------------|
| `TransactionStatusChanged` | `{ transactionId, fromStatus, toStatus, timestamp }` | State geçişi |
| `CountdownSync` | `{ transactionId, timeoutType, remainingSeconds, frozen, frozenReason }` | 30 sn periyodik + freeze/unfreeze |
| `PaymentDetected` | `{ transactionId, amount, txHash, status }` | Blockchain'de ödeme tespiti |
| `PaymentConfirmed` | `{ transactionId, amount, txHash, confirmations }` | 20 blok onay |
| `DisputeUpdate` | `{ transactionId, disputeId, status, autoCheckResult }` | Dispute durumu değişimi |
| `FlagResolved` | `{ transactionId, reviewStatus }` | Admin flag kararı |
| `EmergencyHoldApplied` | `{ transactionId, message }` | İşlem EMERGENCY_HOLD'a alındı |
| `EmergencyHoldReleased` | `{ transactionId, action, resumedStatus }` | EMERGENCY_HOLD kaldırıldı (RESUME/CANCEL) |

### 11.2 RT2 — `/hubs/notifications`

**Amaç:** Anlık bildirim push (S05 header, S11, toast).

| Konu | Değer |
|------|-------|
| Auth | JWT query param |
| Bağlantı | Login sonrası otomatik, logout'ta disconnect |

**Server → Client:**

| Event | Payload | Tetikleyici |
|-------|---------|-------------|
| `NewNotification` | `{ id, type, message, targetType, targetId, createdAt }` | Yeni bildirim |
| `UnreadCountChanged` | `{ unreadCount }` | Okunmamış sayı değişimi |
| `TelegramConnected` | `{ username }` | Telegram bağlantısı tamamlandı |
| `DiscordConnected` | `{ username }` | Discord bağlantısı tamamlandı |
| `MaintenanceStatusChanged` | `{ active, type, message, plannedEnd }` | Bakım/kesinti durumu değişti (C08 banner) |

**Admin grubu (WP9):** Aşağıdaki üç olay yalnızca **admin grubuna** (`admins`) push edilir — admin (`role ∈ {admin, super_admin}`) bağlantıları RT2'ye bağlanırken otomatik bu gruba katılır; admin olmayan istemciler bu payload'ları almaz (`Clients.All` yerine grup kapsamı). Kalıcı kayıt: ilgili AuditLog satırı (`RECONCILIATION_MISMATCH` / `HOT_WALLET_THRESHOLD_BREACHED`) ve `PlatformSteamBot.Status`.

| Event | Payload | Tetikleyici |
|-------|---------|-------------|
| `AdminBotStatusChanged` | `{ botId, steamId, displayName, previousStatus, newStatus, reason, changedAt }` | Platform Steam bot RESTRICTED/BANNED/OFFLINE'a geçti veya havuzdan çıkarıldı (T69) |
| `AdminReconciliationMismatch` | `{ scope, address, token, expected, actual, delta, blockNumber, detectedAt }` | Günlük mutabakat zincir↔ledger uyuşmazlığı tespit etti (T76) |
| `AdminHotWalletThresholdBreached` | `{ token, direction, threshold, actual, blockNumber, detectedAt }` | Hot wallet bakiyesi eşik aştı/altına düştü (T77) |

---

## 12. GAP Kararları

Traceability matrix oluşturulurken tespit edilen ve çözülen GAP'ler:

| # | GAP | Karar |
|---|-----|-------|
| GAP-1 | S01 platform istatistikleri — 03'te tanımsız | P1 public endpoint, 15 dk cache |
| GAP-2 | Cüzdan değişikliğinde Steam re-auth detayı | A5-A6 ayrı re-verify + 5 dk TTL tek kullanımlık reAuthToken |
| GAP-3 | Telegram doğrulama — frontend nasıl öğrenir | SignalR push (RT2 `TelegramConnected`), sayfa yenileme fallback (U6) |
| GAP-4 | Discord OAuth callback eksik | U10b — settings domain'inde callback, SignalR push |
| GAP-5 | Admin doğrudan işlem iptali | AD19 — ayrı endpoint, `CANCEL_TRANSACTIONS` permission. **Downstream etki:** 02 §7/§16, 03 §8, 04 S16/S19, 05 §4.2, 06 kontrol |
| GAP-6 | Logout endpoint eksik | A8 — `POST /auth/logout` |
| GAP-7 | Admin detay sayfalarında nested veri | Tümü nested response, tek istisna: AD16b işlem geçmişi ayrı paginated endpoint |
| GAP-8 | Bildirim tıklama navigasyonu | `targetType` + `targetId` — frontend route mapping |

---

*Skinora — API Design v2.2*
