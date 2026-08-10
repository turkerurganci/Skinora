# T118 — TransactionStateMachine: 05 §4.2 Kapsam Denetimi

**Faz:** F7 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-10

---

## Kapsam neden daraldı

Plan T118'i "state machine yeniden yazımı" olarak tanımlıyordu. Yazım işi T117 dalında yapıldı: enum'dan değer silmek 136 dosyayı birden kırdığı için T117 + T118 + T132 proje sahibi onayıyla tek dalda birleştirildi, aksi hâlde ana dal derlenmez kalırdı (T117_REPORT §"Kapsam neden birleşti"). T117 raporunun kapanış maddesi kalan işi şöyle bırakmıştı: *"T118'e kalan iş, 05 §4.2 karşısında bağımsız bir kapsam denetimi."*

Bu görev o denetimdir. Üç kabul kriteri tek tek ölçüldü, açıklar kapatıldı.

## Yapılan İşler

### 1. AC1 — 05 §4.2 geçiş tablosu ↔ kod ↔ test

Doküman tablosu elle çıkarıldı ve koda karşı satır satır eşlendi. Sonuç: **28 geçiş**, kod ve test tablosu birebir örtüşüyor.

| Kaynak | Geçiş sayısı |
|---|---|
| `TransactionStateMachine.ConfigureTransitions()` | 28 (`Permit`/`PermitIf`) |
| `TransactionStateMachineTests.ValidTransitions` | 28 |
| 12 durum × 14 trigger − 28 = üretilen invalid matris | 140 |

`ValidTransitionData` + `InvalidTransitionData` birlikte tam bir çift yönlü kontrol kuruyor: bir geçiş koda eklenirse invalid teorisi kırılır, kodan çıkarılırsa valid teorisi kırılır. Tüm 14 trigger ve 12 durum kullanılıyor; ölü enum değeri yok.

**Bulunan tek uyumsuzluk dokümanda:** `ACCEPTED | seller_cancel | CANCELLED_SELLER` satırı 05 §4.2'de yoktu.

- v2.0 tablosunda satır **vardı** (`git show ddbdeac~1:Docs/05_TECHNICAL_ARCHITECTURE.md`).
- v3.0 yazımı (T115) onu düşürdü, ama "Kaldırılan trigger'lar" listesinde gerekçesi yok — `send_trade_offer_to_seller`, `escrow_item`, `send_trade_offer_to_buyer`, `buyer_decline` yazıyor, `seller_cancel` yazmıyor.
- Kod geçişi uyguluyor (`TransactionStateMachine.cs:230`) ve `POST /transactions/:id/cancel` bunu **fiilen kullanıyor**: `TransactionCancellationService.ResolveTrigger` ACCEPTED'daki satıcıyı `SellerCancel`'a haritalıyor (`:287`), `SellerDecline`'a değil.
- 07 §7.7 iptal yetkisi tablosu satıcıya CREATED / ACCEPTED / SELLER_CONFIRMED'da iptal hakkı veriyor.

Yani eksik olan tek şey 05 §4.2 satırıydı; 05 ↔ 07 çelişkisi (GUARDRAILS §5). Proje sahibi onayıyla satır geri eklendi, 05 v3.1'e alındı. **Davranış değişikliği yok.**

Ek olarak iki "kullanılamaz" kuralına adlandırılmış test yazıldı. Bunlar üretilen invalid matriste zaten vardı, ama ikisi de para güvenliği kararı — jenerik bir teori satırı olarak kalmaları niyeti gizliyordu:

- `BuyerCancel_FromPaymentReceived_IsRefused` — ödeme emanete girdikten sonra alıcı tek taraflı geri alamaz (02 §7).
- `AdminCancel_FromItemDelivered_IsRefused` — teslimat sonrası standart iptal yok; tek admin çıkışı `admin_resolve_refund` (aynı testte pozitif olarak da doğrulanıyor).

Yeni dokümante edilen satır için de bir test eklendi: `Accepted_SellerDeclineAndSellerCancel_ProduceIdenticalOutcome` — iki eşdeğer çıkışın aynı durumu, aynı `CancelledBy`'ı ve aynı sebebi ürettiğini pinliyor. Ayrışsalardı aynı kullanıcı aksiyonu hangi ucun ürettiğine göre farklı puanlanırdı.

### 2. AC2 — emekli status referansı

Backend test katmanında **11 kalıntı** bulundu. Hiçbiri davranış hatası değil: gövdeler `SELLER_CONFIRMED` kullanıyor, bayat olan adlar ve yorumlar. Kalıntı bir testin ne doğruladığını yanlış anlatır ve emekli modeli geri öğretir.

| Dosya | Tür | Düzeltme |
|---|---|---|
| `TimeoutFreezeServiceTests` (×3) | test **adı** | `FreezeAsync_ITEM_ESCROWED_*` → `FreezeAsync_SELLER_CONFIRMED_*`, `ResumeAsync_*` aynı |
| `TimeoutSchedulingServiceTests` | test **adı** | `..._Not_ITEM_ESCROWED` → `..._Not_SELLER_CONFIRMED` (servis gerçekten `SELLER_CONFIRMED` şart koşuyor, `TimeoutSchedulingService.cs:36`) |
| `TimeoutExecutorTests` | yorum | "payment timeout fires from ITEM_ESCROWED" → `SELLER_CONFIRMED` |
| `TimeoutExecutorSideEffectsTests` | sınıf XML doc | "executor only targets ITEM_ESCROWED" → `SELLER_CONFIRMED` + faz gerekçesi netleştirildi |
| `ReputationAggregatorTests` | yorum | `PreviousStatus = ITEM_ESCROWED` → `SELLER_CONFIRMED` (test zaten öyle seed ediyordu) |
| `AmountValidationServiceTests` | yorum | "past ITEM_ESCROWED" → "past the payment phase" |
| `BlockchainWebhookEndpointTests` (×2) | yorum | biri assert'in tam tersini söylüyordu ("state stays in ITEM_ESCROWED" ↔ `Assert.Equal(SELLER_CONFIRMED, …)`) |
| `SidecarWebhookRouteContractTests` | XML doc | PR #213'ün tarihsel anlatısı — emekli ad yerine "escrow step of the then-current custodial flow" + v3.0 notu |

**Kasıtlı istisna:** `EnumTests` emekli değerlerin adını anıyor ve anmalı — parity testinin işi hangi değerlerin kaldırıldığını belgelemektir (`TransactionStatus_ShouldHave12Values`, `NotificationType_ShouldHave26Values`, `TransactionTrigger` sayımı). Bu referanslar korundu.

**Kapsam dışı, plan sahibi başka:** `frontend/src/components/common/TransactionTimeline.test.tsx` (mock mesaj sözlüğünde `ITEM_ESCROWED`) → **T134** (*FE enum/StatusBadge/Timeline/i18n*, bağımlılık T118). `e2e/**` (7 spec + `src/api.ts` + `src/db.ts`) → **T137/T138**. Bu ikisi T118 dalında kapatılsaydı sahibi olan task'ların kabul kriterleri boşa çıkardı.

### 3. AC3 — `ApplyEmergencyHold` PAYMENT_RECEIVED + `DeliveryDeadline`

Kod dalı mevcut (`TransactionStateMachine.cs:97-108`): aktif deadline `SELLER_CONFIRMED → PaymentDeadline`, `PAYMENT_RECEIVED → DeliveryDeadline`. **Testi yoktu** — mevcut iki hold testi `SELLER_CONFIRMED` (kalan süre var) ve `ACCEPTED` (kalan süre yok) dallarını kapsıyordu, teslimat dalını kimse çalıştırmıyordu.

Eklenen testler:

- `ApplyEmergencyHold_OnPaymentReceived_CapturesDeliveryDeadlineRemainder` — `DeliveryDeadline`'dan kalan süreyi alıyor. Test bilinçli olarak **bayat bir `PaymentDeadline`** de seed ediyor: yanlış kolonu okuyan bir regresyon bu kurulumda geçmez.
- `ApplyEmergencyHold_OnPaymentReceived_PastDeliveryDeadline_ClampsToZero` — negatif kalan süre 0'a kırpılıyor (`CK_Transactions_FreezeActive`).
- `ReleaseEmergencyHold_ClearsHoldFlagAndFreezeFields` — kalan süresi olan bir state'e taşındı ve `TimeoutRemainingSeconds`'ın **korunduğu** eklendi. Kodun yorumu bunu iddia ediyordu, hiçbir test doğrulamıyordu; 05 §4.4 "Otorite" kuralı reschedule'ı bu alandan türetiyor.

Üretimdeki hold yolu iki adımlı — `TimeoutFreezeService.FreezeAsync` ön-geçişi + state machine damgası — ve freeze motorunun `PAYMENT_RECEIVED → DeliveryDeadline` dalının da testi yoktu. AC3'ün kanıtı yarım kalmasın diye iki test daha eklendi:

- `FreezeAsync_PAYMENT_RECEIVED_Captures_DeliveryDeadline_Remainder`
- `ResumeAsync_PAYMENT_RECEIVED_Rewrites_DeliveryDeadline_From_Remainder` — resume'ün yeni deadline'ı `now + remainder` olarak yazdığını doğruluyor (`oldDeadline + elapsed` değil — 06 §8.1 otorite kuralı).

### 4. Denetimde çıkan borç (proje sahibi onayıyla bu dalda kapatıldı)

| # | Bulgu | Sonucu |
|---|---|---|
| 1 | `TransactionTimedOutEvent` XML doc'unda silinmiş `ItemRefundToSellerRequestedEvent` tipine dangling `<see cref>` | T117 B3'ün kapattığı 5 cref'ten arta kalan altıncısı. Derleyici sessiz çünkü `GenerateDocumentationFile` kapalı — bu sınıf yalnız grep ile bulunur |
| 2 | `TransactionStatusChangedEvent` XML doc'u tamamen emekli orkestrasyon bacaklarını anlatıyordu (`SendTradeOfferToSeller`, `EscrowItem`, `SendTradeOfferToBuyer`) | Olay hâlâ canlı ve iki tüketicisi var; belgesi silinmiş bir dünyayı tarif ediyordu. Yeniden yazıldı + **v3.0 üretici durumu** açıkça kaydedildi: kaynakta yayıncı kalmadı, T123/T124 yazacak. Tüketiciler `ToStatus ∈ {SELLER_CONFIRMED, PAYMENT_RECEIVED}` üzerinde çalışıyor |
| 3 | `TransactionStatusChangedRealtimeConsumer` XML doc'u aynı bayat bacak listesini tekrarlıyordu | Yeniden yazıldı — tüketici zaten saf röle, hangi bacakların bindiği üreticinin kararı |
| 4 | `FraudFlagService` freeze ön-geçiş yorumu: *"ApplyEmergencyHold only computes the remainder for ITEM_ESCROWED … CREATED / ACCEPTED / TRADE_OFFER_SENT_TO_SELLER / PAYMENT_RECEIVED / TRADE_OFFER_SENT_TO_BUYER"* | Artık iki kat yanlış (emekli adlar + `PAYMENT_RECEIVED` bu listede olmamalı, state machine onu biliyor). Ön-geçişin gerçek gerekçesi kaldı: `CREATED`/`ACCEPTED` |
| 5 | `AdminTransactionService` aynı yorumun varyantı ("non-SELLER_CONFIRMED states") | İki fazlı gerçek davranışla değiştirildi |
| 6 | **`AdminUserActivityProvider._terminalStates`'te `REFUNDED` eksik** | Davranış hatası. Kendi XML doc'u `AdminTransactionQueryService` / `AdminDashboardService` listelerini "birebir" yansıttığını iddia ediyor; o iki liste `REFUNDED` taşıyor, bu taşımıyordu. Sonuç: iadeyle kapanmış bir işlem S20'de **aktif** sayılıyor ve AD19d hold-by-user yükleminde hâlâ hold'lanabilir görünüyordu. WP5 dönemi borcu (`git diff` boş), T117 validator'ı da işaret etmişti |

(6) için regresyon testi eklendi: `GetUserDetail_RefundedTransaction_IsNotCountedAsActive`. Test fixture'ının `CancelledBy` switch'i `REFUNDED`'ı tanımıyordu (satır `CK_Transactions_Cancel`'a takılırdı) — `ADMIN`'e haritalandı, 05 §4.2 REFUNDED'ı iptal alanlarını yeniden kullanan terminal durum olarak tanımlıyor.

### 5. Bildirim şablonu boşluğu (denetim sırasında bulundu, proje sahibi onayıyla bu dalda kapatıldı)

Emekli-status taraması test katmanından kaynak katmanına genişletildiğinde ortaya çıktı ve üçünden hiçbir kabul kriterine girmiyordu — ama kullanıcıya dönen bir kırıktı.

**Bulgu.** T117 `NotificationType.ITEM_ESCROWED → PAYMENT_WINDOW_OPEN` ve `TRADE_OFFER_SENT_TO_BUYER → DELIVERY_EXPECTED` yeniden adlandırmasını yaptı; `NotificationTemplates.*.resx` anahtarları **4 dilde de eski adlarıyla kaldı**. `ResxNotificationTemplateResolver` anahtarı `$"{type}_{suffix}"` ile üretir (`:78`) ve bulamazsa uyarı loglayıp **anahtar adının kendisini** döndürür (`:98`).

Sonuç: P2P mutlu yolunun iki merkezi bildirimi ham anahtar olarak render olurdu.

| Tip | Alıcısı | Render edilecek olan |
|---|---|---|
| `PAYMENT_WINDOW_OPEN` | Alıcı | `PAYMENT_WINDOW_OPEN_Title` / `..._Body` — ödeme adresi ve tutar hiç görünmez |
| `DELIVERY_EXPECTED` | Satıcı | `DELIVERY_EXPECTED_Title` / `..._Body` — item'ı göndermesi gerektiği hiç söylenmez |

Ölçüm: enum 26 değer, resx 28 anahtar → **2 eksik**, **4 ölü** (`ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_BUYER` yeniden adlandırıldı; `ITEM_RETURNED`, `ADMIN_STEAM_BOT_ISSUE` bot katmanıyla birlikte enum'dan tamamen kaldırıldı).

**Neden hiçbir şey kırılmadı.** Resolver bozulmak yerine **derece kaybediyor** — bu tasarım tercihi doğru (05 §7.3 placeholder semantiği), ama katalog düzeyinde bir testi zorunlu kılıyor. Mevcut `ResxNotificationTemplateResolverTests` yalnız kültür fallback'ini ve parametre ikamesini sınıyordu; anahtar başına test yazılan yaklaşım, tanımı **unutulan** anahtarı yapısal olarak göremez. 107 bildirim testi yeşildi.

**Kapatılış.**

1. 4 dilde `PAYMENT_WINDOW_OPEN_*` + `DELIVERY_EXPECTED_*` yazıldı. Metin 06 §2.13 kataloğundan birebir türetildi (*"Satıcı hazır, ödeme yapabilirsin"* / *"Ödeme alındı, item'ı alıcıya gönder"*). `PAYMENT_WINDOW_OPEN` gövdesi `{Amount}` + `{PaymentAddress}` alıyor — tüketicinin gerçekten gönderdiği parametreler (`EscrowedAndTradeOfferNotificationConsumer:105-109`); `DELIVERY_EXPECTED` parametresiz, tüketici de parametre göndermiyor.
2. 4 ölü anahtar 4 dilden silindi.
3. **`NotificationTemplateParityTests`** eklendi — 4 locale × (26 tip × 2 anahtar), **iki yönlü**: eksik anahtar da, yetim anahtar da kırar. `GetAllStrings(includeParentCultures: false)` kullanıyor, yani tr/es/zh'nin İngilizceye sessizce kayması da yakalanır (WP17 dört-dil parity'si).
4. **Negatif prova yapıldı:** `PAYMENT_WINDOW_OPEN_Title` geçici olarak `tr.resx`'te yeniden adlandırıldı → test `Locale 'tr' has no template for: PAYMENT_WINDOW_OPEN_Title` ile kırıldı, sonra geri alındı. Guard'ın gerçekten koruduğu doğrulandı; yeşil kalan bir parity testi kanıt değildir.
5. Katalog dokümanları koda hizalandı: **06 §2.13** (2 emekli satır silindi, eksik `ADMIN_PLATFORM_OUTAGE` eklendi, 26 değer + v3.0 notu) ve **03 §12.1/§12.2/§12.3** (satıcıya `DELIVERY_EXPECTED`, alıcıya `PAYMENT_WINDOW_OPEN`, admin'e `ADMIN_PLATFORM_OUTAGE`; emekli üç satır kaldırıldı). **03 §3.4 adım 1** custodial cümlesi düzeltildi — ödeme penceresini açan şey item emaneti değil, satıcının hazırlık onayı.

**Plan boşluğu.** F7 listesinde backend bildirim şablonlarını üstlenen görev yok: T134 frontend i18n (`frontend/src/i18n/messages/*.json`), T136 admin sayfaları. T119a ile aynı sınıf boşluk.

## Etkilenen Modüller / Dosyalar

**Doküman**
- `Docs/05_TECHNICAL_ARCHITECTURE.md` v3.1 — §4.2 tablosuna 1 satır + 1 açıklayıcı not
- `Docs/06_DATA_MODEL.md` v6.1 — §2.13 kataloğu koda hizalandı (−2 emekli satır, +1 eksik satır, +v3.0 notu)
- `Docs/03_USER_FLOWS.md` v3.1 — §3.4 adım 1 + §12.1/§12.2/§12.3 bildirim kataloğu

**Kaynak**
- `Skinora.API/Services/AdminUserActivityProvider.cs` — `REFUNDED` terminal listeye eklendi (davranış değişikliği)
- `Skinora.Notifications/Resources/NotificationTemplates{,.tr,.es,.zh}.resx` — 2 yeni tip × başlık+gövde, 4 ölü anahtar silindi (davranış değişikliği)
- `Skinora.Shared/Events/TransactionTimedOutEvent.cs` · `TransactionStatusChangedEvent.cs` — XML doc
- `Skinora.Realtime/.../TransactionStatusChangedRealtimeConsumer.cs` — XML doc
- `Skinora.Fraud/.../FraudFlagService.cs` · `Skinora.Transactions/.../AdminTransactionService.cs` — freeze ön-geçiş yorumları

**Test**
- `Skinora.Transactions.Tests/Unit/StateMachine/TransactionStateMachineTests.cs` — +5 test
- `Skinora.Notifications.Tests/Unit/NotificationTemplateParityTests.cs` — **yeni dosya**, 4 test (locale başına 1)
- `Skinora.Transactions.Tests/Integration/Timeouts/TimeoutFreezeServiceTests.cs` — +2 test, 3 ad
- `Skinora.API.Tests/Integration/AdminUsersEndpointTests.cs` — +1 test, fixture `CancelledBy` haritası
- `TimeoutSchedulingServiceTests` (1 ad) · `TimeoutExecutorTests` · `TimeoutExecutorSideEffectsTests` · `ReputationAggregatorTests` · `AmountValidationServiceTests` · `BlockchainWebhookEndpointTests` · `SidecarWebhookRouteContractTests` — yorum/XML doc

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 05 §4.2'deki her geçişin geçen bir testi var | ✓ | 28 doküman satırı = 28 `Permit`/`PermitIf` = 28 `ValidTransitions` satırı; `Fire_ValidTransition_MovesToTargetState` 28 vaka + `Fire_InvalidTransition_…` 140 vaka geçiyor. Doküman tarafındaki tek eksik (`ACCEPTED\|seller_cancel`) 05 §4.2'ye geri eklendi; iki "kullanılamaz" kuralına adlandırılmış test yazıldı. `TransactionStateMachineTests` **212/212** (öncesi 207) |
| 2 | Hiçbir test emekli status'e referans vermiyor | ✓ | Backend testlerinde 11 kalıntının 11'i temizlendi; kalan tek grup `EnumTests`'in emekliliği **belgeleyen** yorumları (kasıtlı, yukarıda gerekçeli). Doğrulama: `rg "ITEM_ESCROWED\|TRADE_OFFER_SENT_TO_(SELLER\|BUYER)" backend/tests` → yalnız `EnumTests` |
| 3 | `ApplyEmergencyHold` PAYMENT_RECEIVED + `DeliveryDeadline` dalını içeriyor | ✓ | `TransactionStateMachine.cs:100`; yeni `ApplyEmergencyHold_OnPaymentReceived_CapturesDeliveryDeadlineRemainder` + `…_PastDeliveryDeadline_ClampsToZero`. Üretim yolunun diğer yarısı için `FreezeAsync_PAYMENT_RECEIVED_…` + `ResumeAsync_PAYMENT_RECEIVED_…` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 error / 0 warning | `dotnet build Skinora.sln -c Debug` |
| Unit | ✓ **1330/1330** | CI filtresi (`FullyQualifiedName!~.Integration&!~.Contract`), solution genelinde. Baseline 1321 → +5 state machine, +4 şablon parity |
| Integration | ✓ **1077/1077** | Proje bazında seri koşum (T117'nin ölçüm notu). Baseline 1074 → +1 AdminUsers, +2 TimeoutFreeze. Kırılım: API 450 · Transactions 307 · Fraud 73 · Platform 65 · Notifications 60 · Disputes 41 · Auth 37 · Admin 22 · Shared 16 · Payments 6 |
| Contract | ✓ **9/9** | `SidecarWebhookRouteContractTests` (API 4) + Shared 5 |
| Migration | — | Bu görevde migration yok; EF modeli değişmedi |

## Altyapı Değişiklikleri

- Migration: Yok — EF modeline dokunulmadı
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Alınan kararlar

1. **`ACCEPTED | seller_cancel` dokümana geri eklendi, koddan kaldırılmadı.** Alternatif (koddan kaldırıp yalnız `seller_decline` bırakmak) 07 §7.7'yi de değiştirmeyi ve cancel ucunun davranışını bozmayı gerektirirdi. Dokümanın v2.0'da satırı taşıması, düşüşün karar değil eksiklik olduğunu gösteriyor.
2. **İki eşdeğer ACCEPTED çıkışı ayrı trigger olarak korunuyor.** Birleştirmek `TransactionHistory`'de niyeti (hazırlık reddi ↔ genel iptal) silerdi. Eşdeğerlik test ile pinlendi.
3. **`EnumTests`'teki emekli adlar korunuyor.** Parity testinin varlık sebebi emekliliği belgelemek; bu adları silmek testi anlamsızlaştırırdı. AC2 raporda adı konmuş istisnayla kapatıldı.
4. **FE ve E2E kalıntıları bu dalda kapatılmadı.** Sahipleri T134 ve T137/T138; kapatılsalardı o task'ların kabul kriterleri boşa çıkardı.
5. **`AdminUserActivityProvider`'a yalnız `REFUNDED` eklendi, `_cancelledStates`'e dokunulmadı.** S20 "İptal" sayacının REFUNDED'ı içerip içermeyeceği 04 §8.9'da tanımsız; `AdminTransactionQueryService._cancelledStates` (S15 filtre grubu, 04 §8.4) farklı bir ekranın kuralı. Tanımsız olanı doğaçlamak yerine aşağıya not düşüldü.
6. **Bildirim şablonu metni uydurulmadı, 06 §2.13'ten türetildi.** Katalog `PAYMENT_WINDOW_OPEN` için "Satıcı hazır, ödeme yapabilirsin", `DELIVERY_EXPECTED` için "Ödeme alındı, item'ı alıcıya gönder" diyor; dört dildeki metin bunun karşılığıdır. `DELIVERY_EXPECTED` bilinçli olarak **parametresiz** — tüketici bu tip için `Parameters` göndermiyor, placeholder koymak literal `{...}` üretirdi.
7. **Ölü şablon anahtarları silindi, korunmadı.** `ITEM_RETURNED` ve `ADMIN_STEAM_BOT_ISSUE` `NotificationType`'da yok; hiçbir kod yolu üretemez, dolayısıyla erişilemez metin. Bırakılsalardı parity testinin yetim kontrolünü de kırarlardı ve bir sonraki okuyucuya emekli tiplerin hâlâ var olduğunu öğretirlerdi.
8. **03'ün geri kalanına dokunulmadı.** Aşağıdaki listeye bkz — gerekçe orada.

## Known Limitations / Follow-up

- **`TransactionStatusChangedEvent`'in kaynakta yayıncısı yok.** Emekli bot orkestrasyon bacakları T117'de silindi; P2P üreticileri T123 (`seller_confirm_ready`) ve T124 (`confirm_payment`) yazacak. Tüketiciler ve testleri ayakta. Olayın XML doc'una kaydedildi, kod değişikliği yapılmadı — üretici tasarımı o task'ların kararı.
- **04 §8.9 S20 "İptal" sayacının REFUNDED'ı kapsayıp kapsamadığı tanımsız.** Bu görevde değiştirilmedi. Ürün kararı gerektiriyor; karar verilirse `AdminUserActivityProvider._cancelledStates` tek satırlık değişiklik.
- **05 §4.2 şeması ACCEPTED sütununun iptal etiketlerini taşımıyor** (ASCII genişliği yetmiyor). Normatif liste tablodur; §4.2'ye bunu söyleyen bir not eklendi.
- **8 advisory E2E leg'i kırmızı kalmaya devam ediyor** — T117'den beri bilinen, planda öngörülmüş durum (T137 → T138). CI Gate'i bloke etmiyorlar.
- **03'te kapsam dışı bırakılan custodial kalıntılar.** Bildirim kataloğu düzeltilirken 03'te item-custody dilinin başka yerlerde de yaşadığı görüldü. Bu görevde **dokunulmadı**, çünkü dağınık cümle yaması yerine tutarlı bir §5/§8 turu gerekiyor ve o tur kendi gözden geçirmesini hak ediyor. Tam liste:

  | Yer | Kalıntı |
  |---|---|
  | §1.1 aktör tablosu | Satıcı "item'ı **emanet eden**" olarak tanımlı |
  | §3.3 adım 6 | "Eğer item zaten platformdaysa → item satıcıya iade edilir" |
  | §5.3 adım 3 | İşlemin mevcut durumu **`ITEM_ESCROWED`** olarak yazılı (emekli status adı) |
  | §5.3 adım 5 | "Item emanette kalır — emanet durumu etkilenmez" |
  | §5.4 adım 1 | "…işlem iptal edilmiş, **item satıcıya iade edilmiş**" |
  | §8.7 adım 6 | İade kuralları: "Item platformda emanetteyse → satıcıya iade edilir / Her iki varlık da varsa → ikisi de iade edilir" |

  Karşıtlık için: §4.2 adım 3, §4.3 adım 3 ve §3.5 zaten "item hiçbir zaman platformda olmadı" diyor — yani doküman kendi içinde çelişiyor (GUARDRAILS §5). Ayrı bir doküman görevi öneriliyor.
- **F7'de backend bildirim şablonlarını üstlenen görev yok.** T118 boşluğu kapattı ama sahiplik hâlâ tanımsız; ileride yeni bir `NotificationType` eklendiğinde parity testi kırılacak ve o an bunu kimin yazacağı belli olmalı. T134 (FE i18n) ve T136 (admin sayfaları) bu yüzeyi kapsamıyor.

## Notlar

- **Working tree:** Oturum başında temiz.
- **Main CI startup check:** Son 3 tamamlanmış run `success` — `31363716088`, `31363716080`, `31335569151`.
- **Dış varsayım:** Yok. Görev tamamen repo içi; yeni paket, plan tier veya dış API bağımlılığı eklenmedi.
- **Branch izolasyon:** `git log main..HEAD --format='%s' | grep -oE '^T[0-9]+…'` yalnız `T118` üretiyor.
- **Ölçüm ortamı:** Integration koşumu için Docker Desktop oturum içinde başlatıldı; testcontainers SQL Server 2022 kullanıyor.
- **Ölçüm artefaktı (kaydedilmiştir).** Integration turlarından biri `Skinora.API.Tests`'te 6 hata verdi. Sebep ölçüm hatasıydı: o tur, contract süiti (`dotnet test Skinora.sln --filter .Contract` — aynı assembly'yi ikinci bir test host'unda açar) ile **eşzamanlı** koşuyordu. Hiçbir şey başka koşmazken seri tekrarda aynı süit **450/450** geçti, iki kez. Rapordaki sayılar temiz seri turdan alınmıştır. Bu, T117 raporundaki "proje bazında seri koş" notunun aynı sınıfı — CI ayrı leg + sağlanan connection string kullandığı için bu sınıfa girmez.

## Commit & PR

- Branch: `task/T118-state-machine-audit`
- Branch: `task/T118-state-machine-audit`
- Commit: `3428098` — T118: TransactionStateMachine 05 §4.2 kapsam denetimi
- PR: [#224](https://github.com/turkerurganci/Skinora/pull/224)
- CI: ✓ **PASS** — HEAD `3428098`, run [`31370265288`](https://github.com/turkerurganci/Skinora/actions/runs/31370265288), `conclusion=success`

| Job | Sonuç |
|---|---|
| 1. Lint · 2. Build · 3. Unit · 4. Integration · 5. Contract · 6. Migration dry-run · 7. Docker build (backend) | ✓ success |
| **CI Gate** | **✓ success** |
| 3b. JS test (vitest) | skipped (JS yolu değişmedi) |
| 8× E2E (advisory) | ✗ failure — T117'den beri bilinen, planda öngörülmüş (T137 → T138); `continue-on-error` |

> Bu tabloyu taşıyan commit (`2e0731b`, yalnız rapor/status/memory referansları) kendisi de bir CI turu tetikler. Yetkili olan **son tamamlanmış run**'dır; doc-only turun sonucu PR #224 üzerinde görünür ve yapım chat'inde raporlanmıştır.
