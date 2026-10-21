# T160c — İade kesinti kalıntısının toplanması (⚠ PARA)

**Faz:** F9 | **Durum:** ⏳ Yapım bitti — doğrulama bekliyor | **Tarih:** 2026-10-21

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
| 1 | Reddedilen ödeme iadesinde alıcıdan kesilen gas tutarı (USDT/USDC) depozitte kalmaz, sweep ile sıcak cüzdana taşınır (iki ağ) | ✓ (ekonomik olmayan kalıntı hariç — ⚠ (iii), açık soru 1) | USDT: `SweepQueueJobTests` T160c (sweep `Amount = TotalAmount + Σ GasFee`, `RefundFeeRecovered`); USDT/USDC ayrı toplama: `RefundFeeSweepQueueJobTests` (5 iade türü × iptal/iade durumları, TRON ve Ethereum, USDC iki ağda); gönderim: `OutgoingTransferDispatchJobTests` T160c, `HttpBlockchainTransferClientTests` T160c (sweep ucu, USDC gövdesi); Ethereum imzacısı USDC sweep: `TransferService.test.ts` T160c (3); mutabakat: `ReconciliationServiceTests` T160c |
| 2 | Muhasebe kaydı platform geliri/gider dengesini gösterir | ✓ | `RefundFeeRecovered` kolonu (06 §3.8) + AD38 `refundFees` (kesilen / sıcak cüzdanda / yolda / depozitte + iadelerin ve toplamaların zincir ücreti): `AdminWalletOverviewTests.T160c_RefundFeeBooks_…` (SQL Server, migration'la), `WalletsOverview.test.tsx` T160c |
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
9. **Defter görünümü:** zincir ücreti yerel varlıkta gösterilir, USD'ye çevrilmez (gerçekleşen ücretin anlık kuru saklanmıyor); yanlış token kesintisi USDT toplamına 1:1 girer.
10. **T160c'den önce kalmış kalıntı** da aynı kuralla toplanır (prova DB'lerinde; canlıda yok).
11. **Toplayıcı 10 dakikada bir, tur başına 20 depozit;** fiyatı sorulan depozit 24 sa / 30 dk geri çekilir (TronGrid kotası).
12. **`CK_…_RefundFeeRecovered`'da `<= Amount` yok** (SQLite metin karşılaştırması yanlış ret üretirdi; kodda sağlanıyor).

---

## Açık soru (sahip — bloke etmez)

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
- Commit: (aşağıda doldurulur)
- PR: (aşağıda doldurulur)
- CI: (aşağıda doldurulur)

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
