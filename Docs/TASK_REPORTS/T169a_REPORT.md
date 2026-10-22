# T169a — İade tam maliyet kesintisi + depozitten iade günlük tavanı (⚠ PARA)

**Faz:** F9 | **Durum:** ⏳ Devam ediyor — yapım bitti, doğrulama bekliyor | **Tarih:** 2026-10-23

---

## Özet

Proje sahibinin 2026-10-21 (modal) iki kararı uygulandı, iki ağda:

1. **İade kesintisi = platformun o iade için zincir harcamasının tamamı.** Reddedilen ödeme iadelerinde (eksik / fazla / çoklu / yanlış token / geç) ve iptal iadesinde (`BUYER_REFUND`) alıcıdan kesilen tutar artık servislerin gönderimde gerçekten harcadığı tutardır — T160c düzeltme turunun (F1) admin defterinde gider saydığı `PlatformNetworkCost` ile aynı tanım. TRON: 1 SUN + açılış ücreti + yakma takviyesi (transferin **tamamı** için %10 paylı, depozit bakiyesi düşülür) + takviyenin bandı. Ethereum: yakıt (gönderim ihtiyacı − depozitin ETH'si) + yakıtın ücreti; yakıt gerekmiyorsa 0. Sıcak cüzdan iadesi ve payout değişmedi. T160c'de ölçülen açık kapandı (Nile 6: 0,37 kesiliyordu, gerçek 0,898320 → şimdi 0,90).
2. **Depozitten iadeye ağ başına 24 saatte 100 $ tavanı.** Gönderim işi depozitten çıkan her iadeyi yayından hemen önce ağın kayan 24 saatlik toplamına tartar; aşacak iade `PENDING` bekler (deneme hakkı harcanmaz, `FAILED` olmaz), alarm verir (admin bildirimi + Grafana), pencere açılınca ya da admin AD42 ile serbest bırakınca gider. Tartı ve damga ağın kilit satırı altında, yayından önce commit edilir: iki iade aynı toplamı okuyup birlikte tavanı aşamaz.

---

## Yapılan İşler

### 1. Tam maliyet kesintisi

**TRON servisi (`sidecar-blockchain/src/fee/FeeEstimationService.ts`)** — depozit (iade) yolu artık `EnergyDelegationService`'in adımlarını izler:

- Enerji açığı = planın açığı (transferin **tamamı** − depozitin enerjisi, yakma yolunda; devretme yolunda 0). Önceden `energyPayableByCaller − depozit enerjisi` (zincirin depozitten yakacağı pay) kesiliyordu; sahibin payı için gönderilen TRX depozitte kalıp platformun maliyeti oluyordu (Nile'da her iadede ~1,6 TRX).
- `topUpSun(requiredBurn, bakiye)` — `requiredBurn` = enerji açığı × `getEnergyFee` + bant açığı (gönderimin aynı `burnSun` kuralı); bakiye: açılmış depozitte `getaccount` bakiyesi, açılmamışta açılışın bıraktığı 1 SUN.
- Takviyenin bandı: takviye > 0 ve sıcak cüzdanın kalan bandı 270 baytı taşıyamıyorsa 270 × `getTransactionFee` (⚠ aşağıda).
- `burnSun` (kesilen) = (açılmamışsa 1 SUN) + `activationBurnSun` + `topUpSun` + `topUpFeeSun`. Cevaba `topUpSun`, `topUpFeeSun` alanları eklendi; payout yolu aynen.

**Ethereum servisi (`sidecar-ethereum/src/fee/FeeEstimationService.ts`)** — formül zaten "yakıtın tamamı + yakıtın ücreti"ydi (T154/T159a); T169a depozitin elindeki ETH'yi aynı blokta okuyup düşer (`TransferService.sendFuel` gibi): `fuelWei = gasLimit × maxFee − depozit bakiyesi`, `feeWei = fuelWei > 0 ? fuelWei + fuelGas × expected : 0`. Cevaba `depositBalanceWei` eklendi (sözleşme örneği `contracts/estimate-fee/response.json` güncellendi). `FeeRpc` += `getBalance`.

**Backend:** `ChargedGasFeeResolver` değişmedi — servislerin `feeUsdt`'si kesilir; T160b'nin yeniden denemesi ve min(ölçülen, sabit) yedeği yeni toplamla çalışır (ölçüm önbelleği artık tam maliyet tahminlerini tutar). En düşük iade eşiği aynı tutarı kullandığı için (`net < 2 × gas` → iade yazılmaz) yeni toplamla tutarlı. TRON iade yedeği `blockchain.refund_gas_fee_estimate_usdt` 2,60 → **2,80** (seed + `BlockchainNetworkSettingsProvider.TronDefaults` + migration, yalnız hâlâ 2,60 olan satır taşınır).

### 2. Depozitten iade günlük tavanı

- **Ayar:** `blockchain.tron.deposit_refund_daily_limit_usdt`, `blockchain.ethereum.deposit_refund_daily_limit_usdt` (seed 81–82, `Default` — env'den doldurulmaz; `BlockchainNetworkSettingKeys.DepositRefundDailyLimitUsdt`; okunamaz / ≤ 0 → 100). `SystemSettingsCatalog`'a iki satır (`wallet_security`).
- **Kural** (`Application/Transfers/DepositRefundDailyLimit.cs`, yeni): sayılan türler iade ailesi; pencere kayan 24 saat; kullanılan = son 24 saatte `DepositRefundCountedAt` dolu, `FAILED` olmayan satırların `Amount` toplamı (tartılan satır hariç). `IsHeld` ifadesi (PENDING + `HeldAt` dolu + `CountedAt` NULL).
- **Gönderim işi** (`OutgoingTransferDispatchJob`): onay kapısı → kaynak çözümü → token çözümü → **tavan** (yalnız `RefundSource.Deposit` + iade türü) → yayın. `PassDepositRefundLimitAsync`: tek DB işlemi; `DepositRefundDailyLimit.LockAsync` (ağın `DepositRefundLimitLocks` satırını EF `ExecuteUpdate` ile kilitler); 24 saat içinde kabul edilmiş satır yeniden denemede doğrudan geçer (damgası silinmez); `released || kullanılan + Amount ≤ tavan` → `DepositRefundCountedAt = şimdi`, commit, gönder; değilse `HeldAt ??= şimdi`, `NextAttemptAt = +15 dk`, `ErrorMessage = DEPOSIT_REFUND_DAILY_LIMIT: …`, ilk beklemede outbox `DepositRefundLimitHeldEvent`, commit, gönderme. Bekleyen satır kuyruk takılma göstergelerinden çıkarıldı. Her tur, gönderimlerden **sonra**, ağ başına `IDepositRefundLimitObserver.Report(held, used, limit)`.
- **Alarm:** `PrometheusDepositRefundLimitObserver` (`skinora_backend_deposit_refund_limit_held_rows` / `_used_usdt` / `_limit_usdt`), Grafana kuralı `deposit-refund-daily-limit-held` (warning, `max by (network) (held_rows) > 0`, `for: 0m`). Admin bildirimi `DepositRefundLimitHeldAdminNotificationConsumer` → `ADMIN_PAYMENT_FAILURE` (`ErrorCode = DEPOSIT_REFUND_DAILY_LIMIT:<ağ>:<tür>`; T158'in `SELLER_PAYOUT_DEFERRED` deseni — yeni bildirim türü yok, şablonlar değişmedi).
- **Admin kararı:** AD42 `POST /admin/wallets/outbound-approvals/:id/release-daily-limit` (`MANAGE_SETTINGS`): `DepositRefundLimitReleasedAt` / `…ByAdminId`, `NextAttemptAt = NULL`, audit `DEPOSIT_REFUND_LIMIT_RELEASED` (Fon; yeni `AuditAction`, 35 değer). Hata kodları 404 `ROW_NOT_FOUND`, 409 `ROW_NOT_HELD`, 409 `ALREADY_RELEASED`. AD40 yanıtı += `depositRefundLimits[]`, `limitHeldRows[]`.
- **Veri modeli + migration** `20261022214930_T169a_DepositRefundDailyLimit`: `BlockchainTransactions` += `DepositRefundCountedAt`, `DepositRefundLimitHeldAt`, `DepositRefundLimitReleasedAt`, `DepositRefundLimitReleasedByAdminId` (hepsi NULL); yeni tablo `DepositRefundLimitLocks(Network PK, LockedAt)` + iki seed satırı; iki ayar satırı; TRON iade yedeğinin açıklaması (UpdateData) + değeri (`SetIfStillDefault` 2.60 → 2.80, Down tersi). `has-pending-model-changes` → yok.
- **Arayüz:** S22 "Onay bekleyen ödemeler" panelinin altında "Günlük iade tavanı (depozitten)" bölümü (ağ başına "son 24 saatte X / Y USDT", bekleyen iadeler tablosu, "Tavanı aşarak gönder" → uyarılı onay penceresi → AD42); hata kodları; audit etiketi (4 dil).
- **e2e harness:** `seedHappyPath` tavanı 1.000.000'e çeker (onay kapısını açtığı gibi — süitler tavanı değil para hattını ölçer).

### 3. Görünürlük ve hukuk metni

- Alıcının işlem sayfasındaki iade özeti etiketi "Gas fee" → **"Ağ ücreti (iadenin zincir maliyeti)"** (4 dil); admin işlem detayı "Ağ ücreti". Reddedilen ödeme bildirimleri zaten `{GasFee}` ağ ücretini gösteriyordu (T159a) — değişmedi.
- **T168 main'e girmişti** (`69c68df6`, dal açıldıktan sonra) → dala birleştirildi ve Kullanıcı Sözleşmesi "İadeler" bölümü (4 dil) güncellendi: iadenin ağ ücretinin tanımı (iki ağ) + "iade tutarını aşamaz" + depozit iadelerine 24 saatte ağ başına 100 USDT üst sınırı ve bekleme. ToS sürümü (`NEXT_PUBLIC_TOS_VERSION`) T168 kararıyla artırılmadı (gerçek kullanıcı yok; işletmeci bilgisiyle birlikte artırılacak). `legal.test.tsx` += T169a testi.

---

## Önce / sonra (T160c F1'in ölçülmüş sayıları; ETH 2.444,861904 · TRX 0,3313)

"Gerçek gider" = platform cüzdanlarından çıkan (T160c F1 `PlatformNetworkCost`). Net = kesilen − gerçek gider (negatif = platform zararı).

| Senaryo | Önce kesilen | Sonra kesilen | Gerçek gider | Önce net | Sonra net | Not |
|---|---|---|---|---|---|---|
| **Nile 6** `INCORRECT_AMOUNT_REFUND` (yeni depozit; Nile sahibi enerjiyi öder) | 0,37 (yalnız açılış 1.100.000 SUN) | **0,90** (1 + 1.100.000 + 1.611.499 = 2.711.500 SUN) | 2.711.500 SUN = **0,898320** | −0,52832 | **+0,00168** | ölçülen harcamanın adımları birebir; `FeeEstimationService.test.ts` "Nile 6" |
| **Sepolia 2** `BUYER_REFUND` (yeni depozit) | 0,50 (sabit yedek — T159b "block not found") | tahmin alınırsa **≈0,93** (yakıt 168.966.002.534.490 wei + yakıt gas'ı 207.391 × ~1 gwei = 0,920141 → 0,93); yedekte min(ölçülen, 0,20) | 0,913317 | −0,413317 | **≈+0,0167** | açık formülün değil yedeğin açığıydı; T160b yeniden denemesi tahmini getirir. Yedekte Sepolia'da açık kalır (aşağıda sınırlama) |
| **Sepolia 4c** `LATE_PAYMENT_REFUND` | 0,92 | 0,92 (formül aynı; depozit yakıt ihtiyacını karşılamıyordu) | 0,913254 | +0,006746 | +0,006746 | değişmedi |
| Ana ağ (hesap, ölçüm değil), yeni depozit, USDT tutan alıcı, sıcak cüzdanın bandı bitmiş | 2,50 (7.528.500 SUN) | **2,80** (8.441.350 SUN) | 8.441.350 SUN = 2,796619 | −0,302427 | +0,003381 | 64.285 Energy × 100 SUN; test "mainnet, recipient holds USDT" |
| Ana ağ (hesap), yeni depozit, token'ı ilk kez alan alıcı | 4,69 (14.128.500 SUN) | **5,21** (15.701.350 SUN) | 5,201857 | −0,511857 | +0,008143 | 130.285 Energy |
| Sepolia (hesap), aynı depozitten ikinci iade (ilk yakıtın kalıntısı ihtiyacı karşılıyor) | 0,25 | **0,00** | 0 (yakıt gönderilmez) | +0,25 (alıcıdan fazla) | 0 | T169a Ethereum depozit bakiyesi; test "T159b pair" |

TRON kesintisi yuvarlama (2 hane yukarı) yüzünden gerçeğin en çok 1 sent üstündedir. Kalan sapma kaynakları (tahmin ile gönderim arası fiyat / bant / bakiye değişimi) "Bilinen sınırlamalar"da.

**Eski satırlar yeniden fiyatlanmaz.** Kesinti kuyruğa alma anında `Amount` / `GasFee` olarak sabitlenir ve geriye dönük düzeltilmez (08 §3.1a ilkesi): alıcıya bildirim o tutarla gitmiştir, yeni bir tahmin o anın zincirini bilmez ve satır onaylanmış (T169) olabilir. T169a'dan önce kuyruğa girmiş, onay ya da gönderim bekleyen iadeler eski kesintiyle gider; AD38 defterinde gerçek giderleriyle (olası negatif net) görünürler. Deneme yayını veritabanında yalnız test parası var.

---

## Tavan tasarımı (özet)

- **Ne sayılır:** iade ailesi (`BUYER_REFUND`, `EXCESS_REFUND`, `WRONG_TOKEN_REFUND`, `INCORRECT_AMOUNT_REFUND`, `LATE_PAYMENT_REFUND`) **gönderim işi depozitten gönderdiğinde** — kaynak `RefundSourceResolver`'dan: TRON'da her zaman depozit, Ethereum'da sweep öncesi depozit, sweep `CONFIRMED` sonrası sıcak cüzdan (sayılmaz). Payout, sweep, `REFUND_FEE_SWEEP` sayılmaz. Tutar = `Amount` (alıcıya giden net); USDC 1:1.
- **Pencere:** kayan 24 saat; tavana eşit geçer, 0,01 üstü bekler; tavandan büyük tek iade kendiliğinden hiç geçmez.
- **Bekleme:** `PENDING`, `RetryCount` değişmez, `NextAttemptAt` +15 dk, `ErrorMessage` kodlu; ilk beklemede `DepositRefundLimitHeldAt` + admin bildirimi (bir kez); Grafana kuralı bekleyen satır kaldıkça.
- **Onay kapısıyla (T169):** sıra kapı → tavan. Onay bekleyen satır tartılmaz (alarm üretmez). **Onaylanan iade tavana takılırsa bekler** — onay tavanı kaldırmaz. Admin kararı ayrı: AD42 (audit'li); serbest bırakılan satır sonraki turda gider (kapı yine geçerli) ve toplama sayılır.
- **Eşzamanlılık:** tartı + damga tek işlemde, ağın `DepositRefundLimitLocks` satırı `UPDATE` ile kilitli (işlem bitene kadar; öteki ağ bağımsız); damga **yayından önce** commit — ikinci dağıtıcı yolda olan iadeyi kullanılmış sayar. Hangfire `[DisableConcurrentExecution]` ilk katman. Ham SQL kullanılmadı (`NoRawSqlConventionTests`; ilk deneme `sp_getapplock` bu testte kırıldı, kilit tablosuna geçildi). Kabul edilmiş satırın yeniden denemesi kendini iki kez saymaz, yer açmak için damgası silinmez.

---

## Etkilenen Modüller / Dosyalar

- **sidecar-blockchain:** `src/fee/FeeEstimationService.ts` (+ test), `src/api/feeHandlers.test.ts` (fixture alanları)
- **sidecar-ethereum:** `src/fee/FeeEstimationService.ts` (+ test), `src/api/feeHandlers.test.ts`, `contracts/estimate-fee/response.json`
- **Backend yeni:** `Skinora.Transactions/Application/Transfers/DepositRefundDailyLimit.cs`, `IDepositRefundLimitObserver.cs`, `Domain/Entities/DepositRefundLimitLock.cs`, `Infrastructure/Persistence/DepositRefundLimitLockConfiguration.cs`, `Skinora.Shared/Events/DepositRefundLimitHeldEvent.cs`, `Skinora.Notifications/Application/EventHandlers/DepositRefundLimitHeldAdminNotificationConsumer.cs`, `Skinora.API/Monitoring/PrometheusDepositRefundLimitObserver.cs`, migration `20261022214930_T169a_DepositRefundDailyLimit(.Designer).cs`
- **Backend değişen:** `OutgoingTransferDispatchJob.cs`, `BlockchainTransaction.cs`, `BlockchainTransactionConfiguration.cs`, `BlockchainNetworkSettingKeys.cs`, `BlockchainNetworkSettingsProvider.cs`, `SystemSettingSeed.cs`, `SystemSettingsCatalog.cs`, `AuditAction.cs`, `AuditLogCategoryMap.cs`, `OutboundApprovalService.cs`, `AdminOutboundApprovalsController.cs`, `TransactionsModule.cs`, `AppDbContextModelSnapshot.cs`
- **Backend test:** yeni `Skinora.API.Tests/Integration/NetworkDimension/DepositRefundDailyLimitTests.cs`, `Unit/Monitoring/PrometheusDepositRefundLimitObserverTests.cs`; değişen `OutgoingTransferDispatchJobTests.cs` (+11), `AdminOutboundApprovalsEndpointTests.cs` (+AD42), `AdminWalletsEndpointTests.cs` (fake), `AdminAlertNotificationConsumerTests.cs` (+1), `EnumTests.cs`, `AuditLogCategoryMapTests.cs`, `SeedDataTests.cs`, `NetworkContractTests.cs`, `NetworkFeePolicyMigrationRehearsalTests.cs`, `BlockchainWebhookEndpointTests.cs` (yedek 2,80), `NetworkQueueIsolationTests.cs` / `OutboundApprovalTests.cs` / `OutboundRowClosureTests.cs` (kurucu)
- **Frontend:** `components/admin/OutboundApprovalPanel.tsx` (+ test), `lib/api/admin.ts`, `lib/hooks/useOutboundApprovals.ts`, `types/enums.ts`, `i18n/messages/{tr,en,es,zh}.json`, `i18n/catalog-parity.test.ts`, `components/legal/legal.test.tsx`
- **Altyapı:** `infra/grafana/provisioning/alerting/rules.yml` (`deposit-refund-daily-limit-held`), `e2e/src/db.ts`
- **Belgeler:** 02 v4.21 (§4.6, §4.7), 05 v3.34 (§3.3), 06 v6.38 (§2.19, §3.8, §3.17, yeni §3.26), 07 v4.24 (§9.35 AD40/AD42, §9 izin tablosu), 08 v3.46 (§3.1a, yeni §3.3c, §3.4, §3.7.7), DEPLOY_RUNBOOK (§J.2, yeni §M.4), `plan/11_F9.md` v1.26 ("T169a kararları"), DOC_CHANGELOG, DEFERRED_BACKLOG, IMPLEMENTATION_STATUS, STATUS_CHANGELOG, `.claude/memory/MEMORY.md`, bu rapor

---

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Reddedilen ödeme ve iptal iadelerinde kesilen tutar platformun gerçek harcamasını (token gas + Ethereum yakıt değeri ve ücreti + TRON enerji/TRX) kapsar | ✓ | TRON `FeeEstimationService.test.ts` "T169a refund charge = the platform spend of the broadcast" (8 test: Nile 6 2.711.500 SUN → 0,90; ana ağ 8.441.350 → 2,80 ve 15.701.350 → 5,21; takviye bandı 270/269 bayt; depozit bakiyesi düşülür; bakiye yeterse 0; devretme yolunda yalnız bant takviyesi; payout değişmedi) + güncellenen refund-path testleri (sahibin payı artık kesintiyi azaltmaz). Ethereum `FeeEstimationService.test.ts` "T169a the fuel actually sent" (7 test: bakiye düşülür, bakiye bütçeyi karşılarsa 0, 1 wei eksikte 1 wei + ücret, T159b çifti 0,25 → 0, Sepolia ilk iade 0,68, sıcak cüzdan değişmedi, bakiye okunamazsa tahmin başarısız). Kesinti tüm iade türlerinde aynı çözücüden (T159a) — türden bağımsız |
| 2 | Depozitten iadeye ağ başına günlük 100 $ tavanı, aşan iade bekler ve alarm verir | ✓ | Birim (`OutgoingTransferDispatchJobTests` T169a, 11 test, SQLite): 30+35 geçer, 20 bekler + tek alarm + 15 dk; tavana eşit geçer / 0,01 bekler; kayan pencere (24 sa − 1 dk hâlâ bekler, pencereden çıkınca gider); tavandan büyük iade 2 gün sonra da gitmez; AD42 serbest bırakması gider ve sayılır; onay kapısı önce, onay tavanı kaldırmaz; kabul edilmiş satırın yeniden denemesi kendini saymaz; `FAILED` yer açar; payout / sweep / sıcak cüzdan iadesi sayılmaz; ağ başına; bekleyen satır takılma kuyruğunda yok. Entegrasyon (`DepositRefundDailyLimitTests`, LocalDB + migration, 9 test): seed 100 / 100 ve yedek 2,80; bozuk satır 100'e düşer; seed tavanla 60 geçer 50 bekler → outbox olayı → AD40 listeler → AD42 audit → gider (toplam 110); AD42 retleri; **yayında olan iade ikinci dağıtıcıya sayılır** (stamp-before-broadcast); **kilit satırı başka oturumdayken tartı bekler**, öteki ağın kilidi beklemez. Alarm: `PrometheusDepositRefundLimitObserverTests` (gösterge + kuralın gerçek metrik adını okuduğu), `AdminAlertNotificationConsumerTests.T169a_…`. HTTP: `AdminOutboundApprovalsEndpointTests` (AD42 401/403, 200 ve aktör = JWT, 3 ret kodu, eşleme tablosu tam) |
| 3 | Testler | ✓ | Aşağıdaki "Test Sonuçları" |
| 4 | Belgeler | ✓ | 02, 05, 06, 07, 08, DEPLOY_RUNBOOK, plan, DOC_CHANGELOG (başlık kuralına uygun tek cümle + changelog girişleri); hukuk metni (T168 main'de olduğu için güncellendi) |

Görev talimatındaki ek noktalar: yeni toplama göre yedek değerler gözden geçirildi (TRON 2,80 ⚠, Ethereum 0,20 kaldı); küçük iade eşiği aynı tutarla çalışır (✓, 08 §3.4 satırı ~8,4–15,6 $); payout kuralına dokunulmadı (TRON payout testleri ve "a payout is unchanged" ✓); kesinti üst sınırı (iade tutarını aşamaz — eşik `gas ≤ taban ÷ 3` garantiler; `max_charged_gas_fee_usdt` 10) ve görünürlük (işlem sayfası etiketi + bildirimler, 4 dil) ✓; eşzamanlılık ✓; seed + migration ✓; Grafana + admin bildirimi ✓; onay kapısıyla etkileşim ✓.

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-blockchain | ✓ 663/663 | `npx vitest run` (35 dosya; `FeeEstimationService.test.ts` 51); `npm run build`, `npm run lint` temiz; değişen dosyalar prettier temiz |
| sidecar-ethereum | ✓ 901/901 | `npx vitest run` (37 dosya; `src/fee` 65); build + lint temiz |
| Backend unit (tüm Unit filtresi — enum eklendi) | ✓ 2393/2393 | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` (main T168 birleştirmesinden sonra): Shared 572, Platform 161, Transactions 1077, API 140, Notifications 151, Auth 96, Users 49, Realtime 44, Steam 59, Disputes 25, Fraud 19 |
| Backend contract | ✓ 101/101 | `--filter "FullyQualifiedName~.Contract"` (Transactions 72, Shared 5, API 24) |
| Backend integration (dokunulan projeler, LocalDB) | ✓ 1649/1649 (API 815, Transactions 744, Platform 74, Shared 16) | `INTEGRATION_TEST_SQL_SERVER="Server=(localdb)\MSSQLLocalDB;…"`; Shared, Platform, Transactions, API — main birleştirmesinden sonra. İlk koşuda (birleştirme öncesi) 6 kırmızı: 2 migration provası sayısı (77 → 79 ayar, 9 → 10 Ethereum anahtarı) ve 4 webhook testi (yedek 2,60 → 2,80 beklenen değer) — beklenen değerler T169a'ya çekildi |
| Frontend | ✓ | `npx vitest run` 46 dosya / 466 test; `npx tsc --noEmit` temiz; `npm run lint` temiz; `npm run i18n:check` parity OK (4 × 1722); `npm run build` ✓; değişen dosyalar prettier temiz |
| e2e harness | ✓ | `e2e`: `tsc --noEmit`, prettier, eslint (`src/db.ts`) temiz; Docker kullanılmadığı için süitler yerelde koşulmadı (CI advisory) |
| Biçim | ✓ | `dotnet format Skinora.sln --verify-no-changes --severity error` (değişen dosyalar) |

**Bozma kanıtı** (her biri uygulandı, ilgili test koşuldu, `git checkout` ile geri alındı):

| # | Bozma | Sonuç |
|---|---|---|
| M1 | TRON takviyenin bandı hiç kesilmez | öldü — 3 test |
| M2 | TRON iadede eski kural (çağıranın payı − depozit enerjisi) | öldü — 8 test |
| M3 | TRON depozit bakiyesi düşülmez | öldü — 2 test |
| M4 | TRON açılışın 1 SUN'u kesilmez | öldü — 5 test (Nile 6 tam sayısı dahil) |
| M5 | TRON takviyede %10 pay yok (çıplak yakma) | öldü — 15 test |
| M6 | Ethereum depozit bakiyesi düşülmez | öldü — 4 test |
| M7 | Ethereum yakıt gerekmediğinde yine yakıt ücreti kesilir | öldü — 2 test |
| B1 | Damga yayından önce commit edilmez (yalnız bellekte, gönderim sonrası kaydedilir) | öldü — `A_Refund_Being_Broadcast_Already_Counts_For_A_Second_Dispatcher` (yayın sırasında başka oturum 70 yerine 0 okudu). İlk sürümü sağ kaldı (ikinci dağıtıcı satırı kendisi damgalıyordu) → test yayın anındaki toplamı doğrudan ölçecek şekilde güçlendirildi |
| B2 | Kilit alınmaz | öldü — `The_Check_Waits_For_Another_Session_Holding_The_Network_Lock` |
| B3 | Tavan dışlayıcı (`<`) | öldü — 2 test (tam tavan, 48,50) |
| B4 | AD42 serbest bırakması yok sayılır | öldü — `An_Admin_Release_…` |
| B5 | Sıcak cüzdan iadesi de sayılır | öldü — `Payouts_Sweeps_And_Hot_Wallet_Refunds_…` |
| B6 | `FAILED` satır toplamda kalır | öldü — `A_Failed_Refund_Frees_Its_Room` |
| B7 | Yeniden deneme kendini sayar (dışlama + kabul kısayolu yok) | öldü — `A_Retry_Of_An_Admitted_Refund_…` |
| B8 | Onay tavanı kaldırır | öldü — `The_Approval_Gate_Comes_First_…` |
| B9 | Bekleyen satır takılma kuyruğunda kalır | öldü — `A_Held_Refund_Is_Not_In_The_Stall_Queue` |
| B10 | Her beklemede alarm (bir kez değil) | öldü — 2 test |

---

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (yönetici düzeni — bağımsız doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- **Migration: Var** — `20261022214930_T169a_DepositRefundDailyLimit`: `BlockchainTransactions` +4 NULL kolon; yeni tablo `DepositRefundLimitLocks` (+ `TRON`, `ETHEREUM` satırları); `SystemSettings` +2 satır (seed 81–82, 100); `blockchain.refund_gas_fee_estimate_usdt` açıklaması + değeri yalnız hâlâ `2.60` ise `2.80` (Down tersi aynı koşulla). Toplam 61 migration.
- **Config/env:** yok (yeni ayarlar seed'li, env'den doldurulmaz).
- **Docker:** yok. **İzleme:** yeni göstergeler `skinora_backend_deposit_refund_limit_{held_rows,used_usdt,limit_usdt}`, yeni Grafana kuralı `deposit-refund-daily-limit-held` (warning).
- **Sözleşme:** TRON `estimate-fee` cevabı += `topUpSun`, `topUpFeeSun` (backend yalnız `feeUsdt` okur); Ethereum cevabı += `depositBalanceWei` (örnek dosya güncellendi; backend `feeWei` / `ethPriceUsdt` tutarlılığını aynen sınar).

## Commit & PR
- Branch: `task/T169a-iade-tam-maliyet`
- Commit: `8c97a391` — tam maliyet kesintisi (servisler); `adfb2f00` — günlük tavan + yedek 2,80 (backend, frontend, alarm); `33585d99` — main (`69c68df6`, T168 #370) birleştirmesi; __LASTCOMMITS__
- PR: #__PR__
- CI: __CI__

## Kararlar (öneriyle kaydedildi — ⚠ PARA)
1. **TRON takviyesinin bandı** sıcak cüzdanın o anki kalan bandından hesaplanır (bant ≥ 270 bayt → 0, değilse 270 × `getTransactionFee`). Sweep satırı (T149) her zaman "yakılmış" sayar; iade tahmini gönderimden dakikalar önce alındığı için canlı okuma gerçeğe daha yakın (Nile 6'da takviyenin ücreti 0 ölçüldü). Ana ağda günde 5–20 satışta ücretsiz bant erken biter → kesinti çoğunlukla bandı içerir.
2. **Ethereum'da depozitin ETH'si düşülür** (imzacının yakıtı gibi). Sahip kararı "depozite gönderilen yakıt" diyor; gönderilmeyen yakıt kesilmez. Aynı depozitten ikinci iade artık fazla kesilmez.
3. **TRON iade yedeği 2,60 → 2,80** (yeni tanımın ana ağ alt kolu: yeni depozit + USDT tutan alıcı, 8,44 TRX × 0,3313); **Ethereum 0,20 kaldı** (ana ağ tam maliyeti ~0,05–0,08). min(ölçülen, sabit) kuralı değişmedi (alıcı yedekte ayardan fazla ödemez).
4. **Kayan 24 saat** (gece yarısı sıfırlaması yok; servislerin sıcak cüzdan tavanıyla aynı).
5. **Tutar = `Amount`** (alıcıya giden net); USDC 1:1; tavana eşit geçer.
6. **Onay tavanı kaldırmaz**; tavanın üstüne çıkmak ayrı ve audit'li karar (AD42); serbest bırakılan satır toplama **sayılır**.
7. **Kilit tablosu** (`DepositRefundLimitLocks`) — `SystemSetting` satırını kilitlemek `RowVersion`'ı değiştirip admin'in eşzamanlı ayar kaydını bozardı; ham SQL yasak.
8. **15 dk yeniden tartı**; bildirim satır başına bir kez; bekleyen satır takılma alarmlarına sayılmaz.
9. **Bildirim kanalı:** mevcut `ADMIN_PAYMENT_FAILURE` + hata kodu (yeni bildirim türü / şablon yok — `SELLER_PAYOUT_DEFERRED` deseni).
10. **Alıcıya görünen etiket** "Gas fee" → "Ağ ücreti (iadenin zincir maliyeti)" (4 dil); Sözleşme "İadeler" maddesi (4 dil), ToS sürümü artırılmadı (T168 kararı).

## Açık Sorular (bloklamayan)
1. **Tavan backend'de** — ele geçirilmiş bir backend'e karşı koruma değildir (backlog'un 2026-09-17'deki ilk sorusu buydu). Servis tarafında sayım (Ethereum günlüğü var, TRON'da kalıcı defter yok) ayrı iş: ⚪ `DepositRefundLimitBackendOnly`. Gerçek ağdan önce karar gerekir mi?
2. **100 $'dan büyük tek iade her zaman admin kararı ister** (satış 10–2.900 $; 100 $ üstü her iptal iadesi). Test döneminde iadeler zaten onaylı olduğu için admin iki kez tıklar (onay + AD42). "Onay = tavanı aşma izni" istenirse küçük bir değişiklik — sahibe sorulabilir.
3. **Sepolia'da yedek:** Ethereum iade yedeği 0,20 Sepolia'nın tam maliyetinin (~0,7–0,9) altında; tahmin 3 denemede de alınamazsa deneme yayınında fark platformda kalır (test parası, `gas-fee-fallback-charged` alarmlı). Deneme yayını DB'sinde ayar yükseltilsin mi?

## Known Limitations / Follow-up
- **Tahmin ile gönderim arası:** kesinti kuyruğa alma anında sabitlenir; T169 onay kapısıyla gönderim saatler sonra olabilir — o arada TRX/ETH fiyatı, sıcak cüzdanın bandı ya da depozit bakiyesi değişirse fark platformda (önceden de böyleydi).
- **Kalıntı ikinci kez kullanılabilir:** iadenin takviyesinden/yakıtından depozitte kalan TRX/ETH, aynı depozitten sonraki bir gönderimin (ör. sweep) ihtiyacını azaltır; o gönderimin ücreti (TRON ağ ücreti satırı) kabul anında tam fiyatlandığı için platform küçük bir artı yapar (alıcı aleyhine, iade başı ~0,1–0,5 $; TRON'da yalnız aynı depozitten iade + sweep olan satışlarda).
- **Ethereum "ayrılmış" ETH:** imzacı, depozitin yolda olan başka gönderimlerine ayrılan ETH'yi düşer (`reservedOn`); tahmin düşmez (aynı depozitten eşzamanlı iki gönderim nadir; olursa kesinti biraz düşük kalır).
- **Devretme yolu (uyuyan kilit, `ENERGY_STAKING_ENABLED=false`):** kilit hesabının devret/geri al bandı (~0,28 TRX) kesintiye girmez.
- **Hukuk metnindeki "Ücret gönderimden hemen önce hesaplanır"** (T168) onay kapısı açıkken tam doğru değil (kuyruğa alma anında hesaplanır); bu görevde değiştirilmedi — T168 metni kontrol edilmeli.
- **Göstergeler gönderim işiyle güncellenir:** iş çalışmazsa (Hangfire durursa) `held_rows` son değerinde kalır.
- **Seed açıklamaları** (T160b'nin `SystemSettingSeed` satırları 50/64/68/70'teki "runtime tahmin alınamazsa" cümleleri) yalnız satır 50 için güncellendi.

## Notlar
- **Working tree:** temiz (dal `origin/main` `21c93062`'den açık).
- **Main CI startup check:** son 3 tamamlanmış run `success` — Docker Publish `39850436112` (T169 #371), Docker Publish `39844542864` (T163 #369), CI `39844542606` (T163 #369); T169'un CI'si `39850436226` başlangıçta sürüyordu, sonra ✓.
- **main birleştirmesi:** yapım sırasında T168 (#370, `69c68df6`) main'e girdi; dala merge commit `33585d99` ile alındı — çakışma yok (i18n dosyaları kendiliğinden birleşti). Belge başlıkları main'in sürümlerinin bir üstüne alındı (02 v4.21, 07 v4.24, 11_F9 v1.26).
- **Devir kaydı:** STATUS_CHANGELOG'a merge teyitleri: T169 (#371) `21c93062` → CI `39850436226` ✓, Docker Publish `39850436112` ✓; T168 (#370) `69c68df6` → CI `39854740137` ✓, Docker Publish `39854740205` ✓.
- **Dış varsayımlar (kanıtlı):**
  1. SQL Server'da `UPDATE`'in satır kilidi işlem bitene kadar tutulur ve aynı satırı güncellemek isteyen ikinci oturumu bekletir; EF `ExecuteUpdateAsync` açık işleme katılır → `DepositRefundDailyLimitTests.The_Check_Waits_For_Another_Session_Holding_The_Network_Lock` (LocalDB: kilit tutulurken gönderim 2 sn içinde bitmedi, commit sonrası bitti) ve `Another_Networks_Lock_Does_Not_Block_This_One`.
  2. `EthereumRpcClient.getBalance(address, blockNumber)` var ve imzacı zaten kullanıyor (`TransferService` yakıt kararı) → derleme + `wiring.ts`.
  3. TRON `getaccount` bakiyesi (`TronResourceClient.getAccountState().balanceSun`) tahminde zaten okunuyordu (açılış kararı için) → ek prob yok.
  4. `NoRawSqlConventionTests` üretim kodunda `ExecuteSql*` / `FromSql*`'i yasaklar → `sp_getapplock` taslağı bu testte kırıldı, kilit tablosuna geçildi (yeni bir dış bağımlılık yok).
- **Docker kullanılmadı** (talimat); integration LocalDB `(localdb)\MSSQLLocalDB`.
- **Yeni dış bağımlılık:** yok.
