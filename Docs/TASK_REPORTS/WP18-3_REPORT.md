# WP18-3 — Test/CI sertleştirme: backend test + correctness (son PR)

**Faz:** PRE_F6_PLAN (WP18, 3-PR split'in 3/3'ü) | **Durum:** ⏳ Devam ediyor (validator bekliyor) | **Tarih:** 2026-06-21

---

## Bağlam — WP18 3-PR split (kapanış)

- **PR-1 ✓ merged (#191):** CI altyapısı (prettier + i18n + sidecar audit advisory).
- **PR-2 ✓ merged (#192):** FE Vitest runner + frontend-test job + blockchain audit hardening.
- **PR-3 (bu rapor):** backend test + correctness — 4 bağımsız concern. **WP18'i kapatır.**

## Yapılan İşler (4 concern)

1. **filterbar dateTo (FE correctness):** `frontend/src/lib/utils/date.ts` `toEndOfDay` helper (audit-logs'taki inline end-of-day widening'i tek kaynağa çıkardı) + vitest. 2 düzeltilmemiş admin date-filtresine (transactions, flags) uygulandı + audit-logs refactor edildi. **Yalnız client-side** (backend `CreatedAt <= dateTo` exact-instant inclusive). Edit'ler query-useMemo gövdesine anchor'landı — dep array'ler + URL write-back dokunulmadı.
2. **SqlLikeEscaper + no-direct-INSERT arch test (backend correctness + güvenlik):** kanonik bracket-wrapping LIKE escaper `AdminTransactionQueryService` private'ından `Skinora.Shared.Persistence.SqlLikeEscaper`'a çıkarıldı + **3 escape'siz `EF.Functions.Like` call-site'ı** (AdminUserService, AuditLogQueryService, AdminSanctionsService) düzeltildi → LIKE-wildcard injection (`%`/`_`/`[` artık literal). Yeni `NoRawSqlConventionTests`: backend/src üzerinde source-text scan, `ExecuteSqlRaw`/`FromSqlRaw`/* yasaklar (`AppDbContext.EnforceAppendOnly` bypass'ı önler) — **NetArchTest yok** (owner kararı), scanned-count>0 guard'lı, obj/bin/Designer + backend/tests hariç.
3. **notification truncation guard (backend correctness):** `Skinora.Shared.Notifications.BoldHeaderMessageComposer` — bold-header envelope'unu kanal limitinde (Discord 2000 / Telegram 4096) **raw-truncate-then-escape** ile kurar: escape pair asla bölünmez (dangling trailing backslash → Telegram 400 → preference auto-disable engellenir), bold marker'lar korunur. İki handler `FormatMessage` composer'a delege eder.
4. **AdminWallets endpoint test + suspend isolation (test-infra):** `AdminWalletsEndpointTests` (HTTP-boundary: 401/403-wrong-permission/200-envelope/INVALID_TOKEN-pre-service/outcome→status 400-422-422-502, IHotWalletService stub). `AdminUserSuspensionEndpointTests` +2 isolation fact (VIEW_FLAGS ama MANAGE_FLAGS değil → 403, suspend + un-suspend) **ayrı** 4-arg token helper'la (13 role-only call-site dokunulmadı). test-infra'nın diğer kalemleri (TestContainers/Redis/migration-verify) zaten kapsanmış → no-op.

## Etkilenen Modüller / Dosyalar

- **YENİ:** `frontend/src/lib/utils/date.ts` + `date.test.ts` · `Skinora.Shared/Persistence/SqlLikeEscaper.cs` · `Skinora.Shared/Notifications/BoldHeaderMessageComposer.cs` · `Skinora.Shared.Tests` (SqlLikeEscaperTests, NoRawSqlConventionTests, BoldHeaderMessageComposerTests) · `Skinora.API.Tests/Integration/AdminWalletsEndpointTests.cs`
- **EDIT (prod):** 3 admin page (FE) · AdminTransactionQueryService/AdminUserService/AuditLogQueryService/AdminSanctionsService (escape) · Discord/Telegram NotificationChannelHandler (FormatMessage)
- **EDIT (test):** MarkdownV2/Discord EscaperTests (negatif-parity) · Discord/Telegram NotificationChannelHandlerTests (over-length fact) · AdminUserSuspensionEndpointTests (+2 isolation)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | filterbar dateTo 2 ekranda end-of-day, helper paylaşılan, client-side | ✓ | vitest 28/28; query-useMemo'ya uygulandı, dep/write-back dokunulmadı |
| 2 | LIKE-escape 4 call-site'ta, helper Shared'da, behavior-preserving | ✓ | Shared 405/405; Platform AuditLog 69/69 + Admin 22/22 + API search 61/61 regresyonsuz |
| 3 | no-direct-INSERT arch test, NetArchTest yok, vacuous değil | ✓ | source-scan, scanned-count>0 guard, backend/src-only; arch 1/1 |
| 4 | truncation guard ≤ limit + escape-pair bölünmez + bold korunur | ✓ | composer unit 5 + handler over-length 2/2; raw-truncate-then-escape |
| 5 | AdminWallets endpoint testi (auth/mapping/envelope) + suspend isolation | ✓ | API 25/25; ayrı helper, 13 call-site dokunulmadı |
| 6 | Migration yok, yeni dependency yok | ✓ | 0 Migrations/*.cs; helper'lar mevcut paketlerle |

## Test Sonuçları (lokal)

| Tür | Sonuç | Detay |
|---|---|---|
| dotnet format verify | ✓ exit 0 | CI lint gate |
| Release build | ✓ 0W/0E | |
| Shared.Tests | ✓ 405/405 | escaper/composer/arch/sqllike/enum |
| Platform.Tests (AuditLog) | ✓ 69/69 | escaping regresyonsuz |
| API.Tests (Admin*/Sanctions/Wallets/suspend) | ✓ 86/86 | 61 search + 25 yeni |
| Admin.Tests | ✓ 22/22 | AdminUserService escaping |
| Notifications handler over-length | ✓ 2/2 | SQL-tier |
| FE vitest / tsc / eslint / format | ✓ | 28/28 · 0 · 0 · clean |

Tam backend suite + Notifications integration → **CI authoritative**.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor |
| Yapım-içi adversarial review | 4-concern workflow (filterbar/like-escape+arch/truncation/endpoint), refute-default → **filterbar ✓ clean · like-escape+arch ✓ clean · endpoint ✓ clean · truncation 1 minor** (composer "≤ maxLength" invariant'ı `maxLength < overhead` dejenere durumunda — **production'da erişilemez**, call-site'lar 2000/4096 hardcode); **0 onaylı blocking/major**. Minor, composer XML-doc precondition'ıyla kapatıldı. (arch test non-vacuity ajan tarafından throwaway `ExecuteSqlRaw` probe ile kanıtlandı.) |

## Altyapı Değişiklikleri

- Migration: **Yok** (4 concern'in hiçbiri şema/entity değiştirmez)
- Yeni bağımlılık: **Yok** (NetArchTest dahil; arch test pure source-scan)
- Config/env: Yok · Docker: Yok

## Commit & PR

- Branch: `task/WP18-3-backend-tests-correctness`
- Commits: filterbar · SqlLikeEscaper+arch · truncation guard · AdminWallets/suspend · (+docs)
- PR: #TBD
- CI: ⏳ bekleniyor

## Notlar

- Adım -1 temiz; Adım 0 main son-3 success; PR-2 #192 merged.
- **Planlama:** 4-concern understand workflow (4 plan + critic) → exact harness/signature haritası; critic'in 2 kritik düzeltmesi uygulandı (suspend ayrı-helper / filterbar context-anchored edit).
- **LIKE-escape davranış değişimi:** literal `%`/`_` artık wildcard değil (correctness fix); wildcard-as-wildcard assert eden test yok (regresyonsuz).

## Known Limitations / Follow-up

- `MANAGE_WALLETS` ayrı permission (şu an MANAGE_SETTINGS reuse) → T-future (07 §9.11 + 04 §8.8 kontrat değişimi).
- WP18 tamamlanır → **F6 (Uçtan Uca Doğrulama, T107–T114)** başlayabilir.
