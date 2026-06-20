# WP18-1 — Test/CI sertleştirme: CI altyapısı + ucuz gate'ler (prettier + i18n + sidecar audit advisory)

**Faz:** PRE_F6_PLAN (WP18, 3-PR split'in 1/3'ü) | **Durum:** ⏳ Devam ediyor (validator bekliyor) | **Tarih:** 2026-06-20

---

## Bağlam — WP18 3-PR split

WP18 (Test/CI sertleştirme, MVP'nin son iş paketi) 8 alt-kalem içerir ve 3 farklı review moduna ayrılır. Owner kararıyla **3 PR'a bölündü** (her biri ayrı branch + ayrı bağımsız validator):

- **PR-1 (bu rapor):** prettier-drift + i18n-lint-ci + sidecar npm-audit (advisory). CI altyapısı + ucuz gate'ler. İlk iner.
- **PR-2:** FE Vitest runner + seed (B) + yeni `frontend-test` job; blockchain `ethers 6.17` override + BC audit blocking.
- **PR-3:** backend test + correctness (AdminWallets + suspend-isolation testleri + filterbar `toEndOfDay` + template truncation guard + like-escape helper + no-direct-INSERT reflection testi).

## Yapılan İşler (PR-1)

1. **prettier normalizasyonu (Commit A + routes.ts düzeltmesi):** Gerçek LF-drift olan **25 dosya** (FE 13 / steam 7 / BC 5) `prettier --write` ile normalize edildi. Görünen 144-dosya drift'i Windows CRLF working-tree artefaktıydı (autocrlf=true); `git diff --ignore-cr-at-eol` ile gerçek içerik değişikliği 25 olarak doğrulandı. `routes.ts` ayrıca düzeltildi: `prettier --write` CRLF working-tree'de printWidth-sınırındaki bir satırı (çok-baytlı `§` içeren) CI'nin LF checkout'undan farklı sardı → LF içerikten yeniden formatlanarak commit'lenen çıktı `prettier(LF)` ile birebir eşitlendi.
2. **i18n lint (Commit B):** `frontend/scripts/check-i18n.mjs` + `npm run i18n:check`. İki kontrol:
   - **Key-parity (BLOCKING):** 4 locale (en/tr/es/zh) düz anahtar setleri birebir aynı olmalı; eksik/fazla anahtar varsa exit 1. Bugün yeşil (1291 anahtar ×4, identical key-set).
   - **Untranslatable (ADVISORY):** `UNTRANSLATABLE_TERMS` (04 §10.4, `untranslatable.ts`'ten tek-kaynak parse) terimlerinden en değerinde geçenler diğer locale'lerde verbatim kalmalı; ihlaller **uyarı** olarak raporlanır, exit'i etkilemez.
3. **CI lint job gate'leri (Commit C+D):** Koşulsuz çalışan `lint` job'a eklendi:
   - FE + steam + blockchain `format:check` (BLOCKING — backend `dotnet format` paritesi).
   - FE `i18n:check` (BLOCKING parity + ADVISORY untranslatable).
   - Her iki sidecar `npm audit --omit=dev --audit-level=high` (`continue-on-error: true` → asla gate'i kırmaz). Steam kalıcı advisory (owner accept-risk); blockchain PR-2'de override sonrası blocking olacak.

## Etkilenen Modüller / Dosyalar

- `frontend/scripts/check-i18n.mjs` (YENİ) · `frontend/package.json` (+`i18n:check`)
- `.github/workflows/ci.yml` (lint job: +6 step)
- 25 dosya prettier-formatlama (FE 13 + sidecar-steam 7 + sidecar-blockchain 5) — saf format, mantık değişikliği yok
- `Docs/DEFERRED_BACKLOG.md` (prettier/i18n ✅ resolved, sidecar-audit ~kısmi, `i18n-untranslatable-localized` advisory eklendi)

## Kabul Kriterleri Kontrolü (PR-1 alt-kalemleri)

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | prettier `format:check` FE+2 sidecar blocking CI gate'i, repo drift'i fixli | ✓ | `prettier --check --end-of-line=auto` (CI=LF eşdeğeri) 3 alanda exit 0; ci.yml lint job 3 blocking step |
| 2 | i18n key-parity blocking CI gate'i, bugün yeşil | ✓ | `npm run i18n:check` exit 0; "1291 keys each, identical key sets" |
| 3 | i18n untranslatable advisory (exit'i etkilemez) | ✓ | 15 advisory uyarı yazılır, exit 0 |
| 4 | sidecar npm audit advisory CI adımı (steam kalıcı non-blocking) | ✓ | ci.yml 2 step `continue-on-error: true` |
| 5 | Hiçbir yeni gate mevcut yeşil CI'yı kırmaz / doc-PR'ı red-wall'lamaz | ✓ | CI run [`27879240190`](https://github.com/turkerurganci/Skinora/actions/runs/27879240190) **tüm job success** (Lint dahil blocking format:check+i18n geçti; advisory audit job'ı kırmadı; CI Gate success) |

## Test Sonuçları (lokal, CI-eşdeğeri)

| Tür | Sonuç | Detay |
|---|---|---|
| FE format:check (LF) | ✓ | "All matched files use Prettier code style!" |
| steam/BC format:check (LF) | ✓ | exit 0 (her ikisi) |
| FE eslint | ✓ | `npm run lint` exit 0 |
| FE i18n:check | ✓ | exit 0, parity OK + 15 advisory |
| sidecar tsc --noEmit | ✓ | steam + BC exit 0 |
| ci.yml YAML | ✓ | js-yaml parse OK |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor |
| Yapım-içi adversarial review | 3-boyut workflow (CI/i18n/format), refute-default |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env: Yok (yalnız CI workflow + npm script)
- Docker: Yok
- Yeni bağımlılık: Yok (PR-1) — check-i18n.mjs sıfır-dep, Node 20+ built-in

## Commit & PR

- Branch: `task/WP18-1-ci-gates`
- Commits: `4ac19aa` (prettier 25) · `41ae787` (i18n script) · `7c8bcdd` (routes.ts fix) · `96a6706` (ci.yml gates) · (+docs)
- PR: [#191](https://github.com/turkerurganci/Skinora/pull/191)
- CI: ✓ PASS — run [`27879240190`](https://github.com/turkerurganci/Skinora/actions/runs/27879240190) (`ccab9e2`) tüm job success

## Notlar

- **Working tree (Adım -1):** temiz.
- **Main CI (Adım 0):** son 3 run `success` (`27871388334`/`27871388331`/`27859178443`).
- **Dış varsayımlar:** prettier 3.8.1-3.8.2 üç alanda kurulu (çalıştırıldı); `npm audit` lockfile'a karşı offline çalışır; CI Node 20 / dotnet 9.0.x. Doğrulandı.
- **CRLF dersi:** Windows'ta `prettier --write` CRLF working-tree'de width-sınırı satırlarını CI'nin LF'sinden farklı sarabilir → bu PR'da glob LF'ye zorlanıp yeniden formatlandı; commit'lenen içerik `prettier(LF)` ile eşitlendi. Gelecekte `.gitattributes eol=lf` (ts/tsx/json) kalıcı çözüm olur (PR-2/PR-3 değerlendirmesi).
- **15 untranslatable advisory:** owner kararı = çeviriler değiştirilmedi, kural advisory; `DEFERRED_BACKLOG` `i18n-untranslatable-localized` satırında izlenir.

## Known Limitations / Follow-up

- Blockchain `ethers 6.17` override + BC audit blocking → **WP18 PR-2**.
- Untranslatable spec-vs-çeviri uzlaşısı (çeviri düzelt **veya** 04 §10.4 daralt) → follow-up.
- `.gitattributes eol=lf` kalıcı CRLF çözümü → değerlendirilecek.
