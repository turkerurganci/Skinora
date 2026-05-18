# T78 — Email entegrasyonu (Resend)

**Faz:** F4 | **Durum:** ⏳ Devam ediyor (yapım bitti — validate chat bekliyor) | **Tarih:** 2026-05-17

---

## Yapılan İşler

- **`DeliveryStatus.DEFERRED`** enum değeri eklendi (FAILED'dan ayrı — geçici hata sonrası deferred-tier retry için ara durum). `CK_NotificationDeliveries_Deferred_LastError` CHECK constraint FAILED ile simetrik (LastError NOT NULL zorunlu). Migration `T78_AddDeferredDeliveryStatus` sadece CHECK constraint ekler (enum string olarak persist edildiği için schema değişikliği yok).
- **`Skinora.Shared.Email` transport katmanı** (yeni klasör, modüller arası bağımlılığı sadeleştirmek için): `IResendEmailClient` low-level kontratı, `ResendEmailClient` `HttpClient`-tabanlı impl, `ResendSettings` config sınıfı (Provider/ApiKey/BaseUrl/FromAddress/Timeout/WebhookSigningSecret/WebhookReplayWindowSeconds), `ResendSendEmailRequest`/`ResendSendEmailResult` DTO'ları. 08 §4.3 hata sınıflandırması: 5xx + 429 + network/timeout → `ResendTransientException`, 4xx (422 validation, 401 auth, 403, …) → `ResendPermanentException`. Resend community NuGet (pre-1.0) yerine raw HttpClient kullanıldı — plan ("basit HTTP wrapper da yeterli", 08 §4.1) + testability + tek endpoint için minimal yüzey.
- **`SvixSignatureVerifier`** (Skinora.Shared.Email) — Svix dokümante algoritmasının (`docs.svix.com/receiving/verifying-payloads/how-manual`) referans implementasyonu: `whsec_BASE64` prefix soyma + base64-decode → HMAC-SHA256 over `"{msg_id}.{timestamp}.{body}"` → base64 → header `v1,sig` space-delimited entry içinde constant-time compare. `VerifyResult` enum 6 durum (Valid / InvalidSecretConfiguration / MissingHeaders / TimestampUnparseable / TimestampOutOfWindow / SignatureMismatch).
- **`EmailHtmlRenderer` + `IEmailHtmlRenderer` + `EmailCategory`** (Skinora.Shared.Email) — minimal HTML wrapper renderer: kategori bazlı accent rengi (Transaction mavi / Security kırmızı / Account siyah / Timeout turuncu), 4 dil banner + footer (en/tr/es/zh, 05 §7.3 fallback EN), trusted compile-time string'ler encode edilmez (Türkçe karakterler korunur), user-supplied title/body `WebUtility.HtmlEncode` + newline → `<br />`. Plain-text fallback Resend `text` field'ı için. Polished templates MVP-OUT-016 (post-MVP).
- **`ChannelDeliveryException`** (Skinora.Notifications) — `Transient` / `Permanent` ayrımcı subclass'lar. Tüm channel handler'lar bu tiplere maplenir; dispatcher hangi tiriaja düşeceğini bilebilsin diye (T79/T80 Telegram + Discord aynı pattern'i devralacak).
- **`ResendEmailNotificationChannelHandler`** (Skinora.Notifications) — T37 stub'ın Resend swap'i. `INotificationChannelHandler` impl: notification dispatcher pipeline'ından gelen recipient email + rendered template'i alır, latest NotificationDelivery satırından `Notification.UserId`/`Notification.Type` lookup ile locale + EmailCategory çözer, `IResendEmailClient.SendAsync` çağrısı yapar. `ResendPermanentException` → `PermanentChannelDeliveryException`, `ResendTransientException` → `TransientChannelDeliveryException` maplemesi.
- **`EmailCategoryMap`** (Skinora.Notifications) — `NotificationType` → `EmailCategory` exhaustive map. Eksik tip throws (silent mis-categorisation engellenir). Tüm 25 NotificationType eşlendi: 15 Transaction + 1 Timeout + 5 Security (FLAGGED, DISPUTE_RESULT, FLAG_RESOLVED, EMERGENCY_HOLD_APPLIED, EMERGENCY_HOLD_RELEASED) + 4 Account (admin alerts).
- **`ResendVerificationEmailSender`** (Skinora.Users) — T35'in `LoggingEmailSender` stub'ının Resend swap'i. `IEmailSender.SendVerificationCodeAsync` impl: User.Email → User.PreferredLanguage lookup, locale-spesifik subject + body copy (en/tr/es/zh, "Verify your Skinora email" / "Skinora e-postanızı doğrulayın" / …), Account kategorisi HTML wrapper, IResendEmailClient.SendAsync çağrısı. `LoggingEmailSender.MaskAddress` helper'ı verification + logging için yeniden kullanıldı.
- **`NotificationDeliveryJob` güncellemesi** — Hangfire immediate-tier retry (mevcut 1dk/5dk/15dk korundu) + iki yeni davranış:
  - `PermanentChannelDeliveryException` → row FAILED + admin alert + throw **etmez** (Hangfire retry tetiklemez).
  - Final attempt `TransientChannelDeliveryException` (veya bilinmeyen exception) → row DEFERRED + `DeferredNotificationDeliveryJob` 30dk delayed schedule + throw etmez. Intermediate attempts mevcut davranışta (FAILED + throw → Hangfire next retry).
- **`DeferredNotificationDeliveryJob`** (yeni) — 30dk/1sa/4sa deferred tier (08 §4.3). `[AutomaticRetry(Attempts = 0)]` — kendi tier state machine'i yönetir (Hangfire'ın yerine):
  - Tier 1 (initial DEFERRED'den +30dk schedule'a göre) success → SENT, transient fail → DEFERRED + tier 2 schedule (+60dk), permanent fail → FAILED + alert.
  - Tier 2 (tier 1 fail'den +60dk) transient fail → DEFERRED + tier 3 schedule (+4sa).
  - Tier 3 (tier 2 fail'den +4sa) transient fail → FAILED + admin alert (deferred tier tükendi).
  - Invalid tier defensive no-op; already-SENT/FAILED row no-op.
- **`ResendWebhookHandler` + envelope/event/result modelleri** (Skinora.Notifications.Application.Webhooks) — 5 event tipi (`email.bounced`/`delivery_delayed`/`complained`/`failed`/`suppressed`) + `Unknown` forward-compat. Action matrix per 08 §4.3:
  - `bounced`/`complained`/`suppressed` → User by email lookup → `UserNotificationPreference.IsEnabled = false` (EMAIL channel) + warning log; user yoksa `UnknownRecipient`; preference yoksa veya zaten disabled ise `Idempotent`.
  - `failed` → warning log + admin attention (NotificationDelivery row tarafı immediate/deferred pipeline'ın sorumluluğunda — webhook event ek finalize tetiklemez, **K1** in-app notification dispatch T-future).
  - `delivery_delayed` → info log only (Resend retry kendi tarafında).
  - `Unknown` → 200 Acknowledged (forward-compat).
- **`ResendWebhookSignatureMiddleware`** (Skinora.API.Middleware) — path-scoped (`/api/v1/webhooks/resend`) Svix sig + replay + idempotency. `SvixSignatureVerifier` üzerinden imza kontrol; başarısızsa 401 + specific error code; başarılıysa `ProcessedNonces` tablosuna `Source="resend", Nonce=svix-id` INSERT (sidecar webhook idempotency tablosuyla aynı; T63b retention job zaten temizliyor). Unique-violation duplicate → 200 + `Idempotent` early return. Mevcut `WebhookSignatureMiddleware` (Steam/blockchain HMAC) ile paralel, ayrı concern (Resend Svix format farklı).
- **`ResendWebhooksController`** (Skinora.API.Controllers) — `POST /api/v1/webhooks/resend` `AllowAnonymous`; middleware downstream'de body trusted. `IResendWebhookHandler.HandleAsync` çağrısı + envelope + result ApiResponse'a sarılır.
- **DI wiring (composition root)**:
  - `Program.cs`: `Resend` section binding + `SvixSignatureVerifier` singleton + provider switch ile koşullu `AddHttpClient<IResendEmailClient, ResendEmailClient>` (provider=resend dışında HTTP client hiç DI'ya girmez → CI yanlışlıkla ağa çıkmaz) + `ResendWebhookSignatureMiddleware` pipeline'a eklendi (Steam middleware'inden sonra).
  - `NotificationsModule.AddNotificationsModule(IConfiguration)` — yeni signature; provider switch'e göre `EmailNotificationChannelHandler` (stub) veya `ResendEmailNotificationChannelHandler` register; her zaman `IEmailHtmlRenderer` (singleton) + `DeferredNotificationDeliveryJob` + `IResendWebhookHandler` register.
  - `UsersModule` — provider switch'e göre `LoggingEmailSender` veya `ResendVerificationEmailSender` `TryAddScoped` ile `IEmailSender`.
  - `appsettings.json` — yeni `Resend` section (Provider="logging" default, ApiKey/WebhookSigningSecret `REPLACE_IN_ENV`).
- **`Docs/INTEGRATION_RUNBOOKS/RESEND_SETUP.md`** (yeni klasör + dosya) — operasyon runbook'u: Resend hesap kurulumu (domain, API key, webhook endpoint), 4 DNS kaydı (DKIM x2, SPF, DMARC, Return-Path) doğrulama tablo + CLI; secret rotation prosedürü; lokal/CI/staging/production provider matris; sandbox akış (delivered@/bounced@/complained@resend.dev); izleme eşikleri; yaygın hata tablosu.

## Etkilenen Modüller / Dosyalar

### Skinora.Shared (yeni `Email/` klasörü)
- [`backend/src/Skinora.Shared/Email/IResendEmailClient.cs`](../../backend/src/Skinora.Shared/Email/IResendEmailClient.cs) — low-level transport kontratı (yeni)
- [`backend/src/Skinora.Shared/Email/ResendEmailClient.cs`](../../backend/src/Skinora.Shared/Email/ResendEmailClient.cs) — HttpClient impl, error classification (yeni)
- [`backend/src/Skinora.Shared/Email/ResendEmailExceptions.cs`](../../backend/src/Skinora.Shared/Email/ResendEmailExceptions.cs) — `ResendEmailException` base + `Transient` / `Permanent` (yeni)
- [`backend/src/Skinora.Shared/Email/ResendEmailModels.cs`](../../backend/src/Skinora.Shared/Email/ResendEmailModels.cs) — request/response records (yeni)
- [`backend/src/Skinora.Shared/Email/ResendSettings.cs`](../../backend/src/Skinora.Shared/Email/ResendSettings.cs) — config sınıfı (yeni)
- [`backend/src/Skinora.Shared/Email/SvixSignatureVerifier.cs`](../../backend/src/Skinora.Shared/Email/SvixSignatureVerifier.cs) — Svix HMAC-SHA256 referans impl (yeni)
- [`backend/src/Skinora.Shared/Email/EmailCategory.cs`](../../backend/src/Skinora.Shared/Email/EmailCategory.cs) — Transaction/Security/Account/Timeout enum (yeni)
- [`backend/src/Skinora.Shared/Email/IEmailHtmlRenderer.cs`](../../backend/src/Skinora.Shared/Email/IEmailHtmlRenderer.cs) — wrapper kontratı + `EmailHtmlRendererResult` (yeni)
- [`backend/src/Skinora.Shared/Email/EmailHtmlRenderer.cs`](../../backend/src/Skinora.Shared/Email/EmailHtmlRenderer.cs) — HTML + plain-text wrapper impl (yeni)
- [`backend/src/Skinora.Shared/Enums/DeliveryStatus.cs`](../../backend/src/Skinora.Shared/Enums/DeliveryStatus.cs) — `DEFERRED` enum eklendi (PENDING/SENT/DEFERRED/FAILED)
- [`backend/src/Skinora.Shared/Persistence/Migrations/20260517195341_T78_AddDeferredDeliveryStatus.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260517195341_T78_AddDeferredDeliveryStatus.cs) — CHECK constraint migration (yeni)
- [`backend/src/Skinora.Shared/Persistence/Migrations/20260517195341_T78_AddDeferredDeliveryStatus.Designer.cs`](../../backend/src/Skinora.Shared/Persistence/Migrations/20260517195341_T78_AddDeferredDeliveryStatus.Designer.cs) — designer (yeni)
- `backend/src/Skinora.Shared/Persistence/Migrations/AppDbContextModelSnapshot.cs` — auto-regenerated

### Skinora.Notifications
- [`backend/src/Modules/Skinora.Notifications/Application/Channels/ChannelDeliveryException.cs`](../../backend/src/Modules/Skinora.Notifications/Application/Channels/ChannelDeliveryException.cs) — base + Transient/Permanent (yeni)
- [`backend/src/Modules/Skinora.Notifications/Infrastructure/Email/EmailCategoryMap.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Email/EmailCategoryMap.cs) — NotificationType → EmailCategory exhaustive map (yeni)
- [`backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/ResendEmailNotificationChannelHandler.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Channels/ResendEmailNotificationChannelHandler.cs) — Resend channel handler (yeni)
- [`backend/src/Modules/Skinora.Notifications/Infrastructure/Persistence/NotificationDeliveryConfiguration.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/Persistence/NotificationDeliveryConfiguration.cs) — `CK_NotificationDeliveries_Deferred_LastError` CHECK eklendi
- [`backend/src/Modules/Skinora.Notifications/Infrastructure/DeliveryJobs/NotificationDeliveryJob.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/DeliveryJobs/NotificationDeliveryJob.cs) — Permanent/Transient classification + DEFERRED transition + DeferredJob schedule
- [`backend/src/Modules/Skinora.Notifications/Infrastructure/DeliveryJobs/DeferredNotificationDeliveryJob.cs`](../../backend/src/Modules/Skinora.Notifications/Infrastructure/DeliveryJobs/DeferredNotificationDeliveryJob.cs) — 3-tier state machine (yeni)
- [`backend/src/Modules/Skinora.Notifications/Application/Webhooks/IResendWebhookHandler.cs`](../../backend/src/Modules/Skinora.Notifications/Application/Webhooks/IResendWebhookHandler.cs) (yeni)
- [`backend/src/Modules/Skinora.Notifications/Application/Webhooks/ResendWebhookHandler.cs`](../../backend/src/Modules/Skinora.Notifications/Application/Webhooks/ResendWebhookHandler.cs) — event dispatch + EMAIL preference disable (yeni)
- [`backend/src/Modules/Skinora.Notifications/Application/Webhooks/ResendWebhookEnvelope.cs`](../../backend/src/Modules/Skinora.Notifications/Application/Webhooks/ResendWebhookEnvelope.cs) (yeni)
- [`backend/src/Modules/Skinora.Notifications/Application/Webhooks/ResendWebhookEventType.cs`](../../backend/src/Modules/Skinora.Notifications/Application/Webhooks/ResendWebhookEventType.cs) (yeni)
- [`backend/src/Modules/Skinora.Notifications/Application/Webhooks/ResendWebhookResult.cs`](../../backend/src/Modules/Skinora.Notifications/Application/Webhooks/ResendWebhookResult.cs) (yeni)
- [`backend/src/Modules/Skinora.Notifications/NotificationsModule.cs`](../../backend/src/Modules/Skinora.Notifications/NotificationsModule.cs) — IConfiguration overload + provider switch + DeferredJob + WebhookHandler + HtmlRenderer DI

### Skinora.Users
- [`backend/src/Modules/Skinora.Users/Application/Settings/ResendVerificationEmailSender.cs`](../../backend/src/Modules/Skinora.Users/Application/Settings/ResendVerificationEmailSender.cs) — Resend-backed verification email sender (yeni)

### Skinora.API
- [`backend/src/Skinora.API/Middleware/ResendWebhookSignatureMiddleware.cs`](../../backend/src/Skinora.API/Middleware/ResendWebhookSignatureMiddleware.cs) — Svix sig + replay + ProcessedNonces idempotency (yeni)
- [`backend/src/Skinora.API/Controllers/ResendWebhooksController.cs`](../../backend/src/Skinora.API/Controllers/ResendWebhooksController.cs) — POST /api/v1/webhooks/resend (yeni)
- [`backend/src/Skinora.API/Configuration/UsersModule.cs`](../../backend/src/Skinora.API/Configuration/UsersModule.cs) — IEmailSender provider switch
- [`backend/src/Skinora.API/Program.cs`](../../backend/src/Skinora.API/Program.cs) — Resend binding + Svix verifier + koşullu HttpClient + middleware kayıt + NotificationsModule(IConfiguration)
- [`backend/src/Skinora.API/appsettings.json`](../../backend/src/Skinora.API/appsettings.json) — `Resend` section

### Testler
- [`backend/tests/Skinora.Shared.Tests/Unit/Email/ResendEmailClientTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Email/ResendEmailClientTests.cs) — HTTP 200/422/429/5xx/401/timeout/malformed body sınıflandırma (yeni, 12 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/Email/SvixSignatureVerifierTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Email/SvixSignatureVerifierTests.cs) — valid + multi-version + wrong secret + body tamper + replay + missing headers + non-numeric ts (yeni, 8 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/Email/EmailHtmlRendererTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/Email/EmailHtmlRendererTests.cs) — banner/footer locale + HTML escape + newline + culture fallback (yeni, 10 test)
- [`backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs`](../../backend/tests/Skinora.Shared.Tests/Unit/EnumTests.cs) — DeliveryStatus 3→4 count
- [`backend/tests/Skinora.Notifications.Tests/Unit/Email/EmailCategoryMapTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Unit/Email/EmailCategoryMapTests.cs) — exhaustive map + tag formatting (yeni, 8 test)
- [`backend/tests/Skinora.Notifications.Tests/Unit/Webhooks/ResendWebhookHandlerTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Unit/Webhooks/ResendWebhookHandlerTests.cs) — ParseEventType + MaskEmail (yeni, 14 test)
- [`backend/tests/Skinora.Notifications.Tests/Integration/DeferredNotificationDeliveryJobTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Integration/DeferredNotificationDeliveryJobTests.cs) — tier 1 success / tier 1 fail → tier 2 schedule / tier 3 exhaust → alert / permanent / already-SENT / invalid tier (yeni, 6 test)
- [`backend/tests/Skinora.Notifications.Tests/Integration/NotificationDeliveryJobTests.cs`](../../backend/tests/Skinora.Notifications.Tests/Integration/NotificationDeliveryJobTests.cs) — final attempt DEFERRED + permanent failure paths (2 yeni test; eski `FinalAttemptFailure_RaisesAdminAlertAndThrows` semantic değişti)
- [`backend/tests/Skinora.API.Tests/Integration/ResendWebhookEndpointTests.cs`](../../backend/tests/Skinora.API.Tests/Integration/ResendWebhookEndpointTests.cs) — missing headers / invalid sig / stale ts / bounce / suppress / duplicate svix-id idempotent / unknown recipient / delayed acknowledged / unknown type (yeni, 9 integration test)

### Docs
- [`Docs/INTEGRATION_RUNBOOKS/RESEND_SETUP.md`](../INTEGRATION_RUNBOOKS/RESEND_SETUP.md) — operasyon runbook (yeni klasör + dosya)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | IEmailSender interface + Resend implementasyonu | ✓ | İki seviye: `Skinora.Shared.Email.IResendEmailClient` low-level transport (yeni) + iki tüketici (`ResendEmailNotificationChannelHandler` notif dispatch, `ResendVerificationEmailSender` Users T35). Provider switch (`Resend:Provider=logging\|resend`) DI'da swap'i kontrol eder. |
| 2 | POST /emails çağrısı (Authorization: Bearer) | ✓ | `ResendEmailClient.SendAsync` → `_httpClient.PostAsJsonAsync("emails", ...)`, `DefaultRequestHeaders.Authorization = Bearer {ApiKey}`. Unit test `SendAsync_HappyPath_SendsBearerAuthAndJsonBody` doğrular (asserts method=POST, path=/emails, Bearer scheme, body içerik). |
| 3 | Email şablonları: .resx ile 4 dil, kanal bazlı format (işlem, güvenlik, hesap, timeout) | ✓ | İçerik tarafı T37 `NotificationTemplates.{en\|tr\|es\|zh}.resx` (4 dil, mevcut). Kanal bazlı format: `EmailCategory` enum (Transaction/Security/Account/Timeout) + `EmailCategoryMap` exhaustive NotificationType→EmailCategory + `EmailHtmlRenderer` kategoriye göre accent renk + 4 dil banner/footer chrome. Verification email Account kategorisinde + 4 dil subject/body copy ResendVerificationEmailSender içinde. |
| 4 | Retry: 5xx → 3 deneme (1dk, 5dk, 15dk), 422 → retry yok | ✓ | `NotificationDeliveryJob` `[AutomaticRetry(Attempts=3, DelaysInSeconds={60,300,900})]` (mevcut T37); `ResendEmailClient` 5xx + 429 → `ResendTransientException` → `TransientChannelDeliveryException` → job re-throw → Hangfire retry; 422 + 4xx → `ResendPermanentException` → `PermanentChannelDeliveryException` → job FAILED + alert + **no throw** (Hangfire retry tetiklenmez). Test `ResendEmailClientTests` 7 farklı status code'u, `NotificationDeliveryJobTests.RunAsync_PermanentFailure_MarksFailedAlertsAndSwallows` permanent path doğrular. |
| 5 | Deferred: geçici hata → DEFERRED state, arka plan job (30dk, 1sa, 4sa) | ✓ | `DeliveryStatus.DEFERRED` enum + CHECK constraint. NotificationDeliveryJob immediate budget tükendiğinde DEFERRED + `DeferredNotificationDeliveryJob` (yeni) 30dk delayed enqueue. DeferredJob 3-tier state machine: tier 1 fail → tier 2 (+60dk), tier 2 fail → tier 3 (+4sa), tier 3 fail → FAILED + alert. Test `DeferredNotificationDeliveryJobTests` 6 senaryo (success/tier escalation/exhaust/permanent/already-SENT/invalid tier). |
| 6 | Resend webhook handler: bounced, delivery_delayed, complained, failed, suppressed | ✓ | `ResendWebhookHandler` 5 event handle: bounced/complained/suppressed → User by email lookup → `UserNotificationPreference.IsEnabled=false`; failed → admin attention warning; delivery_delayed → info log. `ResendWebhookEventType` enum 5 + Unknown forward-compat. `ResendWebhookHandlerTests` 8 ParseEventType test (5 known + lower/missing prefix/null/empty). Integration `ResendWebhookEndpointTests` bounce + suppressed full pipeline + delayed acknowledged + unknown type. |
| 7 | Webhook güvenlik: Svix header doğrulama, replay koruması (5dk), idempotency (svix-id) | ✓ | `SvixSignatureVerifier` `whsec_` base64 secret + HMAC-SHA256({msg_id}.{ts}.{body}) + space-delimited `v1,sig` constant-time compare. `ResendWebhookSignatureMiddleware` headers missing → 401, invalid sig → 401, stale ts (>300s) → 401, duplicate svix-id → 200 + Idempotent early return (`ProcessedNonces` UNIQUE INSERT race-safe). Test `SvixSignatureVerifierTests` 8 sig path + `ResendWebhookEndpointTests` 5 HTTP path (missing headers, invalid sig, stale ts, duplicate svix-id, unknown recipient). |
| 8 | DNS: DKIM, SPF, DMARC, Return-Path | ✓ | `Docs/INTEGRATION_RUNBOOKS/RESEND_SETUP.md` §2 dört DNS kaydı tablo + dig CLI doğrulama + mail-tester referans. SPF "include" notu (mevcut TXT'ye ekleme, yeni satır açma) RFC 7208 compliance. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (Shared) | ✓ 242/242 | `dotnet test tests/Skinora.Shared.Tests/` — 12 ResendEmailClient + 8 SvixSignatureVerifier + 10 EmailHtmlRenderer + 1 DeliveryStatus enum count update; toplam +42 |
| Unit (Notifications) | ✓ 49/49 | 8 EmailCategoryMap + 14 ResendWebhookHandler dahil; toplam +22 unit |
| Integration (Notifications) | ✓ 74/74 | 6 DeferredNotificationDeliveryJob + 2 NotificationDeliveryJob (permanent + final-deferred semantic) dahil |
| Unit + Integration (Users) | ✓ 16/16 | Regresyon |
| Integration (API) | ✓ 416/416 | 9 ResendWebhookEndpoint dahil; T77 base 374 + 8 yeni + 34 önceki net delta |
| Integration (Auth) | ✓ 93/93 | Regresyon |
| Integration (Transactions) | ✓ 657/657 | Regresyon |
| Integration (Steam) | ✓ 54/54 | Regresyon |
| Integration (Disputes) | ✓ 36/36 | Regresyon |
| Integration (Admin) | ✓ 20/20 | Regresyon |
| Integration (Platform) | ✓ 163/163 | Regresyon |
| Integration (Fraud) | ✓ 64/64 | Regresyon |
| Integration (Payments) | ✓ 6/6 | Regresyon |
| Integration (Realtime) | ✓ 25/25 | Regresyon |
| **Toplam** | **✓ 1915/1915** | Lokal SQL Server + SQLite-backed integration paritesi |
| dotnet format | ✓ Δ=0 | `dotnet format --verify-no-changes` exit 0 |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Validate chat'inde bağımsız PASS bekliyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** `20260517195341_T78_AddDeferredDeliveryStatus` — `NotificationDeliveries.CK_NotificationDeliveries_Deferred_LastError` CHECK constraint (DEFERRED state'in FAILED ile simetrik LastError invariant). Enum string olarak persist edildiği için sütun değişikliği yok. F1+ migration zinciri 1 yeni satır (toplam zincir uzunluğu T28 InitialCreate'den itibaren).
- **Config/env değişikliği:** `appsettings.json`'a yeni `Resend` section (`Provider="logging"` default + 5 alan). Production deploy ek env: `Resend__ApiKey`, `Resend__WebhookSigningSecret`, `Resend__Provider=resend`, `Resend__FromAddress` (override). Runbook §3 secret rotation prosedürü.
- **Docker değişikliği:** Yok — Resend tek dış HTTP servisi, mevcut HttpClient infra üzerinden. DNS kayıtları operasyonel (`Docs/INTEGRATION_RUNBOOKS/RESEND_SETUP.md` §2).
- **Yeni production dep:** Yok (raw HttpClient + System.Net.Http.Json + BCL HMAC). `System.Net.Http.Json` + `Microsoft.Extensions.Http` + `Microsoft.Extensions.Options` + `Microsoft.Extensions.Logging.Abstractions` transitively EFCore'dan zaten geliyordu; Skinora.Shared.csproj'da explicit eklenmedi.

## Commit & PR

- **Branch:** `task/T78-resend-email-integration`
- **Commit:** `347b061` — `T78: Email entegrasyonu (Resend)`
- **PR:** [#119](https://github.com/turkerurganci/Skinora/pull/119)
- **CI:** ⏳ izlemede (push edildi, son CI run sonucu PR/status güncellenirken yansıtılır)

## Known Limitations / Follow-up

- **K1 — Webhook event sonrası in-app bildirim devri (T-future):** 08 §4.3 "Kullanıcıya platform-içi bildirim" satırı (bounce: "Email adresinize ulaşılamıyor", suppressed: "Email adresiniz kara listede") T78 kapsamında **dispatch edilmiyor**. ResendWebhookHandler yalnız preference disable + log. Sebep: in-app bildirim için yeni NotificationType (EMAIL_BOUNCED / EMAIL_SUPPRESSED) + 4-dil .resx çoğaltması + dispatcher fanout'ta EMAIL channel skip (sonsuz döngü engelleme) gerekir; T78 scope'unu odakta tutmak için ayrı task'a (T-future) devredildi. Kullanıcı UI'da preference'ın disabled olduğunu zaten `/users/me/settings` ile görür.
- **K2 — `email.failed` per-NotificationDelivery correlation (T-future):** Resend webhook `email.failed` event'i recipient + `email_id` taşır; bizim `NotificationDelivery` tablosu Resend'in `email_id`'sini persist etmiyor. Şu an `failed` event sadece warning log fire eder; ilgili NotificationDelivery'yi otomatik FAILED'a çekmiyor (zaten immediate/deferred pipeline FAILED'a çekiyor). Per-message correlation için `NotificationDelivery.ProviderMessageId` column eklenmesi + ResendEmailNotificationChannelHandler send sonrası persist + webhook handler lookup gerekir.
- **K3 — Sandbox HTTP test CI'da yok (operasyonel):** `delivered@resend.dev` / `bounced@resend.dev` test mailbox'ları gerçek Resend API çağrısı gerektirir; CI ağ izolasyonu nedeniyle sadece runbook §5'te manuel staging adımı olarak belgelendi. Production deploy öncesi DevOps tarafından bir kez çalıştırılır.
- **K4 — DNS doğrulamaları manuel (operasyonel):** RESEND_SETUP.md §2 DKIM/SPF/DMARC/Return-Path tablosu adım adım veriyor ama runtime DNS health check yok. DNS bozulursa Resend send 422 döner; bu durum mevcut admin alert pipeline tarafından yakalanır. Otomasyon eklemek için Prometheus dnsCheck exporter gerekir — T16 monitoring follow-up.
- **K5 — Provider Resend dışı yedek (post-MVP):** 08 §4.4 Resend down → SendGrid devri için "abstraction layer ile birkaç saatlik iş" der. T78 abstraction'ı kuruyor (`IResendEmailClient` low-level) ama alternative implementation eklenmedi. Üst seviyede `IEmailSender` + `INotificationChannelHandler` provider-agnostic; SendGrid impl ekleme post-MVP.
- **K6 — Hangfire AutomaticRetry conservative Exception classification:** NotificationDeliveryJob bilinmeyen `Exception` tiplerini transient kabul ediyor (pre-T78 davranışla geriye uyumluluk). Telegram (T79) ve Discord (T80) çevrildiğinde kendi PermanentChannelDeliveryException maplemelerini eklemeli — aksi halde permanent başarısızlıklar gereksiz yere retry pipeline'a girer.

## Notlar

- **Working tree:** temiz (T77 PR #118 merge sonrası).
- **Adım 0 main CI startup check (T11.2):** Son 3 main run conclusion=success: `26000220452` (T77 #118 — push), `26000220451` (T77 #118 — docker-publish), `25997787137` (T76 #117). PASS.
- **Dış varsayım doğrulama (Adım 4 — feedback_check_external_assumptions):**
  - **Resend `POST /emails` (08 §4.2):** ✓ resmi doc `https://resend.com/docs/api-reference/emails/send-email` Bearer auth + `from/to/subject/html` JSON + 200/422/429/4xx/5xx response. Web fetch 2026-05-17.
  - **Resend NuGet community paketi:** ✓ `Resend` paketi NuGet'te mevcut, latest stable `0.5.1` (pre-1.0). **Kullanılmadı** — plan 08 §4.1 "basit HTTP wrapper da yeterli" + pre-1.0 dependency riski + tek endpoint coverage için raw HttpClient daha temiz (testability + minimal yüzey).
  - **Svix signing scheme:** ✓ resmi doc `https://docs.svix.com/receiving/verifying-payloads/how-manual` `whsec_` prefix + base64-decode secret, HMAC-SHA256(msg_id.timestamp.body), header `svix-signature: v1,base64sig` space-delimited entries. .NET impl manuel — Svix SDK opsiyonel, küçük yüzey için kendi `SvixSignatureVerifier`'ımız.
  - **Resend webhook events (08 §4.3):** ✓ 5 event tipi (bounced/delivery_delayed/complained/failed/suppressed) Resend dashboard endpoint config seçeneklerinde mevcut. K1 (in-app notification) ve K2 (per-message correlation) T-future devir.
  - **Sandbox test mailbox'ları:** ✓ Resend `delivered@/bounced@/complained@resend.dev` documented. K3 manuel staging.
- **Architecture seçimler:**
  - **`Skinora.Shared.Email`** yerleşimi (cross-module reuse): `IResendEmailClient` + `EmailHtmlRenderer` hem Notifications hem Users tarafından kullanılıyor. Notifications zaten Users'ı reference ediyor (NotificationDispatcher → User entity), o yüzden Users.IEmailSender → ResendVerificationEmailSender → Shared.Email.IResendEmailClient pattern circular dep yaratmıyor.
  - **`ResendWebhookSignatureMiddleware`** ayrı middleware (mevcut `WebhookSignatureMiddleware` Steam+blockchain HMAC için): Resend Svix format farklı (header isimleri svix-id/svix-timestamp/svix-signature, secret format `whsec_`, signing scheme `{msg_id}.{ts}.{body}` farklı). Path-scoped middleware ayrımı tek dosya HMAC switch'inden temiz.
  - **DEFERRED state CHECK constraint** (FAILED ile simetrik LastError NOT NULL): Plan açıkça istemiyor ama 06 §3.13a state-dependent CHECK paterniyle tutarlı — `(Status <> 'X') OR (LastError IS NOT NULL)` invariantı her başarısızlık state'i için aynı kalır.
  - **Two-tier explicit scheduling (DeferredJob):** Hangfire AutomaticRetry tek backoff schedule destekler (1dk/5dk/15dk OR 30dk/1sa/4sa, ikisini birden değil). İki tier için ya AutomaticRetry yok (`Attempts=0`) + manuel `Schedule()` ya da iki ayrı job class. İkincisi daha temiz + log line'larda `tier=N` izlenebilir.
- **Feedback yansıma:**
  - `feedback_think_through_fully`: scope sunumu öncesi `IEmailSender` swap kapsamı + DNS doc yeri + Resend SDK karar — proje sahibine 2 soru sunuldu, default önerilerle birlikte.
  - `feedback_validate_placement`: `Skinora.Shared.Email` klasör seçimi proje sahibinden onay alınmadan koyuldu ama `feedback_question_destructive_proposals` ihlali değil (yeni klasör, mevcut hiçbir şeyi taşımıyor); Notifications.Infrastructure.Email başlangıçta kullanılan yerden silindi çünkü Users.Settings cross-module ref oluşmaması için Shared'a taşındı (deliberate refactor).
  - `feedback_no_edit_permission_asks`: scope onayı sonrasında implementasyon + test + format + report tek akışta yapıldı, ara onay sorulmadı.
