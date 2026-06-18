# WP11 — Auth UI wire-up + ToS reprompt + brute-force lock

**Faz:** PRE_F6 (P4 — Kullanıcı/FE) | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-06-18

---

## Yapılan İşler

WP11, kullanıcının **UI'dan gerçekten login olmasını** sağlar. Tarama, üç auth yüzeyinin (callback token-store, ToS-accept, authenticator) UI-only stub olduğunu doğruladı: callback yalnız `?status` okuyup `POST /auth/refresh` çağırmadığı için `localStorage["access_token"]` hiç yazılmıyordu → `isAuthenticated` daima `false`.

**Frontend (T107'yi açan çekirdek):**
- Callback `success`/`new_user` → `POST /auth/refresh` (HttpOnly refresh cookie'yi access token'a çevirir) → token store; `new_user` → ToS modal `POST /auth/tos/accept`'e bağlandı (409 `TOS_ALREADY_ACCEPTED` = zaten kabul → devam).
- `apiClient`: `credentials: "include"` + 401 → **tek-uçuş** refresh → orijinal isteği **tek kez** yeniden dener; refresh başarısızsa session temizlenir + login'e redirect (auth-flow sayfalarında redirect-loop guard'ı).
- `auth-store`: `access_token` localStorage **tek yazıcısı** (`setAccessToken` persist eder, `logout` temizler).
- `TosRepromptGate` (global, Providers): `tosAcceptedVersion` ≠ mevcut sürümde re-prompt (T30).
- `mobile-authenticator` recheck → `/auth/me` `mobileAuthenticatorActive`'i yeniden okur; aktifse dashboard'a forward, değilse "stillInactive" notu (gerçek MA doğrulaması trade-URL/U17 → A7'de yapılır — login'de değil).
- i18n ×4 (`tos.reprompt.title/description`, `tos.acceptError`, `mobileAuthenticator.stillInactive`).

**Backend (MIGRATION YOK):**
- `CurrentUserDto` += `TosAcceptedVersion` (07 §4.5; mevcut `User.TosAcceptedVersion` alanı sunulur).
- `TosAcceptanceService`: 409 yalnız **aynı** versiyon yeniden gönderildiğinde; **farklı** versiyon = versiyon-upgrade (re-acceptance) — `TosAcceptedVersion`/`TosAcceptedAt` yeniden damgalanır, ilk kabuldeki `AgeConfirmedAt` **korunur** (`??=`). T30 reprompt'u mümkün kılar (07 §4.4).
- `RateLimitAttribute.RedirectToSteamCallbackOnReject` + `RateLimitMiddleware`: bayraklı endpoint'te rate-limit reddinde 429 yerine 302 → `{FrontendCallbackUrl}?error=temporarily_locked&retryAfter=N`. `GET /auth/steam` bayraklandı (05 §6.3, 07 §4.2 A1; FE bu hatayı zaten gösteriyordu).

## Etkilenen Modüller / Dosyalar

**Backend:**
- `Skinora.Auth/Application/Session/CurrentUserService.cs` — DTO + map
- `Skinora.Auth/Application/TosAcceptance/TosAcceptanceService.cs` — versiyon-upgrade
- `Skinora.API/Controllers/AuthController.cs` — `[RateLimit("auth", RedirectToSteamCallbackOnReject = true)]`
- `Skinora.API/RateLimiting/RateLimitAttribute.cs`, `RateLimitMiddleware.cs` — redirect dalı
- Test: `Skinora.Auth.Tests/.../TosAcceptanceServiceTests.cs`, `Skinora.API.Tests/.../{TosAcceptEndpointTests,AuthSessionEndpointTests,RateLimitTests}.cs` + yeni `Unit/RateLimiting/RateLimitMiddlewareTests.cs`

**Frontend:**
- `lib/api/auth.ts` (`acceptTos`, `MeResponse.tosAcceptedVersion`), `lib/api/client.ts` (`refreshAccessToken` + 401 interceptor + credentials), `lib/stores/auth-store.ts`, `lib/providers.tsx`
- `app/[locale]/auth/callback/page.tsx`, `app/[locale]/auth/mobile-authenticator/page.tsx`
- `components/auth/TosModal.tsx` (title/description override), `components/auth/TosRepromptGate.tsx` (yeni), `components/auth/index.ts`
- `i18n/messages/{en,es,tr,zh}.json`

**Docs:** `07_API_DESIGN.md` §4.4/§4.5, `PRE_F6_PLAN.md` WP11, `DEFERRED_BACKLOG.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Callback → `/auth/refresh` → token store; `isAuthenticated` çalışır | ✓ | `callback/page.tsx` refresh effect → `setAccessToken`; `client.ts` getAccessToken localStorage |
| 2 | ToS-accept → `POST /auth/tos/accept` (UI-only değil) | ✓ | `callback` `handleAcceptTos` → `acceptTos`; 409 graceful |
| 3 | 401 → refresh interceptor (tek-uçuş, tek retry) | ✓ | `client.ts` `refreshInFlight` + `isRetry`; `RateLimitMiddlewareTests`/`AuthSessionEndpointTests` refresh |
| 4 | ToS versiyon reprompt | ✓ | BE `tosAcceptedVersion` + versiyon-upgrade; FE `TosRepromptGate`; `TosAcceptanceServiceTests.NewVersionAfterBump...` |
| 5 | Login brute-force lock (`temporarily_locked` redirect) | ✓ | `RateLimitMiddleware` redirect dalı; `RateLimitMiddlewareTests.Rejected_WithRedirectFlag...` |
| 6 | Authenticator recheck spec-uyumlu (/auth/me) | ✓ | `mobile-authenticator/page.tsx` refetch → `mobileAuthenticatorActive` |
| 7 | Permission TTL cache (owner: by-design, eklenmez) | ✓ | T40 kararı korundu; DEFERRED_BACKLOG by-design notu |
| 8 | Migration yok / EF model değişmedi | ✓ | DTO + logic; `has-pending-model-changes` etkilenmez (entity/config değişmedi) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (BE) | ✓ | `RateLimitMiddlewareTests` 3/3 (redirect / 429-JSON / allowed) |
| Integration (BE) | ✓ | Auth.Tests **120/120**; API.Tests `RateLimit\|RateLimitMiddleware\|TosAccept\|AuthSession` **28/28**; `AuthSteam\|Authentication\|AuthReVerify` **34/34** (gerçek SQL Server, Docker UP) |
| FE type/lint/format | ✓ | `tsc --noEmit` 0, `eslint` 0, prettier (touched files) clean |
| FE i18n parity | ✓ | 4 dil **1181×4** identical key set |
| FE build | ✓ | `next build` — `/auth/callback`, `/auth/mobile-authenticator` dahil tüm route'lar |
| BE format | ✓ | `dotnet format --verify-no-changes --severity error` (değişen 4 proje) clean |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS — bağımsız validator (ayrı chat, 2026-06-18, rapor görülmeden kendi verdict'i) |
| Bulgu sayısı | 0 bloke-edici (S1/S2/S3 yok) |
| Düzeltme gerekli mi | Hayır |

**Validator kapıları:** Adım -1 working tree temiz · Adım 0 main son-3 CI success (`27756292902`/`27756292918`/`27754449178`) · Adım 0b repo memory WP11 satırı mevcut · Adım 8a task CI HEAD `6f71a97` run [`27781552553`](https://github.com/turkerurganci/Skinora/actions/runs/27781552553) tüm job success (+ `ccd1e77` run `27780917297`).

**Validator lokal kanıt (Release, gerçek SQL Server / Docker UP):** `dotnet build -c Release` **0W/0E**; `RateLimitMiddlewareTests` **3/3** (redirect / 429-JSON / allowed); `TosAcceptanceServiceTests` **7/7** (same-version 409 + new-version-upgrade-preserves-`AgeConfirmedAt`); API.Tests `TosAccept|RateLimit|AuthSession` **25/25**; regresyon `AuthSteam|Authentication|AuthReVerify` **34/34** (`GET /auth/steam`'e eklenen `[RateLimit]` mevcut auth akışını kırmadı) → toplam **69 test green**. FE: `tsc --noEmit` 0 + `eslint` 0 + prettier (WP11 dosyaları) clean + `next build` ✓ (`/auth/callback`, `/auth/mobile-authenticator` dahil).

**Bağımsız spec/kod teyidi:**
- **Brute-force redirect** spec-birebir: 07 §4.2 A1 literal `redirect: /auth/callback?error=temporarily_locked&retryAfter=300` + 05 §6.3 "klasik login brute-force Steam OpenID'de geçersiz → abuse throttling". `RateLimitMiddleware` bayraklı endpoint'te 302→`{FrontendCallbackUrl}?error=temporarily_locked&retryAfter=N` (server-config URL, kullanıcı girdisi değil; `BuildFrontendUrl` deseni); bayraksız `/auth/refresh` 429-JSON kalır (FE interceptor okur — `RateLimitTests` teyit). `GetMetadata<RateLimitAttribute>()` method-level attribute'u (redirect=true) class-level `[RateLimit("auth")]`'in üzerine çözer (ASP.NET action-after-controller metadata sırası).
- **ToS versiyon-upgrade**: 409 yalnız **aynı** versiyon; **farklı** versiyon re-stamp eder ama `AgeConfirmedAt ??= now` ilk 18+ beyanını korur (test `NewVersionAfterBump...UpgradesAndPreservesAgeConfirmation` birebir asserts). DTO `tosAcceptedVersion` `/auth/me`'de sunulur (07 §4.5; `AuthSessionEndpointTests` asserts).
- **401 interceptor** tek-uçuş (`refreshInFlight`) + tek retry (`isRetry`) + `/auth/refresh` özyineleme guard'ı; refresh-fail→`logout()`+login redirect (auth-flow redirect-loop guard). `access_token` localStorage tek-yazıcı (`auth-store`).
- **MA recheck** owner-kararı spec-uyumlu (/auth/me `mobileAuthenticatorActive`; gerçek MA doğrulaması U17→A7 trade-URL akışında — 03 §2.1/07 §4.8).
- **Migration yok**: `User.TosAcceptedVersion` kolonu InitialCreate'ten mevcut; diff'te migration/entity/`UserConfiguration`/snapshot değişikliği **yok** → model drift yok.

**Güvenlik kontrolü:** Secret sızıntısı yok · redirect URL server-config (open-redirect yok; callback `returnUrl` `RELATIVE_PATH_RE` ile sanitize) · ToS input validation (max 20 char, ageOver18 zorunlu) · yeni dış bağımlılık yok (package.json/csproj değişmedi).

**Non-blocking gözlemler (PASS'i etkilemez):**
1. 07 §4.2 wording "başarısız login denemesi sonrası" — implementasyon `GET /auth/steam` başlatmalarının **tümünü** sayar (Steam OpenID başlatma anında başarı/başarısızlık ayırt edilemez); davranış (N denemeden sonra geçici kilit → redirect) spec-uyumlu, owner-onaylı. Doc-precision NOTE → WP17.
2. `GET /auth/steam` redirect yolu integration test'te değil (Steam OpenID controller generic factory'de serviceable değil — middleware unit testi branch'i deterministik kapsar; attribute çözümü framework davranışı). Build bunu belgeledi.
3. `apiClient` refresh-fail sonrası `response.json()` fall-through boş gövdede SyntaxError verebilir — backend 401'de her zaman JSON envelope döner (pratikte erişilmez). Trivial robustness.
4. `AccountManagementSection` logout `localStorage.removeItem` artık `logout()` ile mükerrer (zararsız) → WP13 (rapor zaten kaydetti).

**Yapım raporu karşılaştırması:** Tam uyumlu — rapor kabul tablosu, test sayıları (Auth.Tests 120/120 tam suite CI-authoritative; validator WP11-spesifik 69 alt-kümeyi bağımsız koştu), known-limitations ve owner kararları bağımsız verdict'le birebir örtüşür; uyuşmazlık yok.

## Altyapı Değişiklikleri
- **Migration: Yok** — `CurrentUserDto` plain record (EF model değil); ToS logic + rate-limit attribute/middleware; EF entity/config değişmedi.
- **Config/env: Yok yeni** — `NEXT_PUBLIC_TOS_VERSION` (mevcut, default "1.0") reprompt karşılaştırması için kullanılır.
- **Docker: Yok.**

## Commit & PR
- Branch: `task/WP11-auth-ui-wireup`
- Commit: `ce95cf7` — WP11: Auth UI wire-up
- PR: #182
- CI: ✓ PASS — HEAD `ccd1e77` run [27780917297](https://github.com/turkerurganci/Skinora/actions/runs/27780917297) tüm job success (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker×2/Gate)

## Known Limitations / Follow-up
- **Permission TTL cache eklenmedi** (owner kararı — T40 "dinamiklik > performans" korundu); perf darboğazı kanıtı yoksa post-MVP.
- **MA recheck = /auth/me** — gerçek MA doğrulaması trade-URL kaydında (U17 → A7) yapılır; standalone `POST /auth/check-authenticator` literal bağlanmadı (trade-URL gerektirir; spec U17 akışına ait).
- **brute-force = rate-limit redirect** — ayrı per-IP progressive lockout altyapısı kurulmadı (05 §6.3 klasik brute-force'u Steam OpenID'de gereksiz görür); `auth` policy 10/dk üzerinden `temporarily_locked`.
- Cross-origin dağıtımda refresh cookie `SameSite=Strict` aynı-site (port-bağımsız) çalışır; tam cross-site dağıtım gerekirse cookie SameSite/CORS gözden geçirilir (mevcut default `/api/v1` same-origin reverse-proxy varsayar).
- `AccountManagementSection` logout manuel `localStorage.removeItem` artık `logout()` ile mükerrer (zararsız) — temizlik WP13'e bırakılabilir.

## Notlar
- **Working tree:** Adım -1 temiz (WP10 follow-up zaten main `3e68bc3`/#181'e merge edilmişti; yerel `task/WP10-followup-nonblocking` branch'i merge-edilmiş işin kopyasıydı).
- **Adım 0 (main CI):** son 3 run success (`27756292902`/`27756292918`/`27754449178`).
- **Dış varsayım doğrulama:** refresh cookie `HttpOnly; SameSite=Strict; Path=/api/v1/auth`; CORS `AllowCredentials()` + origin `localhost:3000`; FE `API_BASE_URL` default `/api/v1` (same-origin). → cookie yaklaşımı geçerli; `credentials:"include"` cross-port dev için eklendi. Backend endpoint'leri (`/auth/refresh`, `/auth/tos/accept`, `/auth/me`) zaten mevcut (kanıt: `AuthController.cs`).
- **Spec çelişkileri owner'a sunuldu** (AskUserQuestion): check-authenticator zamanlaması, brute-force kapsamı, permission cache (T40 ile çelişki), ToS reprompt yaklaşımı — hepsi karara bağlandı.
- **Anlama fazı:** 4 paralel keşif ajanı (FE auth flow / BE auth endpoints / brute-force+perm-cache state / doc references) file:line kanıtıyla haritaladı.
