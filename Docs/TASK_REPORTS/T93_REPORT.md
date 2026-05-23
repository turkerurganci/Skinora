# T93 — Profil sayfaları (S08, S09)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-05-23

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
| Doğrulama durumu | ⏳ Pending (validator chat'inde doğrulanacak) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok (frontend-only task).
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni prod dep:** Yok (mevcut next, react, next-intl, @tanstack/react-query, zustand yeterli).

## Commit & PR

- Branch: `task/T93-profile-pages`
- Commit: pending (commit + push aşamasına geçiliyor)
- PR: pending
- CI: pending

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
