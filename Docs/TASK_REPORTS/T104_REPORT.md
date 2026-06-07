# T104 — Admin Rol & Yetki Yönetimi (S19)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız doğrulama bekliyor) | **Tarih:** 2026-06-07

---

## Yapılan İşler

S19 rol & yetki yönetimi ekranı (04 §8.8), mevcut AD11–AD17 backend'ine bağlandı. **Salt frontend** — backend/migration/test değişikliği yok (AC "Test beklentisi: Yok"). Placeholder `<div>Admin Roles</div>` yerine tam ekran:

- **API client (AD11–AD17):** `listAdminRoles` / `createAdminRole` / `updateAdminRole` / `deleteAdminRole` + `listAdminUsers` (paginated) + `assignUserRole`. Tipler backend DTO'larıyla birebir (`RolesListResponse`, `RoleSummaryDto` — `isSuperAdmin` ek alanı dahil, `PagedResult<AdminUserListItemDto>`, `AssignRoleResponse`).
- **Hooks:** `useAdminRoles` (AD11), `useAdminUsers` (AD15, `keepPreviousData`), `useAdminRoleMutations` (create/update/delete + assign, role+user cache invalidation).
- **Client permission katalog:** `lib/admin/permissionCatalog.ts` — 12 bilinen key + `permissionLabelKey()`; i18n kontratı.
- **Components:**
  - `RolesTable` — Rol Adı (tıklanabilir → düzenleme), Açıklama, Atanmış Kullanıcı, Aksiyonlar (Düzenle/Sil). Süper admin satırı read-only (rozet + aksiyon gizli).
  - `RoleFormModal` — Yeni Rol Oluştur / Düzenle (`<dialog>`), ad + açıklama + 12-yetki checkbox matrisi (`availablePermissions`'tan dinamik). `ROLE_NAME_EXISTS` inline hata.
  - `UserRoleAssignment` — aranabilir (debounce 300ms) + sayfalı kullanıcı listesi + satır içi rol dropdown (boş seçenek = rol kaldır).
  - `RolesManager` — orchestrator; modallar, mutationlar, toastlar, `ROLE_HAS_USERS` toast.
- **i18n:** `adminRoles` bloğu en/tr/es/zh — **61 key × 4 dil IDENTICAL**. Yetki etiketleri client-lokalize (key bazlı, server label'a fallback).

## Etkilenen Modüller / Dosyalar

**Yeni (8):** `frontend/src/lib/admin/permissionCatalog.ts`, `lib/hooks/useAdminRoles.ts`, `lib/hooks/useAdminUsers.ts`, `lib/hooks/useAdminRoleMutations.ts`, `components/admin/RolesTable.tsx`, `RoleFormModal.tsx`, `UserRoleAssignment.tsx`, `RolesManager.tsx`

**Değişen (7):** `lib/api/admin.ts` (AD11–AD17), `components/admin/index.ts` (exports), `app/[locale]/admin/roles/page.tsx` (wire), `i18n/messages/{en,tr,es,zh}.json` (adminRoles bloğu)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Roller listesi tablosu: ad, açıklama, atanmış kullanıcı, aksiyonlar | ✓ | `RolesTable.tsx` — 4 kolon (Rol Adı/Açıklama/Atanmış Kullanıcı/Aksiyonlar) `ResponsiveTable` ile; 04 §8.8 tablosuyla birebir |
| 2 | Yetki matrisi: yetki checkbox listesi | ✓ | `RoleFormModal.tsx` — `availablePermissions`'tan dinamik **12** checkbox. AC "11 yetki" stale (bkz. Notlar); canlı katalog 12 |
| 3 | Yeni rol oluştur modal'ı | ✓ | `RoleFormModal.tsx` + `RolesManager` "Yeni Rol Oluştur" → AD12 `createAdminRole` |
| 4 | Kullanıcı-rol atama (dropdown) | ✓ | `UserRoleAssignment.tsx` — AD15 liste + satır içi `<select>` → AD17 `assignUserRole` |
| 5 | GET /admin/roles, POST/PUT/DELETE roles çağrıları | ✓ | `lib/api/admin.ts` — `listAdminRoles`/`createAdminRole`/`updateAdminRole`/`deleteAdminRole` (+ AD15/AD17) |

**Doğrulama kontrol listesi (04 §8.8):**
- [x] Roller Listesi (4 kolon) — ✓
- [x] Yeni Rol Oluştur butonu + modal (ad/açıklama/yetki seçimi) — ✓
- [x] Yetki Matrisi (12 yetki) — ✓ (dinamik)
- [x] Kullanıcı-Rol Atama (liste + Rol Ata/Değiştir dropdown) — ✓

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Type check | ✓ | `npx tsc --noEmit` → exit 0 |
| Lint | ✓ | `npm run lint` (eslint) → exit 0 |
| Format | ✓ | `prettier --check --end-of-line auto` → "All matched files use Prettier code style" (CRLF lokal Windows artifaktı; git `* text=auto` ile commit'te LF'e normalize — blob LF) |
| Build | ✓ | `npm run build` → exit 0, `/[locale]/admin/roles` route derlendi, TypeScript ✓ |
| i18n parity | ✓ | adminRoles 61 key × 4 dil IDENTICAL (node script) |
| Unit/Integration | — | AC "Test beklentisi: Yok" — salt frontend (T102/T103 emsali) |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız validator bekliyor |
| Bulgu sayısı (self) | 0 |
| Düzeltme gerekli mi | Hayır |

## Altyapı Değişiklikleri

- Migration: **Yok** (salt frontend, mevcut AdminRoles şeması T39'dan)
- Config/env değişikliği: **Yok**
- Docker değişikliği: **Yok**

## Commit & PR

- Branch: `task/T104-admin-roles`
- Commit: `e4eb42f` — T104: Admin Rol & yetki yönetimi (S19) — frontend page (AD11–AD17)
- PR: **#159**
- CI: ✓ **PASS** — kod commit `e4eb42f` → run [`27104250643`](https://github.com/turkerurganci/Skinora/actions/runs/27104250643) **success**; HEAD docs commit `9d24e67` → run [`27104477305`](https://github.com/turkerurganci/Skinora/actions/runs/27104477305) **success**. Branch izolasyon check temiz (yalnız T104).

## Known Limitations / Follow-up

- **K1 — FE permission guard yok:** Sayfa `MANAGE_ROLES`/`VIEW_USERS` için frontend guard içermez; backend tüm AD endpoint'lerinde policy enforce eder (T103 K5 emsali, salt-okunur+server-protected).
- **K2 — Rol atama optimistic değil:** AD17 mutation sırasında `<select>` disabled; değer invalidation/refetch ile güncellenir (kısa görsel gecikme, bloke-edici değil).
- **K3 — Süper admin atama serbest:** `isSuperAdmin` rolünün *tanımı* read-only ama kullanıcıya *atanması* (AD17) backend kuralına bırakıldı (04 §8.8 "Rol Ata/Değiştir" kısıtlamıyor).

## Notlar

- **Working tree:** Session başında temiz (Adım -1).
- **Main CI startup check (Adım 0):** Son 3 main run `success` — `27101235527` (T103), `27101235529` (T103 docker), `27097121138` (T102). HARD STOP yok.
- **Dış Varsayımlar (Adım 4):**
  - *Backend AD11–AD17 mevcut mu?* → ✓ `backend/src/Skinora.API/Controllers/AdminController.cs:105-257` (roles + users + assign endpoint'leri). Bu task'ı salt-frontend yapar.
  - *Permission katalog sayısı?* → **12** (`PermissionCatalog.cs` class doc'u "12 admin permissions" der; 04 §8.8 + 07 §9.11 ile birebir). **AC "11 yetki" stale** — `MANAGE_SANCTIONS` eklenmeden önce yazılmış. Frontend `availablePermissions`'ı dinamik render ettiği için sayı backend'in döndürdüğü = 12; kod hilesi yok.
  - *Roller nav?* → ✓ `AdminSidebar.tsx` `roles` item'ı zaten bağlı (T85).
  - *Yeni paket?* → Yok (0 yeni bağımlılık).
- **Mini güvenlik kontrolü:** Secret sızıntısı yok; yeni endpoint yok (mevcut server-protected AD'ler); input client+server doğrulanır (ad zorunlu, `ROLE_NAME_EXISTS`/`ROLE_HAS_USERS`/`VALIDATION_ERROR` ele alınır); 0 yeni dış bağımlılık.
- **Tasarım kararları (proje sahibi onaylı):** (1) Yetki etiketleri client-lokalize (T103 K6 emsali); (2) Süper admin rolü read-only (kendini-kilitleme önlemi).
