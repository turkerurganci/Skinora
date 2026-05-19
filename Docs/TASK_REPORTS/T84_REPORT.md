# T84 — Ortak UI Bileşenleri (C01–C17)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-05-19

---

## Yapılan İşler

`04 §5 Ortak Bileşen Kütüphanesi`'ndeki 17 ortak UI bileşeni Next.js 16 + React 19 + Tailwind v4 stack'i üzerinde implement edildi.

- **C01 Status Badge:** 14 durum (`TransactionStatus` 13 enum + `EMERGENCY_HOLD` virtual donma state'i) renk-kodlu rozet
- **C02 Countdown Timer:** real-time tick (`setInterval` 1sn), `warningThresholdSeconds` eşiğine göre green/yellow/red zone, kırmızıda pulse + `aria-live="assertive"`, `frozen` + `frozenReason` (`TimeoutFreezeReason`) ile donma state'i, `verbose` (`2g 5sa`) ve `clock` (`02:14:32`) format opsiyonları
- **C03 Item Card:** 3 varyant (`compact` / `detailed` / `selectable`), data URI placeholder onError fallback, tradeable/non-tradeable rozeti
- **C04 User Card:** 2 varyant (`compact` / `detailed`), 5-yıldız `ReputationStars`, completed transactions + account age satırları
- **C05 Transaction Timeline:** 8 adım yatay bar (`md+`) / dikey liste (mobil), tamamlanan=yeşil, aktif=mavi pulse, iptal=kırmızı X, flagged=turuncu pause
- **C06 Cancel Modal:** native `<dialog>` + `showModal()`, ESC/`cancel` event hook, min-length validation (`touched` blur tracking), `key` mount/unmount ile fresh state, refund bilgi paneli
- **C07 Dispute Form:** 3-adım state machine (`type` → `checking` → `result` → optional `escalation` → `done`), `onAutoCheck` async hook, 3 dispute type radyo (PAYMENT/DELIVERY/WRONG_ITEM)
- **C08 Maintenance Banner:** 4 varyant (`plannedMaintenance` sarı / `activeMaintenance` kırmızı / `steamOutage` turuncu / `blockchainDegradation` turuncu), sadece planlı varyant dismiss edilebilir
- **C09 Toast Notification:** 4 varyant (`info` / `success` / `warning` / `error`), `ToastProvider` + `useToast()` hook + `Toast` view, sağ-üst stack, otomatik 5s auto-dismiss, `aria-live="polite"`
- **C10 Language Selector:** 4 dil dropdown (EN / 中文 / ES / TR), `useLocale` + path-segment locale swap, `window.location.assign()` (mutation-free), localStorage `preferredLocale` cache
- **C11 Wallet Address Input:** TRC-20 regex `^T[1-9A-HJ-NP-Za-km-z]{33}$` format validation, opsiyonel async `onValidate` (sanctions screening hook'u — T82 endpoint bağlanacak), 2-aşamalı (input → confirm) flow
- **C12 Copy Button:** `navigator.clipboard.writeText` + 2 saniye `✓ Kopyalandı` feedback, clipboard API unavailable fallback (sessiz)
- **C13 Empty State:** ikon + başlık + açıklama + opsiyonel CTA slot
- **C14 Loading State:** `Skeleton` (animate-pulse placeholder), `Spinner` (sm/md/lg), `Progress` (value/max)
- **C15 Error State:** kırmızı container + retry CTA + `role="alert"`
- **C16 Pagination:** `1 2 3 ... 10` desen üreteci (≤7 sayfada flat liste; aksi halde 1 + ellipsis + current±1 + ellipsis + last), URL state owner-controlled (parent yönetir)
- **C17 Filter Bar:** dynamic field list (`select` / `text` / `date`), apply + clear butonları, aktif filtreler chip'i (remove × ile tekil filtre temizleme)

**Demo route:** `/[locale]/dev/components` — 17 bileşeni galeri olarak render eden showcase sayfası (validator görsel doğrulama için).

**i18n:** 4 dil mesaj dosyası (en/tr/zh/es) `status.*`, `countdown.*`, `itemCard.*`, `userCard.*`, `timeline.*`, `cancelModal.*`, `disputeForm.*`, `maintenanceBanner.*`, `languageSelector.*`, `walletAddress.*`, `pagination.*`, `filterBar.*` + `common.copy/copied` namespace'leri ile dolduruldu. Lokalizasyon notu (C01) 4 dil farklı uzunlukta etiket → badge `whitespace-nowrap` ile metin uzunluğuna esner.

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (20):**
- `frontend/src/components/common/StatusBadge.tsx` — C01
- `frontend/src/components/common/CountdownTimer.tsx` — C02
- `frontend/src/components/common/ItemCard.tsx` — C03
- `frontend/src/components/common/UserCard.tsx` — C04
- `frontend/src/components/common/TransactionTimeline.tsx` — C05
- `frontend/src/components/common/CancelModal.tsx` — C06
- `frontend/src/components/common/DisputeForm.tsx` — C07
- `frontend/src/components/common/MaintenanceBanner.tsx` — C08
- `frontend/src/components/common/ToastNotification.tsx` — C09 (Toast + ToastProvider + useToast hook)
- `frontend/src/components/common/LanguageSelector.tsx` — C10
- `frontend/src/components/common/WalletAddressInput.tsx` — C11
- `frontend/src/components/common/CopyButton.tsx` — C12
- `frontend/src/components/common/EmptyState.tsx` — C13
- `frontend/src/components/common/LoadingState.tsx` — C14 (Skeleton + Spinner + Progress)
- `frontend/src/components/common/ErrorState.tsx` — C15
- `frontend/src/components/common/Pagination.tsx` — C16
- `frontend/src/components/common/FilterBar.tsx` — C17
- `frontend/src/components/common/index.ts` — barrel export
- `frontend/src/lib/utils/cn.ts` — `cn(...classes)` utility (15+ bileşende tekrarı önler)
- `frontend/src/app/[locale]/dev/components/page.tsx` — dev showcase route

**Güncellenmiş dosyalar (4):**
- `frontend/src/i18n/messages/en.json` — 12 yeni namespace
- `frontend/src/i18n/messages/tr.json` — 12 yeni namespace
- `frontend/src/i18n/messages/zh.json` — 12 yeni namespace
- `frontend/src/i18n/messages/es.json` — 12 yeni namespace

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | C01 Status Badge: 14 durum, renk kodlu | ✓ | `StatusBadge.tsx` `STATUS_COLOR_MAP` 14 entry (13 `TransactionStatus` + `EMERGENCY_HOLD`); demo page `c01` section'da hepsi render edilir |
| 2 | C02 Countdown Timer: gerçek zamanlı, renk geçişli, frozen state | ✓ | `CountdownTimer.tsx` `setInterval` 1sn, `classify()` green/yellow/red zone, `frozen`+`frozenReason` props; demo page 5 örnek (Far/Warning/Critical/Frozen/Clock) |
| 3 | C03 Item Card: Compact / Detailed / Selectable | ✓ | `ItemCard.tsx` `variant: 'compact'\|'detailed'\|'selectable'`; demo page 3 varyant |
| 4 | C04 User Card: Compact / Detailed | ✓ | `UserCard.tsx` `variant: 'compact'\|'detailed'`; demo page 2 varyant |
| 5 | C05 Transaction Timeline: 8 adımlı ilerleme çubuğu | ✓ | `TransactionTimeline.tsx` `STEPS` 8 entry; cancelled+flagged state'ler ayrı render branch'leri; demo page 4 varyant (active/completed/cancelled/flagged) |
| 6 | C06 Cancel Modal: sebep textarea, iade bilgisi, onay | ✓ | `CancelModal.tsx` native `<dialog>`, `minReasonLength` (default 10) + `tooShort` validation, `refundDescription` slot; demo page modal trigger |
| 7 | C07 Dispute Form: 3 adımlı | ✓ | `DisputeForm.tsx` `Step` union (`type`/`checking`/`result`/`escalation`/`done`); demo page interactive |
| 8 | C08 Maintenance Banner: 4 varyant | ✓ | `MaintenanceBanner.tsx` `MaintenanceVariant` union 4 üye; demo page 4 banner peş peşe |
| 9 | C09 Toast Notification: bilgi/başarı/uyarı/hata | ✓ | `ToastNotification.tsx` `ToastVariant` 4 üye + `ToastProvider` + `useToast`; demo page 4 buton |
| 10 | C10 Language Selector: 4 dil | ✓ | `LanguageSelector.tsx` `routing.locales` iter (en/zh/es/tr); demo page selector |
| 11 | C11 Wallet Address Input: TRC-20 validation + sanctions + onay | ✓ | `WalletAddressInput.tsx` `TRC20_REGEX`, `onValidate` async hook (sanctions), 2-aşamalı confirm flow; demo page interactive |
| 12 | C12 Copy Button | ✓ | `CopyButton.tsx` clipboard API + 2sn `✓ Kopyalandı`; demo page |
| 13 | C13 Empty State | ✓ | `EmptyState.tsx` icon + title + description + action slot; demo page |
| 14 | C14 Loading State: Skeleton/Spinner/Progress | ✓ | `LoadingState.tsx` 3 named export; demo page üçü |
| 15 | C15 Error State | ✓ | `ErrorState.tsx` retry callback opsiyonel; demo page |
| 16 | C16 Pagination | ✓ | `Pagination.tsx` `buildPageList` ellipsis algoritması; demo page page=3/10 |
| 17 | C17 Filter Bar | ✓ | `FilterBar.tsx` field listesi + apply/clear + chip kaldırma; demo page 3 field (select/text/date) |

**Doğrulama kontrol listesi (plan, tek satır):**
- [✓] 04 §5'teki tüm bileşenler ve varyantları var mı? — 17 dosya + index.ts + demo galeri.

## Test Sonuçları

Plan: **Yok** (görsel bileşenler — E2E'de test edilecek).

| Tür | Sonuç | Detay |
|---|---|---|
| Unit | — | Plan gereği yok (T84 spec'i "test beklentisi: Yok") |
| Integration | — | Plan gereği yok |
| Lint | ✓ PASS | `cd frontend && npm run lint` → 0 error, 0 warning |
| Type-check | ✓ PASS | `cd frontend && npm run build` → "Finished TypeScript in 4.6s" (next build TS pipeline) |
| Build | ✓ PASS | `cd frontend && npm run build` → 18 route compiled; `/[locale]/dev/components` route registered |
| Format | ✓ PASS | `npx prettier --write` T84-scope dosyalarda; pre-existing drift (50 dosya) dokunulmadı, CI lint job zaten frontend prettier check'i çalıştırmıyor (sadece `dotnet format` ile backend) |

**Browser doğrulaması:** Bu yapım chat'i headless Windows ortamında çalıştığı için demo sayfasının canlı browser'da görsel render'ı doğrulanamadı. Validator chat'i (`/validate T84`) görsel doğrulamayı `npm run dev` + browser'da yapacak; build + lint + TypeScript kontratının yeşil olması yeterli statik garanti.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bekliyor (ayrı validate chat'i) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- **Migration:** Yok (frontend-only)
- **Config/env değişikliği:** Yok
- **Docker değişikliği:** Yok
- **Yeni dış bağımlılık:** Yok — Tailwind v4 + next-intl + React 19 + Next.js 16 mevcut stack kullanıldı; `cn()` local utility, headless dialog library eklenmedi (native `<dialog>` tercih edildi)

## Commit & PR

- Branch: `task/T84-common-ui-components`
- Commits: `0f36d01` (T84: Ortak UI bileşenleri) + `f733452` (T84: report PR# back-fill)
- PR: [#129](https://github.com/turkerurganci/Skinora/pull/129)
- CI: ✓ PASS — run [`26110153569`](https://github.com/turkerurganci/Skinora/actions/runs/26110153569) 9/9 job success + 1 skipped (guard direct-push, beklenen)

## Known Limitations / Follow-up

- **K1 (T85 devir):** Demo route `/[locale]/dev/components` dev-only galeri; T85 navigation entegrasyonunda dev-only ayrı menü/link gizlilik kararı (proje sahibi onayı gerekecek). Şimdilik route herkese açık ama unindexed.
- **K2 (T96 devir):** SignalR maintenance/Steam/Blockchain state hub event'leri C08 banner'ı tetikleyecek; T84 sadece UI primitive'i — wiring T96/T85.
- **K3 (T87 devir):** C06 + C07 modal pattern'i T87 Auth ekranlarındaki ToS modal'ı vs. (UI-043–045) tarafından devralınacak; native `<dialog>` primitive yeniden kullanılabilir, headless lib eklemeye gerek yok.
- **K4 (T82 devir):** C11 `onValidate` callback'i sanctions screening endpoint'ine (T82 `POST /sanctions/screen`) bağlanacak; T84 sadece prop/contract.
- **K5 (T100/T101/T104 devir):** UI-047 / UI-048 / UI-049 / UI-050 modal'ları admin ekranlarına özel; T84'te değil, ilgili admin task'larında yapılır (traceability matrix 11_IMPLEMENTATION_PLAN §7.4 satırı 2320 zaten doğru).
- **K6 (04 §5 spec drift T-future):** `EMERGENCY_HOLD` 04 §5 C01 tablosunda bir Transaction status olarak listelenmiş ama backend `TransactionStatus` enum'da yok (06 §2.1 13 değer). T84'te C01 14 etiket render eder, C02 ayrıca `frozenReason: TimeoutFreezeReason` ile donma gösterir (gerçek state). Doc-side düzeltme T-future: 04 §5'e dipnot — "EMERGENCY_HOLD bir transaction status'ü değil, `TimeoutFreezeReason.EMERGENCY_HOLD` ile tetiklenen donma state'i UI etiketidir; C01 14 renkli badge map'i UI-only convenience'tır." Bu T84'ün scope'unda değil — proje sahibi onayı + ayrı doc PR.
- **K7 (T98 devir):** Responsive davranış C05 Timeline (mobil dikey) hariç desktop-first; T98 tam responsive audit.
- **K8 (pre-existing prettier drift, T-future):** Frontend repo'da `npm run format:check` 50 dosyada drift bildiriyor (pre-existing, T13–T63b boyunca biriken). CI bu kontrolü çalıştırmıyor. Ayrı bir `chore: prettier verify-all` PR'ı önerilir; T84 sadece kendi dosyalarını formatladı (drift'i büyütmedi).
- **K9 (ESLint hermes-parser local install fragility, T-future):** Windows lokal'de `node_modules/hermes-parser/dist/generated/` bazen eksik kuruluyor (`npm install` sonrası). `rm -rf node_modules && npm ci` ile düzeliyor. CI Linux'ta sorunsuz. Ayrı chore — `package.json`'a `engines` veya postinstall script eklenebilir.

## Notlar

- **Working tree (Adım -1):** Temiz (`git status --short` boş).
- **Main CI startup (Adım 0):** İlk kontrolde 3 run'dan biri `failure` (PR #128 Docker Publish `sidecar-steam` `Log in to ghcr.io` step — transient ghcr.io auth flake, diğer 3 image aynı run içinde temiz). Proje sahibi onayı (2026-05-19) ile `gh run rerun 26106824879 --failed` çalıştırıldı; re-run `conclusion=success` (run watch background btgkkw7nq). Sonuç: son 3 main run `success` (26106824890 CI #128 + 26106824879 Docker #128 rerun + 26106252282 CI #127). Audit trail tamam.
- **Dış varsayımlar (Adım 4):** (1) Tailwind v4 setup kurulu (`globals.css` `@import "tailwindcss"`, `package.json` `"tailwindcss": "^4"`). (2) next-intl 4 locale mevcut (`i18n/routing.ts` `["en","zh","es","tr"]`). (3) React 19 server/client component sınırı tanımlı (interaktif olanlar `"use client"`). (4) 04 §5 ile 06 §2.1 arasında `EMERGENCY_HOLD` ufak drift → C01 14 etiket map'i UI-side (K6'da forward devir kaydı). 0 kırık varsayım.
- **Scope kararları (Adım 5, proje sahibi onayı 2026-05-19):**
  - Demo showcase: `/[locale]/dev/components` route — Önerilen.
  - Renk tokens: Tailwind utility classes — Önerilen.
  - Modal primitive: native `<dialog>` — Önerilen.
- **Mini güvenlik kontrolü (Katman 1):**
  - Secret sızıntısı: yok (yalnız UI strings + Tailwind class'ları).
  - Auth/authorization: yok (frontend, korumalı endpoint yok; demo route public ama hassas data exposed değil).
  - Input validation: C06/C07 client-side min-length, C11 TRC-20 regex — backend her zaman re-validate eder (mevcut prensip).
  - Yeni dış dep: yok.
- **Lockfile drift:** `npm ci` sonrası `package-lock.json` 11 cosmetic `"peer": true` eklemesi geldi — T84 scope'u dışı, `git checkout` ile revert edildi; CI `npm ci` mevcut lockfile ile zaten yeşil çalışır.
