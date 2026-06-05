# T100 — Admin Flag Kuyruğu + Detay (S13, S14)

**Faz:** F5 | **Durum:** ⏳ Yapım bitti (doğrulama bekliyor) | **Tarih:** 2026-06-05

---

## Yapılan İşler

**Frontend (asıl task — 04 §8.2–§8.3):**
- **S13 Flag Kuyruğu** (`app/[locale]/admin/flags/page.tsx`, stub → tam sayfa): URL-senkron filtreler (kategori/tür/durum/tarih) + `FilterBar` + `ResponsiveTable` (T98) + `Pagination` + başlıkta bekleyen flag rozeti (`pendingCount`).
- **S14 Flag Detay** (`app/[locale]/admin/flags/[id]/page.tsx`, yeni route): işlem-flag varyantı (fiyat sapması / yüksek hacim / anormal davranış `flagDetail`) + hesap-flag varyantı (çoklu hesap sinyali + ilişkili hesaplar). Taraf bilgileri `UserCard`, işlem durumu `StatusBadge`.
- **Aksiyon alanı:** Admin Notu (opsiyonel) + onay modal'ı. İşlem flag: "İşleme Devam Et" (AD4 approve) / "İptal Et" (AD5 reject). Hesap flag: "Flag Kaldır" (AD4 approve) + "Aktif İşlemleri Hold'a Al" (AD19d). "Askıya Al" deferred (disabled + "Yakında") — bkz. Known Limitations.
- Yeni bileşenler: `FlagQueueTable`, `FlagDetailView`, `FlagActionModal` (tone'lu + opsiyonel zorunlu-sebep), `FlagReviewStatusBadge`.
- Yeni API katmanı `lib/api/admin.ts` (flag tipleri + `listAdminFlags`/`getAdminFlag`/`approveAdminFlag`/`rejectAdminFlag`/`holdUserTransactions`) + React Query hook'ları (`useAdminFlagList`/`useAdminFlagDetail`/`useAdminFlagMutations`).
- 4-locale `adminFlags` namespace (en/tr/es/zh, 82 leaf × 4); leaf parity 747×4 korundu.

**Backend (T100'ün kendi gereksinimleri):**
- **Scope query param** (`GET /admin/flags`, AD2 — 07 §9.2): `FraudFlagListQuery.Scope` + `ListAsync` where-clause + controller `[FromQuery] FraudFlagScope?`. Sunucu-taraflı kategori filtresi → pagination/`totalCount` doğru kalır.
- **`userId` flag detayında** (AD3 — 07 §9.3): `FraudFlagDetailDto.UserId` (flag'lenmiş kullanıcının iç Guid'i) — frontend "Hold" aksiyonunu steamId→id lookup'ı olmadan hedefler.
- **AD19d** `POST /admin/transactions/hold-by-user/:userId` (07 §9.22a): `AdminTransactionService.HoldAllUserTransactionsAsync` — mevcut AD19b per-tx freeze+hold+notify+audit sırasını kullanıcının tüm aktif işlemleri üzerinde tekrar kullanır; `EMERGENCY_HOLD` yetkisi; `EMERGENCY_HOLD_APPLIED` audit + `EmergencyHoldAppliedEvent` (yeni enum/event yok). `!IsOnHold` filtresiyle idempotent.

## Etkilenen Modüller / Dosyalar

**Backend:**
- `Skinora.Fraud/Application/Flags/IFraudFlagAdminQueryService.cs` (FraudFlagListQuery + Scope)
- `Skinora.Fraud/Application/Flags/FraudFlagAdminQueryService.cs` (scope where + UserId)
- `Skinora.Fraud/Application/Flags/FraudFlagDtos.cs` (FraudFlagDetailDto.UserId)
- `Skinora.API/Controllers/AdminFlagsController.cs` (scope param)
- `Skinora.Transactions/Application/Admin/{AdminTransactionDtos,IAdminTransactionService,AdminTransactionService}.cs` (AD19d)
- `Skinora.API/Controllers/AdminTransactionsController.cs` (AD19d endpoint)
- Testler: `FraudFlagAdminQueryServiceTests.cs` (+scope filter, +UserId, +query ctor fix ×3), `AdminFlagsEndpointTests.cs` (+scope binding), `AdminTransactionServiceTests.cs` (+3 hold-all)

**Frontend:**
- `lib/api/admin.ts`, `lib/hooks/useAdminFlagList.ts` / `useAdminFlagDetail.ts` / `useAdminFlagMutations.ts`
- `components/admin/{FlagQueueTable,FlagDetailView,FlagActionModal,FlagReviewStatusBadge}.tsx` + `index.ts`
- `app/[locale]/admin/flags/page.tsx`, `app/[locale]/admin/flags/[id]/page.tsx`
- `i18n/messages/{en,tr,es,zh}.json` (+adminFlags)

**Docs:** `07_API_DESIGN.md` (§9.2 scope, §9.3 userId, §9.22a AD19d)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | S13 Flag kuyruğu: filtreleme (kategori, tür, durum, tarih), liste | ✓ | `FilterBar` 5 alan + `useAdminFlagList`; kategori = yeni AD2 `scope` param (server-side) |
| 2 | S14: işlem flag varyantı (fiyat sapması, yüksek hacim) + hesap flag varyantı | ✓ | `FlagDetailView.renderFlagDetail` type-switch + `isAccount` dalları |
| 3 | Admin notu textarea, "devam ettir" / "iptal et" butonları | ✓ | Aksiyon alanı page-level not + İşleme Devam Et/İptal Et (approve/reject) |
| 4 | Onay modal'ı | ✓ | `FlagActionModal` (approve/reject "emin misiniz?" + Hold zorunlu-sebep) |
| 5 | GET /admin/flags, GET /admin/flags/:id, POST approve/reject çağrıları | ✓ | `lib/api/admin.ts` + hook'lar; AD19d Hold (proje sahibi onaylı genişletme) |

## Doğrulama Kontrol Listesi
- [x] 04 §8.2–§8.3 tüm varyantlar ve aksiyonlar var mı? → İşlem + hesap varyant, approve/reject + Hold; **Askıya Al deferred** (K1).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Frontend tsc | ✓ 0 hata | `npx tsc --noEmit` |
| Frontend ESLint | ✓ 0 hata/0 uyarı | T100 dosyaları |
| Frontend Prettier | ✓ clean | T100 dosyaları + 4 locale json |
| Frontend next build | ✓ 26 route | `/admin/flags` + `/admin/flags/[id]` üretildi |
| i18n parity | ✓ 747×4 | adminFlags 82×4 |
| Backend build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| dotnet format | ✓ Δ=0 | değişen 11 C# dosyası `--verify-no-changes` exit 0 |
| AdminFlagsEndpointTests (SQLite) | ✓ 10/10 | scope binding testi dahil |
| Fraud + Transactions integration | ⏳ CI | SQL Server (Testcontainers/Docker) lokal Windows'ta yok → CI shared mssql (T11.3 paterni) |

## Altyapı Değişiklikleri
- Migration: **Yok** (FraudFlag.Scope/UserId entity'de mevcut; Transaction hold alanları T19/T44'ten beri var).
- Config/env: Yok. Yeni dış bağımlılık: Yok. Yeni enum/event: Yok (mevcut `EMERGENCY_HOLD_APPLIED` + `EmergencyHoldAppliedEvent` reuse).

## Commit & PR
- Branch: `task/T100-admin-flag-queue-detail`
- PR: #(pending)
- CI: (pending)

## Known Limitations / Follow-up
- **K1 — "Askıya Al" (hesap askıya alma) deferred:** State modeli yok (User yalnız `IsDeactivated`); migration + auth pipeline enforcement + suspended-session + S03d gerektirir (~40-60h) ve traceability matrisinde **S20 = T105**'e ait. Proje sahibi onayıyla **ayrı bir task/PR** olarak yapılacak (S14'teki buton disabled + "Yakında"; suspend task aktive edecek).
- **K2 — Hesap-flag liste kolonları (Sinyal Detayı / İlişkili Hesaplar / Aktif İşlem Sayısı):** AD2 liste projeksiyonunda yok (07 §9.2); S14 detayında gösteriliyor. Liste DTO genişletmesi (flagDetail parse + per-user aktif sayım) T100 kapsamı dışında — T-future.
- **K3 — SANCTIONS_MATCH `flagDetail`:** Backend `ParseDetail` bu tip için typed payload üretmez (null); S14 generic not gösterir + audit/sanctions kaydına yönlendirir.
- **K4 — `pendingCount` global:** Kategori filtresinden bağımsız toplam bekleyen backlog'u gösterir (T54 sözleşmesi korundu).
- **K5 — Frontend test runner yok** (F5 plan-onaylı); UI doğrulaması next build + tsc + manuel.
- **K6 — dateTo gün-sonu:** Backend `CreatedAt <= dateTo` (gece yarısı) — mevcut admin audit/tx listesiyle tutarlı platform davranışı.

## Notlar
- **Working tree (Adım -1):** temiz.
- **Main CI startup (Adım 0):** son 3 run success — `26371774696`, `26371774707` (T99 #147), `26369407170` (T98 #146).
- **Dış varsayımlar (Adım 4):** (a) `decimal?` para alanları JSON **number** olarak serialize olur — backend'de decimal→string converter yok (`Program.cs` yalnız `JsonStringEnumConverter`); flag `price`/`marketPrice` frontend'de `number | null` tiplendi. (b) `ApiResponseWrapperFilter` başarı yanıtlarını `ApiResponse<T>`'ye sarar → `Ok(rawDto)` frontend `apiClient` ile uyumlu. (c) Enum query param'ları `JsonStringEnumConverter` ile string'ten bağlanır (`scope=ACCOUNT_LEVEL`).
- **Scope kararı (proje sahibi onayı 2026-06-05):** T100 = frontend (S13+S14) + scope param + AD19d Hold; "Askıya Al" suspend özelliği ayrı task/PR (Seçenek C).
