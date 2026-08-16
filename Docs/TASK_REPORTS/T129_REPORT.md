# T129 — Mutabakat süresi + trade geri alma koruması

**Faz:** F7 (P4 — Payout tamponu) | **Durum:** ✗ FAIL (bağımsız doğrulama 2026-08-16 — düzeltme turu bekliyor) | **Tarih:** 2026-08-16

---

## Yapılan İşler

Bu görev, teslim edilmiş bir işlemin parasının **ne zaman** ve **hangi koşulla** satıcıya gideceğini kuran son halkadır. Steam korumalı bir trade'i 7 gün boyunca geri alınabilir tutuyor ve bunu trade'in her iki tarafı da Steam Support'a başvurmadan yapabiliyor (02 §4.5.1); dolayısıyla teslimatın doğrulanmış olması ödemek için yeterli değil. Görev iki şeyi birden getirir: **bekleme** (`PayoutEligibleAt`) ve — asıl koruma olan — **sürenin sonundaki kontrol**.

1. **Üç `Settlement` SystemSetting'i** (06 §3.17, 02 §16.2): `payout_settlement_days` (8, validator 7'nin altını reddeder), `settlement.unreadable_escalation_hours` (48), `settlement.reversal_auto_refund_enabled` (false — launch kapısı). Okuma tarafı `SettlementSettingsProvider`; her fallback güvenli yönde (poisoned satır pencereyi kısaltamaz, kapıyı açamaz).
2. **`PayoutEligibleAt` yazıcısı** (`SettlementWindowStamper`) ve — daha önemlisi — kolonun **ITEM_DELIVERED giriş guard'ına** eklenmesi. `DeliverItem`'ın iki üretim çağıranı (T126 confirm-receipt, T127 timeout turu) stamper'dan geçmek zorunda; geçmezse teslimat hiç gerçekleşmez.
3. **`SettlementVerificationService`** — 02 §4.5.1'in son kontrolü, **iki taraflı** (K1). Alıcı tarafı önce `DeliveredBuyerAssetId` ile tam test edilir, o yoksa sınıf sayımı `baseline + 1` referansına karşı okunur. Item gitmişse satıcı tarafı okunur: orijinal asset geri döndüyse geri alma imzası, dönmediyse ayırt edilemeyen ayrılma.
4. **`SettlementVerificationJob`** (5 dakikada bir, batch 10) — dört verdict'i eyleme çevirir: `SettlementVerifiedAt` damgası / `delivery_reversed` (kapı açıksa) / admin eskalasyonu / eşiğe kadar sessiz retry.
5. **Geri alma dalı**: REFUNDED + `PaymentRefundToBuyerRequestedEvent` (WP2 boru hattı) + hesap düzeyinde `DELIVERY_REVERSED` fraud flag + iki taraf bildirimi + realtime status event + `TransactionHistory` satırı.
6. **Payout ve sweep kapıları**: her ikisi de artık `SettlementVerifiedAt NOT NULL ∧ DeliveryReversedAt NULL` ister. Sweep kapısı yeni bir açığı kapatır (aşağıda).
7. **İtibar** (K4 / planın açık maddesi): 06 §3.1 paydası `REFUNDED[DeliveryReversedAt NOT NULL]` ile genişledi; admin dispute iadesi dışarıda kaldı.
8. **İki yeni kolon** (`SettlementCheckedAt`, `SettlementEscalatedAt`) + migration `T129_SettlementCheckColumns`.

### Yapım öncesi kararlar (proje sahibi, 2026-08-16 — dördü de öneri yönünde onaylandı)

| # | Karar | Gerekçe |
|---|---|---|
| K1 | Kontrol **iki taraflı**; ayırt edilemeyen ayrılma admin'e | Dokümanın "item alıcıda yok = geri alınmış" eşitliği pencerenin tamamında geçerli değil: Steam trade ile edinilen item'ı **7 gün** kısıtlıyor (T122 runbook §6.1) ama mutabakat **8 gün** — son bir gün alıcı skini meşru devredebiliyor. Tek taraflı okuma o alıcıya tam iade verir, item'ı da bırakır, teslim etmiş satıcıya fraud flag koyar: kuralın satıcıya karşı kapattığı dolandırıcılığın **simetriği**. Geri alma item'ı satıcıya döndürür, devir döndürmez |
| K2 | Negatif dal **launch kapısı** arkasında | Geri alma imzası ölçülmemiş bir çıkarım (T122 gerçek rollback gözleyemedi, runbook §7). T125'in `delivery.inventory_evidence_auto_release_enabled` kapısının ikizi: kapalıyken imza kaydedilir + admin'e eskale edilir, para parkta |
| K3 | Okunamaz dal **ayarlı eşik** (48s) + admin kuyruğu | Sınırsız retry alıcının profilini gizleyerek satıcının ödemesini süresiz bloklamasına izin verirdi. Eşik yalnız "ne zaman insana sorulur"u belirler; ödeme her hâlükârda parkta |
| K4 | Yeni `FraudFlagType.DELIVERY_REVERSED`, **hesap** düzeyinde | 02 §4.5.1 "satıcı **hesabına**" diyor ve §14.2 tekrarı sayıyor — sayılabilmesi için vakanın kuyrukta ayırt edilebilir olması gerek. 06 §3.12: ACCOUNT_LEVEL satırları `TransactionId` taşımaz, işlem ID'si details payload'ında |

### Yapım sırasında bulunan ve kapatılan açık

**`SweepQueueJob`'da mutabakat kapısı yoktu.** Job'ın kendi gerekçesi (WP3, owner kararı 2026-06-15) "depozit, iade çekilebilecek yerde kalmalı" olduğu hâlde kapısı yalnız `ITEM_DELIVERED`'dı. Yedinci günde geri alınan bir trade tam da o iadeyi üretir; sweep teslimatta çalıştığı için depozit, ihtiyaç duyulmasına en yakın anda boşalmış olurdu. Kapı payout'unkiyle aynı çifte bağlandı.

## Etkilenen Modüller / Dosyalar

**Yeni (Transactions/Settlement):** `ISettlementSettingsProvider.cs`, `SettlementSettingsProvider.cs`, `SettlementWindowStamper.cs`, `ISettlementVerificationService.cs`, `SettlementVerificationService.cs`, `SettlementVerificationResult.cs`, `SettlementVerificationJob.cs`
**Yeni (Shared/Events):** `SettlementReversalDetectedEvent.cs`, `SettlementReviewRequiredEvent.cs`
**Yeni (Notifications):** `SettlementReversalNotificationConsumer.cs`, `SettlementReviewAdminNotificationConsumer.cs`
**Yeni (migration):** `20260816140704_T129_SettlementCheckColumns`

**Değişen:** `Transaction.cs` (+2 kolon) · `TransactionStateMachine.cs` (giriş invariantı) · `TransactionConfiguration.cs` (index yorumu) · `DeliveryConfirmationService.cs`, `DeliveryTimeoutRound.cs` (stamper) · `SellerPayoutQueueJob.cs`, `SweepQueueJob.cs` (kapı) · `ReputationAggregator.cs` (payda) · `ITransactionFraudFlagWriter.cs` + `TransactionFraudFlagWriter.cs` (hesap flag'i portu) · `FraudFlagType.cs` (+1 değer) · `SystemSettingsCatalog.cs`, `SystemSettingSeed.cs`, `SystemSettingsValidator.cs` · `TransactionsModule.cs`, `OutgoingTransferJobsRegistrar.cs` (DI + cron)

**Frontend:** `lib/admin/settingsCatalog.ts` (`settlement` grubu — aksi hâlde üç ayar "Diğer" kovasına düşerdi) + 4 dil `adminSettings.groups.settlement`

**Dokümanlar:** 02 §4.5.1/§16.2 · 03 §2.4 · 05 §4.2 · 06 §3.1/§3.5/§3.17 · 07 §9.8 · 11 (T129 kararları) · DEPLOY_RUNBOOK §C + yeni §I

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `payout_settlement_days` SystemSetting (varsayılan 8) eklendi | ✓ | `SystemSettingSeed.cs` satır 61 (`Default(61, ..., "8")`), `SystemSettingsCatalog.cs` (`settlement` API kategorisi), migration `InsertData`. Validator 7 altını reddeder — `SystemSettingsValidator.MinimumSettlementDays` |
| 2 | ITEM_DELIVERED girişinde `PayoutEligibleAt` hesaplanıyor | ✓ | `SettlementWindowStamper.Stamp` iki çağıranda; **guard koşulu** olduğu için atlanamaz (`HasDeliveryEntryInvariant`). Testler: `DeliverItem_WithoutPayoutEligibleAt_ThrowsInvalidTransition`, confirm-receipt ve timeout-round happy path'lerinde `PayoutEligibleAt = now + 8g` assertion'ı |
| 3 | `SellerPayoutQueueJob` yalnız `PayoutEligibleAt` geçmiş işlemleri alıyor | ✓ | T126-F1'de erken uygulanmıştı; T129 kapıyı tamamladı (`SettlementVerifiedAt != null && DeliveryReversedAt == null`). Testler: `ElapsedWindow_WithoutSettlementVerification_IsSkipped`, `ReversedDelivery_IsSkipped_EvenWithSettlementStamp` |
| 4 | Ödeme öncesi son kontrol (item / yok / okunamıyor) | ~ **Kısmi** (validator) | Dört dal kodlanmış ve test edilmiş (`SettlementVerificationService` + `SettlementVerificationJob`, 8 + 16 test). **Ancak birinci dal ("item var → ödeme akar") dokümante edilmiş bir popülasyon için yapısal olarak erişilemez** — bkz. bulgu **B1**. Ayrıca K1'in ayırt edici sinyali "satıcıya döndü" yerine "satıcıda var" olarak uygulanmış (bulgu **N1**) |
| 5 | COMPLETED guard'ı: `SettlementVerifiedAt NOT NULL && DeliveryReversedAt NULL` | ✓ | T117/T118'de yazılmıştı (`HasSettlementClearance`), T129'da kanıtlandı ve aynı çift payout+sweep sorgularına yansıtıldı (guard tek başına yetmez: ikisi de COMPLETED'dan önce para hareket ettirir) |
| 6 | Süre içinde açılan dispute ödemeyi bloklar | ✓ | Üç sorguda da `!HasActiveDispute` (settlement job + payout + sweep). Test: `IneligibleTransactions_AreNotEvenRead(kind: "dispute")` |
| 7 | `SweepQueueJob` aynı kapıya bağlandı | ✓ | Sorgu + döngü içi yeniden doğrulama. Testler: `UnverifiedSettlement_IsSkipped`, `ReversedDelivery_IsSkipped` |
| 8 | `delivery_reversed` → REFUNDED'ın itibara etkisine karar verildi ve 06 §3.1 yazıldı | ~ **Kısmi** (validator) | Kriterin saydığı üç artefakt teslim: 06 §3.1 formülü + `ReputationAggregator` + 2 test. **Ancak formülü çalıştıran tetikleyici bağlanmamış** — `SuccessfulTransactionRate` denormalized bir kolondur ve geri alma yolunda hiç yeniden hesaplanmaz; bkz. bulgu **B3** |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (`Category=Unit`) | ✓ 236/236 | `dotnet test --filter "Category=Unit"` — Transactions 131, API 46, Notifications 40, Disputes 17, Users 2. Enum değeri eklendiği için tam Unit suite koşuldu (parity kuralı) |
| Backend tam suite | ✓ 2643/2643 | Assembly başına seri koşum: Transactions **995** · API **540** · Shared **400** · Platform **189** · Notifications **171** · Auth 120 · Fraud **91** · Disputes 68 · Realtime 40 · Steam 39 · Users 22 · Admin 22 · Payments 6 |
| Build | ✓ 0 warning / 0 error | `dotnet build -c Debug` |
| FE lint | ✓ exit 0 | `npm run lint` |
| FE i18n parity | ✓ 1303 × 4 | `npm run i18n:check` — "identical key sets"; T128 tabanı 1302 + 1 (`adminSettings.groups.settlement`) |
| FE vitest | ✓ 33/33 | 9 dosya |
| FE tsc | ✓ exit 0 | `npx tsc --noEmit` |

**Yeni testler:** `SettlementVerificationServiceTests` (8), `SettlementVerificationJobTests` (16 — 6'sı theory vakası), `SettlementWindowStamperTests` (3), + mevcut suite'lere 7 kapı/regresyon testi.

**Paralel koşum artefaktı (kanıtlı, T129 kaynaklı değil).** İlk tam suite koşumu 13 assembly'yi **paralel** çalıştırdı ve 26 başarısızlık verdi. Bunların 9'u gerçekti (seed satır sayısı 60→63, `payout_settlement_days`/`settlement.*` anahtar listeleri, `FraudFlagType` 5→6 parity) ve düzeltildi; kalan 17'si tek paylaşımlı SQL Server üzerindeki çekişmeden geliyordu — assembly başına **seri** yeniden koşumda Fraud 91/91, Notifications 171/171, Platform 189/189, Shared 400/400 hepsi temiz geçti (F6 gate'inde de kaydedilmiş olan "integration timeout artefaktı" ile aynı sınıf). FE vitest'in ilk koşumu da aynı yükün altında worker başlatamadı; backend suite bitince 33/33 geçti.

## Doğrulama

**Tarih:** 2026-08-16 · **Dal:** `task/T129-settlement-window-reversal-guard` · **Commit:** `0a21df8` · **Yöntem:** bağımsız spec-conformance review (yapım raporu Faz 3'e kadar okunmadı; dokuz boyutlu paralel tarama + her ham bulgu için ayrı düşman doğrulayıcı — 48 ham bulgudan 34'ü çürütüldü, 14'ü ayakta kaldı)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | **✗ FAIL** |
| Bloke edici bulgu | 3 (B1, B2, B3) |
| Bloke etmeyen bulgu | 7 (N1–N7) |
| Düzeltme gerekli mi | **Evet** |

### Kapı kontrolleri

| Adım | Sonuç |
|---|---|
| -1 Working tree | ✓ temiz |
| 0 Main CI son 3 run | ✓ `31944080720` · `31944080697` · `31909528316` — üçü de `success` |
| 0b Repo memory drift | ✓ `.claude/memory/MEMORY.md`'de T129 satırları mevcut |
| 8a Dal CI | ✓ dal HEAD `0a21df8` → run [`31960474916`](https://github.com/turkerurganci/Skinora/actions/runs/31960474916), CI Gate `success`, bloke edici 10 job yeşil. 8 advisory E2E leg kırmızı — **main'in son run'ında (`31944080720`) da aynı 8'i kırmızı**, T129 kaynaklı değil |
| Lokal unit suite (validator) | ✓ **1405/1405**, 0 hata (`dotnet test Skinora.sln -c Release --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"`) |

### Bloke edici bulgular

| # | Seviye | Açıklama | Dosya |
|---|---|---|---|
| **B1** | S1 Sapma | **Alıcı envanteri SELLER_CONFIRMED anında gizli olan işlemler mutabakatta kalıcı olarak kilitleniyor.** `BuyerBaselineCapturedAt`/`BuyerBaselineClassCount` bu vakada bilinçli olarak NULL bırakılıyor (`TransactionReadinessService.cs:205-223`, 03 §2.3 gereği bloke etmemeli) ve alıcı-onaylı teslimat yolunda `DeliveredBuyerAssetId` de NULL kalıyor (`DeliveryConfirmationService.cs:173-181`). İki kolon da ITEM_DELIVERED'dan **sonra** hiçbir yolla dolmuyor → `ReadBuyerSideAsync` sonsuza kadar `Inconclusive` → `SettlementVerifiedAt` (tek yazar: `SettlementVerificationJob.cs:235`) asla damgalanmıyor → payout kuyruğa girmiyor, sweep çalışmıyor, COMPLETED guard'ı geçilemiyor. **Admin'in kolu yok:** `AdminResolveRefund` yalnız `AdminDisputeService.cs:266`'dan, ESCALATED bir dispute üzerinden ateşleniyor ve dispute'u yalnız **alıcı** açabiliyor (`DisputeService.cs:105-108`). Sonuç: dürüst satıcı hiçbir zaman ödenemiyor, alıcının parası süresiz donuyor. **K2 launch kapısı bunu hafifletmiyor** — kapı yalnız geri alma dalını etkiler; bu dal launch'ta varsayılan yoldur. AC4'ün birinci dalı ("item var → ödeme akar") ve 02 §4.5.1'in "işlem tamamlanır" satırı bu popülasyon için erişilemez. DEPLOY_RUNBOOK §I.1/§I.4'ün adını verdiği iki çare de (`admin_resolve_refund` / "kontrolün kendi kendine sonuçlanması") bu sınıfta mevcut değil | `SettlementVerificationService.cs:197-206` |
| **B2** | S3 Eksik | **K4'ün amacı karşılanmamış: `DELIVERY_REVERSED` hiçbir katalogda ve hiçbir admin yüzeyinde yok.** Enum değeri eklendi (`FraudFlagType.cs:20`, EnumTests 5→6) ama (a) **06 §2.11 kanonik enum tablosu beş değerde kaldı** — T82 `SANCTIONS_MATCH`'i eklerken §2.11 satırını yazmıştı, emsal net; üstelik T129'un **kendi eklediği** 02 §4.5.1 satırı (`02:168`) 06'da tanımlı olmayan bir ada normatif atıf yapıyor; (b) frontend'de `TYPE_VALUES` hâlâ 5 elemanlı (`admin/flags/page.tsx:20-26`) → admin kuyruğunda yeni tipe göre **filtre yok** ve `parseEnum` beyaz listesi elle URL denemesini de düşürüyor; (c) üç i18n haritası (`adminFlags.type`, `adminDashboard.flagType`, `adminUserDetail.flags.type`) dört dilde de eksik → ekrana ham anahtar basar (`i18n/request.ts` fallback tanımlamıyor). (c)'nin üçüncüsü §14.2'nin "tekrarı say" kuralının fiilen okunduğu **hesap bazlı flag geçmişi** tablosudur. Bu, T129'un 02 §4.5.1'e kendi yazdığı cümleyle çelişiyor: *"vakanın admin kuyruğunda ayırt edilebilir olması gerekir"* | `Docs/06_DATA_MODEL.md:219` · `frontend/src/app/[locale]/admin/flags/page.tsx:19` |
| **B3** | S3 Eksik | **AC8'in formülü var, tetikleyicisi yok.** `SuccessfulTransactionRate` denormalized bir `User` kolonu (`User.cs:44`) ve yalnız `ITransactionReputationRefresher.RefreshAsync` çağrıldığında yazılıyor. Formülü etkileyen her terminal geçiş bunu çağırıyor — `PayoutCompletedConsumer.cs:125`, `TimeoutExecutor.cs:99`, `DeadlineScannerJob.cs:182`, `TransactionCancellationService`. T129'un eklediği yeni terminal geçiş `DeliveryReversed → REFUNDED` **çağırmıyor**: `SettlementVerificationJob` içinde "reputation" geçen tek satır yok, ctor bağımlılıklarında refresher yok. Sonuç: trade'ini geri alan satıcının skoru, o satıcının **başka** bir işlemi terminal olana kadar (hiç olmayabilir) güncellenmiyor — planın AC8 gerekçesinin tam tersi. Doküman tarafı da yarım: 06 §3.1 (formül) güncellendi ama §8.2 tetikleyici satırı (`06:1619`, "İşlem COMPLETED veya CANCELLED olduğunda") ve §3.1 girişi (`06:441`) güncellenmedi → **§3.1 ile §8.2 artık çelişiyor ve kod §8.2'yi izliyor** | `SettlementVerificationJob.cs:248-362` |

### Bloke etmeyen bulgular

| # | Seviye | Açıklama | Dosya |
|---|---|---|---|
| N1 | S1 | **K1'in ayırt edici sinyali "satıcıya döndü" değil "satıcıda var" olarak uygulanmış.** 02 §4.5.1 "satıcıya **dönmüşse**" / "**yeniden belirmesi**" diyor; kod saf bulunma testi yapıyor (`sellerRead.Item is not null`) ve asset'in daha önce satıcıdan ayrıldığını hiçbir yerde doğrulamıyor — `DeliveryEvidence`'ın `SELLER_ASSET_GONE` biti Settlement klasöründe hiç okunmuyor. Alıcı onayıyla kapanan teslimatta platform hiç envanter okumadığı için satıcının orijinal `ItemAssetId`'si envanterinde dururken ITEM_DELIVERED'a girilebiliyor (satıcı aynı sınıftan başka kopyayı gönderdi — 02 §9.2 sayım kuralı bunu geçerli teslimat sayar) → dürüst satıcı için yanlış-pozitif `ReversalSignature`. K2 kapalıyken sonuç `AmbiguousDeparture` ile aynı (admin eskalasyonu, para parkta), o yüzden bloke etmiyor — **ama kapı açılmadan önce kapatılmalı** | `SettlementVerificationService.cs:111-142` |
| N2 | S1 | **Eskalasyon yapışkan değil.** Kapı kapalıyken kaydedilen geri alma imzası hiçbir kolonda saklanmıyor (`SettlementEscalatedAt` dışında iz yok, onu da hiçbir para kapısı okumuyor); sonraki turda alıcı tarafı "item duruyor" derse `ClearForPayoutAsync` koşulsuz `SettlementVerifiedAt` damgalıyor ve para, **admin'e hiç haber verilmeden** çıkıyor — admin kutusunda açık bir `ADMIN_ESCALATION` dururken. §I.3 triyaj SQL'i `SettlementVerifiedAt`'i seçmediği için bu görülmüyor. Öneri: verdict'i kalıcı bir kolona yaz ve `ClearForPayout`'u ona bağla, ya da otomatik çözülmede "eskalasyon kendiliğinden kapandı" olayı yayımla | `SettlementVerificationJob.cs:229-242` |
| N3 | S1 | **DEPLOY_RUNBOOK §I.1'in "`admin_resolve_refund` ile aynı sonucu üretir" iddiası yanlış** — ve farkı aynı commit tanımlıyor: insan yolu `DeliveryReversedAt`'i yazmaz, dolayısıyla ne itibar paydasına girer (06 §3.1'in yeni kuralı) ne de `DELIVERY_REVERSED` flag'i yazılır. 02 §4.5.1'in üç sonucundan ikisi insan yolunda uygulanamıyor. Aynı iddia `SystemSettingSeed.cs:179` açıklamasına (admin UI'da görünür) ve rapor §Known Limitations'a da sızmış | `Docs/DEPLOY_RUNBOOK.md:348` |
| N4 | S1 | 06 §3.17'deki `settlement.reversal_auto_refund_enabled` satırı **DEPLOY_RUNBOOK §H**'ye yönlendiriyor; T129 prosedürü **§I**'de | `Docs/06_DATA_MODEL.md:1129` |
| N5 | S1 | **SystemSetting anahtar sayısı üç dokümanda üç farklı:** 06 §3.17 başlığı "58" (main'de de bayattı — gerçek 60'tı), 07 §9.8 "63" (doğru), DEPLOY_RUNBOOK §C "60 satır" / "altı satır" (tablo artık dokuz satır). Gerçek: **63** (seed = katalog = 63, script ile sayıldı) | `Docs/06_DATA_MODEL.md:1064` · `Docs/DEPLOY_RUNBOOK.md:90` |
| N6 | S1 | 05'in sweep tetikleyicisi satırı hâlâ yalnız "`ITEM_DELIVERED` state gate'i" diyor; T129 kapıyı `SettlementVerifiedAt NOT NULL ∧ DeliveryReversedAt NULL` ile genişletti. Satırın kendi gerekçesi ("depozit iade çekilebilecek yerde kalmalı") tam da yeni kapıyı işaret ediyor ama eski adı taşıyor | `Docs/05_TECHNICAL_ARCHITECTURE.md:313` |
| N7 | S3 | 07 §9.3 `flagDetail` türe göre tablosu `DELIVERY_REVERSED` satırını içermiyor, oysa `SettlementVerificationJob.cs:318-332` on alanlı ayrı bir payload yazıyor ve bu AD3 üzerinden admin ekranına düşüyor. *(Kısmen pre-existing: tablo `SANCTIONS_MATCH`'i de içermiyor)* | `Docs/07_API_DESIGN.md:1873-1878` |

### Doğrulanan noktalar (kanıtla)

- **AC1 ✓** — seed satırı ile migration `InsertData` satırı Id/Category/DataType/Value/IsConfigured/Description alanlarında birebir; katalog kapsaması 1:1 (63 = 63); validator floor'u doğru key'e bağlı ve akışta kendisinden önce hiçbir dal onu yakalamıyor (generic pozitif-sayı kuralı gerçekten override ediliyor); provider'ın üç fallback'i de güvenli yönde; i18n 4 dil parity tam.
- **AC2 ✓** — `TransactionTrigger.DeliverItem`'ı fire eden üretim yolu tam olarak iki (`DeliveryConfirmationService.cs:197`, `DeliveryTimeoutRound.cs:219`) ve her ikisi de tetikleyiciden önce `SettlementWindowStamper.Stamp` çağırıyor; `HasDeliveryEntryInvariant` kolonu geçişin ön koşulu yaptığı için atlayan çağıran ITEM_DELIVERED'a hiç giremez. `PayoutEligibleAt`'in src'de tek yazarı stamper. `DeliveryTimeoutRound`'un rollback'i üç alanı da (`DeliveryVerifiedAt`, `DeliveredBuyerAssetId`, `PayoutEligibleAt`) geri alıyor; `DeliveryConfirmationService` SaveChanges'ini sahiplendiği için rollback'e ihtiyaç duymuyor — ayrım doğru kurulmuş.
- **AC3 ✓** — T126'nın `PayoutEligibleAt` filtresi kaldırılmamış, üstüne iki koşul hem sorguya hem döngü-içi yeniden doğrulamaya eklenmiş.
- **AC5 ✓** — `HasSettlementClearance` yalnız `Complete` geçişinde; COMPLETED'a giden başka yol yok (`Complete`'in tek fire noktası `PayoutCompletedConsumer`, o da payout'un arkasında). Admin dispute SELLER_FAVOR ayrı bir geçiş yapmıyor, yalnız dispute hold'unu kaldırıyor → payout kapısına takılıyor.
- **AC6 ✓** — `DisputeEligibility` ITEM_DELIVERED'da DELIVERY + WRONG_ITEM açılmasına izin veriyor; `HasActiveDispute` üç sorguda da (settlement + payout + sweep) filtreleniyor; dispute kapanınca `HasActiveDispute = otherActiveExist` ile akış devam ediyor.
- **AC7 ✓** — sorgu + döngü-içi yeniden doğrulama, iki yeni test causality assertion'ıyla.
- **Migration ✓** — model snapshot iki yeni kolonu içeriyor, timestamp sıralaması T127'den sonra, `Down()` tam tersini yapıyor, CI "6. Migration dry-run" yeşil.
- **Bildirim altyapısı ✓** — iki consumer MediatR assembly taramasıyla otomatik kayıtlı (`OutboxModule.cs:84-89` `Skinora.Notifications` assembly'sini tarıyor); outbox tip çözümü `Type.GetType` tabanlı, kayıt gerektirmiyor; `ADMIN_ESCALATION` parametre şekli kardeş `DisputeEscalatedAdminNotificationConsumer` ile birebir; yeni `NotificationType` eklenmediği için 06 §2.13 / 07 §8.1 26-tip kataloğu bozulmuyor.
- **Test kalitesi ✓** — mevcut payout/sweep testleri **gevşetilmemiş**; yeni alanlar seed helper'a eklenmiş (gerekli) ve dört yeni kapı testi causality assertion'ı içeriyor. K2'nin iki dalı (kapı açık → REFUNDED+flag / kapı kapalı → yalnız eskalasyon, para hareketsiz), K3'ün iki dalı ve eskalasyon idempotency'si testlerle sabitlenmiş.
- **Güvenlik ✓** — secret sızıntısı yok, yeni endpoint yok, yeni dış bağımlılık yok; SystemSetting girdileri validator'dan geçiyor.

### Yapım raporu karşılaştırması

**Uyum: 3 uyuşmazlık.** Rapor sekiz kabul kriterini de ✓ işaretlemiş; validator AC4 ve AC8'i **~ Kısmi**'ye çekti (B1, B3) ve K4'ün yüzey bacağını eksik buldu (B2).

- Rapor §Known Limitations, N1'i (asset ID davranışı ölçülmedi) ve N2'nin zeminini (sayım rotası zayıf) **doğru öngörmüş** — bu iki sınırlama bilinçli ve belgeli. Validator'ın eklediği, bunların birleşiminin *dürüst satıcı aleyhine yanlış-pozitif* üretebildiği (N1) ve eskalasyonun geri alınabilir olduğu (N2).
- Rapor §Known Limitations son maddesi ("karar `admin_resolve_refund` ile verilir") ile DEPLOY_RUNBOOK §I.1 aynı yanlış iddiayı taşıyor (N3) ve bu iddia B1'in görülmesini engellemiş: o kol bu vakada **erişilebilir değil**.
- B3 raporda hiç geçmiyor; AC8 "3 artefakt teslim" olarak kapatılmış, tetikleyici sorusu sorulmamış.

### Düzeltme turu için öneri (sahiplik önerisi)

1. **B1** — bu popülasyon için bir çıkış yolu gerekli. Seçenekler: (a) `SettlementVerificationJob`'a "baseline yok" vakası için ayrı bir verdict/gerekçe (`Unreadable` yanıltıcı) + admin'in mutabakatı **satıcı lehine** kapatabileceği bir aksiyon; (b) alıcı-onaylı teslimatta `DeliveredBuyerAssetId`'yi doldurmak için ITEM_DELIVERED anında bir envanter okuması; (c) baseline'sız işlemleri mutabakat kontrolünden muaf tutup alıcı onayını yeterli saymak. **Karar proje sahibinin** — üçü de 02 §4.5.1'in koruma seviyesini farklı yerde dengeliyor.
2. **B3** — `SettlementVerificationJob`'a `ITransactionReputationRefresher` enjekte et, `ApplyReversalAsync`'te SaveChanges'ten sonra `RefreshAsync(sellerId, buyerId, evaluateCooldown: false, ct)`; 06 §8.2 ve §3.1 giriş cümlesini geri alma dalıyla genişlet.
3. **B2** — 06 §2.11'e satır, FE `TYPE_VALUES` + üç i18n haritası × 4 dil.
4. **N1** kapı açılmadan önce; **N2–N7** aynı turda ucuz.

## Altyapı Değişiklikleri

- **Migration:** `20260816140704_T129_SettlementCheckColumns` — `Transactions`'a 2 nullable `datetime2` kolon + 3 `SystemSettings` seed satırı. Down temiz (DeleteData + DropColumn).
- **Config:** 3 yeni SystemSetting (hepsi `Default(...)` → `IsConfigured = true`, env ile override edilemez).
- **Hangfire:** yeni recurring job `settlement-verification` (`*/5 * * * *`), `OutgoingTransferJobsRegistrar`'da kayıtlı.
- **Docker:** değişiklik yok.

## Güvenlik Kontrolü

- Secret sızıntısı: yok (yeni secret/credential yok).
- Auth/authorization: yeni endpoint yok; job SYSTEM aktörü ile çalışır, admin yüzeyi mevcut `ADMIN_ESCALATION` bildirimidir.
- Input validation: yeni kullanıcı girdisi yok; SystemSetting değerleri validator'dan geçer (`payout_settlement_days ≥ 7`).
- Yeni dış bağımlılık: yok.

## Known Limitations / Follow-up

- **Geri alma sonrası asset ID davranışı ölçülmedi.** Servis "satıcının orijinal `ItemAssetId`'si geri döndü" imzasını arar; Steam rollback'te ID'yi koruyor mu bilinmiyor (T122-B7 kapanmadı). Korumuyorsa imza oluşmaz ve vaka **ayırt edilemeyen ayrılma** olarak admin'e düşer — güvenli yön. DEPLOY_RUNBOOK §I.3 adım 2 bunu ilk gerçek vakada kapatmayı öngörür.
- **Otomatik iade dalı launch'ta kapalı** (K2). Kapı açılana kadar geri alma vakaları admin kararıyla `admin_resolve_refund` üzerinden kapatılır.
- **Alıcı sayım rotası zayıf kalır.** `DeliveredBuyerAssetId` NULL olan (alıcı onayıyla kapanmış, envanteri okunmamış) teslimatlarda kontrol sınıf sayımına düşer; alıcı aynı skinden başka kopya edinirse geri alma maskelenebilir. Tam test için asset ID gerekir, o da yalnız envanter kanıtı üretilebilen teslimatlarda vardır (06 §8.4 best-effort).
- **Bildirim tipi yeniden kullanıldı.** Geri alma bildirimi `TRANSACTION_CANCELLED` şablonuyla (taraf-özel `Reason` metniyle) gider; yeni `NotificationType` + 4 dil resx maliyeti yerine mevcut zarf tercih edildi. Admin tarafı `ADMIN_ESCALATION` + fraud flag'in kendi `ADMIN_FLAG_ALERT`'i ile iki kanaldan haberdar olur.
- **Eskale satırlar için admin aksiyon yüzeyi yeni değil.** İşlem admin işlem listesinde/detayında görünür ve karar `admin_resolve_refund` ile verilir; T129 ayrı bir "mutabakat incelemesi" ekranı eklemez. Eskalasyonun kendisi bildirim + `SettlementEscalatedAt` kolonu üzerinden izlenir (DEPLOY_RUNBOOK §I.3 sorgusu).

## Notlar

- **Working tree:** temiz (Adım -1 ✓).
- **Adım 0 — main CI son 3 run:** `31944080697` success · `31944080720` success · `31909528316` success.
- **Dış varsayımlar (Adım 4):**
  - *Steam trade geri alma penceresi 7 gün ve item bu süre boyunca kısıtlı* — T122 runbook §6.1 ile doğrulandı (`market_tradable_restriction: 7`, sınıf politikası). **Sonucu scope'u değiştirdi:** 8 > 7 farkı K1'in gerekçesi oldu.
  - *Geri alma item'ı satıcıya döndürür* — 02 §4.5.1'in kendi tanımı; asset ID'nin korunup korunmadığı ölçülmedi (yukarıdaki known limitation).
  - *Yeni paket/servis gerekmiyor* — evet, tüm bağımlılıklar mevcut (`ISteamInventoryReader` T121, refund boru hattı WP2, fraud staging T54).
- **Enum değişikliği:** `FraudFlagType.DELIVERY_REVERSED` eklendi → memory kuralı gereği push öncesi tam `Category=Unit` suite koşuldu.

## Commit & PR

- Branch: `task/T129-settlement-window-reversal-guard`
- Commit: `2813daf` (yapım) + `e24b599` (rapor referansları)
- PR: [#240](https://github.com/turkerurganci/Skinora/pull/240)
- CI: **✓ PASS** — run [`31959216012`](https://github.com/turkerurganci/Skinora/actions/runs/31959216012) (`e24b599`) ve **dal HEAD `36d9549`** için run [`31959858421`](https://github.com/turkerurganci/Skinora/actions/runs/31959858421); ikisinde de **CI Gate `success`**, bloke edici 10 job yeşil
- Dal izolasyonu: `git log main..HEAD` → yalnız `T129` ✓

**CI kırılımı (bloke edici 10 job yeşil):** Detect changed paths · 1. Lint · 2. Build · 3. Unit test · 3b. JS test (vitest) · 4. Integration test · 5. Contract test · 6. Migration dry-run · 7. Docker build (backend + frontend) · CI Gate. `0. Guard (direct push)` beklendiği gibi `skipped` (PR event).

**8 advisory E2E leg kırmızı — T129 kaynaklı değil, bu run'ın logundan doğrulandı.** İmza 8/8 leg'de `Invalid object name 'PlatformSteamBots'` (leg başına tam 1 iz); T129 yüzeylerinden (`settlement` / `PayoutEligibleAt` / `DELIVERY_REVERSED`) log genelinde **0 iz**. T117'den beri pre-existing, sahiplik T137 → T138 (aynı bulgu T128 raporunda da kayıtlı).
