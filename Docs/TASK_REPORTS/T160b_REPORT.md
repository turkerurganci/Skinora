# T160b — İade gas tahmininde yeniden deneme (⚠ PARA)

**Faz:** F9 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-10-21

---

## Yapılan İşler

- **Tahmin denemesinin sonucu artık türüyle dönüyor.** `IGasFeeEstimator.EstimateFeeAsync` → `GasFeeEstimateAttempt(FeeUsdt, Retryable, Failure)`. `HttpSidecarGasFeeEstimator` hatayı sınıflıyor: servisin zarfındaki `retryable` bayrağı varsa o; yoksa 5xx / 408 / 429 yeniden denenebilir; bağlantı hatası ve istemcinin kendi zaman aşımı yeniden denenebilir; 4xx, ayrıştırılamayan / negatif cevap, wei'den çıkmayan Ethereum cevabı ve çağıranın kendi iptali **yeniden denenmez**. Eski `EstimateFeeUsdtAsync` aynı davranışla duruyor (sarmalayıcı).
- **İade ailesinde sınırlı yeniden deneme (iki ağ).** `ChargedGasFeeResolver` iade ve sıcak cüzdan iadesi tahminini `GasFeeEstimateRetryPolicy.Default` ile soruyor: **en çok 3 çağrı, 1 sn ve 3 sn arayla, ilk çağrıdan itibaren 12 sn bütçe**. Yeniden deneme yalnız bütçe bitmeden başlayabiliyorsa başlar ve bütçe sonunda iptal edilir. İlk çağrı bütçeyi tükettiyse (30 sn okuma zaman aşımı) yeniden denenmez → çağıranın en kötü beklemesi T160b öncesiyle aynı (bir istemci zaman aşımı).
- **Yedek değer ölçülen tarifeye yakın.** Yine tahmin yoksa iade, **aynı ağ + gönderen (depozit / sıcak cüzdan) + token için son kesilen tahmini** kesiyor (`MeasuredGasFeeCache`, en çok 6 saat eski, süreç belleğinde; `GasFeeSource.MeasuredFallback`). Yoksa eskisi gibi ağın sabit ayarı. Tavanı aşan tahmin ölçüm sayılmıyor. Payout tahmini sıcak cüzdan kaydını da besliyor.
- **Alarm.** Her yedek (iade, sıcak cüzdan iadesi, payout; sebep `unavailable` / `rejected` / `no_sender`; dayanak `measured` / `static`) `IGasFeeFallbackObserver`'a bildiriliyor → `skinora_backend_gas_fee_fallback_charged_total{network,flow,reason,basis}` sayacı (bütün seriler açılışta 0 ile yayımlanıyor, `increase()` ilk olayı görsün diye) → yeni Grafana kuralı **`gas-fee-fallback-charged`** (warning, 1 sa pencere).
- **Payout değişmedi:** tek çağrı + sabit payout ayarı (satıcının split kuralı, 02 §4.7). Yalnız alarma bağlandı.
- **T159b "block not found" testle yeniden üretildi.** Servisin T159b'de iki kez verdiği cevap ortak örnek dosya oldu: `sidecar-ethereum/contracts/estimate-fee/error.block-not-found.json`. Servisin handler testi `RpcError('eth_estimateGas: block not found: 0xb67b27')`'den bu gövdeyi **aynen** üretiyor (502); backend'in sözleşme testi bu gövdeyi oynatıyor → ilk çağrı `Retryable`, ikinci çağrıdaki 0,09 kesiliyor (T160b öncesi aynı durumda 0,50 yedek kesilirdi).
- **Belgeler:** 08 §3.1a yeni paragraf "Geçici hatada yeniden deneme ve yedek (T160b)" (v3.42), 02 §4.7 "Gas fee tutarının kaynağı" satırı (v4.15), 05 §3.3a estimate-fee maddesi (v3.28), DEPLOY_RUNBOOK §J.2 alarm satırı + §J.7 madde 5, DEFERRED_BACKLOG `RefundGasEstimateSingleAttempt` ✅ (50 aktif / 157 çözülmüş), DOC_CHANGELOG.

## Etkilenen Modüller / Dosyalar

- `backend/src/Modules/Skinora.Transactions/Application/GasFee/`: `IGasFeeEstimator.cs` (yeni `EstimateFeeAsync` + `GasFeeEstimateAttempt`), `HttpSidecarGasFeeEstimator.cs`, `ChargedGasFeeResolver.cs`, `IChargedGasFeeResolver.cs` (`GasFeeSource.MeasuredFallback`), yeni `GasFeeEstimateRetryPolicy.cs`, `MeasuredGasFeeCache.cs`, `IGasFeeFallbackObserver.cs`
- `backend/src/Modules/Skinora.Transactions/Application/Transfers/PaymentRefundToBuyerConsumer.cs` (yalnız yorum)
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` (DI: politika, ölçüm önbelleği, gözlemci), yeni `backend/src/Skinora.API/Monitoring/PrometheusGasFeeFallbackObserver.cs`
- `infra/grafana/provisioning/alerting/rules.yml` (`gas-fee-fallback-charged`)
- `sidecar-ethereum/contracts/estimate-fee/error.block-not-found.json` (yeni örnek), `sidecar-ethereum/src/api/feeHandlers.test.ts` (yalnız test; servis kodu değişmedi)
- Testler: `ChargedGasFeeResolverTests.cs`, `HttpSidecarGasFeeEstimatorTests.cs`, `EthereumFeeWeiContractTests.cs`, `BlockchainSidecarEstimateFeeContractTests.cs` (kurulum), yeni `Helpers/ChargedGasFeeResolverFactory.cs`, yeni `Skinora.API.Tests/Unit/Monitoring/PrometheusGasFeeFallbackObserverTests.cs`
- Belgeler: `Docs/08_INTEGRATION_SPEC.md`, `Docs/02_PRODUCT_REQUIREMENTS.md`, `Docs/05_TECHNICAL_ARCHITECTURE.md`, `Docs/DEPLOY_RUNBOOK.md`, `Docs/DEFERRED_BACKLOG.md`, `Docs/DOC_CHANGELOG.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | İade fiyatlamasında gas tahmini geçici hatada (`retryable` / ağ hatası) sınırlı yeniden denenir | ✓ | `ChargedGasFeeResolver.EstimateAsync` + `GasFeeEstimateRetryPolicy.Default` (3 çağrı, 1 sn + 3 sn, 12 sn). Testler: `T160b_A_Retryable_Failure_Is_Asked_Again_And_The_Estimate_Is_Charged` (Ethereum), `T160b_Tron_Refund_Is_Retried_The_Same_Way` (TRON: bağlantı hatası + 500), `T160b_The_Hot_Wallet_Refund_Is_Retried_Too`, `T160b_A_Final_Failure_Is_Not_Retried`, `T160b_Gives_Up_After_The_Last_Attempt_And_The_Alarm_Is_Told`, `T160b_The_Production_Policy_Waits_1s_Then_3s_Between_Calls`, `T160b_A_First_Attempt_That_Used_The_Budget_Is_Not_Retried`, `T160b_A_Retry_Still_Running_At_The_Budget_Is_Cancelled_And_The_Fallback_Charged` (12. sn'de iptal), `T160b_The_Callers_Cancellation_Still_Propagates`; sınıflama: `T160b_A_Non200_Is_Retryable_When_The_Service_Says_So_Or_The_Status_Is_An_Outage` (9 durum), `T160b_A_Transport_Error_And_The_Clients_Own_Timeout_Are_Retryable`, `T160b_The_Callers_Cancellation_Is_Not_Retryable`, `T160b_An_Answer_That_Cannot_Be_Believed_Is_Not_Retryable` |
| 2 | Sabit yedeğe düşülürse yedek değer ölçülen ağ tarifesine yakın ve alarmlı | ✓ | Yedek = aynı ağ/gönderen/token için son kesilen tahmin (≤ 6 sa), yoksa ayar (`ChargedGasFeeResolver.Fallback`, `MeasuredGasFeeCache`). Testler: `T160b_The_Refund_Fallback_Charges_The_Last_Measured_Estimate_While_Fresh` (0,06 ≠ ayar 0,50), `T160b_A_Measurement_Older_Than_Six_Hours_Is_Not_Used`, `T160b_A_Measurement_Applies_Only_To_Its_Network_Sender_And_Token`, `T160b_A_Refused_Estimate_Is_Never_A_Measurement`, `T160b_A_Payout_Estimate_Prices_A_Hot_Wallet_Refund_Fallback_But_The_Payout_Fallback_Stays_Static`, `T160b_A_Refund_Without_A_Deposit_Address_Charges_The_Measured_Deposit_Refund_And_Is_Reported`. Alarm: `PrometheusGasFeeFallbackObserverTests` (3 test: seriler 0'da var, artış doğru seride, kural sayacın gerçek adını okuyor, `gt 0`, warning). Soğuk önbellekte (açılış, 6 sa'ten uzun kesinti) ayar kesilir ve `basis="static"` ile alarm çalar — bkz. Known Limitations |
| 3 | T159b'deki "block not found" durumu testle yeniden üretilip kapandı | ✓ | Ortak örnek `sidecar-ethereum/contracts/estimate-fee/error.block-not-found.json`. Servis: `feeHandlers.test.ts` "T160b — T159b's lagging node … → 502 retryable, exactly the body the backend's retry test replays" (`toEqual(example(...))`). Backend: `EthereumFeeWeiContractTests.T160b_The_T159b_Block_Not_Found_Answer_Is_Retryable` + `T160b_After_Block_Not_Found_The_Refund_Is_Asked_Again_And_The_Measured_Fee_Charged_Not_The_Fallback` (2 çağrı, 0,09 `RuntimeEstimate`, ≠ 0,50) |
| 4 | DEFERRED_BACKLOG `RefundGasEstimateSingleAttempt` kapandı | ✓ | Satır `✅ … ÇÖZÜLDÜ → T160b`; dosyanın kendi `awk`'i: 51 → **50 aktif**, 156 → **157 çözülmüş**; üstte T160b "Durum" satırı |

İki ağ birlikte: yeniden deneme ve yedek ağdan bağımsız tek çözücüde (`ChargedGasFeeResolver`); TRON ve Ethereum testleri ayrı ayrı var; ölçüm anahtarı ağı içerir (bir ağın ölçümü diğerine kesilmez — test 2. satırda).

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit + Contract (Transactions.Tests) | ✓ 1030/1030 | `dotnet test tests/Skinora.Transactions.Tests --filter "FullyQualifiedName!~.Integration"` (T160b yeni: 31 test vakası; 5 kez tekrarlandı, kararsızlık yok) |
| Unit (API.Tests) | ✓ 143/143 | `dotnet test tests/Skinora.API.Tests --filter "FullyQualifiedName!~.Integration"` (yeni: `PrometheusGasFeeFallbackObserverTests` 3) |
| Unit (çözüm geneli, CI filtresi) | ✓ Transactions/API/Shared/Platform/Auth/Users/Steam/Disputes/Fraud yeşil | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"`; Notifications'ta 16 test yerelde Docker'a bağlanamadığı için düştü (Testcontainers, T160b'yle ilgisiz; CI'da koşar) |
| Integration | ? yerelde koşulamadı | Docker Desktop bu makinede kapalı (`npipe://./pipe/docker_engine` yok); CI "Integration" bacağı yetkili — sonuç aşağıda CI satırında |
| sidecar-ethereum | ✓ 869/869 | `npx vitest run` (36 dosya); `npm run build` ✓, `npm run lint` ✓ |
| Biçim | ✓ | `dotnet format Skinora.sln --verify-no-changes --severity error` temiz |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (yönetici düzeni — ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env değişikliği: Yok (politika ve 6 sa süresi kod sabiti)
- Docker değişikliği: Yok
- İzleme: yeni metrik `skinora_backend_gas_fee_fallback_charged_total`, yeni Grafana kuralı `gas-fee-fallback-charged` (warning)

## Commit & PR

- Branch: `task/T160b-iade-gas-yeniden-deneme`
- Commit: `44d2a88d` — T160b: iade gas tahmininde yeniden deneme + ölçülen yedek + alarm (kod, test, belge); rapor/status/memory ayrı commit
- PR: #364
- CI: sonuç yöneticiye son mesajda (run ID ile)

## Known Limitations / Follow-up

- **Ölçüm bellekte:** backend yeniden başlarsa ilk başarılı tahmine kadar sabit ayar kesilir (alarm `basis="static"` ile çalar). Günde 5–20 satışta bir depozit iadesinin 6 saat içinde ölçümü sık olmayabilir; ölçüm yoksa davranış T160b öncesi gibidir (ayar) ama artık alarmlıdır.
- **Ölçüm anahtarında alıcının durumu yok:** alıcının token tutup tutmaması (TRON ~64k / ~130k Energy, Ethereum ~47k / ~70k gas) ölçülen değerin ~2 kata kadar sapmasına yol açabilir.
- **Sabit ayarların değerleri değişmedi** (Ethereum iade 0,50 = işlem başı ücret tavanı; TRON 2,00). Ana ağda Ethereum iadesi bugün ~0,05–0,08 $; ayarı düşürmek platformun üstlendiği riski değiştiren bir değer kararıdır — T169 "kalan sahip kararları"nda bakılması önerilir.
- **Ayar seed açıklamaları** (`SystemSettingSeed`: "runtime tahmin alınamazsa kesilir") yeni sırayı (önce ölçüm) anmıyor; değiştirmek migration ister, yapılmadı (`FeeCapSeedDescriptionStale` ile aynı aile).
- **Bekleme etkisi:** bir iade tahmini hızlı hata verip yeniden denendiğinde outbox dağıtıcısı ya da ödeme webhook'u en çok ~12 sn daha bekler (TRON izleme turu ve Ethereum taraması o sürede durur). İlk çağrı zaman aşımına düşerse yeniden deneme yok (en kötü durum değişmedi).
- **Servis tarafında revert de `retryable: true`:** Ethereum servisi gerçek revert'ü de 502 `FEE_ESTIMATE_SIMULATION_FAILED` olarak döndürüyor (08 §3.7.7 md. 1); backend onu da 12 sn içinde 3 kez sorar (zararsız, sınırlı). Sınıflamayı servisde ayırmak sözleşme değişikliği olurdu, yapılmadı.
- **Payout tek denemede:** payout split'i (TRON'da satıcı aşan kısmı öder) T160b dışında; yalnız alarma bağlandı.

## Notlar

- **Working tree:** temiz (dal `origin/main` `fc3e8840`'tan açıktı).
- **Main CI startup check:** son 3 tamamlanmış run `success` — CI `39546534823` (#362), Docker Publish `39546534805` (#362), CI `39545306202` (#361).
- **Bağımlılık:** T160b'nin plan bağımlılığı yok (F8 ✓ `phase/F8-pass`).
- **Dış varsayımlar (kanıtlı):**
  1. .NET 9'da `Task.Delay(TimeSpan, TimeProvider, CancellationToken)` ve `new CancellationTokenSource(TimeSpan, TimeProvider)` var → çözüm derlendi, `FakeTimeProvider` testleri geçti.
  2. İki servisin hata zarfı `{ error, message, retryable }` ve `retryable ? 502 : 400` → `sidecar-ethereum/src/api/feeHandlers.ts` (76–81), `sidecar-blockchain/src/api/feeHandlers.ts` (62–74).
  3. Çağıranların süre sınırı: outbox dağıtıcısında olay başına zaman aşımı yok (`Skinora.API/Outbox/OutboxDispatcher.cs` — tek kilit altında sıralı parti); servislerin webhook isteğinde `AbortSignal`/zaman aşımı yok (`sidecar-blockchain/src/webhook/WebhookClient.ts`, `sidecar-ethereum/src/webhook/WebhookClient.ts`; Node `fetch` = undici, başlık bekleme varsayılanı 300 sn); webhook işleyicisinde açık veritabanı işlemi yok (`BlockchainWebhookHandler.cs`'te `BeginTransaction` yok). Tahmin istemcisinin zaman aşımı 30 sn (`BlockchainSidecarOptions.ResolveChainReadTimeout`, `TimeoutSeconds` 10 × 3).
  4. prometheus-net'te `WithLabels(...)` ile yaratılan çocuk seri 0 değerle yayımlanır → `Every_Series_Exists_At_Zero_Before_The_First_Fallback` testi.
- **Kararlar (öneriyle kaydedildi — ⚠):**
  1. ⚠ PARA — yeniden deneme bütçesi: 3 çağrı, 1 sn + 3 sn, 12 sn; ilk çağrıda tükenen bütçe yeniden denenmez (gerekçe `GasFeeEstimateRetryPolicy` belge yorumunda).
  2. ⚠ PARA — yedeğin kaynağı: **son ölçülen tahmin** (aynı ağ + gönderen + token, ≤ 6 sa, bellek içi), yoksa ayar. Ayar tek sayıdır, ortam ve zaman bilmez (Ethereum 0,50 ana ağın ~10 katı, Sepolia'nın altında); birkaç saat önceki ölçüm tarifeye ondan yakındır. Ayarların değerleri değişmedi.
  3. ⚠ PARA — tavanı aşan (reddedilen) iade tahmini ve depozit adresi olmayan iade de aynı yedek sırasını izler (önce ölçüm); kaynak etiketi `EstimateRejected` korunur.
  4. ⚠ PARA — payout'a yeniden deneme ve ölçülen yedek uygulanmadı (satıcı split kuralı); payout tahmini sıcak cüzdan iadesinin ölçümünü besler; payout yedeği de alarma bağlandı.
  5. ⚠ — tek yeniden deneme katmanı backend'de (servislerde ayrıca yeniden deneme yok; çift katman bütçeyi çarpardı). Servis kodu değişmedi, yalnız sözleşme örneği + test eklendi.
  6. ⚠ — alarm `gas-fee-fallback-charged` warning, 1 sa pencere, bütün akışlar (iade, sıcak cüzdan iadesi, payout).
- **Açık soru:** yok (bloklayan). Öneri: sabit ayar değerlerinin (özellikle Ethereum iade 0,50) ana ağ tarifesine göre gözden geçirilmesi T169'a.
- **Paralel dal (T160c):** `SweepQueueJob` ve sweep dosyalarına dokunulmadı.
