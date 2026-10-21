# T160a — Next.js güvenlik yükseltmesi + CI frontend audit

**Faz:** F9 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-10-21

---

## Yapılan İşler
- `next` ve `eslint-config-next` **16.2.3 → 16.3.8** (tam sürüm sabit, `--save-exact`; önceki tanım da sabitti).
- `npm audit fix` (force **değil**) ile lock dosyası yenilendi; transitive high'lar semver aralığı içinde kapandı: `postcss` 8.5.23 (next altında) / 8.5.31 (vite altında), `sharp` 0.34.5 → 0.35.5, `ws` 7.5.10 → 7.5.13 (`@microsoft/signalr` altında), `source-map-js` 1.2.1 → 1.2.2, `nanoid`; moderate'ler: `baseline-browser-mapping`, `next-intl` 4.9.1 → 4.14.9 (`icu-minify`). `package.json`'da `next-intl` aralığı (`^4.9.0`) değişmedi.
- **Sonuç:** `npm audit --omit=dev` → `found 0 vulnerabilities` (önce: 9 — 1 critical, 5 high, 2 moderate, 1 low).
- CI `1. Lint` job'una yeni adım: **`Frontend npm audit (blocking — prod highs)`** → `npm audit --omit=dev --audit-level=high` (continue-on-error yok; sidecar-ethereum ile aynı sıkılık). Gerekçe adımın yorumunda.
- Next sürüm geçişi kırılma kontrolü (16.3 sürüm notu https://nextjs.org/blog/next-16-3 okundu: kırıcı değişiklik listelenmiyor; yeni özellikler opt-in):
  - **config:** `next.config.ts` (`output: "standalone"` + next-intl eklentisi) değişiklik gerektirmedi; build ✓.
  - **middleware/proxy:** `src/middleware.ts` → **`src/proxy.ts`** (içerik aynı). Next 16'da `middleware` dosya adı deprecated; 16.2.3'te de uyarı veriyordu (T13 raporu K-notu "next-intl güncellemesiyle çözülecek"). next-intl belgesi (https://next-intl.dev/docs/routing/middleware) Next 16 için `proxy.ts` + aynı `createMiddleware(routing)` default export'unu gösteriyor. Build çıktısında `ƒ Proxy (Middleware)` görünüyor, deprecation uyarısı kalktı.
  - **API:** kodda kullanılan Next API'leri yalnız `next/link` (33), `next/navigation` (29), `next/server` (1) — tsc 0 hata.
  - **lint:** `eslint-plugin-next` 16.3 yeni kural getirdi: `no-location-assign-relative-destination`. Tek isabet `src/lib/api/client.ts` `redirectToLogin()` — React dışında, oturum silindikten sonra çağrılan bilinçli tam sayfa yönlendirme (router yok; tam yeniden yükleme önbellek/store'u temizler). Davranış korunup satıra gerekçeli `eslint-disable-next-line` kondu.
  - **Node:** next 16.3.8 `engines.node >=20.9.0`; Dockerfile `node:20-slim`, CI `NODE_VERSION` aynı → uyumlu.
- `.claude/CONTEXT.md` dosya haritasında `frontend/src/middleware.ts` → `frontend/src/proxy.ts`.
- `DEFERRED_BACKLOG`: `FrontendNextAdvisories` 🔴 → ✅ (Öne Çıkanlar + §7); yeni ⚪ `FrontendDevToolingBracesAdvisory` (§7); durum satırı **51 aktif / 157 çözülmüş, açık 🔴 yok**.
- **Devir kaydı:** `STATUS_CHANGELOG.md`'ye merge teyitleri: T160 (#361) main `5ad1e6b7` → CI `39545306202` ✓, Docker Publish `39545306158` ✓; #362 main `fc3e8840` → CI `39546534823` ✓, Docker Publish `39546534805` ✓ (`gh run list --branch main` ile okundu).

## Etkilenen Modüller / Dosyalar
- `frontend/package.json`, `frontend/package-lock.json`
- `frontend/src/middleware.ts` → `frontend/src/proxy.ts` (yeniden adlandırma, içerik aynı)
- `frontend/src/lib/api/client.ts` (yalnız yorum + eslint-disable satırı)
- `.github/workflows/ci.yml` (lint job'una frontend audit adımı)
- `.claude/CONTEXT.md`, `.claude/memory/MEMORY.md`
- `Docs/DEFERRED_BACKLOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `Docs/TASK_REPORTS/T160a_REPORT.md`

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `next` danışmasız sürüme yükseltildi (`npm audit --omit=dev` critical/high 0, transitive dahil) | ✓ | `frontend/` içinde `npm audit --omit=dev` → `found 0 vulnerabilities`; `npm ls next` → `next@16.3.8`. Eski aralık: `next 9.3.4-canary.0 - 16.3.7` critical. Override gerekmedi (non-force `npm audit fix` yetti). |
| 2 | CI'da frontend audit adımı var ve critical/high'da kırmızı | ✓ | `ci.yml` lint job: `Frontend npm audit (blocking — prod highs)` = `npm audit --omit=dev --audit-level=high`, continue-on-error yok. Kırmızı olma kanıtı: main'deki eski `package.json` + `package-lock.json` scratch dizinine alınıp aynı komut koşuldu → **çıkış 1** (`9 vulnerabilities (1 low, 2 moderate, 5 high, 1 critical)`); yeni lock → **çıkış 0**. Dal CI'ında (run `39563408752`) adım `found 0 vulnerabilities` ile yeşil. |
| 3 | frontend build + lint + i18n + vitest + E2E yeşil | ✓ | Lokal: `npm run build` ✓ (Next.js 16.3.8 Turbopack, uyarısız); `npm run lint` 0 hata 0 uyarı; `npx tsc --noEmit` 0; `npm run i18n:check` → `4 locales, 1578 keys each, identical key sets` (20 advisory uyarı, öncekiyle aynı); `npm test` → **41 dosya, 391/391**. E2E: CI run `39563408752` 11/11 success. |
| 4 | DEFERRED_BACKLOG `FrontendNextAdvisories` kapandı, sayımlar güncel | ✓ | Satır Öne Çıkanlar + §7'de ✅ (dosyanın kendi kuralı: çözülen satır yerinde ✅ olur); sayaç `awk` ile: önce `51/156`, sonra `51/157` (−1 🔴, +1 ⚪). |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Frontend vitest | ✓ 391/391 | `cd frontend && npm test` → `Test Files 41 passed (41)`, `Tests 391 passed (391)` |
| Frontend build | ✓ | `npm run build` → `▲ Next.js 16.3.8 (Turbopack)`, `ƒ Proxy (Middleware)`, deprecation uyarısı yok |
| Lint / tsc / i18n | ✓ | eslint 0/0 · tsc 0 · i18n parity 4×1578 |
| npm audit (prod) | ✓ 0 | `npm audit --omit=dev --audit-level=high` çıkış 0 |
| E2E (11 suite) | ✓ 11/11 | CI run `39563408752`, 11 `E2E … (advisory)` job'unun hepsi `success` |

Not: lokal `npm run format:check` 290 dosyada uyarı veriyor — `core.autocrlf` CRLF artefaktı (bilinen; CI LF yetkili). Değişen `client.ts` için `prettier --check --end-of-line auto` → temiz.

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (yönetici düzeni — ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: Yok
- Docker değişikliği: Yok (Dockerfile aynı; `npm ci` yeni lock'u kurar)
- CI: lint job'una bir adım (frontend npm audit, bloke edici)

## Commit & PR
- Branch: `task/T160a-nextjs-guvenlik`
- Commit: `f2db4394` — T160a: next 16.2.3 -> 16.3.8, middleware -> proxy, CI frontend npm audit (prod high)
- Commit: `f118bd83` — T160a: rapor, status, backlog, merge teyitleri
- PR: #363
- CI: ✓ PASS — run `39563408752` (HEAD `f118bd83`), `success`; `Frontend npm audit (blocking — prod highs)` → `found 0 vulnerabilities`; frontend build + Docker build (frontend) ✓; **E2E 11/11 success** (happy-path UI dahil). İlk run `39563063416` (`f2db4394`) concurrency ile iptal edildi (yeni push).

## Known Limitations / Follow-up
- **Dev-only high (`braces`)**: `npm audit` (dev dahil) 5 high gösteriyor, hepsi tek kök `braces@3.0.3` (GHSA-vfj7-8cjw-p6xm, etkilenen aralık `*`, npm'de düzeltme yok), `eslint-config-next@16.3.8` → `@next/eslint-plugin-next` → `fast-glob@3.3.1` → `micromatch` altında. Lint aracı, üretim imajına girmez; CI adımı `--omit=dev` ile bilerek kapsam dışı. Backlog ⚪ `FrontendDevToolingBracesAdvisory`.
- **Audit adımı ağa bağlı:** npm danışma veritabanına yeni bir high düştüğünde ilgisiz bir PR da kırmızıya döner. Bu bilinçli (canlı öncesi istenen sinyal); düzeltme o an `npm audit fix` ya da gerekçeli karar.
- **16.4.x'e geçilmedi:** npm'in önerdiği `next@16.4.2` 2026-10-15'te yayımlandı (6 günlük); danışmaların tamamı 16.3.8'de kapalı. 16.4'e geçiş ayrı bir iş.

## Notlar
- **Working tree:** temiz (worktree yönetici tarafından güncel `origin/main`'den açıldı, `git status` boş).
- **Main CI startup check:** son run'lar hepsi `success` — CI `39546534823`, Docker Publish `39546534805` (#362); CI `39545306202`, Docker Publish `39545306158` (#361).
- **Bağımlılık:** T160 ✓ (F8 Gate Check PASS, tag `phase/F8-pass`).
- **Dış varsayımlar:**
  - `next@16.3.8` var ve danışmasız → `npm view next time` (16.3.8: 2026-09-30); `npm audit` etkilenen aralık `9.3.4-canary.0 - 16.3.7`; kurulumdan sonra prod audit 0.
  - `eslint-config-next@16.3.8` var → `npm view eslint-config-next@16.3.8 version` = `16.3.8`.
  - Node uyumu → `npm view next@16.3.8 engines` = `>=20.9.0`; Dockerfile `node:20-slim`.
  - next-intl Next 16 `proxy.ts` desteği → next-intl routing/middleware belgesi; build `ƒ Proxy (Middleware)` ve 391 vitest + E2E UI bacağı.
  - Sürüm notu → https://nextjs.org/blog/next-16-3 (kırıcı değişiklik yok).
- **Öneriyle kaydedilen kararlar (⚠):**
  1. Hedef sürüm **16.3.8**, 16.4.2 değil — tüm danışmaları kapatan en küçük adım, 3 haftalık yama; 16.4.2 6 günlük. (öneriyle kaydedildi — ⚠)
  2. CI eşiği **`--audit-level=high`, bloke edici** (kriter "critical/high'da kırmızı"; sidecar-ethereum paritesi). (öneriyle kaydedildi — ⚠)
  3. `npm audit fix` (non-force) moderate'leri de kapattı → `next-intl` lock'ta 4.9.1 → 4.14.9; testler yeşil. (öneriyle kaydedildi — ⚠)
  4. `middleware.ts` → `proxy.ts` taşındı (deprecation; kriterdeki "middleware/proxy kırılmalarını kontrol et"). (öneriyle kaydedildi — ⚠)
  5. Yeni lint kuralı için davranış değişmedi, satır gerekçeyle susturuldu. (öneriyle kaydedildi — ⚠)
  6. Backlog satırı dosyanın kendi kuralıyla **yerinde ✅** yapıldı (Ek bölümü "non-actionable" içindir); dev-only `braces` için yeni ⚪ satır açıldı. (öneriyle kaydedildi — ⚠)
