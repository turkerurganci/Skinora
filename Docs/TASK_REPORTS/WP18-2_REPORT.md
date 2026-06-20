# WP18-2 — Test/CI sertleştirme: FE Vitest runner + blockchain ethers override

**Faz:** PRE_F6_PLAN (WP18, 3-PR split'in 2/3'ü) | **Durum:** ⏳ Devam ediyor (validator bekliyor) | **Tarih:** 2026-06-21

---

## Bağlam — WP18 3-PR split

- **PR-1 ✓ merged (#191):** CI altyapısı (prettier-drift + i18n-lint-ci + sidecar npm-audit advisory).
- **PR-2 (bu rapor):** FE Vitest runner + seed (B) + yeni `frontend-test` CI job; blockchain `ethers 6.17` override + BC audit blocking@critical.
- **PR-3:** backend test + correctness (AdminWallets + suspend-isolation + filterbar `toEndOfDay` + template truncation + like-escape helper + no-direct-INSERT arch test).

## Yapılan İşler (PR-2)

1. **FE Vitest runner (Commit 1):** `vitest 4.1.9` + `@vitejs/plugin-react` + `jsdom` + `@testing-library/react`/`jest-dom` devDeps; `vitest.config.ts` (jsdom env + `@/`→`./src` alias, vitest tsconfig path'leri otomatik okumaz) + `vitest.setup.ts` (jest-dom matchers); `test`/`test:watch` npm script'leri. **Spike önce:** `format.test.ts` izole çalıştırıldı → `@/i18n/routing`→next-intl transitive import'unun jsdom altında çalıştığı kanıtlandı (kritik risk M6 elendi).
2. **Seed B (Commit 1):** **25 test / 7 dosya** — pure-util: `format` (stablecoin locale-invariant, percent, relativeTime injectable-now, locale fallback), `tableSort` (parse/next toggle), `blockchain` (tronscanTxUrl), `cn`, `roles` (isAdminRole), `untranslatable` (isUntranslatable) + **1 render testi** `StatusBadge` (`NextIntlClientProvider` ile jsdom+RTL+jest-dom+next-intl hattını uçtan uca kanıtlar). Geniş component/hook kapsamı F6'ya ertelendi.
3. **Blockchain non-breaking override'lar (Commit 2 + review-fix):** `sidecar-blockchain/package.json` npm `overrides` → **`ethers 6.17.0`→`ws 8.21.0`** (GHSA-58qx ws uninitialized-memory + DoS) + **`form-data 4.0.6`** (GHSA-hmw2 CRLF injection; yapım-içi review bulgusu — aşağı bkz.). İkisi de tronweb 5.3.5 altında **breaking olmayan** (tronweb 6.x bump'a gerek yok). tsc 0 + vitest 161/161 non-breaking doğrulandı (`ethers 6.17.0` HdWalletService log'unda aktif). Kalan prod high = **axios + lodash** (ikisi de tronweb altında, yalnız breaking tronweb 6.x ile düzelir → accept-risk).
4. **CI `frontend-test` job + BC audit blocking (Commit 3):** yeni `frontend-test` job FE vitest + 2 sidecar vitest suite'ini çalıştırır (her step kendi paths-filter output'uyla gated; `changes` job'a `frontend`/`sidecar-steam`/`sidecar-blockchain` output'ları eklendi), `ci-gate.needs`'e dahil (blocking; doc-only PR'da skipped, fail değil). BC `npm audit` advisory→**blocking@--audit-level=critical** (override sonrası 0 prod critical). Steam audit değişmedi (advisory@high).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | FE Vitest runner kurulu, seed B testleri geçer | ✓ | `vitest run` 25/25; `tsc --noEmit` 0; `next build` 0 (test dosyaları tsconfig'de typecheck) |
| 2 | Render testi jsdom+RTL+jest-dom hattını kanıtlar | ✓ | `StatusBadge.test.tsx` `toBeInTheDocument` ✓ + `next-intl` provider |
| 3 | Yeni `frontend-test` CI job FE+sidecar vitest çalıştırır, blocking | ✓ | ci.yml `frontend-test` job, ci-gate.needs'te, per-area gated |
| 4 | blockchain non-breaking override'lar fixable high'ları kapatır | ✓ | `npm ls`→ws 8.21.0 + form-data 4.0.6 + ethers 6.17.0; tsc 0 + vitest 161/161; prod high 4→3 (residual axios+lodash) |
| 5 | BC audit blocking@critical bugün yeşil | ✓ | `npm audit --omit=dev --audit-level=critical` exit 0 |
| 6 | steam audit advisory korunur (kalıcı non-blocking) | ✓ | ci.yml steam step `continue-on-error: true` |

## Test Sonuçları (lokal, CI-eşdeğeri)

| Tür | Sonuç | Detay |
|---|---|---|
| FE vitest | ✓ 25/25 | 7 dosya (6 pure-util + 1 render) |
| FE tsc / next build | ✓ | exit 0 (jest-dom matcher tipleri çözüldü) |
| FE eslint / format:check (LF) / i18n:check | ✓ | exit 0 |
| steam tsc / vitest / format | ✓ | tsc 0 · 158/158 · format clean |
| BC tsc / vitest / format | ✓ | tsc 0 · 161/161 · format clean |
| BC audit (critical, blocking) | ✓ exit 0 | 0 prod critical |
| steam audit (high, advisory) | exit 1 | continue-on-error → CI'yı kırmaz |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor |
| Yapım-içi adversarial review | 3-boyut workflow (FE-test/CI-wiring/BC-override), refute-default → FE-test ✓ clean · CI-wiring ✓ clean · BC-override **1 major** (form-data accuracy) onaylandı + **bu PR'da kapatıldı** (form-data 4.0.6 override + doc düzeltme) |

## Altyapı Değişiklikleri

- Migration: Yok
- Yeni bağımlılık: **FE** vitest stack (6 devDep, dev-only — prod bundle'a girmez) · **BC** ethers override (yeni paket değil, mevcut transitive'i pinler)
- Config/env: Yok
- Docker: Yok

## Commit & PR

- Branch: `task/WP18-2-fe-test-runner`
- Commits: FE Vitest+seed · BC ethers override · CI frontend-test+BC-critical · form-data override+düzeltme · (+docs)
- PR: [#192](https://github.com/turkerurganci/Skinora/pull/192)
- CI: ✓ PASS — run [`27886435525`](https://github.com/turkerurganci/Skinora/actions/runs/27886435525) (`5d9e950`) **14/14 job success** (yeni `3b. JS test (vitest)` job'u FE+2 sidecar vitest çalıştı; BC critical-blocking audit Lint'te yeşil). İlk run `27886274863` npm-skew ile fail'di (yukarı bkz.).

## Notlar

- **Working tree (Adım -1):** temiz · **Main CI (Adım 0):** son 3 success · **PR-1 #191 merged** (format gate main'de canlı → PR-2'nin yeni dosyaları doğru formatla indi).
- **Dış varsayım kırılması (Adım 4):** ethers override `ws` high'ını kapattı ama tronweb-altı high'lar kaldı — npm'in fix'i breaking `tronweb@6.0.2`. Owner'a sunuldu (AskUserQuestion) → karar: **BC audit blocking@critical** (residual high'lar accept-risk, tronweb 6.x follow-up). Pratik risk düşük (`lodash._template` güvenilmez girdiyle çağrılmıyor).
- **Yapım-içi adversarial review bulgusu (1 major, onaylandı + bu PR'da kapatıldı):** ilk accept-risk gerekçem residual high'ları "yalnız lodash, breaking-only" diye yanlış tanımlamıştı; review `form-data@4.0.5`'in de high (CRLF injection) **ama non-breaking fix'i (4.0.6) olduğunu** kanıtladı → `form-data 4.0.6` override eklendi (high 4→3, tronweb 5.3.5 korundu), gerekçe gerçek residual'a (axios+lodash) düzeltildi.
- **CRLF:** yeni test dosyaları prettier-clean yazıldı (Write LF); `--end-of-line=auto` CI-eşdeğeri temiz.
- **npm sürüm skew dersi (CI-fix):** ilk PR-2 CI run'ı (`27886274863`) **Frontend lint `npm ci`**'da fail'di — lokal **npm 11.6.2** (Node 24) ile üretilen lockfile, CI'nin **npm 10.x** (Node 20) `npm ci`'ı tarafından reddedildi (`@emnapi/*`/`@swc/helpers` missing/invalid). Frontend + sidecar-blockchain lockfile'ları **`npx npm@10 install`** ile yeniden üretildi; `npm@10 ci` exit 0 ile doğrulandı (CI'nin tam yaptığı). Override'lar (ethers/ws/form-data) + BC critical audit korundu. Kalıcı çözüm (CI Node 20→24 npm-11 paritesi veya `engines`/`.nvmrc`) → DEFERRED_BACKLOG follow-up.

## Known Limitations / Follow-up

- BC residual high (axios + lodash, tronweb-altı) → **tronweb 5→6 major bump** (breaking, smoke test gerekir) ayrı scoped task.
- FE geniş component/hook test kapsamı → F6.
- `.gitattributes eol=lf` kalıcı CRLF çözümü → değerlendirilecek.
