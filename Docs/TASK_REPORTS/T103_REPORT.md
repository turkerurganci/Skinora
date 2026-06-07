# T103 — Admin Steam Hesapları (S18)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız validator bekliyor) | **Tarih:** 2026-06-07

---

## Yapılan İşler
- S18 — Platform Steam Hesapları admin sayfası (04 §8.7), mevcut **AD10** `GET /admin/steam-accounts` endpoint'ini (T63) tüketir. **Salt frontend** (proje sahibi onayı 2026-06-07, **Option A**).
- Hesap kartları: Steam ID, durum badge'i, emanet item sayısı, günlük trade kullanımı (x / 200 ToS limiti), son sağlık kontrolü (göreli zaman).
- Üç+bir state: aktif (yeşil), kısıtlı (turuncu — kart vurgulu + uyarı), banned (kırmızı — kart vurgulu + acil uyarı), offline (gri, 06 §2.15 enum ekstrası).
- Uyarı banner'ı: degraded (kısıtlı/banned) hesap varsa **client-side türetilen lokalize** banner + (failover bildirildiğinde) "yeni işlemler yönlendirildi" satırı.
- Kısıtlı/banned kartında emanet item varsa recovery/manuel müdahale notu (02 §15, 03 §11.2a); item **listesi** deferred (AD10 yalnız sayı verir).
- Recovery Queue: 7 spec kolonuyla (İşlem ID, Item, Satıcı/Alıcı, İşlem State, Recovery Durumu, Sorumlu Admin, Admin Notu) **yapısal** render + boş state + deferred not.

## Etkilenen Modüller / Dosyalar
- `frontend/src/lib/api/admin.ts` (M) — `AdminSteamAccountsResponse` + `getAdminSteamAccounts()` (AD10). `AdminSteamAccount` tipi T99'dan mevcuttu.
- `frontend/src/lib/hooks/useAdminSteamAccounts.ts` (yeni) — React Query hook, `["admin","steam-accounts","list"]`, 30s staleTime.
- `frontend/src/lib/utils/format.ts` (M) — `formatRelativeTime` (göreli zaman, `Intl.RelativeTimeFormat`, ≥24h → mutlak fallback, test için `now` injectable).
- `frontend/src/components/admin/SteamAccountsView.tsx` (yeni) — orkestratör (banner + kart grid + recovery panel).
- `frontend/src/components/admin/SteamAccountCard.tsx` (yeni) — 4-state tonlu tek kart.
- `frontend/src/components/admin/RecoveryQueuePanel.tsx` (yeni) — recovery kuyruğu (`RecoveryQueueRow` forward-tip + ResponsiveTable + boş/deferred).
- `frontend/src/components/admin/index.ts` (M) — 3 yeni barrel export.
- `frontend/src/app/[locale]/admin/steam-accounts/page.tsx` (yeni) — route (T99'dan beri 404; nav linki AdminSidebar'da zaten vardı).
- `frontend/src/i18n/messages/{en,tr,es,zh}.json` (M) — `adminSteamAccounts` namespace, 32 leaf/locale.

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Hesap kartları: Steam ID, durum (aktif/kısıtlı/banned), emanet sayısı, günlük trade, son kontrol | ✓ | `SteamAccountCard.tsx` — AD10 alanları: SteamId, status badge, escrowedItemCount, dailyTradeOfferCount/Limit, lastHealthCheck (`formatRelativeTime`). `next build` route emitted. |
| 2 | State'ler: aktif (yeşil), kısıtlı (turuncu + banner + emanet listesi), banned (kırmızı + acil uyarı) | ✓ (~ emanet **listesi** deferred) | `statusTone` 4 tonlu (emerald/amber/red/gray); banner `SteamAccountsView` degraded türetimi; kart-içi kısıtlı/banned uyarısı + emanet **sayı** notu. Emanet item **listesi** AD10'da yok → deferred (Option A, K-not). |
| 3 | Recovery queue: işlem ID, item, taraflar, state, recovery durumu, sorumlu admin, not | ✓ (yapısal) / ⏳ veri deferred | `RecoveryQueuePanel.tsx` 7 kolon ResponsiveTable + boş state. Satır verisi + MANAGE_STEAM_RECOVERY aksiyonları AD10'da yok (T69 deferred) → owner-onaylı Option A. |
| 4 | GET /admin/steam-accounts çağrısı | ✓ | `getAdminSteamAccounts()` → `apiClient("/admin/steam-accounts")`; `useAdminSteamAccounts` hook. |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Unit | — | Test beklentisi "Yok" (AC). Frontend test runner projede yok (F5 plan-onaylı). |
| Integration | — | Yok (salt frontend, backend değişmedi). |
| Type check | ✓ | `npx tsc --noEmit` → 0 hata. |
| Lint | ✓ | `npx eslint <T103 files>` → 0/0. |
| Format | ✓ | `npx prettier --check` → clean (LF). |
| Build | ✓ | `npm run build` → success; `/[locale]/admin/steam-accounts` ƒ Dynamic route emitted. |
| i18n parity | ✓ | 4-locale leaf parity **931×4** (adminSteamAccounts 32×4), 0 missing/extra. |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu (yapım self-check) | ✓ 4/4 kabul (AC2/AC3 emanet-listesi + recovery satır verisi owner-onaylı deferred) |
| Adversarial review (ultracode, yapım-içi) | 6-boyut/11-ajan: 5 ham → 3 onaylandı (düzeltildi) + 2 çürütüldü |
| Bağımsız validator | Bekliyor (ayrı chat) |

## Altyapı Değişiklikleri
- Migration: Yok.
- Config/env değişikliği: Yok.
- Docker değişikliği: Yok.
- Yeni bağımlılık: Yok (next-intl + @tanstack/react-query mevcut).

## Commit & PR
- Branch: `task/T103-admin-steam-accounts`
- Commit: `caacde3` — T103: Admin Steam hesapları (S18) — frontend page (AD10) (+ rapor/status/memory ayrı commit)
- PR: #158
- CI: izleniyor (push sonrası)

## Known Limitations / Follow-up
- **K1 — Recovery Queue veri deferred:** Satır verisi (işlem/item/taraf/state/recovery durumu/sorumlu admin/not) ve MANAGE_STEAM_RECOVERY aksiyonları (Manual Recovery / not / sorumlu admin atama) AD10'da yok; recovery-state domain modeli yok. T69 bot-health/failover pipeline AD10'a bağlanınca (veya adanmış endpoint) dolar. `RecoveryQueueRow` forward-compatible (tip aktif olunca `lib/api/admin.ts`'e taşınır, UI değişmez).
- **K2 — `failoverStatus`/`restrictionReason`/`recoveryTransactionCount` deferred:** AD10 her zaman `NONE`/`null`/`0`. "Yeni işlemler yönlendirildi" banner satırı `failoverStatus==='RESTRICTED_NEW_TXN_DIVERTED'` ile gate'li → bugün gizli ama forward-correct (fabrike edilmedi).
- **K3 — Kısıtlı hesap emanet item listesi deferred:** AD10 yalnız `escrowedItemCount` (sayı) verir; item listesi yok → sayı + recovery uyarısı gösterilir, liste deferred.
- **K4 — Banner Türkçe-sabit `warningMessage` kullanılmadı:** AD10'un server `warningMessage`'ı Türkçe-sabit (`AdminSteamBotQueryService.BuildWarning`) → 4-locale sayfada client-side lokalize banner türetildi (T99 K6 precedent).
- **K5 — Frontend permission guard yok:** Backend `VIEW_STEAM_ACCOUNTS` policy enforce eder (T99 K5 deseni); client-side guard T-future.
- **K6 — Frontend test runner yok:** F5 plan-onaylı; doğrulama tsc/eslint/build/parity ile.

## Notlar
- **Working tree (Adım -1):** Session başı temiz (`git status --short` boş).
- **Main CI startup (Adım 0):** Son 3 main run `success` — `27097121138` (T102 #157) / `27097121144` (T102 #157) / `27092817285` (Fraud fix). HARD STOP yok.
- **Bağımlılık:** T85 (Global layout) ✓ Tamamlandı.
- **Dış varsayım doğrulama (Adım 4):**
  - AD10 `GET /admin/steam-accounts` MEVCUT (T63) — kod okundu (`AdminController.cs:93` + `AdminSteamBotQueryService`). Hesap kartı alanlarını besler. ✓
  - **Kırık varsayım:** AD10 recovery alanları (`recoveryTransactionCount`/`failoverStatus`/`restrictionReason`) T69'a forward-deferred (her zaman 0/NONE/null); recovery-queue satır endpoint'i yok (grep 0 eşleşme). → S18 Recovery Queue + emanet item listesi veri kaynaksız → proje sahibine sunuldu (Adım 5), **Option A (frontend-only + deferred)** onaylandı (AskUserQuestion 2026-06-07).
- **Adversarial review düzeltmeleri (yapım-içi):** (1) status ikonları 04 §8.7 spec tablosuna hizalandı (✅/⚠/❌); (2) banner server Türkçe yerine client-side lokalize edildi; (3) başlık hiyerarşisi düzeltildi (kart başlığı `<h3>`→`<p>`, sayfa h1 → recovery h2). 2 bulgu çürütüldü (dl>div>dt/dd HTML5 geçerli + T98 onaylı; failoverStatus deferral zaten belgeli).
