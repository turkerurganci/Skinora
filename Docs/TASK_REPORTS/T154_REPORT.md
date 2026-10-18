# T154 — Ethereum ücret tahmini + ETH fiyatı

**Faz:** F8 | **Durum:** ⏳ Devam ediyor (yapım bitti, bağımsız doğrulama bekliyor) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Ücret tahmini ucu (`sidecar-ethereum`)** — `POST /api/transfer/estimate-fee` artık 501 değil (`src/api/feeHandlers.ts`, `src/fee/FeeEstimationService.ts`):
  - Gövde TRON'unkiyle aynı (`network`, `fromAddress?`, `toAddress`, `amount`, `token`); `fromAddress` yoksa sıcak cüzdan, açık `null` 400. Adresler EIP-55 kuralıyla (karışık harfte sağlama), tutar en çok 6 kesir haneli pozitif ondalık, token `USDT|USDC`.
  - **Gas:** tam bu transferin (`transfer(alıcı, tutar)`, gönderen, token kontratı) `eth_estimateGas`'ı, son bloğun **açık numarasıyla**; gas fiyatı gönderilmez, depozit yakıtsız fiyatlanır. Revert → `FEE_ESTIMATE_SIMULATION_FAILED` (502, TRON gibi).
  - **Fiyat:** `eth_feeHistory(20, son blok, [50])` — sonraki bloğun base fee'si (dizinin son elemanı), blokların medyan tip'inin medyanı. Beklenen = base + tip, üst = 2 × base + tip.
  - **Kim ne öder:** sıcak cüzdan → gas × beklenen; depozit → gas × üst (yakıtın tamamı, 08 §3.7.5 — artığı toplanmaz) + 21.000 × beklenen (yakıt gönderimi).
  - **USDT:** wei × kur (mikro-USDT tamsayısı) → 2 haneye yukarı. Cevaptaki bütün tamsayılar ondalık metin (`feeWei`, `gasLimit`, `fuelGasLimit`, `baseFeePerGasWei`, `maxPriorityFeePerGasWei`, `maxFeePerGasWei`, `blockNumber`); `ethPriceUsdt` 6 haneli metin; `priceSource`, `priceAgeSeconds`, `sender`.
- **ETH/USDT kuru (`src/price/EthPriceService.ts`):** Binance `avgPrice?symbol=ETHUSDT` (5 dk ortalama, `closeTime`) + CoinGecko `simple/price` (`last_updated_at`), her yenilemede ikisi, eşzamanlı istekler tek yenilemeyi paylaşır. Geçerlilik: düz pozitif ondalık, 1–1.000.000 bandı, kaynak yaşı ≤ 10 dk. Karar: iki kaynak ≤ %2 uyuşursa birincil; uyuşmazsa ikisi de güvenilmez; tek kaynak yalnız son 24 saatte kabul edilmiş fiyata ±%20 içindeyse; kabul edilmiş fiyat yoksa (açılış) tek kaynak **yetmez**. Güvenilir kur yoksa son kabul edilen 60 dk, sonra `ETH_PRICE_UNAVAILABLE` (502). Önbellek 5 dk.
- **Tamsayı aritmetiği (`src/fee/feeMath.ts`):** `parseScaled`/`formatScaled`, `weiToUsdtCentsCeil`, `feeCapPerGasWei` (tavan ÷ (gas × kur), aşağı — T152/T153 kullanacak), `realizedFeeWei` (makbuz `gasUsed × effectiveGasPrice`), `medianOf`.
- **RPC istemcisi:** `estimateGas(tx, blok)` ve `feeHistory(n, blok, yüzdelikler)` eklendi; hex olmayan nicelik o sağlayıcının arızası sayılır (yedek sorulur), sayıya çevrilip yuvarlanmaz.
- **Backend:**
  - `Skinora.Shared.Blockchain.WeiAmount` — wei metni → `decimal` (en çok 28 hane, yalnız ASCII rakam, baştaki sıfır yok), `TryUsdtCentsCeil` servisinkiyle aynı `BigInteger` aritmetiği.
  - `HttpSidecarGasFeeEstimator` — `ReportsFeeInWei` ağında cevabın `feeUsdt`'si `feeWei × ethPriceUsdt`'den yeniden türetilir; kuruşu tutmazsa, `feeWei`/`ethPriceUsdt` yoksa ya da wei JSON sayısıysa `null` (yedek) + hata log'u. TRON yolu değişmedi.
  - `TransferStatusResult.RealizedFeeWei` (`decimal?`), `HttpBlockchainTransferClient` `realizedFeeWei` metnini okur (yanlış JSON tipi / bozuk değer → NULL + uyarı, durum yine `Confirmed`), `OutgoingTransferConfirmationJob` yalnız `ReportsFeeInWei` ağında `BlockchainTransaction.RealizedFeeWei`'e yazar.
  - `BlockchainTransaction.RealizedFeeWei` `decimal(38,0)` NULL — migration `20261018120510_T154_RealizedFeeWei` (tek nullable kolon).
  - `BlockchainNetworkPolicy.ReportsFeeInWei` (TRON false, Ethereum true).
- **Sözleşme örnekleri:** `sidecar-ethereum/contracts/estimate-fee/{payout,refund}.request.json`, `response.json` (`feeWei` = 2^53 + 1), `contracts/transfer-status/confirmed.response.json` (`realizedFeeWei` 9.007.282.507.774.707 — double …708 verir). Backend `EthereumFeeWeiContractTests` ve servisin `feeHandlers.test.ts`'i aynı dosyaları okur.
- **CI:** backend yol filtresine `sidecar-blockchain/contracts/**` ve `sidecar-ethereum/contracts/**` (yalnız örnek değişince backend yarısı da koşsun).
- **Dokümanlar:** 05 v3.19 (§3.3a "Uygulandı (T154)"), 06 v6.28 (§1.4, §3.8 `RealizedFeeWei`), 08 v3.25 (§3.7, §3.7.7 yedi madde, §3.7.8 kur kesintisi satırı), 11 v1.4 ("T154 kararları", §7.6 satırı). Altbilgi sürümleri başlıklarla hizalandı.

## Etkilenen Modüller / Dosyalar

- **sidecar-ethereum (yeni):** `src/fee/feeMath.ts`, `src/fee/FeeEstimationService.ts`, `src/price/EthPriceService.ts`, `src/api/feeHandlers.ts`, testleri (`feeMath.test.ts`, `FeeEstimationService.test.ts`, `EthPriceService.test.ts`, `feeHandlers.test.ts`, `rpc/RpcClient.fee.test.ts`), `contracts/**`.
- **sidecar-ethereum (değişen):** `src/rpc/RpcClient.ts` (iki okuma + `parseQuantity`, sona eklendi), `src/api/routes.ts` (+1 rota, `feeEstimator` bağımlılığı), `src/api/notImplemented.ts` (`estimate-fee` satırı çıktı), `src/index.ts` (servis kurulumu), `src/api/routes.test.ts`.
- **Backend:** `Skinora.Shared/Blockchain/{WeiAmount.cs (yeni), BlockchainNetworkPolicy.cs}`, `Persistence/Migrations/20261018120510_T154_RealizedFeeWei{,.Designer}.cs`, `AppDbContextModelSnapshot.cs`; `Skinora.Transactions/Application/GasFee/HttpSidecarGasFeeEstimator.cs`, `Application/Transfers/{IBlockchainTransferClient,HttpBlockchainTransferClient,OutgoingTransferConfirmationJob}.cs`, `Domain/Entities/BlockchainTransaction.cs`, `Infrastructure/Persistence/BlockchainTransactionConfiguration.cs`.
- **Backend testleri:** `Skinora.Shared.Tests/Unit/Blockchain/{WeiAmountTests.cs (yeni), BlockchainNetworkPolicyTests.cs}`, `Skinora.Transactions.Tests/Contract/EthereumFeeWeiContractTests.cs` (yeni), `Unit/Transfers/{HttpBlockchainTransferClientTests,OutgoingTransferConfirmationJobTests}.cs`.
- **Altyapı:** `.github/workflows/ci.yml` (yol filtresi).
- **Doküman:** `Docs/05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `08_INTEGRATION_SPEC.md`, `11_IMPLEMENTATION_PLAN.md`, `IMPLEMENTATION_STATUS.md`, bu rapor, `.claude/memory/MEMORY.md`.
- **Dokunulmayan:** T149'un TRON dosyaları (`NetworkFeeQuoteService`, `blockchain.tron.*`, ayar sayıları), compose/env (yeni ayar yok).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Ücret tahmini wei cinsinden, hassasiyet kaybı olmadan backend'e ulaşıyor | ✓ | Servis tüm tamsayıları ondalık metin yollar (`FeeEstimationService.test.ts` "every integer leaves as a decimal string"); örnek cevabın `feeWei`'i 2^53 + 1. Backend `EthereumFeeWeiContractTests.Example_Answer_Is_Charged_And_Its_Wei_Reaches_The_Backend_Exactly` (log'da tam `9007199254740993 wei`, ücret 21,75); kuruş ±1 ya da birim kayması → yedek (3 vaka); wei JSON sayısı → yedek; `WeiAmountTests` double'ın düşüreceği wei'yi ve kuruşunu tutar (9.010.000.000.000.001 wei × 1000 → 9,02; double 9,01) |
| 2 | Gerçekleşen ücret wei cinsinden, hassasiyet kaybı olmadan backend'e ulaşıyor | ✓ | `realizedFeeWei` metni → `RealizedFeeWei` `decimal(38,0)`: `EthereumFeeWeiContractTests.Transfer_Status_Example_...` (9.007.282.507.774.707 tam); `HttpBlockchainTransferClientTests` 10 vaka (sayı, hex, eksi, ondalık, baştaki sıfır, 29 hane → NULL, durum yine `Confirmed`; 28 hane tam); `OutgoingTransferConfirmationJobTests` Ethereum'da kaydedilir, TRON'da kaydedilmez. Ucun kendisi T152'nin (aşağı bkz. sınırlar) |
| 3 | (Yönetici) Fiyat kaynağı hata / eskime / aykırı değer, tahmin kullanılamazsa yedek, önbellek — testle ve bozmayla | ✓ | `EthPriceService.test.ts` 26 test (iki kaynak uyuşması ve %2 sınırı, tek kaynak ±%20 ve 24 sa çapası, açılışta tek kaynak reddi, bant, 10 dk yaş + tam sınır, zaman damgası yok, HTTP/HTML hatası, 5 dk önbellek, eşzamanlı tek yenileme, 60 dk bayat tolerans ve sonrası, reddedilen aykırı değerin çapayı değiştirmemesi). Yedek: servis 502 → backend'in mevcut yedek yolu (`ChargedGasFeeResolver`, değişmedi) — kontrat testinde kuruşu tutmayan cevap `null` |
| 4 | (Yönetici) Para için double yok | ✓ | Servis `bigint`, backend `decimal`/`BigInteger`; bozma S3 (kuruşu double ile hesapla) ve B2 (wei'yi double'dan geçir) yakalanıyor |
| 5 | (Yönetici) K4/K8 — Ethereum'da ağ ücretini platform karşılar, alıcıya satır yok | ✓ (değişmedi) | T154 alıcıya satır eklemez; sweep/payout'ta kimseden kesinti yapan kod eklenmedi. Tahmin yalnız iadede (02 §4.6) ve kayıt için kullanılır; tavan çevrimi yardımcı olarak hazır (`feeCapPerGasWei`, T142: 0,50 $ / 67.201 gas / 2.364,48 $ → 3.146.723.791 wei) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-ethereum (vitest) | ✓ 238/238 | `cd sidecar-ethereum && npx vitest run` — 15 dosya (yeni 5 dosya, 100 test; `routes.test.ts`te 501 listesi bir eksildi); `tsc --noEmit`, `eslint src/`, `prettier --check` temiz |
| Backend birim + kontrat | ✓ 2006/2006 | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration"` (Transactions 769, Shared 567, Platform 148, Notifications 133, API 101, Auth 92, Steam 59, Users 49, Realtime 44, Disputes 25, Fraud 19) |
| Backend entegrasyon (SQL Server) | CI'da | Yerelde Docker açılmadı; migration tek nullable kolon. CI sonucu "Commit & PR"da |
| Format | ✓ | `dotnet format Skinora.sln --verify-no-changes --severity error` exit 0 |

**Bozma denemeleri** (her biri tek değişiklik, ilgili test takımı, sonra geri alındı):

- **Servis 30 bozma / 30 yakalandı** (ilk turda 28 / 26). İlk turda kaçan iki bozma testle kapatıldı: S3 (kuruşu `Number` ile hesaplamak — mevcut 2^53+1 örneği iki yolda da 21,75 veriyordu → double'ın düşürdüğü wei'nin kuruşu değiştirdiği örnek eklendi) ve S23 (`parseQuantity` sayıyı kabul etsin — test verisi yedeğin cevabıyla aynı değerdi → ayırt edici değer). Yakalananlar: kuruş aşağı, fiyat kesme, kaynak uyuşmazlığı yok sayma, %20 sınırı, kaynak yaşı, bant, bayat tolerans sınırsız/yok, önbellek, eşzamanlı tek yenileme, zaman damgası, yakıt fiyatı, yakıt gönderimi, eski base fee, tip = en yüksek, gönderen hep sıcak cüzdan, sıcak cüzdan harf duyarlı, blok 0, `null` gönderen, 7 haneli tutar, 502 → 400, revert yedeğe, `feeHistory` bloğu, 1× base, tavan yukarı, %2 sınırı dışlayıcı, açılışta tek kaynak, çapa yaşı.
- **Backend 11 bozma / 11 yakalandı:** kuruş aşağı, wei double'dan, baştaki sıfır, yeniden türetme kontrolü kaldırıldı, sayı olarak gelen `realizedFeeWei`, iş kaydetmiyor, TRON'da da wei okunuyor, fiyat 6+ hane, 29 hane, Ethereum bayrağı kapalı, TRON'da da kaydediliyor.

## Altyapı Değişiklikleri

- Migration `T154_RealizedFeeWei` (yalnız `BlockchainTransactions.RealizedFeeWei decimal(38,0) NULL`; Down kolonu düşürür).
- Yeni env/compose ayarı yok; Ethereum servisinin `api.binance.com` ve `api.coingecko.com`'a HTTPS çıkışı gerekir (TRON servisiyle aynı).
- CI backend yol filtresi `*/contracts/**`.

## Commit & PR

- Dal: `task/T154-ethereum-ucret-tahmini` (origin/main `8a56cf1`'den)
- Kod commit'i: `3d1515b`
- PR: #348
- CI: rapor commit'inden sonraki run — yöneticiye dönüşte bildirildi.

## Known Limitations / Açık sorular

**Yöneticiye bırakılan (PARA):**
1. **Depozitten iadede kesilen ücret.** Alıcının iadesinden kesilen = yakıtın tamamı (gas × (2 × base + tip); artık depozitte kalır, 08 §3.7.5 "ücret tahminine dahildir") + yakıt gönderiminin 21.000 gas'ı. İkinci kalem TRON'daki depozit açılış ücretinin alıcıya kesilmesinin (2026-10-02 sahip kararı) eşi sayılarak eklendi. Bugünkü medyan fiyatla toplam ~0,05 $; çöküş gününde tavan (10 USDT) aşılır ve yedek (0,50) kesilir. Alternatif: yalnız beklenen fiyat (base + tip) — alıcıya daha az, platforma ~2× daha fazla kalır.
2. **Açılışta tek kaynak.** Servis yeni açılmışken (ya da 24 saattir kabul edilmiş fiyat yokken) tek kaynağın fiyatı kullanılmıyor; iki kaynak şart (en temkinli). Bedeli: o sırada CoinGecko'ya ulaşılamazsa iade yedeği kesilir, sweep/payout (tavan kur istediği için) bekler. Gevşetmek (bant içindeyse tek kaynak) yönetici kararı.
3. **Payout tahmini Ethereum'da.** T149 Ethereum'da satıcıdan payout ücreti düşmüyor (`SellerBearsPayoutFeeOverage = false`); payout tahmininin Ethereum'da hâlâ çağrılıp çağrılmayacağı T149'un merge'üne bağlı — T154 iki yolu da fiyatlar, kesintiye karar vermez.

**Sınırlar:**
- `GET /api/transfer/status/:txHash` (T152) hâlâ 501; `realizedFeeWei` alanının biçimi ve örneği burada sabitlendi, T152 aynı örneğe (`contracts/transfer-status/confirmed.response.json`) karşı test etmeli.
- Ücret tavanını gönderimde uygulayan kod T152/T153'ün; T154 yalnız çevrim fonksiyonunu ve kuru verir.
- Anahtarlı sağlayıcılarda (`eth_feeHistory` CU maliyeti) prob yapılmadı (anahtar yok, T158/T169). Alchemy belgelerine göre `eth_feeHistory` ve `eth_estimateGas` Free planda var; tahmin yalnız iade/payout başına çağrıldığı için kotaya etkisi küçük.
- Binance `api.binance.com` bazı bölgelerde erişime kapalıdır (TRON servisi aynı varsayımla çalışıyor); üretim sunucusundan erişim T158/T159 provasında doğrulanmalı.

## Notlar

- **Working tree:** temiz (dal origin/main `8a56cf1`'den kesildi).
- **Main CI startup check:** son run'lar `39124319215` CI ✓, `39124319255` Docker Publish ✓, `39114726766` CI ✓ (T150 ve T147 merge'leri).
- **Bağımlılıklar:** T142 ✓, T150 ✓.
- **Dış varsayımlar:**
  - Binance `GET /api/v3/avgPrice?symbol=ETHUSDT` anahtarsız, ağırlık 2, cevap `{mins, price, closeTime}` — canlı prob 2026-10-18 (`{"mins":5,"price":"2414.57999137","closeTime":1792324190938}`) + developers.binance.com market-data belgesi.
  - CoinGecko `simple/price?ids=ethereum&vs_currencies=usd&include_last_updated_at=true` anahtarsız — canlı prob (`{"ethereum":{"usd":2411.96,"last_updated_at":1792324142}}`); anahtarsız kullanım IP başına sınırlı, Demo planı dakikada 100 (docs.coingecko.com "common errors / rate limit"). Ücretli plan gerekmiyor: 5 dk önbellekle saatte ~12 istek.
  - `eth_estimateGas`'ın blok parametresi ve gas fiyatı yokken bakiye kontrolü yapmaması geth davranışı; `eth_feeHistory`'nin `blockCount + 1` base fee döndürmesi JSON-RPC spesifikasyonu. Canlı zincirde prob yapılmadı (anahtar yok); testler sahte RPC ile.
- **Enum değişikliği:** yok.
- **Docker:** açılmadı.
- **Paralel görevler:** T149 (#345) `HttpSidecarGasFeeEstimator`'a ayrı bir metot ve kayıt ekliyor — T154'ün değişiklikleri aynı dosyanın başka yerlerinde (yöntemin içi ve dosya sonu), çakışma çıkarsa elle birleştirilir; migration snapshot'ı da iki dalda değişiyor. T151/T152 `RpcClient.ts`, `routes.ts`, `notImplemented.ts`, `index.ts`'e dokunacak.
