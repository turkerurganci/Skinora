# T128 — (SellerId, ItemAssetId) tekillik kapısı

**Faz:** F7 (P3 — Yeni ileri yol) | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-08-16

---

## Yapılan İşler

1. **`POST /transactions` uygulama kapısı eklendi.** Satıcının aynı `itemAssetId` üzerinde terminal olmayan bir işlemi varsa uç artık 422 `ITEM_ALREADY_LISTED` döner. T117 kısıtı (`UQ_Transactions_SellerId_ItemAssetId_Active`) DB'de duruyordu ama uygulama tarafında hiçbir kontrol yoktu — ikinci create `SaveChanges`'te `DbUpdateException` fırlatıyor ve controller'ın `_ => 500` dalına düşüyordu. Yani 02 §2.3'ün *"ikinci işlem oluşturma denemesi reddedilir"* kuralı bugüne kadar **500 Internal Server Error** olarak karşılanıyordu.

2. **Kapı iki katmanlı.** (a) Insert öncesi, indeksin filtresini birebir aynalayan bir sorgu — yaygın vaka; (b) `SaveChanges` çevresinde UNIQUE ihlali yakalama — iki eşzamanlı create'in yarıştığı vaka. Ön-kontrol bir **okuma** olduğu için TOCTOU açıktır; indeksi tek gerçek hakem, ön-kontrolü ise "kullanıcıya doğru cevabı ucuz yoldan verme" katmanı olarak konumlandırdık.

3. **Yakalama dalı çakışmayı yeniden okuyarak doğruluyor**, sürücü mesajındaki indeks adını eşleştirerek değil. `Transactions` ikinci bir unique index taşıyor (`UQ_Transactions_InviteToken`); bir token çakışması "item zaten listelenmiş" diye raporlanırsa satıcı yanlış olmayan bir şeyi düzeltmeye gönderilir. Çakışan satır yoksa exception **yeniden fırlatılır**.

4. **Kapının boru hattındaki yeri bilinçli:** satıcı satırı bulunduktan **sonra**, Steam envanter okumasından **önce**. Öncesine konsaydı askıya alınmış/silinmiş satıcı `SellerNotFound` yerine `ITEM_ALREADY_LISTED` duyardı; sonrasına konsaydı her mükerrer deneme rate-limited bir Steam round-trip'i harcardı (T122 Steam kotasını kıt kaynak olarak belgeliyor).

5. **UNIQUE ihlali yordamı tek yere alındı.** `PaymentAddressAllocator`'ın T70'ten beri taşıdığı `IsUniqueViolation`/`TryGetSqlNumber` çifti `Infrastructure/Persistence/DbConstraintViolations.cs`'e taşındı; her iki çağıran aynı kuralı aynı yerden okuyor. Davranış değişmedi (saf taşıma).

6. **Frontend + 4 dil.** `ITEM_ALREADY_LISTED` `POST_ERROR_CODES` setine ve `step4.errors` bloğuna (en/tr/es/zh) eklendi. Kendi mesajını hak ediyor çünkü çözüm ne "tradeable item seç" ne de "envanteri yenile" — item sağlam, sadece başka bir işleme bağlı. T121 emsali (aynı akışta backend + FE birlikte).

7. **07 §7.2 güncellendi.** Hata listesine 422 `ITEM_ALREADY_LISTED` eklendi ve kuralın neden bir düzen değil **para güvenliği** kuralı olduğu normatif not oldu (teslimat kanıtı item **sınıfı** üzerinden sayıldığı için aynı asset'i hedefleyen iki canlı işlem gelen item'ı yanlış işleme atfeder → para yanlış satıcıya gider).

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionCreationService.cs` | Stage 5a kapısı + `SaveChanges` yakalama dalı + `FindOpenListingAsync` |
| `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionErrorCodes.cs` | `ItemAlreadyListed = "ITEM_ALREADY_LISTED"` |
| `backend/src/Modules/Skinora.Transactions/Application/Lifecycle/TransactionLifecycleDtos.cs` | `CreateTransactionStatus.ItemAlreadyListed` |
| `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/DbConstraintViolations.cs` | **Yeni** — UNIQUE ihlali tespiti (T70'ten taşındı) |
| `backend/src/Modules/Skinora.Transactions/Application/PaymentAddresses/PaymentAddressAllocator.cs` | Yerel kopya kaldırıldı, ortak yordama delege ediyor |
| `backend/src/Skinora.API/Controllers/TransactionsController.cs` | 422 grubuna `ItemAlreadyListed` |
| `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCreationServiceTests.cs` | +7 test metodu (12 çalıştırma — biri 6 vakalık `[Theory]`) + `SeedListingAsync` + `FixedInvitationCodeGenerator` |
| `backend/tests/Skinora.Transactions.Tests/Integration/Lifecycle/TestSetupHelpers.cs` | `FakeSteamInventoryReader.OnItemRead` yarış dikişi |
| `backend/tests/Skinora.API.Tests/Integration/TransactionLifecycleEndpointTests.cs` | +1 uçtan uca 422 testi |
| `frontend/src/components/transactions/new/NewTransactionForm.tsx` | `POST_ERROR_CODES` + `ITEM_ALREADY_LISTED` |
| `frontend/src/i18n/messages/{en,tr,es,zh}.json` | 4 dilde mesaj |
| `Docs/07_API_DESIGN.md` | §7.2 hata listesi + normatif not, başlık `Son güncelleme` |

## Yapım Öncesi Kararlar (proje sahibine soruldu, dördü de öneri yönünde onaylandı)

| # | Karar | Seçilen | Gerekçe |
|---|---|---|---|
| K1 | Kapı mekanizması | **Ön-kontrol + UNIQUE yakalama** | Tek başına ön-kontrol yarış penceresi bırakır (kullanıcı 500 görür); tek başına yakalama her mükerrer denemede boşa insert + outbox/history staging harcar ve iş kuralını exception yoluna taşır |
| K2 | HTTP statüsü | **422** | §7.2'nin tüm iş kuralı retleri 422; en yakın kardeşler (`ITEM_NOT_TRADEABLE`, `ITEM_NOT_IN_INVENTORY`) de öyle. 409 HTTP semantiğine yakın ama §7.2 içinde hiç 409 yok — uç içi tek sözlük korundu |
| K3 | Boru hattındaki yer | **Steam okumasından önce** | Mükerrer deneme rate-limited Steam okuması harcamaz; kimlik hataları (`SellerNotFound`) önceliğini korur |
| K4 | Backend dışı kapsam | **07 §7.2 + FE/i18n dahil; çakışan işlem ID'si hariç** | Kod dokümana yazılmadan üretilirse 07 source-of-truth olmaktan çıkar; FE'siz kullanıcı jenerik mesaj görür. Hata zarfına işlem ID'si eklemek §7'nin `code`+`message` sözleşmesini genişletirdi → known limitation |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | İkinci create `ITEM_ALREADY_LISTED` dönüyor | ✓ | Servis düzeyi: `Rejects_Second_Create_For_Same_Asset_With_ItemAlreadyListed` · Uçtan uca (gerçek HTTP + gerçek DI): `Create_Returns_422_ITEM_ALREADY_LISTED_On_Second_Create_For_Same_Asset` — birinci istek 201, ikincisi 422 + `error.code = ITEM_ALREADY_LISTED` |

**Kriterin etrafındaki davranış (kabul kriterine yazılı değil, kapı doğru olsun diye sabitlendi):**

| Davranış | Test |
|---|---|
| Terminal işlem yeni create'i **engellemiyor** (6 statü) | `Terminal_Transaction_Over_Same_Asset_Does_Not_Block_A_New_Create` ×6 — indeksin filtresinden katı bir kapı asset'i kalıcı olarak kilitlerdi |
| Başka satıcının aynı asset'i engellemiyor | `Another_Sellers_Open_Listing_Over_Same_Asset_Does_Not_Block` |
| Kapı Steam okumasından önce çalışıyor | `Already_Listed_Gate_Runs_Before_The_Steam_Read` (`_inventory.ItemReadFreshness` boş) |
| Kapı `SellerNotFound`'u ezmiyor | `Already_Listed_Gate_Does_Not_Preempt_SellerNotFound` |
| Yarışı kaybeden istek 500 değil 422 alıyor ve satır yazmıyor | `Create_Losing_The_Uniqueness_Race_Reports_ItemAlreadyListed` — rakip satır envanter okuması sırasında (kapıdan sonra, `SaveChanges`'ten önce) **ayrı bir bağlantıdan** yazılıyor; sonuçta tek satır kalıyor |
| Başka indeksin ihlali `ITEM_ALREADY_LISTED` diye raporlanmıyor | `Unique_Violation_On_A_Different_Index_Is_Not_Reported_As_ItemAlreadyListed` — `InviteToken` çakışması `DbUpdateException` olarak yeniden fırlatılıyor |
| Ret hiçbir şey yazmıyor | `Rejects_Second_Create_...` içinde outbox boş, allocator çağrılmamış, satır sayısı 1 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 hata / 0 uyarı | `dotnet build` (backend) |
| Unit | ✓ 1382/1382 | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — T127 taban çizgisiyle aynı (yeni testlerin hepsi integration) |
| Integration | ✓ **1279/1279** | Assembly bazında **seri** koşum (T127 dersi: Testcontainers suite'leri paralel koşturulmaz). 10 assembly: Transactions 475 · API 475 · Fraud 73 · Platform 65 · Notifications 60 · Disputes 50 · Auth 37 · Admin 22 · Shared 16 · Payments 6 |
| Contract | ✓ 9/9 | `--filter "FullyQualifiedName~.Contract"` (Shared 5 + API 4) |
| Yeni test | **8 metot → 13 çalıştırma** | 7 metot Transactions integration (biri 6 vakalık `[Theory]` ⇒ 12 çalıştırma) + 1 API endpoint testi. **Aritmetik kapanıyor:** T127'nin 1266'sı + 13 = 1279 |
| FE lint | ✓ 0 | `npm run lint` |
| FE i18n | ✓ 1302×4 parity | `npm run i18n:check` — "identical key sets"; 15 advisory `untranslatable` uyarısı pre-existing (WP18 kararı: advisory) |
| FE test | ✓ 33/33 | `npm run test` (vitest, 9 dosya) |

## Altyapı Değişiklikleri

- **Migration:** Yok. Kısıt T117 migration'ında (`20260809162642_T117_P2P_Pivot`) zaten şemada.
- **Config/env değişikliği:** Yok.
- **Docker değişikliği:** Yok.
- **Yeni paket:** Yok.

## Mini Güvenlik Kontrolü (Katman 1)

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | Yok — yeni sabit yalnız hata kodu dizgesi |
| Auth/authorization etkisi | Yok — uç zaten `Authenticated`; kapı `sellerId`'yi token'dan alır, istekten değil |
| Input validation etkisi | Yok — yeni girdi yüzeyi yok; `itemAssetId` mevcut doğrulamadan geçiyor. Sorgu parametreli LINQ, string birleştirme yok |
| Yeni dış bağımlılık | Yok |
| Bilgi sızıntısı | Kontrol edildi: çakışan işlemin **ID'si yanıta konmadı**, yalnız loglanıyor. Yanıt "sizin açık bir işleminiz var" der; başkasının satırı hakkında hiçbir şey söylemez (sorgu zaten `SellerId` ile sınırlı) |

## Commit & PR

- Branch: `task/T128-item-uniqueness-gate`
- Commit: `e9a9f33` — T128: (SellerId, ItemAssetId) tekillik kapısı · `b644ee4` — rapor/status/memory PR referansları
- PR: [#239](https://github.com/turkerurganci/Skinora/pull/239)
- CI: ✓ **PASS** — kod içeren son commit `b644ee4`, run [`31941068035`](https://github.com/turkerurganci/Skinora/actions/runs/31941068035), **CI Gate `success`**. Bloke edici 9 job yeşil: Lint · Build · Unit test · Integration test · Contract test · **Migration dry-run** · JS test (vitest) · Docker build (backend) · Docker build (frontend). `0. Guard (direct push)` beklendiği gibi `skipped` (PR event). İlk commit'in run'ı (`31941043003`, `e9a9f33`) concurrency ile `cancelled` — başarısızlık değil.
- CI (dal HEAD'i, salt-doküman finalize commit'i): ✓ **PASS** — `16cd40b`, run [`31941624051`](https://github.com/turkerurganci/Skinora/actions/runs/31941624051), bloke edici job'ların tamamı `success`, yine yalnız `Guard` skipped + aynı 8 advisory E2E leg. **Not:** bu satırdan sonraki tek commit bu satırı ekleyen commit'tir (salt-doküman); kod ağacı `b644ee4`'ten beri değişmedi, dolayısıyla iki run da aynı kaynağı doğruluyor.

**8 advisory E2E leg kırmızı — T128 kaynaklı DEĞİL, bu run'ın logundan doğrulandı.** Kök sebep imzası her legde birebir aynı: `Invalid object name 'PlatformSteamBots'` (**8/8**, leg başına tam 1 iz). Aynı loglarda T128 yüzeylerinden (`ITEM_ALREADY_LISTED` / `DbConstraintViolations` / `FindOpenListing`) **0 iz**. T117 tablo düşürmesinden beri pre-existing; sahiplik T137 (sidecar-fake envanter) → T138 (E2E spec yeniden yazımı). `continue-on-error` oldukları için gate'i bloke etmiyorlar.

## Known Limitations / Follow-up

- **Çakışan işlemin ID'si yanıtta taşınmıyor** (K4 kararı). Satıcı "hangi işlem?" sorusunu işlem listesinden cevaplamak zorunda. Hata zarfını genişletmek 07 §7'nin `code`+`message` sözleşmesini değiştirir; ayrı bir karar olarak bırakıldı.
- **Ön-kontrol istekteki `itemAssetId`'yi karşılaştırıyor**, Steam'in döndürdüğü `AssetId`'yi değil (kapı okumadan önce çalışıyor). İkisi normalde birebir aynıdır; ayrılırlarsa (ör. baştaki/sondaki boşluk) ön-kontrol ıskalar ve indeks yakalar — sonuç yine `ITEM_ALREADY_LISTED`, yalnız bir Steam okuması harcanmış olur.
- **`DbConstraintViolations`, `Skinora.API` altındaki üç webhook middleware'inin kendi kopyalarını kapsamıyor** (`WebhookSignatureMiddleware`, `TelegramWebhookSignatureMiddleware`, `ResendWebhookSignatureMiddleware`). Onlar farklı assembly'de ve T128 kapsamı dışında; birleştirme ayrı bir hijyen işi.

## Notlar

### Kapılar

- **Adım -1 (Working tree hygiene):** temiz — `git status --short` boş çıktı, branch `main`.
- **Adım 0 (Main CI startup check):** son 3 main run'ın **üçü de** `success` — `31909528316` + `31909528307` (T127 #238), `31880715941` (T126 #236).
- **Bağımlılık:** T117 ✓ Tamamlandı (merge edilmiş; kısıt migration'da doğrulandı).

### Dış Varsayımlar (Adım 4)

| Varsayım | Kanıt |
|---|---|
| UNIQUE ihlali yakalama için repo emsali var | `PaymentAddressAllocator.cs:160-173` (T70) — SQL Server 2601/2627, SqlClient'a hard-reference vermeden reflection ile |
| Integration testler gerçek SQL Server üzerinde koşuyor (filtered UQ index testte de yürürlükte) | `IntegrationTestBase.cs:141` — Testcontainers `mssql/server:2022-latest`; CI'da `INTEGRATION_TEST_SQL_SERVER` ile job düzeyinde paylaşılan sunucu |
| Soft-delete global query filter var (ön-kontrol indeksin `IsDeleted = 0` bacağını aynalıyor) | `AppDbContext.cs:144` — `modelBuilder.Entity(entityType).HasQueryFilter(lambda)` |
| Kısıt üretim şemasında mevcut | `20260809162642_T117_P2P_Pivot.cs:179,198` — `UQ_Transactions_SellerId_ItemAssetId_Active` |
| FE 4 dil parity aracı var | `frontend/package.json:12` → `npm run i18n:check` |
| Migration / yeni paket / config gerekmiyor | Kolon veya indeks eklenmedi; `dotnet build` 0 uyarı, model snapshot drift yok |

**Kırık varsayım: yok.**

### Tasarım notları

- **`ChangeTracker.Clear()` neden yakalama dalında var:** `SaveChanges` atomiktir, dolayısıyla hiçbir satır yazılmadı — ama staged `Transaction`, `TransactionHistory`, `FraudFlag` ve `OutboxMessage` satırları scoped context'te `Added` olarak asılı kalır. Hedefli `Detach` çağrıları listesi, boru hattına yeni bir aşama eklendiğinde sessizce eskir; "bu iş birimi terk edildi" ifadesi tam olarak `Clear()`'dır.
- **Ön-kontrol ile indeks arasında sapma olursa** sonuç yanlış karar değil, reddedilmiş insert olur: indeks hakem, sorgu yalnızca satıcının cevabı duyma yolu. Bu yüzden sorgu indeksin filtresini altı terminal statü dahil birebir aynalıyor ve `IsDeleted` bacağını tekrarlamıyor (global filtre zaten uyguluyor) — tekrarlamak iki filtrenin ayrışabileceği izlenimi verirdi.
