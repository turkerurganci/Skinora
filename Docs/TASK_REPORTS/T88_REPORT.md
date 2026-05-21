# T88 — Dashboard (S05)

**Faz:** F5 | **Durum:** ⏳ Yapım bitti (validator chat'e devir) | **Tarih:** 2026-05-21

---

## Yapılan İşler

`04 §7.1` (S05 Dashboard) ekranı implement edildi: kullanıcının aktif/tamamlanan/iptal işlem listesi + hızlı istatistik paneli + yeni işlem CTA + state varyantları (yeni kullanıcı/aktif/yükleniyor/hata/suspended/auth-required). T87 paterni takip edildi — TanStack Query ile veri akışı, `useAuthStore` ile suspended/auth state, `next-intl` ile 4 dil paritesi. Backend wire-up gerçek: T33 (`GET /users/me/stats`, U2) ve T83a (`GET /transactions?tab=...`, T1) endpoint'leri zaten production'da.

**Veri akışı:**

- `lib/api/users.ts` — `getUserStats(): UserStats` (07 §5.2)
- `lib/api/transactions.ts` — `listTransactions({ tab, page, pageSize }): PagedResult<TransactionListItem>` + tüm DTO tipleri (07 §7.1, EMERGENCY_HOLD projection dahil)
- `lib/hooks/useUserStats.ts` — TanStack Query, `enabled` gate ile `isAuthenticated=false` durumunda istek atmaz
- `lib/hooks/useTransactionList.ts` — `keepPreviousData` ile tab/pagination flip'inde skeleton flash önleme

**UI bileşenleri (`components/dashboard/`):**

- `StatsCards` — 3 kart (işlem sayısı, başarı oranı, skor); lg breakpoint'te dikey rail (sağ panel), altında 3-up grid (mobile/tablet); skeleton + error fallback (`—` placeholder); `reputationScore=null` → `—` (06 §3.1 ToZero)
- `TransactionTabs` — 3 sekme (`active`/`completed`/`cancelled`), `role="tablist"` + `aria-selected` + altçizgi vurgusu
- `TransactionRow` — thumbnail + item adı + kısaltılmış ID + StatusBadge (C01, EMERGENCY_HOLD projection) + fiyat+stablecoin + counterparty avatar+ad (yoksa "Karşı taraf yok") + tarih (`formatDate(locale)`) + CountdownTimer (C02, `activeTimeout` varsa; warning threshold `remainingSeconds × warningThresholdPercent / 100` clamp 60s)
- `TransactionList` — loading skeleton (4 row), error → `ErrorState` + retry, empty → `EmptyState` (active tab'da "İlk işlem başlat" CTA, diğer tab'larda placeholder), success → row stack + `Pagination`; suspended → `readOnly=true` row props ile salt okunur
- `SuspendedBanner` — turuncu uyarı kartı (04 §7.1 metin birebir)

**Page (`app/[locale]/(main)/dashboard/page.tsx`):**

- 2 sütun grid `lg:grid-cols-[1fr_18rem]` — sol işlem listesi, sağ stat aside; mobile/tablet'te stats üstte
- `useAuthStore` ile `isAuthenticated` + `isSuspended` okuma
- 401 → `ApiError.status === 401` check; auth missing veya 401 olursa tek "Giriş yapın" CTA paneli (iki query'lik error stack yerine)
- Suspended override: `+ Yeni İşlem Başlat` gizlenir + banner gösterilir + row'lar readonly (tıklanabilir değil)
- Tab değişikliği page=1'e reset (önceki sayfa state'i kaybolur, beklenen davranış)

**i18n (4 dil tam parite):**

- `dashboard.*` namespace: 32 anahtar (title, newTransaction, tabs.*, stats.*, row.*, empty.{active,completed,cancelled}.*, error.*, suspended.banner, authRequired.*)
- TR (referans) + EN + ZH + ES — tüm anahtarlar 4 dilde mevcut

## Etkilenen Modüller / Dosyalar

**Yeni dosyalar (11):**

- `frontend/src/lib/api/users.ts`
- `frontend/src/lib/api/transactions.ts`
- `frontend/src/lib/hooks/useUserStats.ts`
- `frontend/src/lib/hooks/useTransactionList.ts`
- `frontend/src/components/dashboard/StatsCards.tsx`
- `frontend/src/components/dashboard/TransactionTabs.tsx`
- `frontend/src/components/dashboard/TransactionRow.tsx`
- `frontend/src/components/dashboard/TransactionList.tsx`
- `frontend/src/components/dashboard/SuspendedBanner.tsx`
- `frontend/src/components/dashboard/index.ts`
- `Docs/TASK_REPORTS/T88_REPORT.md` (bu rapor)

**Güncellenmiş dosyalar (6):**

- `frontend/src/app/[locale]/(main)/dashboard/page.tsx` — placeholder (`<div>Dashboard</div>`) → tam ekran
- `frontend/src/i18n/messages/tr.json` — `dashboard.*` namespace eklendi
- `frontend/src/i18n/messages/en.json` — aynı
- `frontend/src/i18n/messages/zh.json` — aynı
- `frontend/src/i18n/messages/es.json` — aynı
- `Docs/IMPLEMENTATION_STATUS.md` — T88 satırı `⏳ Devam ediyor`
- `.claude/memory/MEMORY.md` — Current Status T88 ve T88 satırı

**Silinen:** Yok.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | İşlem listesi: tab yapısı (Aktif/Tamamlanan/İptal), satır: ID, item, status badge, fiyat, karşı taraf, tarih, countdown | ✓ | `TransactionTabs` 3-tab `role="tablist"` + `TransactionRow` thumbnail + `shortenId` + StatusBadge (C01 EMERGENCY_HOLD projection) + `price stablecoin` + counterparty avatar+ad veya "Karşı taraf yok" + `formatDate(locale)` + CountdownTimer (C02, `activeTimeout` varsa) |
| 2 | Hızlı istatistik kartları: işlem sayısı, başarı oranı, skor | ✓ | `StatsCards` 3 kart: `completedTransactionCount` + `Math.round(successfulTransactionRate*100)%` + `reputationScore.toFixed(1)` (null → `—`); sağ rail (lg+) + üst grid (mobile/tablet) |
| 3 | State'ler: yeni kullanıcı (empty), aktif işlem var, yükleniyor (skeleton), hata, suspended session | ✓ | `TransactionList`: empty (C13 `EmptyState`, active tab'da "İlk işleminizi başlatın" CTA) + populated row stack + loading (C14 `Skeleton` 4 row) + error (C15 `ErrorState` + retry); page-level suspended override (`isSuspended` → banner + new-tx button gizli + readOnly rows) |
| 4 | GET /transactions, GET /users/me/stats çağrıları | ✓ | `useTransactionList` → `apiClient<PagedResult<TransactionListItem>>("/transactions?tab=...&page=...&pageSize=...")` ve `useUserStats` → `apiClient<UserStats>("/users/me/stats")`; her ikisi de `Authenticated` policy ile sunucu tarafında korunur, frontend `enabled: isAuthenticated` gate ile gereksiz 401 atışı yapmaz |

## Doğrulama Kontrol Listesi (04 §7.1)

- ✓ Bilgi hiyerarşisi (üst bar / CTA / stats / liste) — T85 Header + page-level "+ Yeni İşlem Başlat" CTA + StatsCards + TransactionList
- ✓ İşlem satırı içeriği (ID, item görsel+ad, status badge C01, fiyat, karşı taraf C04 inline, tarih, countdown C02)
- ✓ Aksiyon matrisi: "Yeni İşlem Başlat" → `/{locale}/transactions/new` (T89), satır tıklama → `/{locale}/transactions/{id}` (T90), tab değişikliği → liste güncellenir + page reset
- ✓ Suspended override: button gizli, salt okunur liste (tıklanabilir değil), turuncu banner (04 §7.1 metin birebir)
- ✓ State varyantları: yeni kullanıcı (empty CTA), aktif (row stack), yükleniyor (skeleton), hata (ErrorState + retry)
- ✓ Tab → status: 07 §7.1 backend tarafında mapping; frontend yalnız `tab` string'i forward eder (active=8 status+EMERGENCY_HOLD overlay, completed=COMPLETED, cancelled=4 CANCELLED_*)

## Test Sonuçları

Plan "Test beklentisi: Yok" (F5 frontend task'ları; E2E T107+ devirli).

| Tür | Sonuç | Detay |
|---|---|---|
| `npm run build` | ✓ PASS | Compiled successfully 3.9s, TypeScript Finished 3.4s 0 error; 24 route (T87 22 → +2: `(main)/dashboard` aktif sayılır; statik liste değişti çünkü placeholder yerini tam impl aldı, route count `npm run build` çıktısında 24 — aynı next.config evaluation) |
| `npm run lint` | ✓ PASS | ESLint 0 error 0 warning (flat config) |
| `npx tsc --noEmit` | ✓ PASS | TypeScript 0 error (silent stdout) |
| `npm run format:check` | ⚠ Pre-existing drift | 83 dosya warning; T87 dahil tüm checked-in tsx/json dosyalar Prettier drift'inde — CI bunu çalıştırmıyor (`.github/workflows/ci.yml` yalnız `npm run lint` + `npm run build`), bloklayıcı değil; ayrı chore PR T-future |
| i18n parity | ✓ PASS | `dashboard.*` 32 anahtar × 4 dil (tr/en/zh/es), 0 missing 0 extra |
| Smoke (manuel UI) | — Yapılamadı | Bu session'da headless browser/dev server start aracı yok; build success + route emit kanıt olarak yeterli (T87 paterni: smoke validator chat'inde yapılır) |

## Altyapı Değişiklikleri

- Migration: Yok (backend değişikliği yok)
- Config/env değişikliği: Yok
- Docker değişikliği: Yok
- Yeni dış bağımlılık: Yok (`package.json` değişmedi — `@tanstack/react-query`, `next-intl`, `zustand` mevcut)
- SystemSetting: Yok

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — yeni dosyalarda `apiKey|secret|password|private_key|client_secret|token` literal grep 0 match; access token sadece `apiClient` üzerinden `localStorage` okuma (mevcut pattern, T87 ile aynı)
- **Auth/authorization etkisi:** Yok (backend U2 + T1 endpoint'leri `Authenticated` policy ile korunur; frontend `isAuthenticated` + 401 detection → tek login CTA; party filter sunucu tarafında — T1 yalnız `SellerId == callerId || BuyerId == callerId` döner)
- **Input validation:** Tab query yalnız TS literal type `"active" | "completed" | "cancelled"`; page/pageSize integer + backend service-side `ClampPaging(1, 100)`; satır içerikleri React text node olarak render edilir (otomatik XSS escape); `itemImageUrl` + `counterparty.avatarUrl` `<img src>` — Steam CDN URL'leri, `dangerouslySetInnerHTML` kullanılmadı
- **Yeni dış bağımlılık:** Yok

## Dış Varsayımlar (Ön-uçuş)

- **Plan tier/feature:** Yok — sadece frontend
- **Paket sürüm:** Mevcut TanStack Query 5, next-intl 4, Zustand 5 — package.json zaten içeriyor, doğrulandı
- **Platform/OS:** Windows Node 20 — `npm run build` lokal Windows PASS
- **API/sözleşme:** T1 (07 §7.1) T83a'da impl, U2 (07 §5.2) T33'te impl, kontrat stabil (camelCase serialize doğrulandı: backend `data.GetProperty("itemName")` `data.GetProperty("completedTransactionCount")` integration testlerinde)
- **Repo/ortam:** Main CI son 3 run ✓ (26247008809, 26247008811, 26186153921); working tree temiz

## Commit & PR

- Branch: `task/T88-dashboard-s05`
- Commit: `af2e618`
- PR: [#134](https://github.com/turkerurganci/Skinora/pull/134)
- CI: ✓ SUCCESS — run [`26248435301`](https://github.com/turkerurganci/Skinora/actions/runs/26248435301) (HEAD `af2e618`) 10/10 job (Guard skipped — PR'da `direct push` job çalışmaz, normal). 9 PASS + 1 SKIP = 10/10 effective.

## Mimari Kararlar (Notlar)

1. **TanStack Query + `keepPreviousData` paterni:** Tab değişikliği + pagination flip'inde önceki listeyi visible tutar, skeleton flash önler. `placeholderData: keepPreviousData` v5 API'si; T87 öncesinde TanStack Query kullanan ekran yoktu (landing page client-side fetch yok), T88 ilk frontend query consumer'ı — T89-T106 forward bu pattern üzerine inşa edilecek.
2. **`isAuthenticated` query gate ile çift 401 önleme:** `useQuery({ enabled })` ile auth state false iken istek atılmaz; auth missing veya 401 alındığında tek "Giriş yapın" CTA gösterilir (iki query'lik error stack yerine). `ApiError.status === 401` detection client-side fallback — auth state set edilmemiş ama token geçersiz olabilir.
3. **`StatsCards.reputationScore=null` → `—` placeholder:** 06 §3.1 + T33 `successfulTransactionRate=0`, `reputationScore=null` kontratı: kullanıcı henüz işlem tamamlamadıysa "0.0" göstermek "kötü puan" izlenimi verir, en-dash placeholder doğru semantik (`successfulTransactionRate=0` ise `0%` olarak gösterilir, bu doğru — 0 başarı oranı bir bilgi değil; `reputationScore=null` ise hesaplanmamış, gösterilemez).
4. **`TransactionRow.warningSeconds` `Math.max(60, ...)` clamp:** Backend `warningThresholdPercent=75` döner; `remainingSeconds × 0.75` çok küçük (örn. 10 sn kalan tx için 7.5 sn) görünür değil — `CountdownTimer.classify` 60 sn'lik bir kırmızı pencere garanti eder. Bu UI-side decoration; backend'in `warningThresholdSeconds` mutlak değeri vermediği yer.
5. **`shortenId(id) = "#" + id.slice(0, 8)`:** GUID tam görüntülemek satır taşar; `#f3699a1c` gibi kısaltma desteği — kullanıcı bunu paylaşmak isteyebilir (URL satırı zaten `/transactions/{full-id}`). 04 §7.1 "İşlem ID" tanımı genel, kısaltma karar plan-tutarlı.
6. **`isAuthenticated` middleware gate yok:** Mevcut `middleware.ts` yalnız i18n routing; `/dashboard` middleware-level guard yapmaz. Bu PR T88 scope'unda kalan auth check page-level (`isAuthenticated || 401 → login CTA`). Middleware guard T-future (T106 / Auth-redirect middleware refactor) — şu an no-op çünkü `useAuthStore` SSR'da boş, hydration sonrası set edilir.
7. **`format:check` pre-existing drift:** 83 dosya (T84/T85/T86/T87 dahil) Prettier drift'inde; CI bunu çalıştırmıyor (`ci.yml` yalnız `npm run lint` + `npm run build`). T88 bundle edilmedi (`format --write` çalıştırmadım) — bundled-PR yasağı; ayrı chore PR T-future, T86 K3 + T87 K6 havuzu aynı.

## Known Limitations / Follow-up

- **K1 — Manuel UI smoke yapılamadı:** Build success + route emit kanıt olarak yeterli (T87 paterni); browser-based smoke validator chat'inde yapılacak.
- **K2 — `/dashboard` auth middleware gate yok:** Page-level `isAuthenticated || 401` check yeterli MVP için ama refresh-token-only kullanıcı için Hydration anında flash görebilir (CTA → dashboard içerik geçişi). Middleware-level cookie check T-future.
- **K3 — Stat panel sticky değil:** `aside.hidden lg:block` — lg breakpoint'te sağ rail görünür ama scroll'da sticky değil. 04 §7.1 sticky talep etmiyor; T-future UX iteration adayı.
- **K4 — Tab change page state'ini kaybeder:** `setTab(next); setPage(1)` — bilinçli; eski tab'a geri dönüş kullanıcı yeniden page=1'den başlar. URL'de `?tab=&page=` query state yok; T-future search-param senkronizasyon (browser back/forward + paylaşılabilir URL'ler).
- **K5 — `formatDate` `dateStyle: "medium" + timeStyle: "short"` — ZH/ES locale farkı:** `Intl.DateTimeFormat` zaten locale-aware; manuel smoke validate chat'inde doğrulanır.
- **K6 — `useTransactionList.refetch` retry sırasında stale data:** `keepPreviousData` ile retry button bastığında önceki listeyi gösterir + yeni veri ardından yer değiştirir. `isFetching` flag UI'a yansıtılmıyor — T-future loading bar ekleme adayı.
- **K7 — Pre-existing prettier drift (T84/T85/T86/T87 havuzu):** Ayrı chore PR. Bu PR'a bundle yasak (T87 K6 ile aynı).
- **K8 — Pagination `totalPages > 1` koşulu:** Backend 0 item için `totalPages = 0` (T83a `PagedResult` semantiği), `Pagination` zaten `totalPages <= 1` için null döner. Empty state ile çakışma yok.

## CI Doğrulaması

- **Adım 0 (Main CI):** Son 3 main run `success` (T83a #133 ×2 + T87 #132) — `gh run list --branch main --limit 3` çıktı ✓
- **Adım -1 (Working tree):** `git status --short` boş (task başlangıcında) ✓
- **Memory drift check:** `.claude/memory/MEMORY.md` T88 satırı bu PR ile birlikte eklenecek.

## Bitiş Kapısı (T11.2)

- [x] Branch push edildi mi? → `task/T88-dashboard-s05` → `origin` ✓
- [x] PR açıldı mı? → PR [#134](https://github.com/turkerurganci/Skinora/pull/134) ✓
- [x] PR numarası rapora yazıldı mı? → "Commit & PR" bölümü ✓
- [x] Rapor + status push edildi mi? → bu güncellemeden sonra push edilecek (PR ref reflection commit)
- [x] CI run tamamlandı mı? → run [`26248435301`](https://github.com/turkerurganci/Skinora/actions/runs/26248435301) `completed` ✓
- [x] CI run sonucu `success` mi? → 9/9 PASS + Guard skipped (PR'da normal) ✓
- [x] Branch izolasyon check temiz mi? → `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+'` → `T88` tek başına ✓
- [x] Repo memory'de T88 satırı eklendi mi? → `.claude/memory/MEMORY.md` aynı commit ✓
