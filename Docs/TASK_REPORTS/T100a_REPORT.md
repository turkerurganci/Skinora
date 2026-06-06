# T100a — Admin Flag hesap-varyant DTO genişletme (AD2/AD3, S13/S14)

**Faz:** F5 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-06-06

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

## Doğrulama (Bağımsız Validator — 2026-06-06)

> Validator, yapım raporunu **görmeden** bağımsız verdict oluşturdu (kod + spec + test + producer roundtrip). Sonradan rapor karşılaştırıldı: **tam uyum, 0 uyuşmazlık.**

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** |
| Verdict yöntemi | Bağımsız (rapor görülmeden); 4/4 kabul kriteri kanıtlı ✓ + 3/3 kontrol listesi ✓ |
| Bulgu sayısı (S1/S2/S3) | 0 |
| Düzeltme gerekli mi | Hayır |
| Minor advisory (bloklamaz) | 1 — `FraudFlagAdminQueryServiceTests.SeedTransactionAsync` yorumları "SQLite ignores it" diyor; bu proje (Fraud.Tests) `IntegrationTestBase` → Testcontainers **MsSql** kullanıyor (SQLite yolu yok). Yorum kozmetik olarak yanıltıcı, davranışı etkilemiyor (CHECK'ler hem lokal-Docker hem CI'de geçerli). |

**Bağımsız doğrulama kanıtları:**
- **HARD STOP kapıları:** Working tree temiz (Adım -1); main son 3 CI run success — `27059637341`/`27059637345` (T100 #148) + `26371774696` (T99 #147) (Adım 0); repo memory T100a satırı mevcut (Adım 0b).
- **K10:** `MultiAccountFlagDetail.SupportingSignals` deserialize + `NormalizeMultiAccount` top+nested null-coerce; **producer doğrulandı** — `MultiAccountDetector` (satır 129-142) `Details` JSON'unu `{matchType, matchValue, linkedAccounts[], supportingSignals[{type,value,linkedAccounts[]}]}` şekliyle yazıyor → roundtrip gerçek, K10 premisi ("veri zaten Details'te") doğru.
- **K9:** `GetDetailAsync` aktif-işlem predikatı (satır 332-340) — her iki taraf, `!IsDeleted`, 5 terminal hariç, FLAGGED aktif, **hold dahil** (`!IsOnHold` filtre YOK) + `IsOnHold`/`Role` projekte. Spec 07 §9.3:1751 ile birebir.
- **K2:** `ListAsync` (satır 155-172) K9 ile **özdeş** predikat → `activeTransactionCount` == `activeTransactions.length`; yalnız ACCOUNT_LEVEL doldurulur (satır 197-204), işlem-flag'lerde null.
- **Frontend↔backend kontrat 1:1:** `admin.ts` tipleri + `FlagDetailView` (supportingSignals + "Aktif İşlemler" bloğu) + `FlagQueueTable` (ACCOUNT_LEVEL signal/linked/active kolonları) tutarlı; i18n 4-locale parity (12 yeni leaf × 4 doğrulandı).
- **Test (CI authoritative — lokal Docker yok):** task branch CI run [`27061799552`](https://github.com/turkerurganci/Skinora/actions/runs/27061799552) (HEAD `fa69919`) **11/11 job success**; Integration job: `Skinora.Fraud.Tests` **36 passed** + `Skinora.API.Tests` **398 passed**; tüm run'da **0 Failed**. (Lokal Fraud.Tests çalıştırılamadı — Testcontainers MsSql + Docker kapalı; CI mssql authoritative.)
- **Güvenlik:** Secret sızıntısı yok; yeni endpoint/auth değişikliği yok (mevcut Admin + `VIEW_FLAGS`); `signalSummary`/`supportingSignals` yalnız admin yüzeyi; defensive JSON parse (try/catch); 0 yeni bağımlılık; IDOR yok (`activeTransactions` server-türetimli `flag.UserId`).
- **CI iterasyonu:** 3 erken failure (FreezeHold_Reverse → Hold → Cancel CHECK zinciri) **test seed verisi** kaynaklı, doğru biçimde düzeltildi (invariant'lar tam dolduruldu, test zayıflatılmadı); son 2 run yeşil. 3 BYPASS_LOG kaydı Layer-2 `[ci-failure]` (task branch kendi kırığı fix push'u — meşru).

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Backend build (Release) | ✓ 0W/0E | `dotnet build Skinora.sln -c Release` |
| dotnet format | ✓ Δ=0 | `dotnet format --verify-no-changes` exit 0 |
| AdminFlagsEndpointTests (SQLite, lokal) | ✓ 12/12 | `ListFlags_AccountFlag_SerializesSignalFields` + `GetFlag_MultiAccount_SerializesSupportingSignals`; minimal-Details 500 regresyonu `NormalizeMultiAccount` ile giderildi |
| FraudFlagAdminQueryServiceTests (SQL Server, CI) | ✓ 11/11 | +5 yeni (supportingSignals roundtrip + null-coercion top/nested + activeTransactions role/hold/FLAGGED/5-terminal + list signal/active-held + tx-flag null) — CI run `27061575158` |
| API.Tests tam suite (SQLite, lokal) | ✓ 414/414 | 27 Testcontainers/SQL-Server testi lokal Docker yok → CI'de yeşil (T11.3) |
| Frontend tsc / eslint / prettier | ✓ 0 / 0 / clean | T100a dosyaları |
| Frontend next build | ✓ | 26 route PASS |
| i18n locale parity | ✓ 759×4 | 0 missing/extra |

## Altyapı Değişiklikleri
- Migration: **Yok** (yeni alanlar mevcut entity'lerden türetilir/parse edilir).
- Yeni enum: `FlagTransactionRole` (Fraud DTO-only — persiste/Shared değil, JsonStringEnumConverter ile string; Shared EnumTests sayımını etkilemez).
- Config/env: Yok. Docker: Yok. Yeni dış bağımlılık: Yok.

## Commit & PR
- Branch: `task/T100a-flag-dto-expansion` (HEAD `c409c2c`)
- Ana commit: `a8d8eeb` (yapım) + `dabe2bd`/`82d9600` (mssql CK test-seed fix) + `c409c2c` (adversarial review fix)
- PR: [#150](https://github.com/turkerurganci/Skinora/pull/150)
- CI: ✓ **PASS** — run [`27061575158`](https://github.com/turkerurganci/Skinora/actions/runs/27061575158) (HEAD `c409c2c`) **10/10 job success** (Lint/Build/Unit/Integration/Contract/Migration/Docker×2/Gate). FraudFlagAdminQueryServiceTests 11/11 (5 yeni dahil) + AdminFlagsEndpointTests 12/12 gerçek SQL Server'da yeşil.
- **Bypass:** push'lar Layer 2 (önceki run failure) nedeniyle `SKINORA_ALLOW_DIRECT_PUSH=1` ile geçildi (kendi kırığının fix iterasyonu) — `Docs/BYPASS_LOG.md`'ye 3 `ci-failure` kaydı.

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
- **T100'den taşınan latent düzeltme:** `NormalizeMultiAccount` minimal MULTI_ACCOUNT `Details` için detay/liste yollarını NRE'den korur (T100'de bir MULTI_ACCOUNT detay testi olmadığından gözlenmemişti). Adversarial review sonrası nested `supportingSignal.linkedAccounts` da boşa çevrilir hale getirildi (FE-1).

## CI İterasyonu (SQL Server CHECK constraint zinciri)

Lokal Windows'ta Docker olmadığından (T11.3) `Transaction` CHECK constraint'leri yalnız CI'nin shared mssql'inde enforce edilir (SQLite yok sayar). K9 testi held + cancelled işlem seed'lediğinden, her tur bir sonraki eksik invariant'ı ortaya çıkardı — **hepsi tek testin seed verisi, production kodu değil** (K10/K2'nin 3 ana integration testi ilk turdan beri yeşildi):

| Run | HEAD | Kırılan CHECK | Düzeltme |
|---|---|---|---|
| [`27060695530`](https://github.com/turkerurganci/Skinora/actions/runs/27060695530) | `ed63f07` | `CK_Transactions_FreezeHold_Reverse` | held seed'e freeze trio (TimeoutFrozenAt + EMERGENCY_HOLD + RemainingSeconds) |
| [`27060958264`](https://github.com/turkerurganci/Skinora/actions/runs/27060958264) | `82d9600` | `CK_Transactions_Hold` | held seed'e EmergencyHold trio (At + Reason + ByAdminId, FK→user) |
| [`27061212260`](https://github.com/turkerurganci/Skinora/actions/runs/27061212260) | `dabe2bd` | `CK_Transactions_Cancel` | CANCELLED_* seed'e cancel trio (CancelledBy + Reason + At) |
| [`27061575158`](https://github.com/turkerurganci/Skinora/actions/runs/27061575158) | `c409c2c` | — | **✓ 10/10 PASS** (8 CHECK'in hepsi sağlandı) |

Ders: `Transaction` entity'sini seed eden integration testler 8 CHECK constraint'i (Cancel/Hold/Freeze×4/BuyerMethod×2) bilmeli; SQLite fixture bunları yakalamaz, yalnız CI mssql yakalar.

## Çok-Ajanlı Adversarial Review (ultracode)

Bağımsız validator öncesi proaktif 5-boyut (backend correctness / spec-conformance / security / frontend / test adequacy) + adversarial verify (21 ajan): **16 bulgu → 10 gerçek** (verify refute-default). Ele alınanlar:
- **FE-1 (S2):** `NormalizeMultiAccount` nested `supportingSignal.linkedAccounts` null-coercion eksikti → recursive coerce eklendi (minimal/legacy JSON frontend null-deref koruması).
- **BK-1/BK-2/T100a-10 (S2):** K9/K2 "AD19d ile aynı predikat" yorumları yanıltıcıydı (AD19d idempotency için `!IsOnHold` filtreler; K9/K2 held dahil eder) → yorumlar düzeltildi.
- **T100a-2/3, BK-3, BK-4/T100a-4, T100a-7 (test gap):** K9 testi FLAGGED + 5 terminal; K2 held-count ayırt edici; null-coercion (top+nested); AD3 supportingSignals wire testi eklendi.
- **Reddedilenler (verify ile):** T100a-1/5/6/8/9 (test-enhancement, defect değil) + BE-1 (yanlış mekanizma — STJ eksik üye için JsonException atmaz, null atar; gerçek konu FE-1'de yakalandı). Güvenlik boyutu 0 gerçek bulgu (IDOR yok — activeTransactions flag.UserId server-türetimli; signalSummary ham adres/patern, XSS yok; auth değişmedi).
