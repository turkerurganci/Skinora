# T87 — Auth Akışı Ekranları

**Faz:** F5 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekleniyor) | **Tarih:** 2026-05-20

---

## Yapılan İşler

`04 §6.2–§6.7` tanımları doğrultusunda Steam-tabanlı kimlik doğrulama akışının tüm ekranları ve callback pipeline gate'leri implement edildi. Plan "Test beklentisi: Yok"; T86 paterni takip edildi — ekranlar query-string driven state'le sürülür, gerçek backend wire-up (T29 `/auth/steam`, T30 `/auth/tos/accept`, T31 `/auth/check-authenticator`) E2E entegrasyonunda (T-future) bağlanır.

**Route restructure (mimari karar):** T13 stub'unda kullanılan `(auth)` Next.js route grubu URL'de "auth" segmentini *üretmiyordu* (sayfalar `/{locale}/login` olarak servis ediliyordu). Doc 04 §6.2 + 07 §4.3 + T86 HeroSection CTA hepsi `/{locale}/auth/login` ve `/{locale}/auth/callback` varsayar. Route grup parantezi kaldırılıp gerçek `auth/` klasörüne taşındı: 7 sayfa + layout doğru URL'e taşındı; T86 HeroSection CTA href'i hâlâ çalışıyor.

- **`auth/layout.tsx`** — Skinora wordmark (sol) + LanguageSelector (sağ), centered body shell. T13 stub'undaki boş `<div className="min-h-screen">` yerine login öncesi tutarlı chrome (T85 Header'a girilemez çünkü `useAuthStore`'a bağlı; pre-auth shell ayrı).
- **`auth/login/page.tsx` (S02 pre-redirect):** Steam ile Giriş hero bölümü + 3 fayda noktası + "Tek tıkla giriş" CTA. Tıklandığında button "Doğrulanıyor..." loading variant'ına geçer + `useEffect` ile `window.location.assign("${API_BASE_URL}/auth/steam?returnUrl=...")` → backend OpenID redirect başlar. `returnUrl` query parametresi sanitize edilir (yalnız relative path; absolute/protocol-relative reject → fallback `/dashboard`, 07 §4.2 güvenlik kuralları).
- **`auth/callback/page.tsx` (S02 callback):** 4-durumlu state machine (`loading | success | new_user | error`):
  - `?status=success` → `useRouter.replace(returnUrl)` (success spinner geçici)
  - `?status=new_user` → ToS modal mount + dashboard placeholder
  - `?error=auth_failed|steam_unavailable|temporarily_locked|account_banned|unknown` → `InfoScreen` (danger tone) + "Tekrar Dene" + "Ana sayfaya dön" eylemleri
  - `?error=temporarily_locked&retryAfter=N` → mesaj `{minutes}` interpolasyonu (Math.ceil(N/60))
  - Default (boş query) → loading spinner
- **`ToSModal` (modal C ile çağrıldı):** `04 §6.2 ToS Modal` — 18+ checkbox + ToS checkbox + 5 maddelik özet liste + `<link>` chunk ile Türkçe/İngilizce/Çince/İspanyolca i18n linkli ToS satırı + "Devam Et" CTA (her iki checkbox işaretlenmeden disabled) + opsiyonel "Devam etmeye uygun değilim" eyleminden S03b age-gate'e yönlendirme. `role="dialog" aria-modal="true"` + `aria-labelledby/describedby` + ilk focus age checkbox'ında.
- **`auth/mobile-authenticator/page.tsx` (S03):** Steam Mobile Authenticator uyarısı + 4 adımlı talimat listesi + harici Steam mobil uygulama linki + "Tekrar Kontrol Et" + "Panele Devam Et" + blocker-değil notu (kullanıcı dashboard'a gidebilir, sadece işlem başlatamaz).
- **`auth/geo-block/page.tsx` (S03a):** `InfoScreen` danger tone + tam blocker (login engellenir) + destek linki (env `NEXT_PUBLIC_SUPPORT_URL` veya `mailto:`).
- **`auth/age-gate/page.tsx` (S03b):** `InfoScreen` danger tone + tam blocker + "Ana sayfaya dön" eylem.
- **`auth/sanctions/page.tsx` (S03c):** `InfoScreen` danger tone + aktif işlemlerin acil hold'a alındığı bilgilendirme + destek linki.
- **`auth/suspended/page.tsx` (S03d):** `InfoScreen` warning tone + askıya alındı + aktif işlemler salt okunur açıklama (04 §6.7 birebir) + "Paneli Görüntüle" + destek.

**Reusable components (`components/auth/`):**

- `InfoScreen` — tone variant (info/warning/danger/success) + icon slot + title + description + children + actions; S03a/S03b/S03c/S03d/callback error path için 5 sayfada tek-source UI.
- `TosModal` — encapsulated modal davranış (focus management, role, aria, i18n link chunk).
- `index.ts` — re-export.

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (12):**

- `frontend/src/app/[locale]/auth/layout.tsx` *(taşındı: `(auth)/layout.tsx`'ten dönüştürüldü)*
- `frontend/src/app/[locale]/auth/login/page.tsx`
- `frontend/src/app/[locale]/auth/callback/page.tsx` *(T13 stub yerine geçti)*
- `frontend/src/app/[locale]/auth/mobile-authenticator/page.tsx`
- `frontend/src/app/[locale]/auth/geo-block/page.tsx`
- `frontend/src/app/[locale]/auth/age-gate/page.tsx`
- `frontend/src/app/[locale]/auth/sanctions/page.tsx`
- `frontend/src/app/[locale]/auth/suspended/page.tsx`
- `frontend/src/components/auth/InfoScreen.tsx`
- `frontend/src/components/auth/TosModal.tsx`
- `frontend/src/components/auth/index.ts`

**Silinen dosyalar (2 — route grup → real folder taşıma):**

- `frontend/src/app/[locale]/(auth)/callback/page.tsx` (T13 stub, kaldırıldı)
- `frontend/src/app/[locale]/(auth)/layout.tsx` (genişletilmiş hali `auth/layout.tsx`'e taşındı)

**Güncellenmiş dosyalar (4):**

- `frontend/src/i18n/messages/en.json` — `auth.*` namespace 2 → 66 anahtar genişletildi
- `frontend/src/i18n/messages/tr.json` — aynı şema, Türkçe
- `frontend/src/i18n/messages/zh.json` — aynı şema, 中文
- `frontend/src/i18n/messages/es.json` — aynı şema, Español

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | S02 Steam Login: pre-redirect loading, callback loading, auth başarısız | ✓ | `auth/login/page.tsx`: "Doğrulanıyor..." loading variant + `auth/callback/page.tsx`: 4 durum state machine + 5 hata kodu (`auth_failed`/`steam_unavailable`/`temporarily_locked`/`account_banned`/`unknown` fallback) `InfoScreen` danger tone |
| 2 | S03 MA Uyarısı: adım adım talimat, kontrol et butonu | ✓ | `auth/mobile-authenticator/page.tsx`: 4 adımlı `<ol>` + Steam mobile app dış link + "Tekrar Kontrol Et" button (transient loading state, gerçek `/auth/check-authenticator` çağrısı T31 entegrasyonu T-future) + "Panele Devam Et" |
| 3 | S03a Geo-Block: bilgilendirme sayfası | ✓ | `auth/geo-block/page.tsx`: `InfoScreen` danger tone + 🚫 ikon + başlık/açıklama/info + destek linki. Tam blocker (login eylemi yok) |
| 4 | S03b Yaş Gate: 18+ onay | ✓ | `auth/age-gate/page.tsx`: `InfoScreen` danger tone + 🔞 ikon + "18 yaşında olmanız gerekmektedir" + "Ana sayfaya dön". ToS modal'da reject path bu sayfaya yönlendirir |
| 5 | S03c Sanctions Uyarı | ✓ | `auth/sanctions/page.tsx`: `InfoScreen` danger tone + ⛔ ikon + "aktif işlemleriniz acil hold'a alındı" notu + destek linki |
| 6 | S03d Hesap Askıya Alındı: kısıtlı oturum | ✓ | `auth/suspended/page.tsx`: `InfoScreen` warning tone + 🚷 ikon + 04 §6.7 birebir aktif işlem salt okunur notu + "Paneli Görüntüle" (T85 `SuspendedHeader` zaten implement edilmiş — kısıtlı oturum davranışı) + destek |
| 7 | ToS Modal: 18+ checkbox + ToS checkbox | ✓ | `components/auth/TosModal.tsx`: 2 checkbox + her ikisi işaretlenmeden disabled CTA + `t.rich("tosCheckbox", {link})` ile ToS sayfası link chunk + 5 maddelik özet + focus management + `role="dialog"` `aria-modal` |

## Test Sonuçları

Plan "Test beklentisi: Yok" (F5 frontend task'ları, E2E T107+ devirli).

| Tür | Sonuç | Detay |
|---|---|---|
| `npm run build` | ✓ | 22 route (önceki T86 18 → +4 net: `auth/login`, `auth/mobile-authenticator`, `auth/geo-block`, `auth/age-gate`, `auth/sanctions`, `auth/suspended` 6 yeni; `[locale]/callback` route'u `auth/callback`'e taşındığı için net +6 yeni; eski stub +1 silindi). TypeScript Finished 2.4s, 0 error |
| `npm run lint` | ✓ | 0 error, 0 warning (ESLint flat config) |
| `npx prettier --check` (T87 dosyaları) | ✓ | "All matched files use Prettier code style!" — 11 yeni dosya + 4 i18n |
| i18n parity | ✓ | Node JSON traverse: `auth.*` 66 key × 4 dil (en/tr/zh/es), 0 missing |
| Smoke (curl `npm run start`) | ✓ | 8 route 200 OK (`/{locale}/auth/*` + `/auth/callback?status=new_user`, `/auth/callback?error=auth_failed`); render içerikleri en/tr/zh için doğru lokalize string'lerle çıktı |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: 2 yeni opsiyonel `NEXT_PUBLIC_*` env değişkeni — `NEXT_PUBLIC_TOS_VERSION` (default `"1.0"`), `NEXT_PUBLIC_SUPPORT_URL` (default `mailto:support@skinora.app`). İkisi de default fallback ile çalışır, deployment override gerektirmez.
- Docker değişikliği: Yok
- Yeni dış bağımlılık: Yok (`package.json` değişmedi — `git diff main...task/T87 -- frontend/package*.json` boş)

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — `frontend/src/app/[locale]/auth/` ve `frontend/src/components/auth/` altında `apiKey|secret|password|private_key|client_secret` grep 0 match
- **Auth/authorization etkisi:** Yok (frontend chrome — gerçek session/JWT entegrasyonu T29-T31 forward); pre-redirect URL sanitize (`returnUrl` yalnız relative path, absolute reject → `/dashboard` fallback) 07 §4.2 güvenlik kurallarıyla uyumlu
- **Input validation:** Yok (kullanıcı text input'u yok; sadece 2 checkbox ToS modal'da — her ikisi de zorunlu)
- **Yeni dış bağımlılık:** Yok

## Commit & PR

- Branch: `task/T87-auth-flow-screens`
- Commit: `<eklenecek>` — T87: Auth akışı ekranları
- PR: `<eklenecek>`
- CI: `<post-push>`

## Mimari Kararlar (Notlar)

1. **`(auth)` route grup parantezi → real `auth/` folder taşıma (T13 stub düzeltmesi):** Mevcut `(auth)` Next.js route grubu URL segmentine "auth" eklemez; T13 stub `/{locale}/callback` URL'inde çalışıyordu. Doc 04 §6.2 + 07 §4.3 callback URL'i `/auth/callback` der; T86 HeroSection CTA `href="/${locale}/auth/login"`. Parantez kaldırma backward-compat hack değil — T13 stub'un yanlış konumlandırılması fix'i (CLAUDE.md "no half-finished implementations"). HeroSection T86 PR'ında deploy edildiğinden T87'siz route 404 dönüyordu (T86 K2 disclosed).
2. **Query-string driven state, API wire-up yok (proje sahibi onayı):** T86 paterni — plan kabul kriterleri ekranlar + state'ler ister; "Test beklentisi: Yok". Gerçek `/auth/steam` redirect ve `/auth/tos/accept` POST T29/T30 mevcut, frontend wire-up T-future E2E entegrasyonu (T107+). Bu PR ekranları URL query string ile sürülebilir tutar — backend ile manuel test edilebilir, otomatik wire mock'lara dayanmaz.
3. **`InfoScreen` reusable tone variant + 4 alanda kullanım:** S03a/S03b/S03c/S03d ve callback error path için aynı centered card chrome. 4 tone (info/warning/danger/success) ile renk anlamı tutarlı. T84 C component'lerine taşımak yerine `components/auth/`'a koydum — yalnız auth bağlamı, T84 dev showcase scope'unda değildi.
4. **`SteamLoginButton` reusable çıkartılıp silindi:** İlk taslakta `components/auth/SteamLoginButton.tsx` reusable yapmıştım; yalnız login sayfası tüketici, ek abstraction CLAUDE.md "Don't add features beyond what the task requires" — silindi, login page inline button kullanıyor. HeroSection (T86) zaten kendi `<Link>` button'unu render eder.
5. **`ToSModal` 5 maddelik özet liste:** 04 §6.2 "ToS özeti (kısa maddeler)" gereği. İçerik (escrow/commission/crypto/disputes/kyc) 02 §21.1 + 06 commerce model'inden türetildi — i18n 4 dilde paralel. Tam ToS metni `/terms` linkinden açılır (T106/T-future static page).
6. **`callback?error=temporarily_locked&retryAfter=N`:** 07 §4.2 brute force koruması. `retryAfter` saniye cinsinden gelir; UI dakika cinsinden gösterir (`Math.ceil(N/60)`) — minute granularity 5-dakika locks için yeterli, saniye precision UX gürültüsü.
7. **Mobile Authenticator recheck transient loading:** 04 §6.3 "Kontrol Et" butonu MA durumunu tekrar kontrol eder. Backend kontratı (T31 `/auth/check-authenticator`) ile entegrasyon T-future; T87 600ms transient loading + `aria-busy` set eder — UX akışı tam, yalnız network çağrısı yok.

## Known Limitations / Follow-up

- **K1 — Backend wire-up T-future:** Gerçek auth akışı bağlanmadı (T29 `/auth/steam` redirect launch, T30 `/auth/tos/accept` POST submission, T31 `/auth/check-authenticator` MA recheck, T32 `/auth/refresh` token swap, T33 `/auth/me` session bootstrap). Ekranlar URL query string ile manuel test edilebilir; E2E T107+ entegre eder. ToS modal `onAccept` handler şu an dashboard'a redirect eder — `apiClient` POST T-future devir.
- **K2 — `useAuthStore.isSuspended` set wire-up T-future:** S03d sayfası ulaşılabilir ama auth-store `isSuspended` flag'ini set eden kod yok (T33 `/auth/me` response tüketicisi forward-devir). T85 `SuspendedHeader` koşulu (`MainShell`) zaten devreye girer flag set edildiğinde.
- **K3 — `auth/callback` POST refresh route reaktif değil:** 07 §4.3 + 4.10 frontend `/auth/callback` `POST /auth/refresh` çağırıp access token alır. Bu PR'da `status=success` path'i `useRouter.replace(returnUrl)` yapar; refresh çağrısı T29/T32 entegrasyonunda eklenir.
- **K4 — ToS sayfası (`/terms`) 404 dönecek:** Modal link static ToS sayfasına işaret eder; T106 (Admin Audit Log) komşu task'ında veya legal stack'inde T-future sayfa eklenecek. Cosmetik etki.
- **K5 — `NEXT_PUBLIC_TOS_VERSION` env default `"1.0"`:** 07 §4.4 `tosVersion` 20 karakter max kabul eder; default değer T30 backend seed (`"1.0"`) ile birebir. Production deploy farklı versiyon set edebilir, default override gerektirmez.
- **K6 — Pre-existing prettier drift (T84/T85/T86 dönemi):** `npm run format` global çağrısı 19 pre-existing dosyada drift yakaladı; T87 PR'ına bundle edilmedi (`git restore` ile geri alındı, bundled-PR yasağı). Ayrı chore PR — T86 K3 ile aynı havuz.
- **K7 — Geo-block sayfası gerçek IP/VPN kontrolüyle değil URL ile ulaşılır:** 02 §21.1 ve T83 backend pipeline'ı IP geo + VPN tespiti yapar; kullanıcı genellikle login flow callback'ten yönlendirilir. Pre-login direct URL navigasyonu *zarar vermez* (yalnız bilgilendirme), backend zorlamasız frontend gate değil. Yetkili gate backend tarafı (T29/T83).
- **K8 — `auth/login` "prepare" button → `setRedirecting` `useEffect` window.location.assign:** İlk denemede `<a href>` kullandım; sonra "loading state'i göster + sonra redirect" UX'i için button + useEffect pattern'ine geçtim. Adblock veya hızlı çift-tık edge case'inde 2× redirect riski yok (`useEffect` dep array tek state flip'te tetiklenir, `assign` idempotent).

## CI Doğrulaması

- **Adım 0 (Main CI):** Son 3 main run `success` (T85 #130, T86 #131 ×2) — `gh run list --branch main --limit 3` çıktı ✓
- **Adım -1 (Working tree):** `git status --short` boş ✓
- **Memory drift check:** `.claude/memory/MEMORY.md` T87 satırı bu rapor ile birlikte eklenir.

## Bitiş Kapısı (T11.2)

- [ ] Branch push edildi mi? → post-push
- [ ] PR açıldı mı? → post-push
- [ ] PR numarası rapora yazıldı mı? → post-PR
- [ ] Rapor + status push edildi mi? → post-push
- [ ] CI run tamamlandı mı? → post-push
- [ ] CI run sonucu `success` mi? → post-CI
- [ ] Branch izolasyon check temiz mi? → `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` → yalnız T87
- [ ] Repo memory'de TXX satırı eklendi/güncellendi mi? → bu commit'te
