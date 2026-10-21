# T160c — İade kesinti kalıntısının toplanması (⚠ PARA)

**Faz:** F9 | **Durum:** ⏳ Devam ediyor — düzeltme turu, yeniden doğrulama bekliyor | **Tarih:** 2026-10-21

> **Düzeltme turu (doğrulama ✗ FAIL, HEAD `c5dab67c`):** bulgular F1–F7 ve ne yapıldığı raporun sonundaki **"Düzeltme turu"** bölümünde. Kısaca: AD38'deki gider artık platformun gerçek zincir harcaması (F1, bloke edici); "zararına toplama yok" sahip kararı olarak kaydedildi (F2); sweep `CONFIRMED` şartı ve USDC imza bayrağı testle korunuyor (F3, F7); `origin/main` dala merge edildi (F4). Aşağıdaki ilk yapım bölümleri yapım anını anlatır; değişen yerler düzeltme turu bölümünde.

---

## Özet

T159a'dan beri her iade `taban − gas` gönderiyor ve kesilen `gas`'ı `GasFee`'de saklıyor; zincir ücretini platform ödüyor (TRON: sıcak cüzdanın TRX'i, Ethereum: yakıt cüzdanının ETH'si). İade depozitten çıktığında kesilen tutar depozitte token olarak kalıyordu ve hiçbir şey onu toplamıyordu (backlog `RefundGasDeductionDustStaysOnDeposit`, F8 gate maddesi 20; sahip kararı 2026-10-21: "toplansın").

**Şimdi kalıntı sıcak cüzdana iki yoldan gider, iki ağda:**

1. **Satışın sweep'i taşır (ek maliyetsiz).** `SweepQueueJob` işlemin **onaylanmış** ve **depozitten çıkmış** iadelerinin kesintisini (işlemin token'ında: fazla ödeme, çoklu ödeme, eksik ödeme iadeleri) `TotalAmount`'a ekler; o kısım yeni kolon `BlockchainTransaction.RefundFeeRecovered`'a yazılır. Transfer zaten yapıldığı için toplamanın maliyeti sıfırdır.
2. **Kalanı ayrı bir toplama transferi taşır.** Yeni tür `REFUND_FEE_SWEEP` ve yeni iş `RefundFeeSweepQueueJob` (10 dk): sweep'e hiç ulaşmayan işlemler (`CANCELLED_*`, `REFUNDED` — sweep öncesi `BUYER_REFUND`, eksik ödeme + zaman aşımı, geç ödemeler), yanlış token iadesinin kesintisi (USDC, kesildiği token'da), sweep kuyruğa girdikten sonra kesinleşen iade. Depozit → aynı ağın sıcak cüzdanı, servislerin mevcut sweep ucundan (sabit hedef). **Asla zararına değil:** toplamanın canlı ücret tahmini toplanacak tutardan küçükse kuyruğa girer; değilse kalıntı depozitte kalır, defterde "depozitte" görünür ve 24 saat sonra yeniden fiyatlanır.

**Muhasebe:** kesinti defteri ledger satırlarından okunur — kesilen (`GasFee`, gelir) − taşınan (`RefundFeeRecovered`) = depozitte kalan; iadelerin ve ayrı toplamaların zincir ücreti (`RealizedFeeSun` / `RealizedFeeWei`, gider). Admin cüzdan ekranı (AD38) ağ başına bu dengeyi gösterir: alıcılardan kesilen / sıcak cüzdanda / yolda / hâlâ depozitlerde + iadelerin ve toplamaların zincir ücreti (yerel varlıkta).

Ethereum servisi sweep'te USDC'ye izin verir (yalnız sabit sıcak cüzdana; ücret artırmada da akış korunur). TRON servisi zaten iki token'ı sweep ediyordu — değişmedi.

---

## Yapılan İşler

### 1. Defter kuralı (`RefundFeeLedger.cs`, yeni)

- **Kesilen (depozitte kalan):** iade türü (`BUYER_REFUND`, `EXCESS_REFUND`, `WRONG_TOKEN_REFUND`, `INCORRECT_AMOUNT_REFUND`, `LATE_PAYMENT_REFUND`) + `CONFIRMED` + `GasFee > 0` + `FromAddress` = depozit adresi. Yolda (`PENDING`/`DETECTED`) ya da `FAILED` iade sayılmaz — tabanının tamamı depozitte; sıcak cüzdandan çıkan iade (Ethereum sweep sonrası `BUYER_REFUND`, T158 (f)) sayılmaz — kesintisi zaten sıcak cüzdanda.
- **Token:** iadenin zincirde taşıdığı token (`OutboundTransferExpectation.ChainToken` — yanlış token iadesinde `ActualTokenAddress`'in token'ı, USDC).
- **Taşınan:** `SWEEP` / `REFUND_FEE_SWEEP` satırının `RefundFeeRecovered`'ı, `FAILED` olmayan (yoldaki de sayılır — iki kez kuyruğa girmesin).
- **Kalan = kesilen − taşınan**, token başına. Tutarlar yalnız ledger'dan gelir, zincir bakiyesinden değil: eşik altında kalmış iade, admin kararı bekleyen ödeme ya da spam token aynı depozitte olsa da hiçbiri toplamaya girmez.

### 2. Sweep'e katlama (`SweepQueueJob.cs`)

`Amount = TotalAmount + kalan(işlemin token'ı)`, `RefundFeeRecovered = kalan` (yoksa NULL). Yanlış token iadesinin kesintisi bu sweep'e girmez (başka token). Kural kuyruklama anında okunur; o anda kesinleşmemiş iadenin kesintisi sonra 2. yoldan toplanır.

### 3. Ayrı toplama (`RefundFeeSweepQueueJob.cs`, yeni; `OutgoingTransferJobsRegistrar`, `TransactionsModule`)

Bir depozit şu koşulların **hepsinde** ele alınır (sorgu + döngü içinde yeniden doğrulama, 09 §13.3):
- ağın sıcak cüzdanı yapılandırılmış;
- işlem askıda değil, aktif uyuşmazlığı yok;
- satışın sweep'i `CONFIRMED` **ya da** sweep yok ve işlem sweep'siz bitmiş (`CANCELLED_TIMEOUT/SELLER/BUYER/ADMIN`, `REFUNDED`) — canlı satış (sweep'i ileride gelecek) beklenir, çünkü sweep kesintiyi bedavaya taşır; sweep `PENDING`/`DETECTED`/`FAILED` ise dokunulmaz;
- depozitten çıkan hiçbir şey yolda değil (iade, sweep, önceki toplama `PENDING`/`DETECTED`) — iki transfer aynı bakiye için yarışmaz;
- işlemin hiçbir toplaması `FAILED` değil (admin bakar; reddedilmiş transfer yeni kimlikle yeniden açılmaz);
- defter hâlâ borçlu (toplam kesilen > toplam taşınan) ve depozitin `RefundFeeSweepNextCheckAt`'i geçmiş (ya da NULL).

Sonra: token başına kalan hesaplanır, **büyüğü** seçilir (tek seferde tek token), toplamanın maliyeti canlı sorulur (`IGasFeeEstimator.EstimateFeeUsdtAsync(depozit → sıcak cüzdan, tutar, token)` — mevcut arayüz, T160b'nin dosyalarına dokunulmadı). Tahmin yok / negatif / `max_charged_gas_fee_usdt` üstü → hiçbir şey kuyruğa girmez, `şimdi + 30 dk`. **Tutar ≤ maliyet** → kalıntı depozitte kalır, `şimdi + 24 sa`, bilgi log'u. **Tutar > maliyet** → `REFUND_FEE_SWEEP` (`PaymentAddressId` = depozit, `FromAddress` = depozit, `ToAddress` = ağın sıcak cüzdanı, `Amount` = `RefundFeeRecovered` = kalan, `Token` = taşınan token, `GasFee` NULL), `RefundFeeSweepNextCheckAt` NULL.

İdempotens: `[DisableConcurrentExecution]` + defter yoldaki toplamayı "taşınan" sayar + **`UQ_BlockchainTransactions_RefundFeeSweep_InFlight`** = `(TransactionId) WHERE Type='REFUND_FEE_SWEEP' AND Status IN ('PENDING','DETECTED')` (yarışta ikinci ekleme reddedilir, yakalanır, no-op).

### 4. Gönderim ve onay

- `OutgoingTransferDispatchJob`: yeni tür giden kuyrukta; kaynak çözümü sweep gibi (depozit indeksi kardeş ödeme satırından; `RefundSourceResolver`'a girmez). **Ethereum:** `feeCapUsd = min(fee_cap_usd_per_transfer, Amount)` — imzacı toplamayı topladığından pahalıya imzalamaz, bekler. TRON'da tavan yok (değişmedi).
- `HttpBlockchainTransferClient`: `SWEEP or REFUND_FEE_SWEEP` → `api/transfer/sweep` (aynı gövde).
- `OutgoingTransferConfirmationJob`: yeni tür onaylanır (gerçekleşen ücret `RealizedFeeSun` / `RealizedFeeWei` genel yoldan yazılır).
- `ReconciliationService`: toplama depozit çıkışı ve sıcak cüzdan girişi (`HotWalletInflowTypes`). `AdminWalletOverviewService` / `OutboundRowClosureService` giden tür listeleri.

### 5. Ethereum servisi (`sidecar-ethereum`)

- `TransferService.sweep`: USDT ya da USDC (önce yalnız USDT); kontrat ve token istekteki token'dan. Hedef yine `assertSweepDestination` (sabit sıcak cüzdan).
- `sign()` imzacı niyetine `wrongTokenRefund: refundType === 'WRONG_TOKEN_REFUND' || purpose === 'SWEEP'` — `SignerPolicy` USDC'yi yalnız bu iki akışta kabul eder. Ücret artırma (bump) yeniden imzada kaydın `purpose`'unu taşır (tip sistemiyle zorunlu).
- Başka hiçbir depozit akışında USDC yok (test: `EXCESS_REFUND` USDC → `TOKEN_NOT_ALLOWED`).

### 6. Veri modeli ve migration (`T160c_RefundFeeSweep`)

- `BlockchainTransactions.RefundFeeRecovered` decimal(18,6) NULL; `PaymentAddresses.RefundFeeSweepNextCheckAt` datetime2 NULL.
- `CK_BlockchainTransactions_Type_RefundFeeSweep`: `Type <> 'REFUND_FEE_SWEEP' OR (PaymentAddressId IS NOT NULL AND ActualTokenAddress IS NULL AND RefundFeeRecovered IS NOT NULL AND RefundFeeRecovered = Amount)`.
- `CK_BlockchainTransactions_RefundFeeRecovered`: `RefundFeeRecovered IS NULL OR (Type IN ('SWEEP','REFUND_FEE_SWEEP') AND RefundFeeRecovered > 0)` (`<= Amount` bilerek yok: SQLite ondalığı metin karşılaştırır, testlerde yanlış ret üretirdi; kod kuralı sağlıyor).
- `UQ_BlockchainTransactions_RefundFeeSweep_InFlight` (yukarıda). Enum string olarak saklandığı için tür için şema değişikliği yok.

### 7. Muhasebe görünümü (AD38)

`NetworkWalletsDto.RefundFees` (`RefundFeeBooksDto`): `withheld` (onaylanmış iadelerin `GasFee` toplamı), `collected` (sıcak cüzdan iadelerinin kesintisi + onaylanmış sweep/toplamaların taşıdığı), `inTransit`, `onDeposits` (= kesilen − sıcak cüzdanda − yolda), `refundChainCost` ve `collectionChainCost` (yerel varlık; satışın kendi sweep'i sayılmaz — zaten yapılan transfer). Frontend `NetworkWalletsCard` "İade ağ ücretleri" bölümü (4 dil), `outboundType.REFUND_FEE_SWEEP` etiketi.

### 8. Belgeler

02 §4.7 (v4.15), 05 §3.3 "İade gas fee" + "Sweep mekanizması" (v3.28), 06 §2.5 / §3.7 / §3.8 (v6.36), 07 §9.33 AD38 (v4.17), 08 §3.4 notu + §3.7.6 (v3.42), `plan/11_F9.md` "T160c kararları" (v1.20), `DOC_CHANGELOG.md`, `DEFERRED_BACKLOG.md` (`RefundGasDeductionDustStaysOnDeposit` ✅).

---

## Kenar durumları (testle sabitlendi)

| Durum | Davranış | Test |
|---|---|---|
| Çift sweep / çift toplama | Defter yoldakini "taşınan" sayar; ikinci tur yeni satır yazmaz; DB filtreli tekil indeks ikinci yoldaki toplamayı reddeder | `Running_Again_Never_Queues_A_Second_Collection…`, `The_Database_Refuses_A_Second_In_Flight_Collection…`, `T160c_Fees_A_Collection_Already_Carries_Are_Not_Moved_Twice` |
| Eksik/fazla sweep | Yalnız onaylanmış, depozitten çıkmış iadelerin `GasFee`'i; `FAILED` toplama sayılmaz (taşımadı), yoldaki sayılır | `T160c_A_Refund_That_Is_Not_Final_…` (PENDING/DETECTED/FAILED), `…Hot_Wallet_Refund_Fee_Are_Not_Folded_In`, `A_Fee_The_Hot_Wallet_Kept_…` |
| İade yoldayken toplama | Bekler (depozitten çıkan PENDING/DETECTED satır varken hiçbir şey kuyruğa girmez, fiyat bile sorulmaz) | `Nothing_Is_Queued_While_Another_Transfer_Leaves_The_Deposit` (4 vaka) |
| Yakıt/enerji maliyeti kalıntıdan büyük | Kuyruğa girmez, 24 sa sonra yeniden; tam maliyete eşitte de girmez, bir kuruş üstünde girer; maliyet 0 (devredilmiş enerji) → girer | `A_Cent_Above_The_Cost_It_Goes_At_The_Cost_It_Waits`, `Not_Worth_It_Now_…`, `A_Free_Collection_…` |
| Tahmin yok / güvenilmez / istisna | Kuyruğa girmez, 30 dk sonra | `An_Untrusted_Or_Missing_Price_…` (null, 10,01 > tavan 10,0, −0,01), `An_Estimator_That_Throws_…` |
| Ethereum'da gönderim anında gas fırlarsa | İmza tavanı `min(0,75, tutar)` — toplamadan pahalıya imzalanmaz | `T160c_Ethereum_RefundFeeSweep_Is_Capped_…` (0,31 / 0,75 / 2,43) |
| Birden çok iade / ödeme olan depozit | Hepsi toplanır (tek transfer); sweep'e iki fazla iade birlikte katlanır | `Several_Refunds_On_One_Deposit_…`, `T160c_Confirmed_Deposit_Refund_Fees_…` |
| Sweep sonrası geç gelen ödeme | Toplama onaylandıktan sonra gelen geç ödemenin iadesi yalnız kendi kesintisini ekler, ikinci toplama yalnız onu taşır | `Running_Again_…_A_Later_Refund_Adds_Only_Its_Own_Fee` |
| Sweep kuyruğa girdikten sonra kesinleşen iade | Sweep `CONFIRMED` olunca yalnız katlanmayan kısım toplanır | `A_Refund_Final_Only_After_The_Sweep_…` |
| USDT + USDC aynı depozitte | Büyüğü önce, onaylanınca öteki | `Usdt_And_Usdc_Fees_Go_One_Token_At_A_Time_…` |
| Yanlış token kesintisi | USDC olarak toplanır (iki ağ); sweep'e katlanmaz | `A_Wrong_Token_Refunds_Fee_Is_Collected_In_The_Token_…` (TRON/Ethereum), `T160c_A_Wrong_Token_…_Not_Folded_In` |
| Canlı satış (sweep gelecek) | Beklenir | `A_Live_Sale_Waits_…` (SELLER_CONFIRMED / PAYMENT_RECEIVED / ITEM_DELIVERED) |
| Sweep yolda / `FAILED` | Dokunulmaz | `A_Sweep_That_Is_Not_Confirmed_…` |
| `FAILED` toplama | Otomatik yeniden açılmaz | `A_Failed_Collection_Is_Not_Retried_…` |
| Askı / uyuşmazlık / sıcak cüzdansız ağ | Atlanır | `Held_Or_Disputed_…` |
| CK: toplamanın `RefundFeeRecovered`'ı tutarına eşit değil | DB reddeder | `The_Database_Refuses_A_Collection_Whose_Recovered_Part_…` |

---

## Ücret tavanı ve küçük tutar eşikleriyle etkileşim

- **En düşük iade eşiği** (`min_refund_threshold_ratio`, 2,0): değişmedi. Eşik altında iade satırı yazılmaz → hiçbir şey kesilmez → toplanacak bir şey yoktur; para depozitte admin kararını bekler ve toplama ona dokunmaz (ledger'da satır yok). Toplama kuralı bu çarpanı **kullanmaz** (bkz. ⚠ (iii)).
- **`max_charged_gas_fee_usdt`** (TRON 10,0 / Ethereum ayarı): toplamanın tahmini bu tavanı aşarsa güvenilmez sayılır, hiçbir şey kuyruğa girmez (ücret çözücüsünün "ret, kırpma yok" kuralının eşi).
- **Ethereum ücret tavanı** (`fee_cap_usd_per_transfer`, 0,50): satışın sweep'i değişmedi (katlanan kesinti gas'ı değiştirmez, aynı transfer). Ayrı toplama `min(tavan, tutar)` ile imzalanır; tavan üstünde diğer gönderimler gibi bekler (`FEE_ABOVE_CAP`, deneme hakkı harcanmaz). Kuyruklama kapısı zaten "maliyet < tutar" olduğu için bu bekleme yalnız kuyruklamayla gönderim arasındaki fiyat sıçramasında oluşur.
- **TRON:** imzacıda tavan yok; kapı yalnız kuyruklama anında. TRON toplaması sweep ucunun yakma yolunu kullanır (`ENERGY_STAKING_ENABLED=false`).
- **Tek gönderim / günlük tavanlar** sweep'e uygulanmaz (sabit hedef) — toplama da sweep ucundan gittiği için aynı.

---

## Etkilenen Modüller / Dosyalar

**Backend**
- `backend/src/Skinora.Shared/Enums/BlockchainTransactionType.cs` — `REFUND_FEE_SWEEP`
- `backend/src/Modules/Skinora.Transactions/Domain/Entities/BlockchainTransaction.cs`, `PaymentAddress.cs` — yeni alanlar
- `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/BlockchainTransactionConfiguration.cs`, `PaymentAddressConfiguration.cs` — kolonlar, CK'ler, filtreli tekil indeks
- `backend/src/Skinora.Shared/Persistence/Migrations/20261021…_T160c_RefundFeeSweep.cs` (+ Designer, ModelSnapshot)
- `backend/src/Modules/Skinora.Transactions/Application/Transfers/RefundFeeLedger.cs` (yeni), `RefundFeeSweepQueueJob.cs` (yeni), `SweepQueueJob.cs`, `OutgoingTransferDispatchJob.cs`, `OutgoingTransferConfirmationJob.cs`, `HttpBlockchainTransferClient.cs`, `OutgoingTransferJobsRegistrar.cs`
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — DI
- `backend/src/Skinora.API/Services/Reconciliation/ReconciliationService.cs`, `AdminWallets/AdminWalletOverviewService.cs`, `WalletClosures/OutboundRowClosureService.cs`
- Testler: `RefundFeeSweepQueueJobTests.cs` (yeni, 34 vaka), `SweepQueueJobTests.cs` (+6), `OutgoingTransferDispatchJobTests.cs` (+4), `HttpBlockchainTransferClientTests.cs` (+2), `ReconciliationServiceTests.cs` (+1), `AdminWalletOverviewTests.cs` (+1, entegrasyon), `EnumTests.cs` (10 → 11)

**Ethereum servisi:** `sidecar-ethereum/src/transfer/TransferService.ts`, `SignerPolicy.ts` (yorum), `TransferService.test.ts` (+3)

**Frontend:** `src/types/enums.ts`, `src/lib/api/admin.ts`, `src/components/admin/NetworkWalletsCard.tsx`, `walletTestUtils.tsx`, `WalletsOverview.test.tsx` (+1), `src/i18n/messages/{en,tr,es,zh}.json`

**Belgeler:** 02, 05, 06, 07, 08, `plan/11_F9.md`, `DOC_CHANGELOG.md`, `DEFERRED_BACKLOG.md`, `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`

TRON servisi (`sidecar-blockchain`) ve `sidecar-fake` değişmedi (ikisi de sweep ucunda USDT/USDC kabul ediyor). T160b'nin dosyalarına (`HttpSidecarGasFeeEstimator`, `ChargedGasFeeResolver`) dokunulmadı.

---

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Reddedilen ödeme iadesinde alıcıdan kesilen gas tutarı (USDT/USDC) depozitte kalmaz, sweep ile sıcak cüzdana taşınır (iki ağ) | ✓ — sahip kararıyla (2026-10-21, modal): zararına toplama yapılmaz; ekonomik olmayan kalıntı depozitte kalır ve AD38'de "hâlâ depozitlerde" görünür (düzeltme turu F2) | USDT: `SweepQueueJobTests` T160c (sweep `Amount = TotalAmount + Σ GasFee`, `RefundFeeRecovered`); USDT/USDC ayrı toplama: `RefundFeeSweepQueueJobTests` (5 iade türü × iptal/iade durumları, TRON ve Ethereum, USDC iki ağda); gönderim: `OutgoingTransferDispatchJobTests` T160c, `HttpBlockchainTransferClientTests` T160c (sweep ucu, USDC gövdesi); Ethereum imzacısı USDC sweep: `TransferService.test.ts` T160c (3); mutabakat: `ReconciliationServiceTests` T160c |
| 2 | Muhasebe kaydı platform geliri/gider dengesini gösterir | ✓ (düzeltme turu F1) | Gelir: `RefundFeeRecovered` kolonu (06 §3.8) + AD38 `refundFees` (kesilen / sıcak cüzdanda / yolda / depozitte). **Gider artık platformun gerçek zincir harcaması** (`PlatformNetworkCost`, USDT'si onay anı fiyatıyla `PlatformNetworkCostUsdt`), denge tek sayı: `netUsdt`; ölçülmeyen satır `costUnmeasured`. `AdminWalletOverviewTests.T160c_RefundFeeBooks_Expense_Is_What_Left_The_Platform_At_The_Confirmation_Price` (SQL Server, migration'la, üretim onay işiyle — T159b sayıları), `…_Show_Withheld_Collected_…` (ölçülmemiş satır), `WalletsOverview.test.tsx` T160c |
| 3 | DEFERRED_BACKLOG `RefundGasDeductionDustStaysOnDeposit` kapandı | ✓ | `Docs/DEFERRED_BACKLOG.md` §4 satırı ✅ + Durum satırı |

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend Unit (CI filtresi `FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract`) | ✓ 2266/2266 | Shared 572 · Platform 161 · Fraud 19 · Users 49 · Auth 92 · Steam 59 · Disputes 25 · Realtime 44 · Notifications 150 · API 117 · Transactions 978 (son kural değişikliğinden sonra yeniden koşuldu). Yerelde Docker yok: Notifications'ın Testcontainers kullanan 16 testi `INTEGRATION_TEST_SQL_SERVER` = LocalDB (SQL Server 2019) ile koşuldu |
| Backend Integration (LocalDB, SQL Server 2019) | ✓ Transactions 735/735 · Shared 16/16 · API 750/751 + 61/61 | API'deki tek kırmızı `TransactionLifecycleEndpointTests.ConfirmReady_Wrong_State_Returns_409` (1 ms, sınıfın başlatılması) — sınıf tek başına yeniden koşulunca 61/61; T160c'nin dokunmadığı uç |
| Migration | ✓ | Temiz LocalDB'de tüm zincir idempotent betikle uygulandı (çıkış 0); T158 → T160c idempotent betiği aynı DB'ye iki kez uygulandı (iki çıkış 0); `UseMigrations` entegrasyon testleri (AdminWalletOverview, InitialMigration) ✓. Not: tüm zincirin idempotent betiği **ikinci** kez çalıştırılınca T146'nın (`UserWalletAddresses`, satır 3387/3395) sonradan silinen kolona başvuran batch'i derleme hatası verir — önceden var, T160c'yle ilgisiz |
| Ethereum servisi | ✓ 871/871 | `npm run build` (tsc) ✓, `npm test` ✓, `npm run lint` ✓, prettier (`--end-of-line auto`) ✓ |
| Frontend | ✓ 392/392 | `npm run build` ✓, `tsc --noEmit` ✓, `eslint` ✓, `i18n:check` (4 dil 1588 anahtar, eşit) ✓, prettier ✓, `vitest run` ✓ |

### Bozma (mutation) — 17 / 17 yakalandı

Betik her bozmayı uygulayıp ilgili test sınıfını koştu ve dosyayı geri yükledi (`git status` sonrası temiz).

| # | Bozma | Kırmızı |
|---|---|---|
| M1 | Sweep kesintiyi katlamıyor (`recovered = 0`) | 5 |
| M2 | Defter onaylanmamış iadeyi de sayıyor | 3 |
| M3 | Defter "depozitten çıktı" kontrolünü yapmıyor | 1 |
| M4 | Defter `FAILED` taşıyıcıyı taşınmış sayıyor | 1 |
| M5 | Toplayıcı maliyete eşitte de gönderiyor (`<` yerine `<=`) | 1 |
| M6 | Toplayıcıda maliyet kapısı yok | 2 |
| M7 | Yoldaki transfer toplamayı engellemiyor | 4 |
| M8 | `FAILED` toplama yeniden açılıyor | 1 |
| M9 | Canlı satış (sweep gelecek) da toplanıyor | 3 |
| M10 | Zincir token'ı yerine satır token'ı (USDC kesintisi USDT sayılır) | 3 |
| M11 | Askı / uyuşmazlık kontrolü yok | 1 |
| M12 | Ethereum toplama tavanı `min(tavan, tutar)` değil | 1 |
| M13 | Yeni tür gönderim kuyruğunda değil | 4 |
| M14 | Yeni tür sweep yerine iade ucuna | 2 |
| M15 | Mutabakat toplamayı sıcak cüzdan girişi saymıyor | 1 |
| M16 | Ethereum imzacısı sweep'te USDC'ye izin vermiyor (`purpose === 'SWEEP'` yok) | 2 (vitest) |
| M17 | Ücret artırma yeniden imzada `purpose`'u taşımıyor | derleme hatası (TS2345 — tip sistemiyle zorunlu) |

---

## Dış Varsayımlar (Adım 4)

1. **TRON servisinin sweep ucu USDC'yi kabul ediyor** — `sidecar-blockchain/src/api/transferHandlers.ts` `TOKEN_SYMBOLS = ['USDT','USDC']`, `TransferService.sweep` → `resolveContract(request.token)`. Değişiklik gerekmedi.
2. **Ethereum servisinin sweep'i USDC'yi reddediyordu** — `TransferService.sweep` `requireToken(…, 'USDT', 'sweep')`, `SignerPolicy` USDC'yi yalnız `wrongTokenRefund` niyetinde kabul. Değiştirildi (yukarıda §5).
3. **Ethereum ücret tahmini USDC'yi ve depozit göndericiyi fiyatlıyor** — `sidecar-ethereum/src/fee/FeeEstimationService.ts` (`EthTokenSymbol = 'USDT' | 'USDC'`, `fromDeposit` yolu).
4. **SQL Server filtreli indeks `IN (…)` yüklemini kabul ediyor** — migration LocalDB (SQL Server 2019, 15.0.4382) üzerinde uygulandı, `sys.indexes`'te `UQ_BlockchainTransactions_RefundFeeSweep_InFlight` görüldü.
5. **EF Core SQLite toplayıcı sorgusundaki ondalık `Sum` / karşılaştırmayı çeviriyor** — `RefundFeeSweepQueueJobTests` (SQLite) yeşil; ondalık `ORDER BY` çevrilmiyor (testte kaçınıldı).
6. **Yerelde Docker yok** (`dockerDesktopLinuxEngine` borusu yok) — Testcontainers yerine LocalDB kullanıldı; Docker Desktop'a dokunulmadı.

---

## ⚠ Kararlar (öneriyle kaydedildi — ⚠ PARA)

1. **İki toplama yolu:** sweep'e katlama (ek maliyetsiz) + ayrı `REFUND_FEE_SWEEP` (kalanı). Alternatif (her kesintiyi ayrı toplamak) her kesinti başına bir transfer ücreti öderdi.
2. **Yalnız `CONFIRMED` ve depozitten çıkmış iadelerin kesintisi** toplanır. Kesinleşmemiş iadenin tabanının tamamı depozitte kalır (iade başarısız olursa admin elle yeniden fiyatlayabilir); sıcak cüzdan iadesinin kesintisi zaten sıcak cüzdanda.
3. **Ayrı toplama asla zararına değil:** canlı tahmin < tutar ise kuyruğa girer; değilse kalıntı depozitte kalır, 24 sa sonra yeniden fiyatlanır (tahmin yok/güvenilmez → 30 dk). Ayar eklenmedi, iade eşiği çarpanı (2,0) **kullanılmadı** — onu kullanmak toplanan kalıntının yarısını yine depozitte bırakırdı ve iade eşiğiyle toplama kuralını birbirine bağlardı.
4. **Ethereum'da toplamanın imza tavanı `min(fee_cap_usd_per_transfer, tutar)`.**
5. **Uygunluk:** sweep `CONFIRMED` ya da sweep'siz terminal; askı/uyuşmazlık yok; depozitten çıkan hiçbir şey yolda değil; önceki toplama `FAILED` ise otomatik yeniden deneme yok (admin alarmı `TransferDispatchFailedEvent` zaten gider).
6. **Tek seferde tek token** (büyüğü önce); yolda işlem başına en çok bir toplama (filtreli tekil indeks).
7. **USDC kesintisi USDC olarak toplanır;** Ethereum imzacısı sweep'te USDC'ye izin verir (hedef sabit sıcak cüzdan). Ethereum sıcak cüzdanındaki USDC AD39 ile soğuğa aktarılamaz (yalnız USDT, K2) — küçük tutar sıcak cüzdanda kalır.
8. **`BUYER_REFUND` kesintisi de toplanır** — kriter "reddedilen ödeme" diyor, ama aynı depozitte aynı para; backlog satırı da `BUYER_REFUND`'ı anıyordu.
9. **Defter görünümü:** ~~zincir ücreti yerel varlıkta gösterilir, USD'ye çevrilmez~~ — **düzeltme turunda değişti (F1):** gider platformun gerçek harcamasıdır, yerel varlıkta ve onay anı fiyatıyla USDT'de gösterilir, `netUsdt` dengeyi verir. Yanlış token kesintisi USDT toplamına 1:1 girer (değişmedi).
10. **T160c'den önce kalmış kalıntı** da aynı kuralla toplanır (prova DB'lerinde; canlıda yok).
11. **Toplayıcı 10 dakikada bir, tur başına 20 depozit;** fiyatı sorulan depozit 24 sa / 30 dk geri çekilir (TronGrid kotası).
12. **`CK_…_RefundFeeRecovered`'da `<= Amount` yok** (SQLite metin karşılaştırması yanlış ret üretirdi; kodda sağlanıyor).

---

## Açık soru (sahip — bloke etmez) — **cevaplandı: (a), proje sahibi kararı 2026-10-21 (modal)**

1. **Tek iadeli, sweep'e ulaşmamış depozitlerde kalıntının bir kısmı toplanmayacak.** Ayrı toplama da bir depozit transferidir ve maliyeti iadenin kendi ücretine yakındır (TRON: iade ücreti ~7,5–14 TRX — depozit açılışı + alıcının adresine göre enerji; sıcak cüzdana toplama ~6,4 TRX. Ethereum: ikisi de yakıt + token gas'ı). Kural "maliyet < tutar" olduğu için alıcının adresi boş olan iadelerin kesintisi toplanır, adresinde zaten USDT olan alıcıların iadelerinde kalıntı çoğu zaman depozitte kalır (defterde "hâlâ depozitlerde" olarak görünür). Seçenekler: (a) bugünkü kural (önerilen — toplama hiç zarar etmez, kalıntı görünür); (b) sweep'e ulaşmayacak depozitten yapılan iadede alıcıdan toplama maliyetini de kesmek (kesinti kuralını değiştirir — sahip kararı gerekir); (c) kalıntıyı zararına da toplamak (platform transfer başına küçük zarar eder). Kod (a)'yı uygular; (b)/(c) karar verilirse ayrı görev.

---

## Altyapı Değişiklikleri

- Migration: **Var** — `T160c_RefundFeeSweep` (2 kolon, 2 CK, 1 filtreli tekil indeks; veri dönüşümü yok).
- Config/env değişikliği: **Yok** (yeni ayar yok).
- Docker değişikliği: **Yok**.
- Yeni Hangfire işi: `refund-fee-sweep-queue` (`*/10 * * * *`).

---

## Commit & PR

- Branch: `task/T160c-iade-kesinti-kalintisi`
- Commit: `182517cb` — T160c: iade kesinti kalıntısı sıcak cüzdana toplanıyor (⚠ PARA) (+ rapor/PR no commit'i)
- PR: #365
- CI: dal CI'ı yöneticinin son mesajında (run ID + sonuç)

---

## Known Limitations / Follow-up

- Ekonomik olmayan kalıntı depozitte kalır (açık soru 1); defterde görünür, 24 saatte bir yeniden fiyatlanır. Fiyatı sorulan depozit sayısı zamanla büyür (tur başına en çok 20 tahmin).
- `FAILED` toplama otomatik yeniden açılmaz; admin T157 kapatma aracıyla (Ethereum) ya da elle ele alır.
- Ethereum sıcak cüzdanında biriken USDC (yanlış token kesintileri) AD39 ile soğuğa aktarılamaz.
- Mutabakatın depozit kapsamı iade satırlarını hâlâ saymıyor (`PaymentAddressId` NULL — önceden var); toplama satırı depozit çıkışı olarak doğru sayılıyor, sıcak cüzdan girişi tam.
- e2e (`multi-network`, `payment-edge-cases`) değişmedi; toplayıcı 10 dakikalık olduğundan e2e süresince iptal edilmiş depozitlerde toplama satırı belirebilir — e2e satırları türe göre süzdüğü için etkilenmez (CI'da e2e advisory).

---

## Notlar

- **Working tree:** temiz (dal `origin/main` `fc3e8840`'tan açık).
- **Main CI startup check:** son 3 run `success` — 39546534805, 39546534823 (`fc3e8840`, #362), 39545306202 (`5ad1e6b7`, #361).
- **Yönetici düzeni:** yapım alt ajanı; doğrulama ve merge yöneticide (INSTRUCTIONS §3.3).
- **T160b ile ilişki:** iade gas tahmininin yeniden denemesi T160b'de; bu görev yalnız kaydedilmiş `GasFee`'i okur ve toplama maliyeti için `IGasFeeEstimator` arayüzünü çağırır (dosyasına dokunmadan).

---

## Düzeltme turu (doğrulama ✗ FAIL, HEAD `c5dab67c`, 2026-10-21)

Doğrulama raporu yedi bulgu verdi (S1 ×2, S4 ×5); bloke eden yalnız F1. Hepsi aşağıda.

### F1 (S1 ⚠ PARA, bloke edici) — gider artık platformun gerçek zincir harcaması

**Kural.** Gider, bu transfer için **platform cüzdanlarından çıkan yerel varlıktır** (`BlockchainTransaction.PlatformNetworkCost`, en küçük birimde). Depozit, kendi transferinin ücretini platformun ona gönderdiği TRX'ten / yakıt ETH'sinden öder. Bu yüzden "makbuz ücreti + yakıt ücreti + yakıt değeri" toplamı makbuz ücretini **iki kez** sayardı. Doğru toplam:

| Gönderim | Gider (`PlatformNetworkCost`) | Kim ölçer, ne zaman |
|---|---|---|
| TRON depozit gönderimi (sweep, iadeler, `REFUND_FEE_SWEEP`) | Sıcak cüzdanın bu çağrıda depozite gönderdiği TRX (hesap açma 1 SUN, yakma / Bandwidth takviyesi, sabit yedek) + bu gönderimlerin yaktığı ücret (`gettransactioninfobyid.fee`; hesap açmada hesap oluşturma ücreti). Depozitin yakmadığı TRX orada kalır, toplanmaz, yani giderdir. | `sidecar-blockchain` `EnergyDelegationService` → sweep / iade cevabında `platformSpendSun` → `OutgoingTransferDispatchJob` gönderimde yazar |
| Ethereum depozit gönderimi | İsteğin `fuel:<id>` yakıtının değeri (yakıt geri döndüyse 0) + yakıt işleminin makbuz ücreti (`gasUsed × effectiveGasPrice`). Kendi yakıtı yoksa `0` (depozitteki ETH'yi daha önceki bir yakıt getirdi, o sayıldı). Yakılmayan yakıt depozitte kalır (T153 "artık ETH toplanmaz"), yani giderdir. | `sidecar-ethereum` `TransferService.status` (kesin durumda) → `platformSpendWei` → `OutgoingTransferConfirmationJob` onayda yazar |
| Sıcak cüzdan gönderimi (Ethereum payout, sweep sonrası iade) | Makbuz ücreti | Aynı alan (`platformSpendWei` = `realizedFeeWei`) |
| Ölçülmeyen satır (eski servis, kayıp günlük, TRON payout) | NULL | AD38'de makbuz ücretiyle **alt sınır** olarak girer, `costUnmeasured` sayar |

**USDT karşılığı.** Satır `CONFIRMED` olurken, servisin o kesin durum okumasıyla bildirdiği yerel varlık fiyatıyla (`nativePriceUsdt`) **bir kez** hesaplanır: `PlatformNetworkCostUsdt = cost / 10^decimals × fiyat`, 6 ondalığa sıfırdan uzağa yuvarlanır. Sonradan bugünkü fiyatla yeniden değerlenmez. Fiyat kaynağı ücret tahminiyle aynıdır (TRON `TrxPriceService`, Ethereum `EthPriceService`: Binance, yedek CoinGecko, 5 dk önbellek). Durum okuması fiyat için en çok 5 sn bekler. Fiyat gelmezse harcama yine yazılır, USDT NULL kalır ve `costUnmeasured` artar. Ekranda her ağın kendi açıklaması bunu yazıyor ("USDT karşılıkları her transferin onaylandığı andaki TRX / ETH fiyatıyladır").

**AD38 (`refundFees`) yeni alanlar:** `refundChainCostUsdt`, `collectionChainCostUsdt`, `netUsdt` (= `withheld` − iki USDT gideri; negatif = zarar; `costUnmeasured` > 0 iken üst sınır), `costUnmeasured`. `refundChainCost` / `collectionChainCost` artık `PlatformNetworkCost` toplamı. Frontend: satır etiketleri "Bu iadeler için platformun zincir harcaması", "≈ … USDT", "Net (kesilen − zincir harcaması)", ağ başına açıklama, ölçülmeyen satır uyarısı (4 dil, 1593 anahtar eşit).

**Yeniden hesap (T159b ölçülmüş sayıları; entegrasyon testiyle pinlendi):**

| Senaryo | Kesilen | Eski ekran gideri (makbuz) | Yeni gider (platform harcaması) | Eski net | Yeni net |
|---|---|---|---|---|---|
| Sepolia 2 — `BUYER_REFUND` (yakıt 168.966.002.534.490 wei + 204.600 gas × 1 gwei; makbuz 152.417 gas) | 0,50 | 0,000152417 ETH ≈ 0,372639 USDT | 0,000373566002534490 ETH = **0,913317 USDT** | +0,127 (kâr gibi) | **−0,413317 (zarar)** |
| Sepolia 4c — `LATE_PAYMENT_REFUND` (yakıt ≈ 168.940 × 1,000000015 gwei + 204.600 gas; makbuz 152.393 gas) | 0,92 | ≈ 0,372580 USDT | 0,913254 USDT | +0,547 | +0,006746 (başabaş) |
| İkisi birlikte (Ethereum bloğu) | 1,42 | 0,00030481 ETH ≈ 0,745218 | 0,00074710600506859 ETH = 1,826571 | +0,674782 | **−0,406571** |
| Nile 6 — `INCORRECT_AMOUNT_REFUND` (makbuz 0; hesap açma 1 SUN + 1,1 TRX; 1,611499 TRX yakma takviyesi depozitte kaldı) | 0,37 | 0 TRX | 2,7115 TRX = 0,898320 USDT | +0,37 | **−0,52832** |

ETH 2.444,861904, TRX 0,3313 (T159b). Eski ekran Ethereum'da gerçek giderin **%40,8**'ini gösteriyordu (doğrulayıcının %41'i). TRON'da Nile kontratı enerjiyi sahibinden karşıladığı için makbuz 0, harcamanın tamamı hesap açma ve takviyedir. Fikstürler sabitlerden farklı: harcama ≠ makbuz, fiyat ≠ test sabiti `ETH_USD`, 4c'nin yakıt değeri senaryo 2'ninkinden farklı.

**Testler:**
- Backend: `AdminWalletOverviewTests.T160c_RefundFeeBooks_Expense_Is_What_Left_The_Platform_At_The_Confirmation_Price` (SQL Server + migration; DETECTED satırlar **üretim `OutgoingTransferConfirmationJob`'undan** geçer, sonra AD38 okunur); mevcut `…_Show_Withheld_…` testine ölçülmeyen satır (`costUnmeasured` 6 / 1, USDT 0, net üst sınır) eklendi; `OutgoingTransferConfirmationJobTests.T160c_Ethereum_Confirmation_Records_The_Platform_Spend_And_Values_It_At_The_Reported_Price`, `T160c_Tron_Confirmation_Values_The_Spend_The_Broadcast_Recorded_And_Keeps_It`, `T160c_Without_A_Price_The_Spend_Is_Kept_And_Left_Unvalued`; `OutgoingTransferDispatchJobTests.T160c_Tron_Broadcast_Records_What_The_Send_Cost_The_Hot_Wallet` (+ ölçüm yoksa NULL); `HttpBlockchainTransferClientTests.GetStatus_Ethereum_Reads_The_Platform_Spend_And_The_Price_Or_Records_Nothing` (7 vaka: JSON sayı, negatif, 7. ondalık, null → kaydedilmez, durum yine Confirmed), `GetStatus_Tron_Reads_The_Price_But_Never_A_Wei_Spend`, `Broadcast_Reads_Platform_Spend_Sun_Without_Ever_Failing_A_Sent_Transfer` (6 vaka; bozuk ölçüm gönderilmiş transferi asla "ayrıştırılamadı → yeniden dene"ye çevirmez); `EthereumFeeWeiContractTests` sözleşme örneğinin yeni alanları.
- `sidecar-ethereum`: `Fuel.test.ts` "platformSpendWei is the ETH that left the platform" (Sepolia tarifesi: harcama = yakıt cüzdanının ETH kaybı, makbuz ≈ %40; kendi yakıtı yok → `"0"`; geri dönen yakıt → yalnız ücreti; kesin değil → null), `TransferService.test.ts` sıcak cüzdan payout'u (harcama = makbuz, fiyat yok → null, durum yine cevaplanır); sözleşme örnekleri `contracts/transfer-status/{confirmed,unknown}.response.json` yeni alanlarla.
- `sidecar-blockchain`: `EnergyDelegationService.test.ts` "platformSpendSun" (yeni depozit + yakma: 1 + 1.100.000 + 7.071.349 + 267.000 = 8.438.350 SUN; hiçbir şey gönderilmedi → 0; sabit yedek + ücreti), `TronResourceClient.test.ts` `getTransactionLanding` (`fee` okuma; yok/metin/negatif → 0; bloksuz → null), `TransferService.test.ts` (sweep cevabı harcamayı taşır; durum fiyatı 6 ondalık; fiyat hatası → null; bloksuzken fiyat sorulmaz).

**Bozma (F1):** her biri geri alındı, `git status` yalnız gerçek değişiklikler.

| # | Bozma | Sonuç |
|---|---|---|
| F1-M1 | AD38 gideri yine makbuz ücretinden (`nativeUnits = receiptUnits`) | öldü — `T160c_RefundFeeBooks_Expense_…` kırmızı |
| F1-M2 | Onay işi `PlatformSpendWei`'yi yazmıyor | öldü — entegrasyon testi + 2 birim test kırmızı |
| F1-M3 | Ethereum servisi depozit gönderiminde harcama yerine makbuz ücretini bildiriyor | öldü — `Fuel.test.ts` 3 kırmızı |
| F1-M4 | TRON servisi gönderilen TRX'in ücretini harcamaya eklemiyor | öldü — `EnergyDelegationService.test.ts` 2 kırmızı |

### F2 (S1 ⚠ PARA — sahip kararı) — "zararına toplama yok"

**Proje sahibi kararı 2026-10-21 (modal):** ayrı toplama yalnız tahmini ücret kalıntıdan küçükse yapılır. Kalan depozitte kalır ve admin ekranında görünür. Kod zaten böyleydi; değişmedi. Karar 02 §4.7, 05 §3.3, 08 §3.4, `plan/11_F9.md` "T160c kararları" (xi) ve DOC_CHANGELOG'a yazıldı.

**Kabul kriteri 1 bu kararla nasıl karşılanıyor:** sweep'e ulaşan her işlemde kalıntı **0**: sweep kesintiyi ek maliyetsiz taşır. Sweep'e ulaşmayan işlemde ve USDC kesintisinde kalıntı, toplama kendini ödediği anda toplanır. Ödemiyorsa sahip kararıyla depozitte kalır, 24 saatte bir yeniden fiyatlanır ve AD38'de "hâlâ depozitlerde" satırında tutarıyla görünür. Gider tarafı artık gerçek olduğu için "toplamaya değer mi" sorusunun sonucu da ekranda `netUsdt` olarak görünür. Kriterin "depozitte kalmaz" ifadesi, bu sahip kararıyla "kendini ödemeyen kalıntı hariç" olarak okunur.

### F3 (S4) — sweep `CONFIRMED` şartı testle korunuyor

- `RefundFeeSweepQueueJobTests.A_Failed_Sweep_Keeps_Its_Folded_Fee_Even_When_Collecting_Would_Pay` (ITEM_DELIVERED ve COMPLETED): FAILED sweep, katlanmış 1,37 USDT. Toplama ekonomik (1,37 > tahmin 1,20). Toplama yok, fiyat bile sorulmuyor.
- Kontrol: `Control_The_Same_Fee_Behind_A_Confirmed_Sweep_That_Carried_Nothing_Is_Collected`. Aynı ücret CONFIRMED (hiçbir şey taşımamış) sweep'in arkasında 1,37 olarak toplanıyor. Yani fikstür ekonomik.
- **Bozma M8** (sweep `Any(...)` içinden `&& b.Status == CONFIRMED` kaldırıldı): **öldü, 2 kırmızı** (iki vaka). Geri alındı.

### F4 (S4, süreç) — `origin/main` dala merge edildi

- Merge commit `9985a2b7`, rebase değil; ebeveynler `c5dab67c` + `2e502e99`.
- Düzeltme commit'i push edildikten sonra main'e T166 (#366, `e7e8acf1`) birleşti ve PR yine çakıştı. İkinci merge commit'i (rebase değil) aynı yöntemle çözüldü. Kod çakışması yok; i18n dosyaları otomatik birleşti. 02 ve 07 başlıkları T166 ile aynı gün v4.17'de çakıştı, bu yüzden ikisi de **v4.18** oldu (iki görev başlıkta). `STATUS_CHANGELOG`'u T166 bütünüyle yeniden yazmıştı; main'in sürümü alındı, bu dalın girişleri üstüne eklendi.
- Kod çakışması yok (T160b dosyaları otomatik birleşti). T160b'nin min(ölçülen, sabit) yedeği değişmedi.
- Çakışan belgelerde iki tarafın satırları da korundu:
  - 02/05/08 başlıkları: main'in bir üstü, v4.17 / v3.30 / v3.44.
  - `DOC_CHANGELOG`: T160c girişi T160b girişlerinin üstünde.
  - `IMPLEMENTATION_STATUS`: T160a / T160b satırları main'den, T160c satırı bu dal.
  - `STATUS_CHANGELOG`: iki eski başlık da arşivde.
  - `DEFERRED_BACKLOG`: main'in iki Durum satırı korundu, üstüne yeni satır.
- **Backlog sayımı:** dosyanın kendi yöntemiyle, §1'den itibaren `| 🔴/🟡/⚪ |` aktif ve `| ✅ |` çözülmüş satırlar sayıldı → **51 aktif / 159 çözülmüş**. Bu, son main'e (`e7e8acf1`, 52/158) göre; `RefundGasDeductionDustStaysOnDeposit` ✅. `2e502e99`'a göre ilk merge'de 50/159 idi; T166 bir aktif satır ekledi.

### F5 (bilgi) — `plan/11_F9.md` CRLF

Blob ilk commit'te LF'ye normalize edilmişti. Bu turdaki diff yalnız "T160c kararları" satırı (1 satır). Eski satır normalizasyonu geri alınmadı: geri almak dosyayı yeniden CRLF'li index'e taşırdı, `text=auto` kuralına aykırı olurdu.

### F6 (bilgi) — aday sorgusu daraltıldı

`RefundFeeSweepQueueJob.Candidates`'e ilk koşul olarak ucuz bir EXISTS eklendi: "depozitten çıkmış, onaylanmış, `GasFee > 0` bir iade var". Satışların çoğu hiç iade görmez. Bu satışlar artık iki alt sorgu toplamına hiç ulaşmaz; sonuç aynı (`RefundFeeSweepQueueJobTests` 37/37, `SweepQueueJobTests` yeşil). Bilinen sınırlama: bu koşul için özel bir indeks eklenmedi, sorgu planı ölçülmedi. Ölçek büyürse izlenmeli. TRON'da "bedava" toplamanın paylaşılan enerjiyi kullanması (F6'nın ikinci yarısı) staking kapalıyken geçerli değil: her transfer yakar, tahmin bunu fiyatlar.

### F7 (bilgi) — USDC imza bayrağı testli

`Fuel.test.ts` "the USDC flag the signer is given, per flow": imzacıya giden `wrongTokenRefund` bayrağı gerçek politika çalışırken okunuyor. `EXCESS_REFUND` / `INCORRECT_AMOUNT_REFUND` / `BUYER_REFUND` (USDT) → false, `WRONG_TOKEN_REFUND` (USDC) → true, sweep → true. **Bozma S3** (bayrak her akışta `true`): **öldü, 3 kırmızı**. Geri alındı.

### Devir kaydı

`STATUS_CHANGELOG`'a T160b (#364) merge teyidi işlendi: main `2e502e99`, CI `39582270230` ✓, Docker Publish `39582270184` ✓. Ayrıca T166 (#366): main `e7e8acf1`, CI `39600854571` ✓, Docker Publish `39600854541` ✓.

### Yeni ⚠ kararlar (öneriyle kaydedildi — ⚠ PARA)

13. **Gider = platform cüzdanlarından çıkan yerel varlık** (yukarıdaki tablo). Depozit transferinin makbuz ücreti ayrıca eklenmez. Depozitte kalan yakıt / TRX gider sayılır.
14. **USDT değeri onay anı fiyatıyla, bir kez.** Kaynak servisin ücret tahminindeki fiyattır. Yeniden değerleme yok. Fiyat yoksa USDT NULL kalır, `costUnmeasured` sayar.
15. **Ölçülmeyen satır** AD38 yerel varlık toplamına makbuz ücretiyle (alt sınır) girer, USDT toplamlarına girmez. Ekran sayısını yazar.
16. **TRON harcaması gönderim cevabıyla, Ethereum'unki kesin durumla gelir.** TRON'da gönderim zaten eşzamanlıdır ve TRX gönderim anında çıkar. Gönderim yoluna ek bekleme eklenmedi: ücret, mevcut blok yoklamasıyla (`gettransactioninfobyid`) aynı cevaptan okunuyor. Fiyat gönderim yolunda değil, durum okumasında soruluyor; böylece gönderim çağrısı backend zaman aşımına yaklaşmaz.
17. **Ölçüm kayıt engeli değildir.** Bozuk / eksik alan loglanır, NULL yazılır. Gönderilmiş transfer "yeniden dene"ye, kesin durum "okunamadı"ya dönmez.

### Kalan sınırlamalar (yeni)

- **TRON, yarıda kalan çağrı:** hesap açma ya da takviye gönderilip çağrı sonra hata verirse (ör. `TRANSFER_WINDOW_ELAPSED`), o TRX hiçbir satıra yazılmaz. Sonraki deneme yalnız eksik kısmı gönderir, yani gider eksik kalır. Seyrek; kalıcı bir harcama defteri (Ethereum'daki günlük gibi) TRON servisinde yok.
- **Ethereum, sahipsiz yakıt:** bir isteğin yakıtı gönderilip token transferi hiç imzalanmazsa (satır FAILED), o yakıt sayılmaz. Bu depozitten sonraki gönderim "kendi yakıtı yok → 0" der. Gider bu durumda eksik kalır.
- **FAILED satırlar:** defter yalnız onaylanmış iadeleri ve onaylanmış toplamaları gider sayar. Zincirde geri dönen bir iadenin / toplamanın yakıtı ve ücreti AD38'de görünmez.
- **Uyuyan staking yolu (T149):** delegasyon / geri alma işlemlerinin Bandwidth'i sayılmaz (staking kapalı, kullanılmıyor).
- **`sidecar-fake`** yeni alanları dönmüyor. e2e'de satırlar "ölçülmedi" görünür; e2e advisory, akış değişmedi.

### Düzeltme turu test sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend Release/Debug build | ✓ 0 hata | `dotnet build Skinora.sln` |
| Backend Unit (CI filtresi) | ✓ 2324/2324 | Shared 572 · Platform 161 · Fraud 19 · Users 49 · Auth 92 · Steam 59 · Disputes 25 · Realtime 44 · Notifications 150 (Testcontainers'lı 16'sı LocalDB ile) · API 121 · Transactions 1032. Tam koşuda API'nin `ReconciliationServiceTests.T158_Ethereum_BalanceSnapshot_IsReadInChunksOfTen_…` bir kez kırmızı çıktı. Bu teste dokunulmadı; sınıf tek başına 3/3, API unit yeniden 121/121 geçti. Yük altında görülen kararsızlık |
| Backend Contract | ✓ 98/98 | Shared 5 · Transactions 69 · API 24 (sözleşme örneklerinin yeni alanları dahil) |
| Backend Integration (LocalDB, SQL Server 2019) | ✓ Shared 16/16 · Transactions 735/735 · API 752/752 | API'ye 1 yeni test eklendi (751 → 752). Docker kullanılmadı |
| Migration | ✓ | `T160c_PlatformNetworkCost` (2 kolon). `has-pending-model-changes` → "No changes". `UseMigrations` entegrasyon testleri (AdminWalletOverview, InitialMigration) yeşil |
| `dotnet format --verify-no-changes --severity error` (CI'daki komut) | ✓ çıkış 0 | |
| sidecar-ethereum | ✓ 882/882 | tsc ✓, eslint ✓, prettier (`--end-of-line auto`) ✓ |
| sidecar-blockchain | ✓ 597/597 | tsc ✓, eslint ✓, prettier ✓ (`npm ci` ile) |
| Frontend | ✓ 392/392 | `npm ci` (main'in next 16.3.8'i), `tsc --noEmit` ✓, `eslint` ✓, `i18n:check` 4 dil 1593 anahtar eşit ✓, prettier (`--end-of-line auto`) ✓, `vitest run` ✓ |
| **T166 merge'ünden sonra (`e7e8acf1`)** | ✓ | Build 0 hata. Unit 2345/2345 (Auth 96, API 138 — T166 testleri dahil). `AdminWalletOverviewTests` (LocalDB) 8/8. `dotnet format` çıkış 0. Frontend tsc / eslint ✓, i18n 1591 anahtar eşit (T166 iki anahtar kaldırdı), vitest 401/401. Tam integration suite'i T166 sonrası CI'da koşar |
| Bozma (bu tur) | 6 / 6 öldü | F1-M1…M4, M8, S3 (yukarıda) |
