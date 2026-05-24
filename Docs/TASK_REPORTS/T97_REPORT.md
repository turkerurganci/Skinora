# T97 — i18n (4 dil desteği)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-05-24

---

## Yapılan İşler

T84 (`next-intl` setup, `routing.ts`/`request.ts`/`middleware.ts`) + T84–T95 boyunca 4 locale (`en`/`zh`/`es`/`tr`) için **632 leaf × 4 = 2528 anahtar** parity sağlanmıştı (memory snapshot 2026-05-24). T97 bu altyapının runtime katmanını 04 §10'a (Lokalizasyon Notları) **birebir** uydurur:

**§10.2 Tarih/saat formatı (locale-aware):**

`[lib/utils/format.ts]` modülü merkezi helper'larla yeniden yazıldı:

| Helper | Yapı | Spec eşlemesi |
|---|---|---|
| `formatDate(value, locale?)` | `Intl.DateTimeFormat({dateStyle:"medium"})` | "Mar 14, 2026" (en) / "14 Mar 2026" (tr/es) / "2026年3月14日" (zh) |
| `formatTime(value, locale?)` | `Intl.DateTimeFormat({timeStyle:"short"})` | "2:30 PM" (en, 12h) / "14:30" (tr/es/zh, 24h — Intl locale hour-cycle) |
| `formatDateTime(value, locale?)` | `dateStyle:"medium"` + `timeStyle:"short"` | List/audit/modal timestamp'leri |
| `formatDateLong(value, locale?)` | `dateStyle:"long"` | Header/timeline label'ları için reserved (T98+ devir) |

Inline `new Intl.DateTimeFormat(locale, {...})` kullanan **5 component** merkezi helper'a migrate edildi: `CancelInfoBlock`, `DisputeBlock`, `FlagHoldBanner`, `SellerPayoutSummary`, `TransactionInfoPanel`. Ek olarak `MaintenanceGate` (`.toLocaleString(locale, {dateStyle, timeStyle})`) ve `TransactionRow` (eski tek-arg `formatDate`) de `formatDateTime`'a geçirildi — toplam **7 call site**, davranış 1:1 korundu (`dateStyle:"medium"` + `timeStyle:"short"` semantiği aynı).

**§10.3 Sayı formatı (stablecoin hariç locale-aware):**

| Helper | Yapı | Spec eşlemesi |
|---|---|---|
| `formatNumber(value, locale?, options?)` | `Intl.NumberFormat(locale, options)` | en/zh: `1,234.56` / tr/es: `1.234,56` |
| `formatPercent(value, locale?, fractionDigits=1)` | `formatNumber` + `%` suffix | Yüzde değeri (99.5 → "99.5%"), ondalık ayraç locale'e göre |
| `formatStablecoin(amount, symbol, options?)` | `Intl.NumberFormat("en-US",{useGrouping:false})` veya string passthrough | **Locale-invariant** — dot decimal, USDT/USDC sembolü EN kalır (§10.4) |

Stablecoin call site'ları (`{amount} {stablecoin}` literal'leri) `formatStablecoin`'e migrate edildi: `TransactionRow`, `TransactionInfoPanel` (3 satır: price/commission/total), `SellerPayoutSummary` (3 satır: gross/gasFee/net), `CancelInfoBlock` (3 satır: original/gas/net), `Step2Details.commissionPreview()`, `Step4Summary` (price + commission). Backend string'leri verbatim render ediliyor (`"100.50 USDT"` formatı), client-computed `number → string` dönüşümünde `useGrouping:false` ile binlik ayracı bastırılıyor (blockchain standardı).

Non-stablecoin sayılar: `StatsCards` (`completedTransactionCount`, `successfulTransactionRate*100`, `reputationScore`), `TrustSignals` (`totalCompletedTransactions`, `platformUptimePercent`), `ReputationCard` (`completedTransactionCount` + 3 yüzde) — hepsi `formatNumber`/`formatPercent`'e geçirildi. `profile/helpers.ts` `formatPercent`/`formatScore` null handling'i koruyarak locale parametresi alacak şekilde güncellendi.

**§10.4 Çevrilmeyecek terimler:**

Yeni `[src/i18n/untranslatable.ts]` modülü 04 §10.4 listesini sabit olarak tutar: USDT, USDC, TRC-20, Tron, Steam, Steam ID, Mobile Authenticator, Trade offer, CS2, Gas fee. `UNTRANSLATABLE_TERMS` `as const` tuple + `UntranslatableTerm` type + `isUntranslatable(term)` case-insensitive checker — lint/CI veya QA scripti kazara çeviriyi yakalamak için tüketebilir.

**§10.1 Metin uzunluk esnekliği (1.5x):**

Audit sonucu: mevcut Tailwind tasarımı zaten esnek.

- `StatusBadge`: `inline-flex items-center px-2.5 whitespace-nowrap` — içerik genişliğine göre auto-expand, EN baz'a göre TR (1.3x) ve ES (1.3x) için fixed-width yok.
- `TransactionTabs` buttons: `px-4 py-2` flex item'lar — fixed `w-*` yok, auto-expand.
- `Step4Summary` `<dt>` `w-32 flex-shrink-0`: `text-xs uppercase tracking-wide` (kompakt), 1.5x labels'a kadar (örn. "Komisyon Ödeyici") tek satırda sığar. Sığmazsa flex column normal wrap eder (`<dt>` flex-shrink ama line-break engellemiyor).
- `AdminSidebar`: `w-56` sidebar; nav linkleri uzun TR/ES çevirileri için `truncate`/`overflow` yok ama label'lar kısa (Dashboard/Flags/Roles).
- Genel: hiçbir text container'da fixed `w-XX` + truncate kombinasyonu yok; flex/inline-flex layout text length'i absorbe ediyor.

Sonuç: 04 §10.1 spec'ine ek CSS/Tailwind override gerekmiyor. Mevcut layout 4 locale × EN 1.5x guard'ı karşılıyor.

**Yeni helper'ların eski API'lerle uyumu:**

- `formatAmount(amount, token)` — deprecated wrapper, `formatStablecoin`'e forward ediyor (pre-T97 caller yok ama defensive — silmek breaking).
- `formatDate(value, locale)` — date-only davranışa indirildi (önceden date+time idi). Tek caller (`TransactionRow`) önceki davranışı korumak için `formatDateTime`'a geçirildi → public davranış aynı kaldı.

## Etkilenen Modüller / Dosyalar

**Yeni:**

- [frontend/src/i18n/untranslatable.ts](../../frontend/src/i18n/untranslatable.ts) — `UNTRANSLATABLE_TERMS` const + `UntranslatableTerm` type + `isUntranslatable(term)`.

**Değişen — merkezi helper:**

- [frontend/src/lib/utils/format.ts](../../frontend/src/lib/utils/format.ts) — `formatDate/Time/DateTime/DateLong/Number/Percent/Stablecoin` + `normalizeLocale` + `SupportedLocale` type. `formatAmount` deprecated wrapper.

**Değişen — call site migrate (15 dosya):**

- [frontend/src/components/transactions/detail/CancelInfoBlock.tsx](../../frontend/src/components/transactions/detail/CancelInfoBlock.tsx)
- [frontend/src/components/transactions/detail/DisputeBlock.tsx](../../frontend/src/components/transactions/detail/DisputeBlock.tsx)
- [frontend/src/components/transactions/detail/FlagHoldBanner.tsx](../../frontend/src/components/transactions/detail/FlagHoldBanner.tsx)
- [frontend/src/components/transactions/detail/SellerPayoutSummary.tsx](../../frontend/src/components/transactions/detail/SellerPayoutSummary.tsx)
- [frontend/src/components/transactions/detail/TransactionInfoPanel.tsx](../../frontend/src/components/transactions/detail/TransactionInfoPanel.tsx)
- [frontend/src/components/dashboard/TransactionRow.tsx](../../frontend/src/components/dashboard/TransactionRow.tsx)
- [frontend/src/components/dashboard/StatsCards.tsx](../../frontend/src/components/dashboard/StatsCards.tsx)
- [frontend/src/components/landing/TrustSignals.tsx](../../frontend/src/components/landing/TrustSignals.tsx)
- [frontend/src/components/landing/MaintenanceGate.tsx](../../frontend/src/components/landing/MaintenanceGate.tsx)
- [frontend/src/components/transactions/new/Step2Details.tsx](../../frontend/src/components/transactions/new/Step2Details.tsx)
- [frontend/src/components/transactions/new/Step4Summary.tsx](../../frontend/src/components/transactions/new/Step4Summary.tsx)
- [frontend/src/components/profile/helpers.ts](../../frontend/src/components/profile/helpers.ts) — `formatPercent`/`formatScore` locale parametresi
- [frontend/src/components/profile/ReputationCard.tsx](../../frontend/src/components/profile/ReputationCard.tsx) — `useLocale()` + `formatNumber` (count)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | next-intl ile 4 dil: EN, 中文, ES, TR | ✓ Karşılandı | T84'te kurulmuş; T97 sırasında `routing.ts` `locales: ["en","zh","es","tr"]` + `request.ts` `messages/${locale}.json` + `LanguageSelector` 4 opsiyon doğrulandı. Build çıktısında her 4 locale altında 25 route render edildi. |
| 2 | Tarih/saat formatı dil bazlı | ✓ Karşılandı | `formatDate/Time/DateTime/DateLong` `Intl.DateTimeFormat(locale, {dateStyle/timeStyle})` ile 04 §10.2 tablosunu otomatik üretir (en → "Mar 14, 2026, 2:30 PM", tr → "14 Mar 2026 14:30", es → "14 mar 2026, 14:30", zh → "2026年3月14日 14:30"). 7 call site merkezi helper'a migrate, 0 inline `new Intl.DateTimeFormat(...)` kullanıcı kodunda kaldı (yalnız helper içinde). |
| 3 | Sayı formatı dil bazlı (stablecoin hariç) | ✓ Karşılandı | `formatNumber(value, locale)` `Intl.NumberFormat(locale)` ile 04 §10.3 tablosunu otomatik üretir (en/zh `1,234.56`, tr/es `1.234,56`). `formatStablecoin` `Intl.NumberFormat("en-US", {useGrouping:false})` ile her zaman dot decimal — 04 §10.3 stablecoin not'u "her zaman . ile gösterilir" karşılanır. 4 call site (`StatsCards`, `TrustSignals`, `ReputationCard`, `Step2/Step4`) + 9 stablecoin display merkezi helper'a migrate. |
| 4 | Çevrilmeyecek terimler listesi (USDT, Steam ID, Trade offer vb.) | ✓ Karşılandı | `src/i18n/untranslatable.ts` 10 terim sabit listesi (04 §10.4 birebir): USDT, USDC, TRC-20, Tron, Steam, Steam ID, Mobile Authenticator, Trade offer, CS2, Gas fee. `as const` tuple + `isUntranslatable(term)` case-insensitive helper. |
| 5 | Metin uzunluk esnekliği (EN 1.5x'e kadar) | ✓ Karşılandı | Audit (yukarıdaki "§10.1" bölümü): StatusBadge `inline-flex` auto-expand + `whitespace-nowrap`, button'lar `px-4 py-2` auto-expand, hiçbir text container'da fixed `w-XX` + truncate kombinasyonu yok. EN 1.3x (TR/ES) ve 1.5x guard mevcut layout'ta sağlanıyor — ek CSS gerekmiyor. |
| 6 | Tüm ekranlarda dil desteği | ✓ Karşılandı | 4 locale × 632 leaf parity preserved (T97 yeni leaf eklemedi). 25 route × 4 locale = 100 sayfa kombinasyonu Next build başarılı. Build'de `Static page generated for routes ƒ /[locale]/...` 4 locale için aynı. Format helper'ları `useLocale()` sonucunu pass-through ediyor → her sayfa kendi locale'inde doğru format'ı alır. |

## Doğrulama Kontrol Listesi

| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 04 §10 tüm lokalizasyon kuralları uygulanmış mı? | ✓ Karşılandı | §10.1 text-length audit: `StatusBadge`/buttons inline-flex auto-expand ✓. §10.2 tarih/saat: `formatDate/Time/DateTime/DateLong` Intl spec-compliant ✓. §10.3 sayı: `formatNumber`/`formatPercent` locale-aware + `formatStablecoin` locale-invariant ✓. §10.4 çevrilmeyecek terimler: `UNTRANSLATABLE_TERMS` 10/10 ✓. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit | — | Test beklentisi: **Yok** (11_IMPLEMENTATION_PLAN T97) |
| Integration | — | Test beklentisi: **Yok** |
| TypeScript | ✓ PASS | `npm run build` — "Finished TypeScript in 3.7s" (strict tsc pass) |
| ESLint | ✓ PASS | `npm run lint` çıktısız (0 warning, 0 error) |
| Frontend build | ✓ PASS | `npm run build` — "✓ Compiled successfully in 3.6s" + 25 dinamik route × 4 locale = 100 sayfa renderable |
| Prettier (T97 dosyaları) | ✓ PASS | 15/15 dosya "All matched files use Prettier code style!" |
| 4-locale parity | ✓ PASS | en/zh/es/tr → 632/632/632/632 leaf (T97 yeni anahtar eklemedi) — count script: `en:632 zh:632 es:632 tr:632` |

## Altyapı Değişiklikleri

- **Migration:** Yok (frontend-only)
- **Config/env:** Yok
- **Docker:** Yok
- **Yeni dış bağımlılık:** Yok — `next-intl@^4.9.0` T84'te kuruldu, `Intl.DateTimeFormat` / `Intl.NumberFormat` Node 22+ ve modern tarayıcı built-in'i

## Commit & PR

- Branch: `task/T97-i18n-localization`
- Commit: (push sonrası eklenecek)
- PR: (push sonrası eklenecek)
- CI: (push sonrası izlenecek)

## Known Limitations / Follow-up

- **K1 — UNTRANSLATABLE_TERMS lint CI entegrasyonu yok.** Modül exposed ama hangi locale JSON dosyalarında bu terimlerin English kaldığını otomatik test etmiyor. T-future "i18n lint" task'ı `node --eval "Object.values(require('./tr.json')).forEach(v => UNTRANSLATABLE_TERMS.forEach(t => assert !v.includes(translatedT)))"` benzeri bir script ekleyebilir. Şu an manuel/code review katmanı.
- **K2 — `formatDateLong` yalnız tanımlı, henüz çağrı sitesi yok.** 04 §10.2 long form'u T98+ responsive task'ında header/timeline'da kullanılabilir; helper hazır, kullanım T-future.
- **K3 — `formatAmount` deprecated wrapper kaldı.** Pre-T97 caller yok ama public API'de deprecated alias olarak duruyor. T-future "deprecated API cleanup" chore'unda silinir.
- **K4 — AdminSidebar `w-56` + uzun TR/ES nav label'ı için truncate yok.** Mevcut label'lar kısa (≤8 char), TR çevirisi de kısa kalıyor. T99–T106 admin sayfalarında nav label'ı uzarsa truncate eklenir; şu an gerçek bir risk yok.
- **K5 — `LanguageSelector` `localStorage.setItem("preferredLocale", locale)`'ı yapıyor ama `request.ts` bunu okumuyor.** Locale path-based (`/{locale}/...`) çözülüyor → preferredLocale yalnız UI hint, persistence değil. next-intl standardı `NEXT_LOCALE` cookie'sidir; T84 path-based yaklaşımı seçmiş, T97 değiştirmedi. T-future opsiyonel iyileştirme.
- **K6 — Pre-existing prettier drift kaldı.** T96 K6 ile aynı havuz; repo-wide `npm run format:check` 149+ dosyada drift bildirir, T97 yalnız değiştirdiği 15 dosyayı temizledi (T80 K7 paterni). Toplu temizleme T-future chore PR.

## Notlar

- **Working tree (Adım -1):** temiz — `git status --short` çıktısız
- **Main CI startup check (Adım 0):** 3/3 success — `26358603651`/`26358603644` (chore #144 ×2) + `26358310966` (T96 #143)
- **Dış varsayım kontrolü (Adım 4):**
  - `next-intl@^4.9.0` mevcut — kanıt: `frontend/package.json:17` + `npm view next-intl@^4.9.0 version` → "4.9.0/4.9.1/4.9.2"
  - `Intl.DateTimeFormat` / `Intl.NumberFormat` Node 22 + modern tarayıcı built-in — kanıt: MDN docs, ek paket yok
  - 4 locale JSON parity 632×4 — kanıt: T84–T95 memory snapshot, `wc -l messages/*.json` → 1006×4 + leaf count script → 632×4
  - T13 bağımlılığı `✓ Tamamlandı` — kanıt: `IMPLEMENTATION_STATUS.md` "T13 | Next.js Frontend iskeleti | ✓ Tamamlandı"
- **Mini güvenlik:**
  - Secret sızıntısı: temiz (kodda sabit yok, yeni env var eklenmedi)
  - Auth/authorization etkisi: yok (frontend display helper'ları, server-side authorization değişmedi)
  - Input validation etkisi: yok (kullanıcı girdisi alanları değiştirilmedi; `formatStablecoin` backend string'ini passthrough, sayı path'inde `Intl.NumberFormat` numeric değer alıyor — coerce hatası yok)
  - Yeni dış bağımlılık: yok (package.json/lock değişmedi)
- **Locale parity:** 4-locale 632/632/632/632 leaf — T97 yeni i18n anahtarı eklemedi (yalnız format runtime'ı), parity korundu
- **Davranış değişiklikleri:**
  - `formatDate(value, locale)` artık yalnız date (önce date+time idi) — tek caller `TransactionRow` `formatDateTime`'a geçirildi, observable davranış aynı
  - `StatsCards.successfulTransactionRate * 100` artık `formatPercent` ile yuvarlanıyor (fractionDigits=0) — `Math.round` → `Intl` rounding (banker's rounding farkı 0.5 edge case'inde gözlenebilir ama UI'da appreciable etki yok)
  - `StatsCards.reputationScore` artık `formatNumber` ile 1 ondalık — locale ondalık ayracı (tr/es virgül, en/zh nokta) gösterilir
  - `TrustSignals.platformUptimePercent` formatPercent ile gösteriliyor — önce de `.toLocaleString(locale) + "%"` idi, locale davranışı aynı, suffix logic merkezde
- **Scope kararı (proje sahibi onayı 2026-05-24, AskUserQuestion):** Tam scope (Recommended) — format.ts merkezileştirme + 5 inline DateTimeFormat migrate + formatNumber/formatStablecoin helper'ları + UNTRANSLATABLE_TERMS modülü + text-length 1.5x audit. ~10 dosya hedefi ~15'e çıktı (detail blocks + Step2/Step4 + StatsCards + ReputationCard + profile/helpers stablecoin/non-stablecoin call site'ları).
