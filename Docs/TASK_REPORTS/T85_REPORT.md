# T85 — Global Layout (header, navigation, footer)

**Faz:** F5 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-19

---

## Yapılan İşler

`04 §7.1 (Dashboard header)` ve `§8.1 (Admin header/menü)` layout tanımları doğrultusunda kullanıcı, askıya alınmış oturum ve admin chrome bileşenleri implement edildi. Tüm chrome React 19 + Next.js 16 App Router + Tailwind v4 + next-intl 4 dil stack'inde, T84 ortak bileşen kütüphanesini (özellikle C10 LanguageSelector) yeniden kullanır.

- **Header.tsx** (varsayılan kullanıcı varyantı): Logo (`/dashboard` → S05) | Bildirim ikonu (`/notifications` → S11, badge unread count, 99+ truncate) | Profil (`/profile` → S08, avatar veya initial fallback) | C10 LanguageSelector | Ayarlar (`/settings` → S10). `aria-label` her CTA'da, mobile-first responsive (`sm:` breakpoint metin gösterimi).
- **SuspendedHeader.tsx** (kısıtlı oturum varyantı): Logo | C10 LanguageSelector | Destek linki | Çıkış butonu. Turuncu border + bg-orange-50 görsel ayrımı; korumalı CTA'lar (Bildirim/Profil/Ayarlar) yok. 04 §6.7 + §7.3 "Suspended Session Override" tanımı birebir.
- **AdminHeader.tsx**: Logo (`/admin/dashboard`) | Admin adı (`auth-store.displayName` fallback `t("adminFallback")`) | Çıkış. Koyu tema (`bg-gray-900`) admin context'i görsel olarak ayırır.
- **AdminSidebar.tsx** (sol menü, 8 öğe): Dashboard / Flag'ler / İşlemler / Ayarlar / Steam Hesapları / Roller / Kullanıcılar / Audit Log. Aktif route vurgusu (`usePathname` + locale prefix-aware match), `aria-current="page"`, mavi border-l-2 + bg-blue-50 vurgu, hover state.
- **Footer.tsx**: ToS linki | Privacy linki (`privacyComingSoon` tooltip — şu an placeholder, ileride T-future) | C10 LanguageSelector. T86 Landing wiring için hazır; flex-col mobile + flex-row sm+ responsive.
- **MainShell.tsx** (`(main)/layout.tsx` client wrapper): `useAuthStore(s => s.isSuspended)` okur, Header veya SuspendedHeader render eder. Server layout (`(main)/layout.tsx`) bu shell'i çağırır — yalnız conditional kısım client.
- **auth-store extension:** `isAdmin`, `isSuspended`, `displayName`, `avatarUrl` alanları (default `false`/`null` — T85 stub) + `setProfile()` partial setter + `logout()` reset zincirleme tüm kullanıcı verisini temizler. Gerçek değerler T87 Auth akışında set edilecek.

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (7):**

- `frontend/src/components/layout/Header.tsx` — kullanıcı header varyantı (logo + bildirim + profil + dil + ayarlar)
- `frontend/src/components/layout/SuspendedHeader.tsx` — kısıtlı varyant (logo + dil + destek + çıkış)
- `frontend/src/components/layout/AdminHeader.tsx` — admin header (logo + ad + çıkış)
- `frontend/src/components/layout/AdminSidebar.tsx` — admin sol menü (8 öğe, route-aware active state)
- `frontend/src/components/layout/Footer.tsx` — ToS + Privacy + C10 dil
- `frontend/src/components/layout/MainShell.tsx` — client wrapper, isSuspended'a göre header switch
- `frontend/src/components/layout/index.ts` — barrel export

**Güncellenmiş dosyalar (7):**

- `frontend/src/lib/stores/auth-store.ts` — 4 yeni alan (isAdmin, isSuspended, displayName, avatarUrl) + setProfile partial setter + logout reset cascade
- `frontend/src/app/[locale]/(main)/layout.tsx` — stub `<div>` yerine `<MainShell>{children}</MainShell>`
- `frontend/src/app/[locale]/admin/layout.tsx` — stub `<div>` yerine AdminHeader + AdminSidebar + main split
- `frontend/src/i18n/messages/en.json` — `nav` extension (signOut/support/unread/adminFallback/primary/suspendedNav) + yeni `adminNav` (8 öğe + ariaLabel) + yeni `footer` (tos/privacy/privacyComingSoon)
- `frontend/src/i18n/messages/tr.json` — aynı şema, Türkçe çevirileri
- `frontend/src/i18n/messages/zh.json` — aynı şema, 中文
- `frontend/src/i18n/messages/es.json` — aynı şema, Español

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Kullanıcı header: logo, bildirim, profil, dil, ayarlar | ✓ | `Header.tsx`: logo Link `/dashboard`, bildirim Link `/notifications` + unread badge, profil Link `/profile` + avatar, `<LanguageSelector />` (C10), ayarlar Link `/settings`. `npm run build` 18 route compile. |
| 2 | Suspended header: logo, dil, destek, çıkış (kısıtlı) | ✓ | `SuspendedHeader.tsx`: logo Link, `<LanguageSelector />`, destek Link, logout butonu. Bildirim/Profil/Ayarlar yok — 04 §6.7/§7.3 "Suspended Session Override" eşleşir. |
| 3 | Admin header: logo, admin adı, çıkış | ✓ | `AdminHeader.tsx`: logo `/admin/dashboard`, `displayName ?? t("adminFallback")` (data-testid="admin-name"), logout butonu. §8.1 ASCII art birebir. |
| 4 | Admin sol menü: dashboard, flag'ler, işlemler, ayarlar, steam hesapları, roller, kullanıcılar, audit log | ✓ | `AdminSidebar.tsx` `MENU` 8 entry, `useTranslations("adminNav")` 8 key (dashboard/flags/transactions/settings/steamAccounts/roles/users/auditLog), `usePathname` ile active state, `aria-current="page"`. Plan kabul kriteri 8 öğeye 1:1. |

**Doğrulama kontrol listesi (plan, tek satır):**

- [✓] 04 §7.1 ve §8.1 layout tanımları doğru mu? — §7.1 ascii (Logo | Bildirimler | Profil | Dil | Ayarlar) `Header.tsx`'te + §8.1 ascii (Admin Header logo+ad+çıkış, sol menü 7 öğe) + plan kullanıcılar 8. öğe sidebar'a eklendi.

## Test Sonuçları

Plan: **Yok** (görsel chrome — E2E'de test edilecek).

| Tür | Sonuç | Detay |
|---|---|---|
| Unit | — | Plan gereği yok |
| Integration | — | Plan gereği yok |
| Lint | ✓ PASS | `npm run lint` → 0 error, 0 warning (eslint v9 flat config) |
| Type-check | ✓ PASS | `npm run build` → "Finished TypeScript in 4.9s" (next build TS pipeline) |
| Build | ✓ PASS | `npm run build` → 18 route compile (existing + 0 yeni route — T85 sadece layout chrome) |
| Format | ✓ PASS | `npx prettier --write` T85 14 dosya, hepsi yazıldı veya unchanged; pre-existing drift T84 K8'den miras, dokunulmadı |

**Browser doğrulaması:** Bu yapım chat'i headless Windows ortamında çalıştığı için canlı browser render doğrulanmadı; build + lint + TypeScript kontratı yeşil statik garanti, validator chat'i `npm run dev` ile görsel doğrulama yapacak.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

**Validator çıktısı (bağımsız spec conformance review, 2026-05-19):**

- **Hard-stop kapıları:** Adım -1 working tree temiz ✓, Adım 0 main CI son 3 run hepsi success (`26113451017`/`26113451018`/`26106824890`) ✓, Adım 0b memory drift kontrolü T85 satırları MEMORY.md L199-L203 mevcut ✓, Adım 8a task branch CI run [`26116320346`](https://github.com/turkerurganci/Skinora/actions/runs/26116320346) HEAD `2fb208c` 10/10 job success ✓.
- **Kabul kriterleri:** 4/4 ✓ — Header.tsx 5 öğe (logo+bildirim+profil+dil+ayarlar) §7.1 ascii birebir, SuspendedHeader.tsx 4 öğe (logo+dil+destek+çıkış) MainShell `isSuspended` koşullu, AdminHeader.tsx 3 öğe (logo+ad+çıkış) §8.1 birebir, AdminSidebar.tsx 8 öğe (plan supersede ascii 7).
- **Test:** Plan beklentisi yok. Lint 0E/0W, type-check ✓ ("Finished TypeScript in 2.5s"), build ✓ ("Compiled successfully in 2.9s", 18 route).
- **Güvenlik:** Secret sızıntısı temiz, auth-impact yok (chrome-only), input validation yok, yeni dış bağımlılık yok (`package.json` diff boş).
- **i18n:** 4 locale (en/tr/es/zh) anahtar simetrisi tam — `nav.{primary,suspendedNav,support,signOut,unread,adminFallback}` + `adminNav.{ariaLabel + 8 menu key}` + `footer.{tos,privacy,privacyComingSoon}`.
- **Rapor karşılaştırması:** Tam uyumlu — yapım raporunun 4 kabul kriteri tablosu, dosya envanteri (7 yeni + 7 güncel), lint/build/TS claim'leri, PR #130 + CI run referansı bağımsız bulgularla 1:1 eşleşti.
- **Notlar:** §7.1 strict "Suspended header variant" tanımlamıyor; plan T85 kabul kriteri ayrı variant istiyor, build plan'ı izledi (spec-vs-plan farkı plan onayında implicit kabul). §8.1 ascii 7 öğe vs plan 8 öğe drift'i memory L202'de "plan supersede" onayı ile kayıtlı.

## Altyapı Değişiklikleri

- **Migration:** Yok (frontend-only)
- **Config/env değişikliği:** Yok
- **Docker değişikliği:** Yok
- **Yeni dış bağımlılık:** Yok — `next/link`, `next/navigation` (usePathname), `next-intl` (useLocale/useTranslations), `zustand` (useAuthStore), `@/components/common/LanguageSelector` (T84 C10) mevcut stack'ten

## Commit & PR

- Branch: `task/T85-global-layout`
- Commits: `300c139` (T85: Global layout) + `89f45f1` (T85: report PR# + commit back-fill)
- PR: [#130](https://github.com/turkerurganci/Skinora/pull/130)
- CI: ✓ PASS — HEAD `89f45f1` run [`26115822113`](https://github.com/turkerurganci/Skinora/actions/runs/26115822113) 9/9 job success + 1 expected skip (guard direct-push). Önceki commit `300c139` run `26115760614` newer push ile cancelled (concurrency, beklendik).

## Known Limitations / Follow-up

- **K1 (T87 devir):** Auth-store stub alanları (`isSuspended`, `isAdmin`, `displayName`, `avatarUrl`) T87 Auth akışında gerçek `/users/me` çağrısıyla doldurulacak. T85 default `false`/`null` ile çalışır; tüm route'lar bu durumda Header (suspended değil) render eder.
- **K2 (T100 devir):** Admin layout authentication/authorization guard yok — `isAdmin === false` kullanıcı admin route'a girerse layout yine render edilir. T100+ admin task'larında route-level guard (middleware veya layout-level redirect) eklenecek. Mevcut chrome boş data ile crash olmaz.
- **K3 (T96 devir):** Bildirim badge `unreadNotifications` prop ile geçilir (Header parametresi), `MainShell` şu an her zaman 0 verir — SignalR notifications hub bağlandığında (T96) gerçek count store'dan beslenecek.
- **K4 (T86 devir):** Footer T85'te oluşturuldu ama hiçbir layout'a wire edilmedi (Landing page T86'da yazılacak). `<Footer />` `index.ts`'ten export ediliyor, T86 import edip Landing'in sonuna ekleyebilir.
- **K5 (T103 devir):** AdminSidebar "steamAccounts" → `/admin/steam-accounts` linki ama route henüz mevcut değil. T103 (Admin Steam hesapları S18) bu route'u oluşturacak; o zamana kadar tıklama → 404. Sidebar kabul kriterinde 8 öğenin tümü görünmesi gerektiği için link bırakıldı.
- **K6 (T94 devir):** Header "Ayarlar" → `/settings` linki ama route henüz mevcut değil. T94 (Hesap ayarları S10) bu route'u oluşturacak.
- **K7 (S20 devir):** Footer "Privacy" linki `/privacy` route'u T85'te oluşturulmadı (plan'da Privacy Policy "ileride"); placeholder link `aria-disabled="true"` + `title={privacyComingSoon}` ile işaretli.
- **K8 (T98 devir):** Header'da mobil breakpoint hint'leri (`sm:inline`) var ama tam responsive audit T98'de yapılacak. Şu an `sm` (640px) altında text label'lar gizlenir, ikonlar görünür.
- **K9 (S20 destek route):** SuspendedHeader "Destek" linki default `/support`'a gider; bu route MVP scope'ta yok, 04 §6.7'de "Destek linki" referansı var ama route tanımlanmamış. T-future / `supportUrl` prop ile mailto:/external URL'e override edilebilir.

## Notlar

- **Working tree (Adım -1):** Temiz (`git status --short` boş — F4 Gate Check + T84 finalize sonrası temiz state'ten başlandı).
- **Main CI startup (Adım 0):** Son 3 run hepsi `success`:
  - `26113451017` CI #129 (T84 squash ff99de4) ✓
  - `26113451018` Docker Publish #129 ✓
  - `26106824890` CI #128 (F4 docs fix) ✓
- **Dış varsayımlar (Adım 4):**
  1. T84 ortak bileşenler ✓ — `frontend/src/components/common/` 17 dosya + index.ts mevcut, özellikle `LanguageSelector` (C10) Header/SuspendedHeader/Footer'da kullanılıyor.
  2. next-intl 4 locale ✓ — `i18n/routing.ts` `["en","zh","es","tr"]`, `useTranslations` + `useLocale` kalıbı T84 boyunca yerleşik.
  3. Tailwind v4 ✓ — T84 stilini birebir uygula (`bg-*`, `border-*`, `flex`, `gap-*`).
  4. zustand mevcut ✓ — `auth-store` zaten zustand, sadece alan/eylem genişletildi.
  5. Next.js 16 App Router server/client sınırı ✓ — interaktif bileşenler `"use client"`, layout.tsx server (children pass-through), client shell wrapper `MainShell.tsx` ile conditional render handle.
  6. Admin layout authentication guard varsayımı: yok, T100+ devir (K2).
  7. Plan vs. §8.1 ascii art drift: §8.1 7 öğe, plan 8 öğe (kullanıcılar). Plan supersede, sidebar 8 öğe.
  0 kırık varsayım.
- **Scope kararları (Adım 5, proje sahibi onayı 2026-05-19):**
  - Tüm 5 layout bileşeni + MainShell client wrapper + auth-store 4 alan extension — Önerilen ("Onayla — uygula").
  - Footer bileşeni T85'te oluştur, T86 wiring devir — Önerilen kapsam içinde.
  - Auth-store stub alanları (isAdmin/isSuspended default false) — T87 gerçek değerleri set edecek.
- **Mini güvenlik kontrolü (Katman 1):**
  - Secret sızıntısı: yok (yalnız UI strings + Tailwind class'ları + i18n key'leri).
  - Auth/authorization: yok (frontend chrome; admin route guard K2 forward-deferred T100, "admin" UI gösterimi yetkilendirme değildir, mevcut prensip).
  - Input validation: yok (yeni kullanıcı girdisi yok).
  - Yeni dış dep: yok (`package.json` değişmedi, mevcut next/link, next/navigation, next-intl, zustand kullanıldı).
- **Stack uyumluluğu:**
  - Server layout (`(main)/layout.tsx`, `admin/layout.tsx`) `"use client"` değil — children server-rendered kalır.
  - Client shell (`MainShell.tsx`) `"use client"` ile zustand subscription'ı handle eder.
  - AdminHeader/AdminSidebar/SuspendedHeader/Header/Footer hepsi `"use client"` (interaktif veya hook'lu).
- **Route-active state:** AdminSidebar `usePathname` ile current route'u alır; locale prefix dahil match (`/${locale}${path}` startsWith). Locale değişimi LanguageSelector ile yapıldığında pathname'in yeni locale segment'i ile başlaması beklenir — match algoritması bunu doğru handle eder.
