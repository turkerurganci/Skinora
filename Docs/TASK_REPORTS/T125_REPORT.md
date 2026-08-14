# T125 — DeliveryVerificationService + DeliveryEvidence

**Faz:** F7 (P2P geçişi, P3 "Yeni ileri yol") | **Durum:** ✓ Tamamlandı (doğrulama ✓ PASS) | **Tarih:** 2026-08-14

---

## Yapılan İşler

**Kısa özet:** 02 §9.2 teslimat kanıt motoru yazıldı — **saf**, yan etkisiz, polling'e hazır. Envanter kanıtına dayalı otomatik para bırakma bir **launch kapısının** arkasına alındı ve kapının beslendiği kanıt kaydı (append-only tablo) kuruldu. Yolu besleyen iki sidecar açığı kapatıldı: `asset_properties` atılıyordu, kısa (eksik sayfalanmış) envanter okuması sessizce "daha az item" olarak geçiyordu.

### Backend — kanıt motoru (`Skinora.Transactions/Application/Delivery/`)
- **`IDeliveryVerificationService.VerifyAsync(transaction, freshness, ct)`** → `DeliveryVerificationResult`. Sözleşme gereği **hiçbir şey yazmaz**: entity mutasyonu yok, `SaveChanges` yok, state machine tetiklemesi yok, outbox yok. 02 §9.2 kuralların üç ayrı anda çalışmasını şart koşuyor (alıcı onayı · dispute · timeout öncesi) ve ileride bir job bunları periyodik çalıştırabilir — gözlem anında mutasyon yapan bir motor, kaç kez çağrıldığına göre farklı cevap verirdi.
- **`DeliveryVerdict`** beş değerli: `Delivered` · `InventoryEvidencePendingReview` · `MisdeliverySignature` · `NoMovement` · `Inconclusive`. Kritik ayrım son ikisi arasında değil, **`MisdeliverySignature` ↔ `Inconclusive`** arasında: ikisi de teslimat üretmez ama ilki bir satıcı hakkında olumlu bir bulgu, ikincisi platformun bakamadığının itirafıdır.
- **Alıcı onayı kısa devresi:** `BUYER_CONFIRMED` zaten kayıtlıysa Steam hiç okunmaz. Onay alıcının kendi aleyhinedir; envanter okuması yalnız daha zayıf bir sinyalle daha güçlü olanına itiraz edebilirdi (ve poll başına iki rate-limited çağrı harcardı).
- **Kilit durumu hiç okunmuyor.** Ne `market_tradable_restriction` (T122-B8: `tradable: 1` olan item'da da 7 geliyor), ne `IsTradeable` (runbook §6: alan sınıf düzeyinde ve anonim görünümde bitiş tarihi taşımıyor). T122 kilitli bir item'ın anonim imzasını **ölçemedi**; ölçülmemiş bir sinyal tahmin edilerek para hareketine bağlanmak yerine tasarımdan dışlandı.

### Backend — launch kapısı (AC6)
- **`delivery.inventory_evidence_auto_release_enabled`** (bool, seed **`false`**, seed index 60). Kapalıyken `SELLER_ASSET_GONE ∧ INVENTORY_DELTA` kanıtı üretilir, kaydedilir ve raporlanır ama **parayı tek başına serbest bırakmaz** — işlem iptal de edilmez. Alıcı onayı yolu ve yanlış-teslimat yükseltmesi kapıdan **etkilenmez** (ikisi de platformun çıkarımı değil).
- `Default(...)` ile seed edilir → `IsConfigured = true` → `SettingsBootstrapService` bu satırı env'den **asla** doldurmaz (06 §8.9). Bilinçli: kapı bir deploy değişkeni değil, kanıtı okumuş bir insanın kararıdır.
- **`DeliveryEvidenceCapture`** (append-only, `IAppendOnly`) + `DeliveryEvidenceCaptureRecorder` (statik, çağıranın `SaveChanges`'ine katılır — `TransactionHistoryRecorder` kalıbı). Recorder ayrı tutuldu ki motorun saflığı bozulmasın.
- **DEPLOY_RUNBOOK §H** — launch checklist: kapı kapalıyken ne olur/olmaz tablosu, kapıyı açma adımları (SQL sorgusu + `Payload`'dan B1/B2/B3'ün nasıl cevaplanacağı), açılmadan yapılmaması gerekenler.

### Sidecar (`sidecar-steam`)
- **`asset_properties` taşınıyor** — `Pattern Template` / `Wear Rating` / `Item Certificate` / `Name Tag` / `Charm Template`. Kütüphane bunları zaten `CEconItem`'a birleştiriyordu, mapper atıyordu. Değerler **string** kalır: `Wear Rating` 19 anlamlı basamaklı bir ondalık ve inceleyici bu değeri iki envanter arasında karşılaştıracak. Boşken alan **hiç yazılmaz** (T122: 199 asset'in 91'inde var) → 07 §6.1 şekli çoğunluk için değişmedi.
- **Kısa-okuma guard'ı** — Steam'in `total_inventory_count`'u (kütüphanenin 4. callback argümanı, bugüne kadar atılıyordu) birleşen asset sayısıyla karşılaştırılır; **liste eksikse** okuma `UNAVAILABLE` döner ve **cache'e yazılmaz**. Yalnız `<` yönü kontrol edilir (currency item'lar listede yok, eşitlik şart değil).
- `market_tradable_restriction` bilinçli olarak **mapping'e alınmadı**; gerekçe kodda yorum olarak sabitlendi.

### Frontend
- `settingsCatalog.ts`'e `delivery_verification` kategorisi → "Teslimat Doğrulama" grubu (operational bölümü; 04 §8.6 v3.0 öncesi yazıldığı için teslimat grubunu hiç tanımlamamıştı) + 4 dil i18n.

## Etkilenen Modüller / Dosyalar

**Yeni (backend):**
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryVerificationService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryVerificationService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryVerificationResult.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryEvidenceCaptureRecorder.cs`
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/DeliveryEvidenceCapture.cs`
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/DeliveryEvidenceCaptureConfiguration.cs`
- `backend/src/Skinora.Shared/Persistence/Migrations/20260814182849_T125_DeliveryEvidenceCapture.cs` (+ Designer; snapshot yeniden üretildi)
- `backend/tests/Skinora.Transactions.Tests/Integration/Delivery/DeliveryVerificationServiceTests.cs`

**Değişen (backend):**
- `Application/Steam/ISteamInventoryReader.cs` — `InventoryAssetProperty`, `InventoryClassAsset` eklendi; `InventoryClassBaselineResult.Assets` (AssetIds türetilmiş); `InventoryItemSnapshot.AssetProperties` (init-only)
- `Skinora.Steam/.../SteamInventoryDtos.cs`, `HttpSteamSidecarInventoryClient.cs`, `SidecarSteamInventoryReader.cs` — `assetProperties` uçtan uca
- `Skinora.Platform/.../SystemSettingSeed.cs` (index 60), `SystemSettingsCatalog.cs` (`delivery_verification`)
- `Skinora.API/Configuration/TransactionsModule.cs` — DI
- `tests/Skinora.Platform.Tests/Integration/SeedDataTests.cs` (59→60, configured listesi), `tests/.../TestSetupHelpers.cs`, `tests/Skinora.API.Tests/.../TransactionLifecycleEndpointTests.cs` (fake reader `InventoryClassAsset`'e taşındı)

**Sidecar:** `src/trade/InventoryService.ts` · `src/trade/InventoryService.test.ts` · `src/trade/SteamInventoryReadContract.test.ts` (yeni) · `src/api/routes.test.ts`

**Frontend:** `src/lib/admin/settingsCatalog.ts` · `src/i18n/messages/{en,tr,es,zh}.json`

**Dokümanlar:** `02 §9.2` (launch kapısı notu) · `04 §8.6` (yeni parametre grubu) · `06 §3.5a` (yeni entity) + `§8` (yeni ayar satırı) + traceability · `07 §9.8` (kategori + anahtar sayısı) · `DEPLOY_RUNBOOK` §0/§C/§H · `INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md` §7 (kapanış kapısı → UYGULANDI + sapma)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 02 §9.2 tuzak matrisinin HER SATIRI için bir test | ✓ | 13 satırlık matris (aşağıda) → `DeliveryVerificationServiceTests` `Row01`–`Row13` (+ `Row06b`, `Row07` ve `Row12` Theory ile iki vaka). Matris kapsam netleştirmede proje sahibine sunuldu. `dotnet test --filter DeliveryVerificationServiceTests` → **36/36 passed** |
| 2 | Servis saf/yan etkisiz kanıt değerlendirmesi yapıyor (polling'e hazır) | ✓ | `Verification_Writes_Nothing_And_Repeats_Identically` — iki ardışık tur aynı verdict'i verir; `transaction.DeliveryEvidence` bellekte ve DB'de `NONE` kalır, `DeliveredBuyerAssetId` NULL, `DeliveryEvidenceCaptures` boş. Yazma işi ayrı `DeliveryEvidenceCaptureRecorder`'da. `Callers_Choose_The_Read_Freshness` — tazelik parametre, gömülü değil |
| 3 | `market_tradable_restriction` kanıt olarak KULLANILMIYOR + bir test bu alanı okumanın yanlış sonuç verdiğini sabitliyor | ✓ | `SteamInventoryReadContract.test.ts` → *"the measured capture pairs tradable:1 with market_tradable_restriction:7"* ve *"reading the restriction as a lock misclassifies a free item"* (`lockedPerRestrictionField = true` ↔ `actuallyTradable = true` — ikisi çelişiyor). Kaynak: repodaki `Docs/INTEGRATION_RUNBOOKS/data/T122_owner_capture.json`, kopya değil kanonik dosya. Ayrıca *"the mapped wire shape drops the restriction field"* |
| 4 | Kanıt değerlendirmesi item'ın KİLİT DURUMUNA dayanmıyor | ✓ | `Row12_Verdict_Is_Independent_Of_Item_Lock_State` — `IsTradeable` true/false, **aynı** verdict. Kodda `DeliveryVerificationService` `IsTradeable`'ı hiç okumaz (`grep -n "IsTradeable" Application/Delivery/` → boş) |
| 5 | Sayfalama tüketicisi "devam yok"u `more_items`'ın YOKLUĞUNDAN anlıyor | ✓ | Doğrulama: `steamcommunity/components/users.js:668` → `if (body.more_items)` (truthiness). Sabitleme: `SteamInventoryReadContract.test.ts` gerçek kütüphaneyi `httpRequest` stub'ıyla sürer — 1. sayfa `more_items:1`, son sayfa **anahtarsız** → döngü doğru sonlanır, iki sayfa birleşir, `last_assetid` cursor olarak izlenir. Ek: `total_inventory_count` kısa-okuma guard'ı (5 test) |
| 6 | LAUNCH KAPISI: ilk N gerçek teslimatta ham yanıt saklanıyor + insan incelemesi olmadan otomatik para bırakma açılmıyor; kapı DEPLOY_RUNBOOK launch checklist'ine bağlı | ~ | **Kapı tam:** ayar (seed `false`) + `InventoryEvidencePendingReview` verdict'i + append-only `DeliveryEvidenceCaptures` + **DEPLOY_RUNBOOK §H** (açma prosedürü). Testler: `Gate_Closed_Holds_Inventory_Evidence_For_Review`, `Gate_Defaults_To_Closed_When_The_Setting_Is_Missing`, `Gate_Does_Not_Apply_To_Buyer_Confirmation`, `Gate_Open_Releases_On_Inventory_Evidence`, `Gate_Does_Not_Suppress_The_Misdelivery_Escalation`. **Kısmi olan taraf:** "**ham yanıt**" saklanmıyor — saklanamıyor (aşağıdaki kırık varsayım). Yerine işlemin kendi item sınıfı için iki taraflı gözlem + `asset_properties` + zaman damgaları saklanıyor; B1/B2/B3 üçü de bu kayıttan cevaplanabiliyor (`Capture_Records_Both_Sides_And_The_Latency_Timestamps`) |

### AC1 — kullanılan tuzak matrisi (02 §9.2 + 06 §2.24'ten türetildi)

| # | Vaka | Beklenen | Test |
|---|---|---|---|
| 1 | `BUYER_CONFIRMED` | Teslim (tek başına yeter) | `Row01` |
| 2 | `SELLER_ASSET_GONE ∧ INVENTORY_DELTA` | Teslim | `Row02` |
| 3 | Yalnız alıcı sayımı arttı | Teslim **değil** | `Row03` |
| 4 | Yalnız satıcıdan düştü | Yanlış-teslimat imzası → dispute | `Row04` |
| 5 | İki tarafta hareket yok | Teslim değil | `Row05` |
| 6 | Alıcı envanteri okunamıyor | Sonuçsuz (delta yok ≠ okunamadı) | `Row06` ×2 |
| 6b | Satıcıdan düştü **+** alıcı okunamıyor | Sonuçsuz — **misdelivery DEĞİL** | `Row06b` ×2 |
| 7 | Satıcı envanteri okunamıyor | Sonuçsuz (asset gitti sayılmaz) | `Row07` ×2 |
| 8 | Baseline hiç alınmamış | Envanter yolu kapalı; alıcı hiç okunmaz | `Row08` |
| 9 | Alıcıda o sınıftan zaten kopya var | Sayım deltası (varlık değil) | `Row09` |
| 10 | Sayım azaldı/değişmedi | Delta yok | `Row10` ×2 |
| 11 | Alıcı tarafında asset ID | Hiç kullanılmaz (rotasyon) | `Row11` |
| 12 | Kilit/`tradable`/`restriction` | Kanıt girdisi değil | `Row12` ×2 |
| 13 | Aşınma/desen farkı | Otomatik kapsam dışı (T130) | `Row13` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend — T125 | ✓ 36/36 | `dotnet test tests/Skinora.Transactions.Tests --filter "FullyQualifiedName~DeliveryVerificationServiceTests"` |
| Backend — tam suite | ✓ 2597/2597 | `dotnet test` (13 assembly). İlk koşuda `Skinora.Platform.Tests` 2 FAIL (seed 59→60 sayımı + configured anahtar listesi) → testler güncellendi, tekrar koşuldu: **189/189** |
| Sidecar steam | ✓ 204/204 | `npx vitest run` (12 dosya). Yeni: `SteamInventoryReadContract.test.ts` 8 test + `InventoryService.test.ts`'e 8 test |
| Sidecar lint/format | ✓ | `npx eslint src/` temiz · `npx prettier --check --end-of-line auto "src/**/*.ts"` → *All matched files use Prettier code style* |
| Frontend | ✓ | `tsc --noEmit` 0 · `eslint` 0 · `i18n:check` → 4 dil × **1301** anahtar, aynı küme · `vitest run` **33/33** |
| Backend build | ✓ | `dotnet build` — 0 warning, 0 error |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** |
| Doğrulama tarihi | 2026-08-14 (ayrı chat, `/validate T125`) |
| Doğrulanan commit | `b71760f` (branch HEAD) |
| Bulgu sayısı | 3 (hiçbiri bloke edici değil — üçü de takip maddesi) |
| Düzeltme gerekli mi | Hayır (merge öncesi). F1–F3 merge sonrası chore PR'ına |

### Validator kabul kriteri tablosu (bağımsız)

Validator matrisi **yapım raporunu görmeden**, 02 §9.2 + 06 §2.24'ten kendi türetti; sonuç yapım chat'inin 13 satırıyla birebir örtüştü (eksik/fazla satır yok).

| # | Kriter | Validator | Bağımsız kanıt |
|---|---|---|---|
| 1 | 02 §9.2 tuzak matrisinin her satırı için bir test | ✓ | `Row01`–`Row13` + `Row06b`; `Row06/06b/07/10/12` Theory ile çift vaka. `--filter DeliveryVerificationServiceTests` → **36/36**. Testler gerçek DB'ye karşı `IntegrationTestBase` + `FakeSteamInventoryReader` ile koşuyor, saf birim mock değil |
| 2 | Servis saf / yan etkisiz (polling'e hazır) | ✓ | Kod: `VerifyAsync` yalnız `AsNoTracking()` okur (`SystemSetting`, `User`), `transaction`'a yazmaz, `SaveChanges` yok. Yazma ayrı `DeliveryEvidenceCaptureRecorder`'da. Test: `Verification_Writes_Nothing_And_Repeats_Identically` (bellek + DB), `Callers_Choose_The_Read_Freshness` |
| 3 | `market_tradable_restriction` kanıt değil + tuzağı sabitleyen test | ✓ | `grep -rn "market_tradable_restriction" backend/src` → yalnız `DeliveryVerificationService` XML yorumu. Sidecar `mapItem` alanı bilinçli olarak map'lemiyor. `SteamInventoryReadContract.test.ts` kanonik `T122_owner_capture.json` üzerinden `tradable:1` ↔ `restriction:7` çelişkisini sabitliyor |
| 4 | Kanıt değerlendirmesi kilit durumuna dayanmıyor | ✓ | Yapısal: `DeliveryVerificationService.cs` içinde `IsTradeable` referansı **yok**. Davranışsal: `Row12` iki vaka → aynı verdict. *(Not: `Row12` kilit bayrağını yalnız satıcı tarafında değiştiriyor; alıcıya ulaşan kilitli item vakası yapısal yoklukla kapanıyor)* |
| 5 | Sayfalama "devam yok"u `more_items` **yokluğundan** anlıyor | ✓ | Bağımsız teyit: `sidecar-steam/node_modules/steamcommunity/components/users.js:668` → `if (body.more_items)`. Test gerçek kütüphaneyi `httpRequest` seam'iyle sürüyor (mock değil) — son sayfa anahtarsız → 2 istek, 3 asset. Ek: kısa-okuma guard'ı 5 test |
| 6 | Launch kapısı: ham yanıt saklanıyor + insan incelemesi olmadan otomatik bırakma açılmıyor + runbook'a bağlı | ~ | **Para tarafı tam:** seed `false` (migration `InsertData`, `IsConfigured=true`) · `SettingsBootstrapService` `.Where(s => !s.IsConfigured)` ⇒ env ile açılamaz (bağımsız kodda teyit) · fail-closed (`Gate_Defaults_To_Closed_When_The_Setting_Is_Missing`) · DEPLOY_RUNBOOK §H eksiksiz. **Kısmi:** "ham yanıt" saklanmıyor; yerine sınıf-kapsamlı gözlem. B1/B2/B3'ün üçü de kayıttan cevaplanabildiği için amaç karşılanıyor — kabul edilebilir minor. Gerekçe metni için bkz. F1 |

### Doğrulama kontrol listesi

- [x] Adım -1 — working tree temiz (`git status --short` boş)
- [x] Adım 0 — main CI son 3 run: `31825580317` ✓ · `31825580363` ✓ · `31824134702` ✓
- [x] Adım 0b — repo memory `.claude/memory/MEMORY.md` T125 satırı mevcut
- [x] Adım 8a — task branch CI: HEAD `b71760f` run [`31832714884`](https://github.com/turkerurganci/Skinora/actions/runs/31832714884) `success`, **CI Gate `success`**, 12 bloke edici job yeşil
- [x] Advisory E2E kırılması T125 kaynaklı **değil** — bağımsız teyit: aynı 8 leg T125 **öncesinde**, main run [`31824134685`](https://github.com/turkerurganci/Skinora/actions/runs/31824134685) (T124 merge) içinde de kırmızıydı; `--log-failed` imzası yalnız `Invalid object name 'PlatformSteamBots'`, T125 yüzeylerinden (`DeliveryVerification*` · `DeliveryEvidenceCapture` · `assetProperties` · `total_inventory_count` · `InventoryShortRead` · yeni ayar anahtarı) **0 iz**
- [x] Doküman uyumu — 06 §3.5a kolonları ↔ EF config ↔ migration birebir; `DeliveryVerdict` 5 değeri ↔ `nvarchar(40)`; 06 §2.24 geçiş kuralı ↔ `DeliveryEvidenceExtensions`; 02 §9.2 diff'i **tamamen ek** (kanıt kuralları değişmedi — spec koda uydurulmamış)
- [x] Sidecar wire sözleşmesi — `assetProperties`/`propertyId`/`intValue`/`floatValue`/`stringValue` isimleri sidecar ↔ backend DTO'da birebir; alan nullable ⇒ eski sidecar ile geriye uyumlu

### Mini güvenlik kontrolü

| Alan | Sonuç |
|---|---|
| Secret sızıntısı | Temiz — diff'te credential/anahtar yok |
| Auth / authorization | Temiz — yeni endpoint yok; ayar mevcut admin `/admin/settings` yüzeyinden yönetiliyor |
| Input validation | Temiz — `SystemSettingsValidator` `bool` dalı (`bool.TryParse`) yeni anahtarı kapsıyor; motor tarafında parse edilemeyen değer **fail-closed** |
| Yeni dış bağımlılık | Yok — `*.csproj` / `package.json` / lock dosyalarında değişiklik yok |
| Kişisel veri | Kanıt kaydı bilinçli olarak işlemin **kendi item sınıfıyla** sınırlı; envanter dökümü değil (runbook §8) |

### Bulgular (hiçbiri bloke edici değil)

| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| F1 | S1 Sapma (doküman) | "**ham JSON hiçbir noktada dışarı verilmiyor**" ifadesi fazla kesin. `steamcommunity` ham gövdeyi `SteamCommunity.prototype.httpRequest` seam'inden geçiriyor ve **bu görevin kendi contract testi** (`SteamInventoryReadContract.test.ts:86`) tam olarak o seam'i değiştirerek sayfa gövdeleriyle çalışıyor — yani ham yakalama teknik olarak *mümkündü*, yalnız kütüphanenin **public API'ı** vermiyor. Sapmanın doğru ve zaten yeterli gerekçesi kapsam/kişisel-veri minimizasyonudur (runbook §8). Düzeltme olmazsa T127/T130 yapımcısı "imkânsız" diye okuyup yanlış karar verebilir | `Docs/INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md` §7 · bu rapor "Dış Varsayımlar" #7 |
| F2 | S1 Sapma (kod hijyeni) | `InventoryShortReadError` JSDoc bloğu `InventoryService` sınıfının kendi JSDoc'unun **hemen üstüne** kopyalanmış (satır 183–199); aynı blok 343–362'de gerçek sınıfa bağlı olarak zaten var. Yetim/çift kayıt — davranışa etkisi yok, sınıfın dokümantasyonunu okuyan için yanıltıcı | `sidecar-steam/src/trade/InventoryService.ts:183-199` |
| F3 | Takip (ileriye dönük risk) | **Launch kapısı yalnız servis katmanında.** State machine guard'ı `HasDeliveryEvidence()` (`TransactionStateMachine.cs:330`) sadece `IsSufficientForDelivery() && DeliveryVerifiedAt.HasValue` bakar — kapıdan haberi yok. `DeliveryVerificationResult.Evidence` XML'i ise çağırana "bu değeri `Transaction.DeliveryEvidence`'a persist et" diyor. Gated bir turda çağıran hem Evidence'ı yazar hem `DeliveryVerifiedAt`'i damgalarsa kapı **sessizce atlanır**. **Bugün canlı bypass YOK** (bağımsız teyit: `grep -rn "DeliveryEvidence =\|DeliveryVerifiedAt =" backend/src` → üretimde yazan kod yok; tek tüketici `DeliveryDisputeAutoChecker` para hareketi yapmıyor). Kapıyı fiilen tutan alan `DeliveryVerifiedAt`'tir ama bu invariant hiçbir yerde yazılı değil. Öneri: T126/T127 kabul kriterlerine "gated turda `DeliveryVerifiedAt` damgalanmaz" maddesi + `IDeliveryVerificationService` XML'ine not | `Docs/11_IMPLEMENTATION_PLAN.md` (T126/T127) · `IDeliveryVerificationService.cs` |

### Validator test koşumu (bağımsız, commit `b71760f`)

| Tür | Sonuç | Komut |
|---|---|---|
| Backend build | ✓ 0 error | `dotnet build Skinora.sln -c Release` |
| Backend Unit | ✓ **1379/1379** (11 assembly) | `dotnet test --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Backend Integration | ✓ **1209/1209** (10 assembly) | `dotnet test --filter "FullyQualifiedName~.Integration"` |
| Backend Contract | ✓ **9/9** | `dotnet test --filter "FullyQualifiedName~.Contract"` |
| Backend toplam | ✓ **2597/2597** | (raporun sayısı bağımsız olarak teyit edildi) |
| Backend — T125 odaklı | ✓ **36/36** | `--filter "FullyQualifiedName~DeliveryVerificationServiceTests"` |
| Sidecar steam | ✓ **204/204** (12 dosya) | `npm test` |
| Frontend | ✓ tsc 0 · eslint 0 · vitest **33/33** · i18n **1301×4** | `npx tsc --noEmit` · `npm run lint` · `npx vitest run` |

> **Koşum notu (şeffaflık):** İlk birleşik koşuda `Skinora.Transactions.Tests` integration 2 FAIL verdi (418/420, süre 7 dk 11 sn) — o sırada frontend vitest suite'i (381 sn environment) aynı makinede koşuyordu. İki bağımsız temiz yeniden koşuda **yeniden üretilmedi**: izole proje 420/420 (3 dk), solution geneli 1209/1209 (5 dk 51 sn). CI aynı commit'te Integration leg `success`. Eşzamanlı yük kaynaklı, yeniden üretilemeyen artefakt olarak sınıflandırıldı; ilk koşuda test **isimleri yakalanmadığı** için hangi iki test olduğu kayıt altına alınamadı.

## Altyapı Değişiklikleri

- **Migration:** Var — `20260814182849_T125_DeliveryEvidenceCapture`. (1) `DeliveryEvidenceCaptures` tablosu: long IDENTITY PK, FK → Transactions (NO ACTION), 2 index (`TransactionId`, `AutoReleaseGated`). (2) `SystemSettings`'e 1 `InsertData` (`delivery.inventory_evidence_auto_release_enabled` = `false`, Id `0aa51010-…-00000000003c`). `Down` tablo drop + satır delete. **Not:** `Evidence` kolonu bilinçli olarak `int` — global `EnumToStringConverter` bir `[Flags]` enum'u virgüllü isim listesi olarak yazardı (`Transaction.DeliveryEvidence` ile aynı gerekçe); config'de `HasConversion<int>()`.
- **Config/env değişikliği:** Yok. Yeni ayar `SKINORA_SETTING_*` ile **set edilemez** (Default ⇒ IsConfigured=true); §A'daki 19 zorunlu env sayısı değişmedi.
- **Docker değişikliği:** Yok.

## Dış Varsayımlar (task.md Adım 4)

| # | Varsayım | Kanıt | Sonuç |
|---|---|---|---|
| 1 | `steamcommunity` sayfalama döngüsü `more_items` yokluğunu doğru okuyor | `node_modules/steamcommunity/components/users.js:668` → `if (body.more_items)` | ✓ |
| 2 | `asset_properties` bize ulaşıyor | `users.js:650-657` lookup + `classes/CEconItem.js:89-91` | ✓ (mapper atıyordu — bu görevde bağlandı) |
| 3 | `total_inventory_count` erişilebilir | `users.js:669` → `callback(null, inventory, currency, body.total_inventory_count)` | ✓ (atılıyordu — bu görevde tüketildi) |
| 4 | Kanıt için gereken iki okuma portta var | `ISteamInventoryReader.GetItemAsync` + `CaptureClassBaselineAsync` | ✓ yeni uç gerekmedi |
| 5 | AC3 fixture'ı repoda | `Docs/INTEGRATION_RUNBOOKS/data/T122_owner_capture.json` — `tradable:1` + `market_tradable_restriction:7` aynı kayıtta | ✓ |
| 6 | `bool` SystemSetting yolu açık | `open_link_enabled`, `platform.maintenance.active` aynı kalıpta | ✓ |
| 7 | **Steam'in ham gövdesi saklanabilir** (AC6 metni) | `steamcommunity` sayfalama + `assets × descriptions × asset_properties` birleştirmesini **kendi içinde** yapıp `CEconItem[]` döndürüyor; ham JSON hiçbir noktada dışarı verilmiyor | ✗ **KIRIK** — aşağıya bakın |

**Kırık varsayım (#7) — proje sahibine sunuldu, karar alındı (2026-08-14):** Erişilebilir en zengin katman `CEconItem`'dır. Üç seçenek sunuldu: (a) `CEconItem` seviyesi + `asset_properties`, (b) mevcut port çıktısıyla yetin (B3 açık kalır), (c) kütüphaneyi bypass edip ham HTTP çağrısı yaz (sayfalama + merge yeniden yazılır, iki tarafın **tüm** envanteri kişisel veri olarak DB'ye iner). **Seçilen: (a).** Sonuç: B1 (`ObservedAt` − `PaymentReceivedAt`), B2 (`SellerItemAssetId` ↔ `NewAssetIds`), B3 (`Item Certificate` iki tarafta) üçü de kayıttan cevaplanabiliyor; kaybedilen tek şey işlemle ilgisiz asset'lerin gövdesi — ki o zaten üçüncü şahıs verisi (T122 runbook §8). Sapma runbook §7'ye yazıldı.

## Diğer Proje Sahibi Kararları (2026-08-14)

| Konu | Seçenekler | Karar |
|---|---|---|
| AC6 kanıt saklama | (a) append-only tablo + gate ayarı · (b) yalnız structured log · (c) T126/T127'ye ertele | **(a)** — log retention'a bağlı bir "saklanıyor" zayıf kalırdı; erteleme AC6'yı bu görevde karşılamazdı |
| Kanıt zenginliği | yukarıdaki #7 | **(a)** CEconItem + `asset_properties` |
| AC5 sayfalama | (a) regresyon testi + kısa-okuma guard'ı · (b) yalnız test · (c) yalnız doküman | **(a)** — kırpılmış okuma tam olarak sahte "delta yok" üretip haksız iade doğuruyordu |

## Known Limitations / Follow-up

- **Motorun üretimde çağıranı yok.** T125 kanıt kurallarını ve kapıyı teslim eder; çağrı yerleri **T126** (`POST /transactions/:id/confirm-receipt`), **T127** (scanner'ın timeout öncesi doğrulama turu) ve **T130** (dispute auto-check). `DeliveryEvidenceCaptureRecorder` de o görevlerde bağlanır — bu görevde mekanizma + gate hazır, tüketici yok. Bilinçli: çağrı yerleri başka görevlerin kapsamı (bundled-PR yasağı).
- **N sayısı koda gömülü değil.** DEPLOY_RUNBOOK §H "≥ 5 ayrı işlem" öneriyor ama kapı sayaç tutmuyor: kapı kapalıyken **her** nitelikli tur kaydedilir, açma kararı insana ait. İkinci bir "örneklem boyutu" ayarı eklemek yerine bu tercih edildi.
- **`Verdict` string olarak saklanıyor** (`nvarchar(40)`). Enum ordinal'ı değil ad — satırlar enum sırasından uzun yaşayacak ve SQL client'ta insan okuyacak.
- **07 §9.8 anahtar sayısı düzeltildi:** satır "58 anahtar" diyordu, gerçek sayı T125 öncesi **59**'du (T123 rename'i sayıyı değiştirmediği için fark edilmemiş, pre-existing drift). 60'a çekildi ve düzeltme satırda not edildi.
- **Frontend prettier:** lokal `--check` 10 dosyada uyarı veriyor (dokunmadıklarım dahil) — `core.autocrlf` CRLF artifaktı; `--end-of-line auto` ile temiz. CI "1. Lint" (LF) yetkilidir.

## Commit & PR

- Branch: `task/T125-delivery-verification-evidence`
- Commit: `afc54f1` — T125: DeliveryVerificationService + DeliveryEvidence · `068a9bf` (rapor PR bilgisi) · `c97db85` (`Decide()` yorum düzeltmesi) · `b71760f` (CI sonucu rapora/memory'ye işlendi)
- PR: [#234](https://github.com/turkerurganci/Skinora/pull/234)
- CI: **✓ PASS** — final HEAD `b71760f`, run [`31832714884`](https://github.com/turkerurganci/Skinora/actions/runs/31832714884), **CI Gate `success`**; 12 bloke edici job yeşil (Lint · Build · Unit · JS test · Integration · Contract · Migration dry-run · Docker ×3 · Detect paths · Gate), `Guard` skipped (PR event). Önceki run'lar [`31831916818`](https://github.com/turkerurganci/Skinora/actions/runs/31831916818) (`c97db85`) ve [`31831063171`](https://github.com/turkerurganci/Skinora/actions/runs/31831063171) (`afc54f1`) da `success`.

## Notlar

- **Working tree:** temiz (`git status --short` boş, `main` üzerindeydi).
- **Adım 0 — Main CI startup check:** son 3 run `31825580363` ✓ success · `31825580317` ✓ success · `31824134702` ✓ success. Hepsi `conclusion=success` → task başlatıldı.
- **Bağımlılıklar:** T122 ✓ (spike + runbook), T124 ✓ (main `2c33026`, PR #232). İkisi de `✓ Tamamlandı`.
- **Kapsam kararı — `CaptureClassBaselineAsync` yeniden adlandırılmadı.** T125 aynı portu "şu anki sayım" okuması için de kullanıyor, yani ad artık iki işi anlatıyor. Yeniden adlandırma T123 kodunu ve testlerini süpürürdü; money-safety çekirdeği olan bu görevde diff'i dar tutmak tercih edildi. İsim dürüstlüğü açığı olarak not edilir.
