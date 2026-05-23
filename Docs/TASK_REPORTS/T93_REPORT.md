# T93 — Profil sayfaları (S08, S09)

**Faz:** F5 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-23

---

## Yapılan İşler

`04 §7.4` (S08 kendi profil `/profile`) + `04 §7.5` (S09 public profil `/users/:steamId`) iki sayfa baştan yazıldı. T85 frontend skeleton'unun bıraktığı `<div>Profile</div>` stub'ı tam fonksiyonel profil ekranıyla değiştirildi; daha önce hiç route'u olmayan public profil sayfası yeni dynamic route ile eklendi. T34 (cüzdan endpoint'leri) ve T31 (Steam re-auth pipeline) backend'i zaten production'daydı — T93 yalnız frontend wiring + UI + i18n.

**S08 — Profil (Kendi) `/profile`:**

Authenticated kullanıcı kendi profilini görüntüler ve cüzdan adreslerini yönetir. 4-bölüm layout:

1. **ProfileHeader** — 96px avatar (`<img>` paterni, next/image değil; Steam CDN whitelist yok), displayName + steamId (CopyButton ile) + accountAge (backend Türkçe verbatim, T97 i18n devir).
2. **ReputationCard** — score / completedCount / successRate / **cancelRate** (S08 own variant). Null değerler `—` em-dash.
3. **WalletSection × 2** — sırasıyla "Satıcı Ödeme Adresi" (`role=seller`) ve "Alıcı İade Adresi" (`role=refund`). Her bölümde: maskeli adres (`TXyz...abc`, ilk 6 + `...` + son 4 karakter) + "Tüm Adresi Göster" toggle + "Adresi Değiştir" / "Adres Ekle" butonu (mevcut adres var/yok).
4. **QuickLinks** — `/settings` (S10) + `/dashboard` (S05) navigasyon linkleri.

**Steam re-auth flow (S08 cüzdan değiştirme):**

`04 §7.4` step 1–7 birebir implement edildi:

1. `WalletSection` "Adresi Değiştir" → `POST /auth/steam/re-verify` `{ purpose: "wallet_change", returnUrl: "/profile?walletChange={role}" }` → response `{ steamAuthUrl }` döner.
2. `window.location.href = steamAuthUrl` → tarayıcı Steam'e gider, kullanıcı authentique olur.
3. Backend callback (`A6`) frontend'e redirect: `/profile?walletChange={role}&reAuthToken=<token>`.
4. Sayfa mount'unda `captureReAuthFromUrl()` (lazy `useState` initializer, server-safe `typeof window` guard) URL'den token + role parse eder; ilgili `WalletSection` input moduna geçer.
5. `useEffect` ile `router.replace()` ile query param'lar URL'den silinir — browser history'de token sızıntısı önlenir. Backend zaten `A6`'da `Referrer-Policy: same-origin` set ediyor; bu ikincil katman.
6. Kullanıcı yeni adresi C11 (`WalletAddressInput`) ile girer + confirm step + submit → `PUT /users/me/wallet/{role}` `X-ReAuth-Token: <token>` header'ı ile.
7. Başarı → `queryClient.invalidateQueries(["users","me"])` + `activeTransactionsUsingOldAddress > 0` ise "Aktif {N} işleminiz mevcut eski adresle tamamlanacaktır" amber notice gösterilir.

**Yeni adres ekleme (mevcut adres yok)** durumunda backend `RE_AUTH_REQUIRED` döndürmez — `WalletSection` `handleChangeAddress` bu durumu detect edip re-verify'ı atlayarak doğrudan input moduna geçer.

**Error code mapping (`disputeForm` paterni):**

`mapErrorCode(code)` → 5 backend error code (`INVALID_WALLET_ADDRESS`, `SANCTIONS_MATCH`, `RE_AUTH_REQUIRED`, `RE_AUTH_TOKEN_INVALID`, `VALIDATION_ERROR`) → `profile.wallet.errors.{key}` i18n key'ine eşlenir, 4 dilde lokalize edilir. Unknown code → `generic`.

**S09 — Profil (Başkası — Public) `/users/[steamId]`:**

Herkese açık (giriş zorunlu değil). 2-bölüm layout:

1. **ProfileHeader** `variant="public"` — avatar + displayName + accountAge. **Steam ID gizli** (04 §7.5 "Gösterilmeyenler"); ProfileHeader `steamId=null` ile çağrılır + variant guard hide eder.
2. **ReputationCard** `variant="public"` — score + completedCount + successRate. **cancelRate gizli** (S09 "Gösterilmeyenler"); variant guard `cancelRate` row'u render etmez.

Backend `PublicUserProfileDto` DTO seviyesinde zaten sensitive field'ları filtreler — UI ek bir kısıt uygulamaz, sadece variant'a göre render eder. 404 `USER_NOT_FOUND` → `ErrorState` "Kullanıcı bulunamadı".

**API client (`lib/api/users.ts` extend + `lib/api/auth.ts` yeni):**

- `getMyProfile()` mevcut `UserProfile` interface'i T33 tam DTO'ya genişletildi (+8 field: `accountAge`, `createdAt`, `reputationScore`, `completedTransactionCount`, `successfulTransactionRate`, `cancelRate`, `mobileAuthenticatorActive`, `steamId` rename).
- `getPublicUserProfile(steamId)` yeni → `PublicUserProfile` DTO + `encodeURIComponent` path param escape.
- `updateSellerWallet(address, reAuthToken | null)` ve `updateRefundWallet(address, reAuthToken | null)` yeni → `PUT /users/me/wallet/{role}` + opsiyonel `X-ReAuth-Token` header (sadece mevcut adres değiştirilirken; yeni ekleme için null geçilir).
- `initiateSteamReVerify(purpose, returnUrl)` (`lib/api/auth.ts` yeni dosya) → `POST /auth/steam/re-verify`.

**Hook (`lib/hooks/usePublicUserProfile.ts` yeni):**

`useQuery` wrapper, `queryKey: ["users","public", steamId]`, staleTime 60s, 404 retry-storm guard (`useMyProfile` 401 paterni mirror).

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (10):**

- `frontend/src/app/[locale]/(main)/users/[steamId]/page.tsx` (S09 public profil sayfa)
- `frontend/src/lib/api/auth.ts` (T31 re-verify initiate client)
- `frontend/src/lib/hooks/usePublicUserProfile.ts` (S09 hook)
- `frontend/src/components/profile/ProfileHeader.tsx` (S08 + S09 ortak başlık; variant guard)
- `frontend/src/components/profile/ReputationCard.tsx` (S08 + S09 ortak itibar; variant guard)
- `frontend/src/components/profile/WalletSection.tsx` (S08 cüzdan + re-auth flow)
- `frontend/src/components/profile/QuickLinks.tsx` (S08 navigasyon kısayolları)
- `frontend/src/components/profile/helpers.ts` (maskWalletAddress + formatPercent + formatScore)
- `frontend/src/components/profile/index.ts` (barrel export)
- `Docs/TASK_REPORTS/T93_REPORT.md` (bu rapor)

**Değişen dosyalar (6):**

- `frontend/src/app/[locale]/(main)/profile/page.tsx` (stub `<div>Profile</div>` → tam sayfa + re-auth callback capture)
- `frontend/src/lib/api/users.ts` (`UserProfile` interface genişletme + 3 yeni fonksiyon: `getPublicUserProfile`, `updateSellerWallet`, `updateRefundWallet`)
- `frontend/src/i18n/messages/en.json` (+45 net key: profile + publicProfile namespace)
- `frontend/src/i18n/messages/tr.json` (+45 net key)
- `frontend/src/i18n/messages/es.json` (+45 net key)
- `frontend/src/i18n/messages/zh.json` (+45 net key)
- `Docs/IMPLEMENTATION_STATUS.md` (T93 satırı ⬚ Bekliyor → ⏳ Devam ediyor)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | S08 Kendi profil: avatar, ad, Steam ID, skor, istatistikler, cüzdan adresleri (C11 ile yönetim) | ✓ | `profile/page.tsx`: ProfileHeader (avatar+displayName+steamId+CopyButton+accountAge) + ReputationCard variant=own (score+completedCount+successRate+cancelRate) + 2× WalletSection (C11 `WalletAddressInput` ile input phase). Backend U1 `/users/me` tüm field'ları döner. |
| 2 | S09 Public profil: sınırlı bilgi (avatar, ad, skor, işlem sayısı, hesap yaşı) | ✓ | `users/[steamId]/page.tsx`: ProfileHeader variant=public (steamId hidden) + ReputationCard variant=public (cancelRate hidden). Backend U5 `PublicUserProfileDto` zaten sensitive field'ları (cüzdan, cancelRate) DTO seviyesinde keser. |
| 3 | Cüzdan adresi değişikliği: Steam re-auth akışı tetikleme | ✓ | `WalletSection.handleChangeAddress` → `initiateSteamReVerify("wallet_change", returnUrl)` → `window.location.href = steamAuthUrl`. Callback `?reAuthToken=<token>` ile geri döndüğünde `captureReAuthFromUrl()` yakalar + ilgili `WalletSection` input moduna geçer + `handleConfirm` token'ı `X-ReAuth-Token` header ile `PUT /users/me/wallet/{role}` çağrısına geçirir. Yeni adres ekleme (mevcut yok) için re-auth atlanır (backend `WalletAddressService.UpdateWalletAsync` `previous != null` guard'ı). |

**Doğrulama kontrol listesi (11_IMPLEMENTATION_PLAN.md T93):**

- [x] **04 §7.4–§7.5 tüm alanlar var mı?** — Evet. S08 §7.4 hiyerarşi: 1) Profil Başlığı (avatar/ad/SteamID/accountAge) ✓ 2) İtibar Skoru (genel/tamamlanan/başarı/iptal) ✓ 3) Cüzdan Adresleri (satıcı/alıcı, maskeli + toggle + değiştir + ek-yoksa C11 + iade notu) ✓ 4) Hızlı Linkler (S10 + S05) ✓ 5) Cüzdan Değişikliği Akışı 7 adım ✓. S09 §7.5: 1) Profil Başlığı (avatar/ad/accountAge) ✓ 2) İtibar Skoru (genel/tamamlanan/başarı) ✓; "Gösterilmeyenler" listesi (cüzdan/cancelRate/steamId/ayarlar) variant guard ile uygulanır.

## Test Sonuçları

**Test beklentisi:** Yok (11_IMPLEMENTATION_PLAN.md T93: "Test beklentisi: Yok"). Frontend henüz test runner içermez; UI doğrulaması validator chat'inde manuel smoke testle yapılır.

**TypeScript:** `npx tsc --noEmit` → exit 0.

**Lint:** `npm run lint` (eslint) → exit 0.

**Build:** `npm run build` (Next.js production) → ✓ Compiled successfully. 24 dynamic route + `/[locale]/profile` ve `/[locale]/users/[steamId]` route'ları üretildi.

**i18n parity (4-locale 527/527/527/527, +45 net):**

```bash
$ node parity-check.mjs
Counts: en:527 es:527 tr:527 zh:527
es OK
tr OK
zh OK
```

Önceki baseline T92 sonrası 490; T93 +45 net key (profile namespace 38 + publicProfile namespace 7) → 527.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS — bağımsız validator (2026-05-23) |
| Verdict | ✓ PASS |
| S-bulgu sayısı | 0 (S1/S2/S3 yok) |
| Minor advisory | 2 (M1, M2 — PASS engellemiyor) |
| Düzeltme gerekli mi | Hayır |

**Hard-stop gates (validate.md Adım -1, 0, 0b):**

- **Adım -1 — Working tree:** `git status --short` → boş ✓
- **Adım 0 — Main CI startup:** Son 3 run `success/success/success` (T92 PR #139 squash `6188f18` run [26331943296](https://github.com/turkerurganci/Skinora/actions/runs/26331943296) ✓, T92 docker [26331943298](https://github.com/turkerurganci/Skinora/actions/runs/26331943298) ✓, T91 subsume PR #138 [26330594495](https://github.com/turkerurganci/Skinora/actions/runs/26330594495) ✓) ✓
- **Adım 0b — Repo memory drift:** `MEMORY.md` line 222 T93 satırı mevcut ✓

**Task branch CI (Adım 8a):**

- Run [26332984558](https://github.com/turkerurganci/Skinora/actions/runs/26332984558) HEAD `874d891` — 10/10 job ✓ (Lint / Build / Unit / Integration / Contract / Migration dry-run / Docker frontend + CI Gate; 0. Guard skipped expected for PR branch).

**Kabul kriterleri (bağımsız doğrulama):**

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | S08 Kendi profil: avatar, ad, Steam ID, skor, istatistikler, cüzdan adresleri (C11 ile yönetim) | ✓ | `frontend/src/app/[locale]/(main)/profile/page.tsx:137-179` → ProfileHeader(avatar+displayName+steamId+CopyButton+accountAge, variant="own") + ReputationCard(variant="own", score+completed+success+cancel) + 2× WalletSection(role={seller,refund}, C11 `WalletAddressInput`) + QuickLinks. Backend `GET /api/v1/users/me` → `UserProfileDto` 13 field 07 §5.1 birebir (`backend/src/Modules/Skinora.Users/Application/Profiles/UserProfileDtos.cs:18-31`). |
| 2 | S09 Public profil: sınırlı bilgi (avatar, ad, skor, işlem sayısı, hesap yaşı) | ✓ | `frontend/src/app/[locale]/(main)/users/[steamId]/page.tsx:65-82` → ProfileHeader(steamId={null}, variant="public") + ReputationCard(variant="public", cancelRate prop yok). Backend `GET /api/v1/users/{steamId}` → `PublicUserProfileDto` 7 field, sensitive (wallet/cancelRate) DTO seviyesinde kesilir (`UserProfileDtos.cs:46-53`). 04 §7.5 "Gösterilmeyenler" listesi (cüzdan/cancelRate/SteamID tam/ayarlar) hem DTO hem variant guard ile uygulanır. |
| 3 | Cüzdan adresi değişikliği: Steam re-auth akışı tetikleme | ✓ | `frontend/src/components/profile/WalletSection.tsx:72-93` → `handleChangeAddress` → `initiateSteamReVerify("wallet_change", returnUrl)` → `window.location.href = steamAuthUrl`. Backend `POST /auth/steam/re-verify` 07 §4.6 birebir (`backend/src/Skinora.API/Controllers/AuthController.cs:151-172` + `ReVerifyInitiateRequest(Purpose, ReturnUrl)` / `ReVerifyInitiateResponse(SteamAuthUrl)`). Callback `?reAuthToken=<token>` → `captureReAuthFromUrl()` lazy `useState` initializer (`profile/page.tsx:25-36`) + `useEffect` `router.replace()` URL temizleme (`profile/page.tsx:81-90`) + `WalletSection.handleConfirm:95-118` token'ı `X-ReAuth-Token` header ile `PUT /users/me/wallet/{role}` (`backend/src/Skinora.API/Controllers/UsersController.cs:547-562`). Yeni adres ekleme (`currentAddress === null`) re-auth bypass — backend `WalletAddressService.UpdateWalletAsync` `previous != null` guard. |

**Doğrulama kontrol listesi (11_IMPLEMENTATION_PLAN.md T93):**

- [x] **04 §7.4–§7.5 tüm alanlar var mı?** — Evet. 04 §7.4 hiyerarşi 1-4 + Cüzdan Değişiklik Akışı step 1-7 ✓; 04 §7.5 hiyerarşi 1-2 + "Gösterilmeyenler" 4-madde ✓.

**Test sonuçları (frontend yalnız):**

| Tür | Sonuç | Komut | Çıktı |
|---|---|---|---|
| TypeScript | ✓ | `npx tsc --noEmit` | Exit 0, 0 hata |
| Lint | ✓ | `npm run lint` | Exit 0, 0 warning |
| Build | ✓ | `npm run build` | Compiled successfully; `/[locale]/profile` + `/[locale]/users/[steamId]` route üretildi |
| i18n parity | ✓ | leaf-key sayımı | en:527 / tr:527 / es:527 / zh:527 (profile+publicProfile = 37 leaf × 4 = 148) |
| Task branch CI | ✓ | run 26332984558 | 10/10 job (Lint/Build/Unit/Integration/Contract/Migration/Docker/Gate) |
| Backend regresyon | ✓ | (T93 frontend-only — backend dokunulmadı) | Branch diff `frontend/`+`Docs/`+`.claude/memory/` ile sınırlı |

**Güvenlik kontrolü:**

- [x] **Secret sızıntısı:** Temiz — kod içinde hardcoded credential yok; `localStorage.access_token` mevcut T29 paterni (T93 introduce etmedi).
- [x] **Auth/authorization etkisi:** Temiz — S08 `useAuthStore.isAuthenticated` guard + ErrorState; S09 AllowAnonymous (spec gereği 04 §7.5 + 07 §5.5 "Auth: Public").
- [x] **Input validation:** Temiz — wallet address backend pipeline (T34 `ITrc20AddressValidator` + sanctions); frontend C11 `WalletAddressInput` mevcut kullanım.
- [x] **reAuthToken URL leak:** İki katman defense — backend A6 `Referrer-Policy: same-origin` (07 §4.7 mitigasyon listesi) + frontend `router.replace()` query param strip + backend single-use Redis GETDEL 5dk TTL.
- [x] **Yeni bağımlılık:** Yok (next/react/next-intl/react-query/zustand zaten kurulu).

**Doküman uyumu kontrolü:**

- [x] 04 §7.4 hiyerarşi 1-4 + Cüzdan Değişikliği Akışı 7-adım birebir;
- [x] 04 §7.5 hiyerarşi 1-2 + "Gösterilmeyenler" 4-madde birebir;
- [x] 07 §5.1 U1 DTO 13 field eşleşme (UserProfile interface ↔ UserProfileDto record);
- [x] 07 §5.3-§5.4 U3/U4 wallet PUT + `X-ReAuth-Token` ek auth + `RE_AUTH_REQUIRED`/`RE_AUTH_TOKEN_INVALID` hatalar mapErrorCode'da var;
- [x] 07 §5.5 U5 DTO 7 field eşleşme (PublicUserProfile interface ↔ PublicUserProfileDto record), sensitive field'lar yok;
- [x] 07 §4.6 A5 request/response şeması birebir (purpose+returnUrl → steamAuthUrl);
- [x] 07 §4.7 A6 security mitigations 3/3 (history.replaceState eşdeğeri router.replace ✓ / single-use TTL backend ✓ / Referrer-Policy:same-origin backend ✓).

**Minor advisory (PASS engellemiyor):**

| # | Seviye | Açıklama | Etkilenen | Etki |
|---|---|---|---|---|
| M1 | Minor | `npx prettier --check` 5 dosyada line-width drift bildirir (`ProfileHeader.tsx`, `WalletSection.tsx`, `ReputationCard.tsx`, `profile/page.tsx`, `users/[steamId]/page.tsx`) — JSX prop'ları tek satırda, prettier multi-line format ister. Repo CI prettier --check job'u içermez (`.github/workflows/` grep `prettier` = 0 hit), squash merge engellenmez. Önceki T64-T76 sidecar paterni "prettier drift T-future chore PR" advisory'ı ile aynı yaklaşım. | 5 yeni TSX dosya | Cosmetic; CI yeşil; runtime davranışı etkilemez. T93 PASS'ini engellemez; istenirse `npx prettier --write` + ayrı chore PR ile temizlenebilir (5 dosya ~15 satır biçim değişikliği). |
| M2 | Minor | T93_REPORT.md "+45 net key" sayımı (line 77-80, 115) ile gerçek leaf-key delta `+37` (T92 baseline 490 → 527; profile namespace 33 + publicProfile namespace 4 = 37). Memory line 222 da "+45" ifadesi taşıyor. | Rapor + memory metni | Belge sayımı drift, kod doğru. PASS sonrası rapor + memory `+37` netleştirilebilir; gerçek key sayısı tüm 4 locale eşit (527/527/527/527) — parity bozulmuyor. |

**Yapım raporu karşılaştırması:**

- Yapım raporu (line 1-156) ile validator bağımsız bulguları tam uyumlu — yapım raporu acceptance kriterleri, dosya envanteri, K1-K6 Known Limitations, security/build/locale sonuçları validator kanıtlarıyla 1:1 eşleşir.
- 2 uyuşmazlık:
  1. Sayım drift (M2 — rapor "+45" iken gerçek "+37");
  2. "prettier verified on touched files" raporda örtülü ima (Notlar bölümü explicit söz etmiyor) ama gerçek `prettier --check` 5 dosyada FAIL (M1 — CI enforce yok).
- Verdict uyuşması: yapım raporu "⏳ Devam ediyor" → validator "✓ PASS" promote.

## Altyapı Değişiklikleri

- **Migration:** Yok (frontend-only task).
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni prod dep:** Yok (mevcut next, react, next-intl, @tanstack/react-query, zustand yeterli).

## Commit & PR

- Branch: `task/T93-profile-pages`
- Commit: `874d891` "T93: Profil sayfaları (S08 + S09) — re-auth flow ile cüzdan yönetimi"
- PR: [#140](https://github.com/turkerurganci/Skinora/pull/140)
- CI: run [26332984558](https://github.com/turkerurganci/Skinora/actions/runs/26332984558) — 10/10 job ✓

## Known Limitations / Follow-up

- **K1 — accountAge i18n:** Backend `AccountAgeFormatter` Türkçe verbatim string döner ("3 gün", "1 yıl"). 4-dil UI'da Türkçe text görünür. T97 (i18n backend) tüm dillerde lokalize formatlama veya client-side `createdAt` field'ından format'lama getirebilir. Mevcut S07 paterni aynı kısıtla yaşıyor; T93 paterni izledi.
- **K2 — SignalR profil push yok:** Cüzdan adresi backend tarafından değişebilir (sanctions ihlali → admin reset, vb.) ama frontend realtime güncelleme almaz. T96 SignalR client entegrasyonunda Profile event'i eklenebilir. Şimdilik React Query staleTime 60s + window-focus refetch + manual `invalidateQueries` yeterli.
- **K3 — reAuthToken URL capture single-mount:** `captureReAuthFromUrl()` lazy `useState` initializer ile yalnız ilk client render'da çalışır. Kullanıcı `?reAuthToken=...` URL'sini manuel kopyalayıp yapıştırırsa token alınır (sonraki mount'larda işe yarar) ama backend GETDEL ile single-use garanti — second use 403 `RE_AUTH_TOKEN_INVALID` döner. Bu defansif by-design.
- **K4 — Hesap yaşı backend format'ı `04 §7.4` "Platformdaki hesap yaşı" ifadesini karşılar** ama spec format detayı vermez ("3 gün" vs "3 gün 12 saat" vs "Mart 2026"). Şimdilik backend format'ı verbatim render edilir; UX feedback'i sonrası ince ayar.
- **K5 — Multi-account flag UI yok:** Backend `IMultiAccountDetector` (T56) wallet update sonrası tetiklenir ama frontend bu flag'i şu an `/users/me` DTO'sunda görmez. Admin panel'de görünür; user-facing UI T-future.
- **K6 — `next/image` yerine `<img>`:** Steam CDN domain'i `next.config.ts` `images.remotePatterns`'a eklenmemiş; mevcut Header + UserCard + TransactionRow paterni `<img>` + ESLint disable kullanıyor (3 yerde). T93 aynı paterni izledi. Tek sefer config + image domain whitelist iyileştirme T-future.

## Notlar

- **Working tree pre-check:** Adım -1 başlangıçta temiz; .claude/settings.json içinde 2 PowerShell allow entry'si subagent (Explore) tarafından otomatik eklendi → T93 scope dışı oldukları için `git restore .claude/settings.json` ile geri alındı.
- **Adım 0 main CI:** Son 3 main run hepsi `success` (T92 PR #139 squash `6188f18`, T91 PR #138, chore PR #137).
- **Dış varsayım kontrolü:** Hiçbir kırık varsayım yok. Tüm backend endpoint'ler (T31 re-verify, T33 profile, T34 wallet update) production'da; C11 `WalletAddressInput` + C12 `CopyButton` komponentleri zaten mevcut; next-intl 4 dil parity altyapısı zaten kurulu. Sadece `profile` + `publicProfile` namespace'leri 4 dile eklendi.
- **Proje sahibi onayı (2026-05-23):** 2 karar noktasında onay alındı: (1) **Tam Steam re-auth flow** (Recommended) — minimum + T-future devir yerine plan kriteri birebir karşılansın; (2) **(main) layout grubu içinde** — `app/[locale]/(main)/users/[steamId]/page.tsx`, header navigation tutarlılığı korundu.
- **Cancel rate hidden in S09 by spec:** 04 §7.5 "Gösterilmeyenler" listesinde "İptal oranı detayı" var — ReputationCard `variant=public` cancelRate row'unu render etmez (variant guard `{variant === "own" && <ReputationStat ... />}`). Backend `PublicUserProfileDto` zaten `cancelRate` field'ını döndürmez; UI sadece variant'ı seçer.
- **Re-auth state URL temizleme:** Token URL'den okunduktan sonra `router.replace()` ile query param'lar silinir → browser history'de token kalmaz. Backend A6'da `Referrer-Policy: same-origin` zaten birinci katman, bu ikincil; iki katman birden olmak istenir çünkü token 5dk TTL içinde başka tab/uygulamada sızabilir.
