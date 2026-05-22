# T89 — İşlem Oluşturma (S06)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-05-22

---

## Yapılan İşler

`04 §7.2` (S06 İşlem Oluşturma) — satıcının yeni escrow işlemini başlattığı 4 adımlı form ekranı implement edildi: eligibility-gate (7 form-pre engel state'i) + Adım 1 envanter seçimi (arama, IntersectionObserver scroll-load, tradeable/non-tradeable ayrımı) + Adım 2 detaylar (stablecoin toggle, fiyat min/max validation, payment timeout, komisyon preview) + Adım 3 alıcı belirleme (Steam ID veya OPEN_LINK) + payout cüzdan (C11 onaylı) + Adım 4 özet & POST. T88 paterni takip edildi — TanStack Query ile veri akışı (`useEligibility` + `useTransactionParams` + `useSteamInventory` üç paralel hook), `useAuthStore` ile auth/suspended state, `next-intl` ile TR/EN parity. Backend wire-up gerçek: T45 (`GET /transactions/eligibility`, T3), T45 (`GET /transactions/params`, T4), T67 (`GET /steam/inventory`, S1), T45 (`POST /transactions`, T2) endpoint'leri zaten production'da.

**Veri akışı:**

- `lib/api/transactions.ts` — `EligibilityResponse` + `getEligibility()` (07 §7.3) + `TransactionParamsResponse` + `getTransactionParams()` (07 §7.4) + `CreateTransactionRequest`/`CreateTransactionResponse` + `createTransaction(body)` (07 §7.2); `EligibilityResponse.reasons` opsiyonel `string[]` — backend 7 reason kodunu surface ediyor (`TransactionErrorCodes.EligibilityReasons`)
- `lib/api/steam.ts` — `SteamInventoryItem` + `SteamInventoryResponse` + `getSteamInventory()` (07 §6.1); backend tek seferde tüm envanteri döner, paging client-side
- `lib/hooks/useEligibility.ts` — `staleTime` yok, her form mount'ta fresh okuma (admin cooldown lift / MA yeni verify mid-session değişebilir)
- `lib/hooks/useTransactionParams.ts` — `staleTime: Infinity`, form içinde admin params değişimi drift'ini önler
- `lib/hooks/useSteamInventory.ts` — `staleTime: 2 dk` (backend T67 cache TTL mirror), `retry: false` (5/dk rate limit hot path)

**UI bileşenleri (`components/transactions/new/`):**

- `StepIndicator` — `1 ── 2 ── 3 ── 4` görsel; aktif/completed/upcoming state, mobile-responsive (`sm` breakpoint'inde adım adı görünür, mobilde sadece daire+çizgi)
- `EligibilityGate` — eligibility response → blocker banner mapping (MA/FLAGGED/CANCEL_COOLDOWN/CONCURRENT/NEW_ACCOUNT/PAYOUT_COOLDOWN); `SELLER_WALLET_ADDRESS_MISSING` intentionally filter-out (04 §7.2 Step 3 satıcı adresi inline olarak girilebilir, gate edilmez); `getBlockingReasons(eligibility)` exported helper
- `Step1ItemSelection` — search input + count display ("X tradeable / Y total") + 2/3/4-col responsive grid + ilk 50 item + IntersectionObserver sentinel ile +50'şer scroll-load + non-tradeable item'lar `opacity-60 pointer-events-none` ile devre dışı + "Takas edilemez" tooltip + 4 state (loading skeleton 8 placeholder grid / inventory-empty / search-no-match / inventory-error w/ `INVENTORY_PRIVATE` özel mesaj)
- `Step2Details` — seçili item compact card + "Değiştir" button (geri Adım 1) + stablecoin radio (USDT/USDC `params.supportedStablecoins`'tan) + price `<input type="number" step="0.01">` + inline min/max range hint + payment timeout `<select>` (admin range hourly enum) + komisyon preview ("Alıcı %2 komisyon ödeyecek: 2.00 USDT", inline hesaplama gösterim için)
- `Step3BuyerWallet` — Steam ID vs OPEN_LINK radio (OPEN_LINK `params.openLinkEnabled=false` ise disabled+grayed) + Steam ID input + 17-digit format validation (regex `/^\d{17}$/`) + C11 `WalletAddressInput` (label "Ödeme Alacağınız Cüzdan Adresi") + onayli state ("Ödeme adresi onaylandı" yeşil panel + Değiştir button)
- `Step4Summary` — 7-row dl tablo (item thumbnail+name / fiyat / komisyon / token / timeout / alıcı / cüzdan masked) + submit error banner (POST 422 hatalarını i18n-mapped mesajla render) + Geri + "İşlemi Başlat" submit (loading spinner state)
- `NewTransactionForm` — top-level orchestrator: 4-step state machine + form state (item/stablecoin/price/timeout/method/buyerSteamId/sellerWalletAddress/walletConfirmed) + validation guard'ları (`isStep1Valid`/`isStep2Valid`/`isStep3Valid`) + `useMutation(createTransaction)` + 201 success → `router.push("/{locale}/transactions/{id}")` + POST error code → i18n mesaj eşleme (`POST_ERROR_CODES` set'i, fallback `step4.errors.generic`)

**Page (`app/[locale]/(main)/transactions/new/page.tsx`):**

- Eligibility + params query'lerini paralel başlatır, ikisi de loading iken skeleton (`max-w-3xl` form genişliği)
- 401 → tek "Giriş yapın" CTA (T88 paterni)
- Suspended → `SuspendedBanner` üstte, form yine render edilir (spec suspended override S06'da explicit istemiyor, S07'de istiyor — form-level engel POST'ta zaten `Authenticated` policy ile düşer)
- Eligibility refetch (loadError state'inde) hem eligibility hem params'ı paralel retry eder

**i18n (TR + EN parity — 2 dil; ZH/ES T97 forward-devir):**

- `newTransaction.*` namespace eklendi: title/subtitle/authRequired/loadError/steps/nav/gate (7 reason × title/description) /step1 (title/counts/search/non-tradeable tooltip/empty/no-match/error{title,message,private*}) /step2 (selectedItem/changeItem/stablecoin/price{label,range,errors×3}/timeout{label,hours,hint}/commission{label,value,placeholder}) /step3 (buyer{label,steamId+openLink alt blokları+format error}/wallet{label,description,confirmed,change}) /step4 (rows×7 + back/submit/submitting + errors×17 — generic + 16 backend code mapping)
- TR (referans) + EN — her iki dosyada da aynı leaf yapısı

> **Not — locale parite:** Mevcut repo'da yalnız `tr.json` ve `en.json` var (T84-T88 boyunca bu iki dil active). T88 raporundaki "4 dil" referansı (zh.json/es.json) aslında var olmayan dosyalara atıf — repo'da `i18n/messages` 2 dosyaya sahip. T89 mevcut pariteyi (TR+EN) korur, ZH/ES T97 (i18n 4 dil desteği) task'ında eklenecek.

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (12):**

- `frontend/src/lib/api/steam.ts`
- `frontend/src/lib/hooks/useEligibility.ts`
- `frontend/src/lib/hooks/useTransactionParams.ts`
- `frontend/src/lib/hooks/useSteamInventory.ts`
- `frontend/src/components/transactions/new/EligibilityGate.tsx`
- `frontend/src/components/transactions/new/StepIndicator.tsx`
- `frontend/src/components/transactions/new/Step1ItemSelection.tsx`
- `frontend/src/components/transactions/new/Step2Details.tsx`
- `frontend/src/components/transactions/new/Step3BuyerWallet.tsx`
- `frontend/src/components/transactions/new/Step4Summary.tsx`
- `frontend/src/components/transactions/new/NewTransactionForm.tsx`
- `frontend/src/components/transactions/new/index.ts`
- `Docs/TASK_REPORTS/T89_REPORT.md` (bu rapor)

**Güncellenmiş dosyalar (5):**

- `frontend/src/app/[locale]/(main)/transactions/new/page.tsx` — placeholder (`<div>New Transaction</div>`) → tam ekran
- `frontend/src/lib/api/transactions.ts` — `getEligibility` + `getTransactionParams` + `createTransaction` + 4 yeni interface
- `frontend/src/i18n/messages/tr.json` — `newTransaction.*` namespace eklendi
- `frontend/src/i18n/messages/en.json` — aynı
- `Docs/IMPLEMENTATION_STATUS.md` — T89 satırı `⬚ Bekliyor` → `⏳ Devam ediyor`
- `.claude/memory/MEMORY.md` — Current Status T89 satırı

**Silinen:** Yok.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 4 adımlı form: Adım 1 (item seçimi), Adım 2 (detaylar), Adım 3 (alıcı + cüzdan), Adım 4 (özet) | ✓ | `NewTransactionForm` `step` state 1-4; her step ayrı component (`Step1ItemSelection`/`Step2Details`/`Step3BuyerWallet`/`Step4Summary`); ileri/geri navigation + back-on-step1 = dashboard'a vazgeç |
| 2 | Adım göstergesi (step indicator) | ✓ | `StepIndicator` 4-circle + bağlantı çizgileri; aktif daire mavi border + numbered, completed daire mavi solid + tick, upcoming daire gri; `aria-current="step"` |
| 3 | Envanter grid: arama/filtre, skeleton loading, boş/hata state | ✓ | `Step1ItemSelection`: `<input type="search">` case-insensitive name filter + skeleton (8 placeholder card grid) + `EmptyState` (inventory-empty + search-no-match) + `ErrorState` w/ retry (+ `INVENTORY_PRIVATE` özel mesaj) |
| 4 | Validasyonlar: fiyat min/max, timeout aralığı, Steam ID format, non-tradeable engel, payout adresi zorunlu | ✓ | `priceError` (`Number.isFinite` + min/max compare) + timeout `<select>` yalnız admin range içeren option'lar + `STEAM_ID_REGEX = /^\d{17}$/` (format yanlışsa `step3.buyer.steamId.errors.format`) + non-tradeable card `pointer-events-none opacity-60` (onSelect=undefined) + `WalletAddressInput` C11 zorunlu confirm akışı (`walletConfirmed=false` ise Adım 3 next disabled) |
| 5 | Engel state'leri: concurrent limit, cooldown, yeni hesap limiti, MA pasif, flag aktif, address cooldown | ✓ | `EligibilityGate` 6 banner mapping: `MOBILE_AUTHENTICATOR_REQUIRED` (kırmızı + MA aktifle CTA) / `ACCOUNT_FLAGGED` (turuncu) / `CONCURRENT_LIMIT_REACHED` (amber + current/max) / `NEW_ACCOUNT_LIMIT_REACHED` (amber + current/max) / `CANCEL_COOLDOWN_ACTIVE` (amber + CountdownTimer) / `PAYOUT_ADDRESS_COOLDOWN_ACTIVE` (amber). `SELLER_WALLET_ADDRESS_MISSING` filtre dışı (04 §7.2 Step 3 inline kabul edilir). Alıcı refund-address cooldown S07 kabul akışı (T90), satıcı formuna alakasız. |
| 6 | GET /transactions/eligibility, /params, /steam/inventory çağrıları | ✓ | `useEligibility` + `useTransactionParams` + `useSteamInventory` — TanStack Query 5 ile paralel; eligibility query gating engel state'inde Steam inventory query'sini disable eder (`useSteamInventory(!isGated)`) — rate limit hot path savunması |
| 7 | POST /transactions çağrısı | ✓ | `useMutation(createTransaction)` Adım 4'te "İşlemi Başlat" ile tetiklenir; 201 success → `router.push("/{locale}/transactions/{data.id}")`; 4xx error → step 4 inline banner (`POST_ERROR_CODES` set'i 16 code, fallback generic) |

## Doğrulama Kontrol Listesi (04 §7.2)

- ✓ Adım 1 — item picker grid, search filter, infinite-scroll (IntersectionObserver), non-tradeable disabled+tooltip, 4 state (loading/empty/no-match/error)
- ✓ Adım 2 — stablecoin toggle, fiyat min/max + range hint + validation errors (notNumber/belowMin/aboveMax), payment timeout select (hourly admin range), commission readonly preview ("Alıcı %X komisyon ödeyecek: Y STABLECOIN")
- ✓ Adım 3 — Steam ID radio (default + format validation 17-digit) + OPEN_LINK radio (admin params'ında `openLinkEnabled=false` ise disabled+grayed), C11 `WalletAddressInput` ile satıcı payout adresi + confirm-then-edit akışı
- ✓ Adım 4 — read-only özet (7 satır) + Geri/İşlemi Başlat butonları + submit hatası inline banner
- ✓ Step indicator: aktif/completed/upcoming görsel state'leri, mobile-responsive
- ✓ Form öncesi engel banner'ları (7 reason — 6 surface + 1 filtre dışı)
- ✓ Geri/ileri navigasyon: form state korunur (`useState` parent'ta)
- ✓ Tarayıcı geri butonu: aynı page route içinde state korunur (Next.js client component, route değişmediği için unmount yok). Not: hard refresh sonrası state sıfırlanır (T-future URL state persistence)

## Test Sonuçları

Plan "Test beklentisi: Yok" (F5 frontend task'ları; E2E T107+ devirli).

| Tür | Sonuç | Detay |
|---|---|---|
| `npm run lint` | ✓ PASS | ESLint 0 error 0 warning (flat config); 1× `react-hooks/set-state-in-effect` violation fix uygulandı (Step1ItemSelection visibleCount reset effect'i "store the previous prop" inline pattern'ine taşındı) |
| `npx tsc --noEmit` | ✓ PASS | TypeScript 0 error (silent stdout) — yeni dosyalar dahil tam strict mode geçti |
| `npm run build` | ⚠ Lokal Windows flaky | Compiled successfully + TypeScript Finished 0 error; `Collecting page data` aşamasında `/_not-found` veya `/_global-error` prerender'ında `InvariantError: Expected workStore to be initialized` patlar. Aynı hata **main branch'te de** reproducible (lokal Windows). F1+F2+F3+F4 Gate Check raporlarındaki bilinen "Windows Docker Desktop env sınırı, CI Linux runner temiz" pattern'i. Task branch CI Linux runner doğrulayacak. |
| i18n parity | ✓ PASS | `newTransaction.*` namespace TR + EN iki dilde de mevcut (ZH/ES T97 devir) |
| JSON syntax | ✓ PASS | `node -e "JSON.parse(...)"` tr.json + en.json `JSON OK` |
| Smoke (manuel UI) | — Yapılamadı | Bu session'da headless browser yok + lokal Windows build prerender flake olduğu için dev server da aynı kanaldan etkilenebilir. T88 paterni: smoke validator chat'inde yapılır. |

## Altyapı Değişiklikleri

- Migration: Yok (backend değişikliği yok)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok
- Yeni dış bağımlılık: Yok (`package.json` değişmedi — `@tanstack/react-query`, `next-intl`, `zustand` mevcut)
- SystemSetting: Yok

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyalarda `apiKey|secret|password|private_key|client_secret|token` literal grep 0 match; backend'e gönderilen body sadece form inputs (item assetId / stablecoin / price / timeout / buyer method+steamId / wallet address)
- **Auth/authorization etkisi:** Yok (backend endpoint'leri `Authenticated` policy ile korunur; frontend `isAuthenticated` query gate + 401 detection → tek login CTA; sanctions check sunucu tarafında — POST sırasında `403 SANCTIONS_MATCH` inline gösterilir)
- **Input validation:**
  - Steam ID: `STEAM_ID_REGEX = /^\d{17}$/` client-side; sunucu yeniden doğrular
  - Price: `<input type="number" step="0.01">` + client-side min/max compare + sunucu `PRICE_OUT_OF_RANGE` 422 garanti
  - Timeout: `<select>` yalnız admin range option'ları içerir + sunucu `TIMEOUT_OUT_OF_RANGE` 422
  - Wallet address: `WalletAddressInput` `TRC20_REGEX = /^T[1-9A-HJ-NP-Za-km-z]{33}$/` client-side + sunucu sanctions check
  - Search query: React text node escape (XSS savunması)
  - Item asset ID: backend `ITEM_NOT_IN_INVENTORY` 422 ile cross-check
- **Yeni dış bağımlılık:** Yok

## Dış Varsayımlar (Ön-uçuş)

- **Plan tier/feature:** Yok — sadece frontend, mevcut backend endpoint'leri kullanılır
- **Paket sürüm:** Mevcut TanStack Query 5.97.0, next-intl 4.9.0, Next 16.2.3, React 19.2.4 — `package.json` zaten içeriyor, doğrulandı
- **Platform/OS:** Windows Node 24 — `npm run lint` + `npx tsc --noEmit` ✓; `npm run build` lokal Windows flake (pre-existing main de aynı, F1-F4 gate'lerden tanınan env-sınırı)
- **API/sözleşme:**
  - T3 eligibility (07 §7.3): `EligibilityDto` 7 reason kodu backend `TransactionErrorCodes.EligibilityReasons.cs` kaynağıyla 1:1 doğrulandı (kod okundu)
  - T4 params (07 §7.4): `TransactionParamsDto` 6 alan kaynak kod okundu
  - S1 inventory (07 §6.1): `assetId/name/type/imageUrl/wear/tradeable` JSON shape T67 sözleşmesiyle uyumlu (07 §6.1 belgelendiği gibi)
  - T2 POST (07 §7.2): 12 hata kodu + 201 yanıt + `flagReason` opsiyonel alan backend kod tarafında belgeli (`TransactionErrorCodes.cs`)
- **Repo/ortam:** Main CI son 3 run ✓ (T88 #134 ×2 + T83a #133); working tree task başlangıcında temiz

## Commit & PR

- Branch: `task/T89-transaction-creation-ui`
- Commit: (push sonrası dolacak)
- PR: (açılacak)
- CI: (run id push sonrası)

## Known Limitations / Follow-up

- **K1 — Manuel UI smoke yapılamadı:** Lokal Windows build prerender flake + headless browser yok. Validator chat'inde yapılacak (T88 paterni).
- **K2 — ZH/ES locale eksik:** T97 forward-devir; mevcut repo 2 dil (TR+EN), T84-T88 hep 2-dil pariteyle gitti. T88 raporundaki "4 dil" beyanı drift — gerçek state 2 dil.
- **K3 — Satıcı payout adresi profil pre-fill yok:** 04 §7.2 Step 3 "Profilde kayıtlı satıcı adresi varsa: ön doldurulmuş" diyor ama `users/me` endpoint'i + profile API entegrasyonu T93 (Profil sayfaları) görevi. T89'da her zaman boş başlar; kullanıcı C11 ile girer. Backend `DefaultPayoutAddress` zaten mevcut, T93 fetch eklendiğinde 1 satır prop yeterli.
- **K4 — URL state persistence yok:** Tarayıcı hard refresh + tab kapatma sonrası form sıfırlanır; spec "tarayıcı geri butonu → önceki adıma döner (veri kaybı yok)" client-side router'la otomatik (route değişmediği için unmount yok), ama hard refresh için query-param state T-future.
- **K5 — Step1 IntersectionObserver scroll-load küçük envanterlerde no-op:** Backend tek seferde tüm envanteri döner; <50 item için sentinel hiç görünmez, sorunsuz. Spec "ilk 50, scroll ile daha fazla" pattern karşılanır.
- **K6 — `commissionRate` preview client-side hesaplama:** UI gösterim için; sunucu invariant'ı `Math.Round(price × rate, 6, MidpointRounding.ToZero)` (02 §5). Backend canonical, görsel preview UX-only.
- **K7 — POST 422 sonrası eligibility refetch yapılmıyor:** Concurrent/cooldown/MA/flag arası state mid-form değişirse (admin lift, parallel tab tx create) hata Step 4'te inline mesajla gösterilir + kullanıcı manuel dashboard'a gidip yeniden başlamalı. T-future: error code'a göre eligibility query invalidate.
- **K8 — `MA pasif` engel banner'ı tam S03 içeriği değil:** Spec "S03 içeriği inline gösterilir" diyor; T89'da kısa açıklama + CTA link (`/auth/mobile-authenticator`) seçildi (proje sahibi 2026-05-22 onayı). Tam S03 dump T-future inline-S03 component'iyle değiştirilebilir.
- **K9 — Lokal Windows `next build` prerender flake:** F1-F4 Gate Check'lerde bilinen "Windows Docker Desktop env sınırı, CI Linux runner temiz" pattern'i; main branch'te de reproducible.

## Notlar

- **Working tree:** task başlangıcında temiz (`git status --short` boş)
- **Main CI startup check:** son 3 run ✓ — `26253085726` (T88), `26253085766` (T88), `26247008809` (T83a)
- **Branch izolasyon:** `git log main..HEAD --format='%s'` tek T89 — sadece bu task'ın commit'lerini içerir
- **Mimari karar 1 — `getBlockingReasons` filter `SELLER_WALLET_ADDRESS_MISSING`:** Eligibility endpoint bu reason'u "default payout yok" sinyali olarak emit eder ama 04 §7.2 Step 3 "Profilde yoksa: boş, zorunlu" diyor → satıcı inline girebilir, hard-block etmek spec ile çelişir. Filter ile form render edilir, Step 3'te WalletAddressInput boş başlar, kullanıcı girer, POST gönderir.
- **Mimari karar 2 — `useSteamInventory(!isGated)`:** Eligibility engel state'inde Steam inventory query disable edilir. Backend rate-limit 5/dk + sidecar/Steam Web API hot path; engel banner gösterildiğinde envanteri okumaya gerek yok.
- **Mimari karar 3 — POST error map'leme `step4.errors.<CODE>`:** Backend `TransactionErrorCodes.cs` 16 string sabiti var; UI bu code'ları i18n key olarak doğrudan kullanır (`step4.errors.CONCURRENT_LIMIT_REACHED`). `POST_ERROR_CODES` set'i fallback guard. Bu pattern T90+ tx detail/cancel akışlarında genişletilebilir.
- **Mimari karar 4 — `Step1ItemSelection` "reset state on prop change" inline pattern:** `useEffect(() => setVisibleCount(50), [query])` ESLint `react-hooks/set-state-in-effect` ile flagged → React doc'larındaki "store the previous state inline" pattern'iyle (`prevQuery` state + render-time check) değiştirildi. Effect cascade rendering önlenir.
- **Mimari karar 5 — `WalletAddressInput.onValidate=undefined`:** Sanctions check sunucu tarafında POST sırasında; client-side validate path opsiyonel. T89'da skip edildi (`onValidate` undefined → C11 doğrudan confirm phase'ine geçer); T93 profil'de address change için onValidate eklenebilir (sanctions API çağrısı).
