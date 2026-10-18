# T149 — Ağ ücreti politikası

**Faz:** F8 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız doğrulama bekliyor) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **TRON ağ ücreti satırı (11 §F8 K8) kabul anında sabitleniyor.** Yeni kolon `Transaction.NetworkFeeAmount` (decimal(18,6) NULL — kabulden önce "henüz sabitlenmedi"). Kabul, reddedebilecek her kuraldan (ağ, iade adresi, yaptırım, trade URL, cooldown, iki Steam yoklaması) **sonra** satırı okur ve aynı kayıtta `NetworkFeeAmount` + `TotalAmount = Price + CommissionAmount + NetworkFeeAmount` yazar; depozitin `ExpectedAmount`'ı ondan türer. Ethereum'da satır `0`. Kabul yanıtına `networkFeeAmount` eklendi (07 §7.6).
- **Satırın formülü** (`FinancialCalculator.CalculateTronNetworkFeeLine`): `satır = ROUND(sweepCostSun ÷ 10⁶ × trxPriceUsdt × (1 + blockchain.tron.network_fee_buffer_ratio), 2, ToPositiveInfinity)` — yuvarlama bir kez, sonda. Örnek: 8.441.349 SUN × 0,34 × 1,007 = 2,8901 → **2,90** (02 §5 örneğiyle aynı).
- **Sweep maliyeti tahmini — yeni sidecar ucu `POST /api/transfer/estimate-sweep-fee`** `{ network, token }` (`FeeEstimationService.estimateSweepCost`). Kilit kapalıyken depozit akışının izleyeceği yakma yolunu fiyatlar: `sweepCostSun = activationSun + burnTopUpSun + bandwidthSun`:
  - `activationSun` = `activationBurnSun(depositExists=false, …)` → 1 TRX + (sıcak cüzdanın kilitli bandı yetmezse) 0,1 TRX;
  - `burnTopUpSun` = `topUpSun(burnSun(energyRequired × energyFee, deposit bandı 600 ≥ 350 → 0), bakiye 1 SUN)` = ⌈enerji × 100 × 1,1⌉ − 1 → 7.071.349 SUN (transferin **tamamı**, sahibin payı sayılmaz — gönderilen TRX geri toplanmaz);
  - `bandwidthSun` = 270 bayt × `getTransactionFee` (sıcak cüzdanın yakma gönderimi; ücretsiz günlük bant sayılmaz) → 270.000 SUN;
  - enerji = sıcak cüzdandan sıcak cüzdana 1 birimlik USDT transfer simülasyonu (dış varsayım ölçüldü, aşağıda).
- **Teklif = sabitlenen satır:** `NetworkFeeQuoteService` + tekil `SweepCostEstimateCache` — tahmin (SUN + fiyat, satır değil) ağ+token başına 5 dk önbellekte (başarısız tahmin 1 dk). İşlem detayı kabulden önce `networkQuotes[].networkFeeAmount` / `totalAmount`'ı bu servisten verir (TRON gerçek değer, Ethereum `"0.00"`); kabulden sonra üst düzey `networkFeeAmount` döner (07 §7.5). Tampon ve yedek ayarları her çağrıda okunur.
- **Yedek:** tahmin alınamazsa (servis yok/düşük, prob/kur hatası, bozuk cevap) `blockchain.tron.network_fee_fallback_usdt` (3,00; 2 haneye yukarı) yazılır — kabul beklemez. Tahmin `blockchain.max_charged_gas_fee_usdt`'yi (10) aşarsa kırpılmaz, reddedilir, yedek yazılır (`EstimateRejected`, hata log'u).
- **Satır sonradan düzeltilmez** — eksik/fazla platformda kalır (08 §3.1a). Sweep satırının tutarı `TotalAmount` (satır dahil): satır USDT olarak sıcak cüzdana geçer, sweep'in TRX'ini sıcak cüzdan öder.
- **İade:** `PaymentRefundToBuyerConsumer` iade tabanını `FinancialCalculator.CalculateRefundableTotal(TotalAmount, NetworkFeeAmount, sweepHasRun)` ile hesaplar — sweep çalışmadıysa (SWEEP satırı yok / `PENDING` + yayınlanmamış / `FAILED`) satır **tam** iade; sweep yayınlandıysa ya da `CONFIRMED` ise satır iade **edilmez** (⚠ PARA — aşağıda). Reddedilen ödeme iadeleri (eksik/fazla/yanlış token/geç/çoklu) gelen tutar üzerinden — değişmedi.
- **En düşük iade eşiği ağ başına:** `RefundDecisionService` çarpanı `IBlockchainNetworkSettingsProvider`'dan ağın kendi anahtarıyla okur (TRON `min_refund_threshold_ratio`, Ethereum yeni `blockchain.ethereum.min_refund_threshold_ratio` 2,0). `ResolveBuyerRefundAsync` / `ResolveOverpaymentRefundAsync` / `ResolveSellerPayoutAsync` artık `network` alır; bütün çağıranlar (iade tüketicisi, `AmountValidationService` beş kol, `SellerPayoutQueueJob`) ağı geçirir.
- **Payout kuralı ağ başına:** `NetworkFeeRules` (TRON: alıcı satırı + satıcı payout aşımı; Ethereum: ikisi de yok). TRON'da mevcut koruma kuralı aynen; Ethereum'da `ResolveSellerPayoutAsync` fiyatı döner (platform üstlenir; tavanın üstünde bekleme T152/T153/T158).
- **Kilit (hibrit enerji) uykuda — `ENERGY_STAKING_ENABLED` (varsayılan `false`):** sidecar config `parseStakingEnabled` (yalnız `true`/`false`/boş; başka değer açılışı durdurur). Kapalıyken `EnergyDelegationService` hiçbir kilidi okumaz (`getDelegatableEnergySun` çağrılmaz), devretmez (plan "delegate" dese bile yakar), `STAKE_ACCOUNT_*` değerlerini kullanmaz ve doğrulamaz; iade ücret tahmini (`FeeEstimationService`) de kilit okumaz. Açılış satırı `stakingEnabled` alanını ve `Energy delegation: OFF …` mesajını yazar. compose `ENERGY_STAKING_ENABLED=${ENERGY_STAKING_ENABLED:-false}`, `.env.example` satırı.
- **T147 öncesi depozit uyumu:** T147'den önce oluşturulmuş bir ilanın oluşturmada türetilmiş TRON depoziti varsa kabul onun `ExpectedAmount`'ını yeni toplama çeker — aksi hâlde alıcının ödediği toplam tam satır kadar fazla ödeme sayılırdı.
- **Migration `20261018093233_T149_NetworkFeePolicy`** (tek forward): kolon + `CK_Transactions_NetworkFeeAmount_NonNegative` (`NULL OR >= 0`) + T149 öncesi kabul edilmiş satırlara `0` backfill (`TotalAmount`'a dokunmadan) + üç seed satırı (76 `blockchain.tron.network_fee_buffer_ratio` 0.007, 77 `blockchain.tron.network_fee_fallback_usdt` 3.00, 78 `blockchain.ethereum.min_refund_threshold_ratio` 2.0; toplam 75 satır). Down: pozitif satır varsa `THROW 50149`.
- **Ayar katmanı:** `BlockchainNetworkSettingKeys` / `BlockchainNetworkSettings` üç alan (`MinRefundThresholdRatio`, `NetworkFeeBufferRatio`, `NetworkFeeFallbackUsdt`); validator: tampon `0 ≤ x < 1`, Ethereum çarpanı `> 0`; katalog iki TRON + bir Ethereum girdisi.
- **Doküman:** 02 v4.10 (§4.2, §4.6, §4.7, §5 formül + hesaplı örnek, §16.2) · 05 v3.17 (§3.3, §3.3a) · 06 v6.26 (§1.4, §3.5 yeni kolon, §3.17 üç satır + kurallar + sayı 75, §8.3) · 07 v4.11 (§1.3, §7.5 örnek, §7.6 yanıt, §9.8 katalog sayısı 66 → 79) · 08 v3.23 (§3.0, §3.1a yeni tablo — tahmin/satır/önbellek/yedek/kabul/sweep/iade öncesi-sonrası/kilit, §3.3, §3.4, §3.7.7) · DEPLOY_RUNBOOK §B, §C.2 · 11 §F8 "T149 ağ ücreti kararları" bloğu + §7.6 satırları.

## Etkilenen Modüller / Dosyalar

**Backend (src):** `Transactions/Domain/Entities/Transaction.cs`, `Infrastructure/Persistence/TransactionConfiguration.cs`, `Domain/Calculations/FinancialCalculator.cs`, yeni `Domain/Calculations/NetworkFeeRules.cs`, `Application/GasFee/{IGasFeeEstimator, HttpSidecarGasFeeEstimator, IRefundDecisionService, RefundDecisionService, IGasFeeSettingsProvider}.cs`, yeni `Application/GasFee/{INetworkFeeQuoteService, NetworkFeeQuoteService, SweepCostEstimateCache}.cs`, `Application/Networks/{BlockchainNetworkSettingsProvider, UnconfiguredBlockchainService}.cs`, `Application/Lifecycle/{TransactionAcceptanceService, TransactionDetailService, TransactionDetailDto, TransactionLifecycleDtos}.cs`, `Application/Transfers/{PaymentRefundToBuyerConsumer, SellerPayoutQueueJob, SweepQueueJob}.cs`, `Application/Webhooks/AmountValidationService.cs`, `Skinora.Shared/Blockchain/BlockchainNetworkSettingKeys.cs`, `Platform/Application/Settings/{SystemSettingsCatalog, SystemSettingsValidator}.cs`, `Platform/Infrastructure/Persistence/SystemSettingSeed.cs`, `Skinora.API/Configuration/TransactionsModule.cs`, migration `20261018093233_T149_NetworkFeePolicy` (+ Designer, snapshot).

**Sidecar-blockchain:** `src/config/index.ts`, `src/index.ts`, `src/wallet/EnergyDelegationService.ts`, `src/fee/FeeEstimationService.ts`, `src/api/{feeHandlers, routes}.ts`, yeni `contracts/estimate-fee/sweep.request.json`; testler `src/{config/index, config/composeWiring, config/startupWiring, wallet/EnergyDelegationService, fee/FeeEstimationService, api/feeHandlers}.test.ts`.

**Altyapı:** `docker-compose.yml`, `.env.example`.

**Backend testleri:** yeni `Unit/Calculations/NetworkFeePolicyCalculatorTests`, `Unit/GasFee/NetworkFeeQuoteServiceTests`, `Helpers/{StubNetworkFeeQuotes, GasFeeSettingsBackedNetworkSettings}`, `API.Tests/Integration/NetworkDimension/NetworkFeePolicyMigrationRehearsalTests`; güncellenen `RefundDecisionServiceTests`, `PaymentRefundToBuyerConsumerTests`, `HttpSidecarGasFeeEstimatorTests`, `ChargedGasFeeResolverTests`, `TransactionAcceptanceUnitTests`, `SellerPayoutQueueJobTests`, `AmountValidationServiceTests`, `Integration/Lifecycle/{TransactionAcceptanceServiceTests, TransactionDetailServiceTests, NetworkChoiceLifecycleTests}`, `Contract/BlockchainSidecarEstimateFeeContractTests`, `API.Tests/Integration/TransactionLifecycleEndpointTests`.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | K4/K8: TRON ağ ücreti satırı kabul anında alıcıya sabitleniyor | ✓ | `TransactionAcceptanceServiceTests.Tron_Acceptance_Fixes_The_Network_Fee_Line_Into_The_Total` (102 → 104,90, satır 2,90); `NetworkChoiceLifecycleTests` (SQL Server, gerçek quote servisi + seed ayarları: TRON `ExpectedAmount` 104,90, ödeme 104,90 → `AcceptedExact`, sweep 104,90); API `Detail_Before_Acceptance_…` (teklif 2,90 / 104,90 = kabul yanıtı = detay) |
| 1b | payout ücreti satıcıya mevcut %10 kuralıyla (TRON), Ethereum'da platform | ✓ | `NetworkChoiceLifecycleTests` payout TRON 99,70 / Ethereum 100,00 (gas 0,50, komisyonun %10'u 0,20); `RefundDecisionServiceTests.EthereumPayout_…`, `TronPayout_Keeps_The_Protection_Split` (17,75); `NetworkFeePolicyCalculatorTests.TronPayout_Doc_Table` (17,75 / 15,51 / 198,11 / 195,87) |
| 1c | sweep öncesi iadede ağ ücreti tam iade | ✓ | `PaymentRefundToBuyerConsumerTests.FeeLine_RefundBeforeSweep_ReturnsTheLineInFull` (23,30 − 2 = 21,30), `…QueuedButNotBroadcast…`, `…FailedSweep…` |
| 1d | Ethereum ücret politikası uygulanıyor | ✓ (backend kısmı) | Ethereum'da alıcı satırı 0 (`Ethereum_Acceptance_Writes_A_Zero_Line`, `NetworkFeeQuoteServiceTests.Ethereum_Is_Zero_And_Asks_No_Service`), payout'tan kesinti yok; tavanın gönderimde uygulanması ve bekleme/alarm T152/T153/T158 kapsamı (plan §F8 T142 kararı "Uygulayan: T149, T152–T154") |
| 2 | Sweep'ten sonraki iadede satırın işlenişi belirlendi, 02 §4.6 ve 08 §3.1a'ya yazıldı | ✓ (⚠ PARA) | Kural: satır iade edilmez; `FeeLine_RefundAfterSweep_KeepsTheLine` (CONFIRMED ve yayınlanmış PENDING → 18,40), `NetworkFeePolicyCalculatorTests.Refundable_After_Sweep_Keeps_The_Fee_Line`; 02 §4.6 "İade kapsamı", 08 §3.1a tablo |
| 3 | Tahmin alınamazsa ağ başına sabit yedek; satır sonradan düzeltilmez, eksik platformda | ✓ | `Tron_Acceptance_Without_An_Estimate_Writes_The_Seeded_Fallback_And_Does_Not_Wait` (SQL Server, seed 3,00 → 105,00); `NetworkFeeQuoteServiceTests` (yedek, yukarı yuvarlama, servisi olmayan ağ, tavan reddi); satırı yazan tek yer kabul (`git grep "NetworkFeeAmount ="`) |
| 4 | Küçük iade eşikleri ağ başına; ağ seçerken ücret farkı gösteriliyor | ✓ | `RefundDecisionServiceTests.MinRefundThreshold_Is_Per_Network`, `OverpaymentThreshold_Is_Per_Network`; `TransactionDetailServiceTests.Before_Acceptance_Each_Network_Is_Quoted_With_Its_Fee_Line` (TRON 2,90/104,90, ETH 0,00/102,00) |
| 5 | Kilit kodu kapama anahtarının arkasında, varsayılan kapalı | ✓ | sidecar `EnergyDelegationService.test.ts` "staking switched off" (kilit okunmaz, devretme yok, yakar; açıkken aynı kilit devredilir), `FeeEstimationService.test.ts` "staking switched off", `index.test.ts parseStakingEnabled`, `startupWiring.test.ts` (değişken yokken `stakingEnabled:false` + OFF, bozuk değerde açılmaz), `composeWiring.test.ts` (`:-false`) |
| — | Migration (tek forward, prova) | ✓ | `NetworkFeePolicyMigrationRehearsalTests` (SQL Server: kolon/CHECK/seed, geri al → ileri: kabul edilmiş 0, ilan NULL, toplam değişmez; Down pozitif satırda 50149 ile reddedilir) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend unit (bütün projeler) | ✓ 1987/1987 | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — Transactions 797, Shared 528, Platform 157, Notifications 133, Auth 92, API 84, Steam 59, Users 49, Realtime 44, Disputes 25, Fraud 19 |
| Backend contract | ✓ 27/27 | Shared 5, Transactions 5 (yeni sweep sözleşmesi testi dahil), API 17 |
| Backend integration — Transactions | ✓ 715/715 + yeniden koşu 126/126 | Tam koşu (SQL Server Testcontainers) yeni kabul/detay testleri eklenmeden önce 715; sonra `TransactionDetailServiceTests|TransactionAcceptanceServiceTests|NetworkChoiceLifecycleTests` 126/126 |
| Backend integration — Platform / API | ✓ 74/74 · 612/612 | İlk koşuda 3 eski sayım testi kırmızıydı (seed 72 → 75, Ethereum anahtarı 8 → 9: `SeedDataTests` ×2, `NetworkSettingsMigrationRehearsalTests`) — PR CI'nın ilk run'ında (`39118435062`) da aynı üçü; düzeltildi, yeniden koşu 6/6 + 8/8 (yeni `NetworkFeePolicyMigrationRehearsalTests` 6 dahil). Validator testleri 92/92 |
| Sidecar-blockchain | ✓ 573/573 | `npx vitest run` (29 dosya); `tsc --noEmit` temiz, `eslint src/` temiz, değişen dosyalar prettier temiz (`--end-of-line auto`; repo genelindeki uyarılar CRLF artifaktı) |
| Build | ✓ | `dotnet build Skinora.sln` 0 hata (uyarılar önceden var olan CS8602/8604/8620); `npm run build` (sidecar) ✓ |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (bağımsız doğrulama alt ajanı, F8 yönetici düzeni) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- Migration: **Var** — `T149_NetworkFeePolicy` (kolon + CHECK + backfill + 3 seed; Down guard 50149).
- Config/env değişikliği: **Var** — sidecar `ENERGY_STAKING_ENABLED` (varsayılan `false`); `.env.example`; üç yeni `SystemSetting` anahtarı.
- Docker değişikliği: **Var** — `docker-compose.yml` blockchain sidecar'a `ENERGY_STAKING_ENABLED=${ENERGY_STAKING_ENABLED:-false}`.

## Commit & PR
- Branch: `task/T149-ag-ucreti-politikasi`
- Commit: `1d556f6` — T149: Ağ ücreti politikası (kod + doküman); ardından sayım testleri + validator testleri + rapor/status/memory commit'i
- PR: #345
- CI: ilk run `39118435062` ✗ (3 eski sayım testi — düzeltildi); son run aşağıda/PR'da

## Known Limitations / Follow-up
- **Sweep sonrası iade otomatik çalışmaz:** otomatik iade depozitten çıkar; sweep ile payout arasındaki dar aralıkta (admin iptali) istenen iade boş depozitte `DEPOSIT_TRANSFER_WOULD_REVERT` ile `FAILED` olur ve admin'e düşer (T149 öncesi de böyleydi). T149 kuralı bu iadenin **tutarını** belirler; sıcak cüzdandan otomatik iade kapsam dışı.
- **sidecar-fake** `estimate-sweep-fee` ucunu sunmuyor → e2e'de TRON satırı yedek 3,00 olur (e2e tutarları DB'deki `ExpectedAmount`'tan okuduğu için kırılmaz). Sahte servisin ucu T155'te eklenebilir.
- Satır her zaman yakma yolunu fiyatlar; kilit anahtarı açılırsa satır yeniden ele alınmalı (08 §3.1a "Kilit açılırsa").
- Ethereum ücret tavanının gönderimde uygulanması, bekleme ve 3 sa alarmı T152/T153/T158.
- Sıcak cüzdanda hiç USDT yoksa (yeni kurulum) sweep simülasyonu revert eder → yedek yazılır; ilk sweep 130k enerjilik koldur.

## Notlar
- **Working tree:** temiz (dal `origin/main` `7cf71c2`'den açıldı).
- **Main CI startup check:** son 3 tamamlanmış run ✓ — `39107652660` (CI, T148) success, `39107652632` (Docker Publish, T148) success, `39095082123` (CI, T146) success; T147 run'ları (`39114726766` CI, `39114726772` Docker Publish) o sırada sürüyordu, sonra ikisi de success.
- **Bağımlılıklar:** T147 ✓, T148 ✓ (IMPLEMENTATION_STATUS).
- **Dış varsayımlar:**
  1. *Sweep enerjisi bir self-transfer simülasyonuyla ölçülebilir* — mainnet TronGrid `triggerconstantcontract`, USDT `TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t`, büyük tutucu `TV6MuMXfmLbBqPZvBHdwFsDnQeVfnmiuSi` (2026-10-18T09:23:29Z): kendine 1 birim → `energy_used 64285`, kendine 1000 USDT → `64285`, başka tutucuya (`TQrY8tryqsYVCYS3MFbtffiPp2ccyn4STm`) 1 birim → `64285`. 08 §3.3'ün ölçülmüş sweep enerjisiyle (64.285) aynı.
  2. *Zincir birim fiyatları ve açılış ücretleri* (100 SUN/enerji, 1.000 SUN/bayt, 1 TRX + 0,1 TRX) — 08 §3.1a/§3.3'te 2026-10-02 ölçümleri; kod onları her tahminde `getchainparameters`'tan okur.
  3. *Sidecar alanları*: `activationBurnSun`, `topUpSun`, `burnSun`, `ACTIVATION_TX_BYTES` (270) mevcut `DelegationPlanner`'dan — depozit akışıyla aynı hesap.
- **⚠ kararlar** (11 §F8 "T149 ağ ücreti kararları" i–xi): (i) sweep sonrası iadede satır iade edilmez — **PARA, yöneticiye soru olarak döndürüldü**; (iii) tampon 0,007 ve yedek 3,00 — **PARA (varsayılan değerler)**; (ii) maliyet kalemleri; (iv) 5 dk önbellek; (v) kabul sırası ve T144 öncesi ilan; (vi) NULL biçimi + backfill; (vii) anahtar adları; (viii) anahtar ortam değişkeni; (ix) Ethereum payout; (x) `NetworkFeeRules`; (xi) 07 §9.8 sayı düzeltmesi.
- **Paralel görev:** T150 (Ethereum iskeleti + outbox backoff) ayrı dalda; `OutboxDispatcher` ve gönderim/onay işlerinin yeniden deneme mantığına dokunulmadı.
