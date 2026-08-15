# T127 — DeadlineScannerJob'a teslimat doğrulama turu

**Faz:** F7 | **Durum:** ✓ **Tamamlandı — yeniden doğrulama ✓ PASS** | **Tarih:** 2026-08-16

> Bu rapor üç turu birden taşır. Aşağıdaki **Yapılan İşler → Doğrulama** bölümleri **ilk turun**
> kaydıdır ve olduğu gibi korunmuştur; birinci bağımsız doğrulamanın ✗ FAIL verdict'i ve beş bulgusu
> [Doğrulama](#doğrulama) bölümündedir. Bulguların nasıl kapatıldığı
> [Düzeltme Turu](#düzeltme-turu-2026-08-15) bölümünde, **ikinci bağımsız doğrulamanın ✓ PASS
> verdict'i** ise en sonda [Yeniden Doğrulama](#yeniden-doğrulama-2026-08-16--pass) bölümündedir.

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

> **Doğrulayıcı düzeltmesi (2026-08-15):** aşağıdaki tablo yapım chat'inin kendi verdict'idir ve
> olduğu gibi korunmuştur. Bağımsız doğrulama **#2'yi ✓ → ~** olarak düşürdü (motor tarafı doğru,
> re-entry dalı fazla ateşliyor — Bulgu B1). Bağımsız verdict tablosu ve gerekçeleri
> [Doğrulama](#doğrulama) bölümündedir.

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
- Commit: `9314fea` (yapım) · `bec894f` (DI kapısı + rapor)
- PR: [#238](https://github.com/turkerurganci/Skinora/pull/238)
- **CI: ✓ PASS** — HEAD `bec894f`, run [`31884389878`](https://github.com/turkerurganci/Skinora/actions/runs/31884389878), `conclusion=success`. Bloke edici job'ların hepsi yeşil: Lint · Build · Unit · Integration · Contract · Migration dry-run · Docker (backend) · **CI Gate**. (`0. Guard` ve `3b. JS test` skipped — sırasıyla direct-push guard'ı ve değişmeyen FE/sidecar yolu.)
- Önceki run [`31884253788`](https://github.com/turkerurganci/Skinora/actions/runs/31884253788) (`9314fea`) `cancelled` — ikinci push'un concurrency iptali, başarısızlık değil.
- **8 advisory E2E leg kırmızı — T127 kaynaklı DEĞİL.** İmza CI logundan doğrulandı: `Invalid object name 'PlatformSteamBots'` — `e2e/src/db.ts` seed'i T117'nin düşürdüğü bot tablosunu temizliyor. T117'den beri her F7 task'ında aynı; sahiplik **T137 → T138**. `continue-on-error` olduklarından CI Gate'i bloke etmiyorlar.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **TUR 1: ✗ FAIL** (bağımsız chat, 2026-08-15, commit `d81f98e`) → düzeltme turu → **TUR 2: ✓ PASS** (bağımsız chat, 2026-08-16, commit `207e691` — [Yeniden Doğrulama](#yeniden-doğrulama-2026-08-16--pass)) |
| Bulgu sayısı | Tur 1: 5 — **3 bloke edici** (B1 S1 · B2 S2 · B3 S2) + 2 bloke etmeyen (B4 S1 süreç · B5 S1 pre-existing). Tur 2: **0 bloke edici** + 4 bloke etmeyen gözlem (G1–G4, hepsi pre-existing) |
| Düzeltme gerekli mi | **Hayır** (tur 2 itibarıyla). Tur 1'in üç bloke edici bulgusu kapatıldı, B4 kapatıldı, B5 T130'a devredildi |

**Kapılar:** Adım −1 working tree temiz · Adım 0 main CI son 3 run (`31880715941`, `31880715937`, `31880257963`) hepsi `success` · Adım 0b repo memory T127 satırı mevcut · Adım 8a task branch CI run [`31884937003`](https://github.com/turkerurganci/Skinora/actions/runs/31884937003) **CI Gate `success`**, bloke edici 8 job yeşil (Lint · Build · Unit · Integration · Contract · Migration dry-run · Docker backend · CI Gate).

**Bağımsız test koşumu:** unit **1382/1382** (11 assembly, 0 fail) · T127 odaklı integration **88/88** — lokal SQL Server'a karşı **iki bağımsız koşu**, ikisi de 0 fail. Testler yeşil; bulgular testlerin **bakmadığı** yollarda.

**Advisory E2E (8 leg):** main'in T126 merge run'ı (`31880715937`) ile task branch run'ı birebir aynı 8 leg'de kırmızı → **pre-existing, T127 kaynaklı değil**; yapım raporunun tespiti (`Invalid object name 'PlatformSteamBots'`, sahiplik T137→T138) bağımsız olarak teyit edildi.

**Güvenlik:** temiz — `*.csproj` diff boş (yeni bağımlılık yok), secret/connection string sızıntısı yok, yeni endpoint veya authz yüzeyi yok, log satırlarında PII yok. Para hareketi etkisi var ve kasıtlı; itibar ataması `PAYMENT_RECEIVED → Seller` doğru (`ReputationAggregator.cs:184`), iade eventi yayınlanıyor (`Delivery_Timeout_Publishes_Notification_And_PaymentRefund`).

**Bağımsız kabul kriteri verdict'i:** 4 ✓ · 2 ~ — yapım raporuyla **bir uyuşmazlık** (#2).

| # | Kriter | Yapım | Doğrulayıcı | Not |
|---|---|---|---|---|
| 1 | Kanıt tamsa ITEM_DELIVERED | ✓ | ✓ | Damga (`DeliveryTimeoutRound.cs:173`) guard'dan önce; `FireInternal` sırası teyit edildi |
| 2 | SELLER_ASSET_GONE + delta yok → dispute | ✓ | **~** | Motor tarafı doğru; re-entry dalı **fazla** ateşliyor → **B1** |
| 3 | T124 kapısı kaldırıldı, dal tüketen sorguya döner | ~ | ~ | Öz karşılandı, harf karşılanmadı — ama gerekçe **B2 ile çürüyor** + plan güncellenmedi → **B4** |
| 4 | Ters çevrilen iki test eski beklentisine döndü | ✓ | ✓ | `git show 44f42b4~1` ile T124 öncesi adlar doğrulandı; `Delivery_Timeout_Publishes_Notification_And_PaymentRefund` adına **birebir** döndü |
| 5 | Launch kapısı invariantı | ✓ | ✓ | Test invariantın üçünü birden assert ediyor: status · `DeliveryVerifiedAt` NULL · capture `AutoReleaseGated=1` |
| 6 | Ön koşul — freeze/resume faz kayması | ✓ | ✓ | `Payment_Confirmed_Under_Freeze_Does_Not_Shrink_The_Delivery_Window` izole çağrı değil, **gerçek uçtan uca zincir** kuruyor |

### Bulgu B1 — S1, motorun reddettiği kararı bir tarama sonra uygular

`DeliveryVerificationService.cs:271` yanlış-teslimat imzası için `sellerSideKnown && buyerSideKnown` şart koşuyor; hemen üstündeki yorum açık: *"satıcının item'ı gitti ve alıcının envanteri gizli bir yanlış-teslimat imzası **DEĞİLDİR**; bu platformun bakamamasıdır (08 §2.3)."*

Ama `DeliveryTimeoutRound.cs:125` kanıtı **her kolda** kalıcıya yazıyor, ve `:104`'teki re-entry kapısı `buyerSideKnown` niteleyicisi **olmayan** çıplak `IsMisdeliverySignature()` bayrak testini kullanıyor (`SELLER_ASSET_GONE && !INVENTORY_DELTA`, `DeliveryEvidence.cs:66-68`).

**Zincir:** satıcı item'ı gönderdi + alıcının envanteri gizli → `sellerAssetGone = true`, `buyerSideKnown = false` → verdict `Inconclusive` → `Undelivered()` → `SellerProvenToStillHoldTheItem` false → **Held ✓ (tur 1 doğru)**. Ama `DeliveryEvidence = SELLER_ASSET_GONE` yazılı kaldı. **Tur 2 (30 sn sonra):** `:104` ateşler → Steam okumadan `EscalateAsync` → teslim etmiş satıcı hakkında `DELIVERY`/`ESCALATED` dispute + `HasActiveDispute = true` + iki tarafa `DisputeEscalatedEvent`. Dispute metni `DeliveryAssetGoneNotArrived` — bu vakada **yanlış bir iddia**.

Raporun kendi K4 satırı ("`Inconclusive` → ... aksi hâlde **beklet**") tam olarak burada çiğneniyor. `Asset_Gone_With_An_Unreadable_Buyer_Side_Is_Held_Not_Cancelled` yalnız **tur 1**'i sabitliyor; tur 2 testsiz. Karşı örnek: `Second_Round_On_A_Misdelivery_Re_Asserts_The_Escalation_Without_Reading_Steam` yalnız **gerçek** imza için yazılmış.

**Düzeltme yönü:** re-entry kapısı, kanıt bayrağına ek olarak "önceki turun verdict'i gerçekten `MisdeliverySignature` miydi" bilgisini okumalı (capture satırındaki `Verdict`, veya ayrı bir alan) — bayrak tek başına motorun niteleyicisini taşımıyor.

### Bulgu B2 — S2, teslimat fazı kendi bütçesinde aç kalıyor (timeout sessizce ölüyor)

`DeadlineScannerJob.cs:220-229`: `... Status == PAYMENT_RECEIVED && DeliveryDeadline < now` → `.OrderBy(t => t.DeliveryDeadline).Take(DeliveryVerificationBatchSize)` (varsayılan **20**, tarama aralığı 30 sn).

**Held satırlar bu sorgudan hiç çıkmıyor:** hiçbir kol `DeliveryDeadline`'a dokunmuyor, status `PAYMENT_RECEIVED` kalıyor. En eski oldukları için `OrderBy` onları **pencerenin başına** koyuyor. Kalıcı birikenler:
- `InventoryEvidencePendingReview` — launch'ta kapı kapalı olduğu için **alıcısı onay vermeyen her teslimatın beklenen sonucu** (raporun §Sapmalar bölümünün kendi ifadesi). §H.3 kapıyı açana kadar drene olmaz.
- Seller-favor çözülen misdelivery dispute'ları — `AdminDisputeService.cs:307-311` *"no state transition"*, satır `PAYMENT_RECEIVED`'da kalır.
- Kalıcı okunamayan satıcı envanterleri (Limitation #2).

20'inci kalıcı satırdan sonra **yeni hiçbir teslimat timeout'u tur çalıştıramaz** — AC'nin var olma sebebi olan davranış sessizce durur.

Bu, T124 kararı (a)'nın adını koyduğu açlığın yok edilmesi değil, **teslimat fazının içine taşınması**. Tavanın gerekçesi de bu satırlar için tutmuyor: rapor tavanı "tur = iki rate-limited Steam okuması" ile savunuyor, ama tavanı dolduran satırlar re-entry/short-circuit sayesinde **sıfır Steam okuması** yapıyor. Yani bütçe, ihtiyacı olmayan satırlara harcanıp ihtiyacı olanları aç bırakıyor.

`Scanner_Still_Consumes_Other_Phases_When_Held_Delivery_Rows_Pile_Up` yalnız **diğer üç fazın** korunduğunu kanıtlıyor — teslimat fazının drene olduğunu kanıtlayan test yok.

**Düzeltme yönü:** kalıcı-Held satırların pencereyi işgal etmemesi (ör. son tur damgası + "yeniden değerlendirme aralığı" filtresi, veya sonucu kayıtlı satırların sorgudan düşmesi). Limitation #3'ün (operatör görünürlüğü) bu bulgunun yerine geçmediğine dikkat: görünürlük eksiği ayrı, **işlevsel durma** bu.

### Bulgu B3 — S2, SYSTEM açılan dispute çözülünce alıcı bildirimi alamıyor

`MisdeliveryDisputeEscalator.cs:90` → `OpenedByUserId = SeedConstants.SystemUserId` (K1 kararı). Bu alan T127'den önce **her zaman alıcıydı**: `DisputeService.cs:104-108` sert guard (*"Only the buyer can open a dispute"*) ve `:154` `OpenedByUserId = callerUserId`. T127, invariantı kıran **ilk** yazıcı.

`AdminDisputeService.cs:329` o invariantı okuyor:

```csharp
new DisputeResolvedEvent(..., BuyerId: dispute.OpenedByUserId, ...)
```

Admin T127'nin açtığı dispute'u çözdüğünde `DisputeResolvedEvent.BuyerId` **SYSTEM kullanıcısının id'si** olur → gerçek alıcı `DISPUTE_RESULT` bildirimini hiç almaz. Mevcut işlevselliği bozan bir regresyon (S2), K1'in görülmemiş yan etkisi. Escalator'ın **kendi** eventi (`DisputeEscalatedEvent`, `:151-175`) doğru şekilde `transaction.BuyerId` kullanıyor — sapma yalnız **çözüm** yolunda.

**Düzeltme yönü:** `AdminDisputeService`'in alıcıyı `dispute.OpenedByUserId` yerine `transaction.BuyerId`'den çözmesi (tüm dispute'lar için doğru), veya escalator'ın `OpenedByUserId`'yi alıcı bırakması. Birincisi invariantı kaynağında düzeltir.

### Bloke etmeyen bulgular

**B4 — S1, süreç.** AC3 harfen karşılanmadı ve **onaylanan sapma kaynak plana yazılmadı**: `git diff --stat origin/main...HEAD -- Docs/11_IMPLEMENTATION_PLAN.md` **boş**. Rapor sapmayı dürüstçe `~` işaretleyip doğrulayıcıya not düşmüş — bu doğru davranış — ama projenin kendi T122 dersi geçerli: *onaylanmış kapsam değişikliği, kabul kriterlerinin KAYNAK dokümanına yazılmadıkça gerçekleşmemiştir.* B2 düzeltilirken AC3'ün nihai şekli plana işlenmeli.

**B5 — S1, pre-existing (T126'dan tohumlu, T127 genişletiyor).** Kapı kapalıyken biriken yeterli kanıt, alıcı `DELIVERY` dispute'u açtığında `DeliveryDisputeAutoChecker.cs:60-63` üzerinden `Resolved: true` üretiyor → dispute **CLOSED + `CanEscalate = false`** olarak açılıyor. Sonuç: kapı parayı bıraktırmıyor **ve** alıcının eskalasyon yolu kapalı — para kilitli, çıkış yok. T126'da da erişilebilirdi (alıcının kendi confirm-receipt çağrısı üzerinden) ama T127 bunu **alıcı hiçbir şey yapmadan** ve launch'ta **her teslimatta** erişilebilir yapıyor. Sahiplik T130 (auto-checker yeniden yazımı) olabilir — proje sahibi kararı.

### Yapım raporu karşılaştırması

Rapor **dürüst ve yüksek kaliteli**: AC3 sapmasını kendisi `~` işaretlemiş ve doğrulayıcıya açıkça bildirmiş, okunamayan satırların süresiz beklemesini (Limitation #2) kendisi kayda geçmiş, test sayısı farkını (1381 → 1382) not etmiş, advisory E2E'nin pre-existing olduğunu log imzasıyla kanıtlamış.

Uyuşmazlıklar:
- **B1, B2, B3 raporda yok.**
- Limitation #1 ("yanlış-teslimat sonrası geç teslimat gözlenmez") bayrağın **yalnız gerçek imzada** set edildiğini varsayıyor — B1 tam olarak bu varsayımın kırıldığı yer.
- Limitation #3 birikimi bir **görünürlük** eksiği sayıyor; B2 aynı birikimin **işlevsel durma** ürettiğini gösteriyor.
- §Sapmalar bölümü ayrı sorgu + ayrı tavanı T124 açlığının **çözümü** olarak savunuyor; B2 açlığın yok edilmediğini, teslimat fazına taşındığını gösteriyor.

**KALICI DERS (T124 dersinin üçüncü tekrarı):** bir kapı, koruduğu değerin diğer **yazarlarını** denetlemeli (T124), açtığı değerin **tüketicilerini** denetlemeli (T126) — ve **kendi bıraktığı kalıcı durumun** sonraki turda nasıl okunacağını denetlemeli (T127/B1) ile **o durumun biriktiği kuyruğun drene olup olmadığını** denetlemeli (T127/B2). Üçünde de kabul kriteri listesi hatayı yakalamadı; yakalayan soru "bu satır bir daha buraya geldiğinde ne olur?" oldu.

## Known Limitations / Follow-up

1. **Yanlış-teslimat sonrası geç teslimat gözlenmez.** `IsMisdeliverySignature()` kayıtlıysa tur Steam okumaz (K2). Item gerçekten geç ulaşırsa `INVENTORY_DELTA` hiç görülmez. Kabul edilebilir çünkü satır zaten bir admin'in kuyruğunda; ama B1 (teslimat gecikmesi) ölçülene kadar bu bir varsayımdır — DEPLOY_RUNBOOK §H.3 incelemesinde ölçüldükçe yeniden değerlendirilmeli.
2. **Okunamayan satırlar süresiz bekler.** Satıcı envanteri kalıcı olarak private/unavailable ise tur hiçbir zaman sonuca varmaz ve alıcının parası emanette kalır. Tek çıkış admin iptali. Otomatik outage freeze (`T50-OutageFreezeCallers`, DEFERRED_BACKLOG) bu vakayı daraltır ama kapatmaz.
3. **Kapıda biriken satırlar için operatör görünürlüğü yok.** DEPLOY_RUNBOOK §H.3'ün SQL'i tabloyu okuyabiliyor; ayrı bir metrik/alarm yok.
> **Düzeltme turu sonrası (2026-08-15).** #1 artık **varsayım değil**: kısa devre yalnız motorun
> `MisdeliverySignature` verdict'i verdiği (yani iki tarafın da okunduğu) satırlarda çalışıyor, geri
> kalan her satır her turda tam okuma yapıyor — B1 düzeltmesi bu maddenin dayandığı "bayrak yalnız
> gerçek imzada set edilir" varsayımını kodda doğru hâle getirdi. #2 aynen geçerli (okunamayan satır
> süresiz bekler) ama artık **saatte ~4 kez** yeniden deneniyor ve diğer teslimatları aç bırakmıyor.
> #3 kısmen daraldı: `DeliveryRoundAt` operatöre "bu satıra en son ne zaman bakıldı" sorusunun
> cevabını veriyor (§H.2), ayrı bir metrik/alarm hâlâ yok.

4. **T129 çakışması yok ama komşu:** teslim edilen işlemin `PayoutEligibleAt`'ini yazan kod hâlâ yok, dolayısıyla T127'nin ürettiği `ITEM_DELIVERED` satırları da `SellerPayoutQueueJob` kapısında bekler (T126 doğrulama bulgusu F1 ile fail-closed).

## Düzeltme Turu (2026-08-15)

Bağımsız doğrulamanın **3 bloke edici** bulgusu kapatıldı, **B4** (süreç) kapatıldı, **B5** proje
sahibi kararıyla **T130'a devredildi**. Dört düzeltme yönü de yapım öncesi proje sahibine seçenekli
sunuldu ve **önerilen seçenekler onaylandı**.

### B1 — re-entry kapısı artık motorun niteleyicisini taşıyor (S1, kapatıldı)

`DeliveryTimeoutRound.RunAsync` re-entry kapısı çıplak `IsMisdeliverySignature()` bayrak testi yerine
**önceki turun kayıtlı verdict'ini** okuyor:

```csharp
if (await MisdeliveryAlreadyConcludedAsync(transaction.Id, cancellationToken))
```
→ `DeliveryEvidenceCaptures` üzerinde `TransactionId == id && Verdict == "MisdeliverySignature"`
(`IX_DeliveryEvidenceCaptures_TransactionId` üzerinde tek seek, `AsNoTracking`).

Neden bu kaynak: capture satırı **motorun sonucudur**, bayraklar ise gözlemdir — ve motor aynı üç biti
`sellerSideKnown && buyerSideKnown` ile niteliyor (`DeliveryVerificationService.cs:271`). Kanıt olarak
her `MisdeliverySignature` verdict'i **her zaman** capture yazar (`BuildCapture` → `worthCapturing`),
yani kapının okuduğu kayıt eksiksizdir.

**Bulgunun zinciri artık kırık:** satıcının asseti gitmiş + alıcı envanteri gizli vakada tur 1 `Inconclusive`
→ Held (kanıt bayrakları yazılır ama **capture yazılmaz**, çünkü bu verdict `worthCapturing` değildir)
→ tur 2 kapıyı **açmaz**, tam turu tekrar çalıştırır ve yine bekletir. Teslim etmiş olabilecek satıcı
hakkında dispute açılmıyor.

| Test | Ne sabitliyor |
|---|---|
| `Second_Round_On_An_Unread_Buyer_Side_Still_Does_Not_Escalate` (**yeni**) | İki tur; tur 1'den sonra `DeliveryEvidence.IsMisdeliverySignature()` **true** (fixture gerçek) ve capture tablosu **boş**; tur 2 sonunda escalation **yok**, `HasActiveDispute` false, durum `PAYMENT_RECEIVED` |
| `Second_Round_On_A_Misdelivery_Re_Asserts_The_Escalation_Without_Reading_Steam` (**fixture düzeltildi**) | Gerçek imza artık **capture satırıyla** kuruluyor; kısa devre + idempotent re-assert korunuyor, sıfır Steam okuması |

### B2 — teslimat penceresi artık drene oluyor (S2, kapatıldı)

Yeni kolon **`Transaction.DeliveryRoundAt`** (migration `20260815163802_T127_AddDeliveryRoundAt`,
tek kolon `datetime2 NULL`) + yeni knob **`TimeoutSchedulingOptions.DeliveryRoundRecheckSeconds`**
(varsayılan **900**).

- **Damga:** `RunAsync` içinde, **her koldan önce** yazılıyor — sonuca varmayan tur da yazıyor, çünkü
  sorulan soru "ne zaman sonuçlandı" değil **"bu satıra en son ne zaman bakıldı"**.
- **Sorgu:** `(DeliveryRoundAt == null || DeliveryRoundAt <= now - recheck)` filtresi +
  `OrderBy(nulls-first) → ThenBy(DeliveryRoundAt) → ThenBy(DeliveryDeadline)`.

Bulgunun mekaniği böylece yapısal olarak imkânsız: **hiç turlanmamış** bir teslimat, kaç kalıcı-Held
satır birikmiş olursa olsun pencerenin başındadır. Kalıcı satırlar emekliye ayrılmıyor (08 §2.3 —
"okunamadı" sonuçlanmış sayılamaz), yalnız sıraları geliyor: saatte ~4 tur.

Doğrulamanın "tavanın gerekçesi tutmuyor, tavanı dolduran satırlar sıfır okuma yapıyor" tespiti de
karşılandı: bütçe artık **okuma yapması gereken** satırlara gidiyor.

| Test | Ne sabitliyor |
|---|---|
| `Scanner_Examines_A_Never_Rounded_Delivery_Before_Held_Ones` (**yeni**) | 3 kalıcı-Held satır (5+ saatlik deadline, 30 sn önce turlanmış) + 1 yeni süresi dolan satır, tavan **1** → tur **yeni satıra** gidiyor. Deadline sırasında en eski Held satır alırdı — her taramada |
| `Scanner_Rotates_The_Delivery_Window_Across_Passes` (**yeni**) | Tavan 1, üç tarama → **üç farklı** işlem. Düzeltme öncesi üçü de aynı satırdı |
| `Scanner_Re_Examines_A_Held_Delivery_Once_The_Recheck_Interval_Passes` (**yeni**) | 901 sn önce turlanmış satır pencereye giriyor, 899 sn önceki girmiyor |
| `Every_Round_Stamps_When_The_Row_Was_Last_Examined` · `A_Short_Circuited_Round_Stamps_The_Examination_Too` (**yeni**) | Damga hem tam turda hem kısa devrede yazılıyor — kısa devre yazmasaydı admin kuyruğundaki satır pencereyi sonsuza dek tutardı |

### B3 — SYSTEM açılan dispute çözülünce alıcı bildirimi gidiyor (S2, kapatıldı)

`AdminDisputeService` (`Skinora.API/Services/`) `DisputeResolvedEvent.BuyerId`'yi artık
`transaction.BuyerId ?? dispute.OpenedByUserId` ile çözüyor. Doğrulamanın önerdiği **kaynakta düzeltme**
yolu seçildi: `OpenedByUserId`'nin alıcı olduğu varsayımı yalnız T127'nin SYSTEM yolunda değil,
gelecekteki her sistem-açılışında da yanlış olurdu. K1 kararı (dispute kaydında açan taraf SYSTEM
görünür) korundu — 02 §10.2 kaydı bozulmadı, admin listesindeki "OpenedBy" doğru kalıyor.

| Test | Ne sabitliyor |
|---|---|
| `Resolve_OfASystemOpenedDispute_NotifiesTheRealBuyer_NotTheOpener` (**yeni**) | SYSTEM tarafından açılmış `DELIVERY`/`ESCALATED` dispute çözülünce `DisputeResolvedEvent.BuyerId` = **gerçek alıcı**, SYSTEM **değil**; `SellerId` de doğru |

### B4 — onaylanan sapma kaynak plana yazıldı (kapatıldı)

`Docs/11_IMPLEMENTATION_PLAN.md` T127 girdisine eklendi: **AC3'ün NİHAİ ŞEKLİ** (dal tüketiyor, ama
ayrı sorgu + ayrı tavanla; özü karşılandı, harfi bilinçli olarak karşılanmadı, gerekçesiyle) ve **B1/B2/B3
birer kabul kriteri** olarak. Doküman başlığındaki "Son güncelleme" zinciri de güncellendi. T122'nin
kalıcı dersi uygulandı: onaylanmış kapsam değişikliği, kabul kriterlerinin KAYNAK dokümanına
yazılmadıkça gerçekleşmemiştir.

**B1'in komşusu — tarandı, bloke etmiyor.** Kod tabanındaki üçüncü ve son
`IsMisdeliverySignature()` okuyucusu `DeliveryDisputeAutoChecker.cs:69`'dur ve o da niteleyicisiz.
Ama sonucu `Unresolved` + `CanEscalate = true`: satıcı hakkında iddia üretmiyor, durum değiştirmiyor,
alıcının eskalasyon yolunu kapatmıyor — yalnız gösterilen mesaj metni alıcı envanteri okunamayan
vakada yanlış olabiliyor. T130 bu dosyayı zaten yeniden yazıyor; madde oraya kabul kriteri olarak
eklendi (B5'in yanına). Kapsamı T127'de genişletmemenin gerekçesi: iki task aynı dosyayı yeniden
yazmış olurdu.

### B5 — T130'a devredildi (proje sahibi kararı)

Kapı kapalıyken biriken yeterli kanıtın, alıcının açtığı `DELIVERY` dispute'unu `CanEscalate = false` ile
kapatması (para kilitli + eskalasyon yolu kapalı) **T130'un kabul kriteri** olarak yazıldı ve
**launch'tan önce kapatılmalı** kaydı düşüldü. Gerekçe: bulgu pre-existing (T126'dan tohumlu) ve
auto-checker'ın yeniden yazımı zaten T130'un işi; T127 içinde düzeltmek iki task'ın aynı dosyayı
yeniden yazması demekti.

### Düzeltme turu — etkilenen dosyalar

**Kaynak:** `DeliveryTimeoutRound.cs` (B1 kapısı + B2 damgası) · `DeadlineScannerJob.cs` (B2 sorgu +
knob) · `Transaction.cs` · `TransactionConfiguration.cs` · `AdminDisputeService.cs` (B3) ·
`Migrations/20260815163802_T127_AddDeliveryRoundAt.cs`

**Test:** `DeliveryTimeoutRoundTests.cs` (+3 yeni, 1 fixture düzeltmesi) ·
`DeadlineScannerJobTests.cs` (+3 yeni) · `TimeoutTestSupport.cs` (fake tur damgalıyor, `Options`
yeni knob) · `AdminDisputeServiceTests.cs` (+1 yeni, fixture `openedByUserId` parametresi)

**Doküman:** `11_IMPLEMENTATION_PLAN.md` (T127 AC3 nihai şekli + B1/B2/B3 kriterleri, T130'a B5) ·
`06_DATA_MODEL.md` §3.5 (`DeliveryRoundAt`) · `DEPLOY_RUNBOOK.md` §H.2 (biriken satırların tarama
ritmi, operatör sonuçları)

### Düzeltme turu — test sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 error / 0 warning | `dotnet build Skinora.sln -c Debug` |
| Unit | ✓ **1382/1382** | 11 assembly, 0 fail (`FullyQualifiedName!~.Integration&!~.Contract`) |
| Integration | ✓ **1266/1266** | 10 assembly, 0 fail — düzeltme öncesi 1259, +7 yeni test |
| Contract | ✓ **9/9** | |
| Odaklı | ✓ 32/32 | `~DeliveryTimeoutRoundTests\|~DeadlineScannerJob` (önce 26) |
| Odaklı | ✓ 12/12 | `~AdminDisputeServiceTests` (önce 11) |

### Düzeltme turu — PR & CI

- Commit: `c0a1cc7` (düzeltme turu) · `9314fea` + `bec894f` (ilk tur)
- PR: [#238](https://github.com/turkerurganci/Skinora/pull/238)
- **CI: ✓ PASS** — HEAD `c0a1cc7`, run [`31907095957`](https://github.com/turkerurganci/Skinora/actions/runs/31907095957),
  `conclusion=success`. Bloke edici job'ların hepsi yeşil: Lint · Build · Unit · Integration ·
  Contract · **Migration dry-run** (yeni kolon şemaya temiz uygulanıyor) · Docker (backend) ·
  **CI Gate**. (`0. Guard` ve `3b. JS test` skipped.)
- **8 advisory E2E leg kırmızı — T127 kaynaklı DEĞİL, ilk turdakiyle aynı.** İmza bu run'ın
  logundan yeniden doğrulandı: **8/8 leg `Invalid object name 'PlatformSteamBots'`** — `e2e/src/db.ts`
  seed'i T117'nin düşürdüğü bot tablosunu temizliyor. Sahiplik T137 → T138; `continue-on-error`
  oldukları için CI Gate'i bloke etmiyorlar.
- `dotnet ef migrations has-pending-model-changes` → *"No changes have been made to the model since
  the last migration"* (model ile migration senkron).

### Düzeltme turu — altyapı ve güvenlik

- **Migration:** **Var** — `20260815163802_T127_AddDeliveryRoundAt`, `Transactions` tablosuna tek
  nullable `datetime2` kolon. Geri alınabilir (`Down` → `DropColumn`), veri taşıma yok, mevcut satırlar
  `NULL` başlar ve **NULL = "hiç bakılmadı" = pencerenin başı**, yani deploy anında birikmiş satırların
  hepsi önce bir tur alır. Yeni index yok: mevcut filtreli `IX_Transactions_Delivery_Pending` predicate'i
  karşılıyor, sıralamaya giden küme uçuştaki teslimat sayısıyla sınırlı.
- **Config:** `Timeouts:DeliveryRoundRecheckSeconds` (kod varsayılanı 900). `appsettings.json`'da
  `Timeouts` bölümü yok — kardeş knob'lar gibi kod varsayılanıyla çalışır.
- **Güvenlik:** yeni bağımlılık yok, yeni endpoint/authz yüzeyi yok, secret yok. Para hareketi etkisi
  **daralıyor**: B1 düzeltmesi hatalı dispute'u, B2 düzeltmesi ise "timeout hiç çalışmıyor" durumunu
  kaldırıyor; iptali yetkilendiren tek koşul (`SellerProvenToStillHoldTheItem`) değişmedi.

## Yeniden Doğrulama (2026-08-16) — ✓ PASS

**Bağımsız chat, yapım raporu görülmeden başladı.** Verdict önce koddan üretildi, rapor karşılaştırması
en sonda yapıldı (INSTRUCTIONS.md §3.3 izolasyon kuralı).

### Verdict: ✓ PASS

**Kapılar:** Adım −1 working tree temiz · Adım 0 main CI son 3 run (`31880715941` · `31880715937` ·
`31880257981`) hepsi `success` · Adım 0b repo memory T127 satırı mevcut · Adım 8a task branch CI
HEAD `207e691` run [`31907664975`](https://github.com/turkerurganci/Skinora/actions/runs/31907664975)
**CI Gate `success`**, bloke edici 8 job yeşil (Lint · Build · Unit · Integration · Contract ·
Migration dry-run · Docker backend · CI Gate).

### Kabul kriterleri — bağımsız verdict

Plan `11 §P3 T127`'nin **dokuz** maddesi (altı özgün + düzeltme turunda kabul kriteri olarak eklenen
B1/B2/B3). Hepsi kodda izlendi, kanıtla kapatıldı.

| # | Kriter | Sonuç | Bağımsız kanıt |
|---|---|---|---|
| 1 | Kanıt tamsa ITEM_DELIVERED | ✓ | `DeliveryTimeoutRound.DeliverAsync` — `DeliveryVerifiedAt` guard'dan **önce** damgalanıyor (`HasDeliveryEvidence` onu okur); refuse hâlinde alan alan rollback var. `Complete_Evidence_Delivers_Instead_Of_Cancelling` |
| 2 | SELLER_ASSET_GONE + delta yok → dispute | ✓ | `EscalateAsync` → `MisdeliveryDisputeEscalator`; `UQ_Disputes_TransactionId_Type` filtresiz olduğu için 4 durumun dördü de enumere edilmiş (`Opened`/`Promoted`/`AlreadyEscalated`/`AlreadyResolved`), `IgnoreQueryFilters` soft-delete'i de görüyor |
| 3 | T124 kapısı kaldırıldı, dal tüketiyor | ✓ | `grep -rn "ReportGatedDeliveryTimeoutsAsync\|GatedDelivery" backend/` **boş** — metot yok. Dal tüketiyor. Ayrı sorgu + ayrı tavan sapması artık plana **NİHAİ ŞEKİL** olarak yazılı (proje sahibi onaylı) → kriter kaynağıyla uyumlu, tur 1'deki `~` gerekçesi (B4) ortadan kalktı |
| 4 | Ters çevrilen iki test eski beklentisine döndü | ✓ | `grep -rn "Until_T127"` **boş**. Yerlerine `Scanner_Cancels_An_Overdue_Delivery_When_The_Round_Authorises_It` + `Delivery_Timeout_Publishes_Notification_And_PaymentRefund` (T124 öncesi adına birebir dönüş) |
| 5 | Launch kapısı invariantı | ✓ | `HoldForReview` `DeliveryVerifiedAt`'e **dokunmuyor**, capture yazıyor. Test üç şeyi birden assert ediyor: status · `DeliveryVerifiedAt` NULL · capture `AutoReleaseGated=1` |
| 6 | Ön koşul — freeze/resume faz kayması | ✓ | `ArmDeliveryDeadlineAsync` `TimeoutFrozenAt != null` iken `TimeoutRemainingSeconds`'ı yeniden yakalıyor — 05 §4.4 *"Otorite: reschedule'ın kaynağı `TimeoutRemainingSeconds`'tır"* ile hizalı (türetilmiş deadline değil, otorite alanı düzeltiliyor). **Emergency-hold varyantı da kapsanıyor:** `AdminTransactionService:415` release yolu aynı `_freeze.ResumeAsync`'ten geçiyor |
| 7 | **B1** — re-entry kapısı motorun niteleyicisini taşır | ✓ | Kapı `DeliveryEvidenceCaptures.Verdict == "MisdeliverySignature"` okuyor. **Kaynağın eksiksizliği bağımsız doğrulandı:** `BuildCapture.worthCapturing` = `InventoryEvidencePendingReview \| MisdeliverySignature \| (Delivered && gateOpen)` → (a) her gerçek imza **her zaman** capture yazar, (b) `Inconclusive` **hiç** yazmaz. Zincir testle değil **yapıyla** kırık |
| 8 | **B2** — teslimat penceresi aç kalmaz | ✓ | `DeliveryRoundAt` (nulls-first CASE projeksiyonu, provider default'una dayanmıyor) + `DeliveryRoundRecheckSeconds`. Damga `RunAsync`'in ilk satırında, kısa devrelerden **önce**; entity tracked olduğu için tur throw etse bile `SaveChanges`'e ulaşıyor (`ChangeTracker.HasChanges()` erken-dönüş guard'ı da bunu koruyor). Sıralama **gerçek SQL Server**'a karşı sınanıyor (`IntegrationTestBase` → Testcontainers MsSql), yani EF çevirisi de kanıtlanmış |
| 9 | **B3** — SYSTEM dispute'unda gerçek alıcı bildirim alır | ✓ | `transaction.BuyerId ?? dispute.OpenedByUserId`. Kaynakta düzeltme — tüm dispute tipleri için doğru kalıyor; escalator'ın kendi `DisputeEscalatedEvent`'i zaten `transaction.BuyerId` kullanıyordu |

### Doküman uyumu

- **03 §4.4 adım 1** üç yollu eşleme ↔ tur kolları **birebir**.
- **05 §4.4** scanner-driven teslimat fazı + iptalden önce doğrulama turu + `TimeoutRemainingSeconds` otoritesi ✓.
- **02 §9.2 / §10.1** yanlış-teslimat *"sessizce iptal edilmez"* ✓; **08 §2.3** bilgi yokluğu olumsuz bulgu sayılmıyor ✓ (`SellerProvenToStillHoldTheItem` tek pozitif koşul).
- **06 §3.5** `DeliveryRoundAt` satırı eklenmiş ✓ · **DEPLOY_RUNBOOK §H.2** tarama ritmi + operatör sonuçları yazılmış ✓ · **11 §P3 T127** AC3 nihai şekli + B1/B2/B3 kriterleri yazılmış ✓ (B4 kapandı).

### Bağımsız test koşumu

| Tür | Sonuç | Komut |
|---|---|---|
| Build (Release) | ✓ **0 error / 0 warning** | `dotnet build Skinora.sln -c Release` |
| Unit | ✓ **1382/1382** (13 assembly, 0 fail) | `--filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` |
| Integration | ✓ **1266/1266** (10 assembly, 0 fail) | `--filter "FullyQualifiedName~.Integration"` |
| Contract | ✓ **9/9** (API 4 + Shared 5) | `--filter "FullyQualifiedName~.Contract"` |
| Odaklı — T127 çekirdeği | ✓ **68/68** | `~DeliveryTimeoutRoundTests\|~DeadlineScannerJob\|~TimeoutFreezeServiceTests\|~TimeoutSchedulingServiceTests` |
| Odaklı — escalator + DI | ✓ **8/8** | `~MisdeliveryDisputeEscalator\|~DisputesModuleRegistration` |
| Odaklı — admin dispute | ✓ **12/12** | `~AdminDisputeServiceTests` |

Üç sayı da yapım raporunun düzeltme turu ölçümleriyle **birebir**.

> **Ölçüm notu (validator'ın kendi hatası, kayda geçiriliyor).** Integration ilk koşumda **41 fail**
> verdi (Auth 26 · Notifications 10 · Disputes 5). Sebep T127 değil, **validator'ın kendi kaynak
> açlığı**: unit + contract + integration suite'leri eşzamanlı koşturuldu ve makinede zaten tam bir
> `docker compose` yığını (backend · frontend · nginx · redis · mssql · 3 sidecar · grafana · loki ·
> prometheus) ayaktaydı → Testcontainers MsSql örnekleri timeout'a düştü. Üç assembly de **seri**
> koşumda temiz: Auth **37/37** (2 dk 5 sn → 21 sn), Notifications **60/60** (2 dk 52 sn → 27 sn),
> Disputes **50/50** (5 dk 30 sn → 39 sn). Süre farkı tek başına teşhisi kanıtlıyor. Yukarıdaki
> 1266 rakamı seri koşumların toplamıdır. **Ders:** Testcontainers kullanan suite'ler paralel
> koşturulmamalı — bir sonraki validator bu tuzağa düşmesin.

### Güvenlik kontrolü

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | **Temiz** — `git diff origin/main...HEAD` üzerinde secret/anahtar/connection-string taraması boş (tek eşleşme bir test fixture'ının sahte trade URL token'ı) |
| Auth / authorization | **Temiz** — yeni endpoint yok; `AdminDisputeService` değişikliği mevcut yetkili admin akışında bir bugfix, yetki yüzeyine dokunmuyor |
| Input validation | **Temiz** — yeni dış girdi yüzeyi yok; tur yalnız arka plan job'ından çağrılıyor |
| Yeni bağımlılık | **Yok** — `*.csproj` / `*.props` / lock dosyası diff'i boş |
| Migration | **Güvenli** — tek nullable `datetime2` kolon, veri taşıma yok, `Down` → `DropColumn`. Model snapshot delta **tek property** (`AppDbContextModelSnapshot.cs` +3 satır), yani model ↔ migration senkron; CI `6. Migration dry-run` ✓ |
| Para hareketi | **Var, kasıtlı ve daralmış** — iptali yetkilendiren tek koşul `SellerProvenToStillHoldTheItem` değişmedi |

### Advisory E2E (8 leg) — T127 kaynaklı DEĞİL, bağımsız teyit

Yapım raporunun tespiti **karşı kanıtla** doğrulandı: T126 dalının run'ı
[`31880109821`](https://github.com/turkerurganci/Skinora/actions/runs/31880109821) — yani T127 hiç
yokken — **birebir aynı 8 leg**'de `failure`. İmza iki yönlü: `Invalid object name 'PlatformSteamBots'`
(T117'nin düşürdüğü bot tablosu) **ve** spec adlarındaki emekli v2.0 statüleri
(`TRADE_OFFER_SENT_TO_SELLER`, `ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_BUYER`). Sahiplik planda zaten
tanımlı: `Task T138: E2E spec'lerinin yeniden yazımı`. `continue-on-error` + CI Gate'ten hariç.

### Bloke etmeyen gözlemler (yeni — proje sahibi kararı gerekiyor)

Dördü de **pre-existing** ve **hiçbiri T127'nin kabul kriterlerinde değil**; hiçbiri merge'i bloke
etmiyor. Plana yazılmadılar — kapsam değişikliği proje sahibi onayı gerektirir (GUARDRAILS §3).

**G1 (S1, pre-existing — erişilebilirliği T127 genişletiyor). Timeout iptali açık dispute'u kapatmıyor.**
02 §10.2: *"Dispute açık bir işlem timeout nedeniyle iptal olabilir — **bu durumda dispute otomatik
kapanır** ve standart iade kuralları uygulanır."* Cümlenin ikinci yarısının kodda karşılığı **yok**:
`DisputeStatus.CLOSED` yalnız `DisputeService`'in submit-txhash auto-resolve yolunda yazılıyor;
`TimeoutSideEffectPublisher` ve `DeadlineScannerJob` dispute'a hiç dokunmuyor. **Pre-existing** çünkü
`PAYMENT` dispute'u `SELLER_CONFIRMED`'da açılabiliyor (`DisputeEligibility`) ve o fazın timeout'u
T124'ten önce de tüketiyordu. T127 aynı boşluğu teslimat fazına da erişilebilir yapıyor. Zarar sınırlı
(para zaten alıcıya dönüyor), ama admin `BUYER_FAVOR` ile kapatmayı denerse `AdminResolveRefund`
`CANCELLED_TIMEOUT`'tan reddedilir — pratik çıkış yalnız `SELLER_FAVOR`. Sahipsiz.

**G2 (S1, pre-existing). Freeze/resume faz kaymasının iki kardeşi açık.** T127 `DeliveryDeadline`
yazıcısını kapattı (AC6, kriterin kapsamı tam olarak bu). Aynı desenin diğer iki örneği duruyor:
`TransactionAcceptanceService.cs:258` (CREATED'da donmuşken accept → `SellerConfirmDeadline`'a
**accept** fazının artığı) ve `TransactionReadinessService.cs:249` (ACCEPTED'da donmuşken confirm-ready
→ `PaymentDeadline`'a **seller-confirm** artığı). İkisi de T127'den bağımsız olarak **bugün** tüketiliyor,
yani zarar yeni değil — ama T124'ün *"bir kapı, koruduğu DEĞERİN diğer yazarlarını da denetlemeli"*
dersi bu iki kolon için hâlâ uygulanmadı.

**G3 (S1, düşük). SELLER_FAVOR ile çözülen SYSTEM misdelivery dispute'u satırı kalıcı bırakıyor.**
`AdminDisputeService` yalnız `BUYER_FAVOR`'da state geçişi yapıyor; `SELLER_FAVOR`'da işlem
`PAYMENT_RECEIVED`'da ve süresi dolmuş kalıyor. Re-entry kapısı bu satıra her
`DeliveryRoundRecheckSeconds`'ta bir dönüp `AlreadyResolved` uyarı logu üretiyor ve rotasyonda bir slot
tutuyor. Terminal disposition **T131**'in (`AdminDisputeService — item-refund bacağı + override`) işi;
gürültü sınırlı ve B2 düzeltmesi sayesinde yeni teslimatları aç bırakmıyor.

**G4 (kozmetik, T117 kalıntısı).** `DisputeService` sınıf XML doc'u (`<b>Per-type allowed states:</b>`)
hâlâ emekli v2.0 statülerini sayıyor (`TRADE_OFFER_SENT_TO_BUYER`, `ITEM_ESCROWED`) oysa kanonik
`DisputeEligibility` v3.0. T127 bu dosyaya dokunmadı.

### Yapım raporu karşılaştırması

**Tam uyumlu — uyuşmazlık yok.** Bağımsız verdict (9/9 ✓) ile raporun düzeltme turu iddiaları örtüşüyor;
üç test sayısı da (1382 · 1266 · 9) birebir tuttu. Raporun kendi öz-eleştirisi doğrulandı: tur 1'in
`~` işaretli AC3'ü, sapmanın kaynak plana yazılmasıyla (B4) artık `✓` — kriterin kaynağı değişti,
kod değil.

Raporun **doğrulanamayan tek iddiası yok**; B1 fix'inin dayandığı *"her `MisdeliverySignature` verdict'i
her zaman capture yazar"* önermesi bağımsız olarak `worthCapturing` üzerinden teyit edildi — bu
önerme yanlış olsaydı fix sessizce etkisiz kalırdı, dolayısıyla kontrol edilmesi zorunluydu.

**KALICI DERS (validator tarafı):** bir düzeltmenin *"X'i okuyoruz artık"* iddiası, **X'in ne zaman
yazıldığını** denetlemeden onaylanamaz. B1'in tamamı `worthCapturing`'in şekline bağlıydı ve o şart
başka bir dosyada, başka bir task'ın (T125) kodundaydı.

---

## Notlar

- **Working tree:** temiz (`git status --short` boş).
- **Adım 0 — Main CI startup check:** son 3 run `31880715937` ✓ `success` · `31880715941` ✓ `success` · `31880257981` ✓ `success`.
- **Dış varsayımlar: yok.** Yeni paket, yeni dış API, plan tier veya platform varsayımı yok. Task tamamen mevcut iç bileşenler üzerinde: T125 motoru (✓ merge), T120/T121 envanter portu (✓ merge), T124 `DeliveryDeadline` yazıcısı (✓ merge). Steam okuma davranışı T122'de ölçülmüş ve `INTEGRATION_RUNBOOKS/STEAM_INVENTORY_READ_BEHAVIOR.md`'ye yazılmıştır — yeni bir varsayım eklenmedi.
- **T125 motoruna dokunuldu** (short-circuit genişletmesi). Değişiklik davranışsal olarak eşdeğerdir: gözlem `evidence` üzerinde yalnız OR yapar, dolayısıyla kayıtlı kanıt zaten `IsSufficientForDelivery()` ise tam tur da aynı verdict'i üretir. Eşdeğerlik iki testle sabitlendi: kapı kapalıyken sıfır okuma + `Held`, kapı açıkken sıfır okuma + `Delivered`. T126'nın `BUYER_CONFIRMED` yolu bit-bit aynı kaldı (o dalda kapı okuması hâlâ yapılmıyor).
