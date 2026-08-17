# T130 — DisputeEligibility + AutoChecker yeniden yazımı

**Faz:** F7 (P5 — Dispute) | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-08-17

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

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Commit & PR

- Branch: `task/T130-dispute-eligibility-autochecker`
- PR: (aşağıda güncellenecek)

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
