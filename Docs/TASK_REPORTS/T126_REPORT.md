# T126 — POST /transactions/:id/confirm-receipt

**Faz:** F7 (P3 — Teslimat) | **Durum:** ✓ Tamamlandı — doğrulama ✓ PASS | **Tarih:** 2026-08-15

---

## Yapılan İşler

- **`POST /api/v1/transactions/:id/confirm-receipt` (07 §7.6b) implement edildi.** Alıcının "teslim aldım" onayı `PAYMENT_RECEIVED → ITEM_DELIVERED` geçişini üretir. Bu, `TransactionTrigger.DeliverItem`'ın **ilk üretim çağıranıdır** — T126 öncesinde `PAYMENT_RECEIVED`'dan çıkışın tek yolu iptal veya timeout'tu, yani P2P yaşam döngüsünün teslimat bacağının hiç girişi yoktu.
- **`DeliveryConfirmationService`** (yeni, `Application/Delivery/`): parti kapısı → idempotans → durum kapısı → hold kapısı → kanıt birleştirme → T125 doğrulama turu → launch kapısı invariantı → damgalama → `DeliverItem` → history + capture + outbox, hepsi **tek `SaveChanges`** içinde.
- **02 §9.2 kuralları yeniden yazılmadı, T125 motoruna delege edildi.** `BUYER_CONFIRMED` motora **girmeden önce** işleme alınır; motor kayıtlı onayı görüp short-circuit eder → **sıfır Steam okuması**, verdict `Delivered`, `AutoReleaseGated = false`.
- **Launch kapısı invariantı (T125 doğrulama bulgusu F3)** kodda gerçek bir dal olarak uygulandı: `AutoReleaseGated == true` dönen bir turda `DeliveryVerifiedAt` **damgalanmaz**, geçiş ateşlenmez ve **hiçbir şey yazılmaz**.
- `DeliveredBuyerAssetId` (06 §8.4) yalnız boşsa ve tur bir aday verdiyse yazılır — bu yolda daima null, T127/T130 için ileriye dönük.
- Controller action + DI kaydı + `.claude/CONTEXT.md` dosya haritası güncellendi.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryConfirmationService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryConfirmationService.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryConfirmationDtos.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Delivery/DeliveryConfirmationServiceTests.cs` (19 test)

**Değişen:**
- `backend/src/Skinora.API/Controllers/TransactionsController.cs` — `ConfirmReceipt` action + hata zarfı + sınıf XML'i
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — DI kaydı
- `backend/tests/Skinora.API.Tests/Integration/TransactionLifecycleEndpointTests.cs` — 6 endpoint testi + fixture seeder'a `SellerReadyConfirmedAt` / `DeliveryVerifiedAt` / `ItemDeliveredAt` (üçü de `status`'tan türetiliyor, yeni parametre yok)
- `.claude/CONTEXT.md` — Teslimat Doğrulama dosya haritası

**Yeni hata kodu yok, migration yok, config/env yok, yeni paket yok.**

## Yapım Öncesi Kararlar (proje sahibine soruldu, üçü de onaylandı)

| # | Soru | Karar | Gerekçe |
|---|---|---|---|
| 1 | T125 motoru çağrılsın mı, hangi sırada? | **Önce `BUYER_CONFIRMED`, sonra tur** | 02 §9.2 doğrulamanın "alıcı onay verdiğinde" çalışmasını şart koşuyor ve T125 XML'i T126'yı çağıran olarak adlandırıyor → tur çalışmalı. Ama onaydan **önce** okumak iki maliyet doğuruyordu: (a) kullanıcıya dönen POST'ta 1 req/s kuyruklu iki Steam okuması, (b) o turda `AutoReleaseGated = true` dönebileceği için F3 invariantına "`BUYER_CONFIRMED` varsa hariç" istisnası açmak — mekanik para-güvenliği kuralını koşullu hâle getirirdi. Onayı önce işlemek ikisini de kapatıyor. |
| 2 | Askıya alınmış (`IsSuspended`) alıcı çağırabilsin mi? | **Evet, guard yok** | Onay alıcının **kendi aleyhine**dir, yani 02 §14.0'ın kısıtladığı "fon çıkarma inisiyatifi" değil. Bloklamak teslim etmiş satıcıyı cezalandırırdı: kapı kapalıyken T127 turu `InventoryEvidencePendingReview` verip işlemi `PAYMENT_RECEIVED`'da bırakır, para kilitli kalır. T123 confirm-ready'de de guard yok. |
| 3 | `ITEM_DELIVERED` sonrası terminal durumlar (`COMPLETED` / `REFUNDED`) | **409 `INVALID_STATE_TRANSITION`** | 07 §7.6b idempotansı yalnız "zaten `ITEM_DELIVERED`" için tanımlıyor. `COMPLETED`/`REFUNDED` farklı bir gerçeği anlatır (ödendi / iade edildi); 200 dönmek uca teslimat onayı verdiği izlenimi bırakır. |

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Yalnız alıcı | ✓ | `Seller_Cannot_Confirm_Receipt`, `Third_Party_Cannot_Confirm_Receipt`, `Party_Guard_Answers_Before_The_State_Guard` (parti kapısı durum kapısından **önce** — yabancı bir çağıran durumu öğrenemez), endpoint: `ConfirmReceipt_Seller_Calling_Returns_403_NotAParty` |
| 2 | Yalnız `PAYMENT_RECEIVED` | ✓ | `Confirming_Outside_PaymentReceived_Is_Refused` — 5 durum (`CREATED`, `ACCEPTED`, `SELLER_CONFIRMED`, `COMPLETED`, `CANCELLED_TIMEOUT`) reddediliyor; `Emergency_Hold_Refuses_With_Its_Own_Code` (05 §4.5, `TRANSACTION_ON_HOLD`); endpoint: `ConfirmReceipt_Before_Payment_Returns_409` |
| 3 | İdempotent | ✓ | `Second_Call_Returns_The_Same_Answer_Without_Writing_Again` — ikinci çağrı `AlreadyDelivered`, **aynı** `DeliveryVerifiedAt` (saat 5 dk ilerletildiği hâlde yeniden damgalanmıyor), **tek** history satırı, **tek** outbox mesajı. `Already_Delivered_By_Inventory_Evidence_Returns_The_Recorded_Evidence` — envanter kanıtıyla teslim edilmiş bir işlemde tekrar çağrı kayıtlı kanıtı olduğu gibi döner, `BUYER_CONFIRMED` **eklenmez**. Endpoint: `ConfirmReceipt_Repeat_Is_Idempotent_And_Returns_200` |
| 4 | **LAUNCH KAPISI İNVARİANTI** — `AutoReleaseGated == true` turda `DeliveryVerifiedAt` damgalanmaz; test: kapı kapalı + envanter kanıtı tam → `DeliveryVerifiedAt` NULL kalır ve `CanDeliverItem` false döner | ✓ | `Gated_Round_Leaves_The_Delivery_Guard_Shut` — **gerçek** motor, kapı kapalı (seed default), `SELLER_ASSET_GONE ∧ INVENTORY_DELTA` tam: `AutoReleaseGated` true, `IsSufficientForDelivery()` true; kanıt persist edilip damga tutulunca `DeliveryVerifiedAt` NULL ve `CanFire(DeliverItem)` **false**. Test ayrıca **nedenselliği** sabitler: aynı satırda damga konunca `CanFire` true olur — kapıyı fiilen tutan alanın o olduğu kanıtlanır. Ek olarak `Gated_Verdict_Refuses_Instead_Of_Stamping` savunma dalını sürer (aşağıda). |
| 5 | (Parantez) `BUYER_CONFIRMED` yolu kapıdan etkilenmez | ✓ | `Closed_Gate_Does_Not_Restrain_The_Buyer_Path` — kapı kapalı + envanter kanıtı kayıtlı bir işlemde alıcı onayı **yine de** teslim ediyor; kalan kanıt üç bayrağın birleşimi |

### Launch kapısı dalı nasıl test edildi

`BUYER_CONFIRMED` tur **öncesi** işlendiği için `Decide()` daima `Delivered` döner → bu uçtan `AutoReleaseGated = true` **yapısal olarak erişilemez**. Dal yine de gerçek bir `if` olarak yazıldı (varsayım başka bir sınıfta yaşıyor; motorun kapı kuralları genişlerse bu uç para bırakmak yerine reddetmeli) ve şöyle test edildi: **gerçek motordan gerçek bir gated sonuç üretilir**, sonra o sonucu aynen döndüren bir `IDeliveryVerificationService` uca verilir. `DeliveryVerificationResult` ctor'ı `internal` olduğu için sonuç elle kurulamaz — bu yöntem hem o kısıtı aşar hem de sentetik değil **gerçekten üretilebilen** bir sonucu sürer. Sonuç: 409, `Status` hâlâ `PAYMENT_RECEIVED`, `DeliveryVerifiedAt` NULL, `DeliveryEvidence` `NONE` (onay bayrağı bile yazılmaz), outbox boş, history boş.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (tüm süit) | ✓ 1379/1379 | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — 11 assembly, 0 fail |
| Integration (tüm süit) | ✓ 1234/1234 | `dotnet test Skinora.sln --filter "FullyQualifiedName~.Integration"` — 10 assembly, 0 fail |
| T126 servis (yeni) | ✓ 19/19 | `--filter "FullyQualifiedName~DeliveryConfirmationServiceTests"` |
| T126 endpoint (yeni) | ✓ 6/6 | `TransactionLifecycleEndpointTests` içinde; dosyanın tamamı 49/49 |
| Build | ✓ | `dotnet build` — 0 Warning, 0 Error |

**Flake notu:** ilk tam integration turunda `Skinora.Platform.Tests` 63/65 verdi. Tek başına çalıştırıldığında 65/65, tam süit tekrarında da temiz — solution genelinde paralel test veritabanı çekişmesi kaynaklı, T126 ile ilgisi yok (Platform.Tests bu görevin dokunduğu hiçbir dosyayı kullanmıyor).

## Altyapı Değişiklikleri

- Migration: **Yok** — yeni kolon/tablo yok; T125'in `DeliveryEvidenceCaptures` tablosu ve launch kapısı seed'i (index 60) zaten mevcut.
- Config/env değişikliği: **Yok**
- Docker değişikliği: **Yok**

## Mini Güvenlik Kontrolü (Katman 1)

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | Yok — yeni sır, anahtar veya bağlantı dizesi eklenmedi |
| Auth/authorization | Yeni uç `[Authorize(Policy = AuthPolicies.Authenticated)]` + servis içinde **alıcı-only** parti kapısı. Parti kapısı durum kapısından önce çalışır → id probe eden bir yabancı durum bilgisi sızdıramaz. `[RateLimit("user-write")]` diğer yazma uçlarıyla aynı. |
| Input validation | Gövde yok; tek girdi route'taki `{id:guid}` (framework doğruluyor) ve JWT'den gelen `userId`. Serbest metin alanı yok. |
| Yeni dış bağımlılık | Yok |

## Commit & PR

- Branch: `task/T126-confirm-receipt`
- Commit: `ca89286` — T126: POST /transactions/:id/confirm-receipt
- PR: [#236](https://github.com/turkerurganci/Skinora/pull/236)
- CI: **✓ PASS** — run [`31876488303`](https://github.com/turkerurganci/Skinora/actions/runs/31876488303), **CI Gate `success`**. Bloke edici 12 job yeşil (Detect paths · 1. Lint · 2. Build · 3. Unit · 4. Integration · 5. Contract · 6. Migration dry-run · 7. Docker build backend · CI Gate); `0. Guard` skipped (PR event), `3b. JS test (vitest)` skipped (frontend/sidecar yolu değişmedi).
- **Advisory E2E (8 leg) kırmızı — T126 kaynaklı DEĞİL.** İmza `RequestError: Invalid object name 'PlatformSteamBots'` (+ ardıl `PK_Users` çakışması): E2E seed'i T117'de silinen bot tablosuna bakıyor. Aynı 8 leg main'in son run'ında da (`31840953171`, chore T125 doğrulama bulguları — genel sonucu `success`) kırmızı, yani **pre-existing**. T126 hiçbir E2E yüzeyine, bot koduna veya migration'a dokunmuyor. Bu, T125 raporunda da aynı gerekçeyle kaydedilen kırmızılığın devamıdır.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** (bağımsız chat, 2026-08-15, commit `563dfa0`) |
| Bulgu sayısı | 2 (ikisi de kabul kriteri ihlali değil; ikisi de düzeltildi) |
| Düzeltme gerekli mi | Yapıldı — F1 ayrı chore PR [#237](https://github.com/turkerurganci/Skinora/pull/237), F2 bu dalda |

**Kapılar:** Adım −1 working tree temiz · Adım 0 main CI son 3 run (`31840953171`, `31840953115`, `31838962660`) hepsi `success` · Adım 0b repo memory T126 satırı mevcut · Adım 8a task branch CI run [`31877015493`](https://github.com/turkerurganci/Skinora/actions/runs/31877015493) **CI Gate `success`**, 12 bloke edici job yeşil.

**Kabul kriterleri bağımsız verdict'i:** 5/5 ✓ — yapım raporundaki verdict'lerle **tam uyum**, uyuşmazlık yok. Doğrulayıcı AC4'ü (launch kapısı invariantı) `Gated_Round_Leaves_The_Delivery_Guard_Shut`'ın hem invariantı hem **nedenselliği** (damga konunca `CanFire` true olur) sabitlediğini teyit ederek geçirdi.

**Bağımsız test koşumu:** build 0E/0W · backend tam suite **2622/2622** (13 assembly). İlk birleşik koşuda 40 kırılma çıktı; **rasyonalize edilmedi, ölçüldü** — hepsinin imzası `IntegrationTestBase.CreateDatabaseAsync` → `SqlException: Execution Timeout Expired` (11 assembly paralel, DB oluşturma darboğazı). Dört kırmızı assembly **sırayla izole** yeniden koşuldu: Auth 120/120, API 538/538, Notifications 171/171, Transactions 925/925 — hepsi temiz. Bu, T125 doğrulamasında kayda geçen aynı yük artefaktının tekrarıdır.

**Advisory E2E (8 leg):** task branch ve main (`1b4da1c`) failed loglarının imzası **birebir aynı** (`Invalid object name 'PlatformSteamBots'` ×8 + aynı 3 test adı) → pre-existing, doğrulayıcı tarafından bağımsız karşılaştırmayla teyit edildi.

**Güvenlik:** temiz — secret yok, authz alıcı-only + parti kapısı durum kapısından önce, gövde yok (tek girdi route `{id:guid}` + JWT), `[RateLimit("user-write")]` kardeş uçlarla aynı, yeni bağımlılık yok.

### Bulgu F1 — S1, para güvenliği (merge öncesi kapatıldı, PR #237)

**Bulgu:** T126, `TransactionTrigger.DeliverItem`'ın ilk üretim çağıranı olduğu için `ITEM_DELIVERED`'ı **ilk kez erişilebilir** yapıyor (`git grep -n "TransactionTrigger.DeliverItem" origin/main -- backend/src` yalnız state machine tanımını döndürüyor). Ama `SellerPayoutQueueJob` (dakikalık recurring) `ITEM_DELIVERED` işlemleri **`PayoutEligibleAt` / `SettlementVerifiedAt` filtresi olmadan** alıyor ve `NextAttemptAt = null` ile dispatch'e veriyordu. Sonuç: alıcı onayından ~1 dk sonra zincire payout gider; 02 §4.5.1'in 8 günlük mutabakat penceresi ve sonundaki envanter kontrolü tamamen atlanır → Steam'in 7 günlük geri alma vektörü açık kalır. Ardından `Complete` guard'ı `SettlementVerifiedAt` istediği için işlem `ITEM_DELIVERED`'da kilitlenirdi: para gitmiş, durum ilerlemiyor.

**Neden T126'nın AC ihlali değil:** filtreyi plan açıkça T129'a veriyor (11 §P4). Bu bir **sıralama** bulgusu — T126 üreticiyi, tüketici kapatılmadan açıyor.

**Karar (proje sahibi, 2026-08-15):** merge öncesi ara kapı. `SellerPayoutQueueJob` sorgusu + döngü içi yeniden doğrulama `PayoutEligibleAt != null && <= now` okuyor; NULL'da **fail-closed** (T129 kolonu yazana kadar job hiçbir şey kuyruğa almaz, işlemler `ITEM_DELIVERED`'da bekler). 2 yeni test + T129 kabul kriterine "erken uygulandı" notu.

**KALICI DERS (T124 dersinin ikizi):** bir üreticiyi açan görev, açtığı **değerin tüketicilerinin** kapılı olduğunu da doğrulamalı. T126 kendi kabul kriterlerinin hepsini karşıladığı hâlde uyandırdığı tüketici kapısızdı — kabul kriteri listesi bu sınıf hatayı yakalamaz, yalnız "bu geçişi kim tüketiyor?" sorusu yakalar.

### Bulgu F2 — S1, doküman-kod sapması (bu dalda düzeltildi)

`BuyerConfirmedReceiptAt` kolonu `Transaction.cs:102`'de mevcut (T117 migration) ve 06 §3.5'te tanımı aynen *"Alıcının 'teslim aldım' onayını verdiği an"*. T126 tam olarak o eylemi implement ediyor ama kolonu yazan tek yer olmadığı hâlde yazmıyordu — repoda hiçbir yazıcı yoktu. **Düzeltme:** Stage 7'de `DeliveryVerifiedAt` ile birlikte damgalanıyor (Stage 5 yerine Stage 7, ki gated dal hiçbir şey yazmama özelliğini korusun) + 5 assertion. İki kolon yalnız bu yolda aynı değeri taşır: `DeliveryVerifiedAt` teslimatın **herhangi bir yoldan** doğrulandığı anı, bu kolon yolun **alıcının kendi sözü** olduğunu söyler — T127/T130 turları ilkini damgalayıp bunu NULL bırakacak.

> Not: çok-ajanlı bağımsız taramada iki doğrulayıcı F2'yi "kolon zorunlu alan matrisinde değil" gerekçesiyle reddetti; kolonun varlığı ve yazıcısızlığı `grep` ile ayrıca teyit edilerek red düzeltildi.

### Bilgi düzeyinde (bulgu sayılmadı)

- Gated dal `SaveChanges` olmadan dönerken tracked entity üzerinde `BUYER_CONFIRMED` kirli kalıyor. Bu route'un request pipeline'ında `AppDbContext`'i flush eden başka bileşen olmadığı doğrulandı (yalnız webhook signature middleware'leri `SaveChanges` çağırıyor, farklı route'lar) → bugün latent.
- Endpoint fixture'ı `ITEM_DELIVERED` / `COMPLETED` satırlarını `DeliveryEvidence = NONE` ile tohumluyor; 06 §3.5 matrisi bu durumlar için `!= NONE` istiyor. Yalnız test verisi sadakati.
- Durum kapısı Theory'sinde `REFUNDED` yok (kod aynı dalla karşılıyor).

## Known Limitations / Follow-up

- **Mutlu yolda launch kapısı örneklemi üretilmez.** Alıcı onaylı bir tur envanter okumadığı için `DeliveryEvidenceCaptures`'a satır yazmaz. DEPLOY_RUNBOOK §H'nin "ilk N gerçek teslimat" örneklemi bu yüzden **yalnız T127'nin timeout öncesi turlarından** (ve T130 dispute turlarından) gelecek — yani alıcının onaylamadığı işlemlerden. Bu T125'in bilinçli tasarımının sonucudur (onay sonrası Steam okumak "daha zayıf bir sinyali daha güçlüsüyle tartıştırmak"); T127 devreye girmeden kapı açma kararı için veri birikmez.
- **`DeliveredBuyerAssetId` bu yolda daima NULL** (06 §8.4). Kod aday geldiğinde yazacak şekilde bağlandı ama short-circuit aday üretmiyor. `WRONG_ITEM` dispute'unda asset ayrımı bu işlemler için baseline listesine dayanacak.
- **`CancelTimeoutJobsAsync` çağrılmıyor.** Ödeme/uyarı Hangfire job'ları `ConfirmPayment` anında zaten silinmişti; teslimat penceresi scanner sürümlü bir kolon (05 §4.4) ve scanner sorgusu `PAYMENT_RECEIVED` filtreliyor, dolayısıyla durumdan çıkmak işlemi sorgudan düşürüyor. `DeliveryDeadline` satıcının fiilen sahip olduğu pencerenin tarihsel kaydı olarak **bırakılıyor**.
- **FE bağlantısı T135'te.** Uç hazır ama `StateActionPanel`'de `PAYMENT_RECEIVED` + alıcı için "aldım" butonu T135'in kapsamı.

## Notlar

- **Working tree:** temiz (`git status --short` boş, `main` üzerindeydi).
- **Adım 0 — Main CI startup check:** son 3 run `31840953171` ✓ success · `31840953115` ✓ success · `31838962660` ✓ success. Hepsi `conclusion=success` → task başlatıldı.
- **Bağımlılık:** T125 ✓ Tamamlandı (doğrulama ✓ PASS, main `1b4da1c`).
- **Dış varsayımlar (task.md Adım 4):**
  1. *Yeni paket / dış API gerekmiyor* — ✓ doğrulandı: eklenen kod yalnız mevcut portları (`IDeliveryVerificationService`, `IOutboxService`, `AppDbContext`, `TimeProvider`) kullanıyor; `.csproj` dosyalarına dokunulmadı.
  2. *`TransactionTrigger.DeliverItem` bugün üretimde çağrılmıyor* — ✓ doğrulandı: `grep -rn "TransactionTrigger.DeliverItem" backend/src/` yalnız state machine tanımını (`TransactionStateMachine.cs:265`) döndürdü. T126 ilk çağıran, dolayısıyla geçiş için regresyon riski taşıyan mevcut bir tüketici yok.
  3. *T125 launch kapısı seed'i ve kanıt tablosu mevcut* — ✓ doğrulandı: `SystemSettingSeed.cs:170` (index 60, `false`) ve `20260814182849_T125_DeliveryEvidenceCapture` migration'ı repoda.
  4. *`ITEM_DELIVERED` geçişi bir bildirim tüketicisi tetiklemiyor* — ✓ doğrulandı: `EscrowedAndTradeOfferNotificationConsumer` yalnız `SELLER_CONFIRMED` ve `PAYMENT_RECEIVED` `ToStatus`'larına tepki veriyor (`:57-61`); 03 §3.5 md.9 zaten ITEM_DELIVERED için ayrı inbox/email tipi olmadığını söylüyor. `TransactionStatusChangedEvent` yayını yalnız WP9 realtime relay'ini besliyor.
- **Yerleşim kararı (GUARDRAILS §7):** servis + DTO'lar `Application/Lifecycle/` yerine `Application/Delivery/` altına kondu. Gerekçe: sınıf `IDeliveryVerificationService`'e bağımlı ve T127/T130 aynı kümeye gelecek; Lifecycle'a koymak T125'in teslimat kümesini ikiye bölerdi.
- **Test fixture düzeltmesi:** `TransactionLifecycleEndpointTests` seeder'ı `SellerReadyConfirmedAt`'i hiç doldurmuyordu. `DeliverItem` guard'ı bunu `HasFieldsForSellerConfirmed()` üzerinden okuduğu için `PAYMENT_RECEIVED` fixture'ı hiçbir zaman teslim edemezdi. 06 §3.5'in bracket'ine uygun şekilde `SELLER_CONFIRMED` ve sonrası için dolduruldu — mevcut 49 testin hepsi yeşil kaldı.
