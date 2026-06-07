# T102 — Admin Parametre Yönetimi (S17)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-06-07

---

## Yapılan İşler

S17 — Admin Parametre Yönetimi ekranı (04 §8.6). **Salt frontend** — AD8 (`GET /admin/settings`) + AD9 (`PUT /admin/settings/:key`) backend'de T39'dan beri mevcut; backend genişletme/migration yok.

- **API katmanı** ([admin.ts](../../frontend/src/lib/api/admin.ts)): AD8/AD9 settings bölümü — `AdminSettingItem` (key/value/category/label/description/unit/valueType), `AdminSettingsListResponse`, `UpdateSettingResult`, `listAdminSettings()`, `updateAdminSetting(key, value)`.
- **Hook'lar:** `useAdminSettings` (tek çağrı list, react-query) + `useUpdateSetting` (mutation → `["admin","settings"]` invalidation).
- **Sunum eşlemesi** ([settingsCatalog.ts](../../frontend/src/lib/admin/settingsCatalog.ts)): backend kategorilerini 04 §8.6 gruplarına katlama (`geo_blocking`+`age_verification` → "Erişim ve Uyumluluk"); kategori→etki sınıfı türetimi (yeni işlem / runtime); `groupSettings()` belgeli + operasyonel + bilinmeyen (fallback `other`) gruplama.
- **Bileşenler:** `SettingsManager` (info box + belgeli gruplar + operasyonel bölüm), `SettingsGroupTable` (başlıklı kart), `SettingRow` (inline düzenle → value-type'a duyarlı input → Kaydet/İptal → toast + inline backend hata), `ImpactScopeInfoBox` (+ `ImpactBadge`).
- **ToastProvider** admin layout'a mount edildi (S17 "Parametre güncellendi" toast'u için; daha önce yalnız dev demo'da vardı).
- **Sayfa:** `/admin/settings` stub → tam sayfa (loading/error/empty + `SettingsManager`).
- **i18n:** `adminSettings` namespace 4-locale (en/tr/es/zh), leaf parity **899×4** (36 yeni leaf × 4).
- **Doc hizalama** (proje sahibi onayı 2026-06-07 — Q3 "bu task'ta hizala"):
  - 07 §9.8: stale örnek key → gerçek (`commission_rate`); **Kategoriler** 12→**15** API kategorisi (`sanctions_screening` kaldırıldı, `wallet_security`/`reputation`/`platform_maintenance`/`retention` eklendi) + 4 not.
  - 06 §3.17: tablo 30→**58 anahtar** (seed sırası, DB Category/DataType/default authoritative `SystemSettingSeed`'den) + API-lehçesi notu.

## Etkilenen Modüller / Dosyalar

**Yeni:** `frontend/src/lib/admin/settingsCatalog.ts`, `frontend/src/lib/hooks/useAdminSettings.ts`, `frontend/src/lib/hooks/useUpdateSetting.ts`, `frontend/src/components/admin/{ImpactScopeInfoBox,SettingRow,SettingsGroupTable,SettingsManager}.tsx`

**Değişen:** `frontend/src/lib/api/admin.ts`, `frontend/src/components/admin/index.ts`, `frontend/src/app/[locale]/admin/layout.tsx`, `frontend/src/app/[locale]/admin/settings/page.tsx`, `frontend/src/i18n/messages/{en,tr,es,zh}.json`, `Docs/07_API_DESIGN.md`, `Docs/06_DATA_MODEL.md`

## Kabul Kriterleri Kontrolü

| # | Kriter (11 plan) | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Parametre grupları: timeout, komisyon, işlem limitleri, iptal kuralları, yeni hesap, gas fee, fraud, alıcı belirleme, erişim/uyumluluk, blockchain health | ✓ | `SETTING_GROUPS` (settingsCatalog.ts) — 11 belgeli grup + 3 operasyonel; `adminSettings.groups` 4-locale. `accessCompliance` = geo_blocking+age_verification. |
| 2 | Inline edit: düzenle → kaydet/iptal | ✓ | `SettingRow` — Düzenle → value-type input → Kaydet (AD9 mutate) / İptal; başarı → toast "Parametre güncellendi". |
| 3 | Etki kapsamı bilgi kutusu (yeni işlem vs. runtime) | ✓ | `ImpactScopeInfoBox` (3-sınıf legend) + her satırda `ImpactBadge` (`impactForCategory`). |
| 4 | `GET /admin/settings`, `PUT /admin/settings/:key` çağrıları | ✓ | `listAdminSettings()` + `updateAdminSetting()` (admin.ts); apiClient envelope. |

**Doğrulama kontrol listesi (11 plan):** "04 §8.6 tüm parametre grupları var mı?" → ✓ (10 belgeli grup eşlendi; superset olarak operasyonel kategoriler de gösteriliyor).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| TypeScript | ✓ 0 hata | `npx tsc --noEmit` (exit 0) |
| ESLint | ✓ 0/0 | dokunulan 11 ts/tsx dosyası |
| Prettier | ✓ temiz | dokunulan dosyalar (LF; Windows working-copy CRLF artefakt, git LF saklar) |
| Build | ✓ | `npx next build` exit 0 — `ƒ /[locale]/admin/settings` |
| i18n parity | ✓ 899×4 | 4 locale eşit leaf, 0 missing/extra |
| Unit/Integration | — | Test beklentisi: Yok (frontend görsel task; frontend test runner yok, F5 plan-onaylı). Backend kodu değişmedi (yalnız doc). |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Yapım bitti — bağımsız validator chat'i bekliyor |
| Bulgu sayısı | — (validator) |
| Düzeltme gerekli mi | — (validator) |

## Altyapı Değişiklikleri

- Migration: **Yok**
- Config/env değişikliği: **Yok**
- Docker değişikliği: **Yok**
- Yeni dış bağımlılık: **Yok**

## Commit & PR

- Branch: `task/T102-admin-settings`
- Commit: `695e957` — T102: Admin Parametre yönetimi (S17) (kod + doc)
- PR: **#157**
- CI: ⏳ izleniyor

## Known Limitations / Follow-up

- **K1 — Per-key label/description backend-Türkçe:** Setting label/açıklama AD8'den (catalog meta, Türkçe) gelir; EN/ES/ZH locale'lerinde de Türkçe görünür. UI chrome (grup başlıkları, etki etiketleri, butonlar, toast) 4-locale yerelleştirilmiş. T100/T101 raw-enum-display precedent'i.
- **K2 — Client-side per-key validation yok:** min/max/range/çapraz-alan kuralları (06 §3.17, ör. `payment_timeout_min < max`, `0<commission_rate<1`) yalnız backend'de uygulanır; `VALIDATION_ERROR` satır altında gösterilir ve düzenleme modu açık kalır. UI tip-bazlı input (number/select/text) verir ama değer-aralığı zorlamaz.
- **K3 — Frontend test runner yok:** F5 plan-onaylı (T84+); inline-edit/toast davranışı manuel + build ile doğrulandı.
- **K4 — "Destekleyici sinyal" etki sınıfı veri-boş:** Info box 04 §8.6 üç sınıfı belgeler, ancak katalogda VPN (tek destekleyici-sinyal grubu) anahtarı yok → bu etiketli satır render edilmez (yalnız bilgi amaçlı legend).
- **K5 — CSV/string inputlar düz metin:** `auth.banned_countries`, `multi_account.exchange_addresses` gibi CSV alanlar tek satır metin; chip/liste editörü yok (yeterli, gelişmiş değil).
- **K6 — Audit alanları UI'da yok:** `updatedAt`/`UpdatedByAdminId` AD8 DTO'da yok; AD9 `updatedAt` döner ama satırda gösterilmez (düzenleme sonrası invalidation değeri yeniler). S21 (T106) audit log ayrı yüzey.
- **K7 — Doc hizalama kapsamı:** 07 §9.8 + 06 §3.17 hizalandı; diğer dokümanlardaki parametre referansları (ör. 02 §16.2) taranmadı — drift varsa forward.

## Notlar

- **Working tree (task.md Adım -1):** temiz (session başında `git status --short` boş).
- **Main CI startup (task.md Adım 0):** son 3 run `success` — `27092817285` (CI), `27092817282` (Docker Publish), `27091008244` (Docker Publish).
- **Dış varsayımlar (task.md Adım 4):**
  - AD8 mevcut ✓ — kod okundu (`AdminController.cs:342`, `SystemSettingsService.ListAsync`, `SettingItemDto` 7 alan).
  - AD9 mevcut ✓ — `SystemSettingsService.UpdateAsync` (catalog üyelik kontrolü, `MANAGE_SETTINGS`).
  - **Kırık varsayım:** Doc kategori sayısı (07 §9.8 = 12, `sanctions_screening` dahil) ≠ backend (15, `wallet_security`/`reputation`/`platform_maintenance`/`retention` dahil; `sanctions_screening` yok). Proje sahibine 3 kapsam kararıyla sunuldu → "hepsini göster + operasyonel bölüm" + "kategori-bazlı 3-sınıf etki map" + "doc'u bu task'ta hizala" onaylandı (2026-06-07).
- **Kapsam kararları (AskUserQuestion, 2026-06-07):** (1) tüm 15 kategori gösterilir, operasyonel 3 kategori ayrı bölümde; (2) etki AD8'de alan olmadığı için frontend kategori map'iyle türetilir; (3) doc drift bu PR'da kapatıldı.
