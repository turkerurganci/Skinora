# T121 — Backend envanter portu: üç değerli visibility

**Faz:** F7 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-11

---

## Yapılan İşler

1. **Port sözleşmesi üç değerli oldu (AC1).** `ISteamInventoryReader.TryGetItemAsync` → `GetItemAsync`; dönüş `InventoryItemSnapshot?` yerine `InventoryLookupResult(InventoryVisibility Visibility, InventoryItemSnapshot? Item)`. `InventoryVisibility` = `Public` / `Private` / `Unavailable`, 08 §2.3'ün üç durumu birebir.

2. **Yeniden kurulamaz şekilde kuruldu.** `InventoryLookupResult`'ın ctor'u **private**; tek giriş yolu dört fabrika (`Found` / `NotFound` / `Private` / `Unavailable`). Bunun sonucu: `Private` veya `Unavailable` bir sonuç **item taşıyamaz**, ve bir çağıran keyfi bir visibility'yi `null` ile eşleştirip eski çöktürmeyi yeniden üretemez. `SteamTradeHoldProbeResult` (T119a) emsaliyle aynı şekil.

3. **Rename bilinçli (AC2).** Metodun adı değiştirildiği için 7 çağrı yeri (2 üretim + 5 test double'ı) **derleme hatası** verdi. Aynı imzayı koruyup davranışı değiştirmek, çağıranların sessizce eski varsayımla kalmasına izin verirdi — T119a'nın positional record kararının aynısı.

4. **`HttpSteamSidecarInventoryClient` artık `visibility`'yi okuyor.** T120 alanı 200 gövdesine eklemişti ama backend'de okuyan yoktu. Şimdi: 200 + gövde `PRIVATE`/`UNAVAILABLE` ise **statü değil gövde** kazanır (`InventoryPrivate`/`Unavailable` döner); tanınmayan bir değer `Unavailable`'a düşer; alan **yoksa** HTTP statüsü yetkilidir (07 §6.1 normatif sözleşme, T120 öncesi sidecar'la geriye uyum). T120 validator gözlemi **V3** böylece kapandı.

5. **Kırık zarf artık 500 değil 503.** `items` dizisi olmayan bir 200 gövdesi eskiden `ToDto()` içinde `NullReferenceException` üretiyordu (yakalanan tipler yalnız `HttpRequestException`/`TaskCanceledException`/`JsonException`) — yani satıcı 500 alıyordu. Şimdi `Unavailable`. Aynı dosyadaki iki satırlık fail-safe; para yolunda bir 500'ü 503'e çevirir.

6. **`SidecarSteamInventoryReader` çöktürmüyor.** Üç `SteamSidecarStatus` değeri üç visibility'ye birebir gidiyor. Boş `steamId`/`assetId` girdisi `NotFound` **değil** `Unavailable` döner: okuma hiç yapılmadı, dolayısıyla "item yok" kanıtı üretilemez.

7. **Create ucu üç sonucu ayrı raporluyor (proje sahibi kararı, 2026-08-11).** `Public`+item yok → 422 `ITEM_NOT_IN_INVENTORY` (kanıt) · `Private` → 422 `INVENTORY_PRIVATE` · `Unavailable` → 503 `STEAM_UNAVAILABLE` (tekrar denenebilir). İki yeni kod **icat edilmedi** — 07 §6.1'in envanter listeleme ucunda zaten normatif olan sözlüğün aynısı, aynı statülerle. Satıcı böylece aynı akışta (step 1 listeleme → step 4 oluşturma) tek bir sözlük görür.

8. **`WrongItemDisputeAutoChecker` üç dalı ayrı ele alıyor.** Üçü de fail-closed kalır (dispute OPEN) — sonuç aynı olduğu için değil, **her biri için ayrı karar verildiği** için. Gizli envanterin kullanıcıya ne diyeceği 03 §6.2 Sonuç D'de tanımlı ve sahibi **T130**; burada bir mesaj anahtarı uydurmak o kararı gasp ederdi.

9. **`StubSteamInventoryReader` doğruyu söylüyor.** Artık `Unavailable` dönüyor. XML doc'u zaten "production callers fail closed (`STEAM_INVENTORY_UNAVAILABLE`)" diyordu ama kod `null` dönüyordu; yani sidecar hiç kayıtlı olmadığında satıcıya "item envanterinizde yok" deniyordu.

10. **FE ve doküman ucu kapatıldı.** Create formunun `POST_ERROR_CODES` kümesine iki kod, `step4.errors` altına iki mesaj × 4 dil. Eklenmeseydi kullanıcı jenerik "İşlem başlatılamadı" görürdü — yani backend'de ayrılan sinyal ekranda tekrar çökerdi. 07 §7.2 hata listesi de tamamlandı.

## Reddedilen tasarım: `TryGetItemAsync`'i kolaylık metodu olarak bırakmak

`GetItemAsync`'in yanına eski nullable metodu "geçiş kolaylığı" olarak bırakmak diff'i 7 dosya küçültürdü. Reddedildi: AC2 çöktürmenin **kaldırılmasını** istiyor, oysa bu, çöktürmeyi tek satırlık bir tercih olarak yaşatır. `InventoryLookupResult` üzerinde `ItemOrNull` gibi bir kısayol da aynı sebeple eklenmedi — porta eklenen her "kolay yol", 08 §2.3'ün ayrımını atlamanın kolay yoludur.

## Değerlendirilip kapsam dışı bırakılanlar

| Konu | Neden şimdi değil |
|---|---|
| `refresh` (cache bypass) parametresinin porta taşınması | T120 K3: çağıranları T123/T125/T129 bağlayacak. Kullanıcısı olmayan bir parametre eklemek, kullanılmayan yüzeydir |
| Envanterin tamamını okuyan port metodu (classid+instanceid sayımı) | 02 §9.2 delta hesabı **T125**'in; bugünkü tek ihtiyaç asset bazlı arama |
| `ISteamInventoryReader`'ın `Skinora.Shared/Steam/`'e taşınması | `ISteamTradeHoldProbe` orada duruyor ve simetri cazip; ama port bugün `Skinora.Transactions`'ta çalışıyor ve Disputes zaten referans veriyor. GUARDRAILS §4: varsayılan korumak |
| 03 §2.2'ye envanter-okunamadı adımı | 03 bugün create-time envanter hata modlarını **hiç** saymıyor (step 8 yalnız tradeable). Yeni bir tutarsızlık doğmadı; 03/04/07 hizalama turu **T133a**'nın |
| Gizli envanter için yeni dispute mesaj anahtarı | 03 §6.2 Sonuç D'nin sahibi **T130** |

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Skinora.Transactions/Application/Steam/ISteamInventoryReader.cs` | `GetItemAsync` + `InventoryVisibility` enum + `InventoryLookupResult` (private ctor, 4 fabrika) |
| `Skinora.Transactions/Application/Steam/StubSteamInventoryReader.cs` | `null` → `Unavailable` |
| `Skinora.Steam/Application/Inventory/SidecarSteamInventoryReader.cs` | Üç dal; boş girdi → `Unavailable` |
| `Skinora.Steam/Application/Inventory/HttpSteamSidecarInventoryClient.cs` | `visibility` alanı okunuyor (`ParseVisibility`); `items` yoksa `Unavailable` |
| `Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs` | Visibility switch → üç ayrı sonuç |
| `Skinora.Transactions/Application/Lifecycle/TransactionLifecycleDtos.cs` | `CreateTransactionStatus` +2 (`InventoryPrivate`, `SteamUnavailable`) |
| `Skinora.Transactions/Application/Lifecycle/TransactionErrorCodes.cs` | `InventoryPrivate = "INVENTORY_PRIVATE"` |
| `Skinora.API/Controllers/TransactionsController.cs` | 422 `INVENTORY_PRIVATE` + 503 `STEAM_UNAVAILABLE` eşlemesi |
| `Skinora.Disputes/.../WrongItemDisputeAutoChecker.cs` | Üç dal ayrı |
| `tests/Skinora.Steam.Tests/Unit/SidecarSteamInventoryReaderTests.cs` | 5 → **8** test, hepsi visibility pinliyor |
| `tests/Skinora.Steam.Tests/Unit/HttpSteamSidecarInventoryClientTests.cs` | 8 → **15** test (+1 fact, +4 theory vakası, +2 fact) |
| `tests/Skinora.Transactions.Tests/.../TransactionCreationServiceTests.cs` | **+3** test |
| `tests/Skinora.Transactions.Tests/.../TestSetupHelpers.cs` | Double'a `ForcedVisibility` |
| `tests/Skinora.API.Tests/Integration/TransactionLifecycleEndpointTests.cs` | **+3** endpoint testi + factory'ye `InventoryVisibilityOverride` |
| `tests/Skinora.Disputes.Tests/Integration/DisputeServiceTests.cs` | 1 `Fact` → 3 vakalı `Theory` (**+2**); double `InventoryLookupResult` taşıyor |
| `tests/Skinora.API.Tests/Integration/{Disputes,PayoutIssue}EndpointTests.cs` | Double'lar `NotFound` döner (eski `null`'ın bu suite'lerdeki anlamı) |
| `frontend/src/components/transactions/new/NewTransactionForm.tsx` | `POST_ERROR_CODES` +2 |
| `frontend/src/i18n/messages/{en,tr,es,zh}.json` | `step4.errors` +2 anahtar × 4 dil |
| `Docs/07_API_DESIGN.md` | §7.2 hata listesi + üç sonucu anlatan normatif not; başlık "Son güncelleme" |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | private != unavailable != boş, port seviyesinde gözlenebilir | ✓ | **Port:** `SidecarSteamInventoryReaderTests` — `GetItemAsync_Returns_Public_Without_Item_When_Asset_Not_In_Inventory`, `..._Returns_Private_When_Profile_Private`, `..._Returns_Unavailable_When_Sidecar_Unavailable`, ve üçünü tek assert'te karşılaştıran `GetItemAsync_Distinguishes_Private_From_Unavailable_From_Empty` (üç `Visibility` çifter çifter `NotEqual`). **Tüketici:** `Inventory_Visibility_Drives_Three_Distinct_Create_Outcomes` — 3 farklı `CreateTransactionStatus` **ve** 3 farklı `ErrorCode` (`Distinct().Count() == 3`). **HTTP:** üç ayrı endpoint testi 422 `ITEM_NOT_IN_INVENTORY` / 422 `INVENTORY_PRIVATE` / 503 `STEAM_UNAVAILABLE` |
| 2 | Mevcut null'a çöktürme davranışı kaldırıldı (money-safety) | ✓ | `TryGetItemAsync` repoda **yok** (`grep -rn "TryGetItemAsync" --include=*.cs backend/` → 0 eşleşme). Dönüş tipi non-nullable; `InventoryLookupResult` ctor'u private olduğu için `Private`/`Unavailable` item taşıyamaz. Çöktürmenin **yeniden girme yolları** da kapatıldı: sidecar 200 + `PRIVATE` gövdesi (`GetInventoryAsync_Honours_A_NonPublic_Visibility_On_200`), `items`siz 200 (`..._When_200_Body_Has_No_Items_Array`), boş girdi (`..._On_Empty_Inputs_Without_Calling_Sidecar`), `Success`+zarf yok (`..._When_Success_Carries_No_Envelope`) — dördü de `Unavailable`, hiçbiri "okundu, yok" değil |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend build | ✓ | `dotnet build Skinora.sln` — **0 warning, 0 error** |
| Backend test (tümü) | ✓ **2494/2494** | `dotnet test Skinora.sln --no-build` — 13 proje, 0 fail, 0 skip. Bu task **+18** test |
| — `Skinora.Steam.Tests` | ✓ 31/31 | Baseline **21** (T120 raporunda ölçülmüş) → +10 |
| — `Skinora.Transactions.Tests` | ✓ 822/822 | +3 (create ucu visibility) |
| — `Skinora.API.Tests` | ✓ 521/521 | +3 (HTTP statü/kod) |
| — `Skinora.Disputes.Tests` | ✓ 60/60 | +2 (`Fact` → 3 vakalı `Theory`) |
| FE lint | ✓ | `npm run lint` — 0 bulgu |
| FE typecheck | ✓ | `npx tsc --noEmit` — 0 hata |
| FE test | ✓ 33/33 | `npm test` (vitest, 9 dosya) |
| FE i18n parity | ✓ | `npm run i18n:check` — "4 locales, **1300 keys each**, identical key sets". 15 advisory uyarı **mevcut** ("Gas fee" verbatim kuralı, bu task'ın anahtarlarıyla ilgisiz) |
| FE build | ✓ | `npm run build` — başarılı |

> **Baseline notu.** 2494 ölçülmüş sayıdır; **2476** baseline'ı ondan +18 çıkarılarak **türetilmiştir** (ayrı bir main run'ı ölçülmedi). Tek bağımsız çapa `Skinora.Steam.Tests` 21'dir ve T120 raporunda main üzerinde ölçülmüştür.

> **Lokal Docker notu.** İlk suite koşusunda `IntegrationTestBase` türevi 43 test `DockerUnavailableException` ile düştü — Docker Desktop kapalıydı, değişiklikle ilgisi yok. Docker açıldıktan sonra aynı komut 0 fail verdi. Yetkili ölçüm ikinci koşudur.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **✓ PASS** |
| Tarih / yöntem | 2026-08-11, ayrı chat, **yapım raporu görülmeden** bağımsız verdict |
| Doğrulanan commit | `a075e5e` (branch HEAD) |
| Bulgu sayısı | **0 bloke edici** — S1 Sapma 0 · S2 Kırılma 0 · S3 Eksik 0 |
| Düzeltme gerekli mi | Hayır |

**Başlangıç kapıları.** Adım -1 working tree **temiz**. Adım 0 main CI son 3 tamamlanmış run `success` — `31508344655`, `31508344617` (T120 #228), `31432878950` (T119a #227). Adım 0b repo memory'de T121 satırı **mevcut**.

**Kabul kriterleri — bağımsız kanıt.**

| # | Kriter | Sonuç | Validator kanıtı |
|---|---|---|---|
| 1 | private != unavailable != boş, port seviyesinde gözlenebilir | ✓ | Port sözleşmesi okundu: `GetItemAsync` → `InventoryLookupResult`, `InventoryVisibility` ∈ {`Public`,`Private`,`Unavailable`} = 08 §2.3 tablosunun üç satırı birebir. `Skinora.Steam.Tests` **31/31**; `GetItemAsync_Distinguishes_Private_From_Unavailable_From_Empty` üç visibility'yi çifter çifter `NotEqual` ile pinliyor. Tüketici katmanı: `Inventory_Visibility_Drives_Three_Distinct_Create_Outcomes` (3 farklı status **ve** 3 farklı ErrorCode), HTTP katmanı: 422 `ITEM_NOT_IN_INVENTORY` / 422 `INVENTORY_PRIVATE` / 503 `STEAM_UNAVAILABLE` üç ayrı endpoint testi |
| 2 | Mevcut null'a çöktürme davranışı kaldırıldı (money-safety) | ✓ | `grep -rn "TryGetItemAsync" --include=*.cs .` → **0 eşleşme** (kalan 9 iz yalnız doküman/rapor/memory). Dönüş tipi non-nullable; ctor private + 4 fabrika → `Private`/`Unavailable` item taşıyamaz (kod okumasıyla teyitli). Üretimdeki **tüm** tüketiciler sayıldı: `grep -rn "GetItemAsync" backend/src/` → 2 çağıran (`TransactionCreationService`, `WrongItemDisputeAutoChecker`), ikisi de Visibility üzerinden dallanıyor; `ISteamInventoryReader` başka üretim tüketicisi yok |

**Wire sözleşmesi bağımsız izlendi.** `sidecar-steam/src/api/routes.ts:121-139` üç dalı 200+`PUBLIC` / 422+`PRIVATE` / 503+`UNAVAILABLE` olarak yayınlıyor; backend `HttpSteamSidecarInventoryClient` 422→`InventoryPrivate`, non-success→`Unavailable`, artı 200 gövdesindeki `visibility` — iki uç tutarlı. `sidecar-fake/src/routes/steam.ts:50` `visibility: 'PUBLIC'` (parite doğrulandı). 07 §6.1 (satır 951) *"503 `STEAM_UNAVAILABLE`, 422 `INVENTORY_PRIVATE`"* → create ucuna eklenen iki kod **icat edilmemiş**, aynı statülerle mevcut sözlükten alınmış.

**Dört negatif prova (hepsi geri alındı, tree temiz).** Testlerin AC'yi gerçekten koruduğunu kanıtlamak için üretim kodu mutasyona uğratıldı:

| Prova | Mutasyon | Kırılan test |
|---|---|---|
| A | `SidecarSteamInventoryReader`: `Private` dalı → `NotFound` (08 §2.3'ün en tehlikeli çöktürmesi) | **2** — `..._Returns_Private_When_Profile_Private`, `..._Distinguishes_Private_From_Unavailable_From_Empty` |
| B | `TransactionCreationService`: visibility switch'i tamamen kaldırıldı (üçü de `ITEM_NOT_IN_INVENTORY`) | **3** — `Rejects_With_InventoryPrivate...`, `Rejects_With_SteamUnavailable...`, `Inventory_Visibility_Drives_Three_Distinct_Create_Outcomes` |
| C | `ParseVisibility`: tanınmayan değer fail-open → `Success` | **2** theory vakası — `..._Honours_A_NonPublic_Visibility_On_200("UNAVAILABLE")`, `("SOMETHING_NEW")` |
| D | `TransactionsController`: `SteamUnavailable` → 503 yerine 422 | **1** — `Create_Returns_503_STEAM_UNAVAILABLE_When_Inventory_Unreadable` |

**Ölçümler yapım raporuyla birebir.** Backend build **0 warning / 0 error** · `dotnet test Skinora.sln` **2494/2494** (13 proje, 0 fail, 0 skip — Steam 31 · Transactions 822 · API 521 · Disputes 60 · Shared 399 · Platform 189 · Notifications 171 · Auth 120 · Fraud 91 · Realtime 40 · Users 22 · Admin 22 · Payments 6) · FE eslint 0 · tsc 0 · vitest **33/33** · `npm run i18n:check` *"parity OK — 4 locales, **1300 keys each**, identical key sets"* (15 advisory uyarı **T121 öncesinden**, "Gas fee"/"Mobile Authenticator" verbatim kuralı — bu task'ın anahtarlarıyla ilgisiz) · `npm run build` ✓ · migration yok. Steam.Tests `+10` iddiası `git show origin/main` sayımıyla bağımsız doğrulandı (reader 5→8 `Fact`, client 8→12 attribute = 8→15 vaka).

**Task branch CI (Adım 8a).** Yetkili run **[`31518644528`](https://github.com/turkerurganci/Skinora/actions/runs/31518644528)** — branch HEAD `a075e5e`, **CI Gate `success`**, bloke edici 9 job yeşil. (Rapor `31517635620`'yi yetkili gösteriyor; validator bir sonraki, yani gerçekten merge edilecek HEAD'in run'ını ölçtü — o da yeşil.) `31517075770` concurrency ile **cancelled**, FAIL değil.

**E2E kırmızılığı bağımsız ölçüldü.** `gh run view 31518644528 --log-failed` (970 satır): `PlatformSteamBots` **8 iz** (leg başına tam bir tane, kök sebep `RequestError: Invalid object name 'PlatformSteamBots'`), T121 yüzeyleri (`InventoryLookupResult` / `GetItemAsync` / `INVENTORY_PRIVATE` / `STEAM_UNAVAILABLE` / `ITEM_NOT_IN_INVENTORY` / `visibility`) **0 iz**. **Main baseline karşılaştırması yapıldı:** `31508344617` (T120 squash, main) **aynı 8 leg**'i aynı adlarla kırıyor → T121 yeni kırılma getirmedi (sahiplik T137 → T138).

**Güvenlik kontrolü.** Secret sızıntısı: temiz (diff üzerinde tarama, 0 eşleşme). Auth/authorization: etkisiz — yeni uç yok, `POST /transactions` policy + rate limit değişmedi. Input validation: **iyileşme** — boş `steamId`/`assetId` artık sidecar'a hiç gitmiyor (`GetInventoryCalls == 0` ile pinli) ve sidecar `visibility` değeri allowlist ile ayrıştırılıp tanınmayan değer fail-safe `Unavailable`'a düşüyor. Yeni dış bağımlılık: yok. Bilgi ifşası: 503 gövdesi sabit kod + sabit mesaj; envanter sahibi zaten çağıranın kendisi.

**2 bloke etmeyen gözlem (kayda geçirildi, düzeltme istenmedi):**

- **(V1) Switch'ler enum genişlemesine karşı fail-open.** `TransactionCreationService`'in `switch (lookup.Visibility)` bloğunda `default` arm yok; `WrongItemDisputeAutoChecker` de `is Private or Unavailable` şeklinde **allowlist değil denylist** kuruyor. `InventoryVisibility`'ye ileride dördüncü (okunamaz anlamına gelen) bir değer eklenirse ikisi de sessizce `lookup.Item is null` yoluna düşer, yani yeni durum **`ITEM_NOT_IN_INVENTORY` kanıtına çöker** — T121'in kaldırdığı hatanın aynısı. Bugün gerçek bir açık değil (enum 3 değerli ve genişletmek spec değişikliğidir), ama `default:` → `SteamUnavailable` yazmak korumayı tasarımdan gelir hâle getirirdi. T125/T130 porta yeni tüketici bağladığında ucuz sigorta.
- **(V2) Ayrımın kullanıcıya ulaştığı tek yüzey create ucu.** `WrongItemDisputeAutoChecker` üç dalı ayrı ele alıyor ama üçü de aynı `Unresolved(NoDeliveryMessage)` sonucunu üretiyor — yani port seviyesindeki ayrım dispute yüzeyinde bugün **gözlenemiyor**. Kod bunu bilerek yapıyor (03 §6.2 Sonuç D metni T130'un) ve doğru kapsamlama; gözlem, T130 o katmanı yazarken ayrımın **hazır** olduğunun ve tüketilmesi gerektiğinin kaydı olarak bırakılıyor.

**Yapım raporu karşılaştırması: tam uyumlu.** İki AC verdict'i, ölçümler, kapsam-dışı gerekçeleri ve E2E imza analizi bağımsız ölçümle örtüşüyor; uyuşmazlık yok. Tek fark yorum değil kapsam: rapor yetkili CI run'ı olarak `31517635620`'yi (kod commit'i) gösteriyor ve doküman-only commit'in run'ını bilinçli olarak raporlamıyor (sonsuz regresyon argümanı); validator merge edilecek HEAD'in run'ını (`31518644528`) ölçüp yukarıya ekledi — sonuç aynı, kanıt bir commit daha güncel.

## Altyapı Değişiklikleri

- **Migration:** Yok — hiçbir entity/DbContext/konfigürasyona dokunulmadı; değişiklik tamamen application katmanında.
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni paket:** Yok.
- **Yeni public hata kodu:** 2 — `INVENTORY_PRIVATE` (422) ve `STEAM_UNAVAILABLE` (503) `POST /transactions`'ta. İkisi de platformda zaten var olan kodlar; yeni bir uç veya yeni bir sözcük eklenmedi.

## Mini Güvenlik Kontrolü

| Alan | Sonuç |
|---|---|
| Secret sızıntısı | Temiz — yeni config/env/log alanı yok; `visibility` değeri kullanıcı verisi değil, üç sabitten biri |
| Auth/authorization | Etkisiz — yeni uç yok; `POST /transactions` yetkilendirmesi ve rate limit'i değişmedi |
| Input validation | Etkisiz kullanıcı girdisi yok. Sidecar'dan gelen `visibility` **allowlist** ile ayrıştırılıyor ve tanınmayan değer `Unavailable`'a düşüyor (fail-safe) |
| Yeni dış bağımlılık | Yok — `package.json` / `.csproj` diff'te değil |
| Bilgi ifşası | 503 gövdesi yalnız sabit kod + sabit mesaj taşıyor; sidecar hata metni kullanıcıya iletilmiyor |

## Commit & PR

- Branch: `task/T121-inventory-port-visibility`
- Commit: `1479453` — T121: Backend envanter portu — üç değerli visibility
- PR: [#229](https://github.com/turkerurganci/Skinora/pull/229)
- Branch izolasyon kontrolü: ✓ temiz — `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+…'` → yalnız `T121`
- CI: **✓ PASS** — run [`31517635620`](https://github.com/turkerurganci/Skinora/actions/runs/31517635620) (HEAD `e6f97c4`), **CI Gate `success`**

**Bloke edici job'lar (9/9 yeşil):** Detect changed paths · 1. Lint · 2. Build · 3. Unit test · 3b. JS test (vitest) · 4. Integration test · 5. Contract test · 6. Migration dry-run · 7. Docker build (backend + frontend) · CI Gate. (`0. Guard (direct push)` skipped — PR yolunda beklenen.)

**Önceki run cancel edildi, FAIL değil.** `31517075770` (HEAD `1479453`, yalnız kod commit'i) ikinci push tarafından concurrency ile iptal edildi: 8 bloke edici job `success`, "4. Integration test" **cancelled**, dolayısıyla CI Gate zincirleme kırmızı. task.md'nin concurrency notu bu durumu `failure` saymaz; yetkili ölçüm son tamamlanmış run olan `31517635620`'dir.

**8 advisory E2E leg'i kırmızı — bu task kaynaklı değil, kanıtlı.** Kırılma T117'den beri sürüyor (`continue-on-error`, CI Gate'i bloke etmiyor; sahiplik T137 → T138). `gh run view 31517635620 --log-failed` (970 satır) üzerinde ölçüm:

| Arama | İz sayısı | Anlamı |
|---|---|---|
| `PlatformSteamBots` | **8** | T117'nin bıraktığı kök sebep — leg başına tam bir tane; T120 run'ındaki imzayla birebir aynı |
| `InventoryLookupResult` / `GetItemAsync` | **0** / **0** | Yeni port tipi ve metodu hiçbir kırılmada geçmiyor |
| `INVENTORY_PRIVATE` / `STEAM_UNAVAILABLE` | **0** / **0** | İki yeni hata kodu hiçbir kırılmada geçmiyor |
| `ITEM_NOT_IN_INVENTORY` | **0** | Değişen create dalı hiçbir kırılmada geçmiyor |
| `visibility` | **0** | Backend'in okumaya başladığı alan hiçbir kırılmada geçmiyor |

Yani E2E yığını `sidecar-fake`'in `visibility: 'PUBLIC'` yanıtını yeni port üzerinden sorunsuz geçiriyor; T120'nin sözleşme paritesi eklemesi amacına ulaştı ve T121 yeni bir kırılma getirmedi.

> **Not:** Bu bölümü ekleyen doküman-only commit kendi CI run'ını tetikler; o run'ın kimliği raporlanmaz — aksi hâlde her rapor güncellemesi bir sonrakini gerektirir (sonsuz regresyon). Yetkili ölçüm, kodu taşıyan `31517635620` run'ıdır.

## Known Limitations / Follow-up

| # | Açık | Durum |
|---|---|---|
| 1 | `Private` ile `Unavailable` ayrımı **sidecar'ın** doğru sınıflandırmasına bağlı; sidecar tarafında private tespiti `steamcommunity`'nin mesaj string'ine dayanıyor | T120 K5 — mevcut stratejinin sınırı, tanınmayan hata `UNAVAILABLE`'a düşer (fail-safe) |
| 2 | `refresh` (önbelleksiz okuma) porta taşınmadı | T123 / T125 / T129 — sidecar ucu T120'de hazır |
| 3 | Envanterin tamamını okuyan (classid+instanceid delta) port metodu yok | **T125** — 02 §9.2 kanıt matrisi |
| 4 | 07 §7.6a'nın tek boolean'ı (`buyerInventoryVisible`) hâlâ Private ile Unavailable'ı çöktürüyor | T120 K7 — **T123**'ün; T121 o katmanın ihtiyaç duyduğu üç değerli primitifi sağladı |
| 5 | Gizli envanterde dispute cevabının metni hâlâ `WRONG_ITEM_NO_DELIVERY` | **T130** (03 §6.2 Sonuç D) |
| 6 | `sidecar-fake` steamId başına Private/Unavailable süremiyor (hep PUBLIC) | **T137** — bu yüzden E2E bugün yalnız Public dalını gerçek yığında geçiriyor |
| 7 | 03 §2.2 create-time envanter hata modlarını saymıyor | Bu task öncesinde de saymıyordu; 03/04/07 hizalama turu **T133a** |

## Notlar

**Dış Varsayımlar**

| Varsayım | Kanıt |
|---|---|
| Sidecar 200 gövdesinde `visibility` yayınlıyor | `sidecar-steam/src/api/routes.ts:123` — `res.status(200).json({ visibility: result.visibility, ...result.inventory })` |
| `sidecar-fake` de aynı alanı yayınlıyor (E2E sessizce eski şekli görmez) | `sidecar-fake/src/routes/steam.ts:50` — `visibility: 'PUBLIC'` |
| Portun bugünkü tek çöktürme noktası biliniyor | `grep -rn "TryGetItemAsync" --include=*.cs backend/` → 2 üretim çağıranı (`TransactionCreationService`, `WrongItemDisputeAutoChecker`) + 5 test double'ı. `DeliveryDisputeAutoChecker` envanter portunu **kullanmıyor** (kendi XML doc'u: T130 geri getirecek) |
| `InventoryVisibility` / `InventoryLookupResult` isim çakışması yok | `grep -rn "InventoryVisibility\|InventoryLookupResult" --include=*.cs backend/` → 0 eşleşme (değişiklik öncesi) |
| Üç değerli port için repo emsali var | `Skinora.Shared/Steam/ISteamTradeHoldProbe.cs` — `SteamTradeHoldProbeResult` record + statik fabrikalar (T119a) |
| 07 §6.1'in envanter sözlüğü normatif | `Docs/07_API_DESIGN.md:951` — "**Hatalar:** 503 `STEAM_UNAVAILABLE`, 422 `INVENTORY_PRIVATE`" |
| FE'de hata kodu → mesaj için genişletme noktası var | `NewTransactionForm.tsx` `POST_ERROR_CODES` kümesi + `step4.errors.<CODE>` anahtarı; kümede olmayan kod jenerik mesaja düşer |
| Migration gerekmiyor | Değişen dosyaların hiçbiri entity/configuration değil; `dotnet test` içindeki `InitialMigrationTests.Model_HasNoPendingChanges` yeşil |

**Başlangıç kapıları (yapım turu)**

- Working tree (Adım -1): **temiz** — `git status --short` boş.
- Main CI (Adım 0): son 3 tamamlanmış run `success` — `31508344655` (Docker Publish, T120 #228), `31432878950` (T119a #227), `31414178181` (T119 #226).
- Bağımlılık: **T120 ✓ Tamamlandı** (doğrulama ✓ PASS, `IMPLEMENTATION_STATUS.md`, 2026-08-11).

**Proje sahibi kararı (2026-08-11).** `POST /transactions` envanter okuması `Private`/`Unavailable` döndüğünde ne olacağı sorusu üç seçenekle sunuldu: (a) ayrı kodlar (422 `INVENTORY_PRIVATE` + 503 `STEAM_UNAVAILABLE`), (b) üçü de `ITEM_NOT_IN_INVENTORY` kalsın, (c) yalnız `Unavailable` ayrılsın. **(a) seçildi.** Gerekçe: kodlar 07 §6.1'de zaten normatif, satıcı aynı akışta tek sözlük görüyor ve (b) T120'nin "reddedilen tasarım" tablosundaki yanlış mesajı bir katman yukarıda tekrarlıyordu.

**Doküman güncellemesi.** Yalnız 07 §7.2 hata listesi (+ üç sonucu anlatan normatif not) ve başlık satırındaki "Son güncelleme". Sürüm **v3.1'de bırakıldı** — T133a'nın kabul kriteri "07 v3.1 sürüm notları yazıldı" diyor, şimdi bump etmek o görevin metnini bayatlatırdı. 08 §2.3 zaten bu davranışı normatif olarak dayatıyordu; T121 onu uygulayan koddur, değiştiren değil.
