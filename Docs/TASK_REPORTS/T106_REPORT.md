# T106 — Admin Audit Log (S21)

**Durum:** ✓ Tamamlandı — bağımsız validator PASS (2026-06-13)
**Branch:** `task/T106-admin-audit-log`
**PR:** #163
**Tip:** Full-stack (frontend ağırlıklı + minimal backend search genişletme; migration YOK)

---

## 1. Yapılan İşler

S21 Admin Audit Log ekranı (04 §8.10) — platform fon hareketleri, admin aksiyonları ve güvenlik olaylarının kronolojik, filtrelenebilir görüntüsü. Mevcut **AD18** `GET /admin/audit-logs` (T42, 07 §9.19) tüketilir.

**Backend (minimal genişletme — proje sahibi onayı Option A, 2026-06-13):**
- `AuditLogQueryService.ListAsync` serbest-metin `search` filtresi EntityId-only iken **kullanıcı kimliğini de kapsayacak** şekilde genişletildi: `EntityId LIKE %term%` **VEYA** eşleşen `User.SteamId`/`SteamDisplayName`'in `ActorId`/`UserId`'si (Users alt-sorgusu, `IgnoreQueryFilters` ile actor/subject hydration domain'iyle hizalı). Böylece 04 §8.10 "Kullanıcı: Steam ID veya kullanıcı adı" filtresi gerçek kişilere çözümlenir — EntityId bir Guid taşısa bile.
- Migration/yeni endpoint/yeni dep YOK; salt-okunur sorgu değişikliği.

**Frontend (S21):**
- `app/[locale]/admin/audit-logs/page.tsx` — stub→tam. URL-senkron `FilterBar` (kategori/kullanıcı arama/işlem ID/tarih aralığı) + `AuditLogTable` + `Pagination`; 3 state (log var / filtre sonucu boş / yükleniyor skeleton). `dateTo` audit precision için gün-sonuna genişletilir (review AC-2 fix).
- `components/admin/AuditLogTable.tsx` (yeni) — `ResponsiveTable<AdminAuditLogItem>`, 6 kolon: Tarih/Saat (`formatDateTime`), Kategori (`AuditCategoryBadge`), Aksiyon (client-lokalize, `t.has` fallback), Kullanıcı (actor + varsa subject, ikisi de →S20 link; SYSTEM düz metin), İşlem ID (→S16 link), Detay (opaque JSON compact render).
- `components/admin/AuditCategoryBadge.tsx` (yeni) — 3 kategori tonlu rozet (fund=blue / admin=slate / security=amber).
- `lib/api/admin.ts` — AD18 katmanı: `AdminAuditCategory` / `AuditLogParticipant` / `AdminAuditLogItem` / `AdminAuditLogResponse` / `AdminAuditLogQuery` + `listAdminAuditLogs()`.
- `lib/hooks/useAdminAuditLogList.ts` (yeni) — React Query + `keepPreviousData`.
- `components/admin/index.ts` — barrel export.
- `i18n/messages/{tr,en,es,zh}.json` — yeni `adminAuditLog` namespace, **48 leaf × 4 IDENTICAL** (26 AuditAction etiketi + 3 kategori + filtre/kolon/state metinleri).

**Doc:** 07 §9.19 — `search` semantiği (EntityId + kullanıcı kimliği) belgelendi (04 §8.10 hizalaması, T101/T102 doc-reconciliation deseni).

---

## 2. Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `backend/.../Skinora.Platform/Application/Audit/AuditLogQueryService.cs` | search → EntityId + user identity (Users alt-sorgu) |
| `backend/tests/.../Integration/AuditLogQueryServiceTests.cs` | +3 test (SteamId / displayName / actor-tarafı) |
| `frontend/src/app/[locale]/admin/audit-logs/page.tsx` | stub→tam S21 sayfası |
| `frontend/src/components/admin/AuditLogTable.tsx` | yeni — 6 kolon tablo |
| `frontend/src/components/admin/AuditCategoryBadge.tsx` | yeni — kategori rozeti |
| `frontend/src/lib/api/admin.ts` | AD18 tipleri + `listAdminAuditLogs` |
| `frontend/src/lib/hooks/useAdminAuditLogList.ts` | yeni — liste hook'u |
| `frontend/src/components/admin/index.ts` | barrel export |
| `frontend/src/i18n/messages/{tr,en,es,zh}.json` | `adminAuditLog` 48×4 |
| `Docs/07_API_DESIGN.md` | §9.19 search notu |

---

## 3. Kabul Kriterleri Kontrolü (kanıtlı)

| AC | Durum | Kanıt |
|---|---|---|
| Filtre formu: kategori, tarih, kullanıcı, işlem ID | ✓ | `page.tsx` `fields[]` — category(select 3) / search(text "Kullanıcı") / transactionId(text) / dateFrom+dateTo(date); URL-senkron |
| Log tablosu: kategori, aksiyon, aktör, konu, işlem ID, detay, tarih | ✓ | `AuditLogTable` 6 kolon (Kullanıcı kolonu actor+subject birleşik); aktör+konu ikisi de render |
| State'ler: log var, filtre sonucu boş, yükleniyor | ✓ | `page.tsx` isError→ErrorState / isLoading→Skeleton×5 / else→AuditLogTable (`emptyMessage` boş-state) |
| GET /admin/audit-logs çağrısı | ✓ | `listAdminAuditLogs` → `apiClient<AdminAuditLogResponse>("/admin/audit-logs"+qs)` |

**Deep-link'ler:** Kullanıcı (actor/subject steamId) → `/admin/users/{steamId}` (S20, T105 ile mevcut); İşlem ID → `/admin/transactions/{id}` (S16, T101).

---

## 4. Test Sonuçları

**Backend:**
- `dotnet test Skinora.Platform.Tests --filter AuditLogQueryServiceTests` → **15/15 PASS** (SQLite; mevcut 12 + 3 yeni: SteamId araması→1, displayName araması→1, actor-tarafı çoklu satır→3 dedup).
- `dotnet build Skinora.sln -c Release` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
- `dotnet format --verify-no-changes` (değişen dosyalar) → **Δ=0**.

**Frontend:**
- `npx tsc --noEmit` → **0**.
- `npx eslint` (T106 dosyaları) → **0 error** (4 JSON "no matching config" uyarısı — ESLint JSON'u lint etmez, beklenen).
- `npx prettier --check --end-of-line auto` (T106 dosyaları) → **clean**.
- `npm run build` → **PASS**, `/[locale]/admin/audit-logs` ƒ (Dynamic).
- i18n parity (node leaf-sayım): `adminAuditLog` **48 / 48 / 48 / 48** (tr/en/es/zh, 0 drift).

**Test beklentisi (plan):** "Yok" — ancak Option A backend genişletmesi gerçek davranış değişikliği olduğu için 3 entegrasyon testi eklendi.

---

## 5. Altyapı Değişiklikleri

Migration YOK. Yeni paket bağımlılığı YOK. Yeni endpoint YOK (AD18 mevcut). Yeni Shared enum YOK.

---

## 6. Dış Varsayımlar (Adım 4 — Ön-uçuş)

- **Varsayım:** AD18 `GET /admin/audit-logs` mevcut ve 04 §8.10 filtrelerini (kategori/tarih/kullanıcı/işlem ID) destekliyor.
- **Doğrulama (anlama-fazı fan-out workflow, 3 ajan):** Endpoint **gerçek** (T105'teki placeholder tuzağı YOK) — `AdminController.cs:392-417`, `VIEW_AUDIT_LOG` korumalı, gerçek `AuditLogListItemDto` döndürür. **Kırık varsayım:** `search` parametresi yalnız `EntityId` üzerinde LIKE yapıyordu (`AuditLogQueryService.cs`), `User.SteamId`/`SteamDisplayName` sorgulanmıyordu → 04 §8.10 "Kullanıcı" filtre niyeti karşılanmıyordu (07 §9.19 jenerik `search` tanımlıyordu).
- **Karar:** Proje sahibine sunuldu (AskUserQuestion 2026-06-13) → **Option A: minimal backend genişletme** (full-stack). Recovery/failover ve `Transaction.EscrowBotId` boşlukları AYRI bir bulguydu (T103b) — bu task'ın kapsamı dışında, ertelenmiş ve belgelenmiştir (PR #162).

---

## 7. Mini Güvenlik Kontrolü

- **Auth:** Yeni endpoint yok; AD18 `VIEW_AUDIT_LOG` ile server-korumalı (`PolicyViewAuditLog` + `admin-read` rate limit). FE client-guard yok — backend 403 → ErrorState (T103 K5 emsali).
- **Injection:** `EF.Functions.Like($"%{term}%")` — EF interpolasyonu parametreleştirir (SQL injection yok).
- **PII:** Kullanıcı araması `IgnoreQueryFilters` kullanır ama anonimleştirilmiş kullanıcı kimliği 02 §19 ile scrub'landığı için eşleşmeyi durdurur (sızıntı yok). Audit zaten admin-only.
- **XSS:** Detay JSON `String()`/`JSON.stringify` ile render, React escape eder.
- **Dep/secret:** 0 yeni bağımlılık, secret yok.

---

## 8. Ön-Validasyon Adversarial Review

6-boyut/refute-default workflow (AC-conformance / contract-drift / backend-search / i18n-a11y / security / spec-deviation): **2 ham bulgu, ikisi de S3, 0 bloke-edici** (4 boyut sıfır bulgu). AC-2 (dateTo gün-sonu) bu PR'da düzeltildi; AC-1 (kolon sıralama) K-note olarak bırakıldı.

---

## 8b. Bağımsız Doğrulama (Validator — ayrı chat, 2026-06-13)

**Verdict: ✓ PASS.** Yapım raporu görülmeden bağımsız verdict oluşturuldu, sonra karşılaştırıldı (tam uyum).

**Hard-stop kapıları:** Adım -1 working tree temiz; Adım 0 main son-3 run `success` (`27464744799`/`27464744804` docs(T103b) + `27463601800` T105b); Adım 0b repo memory T106 satırı mevcut.

**Mekanik kapılar (validator-çalıştırıldı, HEAD `d95209d`):**
- Backend: `Skinora.Platform.Tests` **166/166 PASS** (0 Failed; 3 yeni search testi dahil).
- Frontend: `tsc --noEmit` **0** · `eslint` (T106 dosyaları) **0** · `prettier --check` **clean** · `next build` ✓ (`/[locale]/admin/audit-logs` ƒ Dynamic).
- i18n parity: `adminAuditLog` **48 / 48 / 48 / 48** (en/tr/es/zh, 0 missing / 0 extra).
- Kontrat: `AuditLogListItemDto` ↔ 07 §9.19 ↔ FE `AdminAuditLogItem` 1:1 (alan adı/nullability/casing); `AuditAction` enum **26 ↔ 26** i18n aksiyon etiketi (exact-name match, 4 dil); AD18 endpoint `VIEW_AUDIT_LOG` server-korumalı, tüm query param wire'lı.
- Task CI `27465633952` HEAD `d95209d` **success**.

**Kabul kriterleri:** AC1 ✓ / AC2 ✓ / AC3 ✓ / AC4 ✓. Doğrulama kontrol listesi ("04 §8.10 tüm bileşenler ve state'ler") = **~ Kısmi**: tüm bileşen ve state'ler mevcut, **tek istisna interaktif kolon-sıralama** (varsayılan en-yeni-üstte var; kolon-tık sıralama yok — AD18 07 §9.19 sort param tanımlamıyor + paylaşılan tablo affordance T98'den beri yok; emsal T101 K10 / kardeş S15-S16 aynı). **Bloke-edici değil.**

**Adversarial review (6-boyut/refute-default workflow, gerçek dosya okuması):** 3 ham bulgu → **2 onaylandı (ikisi de non-blocking) → 0 bloke-edici.** Kolon-sıralama adayı çürütüldü (pre-existing shared-component + sort-less API contract + kardeş S15/S16 aynı boşluk). Onaylanan iki non-blocking: (1) LIKE wildcard escape edilmiyor (precision note — kardeş AD16 T105 `AdminUserService.cs:88-89` + sanctions search ile birebir aynı, önceden doğrulanmış desen; injection değil, parametrize); (2) **TR `title` çevrilmemiş** (aşağıda K7 — validator-fix ile kapatıldı).

**Validator-fix (proje sahibi kararı 2026-06-13):** `tr.json` `adminAuditLog.title` `"Audit Log"` → `"Denetim Kaydı"` (blok terimi `tableAriaLabel`/`loadError` "Denetim kaydı" ile + ES/ZH çevirisiyle tutarlı). Fix sonrası FE gate'leri yeniden koşuldu: i18n 48×4 ✓ · prettier clean ✓ · next build ✓.

**Güvenlik:** Temiz (yeni endpoint yok—AD18 `VIEW_AUDIT_LOG` server-korumalı; `EF.Functions.Like` parametrize—injection yok; `IgnoreQueryFilters` anonimleştirilmiş kimliği sızdırmaz—02 §19 scrub eşleşmeyi durdurur; detay React-escaped; 0 yeni dep; secret yok).

**Yapım raporu karşılaştırması:** Tam uyum — AC1-4 ve K1-K6 birebir örtüşür. Validator ek olarak K7'yi (TR title) yakaladı; construction chat'in K-not'larında yoktu (i18n parity kapısı yalnız key-yapısını kontrol eder, değer çevirisini değil).

---

## 9. Known Limitations (K-notes)

- **K1 (AC-1, S3): İnteraktif kolon-sıralama yok.** 04 §8.10 "kolon başlıkları tıklanarak sıralama" istiyor; yalnız varsayılan en-yeni-üstte (`OrderByDescending(Id)`) karşılanıyor. `ResponsiveTable`'da sort affordance T98'den beri yok + backend sort param gerektirir. **T101 K10 emsali** — owner-onaylı bilinçli sapma.
- **K2 (AC-2 kardeş): `dateTo` gün-sonu yalnız T106'da düzeltildi.** Aynı off-by-one-gün deseni S13 (`flags/page.tsx`) ve S15 (`transactions/page.tsx`) paylaşılan FilterBar akışında hâlâ var (T100 K6'da ertelenmişti). Repo-geneli düzeltme ayrı bir chore'a bırakıldı (T106 PR'ı diğer task yüzeylerine dokunmaz — bundled-PR yasağı).
- **K3: Detay kolonu yalnız `NewValue` gösterir.** Backend `AuditLogListItemDto.Detail` yalnız `NewValue`'yu parse eder; `OldValue` DTO'da yok (07 §9.19 ile uyumlu, 04 §8.10 "eski/yeni değer" ifadesine göre eksik). Backend DTO genişletmesi gerektirir — defer.
- **K4: FE permission guard yok.** Backend `VIEW_AUDIT_LOG` enforce eder (T99 K5 / T103 K5 emsali).
- **K5: Frontend test runner yok** (F5 plan-onaylı).
- **K6: Aksiyon etiketleri client-lokalize.** Backend ham enum adı döndürür; FE 26 AuditAction'ı `adminAuditLog.action.*` ile lokalize eder, eksik/gelecekteki enum'da `t.has` fallback ham isim gösterir (T104 permission deseni).
- **K7 (validator-caught, ÇÖZÜLDÜ): TR `title` çevrilmemişti.** `adminAuditLog.title` = "Audit Log" (İngilizce) iken ES/ZH çevrilmiş + aynı TR blok "Denetim kaydı" kullanıyordu. Bağımsız validator yakaladı (i18n parity kapısı yalnız key-yapısını kontrol eder). Proje sahibi kararı: validator-fix → "Denetim Kaydı". **Bloke-edici değildi (S3 cosmetic page-H1).**
- **K8 (LIKE precision note): Serbest-metin `search` LIKE wildcard escape etmiyor.** `_`/`%`/`[` içeren nadir girdide over-match olur (Steam ID numerik / tipik kullanıcı adı bu karakterleri içermez). Kardeş AD16 (`AdminUserService.cs:88-89`, T105) + sanctions search ile birebir aynı, önceden doğrulanmış desen; `EF.Functions.Like` parametrize → SQL injection değil. `EscapeLike` helper (T63) yalnız user-typed `ItemName` aramasında benimsenmiş. Repo-geneli tutarlılık chore'una defer.

---

## 10. Notlar (Startup + Audit Trail)

- **Adım -1 (working tree):** Session içinde temiz (PR #162 merge sonrası main'den dallanıldı).
- **Adım 0 (main CI startup):** main son tamamlanmış run'lar success — `4c90d2b` CI (`27463601800`) + `4c90d2b` Docker (`27463601806`) + `04e58ac` Docker (`27464744799`); `04e58ac` CI docs-only (Lint) PR aşamasında yeşildi.
- **Anlama + review:** 2 workflow (anlama 3-ajan fan-out + review 6-boyut) gerçek dosya okumasıyla çalıştırıldı.

## 11. Commit & PR

- Kod commit + rapor/status/memory commit (ayrı).
- **PR: #163**
- Repo memory `.claude/memory/MEMORY.md` T106 satırı eklendi.
