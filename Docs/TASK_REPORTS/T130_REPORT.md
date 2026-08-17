# T130 — DisputeEligibility + AutoChecker yeniden yazımı

**Faz:** F7 (P5 — Dispute) | **Durum:** ✓ Tamamlandı (doğrulama ✓ PASS) | **Tarih:** 2026-08-17

---

## Yapılan İşler

Teslim ve yanlış-item itirazlarının otomatik kontrolü, kayıtlı bayrakları çıplak
okumaktan çıkarılıp **taze 02 §9.2 turuna** bağlandı. Üç yapısal kusur kapandı:

1. **Launch kapısı çıkmazı.** `delivery.inventory_evidence_auto_release_enabled`
   kapalıyken envanter kanıtı işlemde birikiyor ama para bırakmıyor. Eski checker
   bunu `IsSufficientForDelivery()` ile okuyup dispute'u `Resolved: true` +
   `CanEscalate: false` ile **CLOSED** açıyordu: otomatik yol kapılı, elle yol
   kapalı — alıcının parasının hiçbir çıkışı yoktu. Artık kapı kapalıyken dispute
   **OPEN kalıyor ve eskale edilebiliyor** (03 §6.2 Sonuç E).
2. **Yanlış-teslimat imzası otomatik eskale olmuyordu.** 03 §6.2 Sonuç C
   "kullanıcı aksiyonu beklenmez" diyor; eski checker dispute'u OPEN bırakıp
   alıcıdan buton beklemesini istiyordu — hiçbir şey ulaşmadığı için durumdan
   haberi bile olmayabilecek alıcıdan.
3. **Çıplak bayrak "bakamadım"ı ifade edemiyordu.** `IsMisdeliverySignature()`
   alıcı envanteri okunamadığında da true dönüyor; motor bunu
   `sellerSideKnown && buyerSideKnown` ile niteliyor. Artık **her mesaj verdict'ten**
   seçiliyor, bayraktan değil.

Ayrıca yapım öncesi ön-uçuşta çıkan bloke edici bulgu kapatıldı (aşağıda).

## Yapım Öncesi Bulgu — WRONG_ITEM uyuşmazlık dalı erişilemezdi (SPEC_GAP)

Kabul kriteri 3 ("gelen item'ın adı admin'e kanıt olarak taşınıyor") mevcut
veriyle **karşılanamıyordu**:

| # | Kanıt | Sonuç |
|---|---|---|
| 1 | `TransactionReadinessService.cs:198` — `CaptureClassBaselineAsync(steamId, ItemClassId, ItemInstanceId, …)` | Baseline **sınıf-kapsamlı** |
| 2 | `DeliveryVerificationService.cs:195` — `CandidateDeliveredAssetId` o sınıfın yeni asset'i | Aday hep işlemin sınıfından |
| 3 | `DeliveryConfirmationService.cs:180` + `DeliveryTimeoutRound.cs:213` — `DeliveredBuyerAssetId`'nin tek iki yazarı | Kolon yalnız doğru sınıfla dolabilir |
| 4 | `WrongItemDisputeAutoChecker.cs:93` — `snapshot.ClassId == transaction.ItemClassId` | **Her zaman eşleşir** → `AutoEscalated` dalı ölü kod |
| 5 | Yanlış sınıf geldiğinde sayaç hiç artmaz | Kolon NULL kalır → checker `NoDelivery` döner |

06 §3.5 satır 617 `BuyerBaselineAssetIds`'i "yanlış item tespitinde sonradan
gelen asset'i ayırmak için" diye tanımlıyordu; sınıf-kapsamlı bir baseline bunu
yapamaz — yanlış item tanımı gereği farklı bir sınıftır.

**Proje sahibi kararları (2026-08-17):**

| # | Karar |
|---|---|
| D1 | 03 §6.2'ye **Sonuç E** eklendi (kapı kapalı + kanıt var → OPEN + eskale edilebilir), yeni mesaj anahtarı `DELIVERY_EVIDENCE_UNDER_REVIEW`, DEPLOY_RUNBOOK §H.2'ye dispute satırı |
| D2 | Gelen item'ın adı **`Disputes.DeliveredItemName` kolonuna** yazılır — `SystemCheckResult` içine gömülmez, çünkü o metin alıcının dilinde üretiliyor ve admin o dili okumayabilir |
| D3 | **`Transactions.BuyerBaselineClassIds`** eklendi: SELLER_CONFIRMED'da alıcı envanterinin tüm sınıf kimlikleri kaydedilir, dispute anında taze okuma ile diff alınır |

D3'ün maliyeti beklenenden düşük: sidecar zaten **her istekte tüm envanteri**
döndürüyor (`SidecarSteamInventoryReader.cs:48` ve `:115` aynı
`GetInventoryAsync`'i çağırıp istemci tarafında filtreliyor), dolayısıyla
parmak izi **ek Steam çağrısı getirmiyor** — aynı cevabın farklı projeksiyonu.

## Mimari

`IDeliveryDisputeRound` portu **Transactions** modülüne kondu (T127'nin
`IDeliveryTimeoutRound` kalıbı): Sonuç A kolu `DeliverItem` tetikliyor, yani
state machine + `SettlementWindowStamper` + `TransactionHistoryRecorder` işi —
hepsi Transactions tarafı. Disputes modülü kendi işini tutuyor: alıcıya ne
söyleneceği ve dispute'un kapanıp kapanmayacağı.

| Verdict | `DeliveryDisputeOutcome` | Dispute sonucu | Mesaj |
|---|---|---|---|
| `Delivered` | `Delivered` | CLOSED + `DeliverItem` (→ ITEM_DELIVERED) | `DELIVERY_DELIVERED` |
| `InventoryEvidencePendingReview` | `PendingReview` | **OPEN + eskale edilebilir** | `DELIVERY_EVIDENCE_UNDER_REVIEW` (yeni) |
| `MisdeliverySignature` | `MisdeliverySignature` | **ESCALATED** (iki tarafa bildirim) | `DELIVERY_ASSET_GONE_NOT_ARRIVED` |
| `NoMovement` | `NotSent` | OPEN + eskale edilebilir | `DELIVERY_NOT_SENT` |
| `Inconclusive` | `Unreadable` | OPEN + eskale edilebilir | `DELIVERY_INVENTORY_UNREADABLE` (yeni) |

Tur **hiçbir kolda iptal üretmez** — bu, timeout turundan ayıran kural: alıcı bir
soru sordu, cevabı işlemini iptal etmek olamaz. Tur `SaveChanges` çağırmaz;
dispute servisi tek unit of work'te commit eder, böylece dispute satırı ve
tetiklediği geçiş birlikte iner ya da hiç inmez.

`MisdeliveryDisputeEscalator` bu yolda **çağrılmaz**: dispute satırını zaten
çağıran oluşturuyor ve `UQ_Disputes_TransactionId_Type` tek satıra izin veriyor.
Escalator, hiç dispute olmayan timeout tarayıcısının yolu olarak kalıyor.

## Etkilenen Modüller / Dosyalar

**Yeni**
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryDisputeRound.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryDisputeRound.cs`
- `backend/src/Skinora.Shared/Persistence/Migrations/20260817150125_T130_WrongItemEvidenceColumns.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/Delivery/DeliveryDisputeRoundTests.cs`

**Değişen — kaynak**
- `Skinora.Transactions/Application/Steam/ISteamInventoryReader.cs` — `CaptureInventoryFingerprintAsync` + `InventoryFingerprintResult`/`InventoryFingerprintEntry`; `InventoryClassBaselineResult`'a `InventoryClassIds`
- `Skinora.Steam/Application/Inventory/SidecarSteamInventoryReader.cs` — iki projeksiyon
- `Skinora.Transactions/Application/Steam/StubSteamInventoryReader.cs`
- `Skinora.Transactions/Domain/Entities/Transaction.cs` + `Infrastructure/Persistence/TransactionConfiguration.cs` — `BuyerBaselineClassIds`
- `Skinora.Transactions/Application/Lifecycle/TransactionReadinessService.cs` — parmak izini yazar
- `Skinora.Disputes/Domain/Entities/Dispute.cs` + `Infrastructure/Persistence/DisputeConfiguration.cs` — `DeliveredItemName`
- `Skinora.Disputes/Application/AutoCheckers/DeliveryDisputeAutoChecker.cs` — **yeniden yazıldı**
- `Skinora.Disputes/Application/AutoCheckers/WrongItemDisputeAutoChecker.cs` — **yeniden yazıldı**
- `Skinora.Disputes/Application/AutoCheckers/IDisputeAutoCheckers.cs` — `AutoCheckResult.DeliveredItemName`
- `Skinora.Disputes/Application/AutoCheckers/DisputeAutoCheckMessages.cs` — 3 yeni anahtar × 4 dil
- `Skinora.Disputes/Application/Disputes/DisputeService.cs` — kolonu satıra kopyalar; eski enum'ları anan XML yorumu düzeltildi
- `Skinora.Disputes/Application/Admin/AdminDisputeDtos.cs` + `Skinora.API/Services/AdminDisputeService.cs` — AD28 alanı
- `Skinora.API/Configuration/TransactionsModule.cs` — `IDeliveryDisputeRound` kaydı

**Değişen — doküman**
- `02_PRODUCT_REQUIREMENTS.md` §10.1 — teslim itirazı satırına kapı istisnası
- `03_USER_FLOWS.md` §6.2 — **Sonuç E**; §6.3 — referans noktası + tek-sınıf adlandırma kuralı
- `06_DATA_MODEL.md` §3.5 — `BuyerBaselineClassIds`, 617 satırının düzeltilmesi; §3.11 — `DeliveredItemName`
- `07_API_DESIGN.md` AD28 — `deliveredItemName`
- `DEPLOY_RUNBOOK.md` §H.2 — dispute satırı
- `11_IMPLEMENTATION_PLAN.md` T130 — bulgu + D1/D2/D3 kaydı

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | "Satıcı başka yere gönderdi" imzası auto-escalate ediyor | ✓ | `DeliveryDisputeAutoCheckerTests.MisdeliverySignature_AutoEscalates` · `DisputeServiceTests.Open_Delivery_MisdeliverySignature_AutoEscalates_AndNotifiesBothParties` (ESCALATED + `DisputeEscalatedEvent{AutoEscalated=true}`, iki taraf) |
| 2 | WRONG_ITEM PAYMENT_RECEIVED'dan da açılabiliyor | ✓ | Kapı `DisputeEligibility` (T117) zaten açıktı; T130 motoru anlamlı kıldı: `Open_WrongItem_DifferentClassArrived_AutoEscalates_AndRecordsItsName` PAYMENT_RECEIVED'da koşuyor ve eskale ediyor. Önceki checker bu durumda `DeliveredBuyerAssetId` NULL olduğu için hep `NoDelivery` dönüyordu |
| 3 | Yanlış item vakasında gelen item'ın adı admin'e kanıt olarak taşınıyor | ✓ | Aynı test: `dispute.DeliveredItemName == "AWP | Asiimov (Field-Tested)"`; AD28 DTO alanı + 07 §9.x. Belirsizlikte ad yazılmaz: `Open_WrongItem_SeveralClassesArrived_Escalates_WithoutNamingOne` |
| 4 | LAUNCH KAPISI ÇIKMAZI kapandı — kapı kapalıyken auto-checker "teslim edildi" üretmiyor, dispute OPEN + eskale edilebilir kalıyor | ✓ | `DeliveryDisputeAutoCheckerTests.LaunchGateClosed_DoesNotResolveAsDelivered_AndKeepsTheEscalationRoute` · `DisputeServiceTests.Open_Delivery_InventoryEvidence_LaunchGateClosed_StaysOpenAndEscalatable` (OPEN, `CanEscalate`, `DeliveryVerifiedAt` NULL, `PayoutEligibleAt` NULL) · `DeliveryDisputeRoundTests.GateClosed_Holds_For_Review_Without_Stamping_The_Guard` |
| 5 | İkinci madde: mesaj seçimi motorun verdict'ine bağlandı (bayrağa değil) | ✓ | Checker artık `DeliveryEvidence`'ı hiç okumuyor — tek girdisi `DeliveryDisputeOutcome`. `Open_Delivery_UnreadableBuyerInventory_StaysOpen_WithoutBlamingTheSeller`: çıplak `SELLER_ASSET_GONE` + okunamayan alıcı tarafı artık `DELIVERY_INVENTORY_UNREADABLE`, `DELIVERY_NOT_SENT` değil |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend tam suite | ✓ 2754/2754 | `dotnet test` — exit 0 (önceki taban 2732, +22) |
| Disputes | ✓ 79/79 | `Skinora.Disputes.Tests` |
| Transactions | ✓ 1022/1022 | `DeliveryDisputeRoundTests` 11 test dahil |
| API | ✓ 540/540 | AD28 DTO değişimi regresyonsuz |
| Build | ✓ 0 error / 0 warning | `dotnet build` |

Yeni testler: `DeliveryDisputeRoundTests` (11), `DeliveryDisputeAutoCheckerTests`
(yeniden yazıldı — 5 kol + kapsayıcılık teorisi + i18n), `DisputeServiceTests`
DELIVERY bloğu (5 senaryo) + WRONG_ITEM bloğu (7 senaryo).

## Altyapı Değişiklikleri

- **Migration:** `20260817150125_T130_WrongItemEvidenceColumns` — iki additive nullable kolon (`Transactions.BuyerBaselineClassIds` nvarchar(max), `Disputes.DeliveredItemName` nvarchar(200)). Veri taşıma yok, geri alınabilir.
- **Config/env:** Yok.
- **Docker:** Yok.

## Doğrulama

**Bağımsız doğrulama (2026-08-17, ayrı chat, yapım raporu görülmeden, dal HEAD `7aafaf0`).**

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** |
| Kabul kriteri | 5/5 ✓ (bağımsız olarak yeniden üretildi) |
| Bulgu sayısı | 2 — **ikisi de bloke etmeyen** (N1 doküman başlığı, N2 sahipsiz FE yüzeyi) |
| Düzeltme gerekli mi | Merge için hayır; N1/N2 için proje sahibi kararı |

### Kapı kontrolleri

| Adım | Sonuç |
|---|---|
| −1 Working tree | ✓ Temiz (`git status --short` boş) |
| 0 Main CI (son 3 run) | ✓ 3/3 `success` — `32039187802`, `32039187921`, `32033733318` |
| 0b Repo memory drift | ✓ `.claude/memory/MEMORY.md` satır 51'de T130 kaydı mevcut |
| 8a Task dalı CI | ✓ Dal HEAD `7aafaf0` run [`32046789416`](https://github.com/turkerurganci/Skinora/actions/runs/32046789416) — bloke edici 9 job yeşil, CI Gate `success` |

### Kabul kriterleri — validator kanıtı

| # | Kriter | Sonuç | Bağımsız kanıt |
|---|---|---|---|
| 1 | Yanlış-teslimat imzası auto-escalate ediyor | ✓ | `DeliveryDisputeAutoChecker.cs:72-73` → `AutoEscalated`; `DisputeService.cs:161-165` `AutoEscalated → DisputeStatus.ESCALATED`, `:208-221` `DisputeEscalatedEvent{AutoEscalated=true}` iki tarafa. Motorun `MisdeliverySignature` kolu `sellerSideKnown && buyerSideKnown` ile nitelenmiş (`DeliveryVerificationService.cs:271`) |
| 2 | WRONG_ITEM PAYMENT_RECEIVED'dan açılabiliyor | ✓ | `DisputeEligibility.cs:38-42` — `origin/main`'de de mevcut (T117, `82bff4d`), yani kapı T130 öncesinde açıktı; T130 arkasındaki motoru işler kıldı (`Open_WrongItem_DifferentClassArrived_...` PAYMENT_RECEIVED'da eskale ediyor) |
| 3 | Gelen item'ın adı admin'e kanıt olarak taşınıyor | ✓ | Zincir uçtan uca doğrulandı: `WrongItemDisputeAutoChecker.cs:133,151` → `AutoCheckResult.DeliveredItemName` → `DisputeService.cs:180` → `Dispute.DeliveredItemName` (nvarchar(200), migration + snapshot senkron) → `AdminDisputeService.cs:181` → AD28 `deliveredItemName`. Belirsizlikte NULL (`distinctClasses == 1` koşulu). **Not:** API sözleşmesinde tam; admin **arayüzünde** yüzeye çıkmıyor → N2 |
| 4 | Launch kapısı çıkmazı kapandı | ✓ | `DeliveryDisputeRound.cs:210-224` (`HoldForReview`) `DeliveryVerifiedAt` **damgalamıyor**; `DeliveryDisputeAutoChecker.cs:85-86` `PendingReview → Unresolved` ⇒ `Resolved:false`, `CanEscalate:true` ⇒ `DisputeStatus.OPEN`. `DeliverItem` reddedilirse de (05 §4.5 hold) alan-alan rollback + aynı `PendingReview` (`:146-169`). Yerel koşum: `Open_Delivery_InventoryEvidence_LaunchGateClosed_StaysOpenAndEscalatable` ✓ |
| 5 | Mesaj seçimi verdict'e bağlandı (bayrağa değil) | ✓ | `grep "IsMisdeliverySignature()\|IsSufficientForDelivery()" backend/src` → Disputes modülünde **yalnız XML yorumu**; tek çalışan okuma motorda (`DeliveryVerificationService.cs:256,271`) ve state machine guard'ında. Checker'ın tek girdisi `DeliveryDisputeOutcome` |

### Validator'ın bağımsız koştuğu testler

| Tür | Sonuç | Komut / kanıt |
|---|---|---|
| Unit (tüm solution) | ✓ **1433/1433**, exit 0 | `dotnet test Skinora.sln -c Release --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Integration (tüm solution) | ✓ **1312/1312**, exit 0 | Aynı filtre `~.Integration`, lokal SQL Server 2022 container (`INTEGRATION_TEST_SQL_SERVER`) |
| Contract | ✓ | CI job `5. Contract test` — run `32046789416` |
| Migration dry-run | ✓ | CI job `6. Migration dry-run`; ayrıca `AppDbContextModelSnapshot` diff'i migration ile birebir |

Toplam bağımsız yeşil: **2745** (unit + integration); raporun 2754 rakamı contract testlerini de sayıyor — tutarlı.

### Güvenlik kontrolü

| Alan | Sonuç |
|---|---|
| Secret sızıntısı | Temiz — yeni secret/credential yok |
| Auth / authorization | Temiz — dispute açma hâlâ yalnız alıcı (`DisputeService` Stage 2); `deliveredItemName` yalnız `VIEW_DISPUTES` gerektiren AD28'de, alıcı yüzeyinde yok |
| Input validation | Temiz — üçüncü taraf kaynaklı item adı 200 karaktere kırpılıyor (`Truncate`), kolon `nvarchar(200)` ile sınırlı |
| Yeni dış bağımlılık | Yok — `CaptureInventoryFingerprintAsync` mevcut sidecar cevabının farklı projeksiyonu (ek Steam round-trip yok, kod üzerinden doğrulandı) |
| Fail-closed davranışı | ✓ Okunamayan envanter hiçbir kolda olumsuz bulguya çevrilmiyor (08 §2.3); baseline yoksa karşılaştırma yapılmıyor |

### Bulgular (ikisi de bloke etmiyor)

| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| N1 | S1 Sapma | T130 dört kaynak dokümanı (02 §10.1, 03 §6.2/§6.3, 06 §3.5/§3.11, 07 AD28 — ikisi **şema/sözleşme** değişikliği) değiştirdi ama hiçbirinin **sürüm/`Son güncelleme` başlığını** güncellemedi. T129 aynı dokümanlarda satır 3'ü güncellemişti (`2dbd0c1`), yani konvansiyon yerleşik. Sonuç: 06 hâlâ "v6.7 · T129" diyorken iki T130 kolonu taşıyor; audit ve GPT cross-review turları bu başlıkları okuyor | `Docs/02_…`, `Docs/03_…`, `Docs/06_…`, `Docs/07_…` (satır 3) |
| N2 | S3 Eksik | `deliveredItemName` AD28 cevabına kadar geliyor ama admin **arayüzünde** hiçbir yerde gösterilmiyor: `AdminDisputeDetail` TS tipinde alan yok, `DisputeResolveModal.tsx` yalnız `systemCheckResult` + `dispute.itemName` render ediyor. 03 §6.3 Sonuç B "admin karşılaştırmayı elle yapmak zorunda kalmaz" ve 07 AD28 notu "yan yana görür" diyor — bugün admin'in bunu görmesi için ham API'ye bakması gerekiyor. **Sahibi yok:** raporun §Notlar'da işaret ettiği T134 (enum/StatusBadge/Timeline/i18n) ve T135 (StateActionPanel state×rol matrisi) kabul kriterleri admin dispute ekranını kapsamıyor; T136 admin **bot** sayfalarıyla ilgili | `frontend/src/lib/api/admin.ts`, `frontend/src/components/admin/DisputeResolveModal.tsx` |

Hiçbiri merge'i bloklamıyor: AC3'ün zinciri API sözleşmesine kadar eksiksiz, para güvenliği davranışı (AC4) uçtan uca kanıtlandı. N1 doküman hijyeni, N2 sahiplik ataması gerektiriyor — ikisi de proje sahibi kararı.

### Yapım raporu karşılaştırması

- **Uyum:** Kabul kriterleri, mimari anlatımı, migration tanımı ve test rakamları bağımsız bulgularla **tam uyumlu**. Rapordaki hiçbir iddia yanlış çıkmadı.
- **Bir fazla iyimserlik (N2):** §Notlar "Admin AD28 alanı FE tarafında T134/T135 kapsamında yüzeye çıkabilir" diyor; bu iki task'ın kabul kriterleri admin dispute ekranını içermiyor, dolayısıyla alan bugün **sahipsiz**.
- **Kayıt tazeliği:** §Commit & PR bölümü dal HEAD'ini `af2c9a6` / run `32042922572` diye anıyordu; doğrulama anında dal `7aafaf0`'a ilerlemişti. Aşağıda nihai HEAD run'ı ile güncellendi.

### E2E advisory legleri

8/8 leg kırmızı, **T130 dışı**: `Invalid object name 'PlatformSteamBots'` — run `32045939393` log'unda **tam 8 iz** (leg başına 1), yani legler spec'lere hiç ulaşmadan setup'ta ölüyor. T117'den beri pre-existing, sahiplik **T137a**. Raporun teşhisi bağımsız olarak doğrulandı.

## Commit & PR

- Branch: `task/T130-dispute-eligibility-autochecker`
- Commit: `4429f31` — T130: DisputeEligibility + AutoChecker yeniden yazımı
- PR: [#242](https://github.com/turkerurganci/Skinora/pull/242)
- CI: ✓ **PASS** — dal HEAD `7aafaf0`, run [`32046789416`](https://github.com/turkerurganci/Skinora/actions/runs/32046789416) (doğrulama anındaki nihai HEAD). Önceki kod-kapsamlı yeşil run'lar: `32042922572` (`af2c9a6`), `32045939393` (`741f58d`)

**Bloke edici 9 job yeşil:** Detect changed paths · 1. Lint · 2. Build · 3. Unit test · 4. Integration test · 5. Contract test · 6. Migration dry-run · 7. Docker build (backend) · CI Gate. İki job `skipped`: *0. Guard* (direct push guard — PR yolunda çalışmaz) ve *3b. JS test (vitest)* (path filtresi — bu task'ta frontend değişikliği yok).

**8 advisory E2E leg kırmızı, tamamı T130 dışı.** Nihai (yeşil) run'da 8/8 legin tamamı aynı imzayla düşüyor: `Invalid object name 'PlatformSteamBots'`, **leg başına tam 1 iz** — yani legler spec'lere hiç ulaşmadan setup'ta ölüyor. T117'den beri pre-existing; sahiplik **T137a** (E2E harness custodial seed triyajı). Log genelinde T130 yüzeylerinden (`BuyerBaselineClassIds`, `DeliveredItemName`, `DeliveryDisputeRound`, `CaptureInventoryFingerprint`, iki yeni mesaj anahtarı) **0 iz**; setup-download kırılması **0**.

**CI notu (altyapı, kod dışı — 2026-08-17 GitHub kesintisi).** Run üç kez GitHub kaynaklı düştü: `dorny/paths-filter@v3` ve `actions/setup-dotnet` `codeload.github.com`'dan indirilemedi (429/503, her seferinde 3 denemede de). Tur 1'de `Detect changed paths`, tur 2'de `3. Unit test` + `4. Integration test` + `6. Migration dry-run`, tur 3'te iki advisory leg **"Set up job"** aşamasında öldü — bu turlarda hiçbir test kodu çalışmadı. Dördüncü rerun'da kesinti geçti ve run `success` oldu.

> **Öğrenim (bu task'ta ölçüldü):** advisory E2E legleri `continue-on-error: true` olduğu için normalde run sonucunu düşürmez — T129'un **yeşil** run'ında da 8'i job düzeyinde kırmızıydı. Ancak `continue-on-error` yalnız **adım** hatalarını tolere eder; bir leg *"Set up job"* aşamasında (runner düzeyinde, action indirilemediği için) ölürse tolerans işlemez ve run `failure` olur. Dolayısıyla "advisory legler run'ı düşürdü" açıklaması yanlıştır — bu ayrımı yapmadan bir kırmızıyı advisory'ye yıkmak, gerçek bir bloke edici kırılmayı da aynı gerekçeyle geçiştirmeye açık kapı bırakır. Pre-push Layer 2 hook'u bu turlarda **doğru** davrandı; bypass kullanılmadı, temiz bir run üretilerek geçildi.

## Known Limitations / Follow-up

- **Aynı sınıf içindeki kalite farkı** hâlâ otomatik tespit dışı — satıcı aynı skinin daha kötü kopyasını gönderirse sınıf sayısı beklendiği gibi artar ve Sonuç A üretilir. Bu bilinçli ve kayıtlı: `DEFERRED_BACKLOG` `P2P-FloatVerification` (03 §6.3 notu, 02 §9.2).
- **Parmak izi geriye dönük değil.** `BuyerBaselineClassIds` yalnız migration'dan sonra SELLER_CONFIRMED'a giren işlemlerde dolar; önceki işlemlerde NULL kalır ve WRONG_ITEM kontrolü "karşılaştırma yapılamadı" der (fail-closed). Üretimde henüz işlem yok, pratik etkisi yok.
- **Kapı kapalıyken biriken dispute'lar** admin kuyruğuna düşmez (bilinçli — D1). Alıcı eskale ederse düşer; ayrıca DEPLOY_RUNBOOK §H.3'ün toplu incelemesi aynı satırları zaten okur.

## Notlar

- **Working tree (Adım -1):** temiz.
- **Main CI (Adım 0):** son 5 run `success` — `32039187802`, `32039187921`, `32033733318`, `32033733299`, `31944080720`.
- **Dış varsayımlar (Adım 4):** ① Gelen item'ın adı okunabiliyor — `InventoryItemSnapshot.Name` + sidecar description merge (`SidecarSteamInventoryReader.cs:65`) ✓ · ② Fresh (cache-bypass) okuma portu var — `InventoryReadFreshness.Fresh`, T120/T121 ✓ · ③ Disputes → Transactions bağımlılığı serbest — `MisdeliveryDisputeEscalator` emsali ✓ · ④ Sidecar tüm envanteri döndürüyor (parmak izi ek çağrı gerektirmiyor) — `SidecarSteamInventoryReader.cs:48`/`:115` ✓. Kırık varsayım yok.
- **Frontend etkisi yok:** mesajlar sunucuda üretim anında lokalize ediliyor (WP17), FE'de anahtar tablosu yok (`grep` ile doğrulandı). Admin AD28 alanı FE tarafında T134/T135 kapsamında yüzeye çıkabilir.
- `DisputeService`'in P2P öncesi state listesini anan XML yorumu (`ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_BUYER`) düzeltildi — T130'un sahip olduğu matrisi yanlış anlatıyordu.
