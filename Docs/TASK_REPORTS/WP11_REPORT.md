# WP11 — Auth UI wire-up + ToS reprompt + brute-force lock

**Faz:** PRE_F6 (P4 — Kullanıcı/FE) | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-06-18

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
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- **Migration: Yok** — `CurrentUserDto` plain record (EF model değil); ToS logic + rate-limit attribute/middleware; EF entity/config değişmedi.
- **Config/env: Yok yeni** — `NEXT_PUBLIC_TOS_VERSION` (mevcut, default "1.0") reprompt karşılaştırması için kullanılır.
- **Docker: Yok.**

## Commit & PR
- Branch: `task/WP11-auth-ui-wireup`
- Commit: `ce95cf7` — WP11: Auth UI wire-up
- PR: #182
- CI: ⏳ İzleniyor

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
