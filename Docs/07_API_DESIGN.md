# Skinora — API Design

**Versiyon: v2.2** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-04-21

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
| §11a.3 Sanctions screening | AD19b, AD19c |
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

**Amaç:** Terms of Service kabul + 18+ yaş beyanı (ilk kayıt). Tek adımda ToS kabul ve soft yaş gate self-attestation (02 §21.1, 03 §11a.2).

| Konu | Değer |
|------|-------|
| Auth | Authenticated (henüz ToS kabul etmemiş) |

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

**Hatalar:** 409 `TOS_ALREADY_ACCEPTED`, 400 `VALIDATION_ERROR` (ageOver18 false/eksik veya tosVersion eksik)

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
  "role": "user",
  "language": "tr",
  "hasSellerWallet": true,
  "hasRefundWallet": false,
  "createdAt": "2026-03-10T08:00:00Z"
}
```

| Field | Açıklama |
|-------|----------|
| `role` | `"user"` veya `"admin"` — routing kararı |
| `mobileAuthenticatorActive` | İşlem başlatma kontrolü |
| `tosAccepted` | `false` → ToS modal |

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
  "successfulTransactionRate": 96.0,
  "cancelRate": 4.0,
  "sellerWalletAddress": "TXyz1234567890abcdef1234567890ab",
  "refundWalletAddress": "TAbcdef1234567890abcdef12345678cd",
  "mobileAuthenticatorActive": true
}
```

`sellerWalletAddress` / `refundWalletAddress`: Tam adres, `null` ise tanımlanmamış.

### 5.2 U2 — `GET /users/me/stats`

**Amaç:** Dashboard hızlı istatistikleri (S05).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Response (200) `data`:**
```json
{
  "completedTransactionCount": 24,
  "successfulTransactionRate": 96.0,
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
  "successfulTransactionRate": 96.0
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
  "commissionRate": 2.0,
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
  "commissionRate": 2.0,
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
  "inviteUrl": "https://skinora.com/transactions/guid",
  "buyerRegistered": false,
  "buyerNotified": false
}
```

**`availableActions` kuralları:**

| Aksiyon | Koşul |
|---------|-------|
| `canAccept` | buyer + CREATED + Steam ID eşleşme (veya açık link) |
| `canCancel` | (seller veya buyer) + aktif state + ödeme gönderilmemiş |
| `canDispute` | buyer + {ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED} + aktif dispute yok + aynı tür daha önce açılmamış |
| `canEscalate` | dispute var + otomatik kontrol tamamlanmış + henüz eskalasyon yok |

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

### 7.6 T6 — `POST /transactions/:id/accept`

**Amaç:** Alıcı işlemi kabul eder (S07, 03 §3.2).

| Konu | Değer |
|------|-------|
| Auth | Authenticated |

**Request:**
```json
{ "refundWalletAddress": "TAbcdef1234567890abcdef12345678cd" }
```

**Response (200) `data`:**
```json
{ "status": "ACCEPTED", "acceptedAt": "2026-03-16T14:45:00Z" }
```

**Doğrulama:** `refundWalletAddress` merkezi doğrulama pipeline'ından geçer: (1) TRC-20 format geçerliliği, (2) sanctions screening (02 §12.3). Geçersiz veya yaptırımlı adres → ilgili hata.

**Hatalar:** 409 `INVALID_STATE_TRANSITION`, 403 `STEAM_ID_MISMATCH`, 409 `ALREADY_ACCEPTED`, 400 `VALIDATION_ERROR`, 400 `INVALID_WALLET_ADDRESS`, 403 `SANCTIONS_MATCH`, 403 `WALLET_CHANGE_COOLDOWN_ACTIVE`

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
  "itemReturned": true,
  "paymentRefunded": false
}
```

**Hatalar:** 422 `PAYMENT_ALREADY_SENT`, 409 `INVALID_STATE_TRANSITION`, 403 `NOT_A_PARTY`, 400 `VALIDATION_ERROR`

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

**Hatalar:** 403 `NOT_BUYER`, 409 `INVALID_STATE_TRANSITION`, 409 `DUPLICATE_DISPUTE`, 409 `ACTIVE_DISPUTE_EXISTS`

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
| `MANAGE_SETTINGS` | Parametreleri yönet |
| `VIEW_STEAM_ACCOUNTS` | Steam hesaplarını görüntüle |
| `VIEW_USERS` | Kullanıcı detay görüntüle |
| `MANAGE_ROLES` | Rolleri yönet (süper admin) |
| `VIEW_AUDIT_LOG` | Audit log görüntüle |
| `CANCEL_TRANSACTIONS` | İşlemleri iptal et |
| `EMERGENCY_HOLD` | İşlemleri acil dondurma/kaldırma (AD19b, AD19c) |

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

**Query Params:** `type`, `reviewStatus`, `dateFrom`, `dateTo`, `sortBy`, `sortOrder`

**Response (200) `data.items[]`:**
```json
{
  "id": "flag-guid",
  "transactionId": "tx-guid",
  "type": "PRICE_DEVIATION",
  "reviewStatus": "PENDING",
  "seller": { "steamId": "...", "displayName": "...", "avatarUrl": "..." },
  "itemName": "AK-47 | Redline",
  "price": "100.00",
  "stablecoin": "USDT",
  "marketPrice": "50.00",
  "createdAt": "2026-03-16T13:00:00Z"
}
```

Ek field: `pendingCount` — bekleyen flag sayısı (badge).

### 9.3 AD3 — `GET /admin/flags/:id`

**Amaç:** Flag detay (S14). Permission: `VIEW_FLAGS`.

**Response (200) `data`:**
```json
{
  "id": "flag-guid",
  "type": "PRICE_DEVIATION",
  "reviewStatus": "PENDING",
  "createdAt": "2026-03-16T13:00:00Z",

  "flagDetail": {
    "inputPrice": "100.00",
    "marketPrice": "50.00",
    "deviationPercent": 100.0
  },

  "transaction": {
    "id": "tx-guid",
    "status": "FLAGGED",
    "itemName": "AK-47 | Redline",
    "itemImageUrl": "https://steamcdn.../abc.png",
    "price": "100.00",
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
  "reviewedBy": null,
  "reviewedAt": null,
  "adminNote": null
}
```

**`flagDetail` türe göre:**

| Tür | Yapı |
|-----|------|
| PRICE_DEVIATION | `{ inputPrice, marketPrice, deviationPercent }` |
| HIGH_VOLUME | `{ periodHours, transactionCount, totalVolume }` |
| ABNORMAL_BEHAVIOR | `{ pattern, description }` |
| MULTI_ACCOUNT | `{ matchType, matchValue, linkedAccounts: [{steamId, displayName}] }` |

### 9.4 AD4 — `POST /admin/flags/:id/approve`

**Amaç:** Flag onayla (S14). Permission: `MANAGE_FLAGS`.

**Request:**
```json
{ "note": "Fiyat makul, geçmişi temiz" }
```

`note`: Opsiyonel.

**Response (200) `data`:**
```json
{ "reviewStatus": "APPROVED", "transactionStatus": "CREATED", "reviewedAt": "..." }
```

**Hatalar:** 409 `ALREADY_REVIEWED`, 404 `FLAG_NOT_FOUND`

> **UI terminoloji notu:** API endpoint `/approve` kullanır, UI'da bu aksiyonun karşılığı **"İşleme Devam Et"** butonudur (flag false positive). Frontend mapping: `approve` → "İşleme Devam Et" (04 §S14).

### 9.5 AD5 — `POST /admin/flags/:id/reject`

**Amaç:** Flag reddet — işlem CANCELLED_ADMIN olur (S14). Permission: `MANAGE_FLAGS`.

> **UI terminoloji notu:** API endpoint `/reject` kullanır, UI'da bu aksiyonun karşılığı **"İptal Et"** butonudur (fraud doğrulanmış). Frontend mapping: `reject` → "İptal Et" (04 §S14).

**Request:**
```json
{ "note": "Fiyat manipülasyonu şüphesi" }
```

**Response (200) `data`:**
```json
{ "reviewStatus": "REJECTED", "transactionStatus": "CANCELLED_ADMIN", "reviewedAt": "..." }
```

**Hatalar:** AD4 ile aynı.

### 9.6 AD6 — `GET /admin/transactions`

**Amaç:** Tüm işlem listesi (S15). Permission: `VIEW_TRANSACTIONS`. Paginated.

**Query Params:** `status`, `stablecoin`, `dateFrom`, `dateTo`, `minAmount`, `maxAmount`, `search`, `sortBy`, `sortOrder`

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
  "completedAt": "2026-03-16T17:00:00Z"
}
```

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
| `notificationHistory` | Gönderilen bildirimler: `[{ type, recipient, channels, sentAt }]` |
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
      "key": "buyer_accept_timeout_hours",
      "value": "48",
      "category": "timeout",
      "label": "Alıcı kabul timeout'u",
      "description": "Alıcının işlemi kabul etme süresi",
      "unit": "saat",
      "valueType": "number"
    }
  ]
}
```

**Kategoriler:** `timeout`, `commission`, `transaction_limits`, `cancel_rules`, `new_account`, `gas_fee`, `fraud_detection`, `buyer_identification`, `geo_blocking`, `sanctions_screening`, `age_verification`, `blockchain_health`

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

**Amaç:** Steam bot hesapları (S18). Permission: `VIEW_STEAM_ACCOUNTS`.

**Response (200) `data`:**
```json
{
  "accounts": [
    {
      "id": "guid",
      "name": "Platform Hesap 1",
      "steamId": "76561198000000001",
      "status": "ACTIVE",
      "escrowedItemCount": 12,
      "dailyTradeOfferCount": 45,
      "dailyTradeOfferLimit": 200,
      "lastHealthCheck": "2026-03-16T14:25:00Z",
      "restrictionReason": null,
      "failoverStatus": "NONE",
      "recoveryTransactionCount": 0
    }
  ],
  "warningMessage": null
}
```

`status`: `ACTIVE`, `RESTRICTED`, `BANNED`, `OFFLINE` (06 §2.15). `warningMessage`: Sorunlu hesap varsa.

`failoverStatus`: `NONE` (normal), `RESTRICTED_NEW_TXN_DIVERTED` (yeni işlemler diğer botlara yönlendirildi), `ACTIVE_TXN_IN_RECOVERY` (aktif işlemler recovery/manual intervention'da). `recoveryTransactionCount`: Recovery'deki aktif işlem sayısı (02 §15).

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
    { "key": "EMERGENCY_HOLD", "label": "İşlemleri acil dondurma/kaldırma" }
  ]
}
```

> **Not:** `MANAGE_STEAM_RECOVERY` 04 §8.8 "Steam recovery yönet" satırının string identifier'ıdır — S18 Manual Recovery Başlat / not düşme / sorumlu admin atama akışlarını kapsar (fon/item güvenliği etkili, salt-okunur `VIEW_STEAM_ACCOUNTS` yetkisinden ayrı). T103 (S18) wire eder; T39 yalnızca katalog girişini sağlar.

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
    "reputationScore": 4.8
  },
  "stats": {
    "totalTransactions": 30,
    "completedTransactions": 24,
    "cancelledTransactions": 4,
    "flaggedTransactions": 2,
    "successfulTransactionRate": 80.0,
    "totalVolume": "5420.00",
    "lastTransactionAt": "2026-03-15T18:00:00Z"
  },
  "walletHistory": [
    { "type": "seller", "address": "TXyz...", "setAt": "...", "current": true }
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

`accountStatus`: `ACTIVE`, `DEACTIVATED`, `DELETED`. İşlem geçmişi bu response'a dahil değil — AD16b.

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
  "action": "SELLER_PAYOUT_SENT",
  "actor": { "steamId": "...", "displayName": "System" },
  "subject": { "steamId": "...", "displayName": "SellerPlayer" },
  "transactionId": "tx-guid",
  "detail": { "amount": "99.70", "stablecoin": "USDT", "txHash": "abc123..." },
  "createdAt": "2026-03-16T17:00:00Z"
}
```

`category`: `FUND_MOVEMENT`, `ADMIN_ACTION`, `SECURITY_EVENT`. `subject`: Opsiyonel.

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

> **Tasarım kararı:** ITEM_DELIVERED → EMERGENCY_HOLD → CANCEL zinciri yasaktır. Bu, AD19'daki "ITEM_DELIVERED'da standart iptal uygulanamaz" kuralıyla tutarlıdır. Admin bu durumda yalnızca RESUME yapabilir; exceptional resolution (ör. yanlış item teslimi) ayrı bir manuel süreçle ele alınır.

**Hatalar:** 409 `NOT_ON_HOLD`, 400 `VALIDATION_ERROR`, 422 `CANNOT_CANCEL_DELIVERED_HOLD` (ITEM_DELIVERED hold'unda CANCEL denemesi)

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
