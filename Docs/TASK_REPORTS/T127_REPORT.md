# T127 — DeadlineScannerJob'a teslimat doğrulama turu

**Faz:** F7 | **Durum:** ⏳ Devam ediyor (doğrulama bekleniyor) | **Tarih:** 2026-08-15

---

## Yapılan İşler

- **T124 scanner kapısı kaldırıldı.** `DeadlineScannerJob.ReportGatedDeliveryTimeoutsAsync` silindi; süresi dolan `PAYMENT_RECEIVED` satırları artık **tüketiliyor** — ama doğrudan değil, 05 §4.4'ün şart koştuğu doğrulama turundan geçerek.
- **Yeni: `DeliveryTimeoutRound`** (`Application/Delivery/`) — T125 motorunun beş verdict'ini 03 §4.4 adım 1'in üç aksiyonuna eşleyen tur. Yan etki üretir ama `SaveChanges` çağırmaz; çağıranın unit of work'üne yazar.
- **Yeni: `IDeliveryMisdeliveryEscalator` portu** (Transactions) + **`MisdeliveryDisputeEscalator` adapter'ı** (Disputes) — yanlış-teslimat imzasında `DELIVERY` tipi dispute'u `ESCALATED` olarak açar/yükseltir. Port gerekli çünkü modül bağımlılığı **Disputes → Transactions** yönünde.
- **T125 motorunun short-circuit'ü genişletildi:** `BUYER_CONFIRMED` → **kayıtlı kanıt zaten yeterliyse**. Gözlem bayrakları yalnız OR'lar, dolayısıyla bu bir eşdeğerliktir, yeni kural değil. Kapıda bekleyen bir satırın her taramada iki Steam okuması yapmasını önler.
- **Ön koşul kapatıldı — freeze/resume faz kayması.** `ArmDeliveryDeadlineAsync`, işlem donmuşsa `TimeoutRemainingSeconds`'ı teslimat penceresinin tamamına yeniden yakalıyor (plandaki seçenek **(b)**).
- **Yeni knob:** `TimeoutSchedulingOptions.DeliveryVerificationBatchSize` (varsayılan 20) — tur başına Steam okuması maliyeti nedeniyle teslimat fazının kendi bütçesi.
- T124'te ters çevrilen **iki test eski beklentisine döndürüldü**.

## Etkilenen Modüller / Dosyalar

**Kaynak (yeni):**
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryTimeoutRound.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryTimeoutRound.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryMisdeliveryEscalator.cs`
- `backend/src/Modules/Skinora.Disputes/Application/Disputes/MisdeliveryDisputeEscalator.cs`

**Kaynak (değişen):**
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/DeadlineScannerJob.cs` — kapı → tüketen tur + yeni knob
- `backend/src/Modules/Skinora.Transactions/Application/Timeouts/TimeoutSchedulingService.cs` — freeze altında artık yeniden yakalama
- `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryVerificationService.cs` — short-circuit genişletmesi
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` · `backend/src/Modules/Skinora.Disputes/DisputesModule.cs` — DI

**Test (yeni):**
- `backend/tests/Skinora.Transactions.Tests/Integration/Delivery/DeliveryTimeoutRoundTests.cs` (13 test)
- `backend/tests/Skinora.Disputes.Tests/Integration/MisdeliveryDisputeEscalatorTests.cs` (7 test)
- `backend/tests/Skinora.Disputes.Tests/Unit/DisputesModuleRegistrationTests.cs` (1 test — DI kapısı, aşağıda)

**Test (değişen):** `DeadlineScannerJobTests.cs` · `DeadlineScannerJobSideEffectsTests.cs` · `TimeoutTestSupport.cs` · `TimeoutSchedulingServiceTests.cs` · `TimeoutFreezeServiceTests.cs` · `Skinora.Fraud.Tests/Integration/FraudFlagServiceTests.cs`

**Doküman:** `Docs/DEPLOY_RUNBOOK.md` (§A #6 uyarısı güncel zamana çekildi, §H.2'ye timeout davranışı notu, §H.4)

## Karar Kayıtları (proje sahibi onayı, 2026-08-15)

Yapım öncesi dört karar soruldu, dördü de öneri yönünde onaylandı:

| # | Konu | Karar |
|---|---|---|
| K1 | Dispute nasıl açılır | **Port + adapter, aynı transaction.** `OpenedByUserId = SeedConstants.SystemUserId`, `Status = ESCALATED`. Mevcut DELIVERY dispute varsa ikinci satır yazılmaz — `UQ_Disputes_TransactionId_Type` filtresiz |
| K2 | Tur tekrarı | **Kanıt bayrağı kapısı + tur başına tavan.** Sonucu kayıtlı satır Steam okumaz; tarama başına en fazla `DeliveryVerificationBatchSize` tur |
| K3 | Freeze faz kayması | **(b) faz değişince artığı yeniden yakala.** (a) — `ConfirmPayment`'i dondurmaya karşı korumak — zincire düşmüş bir ödemeyi yeniden sürecek yol olmadığı için reddedildi |
| K4 | `Inconclusive` | **Satıcı tarafına bak:** okunabildi ve item duruyorsa iptal; okunamıyorsa veya item düşmüş ama alıcı tarafı okunamıyorsa beklet |

## Verdict → Aksiyon Tablosu (03 §4.4 adım 1)

| Verdict | Aksiyon | Neden |
|---|---|---|
| `Delivered` | `ITEM_DELIVERED` + history (SYSTEM) + capture + outbox | Turun var olma sebebi: item ulaşmışken iptal etmemek |
| `InventoryEvidencePendingReview` | **Beklet** + capture. `DeliveryVerifiedAt` **damgalanmaz** | Kanıt item'ın ulaştığını söylüyor → iptal yanlış; kapı kapalı → otomatik ödeme yanlış (T125 F3) |
| `MisdeliverySignature` | **Beklet** + capture + dispute eskalasyonu | 02 §9.2: "işlem sessizce iptal edilmez" |
| `NoMovement` | **İptal** | Satıcı göndermedi — pozitif kanıt |
| `Inconclusive` | Satıcı okunabildi **ve** item duruyorsa **iptal**, aksi hâlde **beklet** | 08 §2.3 bilgi yokluğunu olumsuz bulgu saymayı yasaklıyor; ama iki taraf da gizliyken parayı süresiz kilitlemek de çözüm değil |

İptali yetkilendiren **tek** koşul: `SellerVisibility == Public && !Evidence.HasFlag(SELLER_ASSET_GONE)`. `NoMovement` bunu yapısal olarak sağlar; kural tek yerde yaşıyor (`SellerProvenToStillHoldTheItem`).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Kanıt tamsa timeout iptal yerine ITEM_DELIVERED üretiyor | ✓ | `DeliveryTimeoutRoundTests.Complete_Evidence_Delivers_Instead_Of_Cancelling` — durum `ITEM_DELIVERED`, `DeliveryVerifiedAt` damgalı, history satırı `DeliverItem`/SYSTEM, `TransactionStatusChangedEvent` yayınlandı |
| 2 | SELLER_ASSET_GONE ve delta yoksa dispute'a yükseltiliyor | ✓ | `..Misdelivery_Signature_Escalates_Instead_Of_Cancelling` (tur tarafı) + `MisdeliveryDisputeEscalatorTests` 7 test (adapter tarafı: açma, yükseltme, no-op, resolved'a dokunmama, çağıranın UoW'una yazma) |
| 3 | T124 SCANNER KAPISI KALDIRILDI — `ReportGatedDeliveryTimeoutsAsync` silinir, `PAYMENT_RECEIVED && DeliveryDeadline < now` dalı tüketen tarafa döner | ~ | Metot **silindi**; dal artık **tüketiyor** (`Scanner_Cancels_An_Overdue_Delivery_When_The_Round_Authorises_It` → `CANCELLED_TIMEOUT`). **Sapma:** dal tek sorguya değil, **ayrı sorgu + ayrı tavan** ile bağlandı — gerekçe "Sapmalar" bölümünde |
| 4 | Ters çevrilen iki test eski beklentisine döndü | ✓ | `Scanner_Does_Not_Consume_..._Until_T127` → `Scanner_Holds_An_Overdue_Delivery_When_The_Round_Concludes_Nothing` + `Scanner_Cancels_An_Overdue_Delivery_When_The_Round_Authorises_It`; `Delivery_Timeout_Publishes_Nothing_While_Gated_Until_T127` → `Delivery_Timeout_Publishes_Notification_And_PaymentRefund` (+ `Held_Delivery_Timeout_Publishes_Nothing` invariantı korudu) |
| 5 | LAUNCH KAPISI İNVARİANTI (T125 F3): gated tur ne teslim eder ne iptal eder, `DeliveryVerifiedAt` damgalanmaz, capture yazılır | ✓ | `..Gated_Round_Neither_Delivers_Nor_Cancels_And_Leaves_The_Guard_Shut` — `DeliveryEvidence` **yazılır**, `DeliveryVerifiedAt` NULL, capture `AutoReleaseGated = 1` |
| 6 | ÖN KOŞUL — freeze/resume faz kayması kapatıldı ve testle sabitlendi | ✓ | `TimeoutFreezeServiceTests.Payment_Confirmed_Under_Freeze_Does_Not_Shrink_The_Delivery_Window` (uçtan uca zincir) + `TimeoutSchedulingServiceTests.ArmDeliveryDeadline_Recaptures_The_Remainder_When_Still_Frozen` / `.._Leaves_The_Remainder_Null_When_Not_Frozen` |

## Sapmalar

**AC3 — teslimat dalı ayrı sorguda tutuldu (~ Kısmi).**

Kriter "tüketen sorguya geri konur" diyor. Dal **tüketiyor** ama sorgu ayrı kaldı, çünkü T124 kararı (a)'nın gerekçesi T127'den sağ çıkıyor: **beş verdict'ten üçü satırı `PAYMENT_RECEIVED`'da ve kalıcı olarak süresi dolmuş bırakıyor** (kapıda inceleme bekleyen, dispute'a yükseltilmiş, okunamayan). Launch'ta kapı kapalı olduğu için bunlardan ilki, alıcısı onay vermeyen her teslimatın **beklenen** sonucudur — yani birikirler. Tek sorguda `DeadlineScannerBatchSize` (200) bu satırlarla dolar ve accept / seller-confirm / payment fazlarının timeout'unu sessizce durdurur; T124'ün adını koyduğu availability hatası budur.

Ayrıca bir tur, bir state kontrolü değil **iki rate-limited Steam okumasıdır** (08 §2.2) — teslimat fazının kendi, çok daha küçük bütçesi olması gerekiyor.

Korunan iki test bu gerekçeyi mekanik hâle getiriyor: `Scanner_Still_Consumes_Other_Phases_When_Held_Delivery_Rows_Pile_Up` ve `Scanner_Caps_Delivery_Verification_Rounds_Per_Pass`.

**Doğrulayıcıya not:** bu, planın iki maddesi arasındaki gerçek bir gerilimdir (T127 AC3 ↔ T124 kararı (a)). Kriterin **özü** — kapı kalktı, dal artık tüketiyor — karşılanmıştır; **harfi** karşılanmamıştır. Karar bilinçlidir ve kodda `RunDeliveryTimeoutRoundsAsync` XML doc'unda gerekçesiyle yazılıdır.

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 error / 0 warning | `dotnet build Skinora.sln -c Debug` |
| Unit | ✓ 1381/1381 | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Integration | ✓ 1259/1259 | `--filter "FullyQualifiedName~.Integration"` |
| Contract | ✓ 9/9 | `--filter "FullyQualifiedName~.Contract"` |
| Odaklı | ✓ 26/26 | `~DeliveryTimeoutRoundTests\|~DeadlineScannerJob` |
| Odaklı | ✓ 7/7 | `~MisdeliveryDisputeEscalator` |
| Odaklı | ✓ 36/36 | `~TimeoutFreezeServiceTests\|~TimeoutSchedulingServiceTests` |
| Odaklı | ✓ 1/1 | `~DisputesModuleRegistration` |

**Not:** unit/integration/contract sayıları DI kapısı testi eklenmeden önce ölçüldü; o test unit sayısını 1381 → 1382 yapar. Nihai sayılar PR CI'ında.

### DI kapısı (yapım sırasında eklendi)

Scanner'ın bağımlılık zinciri T127 ile **ilk kez kendi assembly'sinden çıkıyor** (`IDeliveryMisdeliveryEscalator` → Disputes). `DeadlineScannerJob` kendini yeniden zamanlayan bir Hangfire job'ı olduğu için **lazy** resolve edilir: eksik bir kayıt build'i, unit testi veya endpoint testini kırmaz — üretimde ilk teslimat deadline'ı dolduğunda kırılır ve **aynı job dört fazın hepsini yürüttüğü için** accept / seller-confirm / payment timeout'larını da beraberinde götürür. `DisputesModuleRegistrationTests` bu kaydı sabitliyor. Zincirin diğer yarısı (`Program.cs` hâlâ `AddDisputesModule()` çağırıyor mu) zaten kapsanmış durumda — API integration süiti gerçek host'u ayağa kaldırıyor ve dispute uçları o çağrı olmadan resolve olmaz.

## Altyapı Değişiklikleri

- **Migration:** Yok — yeni kolon/tablo eklenmedi. K3 çözümü mevcut `TimeoutRemainingSeconds` kolonunu yeniden yazar.
- **Config/env değişikliği:** `TimeoutSchedulingOptions.DeliveryVerificationBatchSize` (kod varsayılanı 20). `appsettings.json`'da `Timeouts` bölümü yok — kardeş knob'lar gibi kod varsayılanıyla çalışır, override isteyen ortam bölümü ekler.
- **Docker değişikliği:** Yok.

## Mini Güvenlik Kontrolü

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | Yok — yeni sır, anahtar veya connection string yok |
| Auth/authorization etkisi | Yok — yeni endpoint yok; tur yalnız arka plan job'ından çağrılıyor |
| Input validation | Yok — dış girdi yüzeyi değişmedi; tek dış veri Steam envanteri, T120/T121 portundan geçiyor |
| Yeni dış bağımlılık | Yok |
| Para hareketi etkisi | **Var, kasıtlı.** Teslimat timeout'u T124'ten beri hiçbir şey yapmıyordu; artık iptal + iade üretebiliyor. Kapı `SellerProvenToStillHoldTheItem` tek koşuluna daraltıldı ve `DeliveryVerifiedAt` gated turda damgalanmıyor |

## Commit & PR

- Branch: `task/T127-delivery-timeout-round`
- Commit: `9314fea` — T127: DeadlineScannerJob'a teslimat dogrulama turu
- PR: [#238](https://github.com/turkerurganci/Skinora/pull/238)
- CI: (izleniyor — sonuç aşağıya yazılacak)

## Known Limitations / Follow-up

1. **Yanlış-teslimat sonrası geç teslimat gözlenmez.** `IsMisdeliverySignature()` kayıtlıysa tur Steam okumaz (K2). Item gerçekten geç ulaşırsa `INVENTORY_DELTA` hiç görülmez. Kabul edilebilir çünkü satır zaten bir admin'in kuyruğunda; ama B1 (teslimat gecikmesi) ölçülene kadar bu bir varsayımdır — DEPLOY_RUNBOOK §H.3 incelemesinde ölçüldükçe yeniden değerlendirilmeli.
2. **Okunamayan satırlar süresiz bekler.** Satıcı envanteri kalıcı olarak private/unavailable ise tur hiçbir zaman sonuca varmaz ve alıcının parası emanette kalır. Tek çıkış admin iptali. Otomatik outage freeze (`T50-OutageFreezeCallers`, DEFERRED_BACKLOG) bu vakayı daraltır ama kapatmaz.
3. **Kapıda biriken satırlar için operatör görünürlüğü yok.** DEPLOY_RUNBOOK §H.3'ün SQL'i tabloyu okuyabiliyor; ayrı bir metrik/alarm yok.
4. **T129 çakışması yok ama komşu:** teslim edilen işlemin `PayoutEligibleAt`'ini yazan kod hâlâ yok, dolayısıyla T127'nin ürettiği `ITEM_DELIVERED` satırları da `SellerPayoutQueueJob` kapısında bekler (T126 doğrulama bulgusu F1 ile fail-closed).

## Notlar

- **Working tree:** temiz (`git status --short` boş).
- **Adım 0 — Main CI startup check:** son 3 run `31880715937` ✓ `success` · `31880715941` ✓ `success` · `31880257981` ✓ `success`.
- **Dış varsayımlar: yok.** Yeni paket, yeni dış API, plan tier veya platform varsayımı yok. Task tamamen mevcut iç bileşenler üzerinde: T125 motoru (✓ merge), T120/T121 envanter portu (✓ merge), T124 `DeliveryDeadline` yazıcısı (✓ merge). Steam okuma davranışı T122'de ölçülmüş ve `INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md`'ye yazılmıştır — yeni bir varsayım eklenmedi.
- **T125 motoruna dokunuldu** (short-circuit genişletmesi). Değişiklik davranışsal olarak eşdeğerdir: gözlem `evidence` üzerinde yalnız OR yapar, dolayısıyla kayıtlı kanıt zaten `IsSufficientForDelivery()` ise tam tur da aynı verdict'i üretir. Eşdeğerlik iki testle sabitlendi: kapı kapalıyken sıfır okuma + `Held`, kapı açıkken sıfır okuma + `Delivered`. T126'nın `BUYER_CONFIRMED` yolu bit-bit aynı kaldı (o dalda kapı okuması hâlâ yapılmıyor).
