# T100a — Admin Flag hesap-varyant DTO genişletme (AD2/AD3, S13/S14)

**Faz:** F5 | **Durum:** ⏳ Devam ediyor | **Tarih:** 2026-06-06

---

## Bağlam

T100 validasyonunda (2026-06-06) hesap-flag içerik tamlığı eksiklikleri tespit edilip — hepsi AD2/AD3 backend projeksiyonu kök nedenli — proje sahibi onayıyla backend DTO-genişletme task'ına ertelenmişti:

- **K9** — Hesap-flag "Aktif İşlemler" (sayı + liste) S14'te hiçbir yüzeyde yok (AD3 projekte etmiyor).
- **K10** — MULTI_ACCOUNT `supportingSignals` (IP/cihaz/source-adres kanıtı) AD3 DTO'da düşüyor (veri `FraudFlag.Details` JSON'unda var, DTO deserialize etmiyor).
- **K2** — Hesap-flag liste kolonları (Sinyal Detayı / İlişkili Hesaplar / Aktif İşlem Sayısı) AD2 projeksiyonunda yok.

T100a bu üçünü **full-stack** kapatır (backend DTO + frontend render). Numara/scope onayı: proje sahibi 2026-06-06 (full-stack + T100a).

## Yapılan İşler

### Backend (Skinora.Fraud)
- **K10** — `MultiAccountFlagDetail`'e `SupportingSignals` + yeni `MultiAccountSupportingSignal` record (type/value/linkedAccounts). `ParseDetail` mevcut deserializasyonla otomatik doldurur; `NormalizeMultiAccount` minimal/legacy `Details` (örn. yalnız `matchType`) için null koleksiyonları boşa çevirir (frontend null-deref koruması — T100'den beri var olan latent NRE riskini de kapatır).
- **K9** — `FraudFlagDetailDto`'ya `ActiveTransactions` (+ `FlagActiveTransactionDto` + `FlagTransactionRole` enum: SELLER/BUYER). `GetDetailAsync` flag'lenen kullanıcının aktif (terminal-olmayan) işlemlerini sorgular; `IsOnHold` + `Role` damgalanır.
- **K2** — `FraudFlagListItemDto`'ya `SignalSummary` / `LinkedAccountCount` / `ActiveTransactionCount` (yalnız ACCOUNT_LEVEL satırlarda dolu, işlem-flag'lerde null). `ListAsync` sayfadaki hesap-flag satırları için `Details`'ı parse eder (`ParseAccountSignal`) + per-user aktif sayımı tek batch sorguyla çeker.
- **Aktif işlem predikatı** AD19d (07 §9.22a) ile birebir: `(seller || buyer) && !IsDeleted && 5 terminal-state hariç` (COMPLETED + 4×CANCELLED_*; FLAGGED aktif). `IsOnHold` hariç tutulmaz — hold'lu işlem hâlâ aktiftir; gösterilen sayı kullanıcının gerçek aktif sayısıdır, Hold ise idempotent olarak yalnız hold'suz alt-kümeyi etkiler.

### Frontend
- `lib/api/admin.ts` — `MultiAccountSupportingSignal`, `FlagActiveTransaction`, `FlagTransactionRole` tipleri + `MultiAccountFlagDetail.supportingSignals` + `AdminFlagDetail.activeTransactions` + `AdminFlagListItem` 3 hesap alanı.
- `FlagDetailView.tsx` — MULTI_ACCOUNT bloğuna `supportingSignals` render (tip etiketi + değer + ilişkili hesaplar); hesap-varyanta "Aktif İşlemler ({count})" bölümü (item/rol/hold rozeti/durum/fiyat/tarih + boş durum).
- `FlagQueueTable.tsx` — ACCOUNT_LEVEL kolon setine `signalColumn` / `linkedColumn` / `activeColumn` (04 §8.2 sırası: Kullanıcı/Tür/Sinyal/İlişkili/Aktif/Tarih/Durum); stale "deferred" yorumu güncellendi.
- i18n 4-locale (+12 leaf/locale): `columns.{signal,linkedAccounts,activeTransactions}` + `detail.{supportingSignals,activeTransactions,noActiveTransactions,onHold}` + `signalType.{IP_ADDRESS,DEVICE_FINGERPRINT,SOURCE_ADDRESS}` + `role.{SELLER,BUYER}`.

### Doküman (kod-doc 1:1)
- `07 §9.3` — `activeTransactions` örnek + not.
- `07 §9.2` — hesap-flag kolonları (`signalSummary`/`linkedAccountCount`/`activeTransactionCount`) notu.
- `11_IMPLEMENTATION_PLAN.md` — T100a tanımı; `IMPLEMENTATION_STATUS.md` — T100a satırı.

## Etkilenen Modüller / Dosyalar
- `backend/src/Modules/Skinora.Fraud/Application/Flags/FraudFlagDtos.cs`
- `backend/src/Modules/Skinora.Fraud/Application/Flags/FraudFlagAdminQueryService.cs`
- `frontend/src/lib/api/admin.ts`, `components/admin/FlagDetailView.tsx`, `components/admin/FlagQueueTable.tsx`
- `frontend/src/i18n/messages/{tr,en,es,zh}.json`
- `Docs/07_API_DESIGN.md`, `Docs/11_IMPLEMENTATION_PLAN.md`, `Docs/IMPLEMENTATION_STATUS.md`
- Test: `backend/tests/Skinora.Fraud.Tests/Integration/FraudFlagAdminQueryServiceTests.cs`, `backend/tests/Skinora.API.Tests/Integration/AdminFlagsEndpointTests.cs`

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | K10 — AD3 MULTI_ACCOUNT `supportingSignals` projekte + S14 render | ✓ | `MultiAccountFlagDetail.SupportingSignals` + `GetDetailAsync_Returns_MultiAccount_SupportingSignals`; FlagDetailView supportingSignals bloğu |
| 2 | K9 — AD3 hesap-flag "Aktif İşlemler" sayı+liste + S14 render | ✓ | `FraudFlagDetailDto.ActiveTransactions` + `GetDetailAsync_Returns_ActiveTransactions_With_Role_And_Hold`; FlagDetailView "Aktif İşlemler" bölümü |
| 3 | K2 — AD2 hesap-flag kolonları (Sinyal/İlişkili/Aktif) + S13 render | ✓ | `FraudFlagListItemDto` 3 alan + `ListAsync_Account_Flag_Populates_Signal_And_ActiveCount` + `ListFlags_AccountFlag_SerializesSignalFields`; FlagQueueTable ACCOUNT_LEVEL kolonları |
| 4 | Aktif işlem tanımı AD19d predikatıyla tutarlı (her iki taraf, 5 terminal hariç, FLAGGED aktif) | ✓ | GetDetailAsync + ListAsync where klozları AD19d `HoldAllUserTransactionsAsync` ile birebir; `ListAsync_Transaction_Flag_Leaves_Account_Fields_Null` + role/hold testi |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Backend build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| dotnet format | ✓ Δ=0 | `dotnet format --verify-no-changes` exit 0 |
| AdminFlagsEndpointTests (SQLite, lokal) | ✓ 11/11 | yeni `ListFlags_AccountFlag_SerializesSignalFields` dahil; minimal-Details 500 regresyonu `NormalizeMultiAccount` ile giderildi |
| FraudFlagAdminQueryServiceTests (SQL Server → CI) | ⏳ CI | +4 yeni test (supportingSignals roundtrip + activeTransactions role/hold + list signal/active + tx-flag null) — lokal Windows Docker yok (T11.3) |
| API.Tests tam suite (SQLite) | ⏳ | regresyon koşusu |
| Frontend tsc / eslint / prettier | ✓ 0 / 0 / clean | T100a dosyaları |
| Frontend next build | ✓ | 26 route PASS |
| i18n locale parity | ✓ 759×4 | 0 missing/extra |

## Altyapı Değişiklikleri
- Migration: **Yok** (yeni alanlar mevcut entity'lerden türetilir/parse edilir).
- Yeni enum: `FlagTransactionRole` (Fraud DTO-only — persiste/Shared değil, JsonStringEnumConverter ile string; Shared EnumTests sayımını etkilemez).
- Config/env: Yok. Docker: Yok. Yeni dış bağımlılık: Yok.

## Commit & PR
- Branch: `task/T100a-flag-dto-expansion`
- Commit: <push sonrası>
- PR: <oluşturulacak>
- CI: ⏳

## Known Limitations / Follow-up
- **K1 (önceki) — "Askıya Al" / hesap askıya alma** hâlâ T105 (S20) kapsamında; T100a yalnız flag içerik projeksiyonunu kapatır.
- **Aktif işlem sayımı `IsOnHold` dahil** — hold'lu işlem aktif sayılır (07 §9.22a tutarlılığı + 04 §8.3 "mevcut aktif işlem sayısı" literal okuması). Hold idempotent olarak yalnız hold'suz alt-kümeyi etkiler; liste/detayda satır bazında hold durumu görünür.
- **`signalSummary` ham değer** (cüzdan adresi / patern — çevrilebilir değil); liste yalnız bunu etiketler, tam IP/cihaz kanıtı AD3 `supportingSignals`'tedir (liste/detay ayrımı). SANCTIONS_MATCH için `signalSummary`/`linkedAccountCount` null (typed payload yok).
- **`activeTransactions` cap yok** — kullanıcı başına aktif işlem zaten `transaction_limits` ile doğal sınırlı; sayfalama gerektirmez.
- **Frontend test runner yok** (F5 plan-onaylı) — UI doğrulaması tsc + eslint + next build + manuel.

## Notlar
- **Working tree (Adım -1):** temiz.
- **Main CI startup (Adım 0):** son 3 run success — `27059637345`, `27059637341` (T100 #148), `26371774696` (T99 #147).
- **Dış varsayımlar (Adım 4):** Yok — yeni paket/dış API yok; mevcut EF Core + `Transaction` entity + STJ yeniden kullanıldı.
- **Mimari karar:** Aktif-işlem EF predikatı Fraud sorgusunda inline yazıldı (AD19d ile birebir) — EF Core custom extension metodunu `.Where()` içinde çeviremediğinden paylaşılan helper yerine inline + cross-ref yorum tercih edildi.
- **T100'den taşınan latent düzeltme:** `NormalizeMultiAccount` minimal MULTI_ACCOUNT `Details` için detay/liste yollarını NRE'den korur (T100'de bir MULTI_ACCOUNT detay testi olmadığından gözlenmemişti).
