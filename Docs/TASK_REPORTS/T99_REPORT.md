# T99 — Admin Dashboard (S12)

**Faz:** F5 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-24

---

## Yapılan İşler

- AD1 — `GET /admin/dashboard` (07 §9.1) için tipli frontend istemcisi: `frontend/src/lib/api/admin.ts` içine `AdminDashboardResponse` + 3 sub-record (`AdminDashboardSummaryCards`, `AdminSteamAccount`, `AdminDashboardRecentFlag`) + `AdminSteamAccountStatus` / `AdminFlagType` / `AdminFlagReviewStatus` string-enum tipleri. Wire format backend `Skinora.API/Services/AdminDashboardDtos.cs` ve `AdminSteamBotDtos.cs` ile 1:1 hizalı (T63 PR #100).
- React Query hook: `frontend/src/lib/hooks/useAdminDashboard.ts` — `useQuery({ staleTime: 30_000 })`. Tek AD1 fetch sayfa içinde fan-out yapar; üç child kendi `loading/error` sub-state'ini yönetir, parsiyel hata bot bloğunu blank etmez.
- S12 sayfası rewrite: `frontend/src/app/[locale]/admin/dashboard/page.tsx` (placeholder `<div>Admin Dashboard</div>` → tam sayfa). Layout: 1) Page heading; 2) `SummaryCards` (4 sütun, top-row); 3) `SteamAccountsStatus` + `RecentFlagsTable` 2-col grid (lg+) / tek kolon (mobil/tablet).
- Üç yeni bileşen:
  - `SummaryCards` (`frontend/src/components/admin/SummaryCards.tsx`) — 4 kart (`activeTransactions`/`pendingFlags`/`dailyCompleted`/`weeklyCompleted`). Her kart `<Link>` (04 §8.1 click table: Aktif → `/admin/transactions?tab=active`, Bekleyen Flag'ler → `/admin/flags?status=PENDING` (kırmızı badge — urgent variant), Günlük → `?range=daily`, Haftalık → `?range=weekly`). Target sayfalar T100/T101 forward-deferred — query param hot-link, target sayfa açıldığında filter honor edilir.
  - `SteamAccountsStatus` (`frontend/src/components/admin/SteamAccountsStatus.tsx`) — durum-tonlu kart grid (sm:2col / lg:3col), `STATUS_ICON` map (✓/⚠/✕/○), `statusTone` helper (ACTIVE=emerald, OFFLINE=gray, RESTRICTED+BANNED=red border). 04 §8.1 "Kısıtlı/banned bot uyarısı" iki kanaldan: (1) per-card kırmızı border, (2) header üstünde `role="alert"` banner — `degraded = accounts.filter(s ∈ {RESTRICTED, BANNED})`, banner `degraded.length > 0`. "Yönet" link + kart-click → `/admin/steam-accounts` (T103 forward).
  - `RecentFlagsTable` (`frontend/src/components/admin/RecentFlagsTable.tsx`) — `ResponsiveTable<AdminDashboardRecentFlag>` (T98 ilk tüketici) ID/Tür/Tarih/Durum 4 kolon. Mobil <md: 04 §9.4 dt/dd label/value card list (ResponsiveTable internal). "Tümünü Gör" link → `/admin/flags` (T100 forward). ID hücresi `<Link>` → `/admin/flags/{id}` (T100 detay forward), GUID prefix 8-char.
- 4-locale i18n parity (T97 paterni): `adminDashboard.*` namespace (~32 anahtar) `tr/en/es/zh` 4 dile eklendi. ICU placeholder `{count}` (warning), `{name}/{status}` (cardAriaLabel). Flag tür ve status terimleri 04 §8.1 + 04 §8.2 tablo terminolojisinden verbatim. Leaf parity: 1055/1055/1055/1055.
- Link target sayfalar `/admin/transactions`, `/admin/flags`, `/admin/steam-accounts` — ilk üçü router'da mevcut (T13 stub + sidebar kayıtlı). `steam-accounts` yolu Adminsidebar'da var ama sayfası henüz route'da değil — known limitation K2.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `frontend/src/lib/api/admin.ts`
- `frontend/src/lib/hooks/useAdminDashboard.ts`
- `frontend/src/components/admin/SummaryCards.tsx`
- `frontend/src/components/admin/SteamAccountsStatus.tsx`
- `frontend/src/components/admin/RecentFlagsTable.tsx`
- `frontend/src/components/admin/index.ts`

**Değişen:**
- `frontend/src/app/[locale]/admin/dashboard/page.tsx` (placeholder → tam sayfa)
- `frontend/src/i18n/messages/tr.json` (+adminDashboard namespace)
- `frontend/src/i18n/messages/en.json` (+adminDashboard namespace)
- `frontend/src/i18n/messages/es.json` (+adminDashboard namespace)
- `frontend/src/i18n/messages/zh.json` (+adminDashboard namespace)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Özet kartları: aktif işlemler, bekleyen flag'ler, günlük/haftalık tamamlanan | ✓ Karşılandı | `SummaryCards.tsx` 4 kart, `formatNumber(value, locale)` ile lokalize sayı. Bekleyen Flag'ler kart `urgent` variant'ı (kırmızı border + kırmızı sayı + kırmızı hover) 04 §8.1 "Sayı (kırmızı badge — acil)" şartını karşılar. Her kart `<Link>`. |
| 2 | Son flag'lenmiş işlemler tablosu (son 5) | ✓ Karşılandı | `RecentFlagsTable.tsx` `ResponsiveTable<AdminDashboardRecentFlag>` 4 kolon (ID kısa GUID prefix + flag detay link, Tür lokalize, Tarih `formatDateTime(locale)` `<time dateTime>`, Durum tonlu pill). Backend `AdminDashboardService.RecentFlagsLimit = 5` ile sınır server-side. "Tümünü Gör" link → `/admin/flags`. Boş durum `emptyMessage` ResponsiveTable internal. |
| 3 | Steam hesapları durum kartları | ✓ Karşılandı | `SteamAccountsStatus.tsx` grid kart düzeni (sm:2col / lg:3col), her kart hesap adı + status icon + tonlu badge. Status enum 4 değer (ACTIVE/RESTRICTED/BANNED/OFFLINE) 06 §2.15 birebir. Kart `<Link>` → `/admin/steam-accounts` (T103). |
| 4 | Kısıtlı/banned bot uyarısı | ✓ Karşılandı | İki kanal: (1) Per-card kırmızı border (`border-red-300`) RESTRICTED+BANNED için (OFFLINE gri kalır, geçici çevrimdışı uyarı seviyesinde değil); (2) Header altında `role="alert"` kırmızı banner `degraded.length > 0` koşuluyla, lokalize `t("warning", { count })` mesajı. AD1 response'unda `warningMessage` field'ı yok (AD10'a özel) — banner client-side türetilir, kabul kriteri metni "kısıtlı/banned" iki sınıfa filtrelenir. |
| 5 | GET /admin/dashboard çağrısı | ✓ Karşılandı | `useAdminDashboard()` → `getAdminDashboard()` → `apiClient<AdminDashboardResponse>("/admin/dashboard")`. Backend `AdminController.GetDashboard` policy `AdminAccess` + rate-limit `admin-read` — 401/403 isError path'inde `loadError` mesajı. |

### Doğrulama Kontrol Listesi (Self-check)

- [x] **04 §8.1 tüm bileşenler var mı?** — Admin Header (AdminShell layout T98 ✓), Sol Menü (AdminSidebar T85 ✓), Özet Kartları 4 adet ✓, Steam Hesapları Durumu ✓, Son Flag'lenmiş İşlemler ✓, Tıklanabilir cards (5 kart + Steam + Recent rows) ✓, "Tümünü Gör" → S13 ✓, Steam kartları → S18 ✓.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit/Integration | — | Plan "Test beklentisi: Yok" |
| `npx tsc --noEmit` | ✓ | Exit 0 (3.5s) |
| `npx eslint <T99 files>` | ✓ | Exit 0, 0 problem |
| `prettier --check <T99 files>` | ✓ | All matched files use Prettier code style (auto-fix sonrası) |
| `npm run build` (next build) | ✓ | Compiled successfully, 26 route, /admin/dashboard ƒ Dynamic |
| 4-locale parity | ✓ | en=1055 tr=1055 es=1055 zh=1055 (önceki 1009 → T99 sonrası 1055, +46 satır 4 dilde) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS bağımsız 2026-05-24 |
| Bulgu sayısı | 0 S-bulgu (S1/S2/S3 yok), 1 minor terminoloji nit |
| Düzeltme gerekli mi | Hayır (nit isteğe bağlı) |

### Validator Bağımsız Kontroller

| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Working tree clean | ✓ | `git status --short` boş |
| 2 | Main CI startup ardışık 3 | ✓ | `26369407170` + `26369407194` (T98 #146) + `26362494599` (T97 #145) — hepsi success |
| 3 | Repo memory drift | ✓ | `.claude/memory/MEMORY.md` T99 satırı 10 ve 229'da mevcut |
| 4 | Backend AdminT63 regresyon | ✓ 21/21 PASS | `dotnet test --filter AdminT63` 6.5s, `Dashboard_Anonymous_Returns401` ✓ |
| 5 | Frontend `tsc --noEmit` | ✓ Exit 0 | T99 dosyaları tip-temiz |
| 6 | Frontend `eslint <T99 files>` | ✓ Exit 0 | 7 dosya 0 problem |
| 7 | Frontend `prettier --check <T99 files>` | ✓ Clean | "All matched files use Prettier code style!" |
| 8 | `next build` | ✓ Compiled successfully (3.1s) | `/admin/dashboard` ƒ Dynamic route |
| 9 | 4-locale leaf parity (strict) | ✓ 665×4 | tr/en/es/zh = 665 anahtar, 0 missing/extra |
| 10 | adminDashboard namespace anahtar sayısı | ✓ 31 anahtar | summary 4 + steamAccounts 10 + recentFlags 8 + flagType 5 + flagStatus 3 + title 1 |
| 11 | Secret leak scan | ✓ Temiz | `process.env`/API_KEY/SECRET/password match yok T99 dosyalarında |
| 12 | ResponsiveTable T98 tüketici (K2 kapanışı) | ✓ | `RecentFlagsTable` `ResponsiveTable<AdminDashboardRecentFlag>` `<dl>`/`<dt>`/`<dd>` mobil card list 04 §9.4 |
| 13 | Task branch CI HEAD | ✓ 9/9 SUCCESS | run [`26371274406`](https://github.com/turkerurganci/Skinora/actions/runs/26371274406) commit `1417f2e` |
| 14 | PR mergeable | ✓ MERGEABLE | PR #147 OPEN, 9/9 check success, Guard skipped |

### Yapım Raporu Karşılaştırması

- **5/5 kabul kriteri eşit verdict** — bağımsız kontrol ile yapım raporu sonuçları örtüşüyor.
- **Test sonuçları eşit** — `next build` PASS, `tsc/eslint/prettier` clean, leaf parity 4×665.
- **CI run ID'leri uyumlu** — Rapor `26370766445` (kod) + `26370958533` (docs flaky re-run); validator HEAD'i `26371274406` ek olarak gözledi (ikinci docs commit'i).
- **Minor nit (M1):** Rapor "Leaf parity: 1055/1055/1055/1055" yazıyor — bu **JSON dosya satır sayısı** (line count), leaf sayısı değil. Strict leaf count = 665/lokal. Parity iddiası kendisi (4 dosya eşit) doğru, yalnız etiket terminolojisi. Bulgu değil, ileride raporlarda "line parity" ya da "leaf parity" net ayrımı istenirse düzeltilebilir.
- **Uyuşmazlık yok** — Tüm K1–K7 forward-deferred kalemler validate edilen kod tabanıyla tutarlı (route stub'lar var, query param hot-link mantıklı, AD1 sözleşmesi backend ile birebir).

## Altyapı Değişiklikleri

- Migration: Yok (read-only sayfa).
- Config/env değişikliği: Yok.
- Docker değişikliği: Yok.

## Commit & PR

- Branch: `task/T99-admin-dashboard`
- Commit: `774525d` — T99: Admin Dashboard (S12)
- PR: [#147](https://github.com/turkerurganci/Skinora/pull/147)
- CI: ✓ PASS — run [`26370766445`](https://github.com/turkerurganci/Skinora/actions/runs/26370766445) (kod commit `774525d`) **9/9 SUCCESS** + Guard skipped. Docs commit `b7a25d6` follow-up run [`26370958533`](https://github.com/turkerurganci/Skinora/actions/runs/26370958533) ilk geçişte Integration test "Test Run Successful 395/395 Passed" yazmasına rağmen Hangfire InMemory `Dispatcher.ThrowObjectDisposedException` shutdown race nedeniyle process exit 1 (flaky; T20+ döneminden bilinen pattern; bu commit'te kod path'i değişmedi). `gh run rerun --failed` ile re-run → 9/9 SUCCESS, flaky doğrulandı.

## Known Limitations / Follow-up

- **K1 — Link target query param honor T100/T101 forward:** Summary card linkleri `?tab=active`, `?status=PENDING`, `?range=daily`, `?range=weekly` query param taşır; T100 (S13 flag kuyruğu) ve T101 (S15 admin tx listesi) sayfalar bu param'ları okuyup filtre uygulayacak. Şu an target sayfalar T13 stub.
- **K2 — `/admin/steam-accounts` route henüz yok:** AdminSidebar `MENU` array'inde path mevcut ama `app/[locale]/admin/steam-accounts/page.tsx` yok — T103 (S18 Admin Steam) forward. Şu an link tıklanırsa 404 — kabul: T103 route oluşturduğunda otomatik canlı.
- **K3 — `/admin/flags/{id}` flag detay route henüz yok:** RecentFlagsTable ID hücresinden tıklanan link → 404 (T100 S14 forward).
- **K4 — Frontend test runner yok:** F5 başlangıcından beri frontend Vitest/Playwright kurulmadı (T84+ task'ları "Test beklentisi: Yok"). T99 görsel doğrulama için lokal `npm run dev` + manuel test yapılmadı (sandbox env), build + tsc + eslint + prettier 4-eşli statik kontrol; backend kontrat değişmediği için runtime regresyon riski minimal.
- **K5 — Admin permission frontend guard yok:** Backend `AuthPolicies.AdminAccess` policy enforce eder; admin olmayan kullanıcı sayfayı açarsa GET /admin/dashboard 401/403 döner → `isError=true` → "Could not load" mesajı. Admin değil kullanıcının URL'i bilse bile sayfayı açma riski (sidebar yalnız admin'lere gösterilir T85 sonrası), client-side guard T-future ek bir admin auth task'ında ele alınmalı.
- **K6 — `warningMessage` AD1 response'unda yok:** AD10 endpoint'inde mevcut, AD1'de yok. Client-side `degraded = accounts.filter(...)` türetimi yapıldı; sunucu-driven mesaj istenirse AD1 sözleşmesi T-future enhancement.
- **K7 — Pre-existing prettier drift (T80 K7 havuzu):** `npm run format:check` global olarak ~152 file drift gösteriyor (T98 raporunda da disclosed). T99 yeni 8 dosyası clean (`prettier --check` PASS sonrası).

## Notlar

- **Working tree:** 1 dosya (`.claude/settings.json` PowerShell permission additive) — kullanıcı kararı `git restore` (discard).
- **Main CI startup ardışık 3:** [`26369407170`](https://github.com/turkerurganci/Skinora/actions/runs/26369407170) (T98 #146) + [`26369407194`](https://github.com/turkerurganci/Skinora/actions/runs/26369407194) (T98 #146) + [`26362494599`](https://github.com/turkerurganci/Skinora/actions/runs/26362494599) (T97 #145) hepsi success ✓.
- **Dış varsayımlar (doğrulanmış):**
  - AD1 backend endpoint mevcut → `backend/src/Skinora.API/Controllers/AdminController.cs:76` `GetDashboard()` + `Services/AdminDashboardDtos.cs` + `Services/AdminDashboardService.cs` (T63 PR #100 squash `e782e53`). Sözleşme alanları: `summaryCards.{activeTransactions,pendingFlags,dailyCompleted,weeklyCompleted}` int, `steamAccounts[]` `AdminSteamAccountDto`, `recentFlags[]` (server limit 5).
  - JSON enum serialization → `JsonStringEnumConverter` Program.cs:268 + AddJsonOptions:340 — enum'lar string olarak gelir ("ACTIVE", "PENDING" vb.); frontend tip union'ları string literal.
  - next-intl 4-locale altyapısı T97 ile kurulu, `lib/utils/format.ts` `formatNumber`/`formatDateTime` `locale` parametresi kabul eder; `LanguageSelector` 4 dilde mevcut.
  - ResponsiveTable T98 ile public — `frontend/src/components/common/index.ts` export ✓.
- **Mimari kararlar:**
  - **Tek `useQuery` + üç child kompozisyon:** AD1 atomik response, her child kendi `isLoading/isError` ile çağrılır → parsiyel network hatası bir bloğu blank etmez (örn. timeout durumunda hepsi loading skeleton). Alternatif: 3 ayrı query (split fetch), backend tarafta servis tek `Task` yapıyor, paralel fetch fayda etmiyor + ek RPC.
  - **Tıklanabilir kartlar yerine ayrı CTA button yok:** 04 §8.1 "Tıklanabilir" şartı kart'ın tamamını `<Link>` yapar; hover state visible feedback. Status badge / icon klavye tab ile odaklanmaz (Link parent fokuslanır).
  - **Banner client-side türetimi:** AD1 response'unda `warningMessage` yok (AD10'da var, ayrı sözleşme); kabul kriteri "Kısıtlı/banned bot uyarısı" client filter ile karşılanır (locale-aware mesaj). Alternatif AD1 enhancement T-future.
  - **Query param hot-link:** Forward devirli T100/T101 sayfalar query param honor etmediği sürece linkler stub sayfaya açılır; ileride filter eklenince tek satır enhancement. Path-only alternatif reddedildi — semantic intent kayıp.
  - **GUID 8-char prefix:** Recent flags tablosu tam GUID render etmek tablo genişliğini bozar (`font-mono` text-xs); admin detay sayfasından tıklayarak inceler. Alternatif: tooltip ile tam ID — T-future enhancement (CopyButton entegrasyonu).
