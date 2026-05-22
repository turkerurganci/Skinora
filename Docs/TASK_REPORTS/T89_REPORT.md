# T89 — İşlem Oluşturma (S06)

**Faz:** F5 | **Durum:** ⏳ Re-validate bekliyor (F1 fix uygulandı 2026-05-22) | **Tarih:** 2026-05-22

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

> **Not — locale parite (validator düzeltmesi 2026-05-22):** Yapım chat'inin "yalnız tr.json + en.json var" iddiası **factual error**. Repo'da `frontend/src/i18n/messages/` altında **4 dosya** var: tr.json + en.json (T89 sonrası 344 leaf), zh.json + es.json (240 leaf — T87/T88 boyunca eşit parity ile dolduruldu, Çince ve İspanyolca çeviriler içeride). T89, `newTransaction.*` namespace'ini sadece TR+EN'e ekledi → ZH+ES 104 leaf eksik. Sonuç: `/zh/transactions/new` ve `/es/transactions/new` runtime'da raw key render ediyor (validator curl smoke: `<h1>newTransaction.authRequired.title</h1>`). Bu T87/T88 4-locale parity pattern'ini bozan regresyon — T97'ye forward-devir doğru değil çünkü T97 yeni locale ekleme görevi, mevcut locale regresyonunu fix etme görevi değil. **Düzeltme:** ZH+ES'ye `newTransaction.*` 104 leaf eklenmeli (gerçek çeviri veya EN fallback stub). Bkz. `## Doğrulama Sonucu (Validator)` bölümü.

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
- Commit: `6fa4a9a`
- PR: [#135](https://github.com/turkerurganci/Skinora/pull/135) (OPEN — FAIL nedeniyle merge yok)
- CI: [26300957458](https://github.com/turkerurganci/Skinora/actions/runs/26300957458) ✓ 10/10 job success

## Known Limitations / Follow-up

- **K1 — Manuel UI smoke yapılamadı:** Lokal Windows build prerender flake + headless browser yok. Validator chat'inde yapılacak (T88 paterni).
- **K2 — ZH/ES locale `newTransaction.*` namespace eksik (validator finding F1):** Yapım chat'i ZH/ES dosyalarının var olmadığını sanmıştı — yanlış premise. Gerçek: 4 locale dosyası mevcut (TR/EN/ZH/ES), T87/T88 boyunca 240-leaf parity ile gidildi. T89 yalnız TR+EN'e `newTransaction.*` ekledi → ZH+ES'de 104 leaf eksik → runtime'da `/zh|/es/transactions/new` raw i18n key render ediyor. Düzeltme yeni yapım chat'ine devredildi.
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

---

## Doğrulama Sonucu (Validator)

**Tarih:** 2026-05-22
**Validator:** Türker Urgancı + Claude (Opus 4.7) bağımsız chat
**Verdict:** ✗ **FAIL** — düzeltme yeni yapım chat'inde

### Faz 1 — Ön Kontroller

- **Working tree (Adım -1):** `.claude/settings.local.json` modified → kullanıcı kararı: stash → doğrulama, sonra pop. Temiz state'te başlandı.
- **Main CI startup (Adım 0):** ✓ Son 3 main run hepsi success — [26253085726](https://github.com/turkerurganci/Skinora/actions/runs/26253085726) (T88), [26253085766](https://github.com/turkerurganci/Skinora/actions/runs/26253085766) (T88), [26247008809](https://github.com/turkerurganci/Skinora/actions/runs/26247008809) (T83a)
- **Repo memory drift (Adım 0b):** ✓ MEMORY.md'de T89 için 4 satır mevcut (210, 212, 213, 214)

### Faz 2 — Kabul Kriterleri Verdict

| # | Kriter | Verdict | Bağımsız Kanıt |
|---|---|---|---|
| 1 | 4 adımlı form | ✓ | `NewTransactionForm.tsx:141-228` step state + 4 component render guard'ları |
| 2 | Adım göstergesi | ✓ | `StepIndicator.tsx` aria-current="step", completed/active/upcoming görsel state'leri doğrulandı |
| 3 | Envanter grid (arama/filtre/skeleton/empty/error) | ✓ | `Step1ItemSelection.tsx:90-180` 4 state branch + IntersectionObserver pagination |
| 4 | Validasyonlar | ✓ | `STEAM_ID_REGEX = /^\d{17}$/` + priceError min/max + timeout select admin range + non-tradeable `pointer-events-none` + walletConfirmed gate |
| 5 | Engel state'leri (6 surface + 1 filtered) | ✓ | `EligibilityGate.tsx` 7/7 backend `TransactionErrorCodes.EligibilityReasons` ile 1:1 doğrulandı; `SELLER_WALLET_ADDRESS_MISSING` 04 §7.2 Step 3 inline-input gerekçesiyle filter'landı (mimari karar makul) |
| 6 | GET eligibility/params/inventory çağrıları | ✓ | 3 TanStack Query hook (`useEligibility`/`useTransactionParams`/`useSteamInventory`) backend `T45/T67` endpoint'lerine vurur |
| 7 | POST /transactions | ✓ | `useMutation(createTransaction)` 201→redirect, 4xx→inline banner (16 error code mapped + generic fallback) |

### Faz 2 — Kalite Gate'leri

- ✓ `npx tsc --noEmit` ExitCode=0 (silent)
- ✓ `npm run lint` ExitCode=0
- ✓ `npm run build` ExitCode=0 — Compiled successfully in 3.5s + TS 3.2s + static gen 3/3 137ms. **Rapor'daki "lokal Windows flaky" iddiası validator makinesinde üretilemedi** (env-spesifik veya pre-mevcut farklı bir state)
- ✓ Task branch CI run [26300957458](https://github.com/turkerurganci/Skinora/actions/runs/26300957458) 10/10 success: Lint, Build, Unit, Integration, Contract, Migration dry-run, Docker (frontend), CI Gate
- ✓ Backend kontrat: `TransactionErrorCodes.cs` 16 POST code + 7 EligibilityReason kaynak kod ile 1:1
- ✓ JSON syntax: 4 locale dosyası parse oluyor
- ⚠ Prettier `--check`: 16/19 T89 dosya fail (broader repo-wide drift, advisory only)
- ⚠ TR/EN parity 104/104 ✓ **ama ZH/ES parity 0/104 — F1 bulgusu**

### Faz 2 — Güvenlik Kontrolü

- ✓ Secret sızıntısı: temiz
- ✓ Auth: store gate + 401 detect + sunucu Authenticated policy
- ✓ Input validation: client + server defense-in-depth (regex + range + sanctions)
- ✓ Yeni dış bağımlılık: yok

### Faz 2 — Bulgular

| # | Seviye | Açıklama | Etkilenen | Düzeltme |
|---|---|---|---|---|
| F1 | **S2 Kırılma** | **ZH+ES locale i18n parity regression.** Runtime kanıt: `curl http://localhost:3000/zh/transactions/new` → HTML'de `<h1>newTransaction.authRequired.title</h1>` (raw key, çevirisiz). `/es/...` aynı 3 raw key gösterdi (curl smoke confirmed). TR'da Türkçe string doğru render ("Giriş yapmanız"). T89 `newTransaction.*` namespace'ini yalnız TR+EN'e ekledi; ZH+ES 240 leaf'te kaldı (104 eksik). T87/T88 4-locale parity pattern'i kırıldı. Auth-gate path'te 3 raw key görünür; auth sonrası form path'te 104 raw key görünür. | `frontend/src/i18n/messages/zh.json` + `es.json` — +104 leaf gerekli; `/zh/transactions/new` + `/es/transactions/new` rotaları | Yeni yapım chat'i — ZH+ES'ye `newTransaction.*` 104 leaf ekle (Recommended: gerçek çeviri; minimal: EN passthrough stub) |
| F2 | **S1 Sapma** | **Yapım raporunda factual error.** Önceki rapor "yalnız tr+en var" + K2 "mevcut repo 2 dil" dedi — yanlış. Glob doğruladı: 4 dosya mevcut, ZH/ES her biri 240 leaf'le dolu (Çince + İspanyolca çeviriler T87/T88'den). Yanlış premise'a dayalı T97 forward-deferral hatalı yön gösteriyordu (T97 yeni locale ekleme task'ı, regresyon fix değil). | `T89_REPORT.md` 41, 152 (validator tarafından düzeltildi) | Bu finalize commit'iyle düzeltildi |
| F3 | Advisory | **Prettier format drift.** 16/19 T89 dosya `prettier --check` fail. Pre-existing repo-wide drift (102 dosya). T71 K6 / T73 K6 / T79 K7 chore PR backlog'una eklenmişti. T89'a özel kırılma değil. | 16 T89 dosyası + 86 pre-existing | F5 sonu toplu chore PR (`chore: prettier --write` sweep) — PASS engeli değil |

### Faz 3 — Rapor Karşılaştırma

- **F1 uyuşmazlığı:** Yapım raporu K2 "ZH/ES T97 devir" diyor; runtime regresyonunu surface etmiyor + yanlış premise (dosyalar var olmadığı sanılmış). Validator runtime smoke ile gerçek davranışı kanıtladı.
- **Lokal build:** Rapor "Lokal Windows flaky" diyor; validator aynı Windows makinesinde 3.5s'de temiz build aldı. Pre-mevcut bir env state farkı olabilir (Docker Desktop, Node version, vb.), F5'in sonraki task'larında izlenmeli.
- **Test sonuçları:** Rapor unit test "Yok" diyor (plan F5 frontend için doğru); validator runtime smoke (curl) ile manuel kontrol yaptı, build/lint/typecheck/CI tümü ✓.

### Karar

- ✗ **FAIL** — F1 (S2 Kırılma) nedeniyle merge yapılmaz.
- Düzeltme yeni yapım chat'inde: `zh.json` + `es.json` dosyalarına `newTransaction.*` 104 leaf ekle (104 = TR/EN parity).
- Düzeltme sonrası yeni validate chat'i açılır.
- F2 (S1 Sapma) bu finalize commit'iyle düzeltildi (rapor K2 + locale parity notu güncellendi).
- F3 (Advisory) blocking değil, F5 sonu chore PR backlog'una kalır.

---

## F1 Düzeltme — Yapım Chat'i 2 (2026-05-22)

**Verdict ön-koşulu:** F1 (S2 Kırılma) — ZH+ES locale `newTransaction.*` 104 leaf eksik → raw key render.

**Düzeltme:**

- `frontend/src/i18n/messages/zh.json` — `newTransaction.*` namespace eklendi, 104 leaf (mainland Simplified Chinese çevirisi; teknik terimler EN: Steam, Mobile Authenticator, TRC-20, Stablecoin korundu)
- `frontend/src/i18n/messages/es.json` — `newTransaction.*` namespace eklendi, 104 leaf (neutral Spanish, "tú" form ile mevcut zh/es dashboard paterni mirror; teknik terimler EN korundu)
- Mevcut `dashboard.newTransaction` leaf (zh: "+ 发起新交易", es: "+ Iniciar una nueva transacción") aynen korundu — `dashboard.*` namespace'inden bağımsız bir leaf

**Doğrulama (lokal):**

- `node` JSON.parse → 4 dosya temiz parse (syntax error yok)
- Leaf parity: TR=344, EN=344, ZH=344, ES=344 — birebir eşleşme
- `newTransaction.*` namespace: TR=104, EN=104, ZH=104, ES=104 — birebir eşleşme
- `npx tsc --noEmit` → ExitCode=0 (frontend type-check temiz)
- Diff stat: `es.json +168` / `zh.json +168` — sadece namespace ekleme, mevcut leaf'lere dokunulmadı

**Re-validate gereken kontrol noktaları:**

- `curl /zh/transactions/new` ve `/es/transactions/new` runtime smoke — raw key görünmemeli
- `npm run build` Next.js production build temiz
- Task branch CI run yeniden tetiklenmeli
- ICU placeholder'lar (`{tradeable}`, `{total}`, `{query}`, `{current}`, `{max}`, `{percent}`, `{amount}`, `{min}`, `{max}`, `{hours}`, `{steamId}`) ZH+ES'de korunuyor — manuel review

**Re-validate bu yapım chat'inde değil, ayrı bir validate chat'inde yapılır** (feedback: validation_separate_chat).
