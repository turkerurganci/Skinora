# T100 — Admin Flag Kuyruğu + Detay (S13, S14)

**Faz:** F5 | **Durum:** ✓ Tamamlandı (Bağımsız Validator PASS — KL düzeltmeleriyle, 2026-06-06) | **Tarih:** 2026-06-05

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
- [~] 04 §8.2–§8.3 tüm varyantlar ve aksiyonlar var mı? → **Aksiyonlar tam** (approve/reject + Hold; Askıya Al deferred K1). **Hesap-flag içeriği kısmi:** Aktif İşlemler (sayı+liste, §8.3 madde 4) ve IP/cihaz sinyali (§8.3 madde 1) gösterilmiyor — AD2/AD3 projeksiyonu taşımıyor (K2/K9/K10, backend DTO genişletmesi deferred, proje sahibi onayı 2026-06-06).

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
- Commit: `fbae70c` (+ `51fdf1c` PR ref)
- PR: [#148](https://github.com/turkerurganci/Skinora/pull/148)
- CI: ✓ PASS — run [`27032782994`](https://github.com/turkerurganci/Skinora/actions/runs/27032782994) (CI Gate ✓, tüm job'lar; SQL-Server integration testleri shared mssql üzerinde yeşil + migration dry-run ✓)

## Known Limitations / Follow-up
- **K1 — "Askıya Al" (hesap askıya alma) deferred:** State modeli yok (User yalnız `IsDeactivated`); migration + auth pipeline enforcement + suspended-session + S03d gerektirir (~40-60h) ve traceability matrisinde **S20 = T105**'e ait. Proje sahibi onayıyla **ayrı bir task/PR** olarak yapılacak (S14'teki buton disabled + "Yakında"; suspend task aktive edecek).
- **K2 — Hesap-flag liste kolonları (Sinyal Detayı / İlişkili Hesaplar / Aktif İşlem Sayısı):** AD2 liste projeksiyonunda yok (07 §9.2). S14 detayı bunlardan **yalnız çoklu-hesap sinyalini + ilişkili hesapları** gösterir (MULTI_ACCOUNT); **Aktif İşlem Sayısı hiçbir yüzeyde gösterilmez** (bkz. K9) ve IP/cihaz sinyali düşürülür (bkz. K10). Liste DTO genişletmesi (flagDetail parse + per-user aktif sayım) T100 kapsamı dışında — backend DTO-genişletme task'ına deferred (proje sahibi onayı 2026-06-06). *Düzeltme notu: önceki "S14 detayında gösteriliyor" ifadesi Aktif İşlem Sayısı için hatalıydı, validator AC2-F1 ile düzeltildi.*
- **K3 — SANCTIONS_MATCH `flagDetail`:** Backend `ParseDetail` bu tip için typed payload üretmez (null); S14 generic not gösterir + audit/sanctions kaydına yönlendirir.
- **K4 — `pendingCount` global:** Kategori filtresinden bağımsız toplam bekleyen backlog'u gösterir (T54 sözleşmesi korundu).
- **K5 — Frontend test runner yok** (F5 plan-onaylı); UI doğrulaması next build + tsc + manuel.
- **K6 — dateTo gün-sonu:** Backend `CreatedAt <= dateTo` (gece yarısı) — mevcut admin audit/tx listesiyle tutarlı platform davranışı.
- **K7 — Flag para alanları JSON number:** AD2/AD3 `price`/`marketPrice`/`flagDetail` sayısalları `decimal`→JSON number (07 §9.2 note); işlem DTO'larının `string Price` scale-6 konvansiyonundan farklı (T54 mirası + `flagDetail` kayıtlı JSON number olduğundan zorunlu). 2-ondalıklı item fiyatlarında double precision riski ihmal edilebilir; flag yüzeyi kendi içinde tutarlı. 07 doc örnekleri number'a düzeltildi.
- **K8 — Bulk hold Hangfire delete EF-tx dışı:** AD19d döngüsünde `FreezeAsync` Hangfire `Delete`'i anında commit eder (EF transaction'a dahil değil); döngü ortasında throw olursa (pratikte ~imkansız — reason validate + `!IsOnHold` pre-filter) DB tarafı atomik rollback olur ama silinmiş job'lar geri gelmez. AD19b + T54 cascade ile **birebir aynı mevcut patern** — yeni defect değil; T-future cross-cutting iyileştirme adayı.
- **K9 — Hesap-flag "Aktif İşlemler" (sayı+liste, 04 §8.3 hesap-varyant madde 4) gösterilmiyor** (validator AC2-F1, S3): Ne S13 listesinde ne S14 detayında var; AD3 yanıtı (FraudFlagDetailDto) bu alanı projekte etmez. Per-user aktif işlem sayımı + listesi backend AD3 DTO genişletmesi gerektirir → backend DTO-genişletme task'ına deferred (proje sahibi onayı 2026-06-06, K2 ile aynı kova).
- **K10 — MULTI_ACCOUNT `supportingSignals` (IP/cihaz/source-address kanıtı) AD3 DTO'da düşüyor** (validator AC2-F2, S1): `MultiAccountDetector` bu sinyalleri üretip `FraudFlag.Details`'e yazar, ancak AD3 mapping DTO'su `MultiAccountFlagDetail` `supportingSignals` üyesini taşımaz → 07 §9.3:1742 kontrat alanı + 04 §8.3 "IP/cihaz bilgisi" gösterilemez. Düzeltme = AD3 DTO + ParseDetail genişletmesi (backend) + frontend render → K2/K9 ile aynı backend DTO-genişletme task'ına deferred (proje sahibi onayı 2026-06-06).

## Çok-Ajanlı Diff Review (ultracode)
4-boyutlu (backend correctness / frontend correctness / spec-conformance / security) paralel review + adversarial verify: **3 bulgu → 0 gerçek defect** (verify ile doğrulandı). 3 bulgu da low-severity gözlem: (1) Hangfire delete tx-dışı = mevcut patern (K8), (2) money-as-number = doğru wire eşleşmesi + intra-tutarlı (K7), (3) FilterBar history-nav resync = masked (tüm nav `router.replace`, T84 component davranışı, kullanıcıya görünür değil). Kod değişikliği gerekmedi.

## Notlar
- **Working tree (Adım -1):** temiz.
- **Main CI startup (Adım 0):** son 3 run success — `26371774696`, `26371774707` (T99 #147), `26369407170` (T98 #146).
- **Dış varsayımlar (Adım 4):** (a) `decimal?` para alanları JSON **number** olarak serialize olur — backend'de decimal→string converter yok (`Program.cs` yalnız `JsonStringEnumConverter`); flag `price`/`marketPrice` frontend'de `number | null` tiplendi. (b) `ApiResponseWrapperFilter` başarı yanıtlarını `ApiResponse<T>`'ye sarar → `Ok(rawDto)` frontend `apiClient` ile uyumlu. (c) Enum query param'ları `JsonStringEnumConverter` ile string'ten bağlanır (`scope=ACCOUNT_LEVEL`).
- **Scope kararı (proje sahibi onayı 2026-06-05):** T100 = frontend (S13+S14) + scope param + AD19d Hold; "Askıya Al" suspend özelliği ayrı task/PR (Seçenek C).

---

## Doğrulama (Bağımsız Validator — 2026-06-06)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** (Known Limitations ile) |
| Bağımsız verdict | İlk geçiş: düzeltme gerektiren (1×S3 + 2×S1, hesap-flag içerik tamlığı); proje sahibi "defer + disclosure düzelt" kararıyla (2026-06-06) KL'lere dönüştürüldü → PASS |
| Düzeltme gerekli mi | Uygulandı: K2 yanlış beyan düzeltildi + K9/K10 eklendi + `FlagQueueTable` yorumu düzeltildi |

**Ön-kapılar:** Working tree temiz · Main CI 3/3 success (`26371774696`/`26371774707`/`26369407170`) · MEMORY.md T100 satırı mevcut · Task branch CI [`27033279038`](https://github.com/turkerurganci/Skinora/actions/runs/27033279038) (HEAD `aac96e9`) **10/10** (Lint/Build/Unit/Integration/Contract/Migration/Docker).

**Bağımsız kanıt:** backend Release 0W/0E · frontend next build PASS (S13+S14 route) · eslint 0/0 · i18n parity 747×4 (0 missing/extra) · enum kontratı (FraudFlagScope/Type + ReviewStatus) backend↔frontend birebir · AD19d: seçim mantığı T54 cascade ile birebir, inlined terminal listesi `IsTerminalState` ile birebir, AD19b per-tx sırası (freeze→ApplyEmergencyHold→EmergencyHoldAppliedEvent→audit) korunmuş, atomik+idempotent · güvenlik temiz (EMERGENCY_HOLD policy, server-side reason≥10, IDOR yok, 0 yeni bağımlılık, XSS yok).

**Çok-ajanlı bağımsız tarama (7 boyut + adversarial verify, 19 ajan):** Maddi bulgular → AC2-F1 (S3, K9), AC2-F2 (S1, K10), FND-002 (S1, K2). Tümü AD2/AD3 backend projeksiyonu kök nedenli; proje sahibi onayıyla backend DTO-genişletme task'ına deferred. Advisory: durum filtresi kategori-duyarlı değil (AD2 kontratıyla uyumlu), İlişkili Hesaplar/taraf → S20 link (S20=T105), işlem ID detayda yok, `enums.ts` FraudFlagType drift (T54/T82 mirası), AD19d try/catch yok (ölü exception yolu).

**Kabul kriterleri:** 1 ✓ · 2 ~ (hesap-varyant içerik tamlığı K9/K10 deferred) · 3 ✓ · 4 ✓ · 5 ✓.
