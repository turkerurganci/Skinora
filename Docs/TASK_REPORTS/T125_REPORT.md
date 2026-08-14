# T125 — DeliveryVerificationService + DeliveryEvidence

**Faz:** F7 (P2P geçişi, P3 "Yeni ileri yol") | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-08-14

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
| Doğrulama durumu | ⏳ Bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

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
- Commit: `afc54f1` — T125: DeliveryVerificationService + DeliveryEvidence
- PR: [#234](https://github.com/turkerurganci/Skinora/pull/234)
- CI: run [`31831063171`](https://github.com/turkerurganci/Skinora/actions/runs/31831063171) — izleniyor

## Notlar

- **Working tree:** temiz (`git status --short` boş, `main` üzerindeydi).
- **Adım 0 — Main CI startup check:** son 3 run `31825580363` ✓ success · `31825580317` ✓ success · `31824134702` ✓ success. Hepsi `conclusion=success` → task başlatıldı.
- **Bağımlılıklar:** T122 ✓ (spike + runbook), T124 ✓ (main `2c33026`, PR #232). İkisi de `✓ Tamamlandı`.
- **Kapsam kararı — `CaptureClassBaselineAsync` yeniden adlandırılmadı.** T125 aynı portu "şu anki sayım" okuması için de kullanıyor, yani ad artık iki işi anlatıyor. Yeniden adlandırma T123 kodunu ve testlerini süpürürdü; money-safety çekirdeği olan bu görevde diff'i dar tutmak tercih edildi. İsim dürüstlüğü açığı olarak not edilir.
