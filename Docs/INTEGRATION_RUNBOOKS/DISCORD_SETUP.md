# Discord Bot Kurulum Runbook (T80 — 08 §6.1–§6.5)

Bu runbook Discord bildirim kanalını sıfırdan ayağa kaldırmak için
gerekli operasyon adımlarını tanımlar. Hem ilk kurulum (production
deploy) hem de secret rotation senaryolarını kapsar.

## 1. Discord Application + Bot Oluşturma

1. [Discord Developer Portal](https://discord.com/developers/applications)'a
   admin Discord hesabıyla giriş yap.
2. **New Application** → isim `Skinora` (ör.); team account önerilir.
3. Sol menüden **Bot** → **Add Bot** → bot otomatik oluşur. Bot
   kullanıcı adı uygulama adından miras alınır; gerekirse değiştir.
4. Bot panelinde:
   - **Reset Token** → bot token'ını al. Token formatı:
     `MTEx...` (Base64-benzeri uzun string). Sızdığı an
     bot kaçırılmış sayılır → token rotate edilir, yeni token
     production env'a yazılır.
   - **Privileged Gateway Intents** → MVP'de hiçbiri açılmaz
     (intents=0). Skinora bot'u sadece DM gönderir, gateway
     event'lerini dinlemez.
   - **Public Bot** → false önerilir (sadece resmi davetlerle
     guild'lere katılır).
5. Sol menüden **OAuth2** → **General**:
   - **Client ID** kopyala (production env'a).
   - **Reset Secret** → Client Secret kopyala (production env'a).
   - **Redirects** → Skinora callback URL'sini ekle, ör.
     `https://skinora.com/api/v1/users/me/settings/discord/callback`.
     URL exact-match zorunlu; trailing slash, query string olamaz.

## 2. MVP Guild Install (Skinora Discord Sunucusu)

08 §6.1 — MVP'de bot DM gönderebilmek için **mutual guild** gerekli
(kullanıcı ve bot aynı sunucuda olmalı). Skinora resmi Discord
sunucusu kurulur ve kullanıcıların katılması teşvik edilir.

1. Discord sunucusu oluştur (`Skinora Community`).
2. Developer Portal → **OAuth2** → **URL Generator**:
   - **Scopes:** `bot` (DM göndermek için gateway permission'a
     ihtiyaç yok — Create DM + Send Message API çağrılarıyla yapılır)
   - **Bot Permissions:** 0 (hiçbiri). MVP'de bot guild içinde
     mesajlaşmaz; ileride mesaj atması gerekirse ek permission
     açılır.
3. Üretilen URL admin tarafından kullanılır → "Authorize" → bot
   Skinora sunucusuna eklenir.
4. Frontend "Discord bildirim aç" akışında kullanıcıya "Skinora
   Discord sunucusuna katılın" yönlendirmesi yapılır (bağlantı
   kurulurken).
5. Büyüme aşamasında **user-install** desteği değerlendirilir —
   kullanıcı bot'u kendi hesabına ekler ve guild önkoşulu kalkar
   (08 §6.1 ikinci tabloda dokümante).

## 3. Konfigürasyon

Backend `appsettings.json`'da varsayılan değerler:

```json
"Discord": {
  "Provider": "logging",
  "ClientId": "REPLACE_IN_ENV",
  "ClientSecret": "REPLACE_IN_ENV",
  "BotToken": "REPLACE_IN_ENV",
  "AuthorizeUrl": "https://discord.com/api/oauth2/authorize",
  "BaseUrl": "https://discord.com/api/v10",
  "RedirectUri": "https://skinora.com/api/v1/users/me/settings/discord/callback",
  "Scope": "identify",
  "StateTtlSeconds": 600,
  "SuccessRedirectUrl": "/settings?discord=connected",
  "FailureRedirectUrl": "/settings?discord=error",
  "TimeoutSeconds": 10,
  "GlobalRatePerSecond": 45,
  "DmChannelCacheTtlHours": 24,
  "MaxRetries": 3
}
```

Production env override:

| Değişken | Değer |
|----------|-------|
| `Discord__Provider` | `discord` |
| `Discord__ClientId` | Developer Portal Client ID |
| `Discord__ClientSecret` | Developer Portal Client Secret |
| `Discord__BotToken` | Developer Portal Bot Token |
| `Discord__RedirectUri` | Production callback URL (Developer Portal'daki ile **exact match**) |

`Provider=logging` (default) iken backend Discord API'sına HTTP
çağrısı yapmaz — `LoggingDiscordNotificationChannelHandler` ve
`StubDiscordOAuthClient` stub'lar pipeline'ı in-memory işletir. Bu
sayede yanlış yapılandırılmış bir ortam asla canlı Discord'a
ulaşamaz (fail-closed).

## 4. Bağlantı Akışı Doğrulama

Konfigürasyon ayağa kalktıktan sonra test akışı:

1. Test hesabıyla Skinora'ya giriş yap.
2. Settings → "Discord bildirimlerini aç" → Discord OAuth ekranına
   yönlendirilirsin (`?client_id=...&state=...&scope=identify`).
3. İzin ver → Discord callback'e döner →
   `/users/me/settings/discord/callback?code=...&state=...`.
4. Beklenen redirect: `/settings?discord=connected`.
5. Backend log'da:
   ```
   Discord channel auto-disabled — target=...   (yok)
   Discord DM delivered — target=...            (varsayılan)
   ```
6. Skinora'dan bot'a test mesajı tetikle (admin → manual notification
   send) → kullanıcının Discord DM'ine mesaj gider.

Bot'un DM açabilmesi için kullanıcının Skinora Discord sunucusuna
katılmış olması gerekir; aksi halde `Create DM` 403 (`reason=mutual_guild_required`)
döner.

## 5. Hata Senaryoları (08 §6.4)

| Hata | Trigger | Beklenen davranış |
|------|---------|-------------------|
| `access_denied` | Kullanıcı OAuth ekranında reddetti | `?discord=error&reason=denied` |
| `invalid_grant` | Auth code expired / replayed | `?discord=error&reason=expired` |
| State mismatch | CSRF veya session timeout | `?discord=error&reason=invalid_state` |
| Token exchange 5xx | Discord OAuth API down | `?discord=error&reason=exchange_failed` |
| Bot DM 401 | Bot token revoked / rotated | Admin alert, DM kuyruğu pause |
| Bot DM 403 + createDM | Mutual guild yok | Email/platform fallback + "Skinora sunucusuna katılın" mesajı |
| Bot DM 403 + sendMessage (50007) | Kullanıcı DM kapatmış | Preference auto-disable + "DM ayarlarınızı açın" mesajı |
| Bot DM 404 | Channel deleted server-side | DM cache invalidate + retry once + preference disable |
| Bot DM 429 | Rate limit | `Retry-After` honor, rate limiter bucket pause |
| Bot DM 5xx | Discord API down | 3 retry (1dk / 5dk / 15dk) |

## 6. Secret Rotation Prosedürü

### 6.1 Bot Token Rotation

1. Developer Portal → Bot → **Reset Token** → yeni token al.
2. Production secret store'a yeni token'ı yaz.
3. Backend env güncelle (`Discord__BotToken`) → rolling restart.
4. Eski token derhal devre dışı kalır (Discord one-token-at-a-time).
5. Restart sonrası birkaç dakika 401 alabilir (DM kuyruğu pause olur);
   eski token kullanılan eski request'ler timeout olur.

### 6.2 Client Secret Rotation

1. Developer Portal → OAuth2 → **Reset Secret**.
2. Production secret store'a yaz, backend env güncelle
   (`Discord__ClientSecret`) → rolling restart.
3. Yeni secret yalnız OAuth flow'da kullanılır — restart sırasında
   bir kullanıcı yarım kalmış OAuth flow'da ise `invalid_grant`
   alır (`?reason=expired` redirect) ve baştan başlar.

### 6.3 Redirect URI Değişikliği

1. Developer Portal'a yeni URI ekle (eski URI'yi geçici tut).
2. Backend env güncelle (`Discord__RedirectUri`) → rolling restart.
3. Tüm replica'lar geçince Developer Portal'dan eski URI'yi sil.
4. Geçiş sırasında yarım OAuth flow'lar kırılmaz çünkü her iki URI
   da geçerli kalır.

## 7. İzleme & Limitler

| Metrik | Tipik | Kaynak |
|--------|-------|--------|
| Global rate cap | ~50 req/s | Discord docs |
| Per-bucket rate | endpoint+resource bazında | `X-RateLimit-Limit` header |
| Reset window | resource bazında | `X-RateLimit-Reset-After` header |
| 429 retry | `retry_after` saniye (float) | Response body / `Retry-After` header |

Header-driven limiter (`DiscordRateLimiter`) Discord'un yayınladığı
bucket id'leri (`X-RateLimit-Bucket`) baz alır; sabit kodlanmış
limit yoktur. `Discord:GlobalRatePerSecond` (default 45) global
ceiling; Discord'un yayınladığı limit değişirse ayar update edilir.

## 8. Sandbox / Staging / Prod Matrisi

| Ortam | Provider | Bot ekstra |
|-------|----------|-------------|
| Local dev | `logging` | Hiçbiri — stub kullanılır |
| CI | `logging` | Hiçbiri — testler stub'la koşar |
| Staging | `discord` | Ayrı staging bot + staging redirect URI |
| Production | `discord` | Production bot + production redirect URI |

## 9. Yaygın Hatalar

- **OAuth callback'te `invalid_grant`**: Auth code zaten kullanılmış
  veya 10 dk'dan eski → kullanıcı baştan başlar.
- **Bot DM "Cannot send messages to this user"**: Kullanıcı server
  DM ayarını kapatmış. Preference auto-disable; email fallback ile
  bilgilendirilir.
- **Bot DM "Cannot create DM"**: Kullanıcı Skinora sunucusunda
  değil. Email fallback ile "sunucuya katıl" mesajı.
- **OAuth `redirect_uri_mismatch`**: Developer Portal'daki URI
  ile `Discord__RedirectUri` exact-match değil. Trailing slash,
  şema (http/https), port, query string dikkat.

## 10. Referanslar

- [Discord Developer Portal](https://discord.com/developers/applications)
- [OAuth2 docs](https://discord.com/developers/docs/topics/oauth2)
- [Rate Limits](https://discord.com/developers/docs/topics/rate-limits)
- [User Resource](https://discord.com/developers/docs/resources/user)
- [Message Resource](https://discord.com/developers/docs/resources/message)
- [Allowed Mentions](https://discord.com/developers/docs/resources/message#allowed-mentions-object)
