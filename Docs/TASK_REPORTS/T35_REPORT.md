# T35 — Hesap ayarları (dil, bildirim tercihleri, Telegram/Discord bağlama)

**Faz:** F2 | **Durum:** ⏳ Yapım bitti — doğrulama bekliyor | **Tarih:** 2026-04-23

---

## Yapılan İşler

- **`GET /api/v1/users/me/settings` (U6, 07 §5.6):** `[Authorize(Authenticated)]` + `[RateLimit("user-read")]`. `AccountSettingsService` User (`PreferredLanguage`, `Email`, `EmailVerifiedAt`) + 3 external kanal (email/telegram/discord) için `UserNotificationPreference` satırını birleştirir; platform kanalı her zaman `enabled=true, canDisable=false` (04 §7.6).
- **`PUT /api/v1/users/me/settings/language` (U8, 07 §5.10):** Whitelist `en|zh|es|tr`; büyük-küçük harf farksız, persist sonrası `ToLowerInvariant`. `INVALID_LANGUAGE` → 400.
- **`PUT /api/v1/users/me/settings/notifications` (U7, 07 §5.9):** Yalnız gönderilen kanal güncellenir. Email özel: adres `User.Email`'de yaşar, adres değişirse `EmailVerifiedAt` null'a düşer (her değişiklik yeniden doğrulama gerektirir). Telegram/Discord aktif olmayan bir kanalda enabled toggle edilemez → 422 `CHANNEL_NOT_CONNECTED`.
- **Email doğrulama (`POST .../email/send-verification` + `.../email/verify`, U15/U16):** 6 haneli kod, Redis `skinora:settings:email_verify:{userId}` 10 dk TTL; cooldown `skinora:settings:email_verify_cooldown:{userId}` 1 dk. `IEmailSender` stub (`LoggingEmailSender`) masked recipient log — kod plaintext kayıt edilmez. Başarılı doğrulama: `User.EmailVerifiedAt = utcNow` + `UserNotificationPreference(EMAIL)` upsert (enabled=true, ExternalId=email). Hatalar: `NO_EMAIL_SET` 422, `VERIFICATION_COOLDOWN` 429, `VERIFICATION_CODE_EXPIRED` 422, `INVALID_VERIFICATION_CODE` 400.
- **Telegram connect/webhook/delete (U9/W1/U11, 07 §5.11, §5.11b, §5.14):** Connect endpoint `SKN-######` 6 haneli kod üretir (5 dk Redis TTL); webhook `/start {kod}` parse eder, `ITelegramVerificationStore.ConsumeAsync` atomic GETDEL ile tek kullanımlık redeem + `UserNotificationPreference(TELEGRAM)` upsert. ExternalId stabilite için `message.from.id` (Telegram user ID) — username opsiyonel fallback. Webhook 2 savunma katmanı: (a) `X-Telegram-Bot-Api-Secret-Token` header ordinal eşleşme (config'deki secret boşsa reddedilir, fail-safe), (b) Telegram retry storm'una karşı her outcome'da 200 döner (SignalR push T62'de).
- **Discord connect/callback/delete (U10/U10b/U12, 07 §5.12, §5.13, §5.15):** Connect authorize URL üretir + random 32-byte state'i Redis'e yazar. Callback state'i `ConsumeAsync` (tek kullanım), `IDiscordOAuthClient` ile `code → profile` exchange (stub deterministic sha256; T80 gerçek Discord OAuth devralır). Başarı `?discord=connected` redirect + `DiscordConnected` preference upsert; `invalid_state / denied / already_linked / exchange_failed` sebep ayrımı redirect query ile.
- **`PUT /api/v1/users/me/settings/steam/trade-url` (U17, 07 §5.16a, 08 §2.2):** `TradeUrlParser` URL parse (host `steamcommunity.com`, path `/tradeoffer/new/`, integer `partner`, alphanumeric `token`), 422 `INVALID_TRADE_URL`. Parse OK → `ITradeHoldChecker.CheckAsync(steamId, token)` çağrılır:
  - `Available=true, Active=true` → `MobileAuthenticatorVerified=true`, response OK `mobileAuthenticatorActive=true`.
  - `Available=true, Active=false` → `MobileAuthenticatorVerified=false`, response OK + `setupGuideUrl`.
  - `Available=false` → URL + parse edilmiş partner/token persist edilir ama response 503 `STEAM_API_UNAVAILABLE`, `MobileAuthenticatorVerified=false` (pending).
  - Çağrıda exception → otomatik `Available=false` ile 503'e düşülür (Steam down → pending; 07 §5.16a fallback).
- **Stub integrasyonlar:** `LoggingEmailSender` (T78 Resend devir), `StubDiscordOAuthClient` (T80 Discord gerçek devir), `StubTradeHoldChecker` (T64-T69 sidecar devir). Hepsi `TryAddScoped` / `TryAddSingleton` ile kaydedildi → DI swap için hazır.
- **Cross-module `INotificationPreferenceStore`:** Interface Skinora.Users'de (çünkü Settings servisleri oradaki kaynağa ihtiyaç duyuyor), implementation `NotificationPreferenceStore` Skinora.Notifications'da (UserNotificationPreference entity'sine sahip olan modül). Project reference grafiği `Notifications → Users` olduğu için tersi cycle yaratırdı; T34 `IActiveTransactionCounter` pattern'i birebir mirror edildi.
- **User entity + migration:** 4 yeni nullable alan (`EmailVerifiedAt`, `SteamTradeUrl`, `SteamTradePartner`, `SteamTradeAccessToken`) + UserConfiguration map + `20260423163805_T35_AddAccountSettingsFields` migration (dotnet ef ile üretildi). Mevcut veriyi etkilemez (hepsi nullable).
- **Yeni NuGet bağımlılıkları:** Skinora.Users → `Microsoft.EntityFrameworkCore 9.0.3` + `StackExchange.Redis 2.8.16` (AppDbContext + Redis stores için).

## Known Limitations (dokümansal devirler)

- **Email gerçek gönderim — T78 devir.** `LoggingEmailSender` kod ve masked recipient dışında bir şey yapmaz. T78 (Email entegrasyonu / Resend) DI swap ile devralır; interface sözleşmesi kalıcı.
- **Telegram bot push mesajları + webhook set-up — T79 devir.** Kod sözleşmesi (SKN-XXXXXX + webhook secret header) tamamlandı; bot tarafı setup (`setWebhook`, `getMe`, push delivery, RT2 SignalR `TelegramConnected`) T79 scope'unda. Şu an webhook kabul edip DB upsert'i yapıyor — cevap Telegram'a push olarak gitmiyor.
- **Discord OAuth token exchange — T80 devir.** `StubDiscordOAuthClient` deterministik fake profil döner. T80 Discord gerçek entegrasyonu `POST /oauth2/token` + `GET /users/@me` HTTP çağrılarını ekler; interface swap ile.
- **Steam sidecar trade-hold çağrısı — T64-T69 devir.** `StubTradeHoldChecker` default `Available=true, Active=false`. T64-T69 sidecar entegrasyonu `IEconService/GetTradeHoldDurations/v1` çağrısını DI swap ile devralır; 07 §5.16a sözleşmesi aynı.
- **SignalR push (TelegramConnected, DiscordConnected) — T62 devir.** 07 §5.11, §5.13 bot doğruladığında SignalR `TelegramConnected` / `DiscordConnected` event'i push etmeli. Hub altyapısı T62 (SignalR hub — bildirim push); T35 yalnızca DB bind yapar, RT2 kanalı yok.
- **Rate limit bucket'larının parametrizasyonu — T41 devir.** Endpoint'ler `user-write` + `user-read` + `auth` bucket'ları kullanıyor. Fine-grained bucket per-endpoint (örn. email send için özel cooldown) T41 Admin parametre yönetimi kapsamında.

## Etkilenen Modüller / Dosyalar

**Yeni — Skinora.Users/Application/Settings/:**
- `SettingsErrorCodes.cs`, `SettingsDtos.cs`
- `IAccountSettingsService.cs` + `AccountSettingsService.cs`
- `ILanguageService.cs` + `LanguageService.cs`
- `INotificationPreferenceStore.cs` (interface; impl Notifications'da)
- `INotificationPreferenceService.cs` + `NotificationPreferenceService.cs`
- `IEmailVerificationCodeStore.cs` + `RedisEmailVerificationCodeStore.cs` + `InMemoryEmailVerificationCodeStore.cs`
- `IEmailSender.cs` + `LoggingEmailSender.cs`
- `IEmailVerificationService.cs` + `EmailVerificationService.cs`
- `TelegramSettings.cs`, `ITelegramVerificationStore.cs` + `RedisTelegramVerificationStore.cs` + `InMemoryTelegramVerificationStore.cs`
- `ITelegramConnectionService.cs` + `TelegramConnectionService.cs`
- `DiscordSettings.cs`, `IDiscordOAuthStateStore.cs` + `RedisDiscordOAuthStateStore.cs` + `InMemoryDiscordOAuthStateStore.cs`
- `IDiscordOAuthClient.cs` + `StubDiscordOAuthClient.cs`
- `IDiscordConnectionService.cs` + `DiscordConnectionService.cs`
- `ITradeUrlParser.cs` (+ `TradeUrlParser`)
- `ITradeHoldChecker.cs` + `StubTradeHoldChecker.cs`
- `ISteamTradeUrlService.cs` + `SteamTradeUrlService.cs`

**Yeni — Skinora.Notifications/Application/Settings/:**
- `NotificationPreferenceStore.cs` — `INotificationPreferenceStore` concrete impl (AppDbContext üzerinden UserNotificationPreference CRUD + soft delete).

**Değişiklik — Skinora.Users:**
- `Domain/Entities/User.cs` — `EmailVerifiedAt`, `SteamTradeUrl`, `SteamTradePartner`, `SteamTradeAccessToken` alanları.
- `Infrastructure/Persistence/UserConfiguration.cs` — 4 yeni property map.
- `Skinora.Users.csproj` — `Microsoft.EntityFrameworkCore`, `StackExchange.Redis` package reference'ları.

**Değişiklik — API katmanı:**
- `Skinora.API/Controllers/UsersController.cs` — 11 yeni endpoint + `TryGetUserId` + `ValidationError` helper + 8 yeni DI parametre.
- `Skinora.API/Controllers/WebhooksController.cs` — **yeni** (`POST /webhooks/telegram`, generated regex `/start SKN-…` parser, secret header doğrulama).
- `Skinora.API/Configuration/UsersModule.cs` — `IConfiguration` parametresi alacak şekilde genişletildi; 14 yeni DI kaydı (Settings/Telegram/Discord/Email stores + services + stubs).
- `Skinora.API/Program.cs` — `AddUsersModule(configuration)` imza güncellemesi.

**Yeni — Migration:**
- `Skinora.Shared/Persistence/Migrations/20260423163805_T35_AddAccountSettingsFields.cs` + `.Designer.cs`
- `Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` — güncellendi.

**Yeni — Testler:**
- `backend/tests/Skinora.API.Tests/Integration/AccountSettingsEndpointTests.cs` — **23 integration test** (SQLite in-memory, WalletAddressEndpointTests factory pattern mirror'ı + Settings store swap'ları).

## Kabul Kriterleri Kontrolü

| # | Kriter (11 §T35) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `GET /users/me/settings` → hesap ayarları | ✓ | `UsersController.GetSettings` + `AccountSettingsService.GetAsync`. Test: `GetSettings_NewUser_ReturnsDefaultsPlatformAlwaysOn` (language=en + platform enabled+canDisable=false + email.verified=false). |
| 2 | `PUT /users/me/settings/language` → dil değiştirme (en/zh/es/tr) | ✓ | Whitelist + `ToLowerInvariant` persist. Testler: `UpdateLanguage_ValidCode_PersistsAndEchoes` (tr), `UpdateLanguage_InvalidCode_Returns400` (de/boş/`TRR` için 400 `INVALID_LANGUAGE`). |
| 3 | `PUT /users/me/settings/notifications` → bildirim tercihleri | ✓ | `NotificationPreferenceService.UpdateAsync`. Testler: `UpdateNotifications_TelegramDisabled_NoActiveRow_Returns422ChannelNotConnected`, `UpdateNotifications_EmailAddressSet_CreatesPrefRow`, `UpdateNotifications_EmailAddressChanged_InvalidatesVerification`. |
| 4 | POST/DELETE telegram/discord bağlantı endpoint'leri | ✓ | Telegram: `InitiateTelegramConnect` + `ProcessWebhookAsync` + `DisconnectTelegram`. Discord: `InitiateDiscordConnect` + `DiscordCallback` + `DisconnectDiscord`. Testler: `TelegramConnect_ThenWebhook_LinksChannel` (kod → /start → preference upsert, ExternalId=telegram user id), `TelegramWebhook_MissingSecret_Returns401`, `DisconnectTelegram_Idempotent_Ok`, `DiscordConnect_ReturnsAuthorizeUrl`, `DiscordCallback_ValidState_BindsAccountAndRedirects`, `DiscordCallback_UnknownState_RedirectsInvalidState`, `DiscordCallback_UserDenied_RedirectsDenied`. |
| 5 | Email doğrulama akışı (send-verification + verify) | ✓ | 6-digit Redis store 10 dk TTL + 1 dk cooldown, `LoggingEmailSender` stub. Testler: `SendEmailVerification_NoEmailSet_Returns422` (422 `NO_EMAIL_SET`), `SendEmailVerification_SendThenVerify_SetsEmailVerifiedAt` (kod → verify → `User.EmailVerifiedAt` populated), `VerifyEmail_WrongCode_Returns400InvalidCode`, `VerifyEmail_NoPendingCode_Returns422Expired`. |
| 6 | `PUT /users/me/settings/steam/trade-url` → trade URL kayıt + MA doğrulama | ✓ | `TradeUrlParser` + `ITradeHoldChecker`. Testler: `UpdateTradeUrl_InvalidFormat_Returns422`, `UpdateTradeUrl_MaActive_PersistsAndFlagsTrue` (MA=true + persist), `UpdateTradeUrl_MaInactive_PersistsAndReturnsSetupGuide` (MA=false + setupGuideUrl), `UpdateTradeUrl_SteamApiUnavailable_Returns503ButPersists` (503 + URL persist + MA=false pending). |

## Doğrulama Kontrol Listesi (11 §T35)

- [x] **07 §5.6–§5.16a tüm endpoint'ler var mı?** — 12 endpoint (§5.6 U6, §5.7 U15, §5.8 U16, §5.9 U7, §5.10 U8, §5.11 U9, §5.11b W1, §5.12 U10, §5.13 U10b, §5.14 U11, §5.15 U12, §5.16a U17) — `UsersController` + `WebhooksController`'da hepsi mevcut.
- [x] **Trade URL kaydında MA kontrolü yapılıyor mu (08 §2.2)?** — Evet. `ITradeHoldChecker` abstraction'ı 08 §2.2 `GetTradeHoldDurations` semantiği ile; stub conservative `Active=false + setupGuideUrl` döner (T64-T69'a devir), gerçek sidecar DI swap ile devralır. 3 branch (active/inactive/api-unavailable) integration testlerle kapalı.

## Test Sonuçları

**Build (lokal, Release):**
```bash
dotnet build Skinora.sln -c Release
```
→ Build succeeded. **0 Warning(s). 0 Error(s).** 00:00:09.

**Tam backend test koşumu (lokal, Release):**

| Test projesi | Geçen | Başarısız | Süre |
|---|---|---|---|
| `Skinora.Shared.Tests` | 166 | 0 | 1 dk 57 s |
| `Skinora.API.Tests` (unit + SQLite integration) | **171** | **0** | 3 dk 14 s |
| `Skinora.Auth.Tests` | 85 | 0 | 3 dk 34 s |
| `Skinora.Transactions.Tests` | 68 | 0 | 3 dk 54 s |
| `Skinora.Platform.Tests` | 28 | 0 | 29 s |
| `Skinora.Notifications.Tests` | 25 | 0 | 1 dk 44 s |
| `Skinora.Steam.Tests` | 21 | 0 | 2 dk 47 s |
| `Skinora.Admin.Tests` | 20 | 0 | 1 dk 29 s |
| `Skinora.Fraud.Tests` | 12 | 0 | 1 dk 51 s |
| `Skinora.Disputes.Tests` | 11 | 0 | 1 dk 45 s |
| `Skinora.Payments.Tests` | 6 | 0 | 1 dk 2 s |
| **Toplam** | **613** | **0** | — |

`AccountSettingsEndpointTests` 23/23 PASS:
1. `GetSettings_NewUser_ReturnsDefaultsPlatformAlwaysOn`
2. `UpdateLanguage_ValidCode_PersistsAndEchoes`
3-5. `UpdateLanguage_InvalidCode_Returns400` (3 theory row: `de`, empty, `TRR`)
6. `UpdateNotifications_TelegramDisabled_NoActiveRow_Returns422ChannelNotConnected`
7. `UpdateNotifications_EmailAddressSet_CreatesPrefRow`
8. `UpdateNotifications_EmailAddressChanged_InvalidatesVerification`
9. `SendEmailVerification_NoEmailSet_Returns422`
10. `SendEmailVerification_SendThenVerify_SetsEmailVerifiedAt`
11. `VerifyEmail_WrongCode_Returns400InvalidCode`
12. `VerifyEmail_NoPendingCode_Returns422Expired`
13. `TelegramConnect_ThenWebhook_LinksChannel`
14. `TelegramWebhook_MissingSecret_Returns401`
15. `DisconnectTelegram_Idempotent_Ok`
16. `DiscordConnect_ReturnsAuthorizeUrl`
17. `DiscordCallback_ValidState_BindsAccountAndRedirects`
18. `DiscordCallback_UnknownState_RedirectsInvalidState`
19. `DiscordCallback_UserDenied_RedirectsDenied`
20. `UpdateTradeUrl_InvalidFormat_Returns422`
21. `UpdateTradeUrl_MaActive_PersistsAndFlagsTrue`
22. `UpdateTradeUrl_MaInactive_PersistsAndReturnsSetupGuide`
23. `UpdateTradeUrl_SteamApiUnavailable_Returns503ButPersists`

**Format:**
```bash
dotnet format Skinora.sln --verify-no-changes --no-restore
```
→ 0 değişiklik.

## Altyapı Değişiklikleri

- **Skinora.Users package reference'ları:** `Microsoft.EntityFrameworkCore 9.0.3` + `StackExchange.Redis 2.8.16`. Daha önce yalnız Skinora.Shared'a dayanıyordu; AppDbContext sorguları + Redis store'ları için gerekli.
- **Konfigürasyon section'ları:** `Telegram:*` (3 anahtar), `Discord:*` (7 anahtar). `appsettings.*.json`'a eklenmedi çünkü prod değerleri env-var override ile ship edilmeli (test harness UseSetting ile doldurur). Bootstrap fail-fast (§8.9) bu bloklar için hit etmiyor — MVP'de endpoint config eksikse 4xx döner.
- **Migration:** `20260423163805_T35_AddAccountSettingsFields`. 4 nullable kolon ekler. Down tam reverse. T28 CI idempotent job'u bu migration'ı yeni container'a 2× uygulayacak.
- **Test harness pattern:** `AccountSettingsEndpointTests.Factory` — WalletAddressEndpointTests.Factory + 4 yeni in-memory store swap (`EmailCode`, `TelegramVerification`, `DiscordOAuthState`) + `ConfigurableTradeHoldStub`. T35+ settings/webhook akışları için reusable.

## Notlar

- **Working tree (Adım -1):** Temiz. `git status --short` boş (T34 `82f3999` merge'den sonra main yakalandı).
- **Startup CI check (Adım 0):** Son 3 main run `24845642208` ✓, `24845641743` ✓, `24841594435` ✓ — hepsi success (T34 merge CI + T33 M1 chore).
- **Dış varsayımlar (Adım 4):**
  - 4 MVP dil kodu (en/zh/es/tr) — 07 §5.10 explicit ✓
  - `UserNotificationPreference` entity + `NotificationChannel` enum (T23 ✓) ✓
  - `User.PreferredLanguage` + `User.Email` + `User.MobileAuthenticatorVerified` mevcut (T18, T30) ✓
  - Steam trade URL format (`/tradeoffer/new/?partner=N&token=T`) — 08 §2.2 explicit ✓
  - Discord OAuth `authorize` endpoint + `state` parametresi (CSRF) — Discord public docs ✓
  - Telegram `X-Telegram-Bot-Api-Secret-Token` webhook header — Telegram Bot API docs ✓
  - `StackExchange.Redis` 2.8.16 (Auth modülünde mevcut) — aynı sürüm Users modülüne eklendi ✓
- **Cross-module design kararı (`INotificationPreferenceStore`):** T34 `IActiveTransactionCounter` pattern'i birebir mirror; interface tüketici tarafında (Users), impl sahibi tarafında (Notifications), DI API composition root'ta. Alternatif (Settings'i Notifications'a taşımak) endpoint path'ini `/users/me/settings/*` olarak tutmak ile ters düşerdi.
- **Bundled-PR check (Bitiş Kapısı):** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+(\.[0-9]+)?[a-z]?'` → sadece `T35`. Yabancı task commit'i yok.
- **Post-merge CI watch:** Validate chat'i merge sonrası main CI'yi izler (validate.md Adım 18).

## Commit & PR

(bu bölüm push + PR açılışı sonrası doldurulur)
