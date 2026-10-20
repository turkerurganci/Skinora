# T159a — Ethereum gas tarifesine uyum (T159 B1)

**Faz:** F8 | **Durum:** ⏳ Devam ediyor — yapım bitti + sahip kararı uygulandı (2026-10-20), doğrulama bekliyor | **Tarih:** 2026-10-19 · 2026-10-20

---

## Özet

T159 Sepolia provası (PR #355) bir ⚠ PARA tasarım hatası buldu: Sepolia'nın güncel tarifesinde boş hesaba ETH **204.600** gas, daha önce token almamış adrese USDT ~**153.000** gas tutuyor; `sidecar-ethereum` yakıtı **sabit 21.000** gasla imzaladığı için her yakıt zincirde geri döndü (ücret yandı), sweep ve sweep öncesi iade `FUEL_REQUIRED`'da durdu, boş adrese USDC iadesi `FEE_ABOVE_CAP`'e takıldı.

**Yönetici kararı uygulandı: hiçbir gas sabit varsayılmaz.** Yakıtın gas'ı `eth_estimateGas` ile (yakıt cüzdanından depozite 1 wei'lik düz ETH — depozitin hesap durumu tahminin içinde), token gönderimi tahmin + %10, ücret tavanı token ve yakıtın **tahmini** gas'ına bölüşülür. Tahmin geçici olarak alınamazsa gönderim bekler (`GAS_ESTIMATE_UNAVAILABLE`, `FAILED` değil); 21.000–1.000.000 dışındaki tahmin imzalanmaz, bekler ve kritik alarm verir (`GAS_ESTIMATE_ABNORMAL`, `ethereum-gas-estimate-abnormal`). Ücret tahmini ucu (T154) ve iade kesintisi aynı kuralları kullanır. Sahte zincirde Sepolia tarifesiyle yakıt artık geri dönmüyor, sweep / iade / yanlış token iadesi / ilk kez alan satıcıya payout tamamlanıyor; mainnet tarifesinde eski testler geçiyor (yalnız tavan oranının beklenen sayıları yeni formüle çekildi). 12 bozmanın 12'si yakalandı.

**Sahip kararı uygulandı (2026-10-20, modal — ⚠ PARA):** reddedilen ödeme iadelerinde — eksik ödeme, fazla ödemenin fazlası, çoklu ödeme, yanlış token, geç ödeme — ağ ücreti **her durumda** alıcıya iade edilen tutardan kesilir, TRON ve Ethereum iki ağda da ("kodu belgeye çek"). Kod artık iade satırına `net = gelen (fazla tutarda fazlası) − gas` yazıyor ve düşülen tahmini `GasFee`'de saklıyor; tahmin ve en düşük iade eşiği mevcut olanlar (ayrıntı: "Sahip kararı uygulaması" bölümü). Dal main ile bütünleştirildi (#357 belge düzeni, #358 hafıza).

Gerçek ağa işlem gönderilmedi. Yalnız salt okuma: Sepolia ve mainnet PublicNode'a `eth_feeHistory` (madde 6 için).

---

## Yapılan İşler

### 1. Yakıt gönderimi (`TransferService.ts`, `SignerPolicy.ts`)

- **Gas limiti tahminden:** `fuelGasLimitOf` — yakıt cüzdanından depozite `{ data: '0x', value: 1 wei }` için `eth_estimateGas`, fiyatlanan `latest` blokta. Pay eklenmez: yakıtın gas'ını yalnız hedefin hesap olup olmadığı belirler ve o durum tek yönlüdür (hesap bir kez oluşunca kalır), gönderim yalnız ucuzlayabilir. Sepolia: tahmin 207.391, kullanılan 204.600 (rehearsal ölçümü) — tahmin yeterli.
- **1 wei gerekçesi:** gas'ı tutar değil hesabın oluşması belirler (değer > 0 her durumda hesap oluşturur); gerçek tutar tavan bölüşümünden sonra belli olur (döngüsellik). Düğüm yakıt cüzdanında 1 wei olduğunu denetler.
- **Yakıt zaten günlükteyse** onun kayıtlı gas limiti kullanılır (yakıt bloğa girince depozit hesap olur, yeniden tahmin 21.000 döner; bölüşüm yakıtın imzalandığı hâliyle kalmalı — bozma M12 bunu yakalıyor).
- **Yakıt cüzdanı yoksa** bölüşüm 21.000 sayar (yakıt zaten gidemez; gönderim depozitin kendi ETH'siyle ya da `FUEL_REQUIRED` beklemesiyle).
- **İmzacı:** yakıt işlemi çağıranın kararlaştırdığı gas limitini taşımalı (`FuelIntent.gasLimit`; fee bump'ta kaydın gas limiti), `21.000 ≤ gasLimit ≤ 1.100.000`. Fazla değer (`maxForSendWei`), yakıtın kendi ücret payı (`gasLimit × maxFeePerGas ≤ pay`), tek / 24 saatlik tavan (değer + `gasLimit × maxFeePerGas`), bir istek bir yakıt, idempotency — T152/T153 kuralları aynen, tahmini değerle.
- **Fee bump** kayıttaki gas limitiyle (Sepolia testinde 207.391 korunuyor).

### 2. Token gönderimi ve tavan (`FeePolicy.ts`, `feeMath.ts`)

- Token gönderimi zaten işlem başına `eth_estimateGas` kullanıyordu (T152); sabit olan **tavanın bölüşüm oranı** (46.201 / 67.201, 64.031 / 85.031) ve **imzacının 150.000 sınırıydı**. İkisi kaldırıldı:
  - `capShareWei(cap, ethUsd, ownGas, flowGas)` — tek 0,50 $ akışın tahmini gas'ına oranla bölünür. Depozit akışında token payı = tavan × token gas limiti ÷ (token + yakıt), yakıt payı = tavan × yakıt gas'ı ÷ aynı toplam; ikisi aynı gas başı tavanda, ayrı ayrı aşağı yuvarlandığı için toplamları tavanı aşmaz. Yakıtsız akışlar (payout, soğuk, sıcak cüzdan iadesi) tavanın tamamını alır (değişmedi — `(A×g)/(B×g) = A/B` tam).
  - `CAP_REFERENCE_GAS`, `CAP_TRANSFER_GAS`, `FUEL_SEND_GAS`, `capWeiPerGas`, `capWeiPerTransaction`, `fuelCapWei` kaldırıldı.
  - İmzacı sınırı `maxTokenGasLimit: 150.000` → `maxGasLimit: MAX_SIGNABLE_GAS_LIMIT = 1.100.000` (token ve yakıt için; Sepolia'da ilk USDC iadesi 169.926 × 1,1 eski sınırı aşıyordu).

### 3. Tahmin başarısız / anormal

- **`GAS_ESTIMATE_UNAVAILABLE`** (503 `deferred: true`): token gönderiminin ya da yakıtın `eth_estimateGas`'ı düğüm hatası (`header not found`, `missing trie node`, `internal error` …) ya da taşıma arızasıyla düşerse. Backend genel `deferred` yolu: satır `PENDING`, deneme hakkı harcanmaz (backend değişikliği yok). Proje sahibi kararı: `FAILED` değil. Token tahmininin **revert**'ü ret olarak kalır (`TRANSFER_SIMULATION_REVERTED` — zincirin transfer hakkındaki cevabı). `eth_call` simülasyonunun düğüm hatası eskisi gibi geçici 502 (bu "tahmin" değil; ayrıca test edildi).
- **`GAS_ESTIMATE_ABNORMAL`** (503 `deferred`): tahmin < 21.000 ya da > 1.000.000 → imza yok, hata log'u, `skinora_ethereum_gas_estimate_abnormal_total{flow,leg}` artar, Grafana kuralı `ethereum-gas-estimate-abnormal` (critical, `for: 0m`, `increase[1h] > 0`).
- İki sebep de `FEE_CAP_WAIT_REASONS`'a eklendi: 3 sa `ethereum-transfer-wait` (`kind="fee_cap"`) saatine girer; gas kıtlığı saatine (`kind="gas"`) girmez.
- Tahmin tavanı aşarsa mevcut `FEE_ABOVE_CAP` beklemesi + 3 sa alarm (değişmedi; Sepolia'nın gerçek fiyatında bu yol test edildi).

### 4. "Kendi ücretinden küçük yakıt yok" (T153 (xviii)) — uyarlama (öneriyle kaydedildi — ⚠ PARA)

Sepolia tarifesinde yakıt (207.391 gas) taşıdığı sweep'in ihtiyacından (~53.000 gas × ücret) **pahalıdır**; kural olduğu gibi kalsaydı her yeni depozit "depozitin karşıladığı ücretle imzala" koluna düşer, depozitte ETH olmadığı için **süresiz `FUEL_REQUIRED`** beklerdi — T159'daki durmanın aynısı. Uyarlama: kural yalnız depozitin bu isteğe ayrılabilir **kendi** ETH'si varken uygulanır (`ctx.available > 0`). Hiç ETH'si olmayan depozit için yakıt tek yoldur; toplam maliyeti tavan bölüşümü sınırlar ve bir uyarı log'u yazılır. Mainnet tarifesinde bu durum oluşamaz: boş depozitin eksiği en az `23.100 gas × ücret` (token gas limitinin alt sınırı), 21.000 gas'lık yakıtın ücretinden büyük — kuralın şimdiye kadar uygulandığı her durum aynen geçerli (T153 testleri değişmeden geçiyor). Depozitin kendi ETH'si varsa hesap vardır, yakıt tahmini Sepolia'da da 21.000'dir (test edildi).

### 5. Ücret tahmini ucu (T154) ve iade kesintisi (`FeeEstimationService.ts`)

- Depozit yolunda yakıt gas'ı **aynı probla** tahmin edilir (`fuelGasLimit` artık tahmin; yakıt cüzdanı adresi `wiring.ts`'ten, yoksa sıcak cüzdan — düz ETH gönderiminin gas'ı gönderene değil alıcıya bağlı).
- Depozit yolunda token gas'ı **imzacının gas limiti** (tahmin + %10): yakıtın tamamı `gasLimit × maxFeePerGas`'tan boyutlanıyor; eskiden ham tahminle fiyatlanıyordu ve yakıtın ~%10'u kesintiye girmiyordu — T154 (iii) "yakıtın tamamı" kararıyla hizalandı (⚠ PARA, öneriyle kaydedildi). Sıcak cüzdan yolu değişmedi (gas × beklenen fiyat).
- Bahşiş imzacının kuralıyla seçilir (`signingTip`: son 20 bloğun medyanlarının üst ortası, en az 0,01 gwei). Eskiden alt orta ve tabansızdı; çift sayıda blokta ya da boş bloklarda kesinti imzacının ödediğinden düşük çıkabiliyordu.
- Anormal tahmin `GAS_ESTIMATE_ABNORMAL` (502, retryable) → backend yedeği (`blockchain.ethereum.refund_gas_fee_estimate_usdt`, 0,50).
- Sözleşme örneği `contracts/estimate-fee/response.json` değişmedi (alan kümesi aynı; formül `feeWei = gasLimit × maxFee + fuelGasLimit × expected` depozit yolunda geçerli).

### 6. Sahte zincir (`testing/FakeEthereumChain.ts`)

`FakeSendChain.tariff` (`GasTariff`): düz ETH'nin yeni / var olan hesaba gas'ı (tahmin + kullanılan), ilk token alımının gas'ı. `MAINNET_GAS_TARIFF` (varsayılan — eski testler) ve `SEPOLIA_T159_GAS_TARIFF` (207.391 / 204.600; 153.000 / 152.405). Hesap varlığı EIP-161 gibi (bakiye ya da nonce; token bakiyesi hesap yapmaz). Tarifenin istediğinden düşük gas limitli işlem **gas'ı tükenip status 0 ile döner**, limitin tamamı yanar, değer taşınmaz — T159'daki yakıtın birebir modeli. `estimateGas` düz ETH'de değeri ve gönderen bakiyesini denetler; ayrı hata kuyruğu `ethEstimateFailures`.

### Sabit kalan sayılar ve gerekçeleri

| Sayı | Nerede | Gerekçe |
|---|---|---|
| 21.000 (`NATIVE_TRANSFER_GAS`) | `feeMath.ts` | Protokolün her işlem için temel gas'ı. **Gas değil, taban:** en düşük inanılır tahmin; yakıt gas limitinin alt sınırı; yakıt cüzdanı yokken (yakıt gidemez) bölüşümdeki yakıt yeri |
| %10 (`GAS_LIMIT_HEADROOM_PERCENT`) | token gönderimi | T152: tahmin ile blok arasında token durumu değişebilir; kullanılmayan gas ödenmez. Yakıtta yok (yukarıda) |
| 1.000.000 (`MAX_GAS_ESTIMATE`) | anormal tahmin | Yönetici örneği. Ölçülen en büyük tahminin (207.391) ~5 katı, bloğun (≥ 30 M) çok altında: gerçek transfer ulaşmaz, değişmiş kontrat ya da bozuk cevap ulaşır |
| 1.100.000 (`MAX_SIGNABLE_GAS_LIMIT`) | imzacı | 1.000.000 + %10 — kabul edilen en büyük tahminin gas limiti |
| 1 wei (`FUEL_ESTIMATE_PROBE_WEI`) | yakıt probu | Yukarıda §1 |
| 0,01 gwei (`MIN_PRIORITY_FEE_WEI`) | bahşiş tabanı | T152: anahtarsız bir uç `eth_maxPriorityFeePerGas`'a 0 döndü; boş blok medyanı 0. Madde 6 |
| 20 blok, %50 | bahşiş örneklemi | T142'nin etkin fiyat ölçümü |
| 46.201 / 64.031 / 67.201 / 85.031 / 21.000 (T142) | yalnız belgede ve testlerin mainnet örneklerinde | Artık **ölçüm / referans**, hesapta girdi değil (08 §3.7.5) |

---

## Madde 5 — İade türlerinde gas kesintisi (İNCELEME → sahip kararı 2026-10-20 uygulandı — ⚠ PARA)

> **Sonuç:** sahip seçenek (2)'yi seçti — ailenin tamamında `net = gelen − gas`, iki ağda. Uygulama aşağıdaki "Sahip kararı uygulaması" bölümünde. Bu bölümün geri kalanı kararın girdisi olan incelemedir (2026-10-19).

**Ne gözlendi (T159):** Ethereum'da geç ödeme ve yanlış token iadeleri **tam 10,20** gitti (gas'ı platform ödedi), sweep öncesi `BUYER_REFUND`'dan **0,19** kesildi.

**Kod (iki ağda aynı — backend `AmountValidationService` + `PaymentRefundToBuyerConsumer`):**
- `BUYER_REFUND` (admin iptali, uyuşmazlık, timeout iadesi): `Amount = decision.NetRefund` (iade tabanı − gas tahmini), `GasFee` snapshot'lanır.
- **Reddedilen ödeme ailesi** — `INCORRECT_AMOUNT_REFUND` (eksik tutar), `EXCESS_REFUND` (fazla tutar ve çoklu transfer), `WRONG_TOKEN_REFUND`, `LATE_PAYMENT_REFUND`: satır `QueueRefundIntent(…, received / excess, …)` ile **brüt** yazılır, `GasFee` boş. Gas tahmini yalnız en düşük iade eşiğinde kullanılır (net < 2 × gas → iade yok, admin alarmı). Ne backend gönderim işi (`Amount: row.Amount`) ne TRON servisi (`RefundService` — "sidecar only broadcasts the pre-computed net amount") ne Ethereum servisi tutardan bir şey düşer.
- Yani **Ethereum'da TRON'a göre sapma yok**; provadaki 10,20 / 10,01 farkı kodun iki ağdaki ortak davranışıdır. 0,19'un kaynağı: (~55.000 gas × ~1 gwei + 21.000 × ~1 gwei) × ETH fiyatı — Sepolia'nın 1 gwei bahşişi (madde 6).

**Belgeler:**
- 02 §4.6 "Gas fee: İade işleminin gas fee'si iade tutarından düşülür (alıcı karşılar)" — tablo İade Politikası (kabul edilmiş ödemenin iadesi odaklı).
- 02 §4.7 "İade gas fee'leri: İade tutarından düşülür (alıcı karşılar …)" ve 05 §3.3 "İade gas fee: İade tutarından düşülür" — genel.
- **08 §3.4 "Tutar doğrulama" tablosu açık:** "Eksik tutar: Red → iade (**gas fee düşülerek**)", "Fazla tutar: … fazla iade (**gas fee düşülerek**)". Yanlış token ve geç ödeme satırlarında gas ifadesi yok.
- 02 §4.4: "Eksik tutar: … gelen tutar iade edilir"; "Timeout sonrası gecikmeli ödeme: … gelen ödeme alıcıya otomatik iade edilir"; kod yorumu (çoklu ödeme) "refunded in full" diyerek 02 §4.4'e atıf yapıyor.
- 08 §3.7.7 (Ethereum): "İade: iadenin **ölçülen** ağ ücreti iade tutarından düşülür (02 §4.6)".
- Geçmiş: T110 raporu "refund row gross excess saklar, gas broadcast'ta düşer" diyor — **yanlış**, hiçbir katman düşmüyor. WP2 raporu bu yolu "Gross-amount `QueueRefundIntent` (T72-dönemi, ayrı senaryo); kapsam dışı" diye bilerek dışarıda bırakmış. Belgelerde brüt iade kararı yok.

**Sonuç:** belge ile kod çelişiyor — en açık hâliyle eksik ve fazla tutar satırlarında (08 §3.4), genel olarak bütün reddedilen ödeme ailesinde (02 §4.7, 05 §3.3, 08 §3.7.7). Ama: (a) iki ağı birden etkiliyor (TRON parası, bu görevin kapsamı Ethereum gas tarifesi); (b) yanlış token ve geç ödeme için kural belirsiz (02 §4.4 "gelen tutar/ödeme iade edilir"); (c) 2026-05'ten beri bu davranışla geçen e2e testleri (T110 §5.2 brüt fazla tutar) ve bildirim tutarları değişir. **Düzeltilmedi, sahip kararına bırakıldı.** Para etkisi: TRON'da her reddedilen ödeme iadesinin gas'ı (enerji yakma ~6,4–13 TRX + gerekirse 1,1 TRX açılış, 0,34 $/TRX ile ≈ 2,5–5 $ — 08 §3.1a) bugün platformdan çıkıyor; Ethereum mainnet'te ~0,02–0,05 $.
Seçenekler: (1) belgeyi koda çekmek — reddedilen ödemeler brüt iade edilir, gas platformda (en düşük eşik toz koruması olarak kalır); (2) kodu belgeye çekmek — ailenin tamamında net = gelen − gas tahmini, `GasFee` snapshot (BUYER_REFUND gibi); (3) karma — eksik/fazla tutarda net (08 §3.4 açık), yanlış token / geç ödemede brüt.

## Sahip kararı uygulaması (2026-10-20) — reddedilen ödeme iadelerinde gas alıcıdan (⚠ PARA)

**Karar (proje sahibi, modal):** reddedilen ödemelerin iadesinde — eksik ödeme, fazla ödeme (fazla kısım), çoklu ödeme, yanlış token, geç ödeme — ağ ücreti her durumda alıcıya iade edilen tutardan kesilir; TRON ve Ethereum iki ağda. Belgeler (08 §3.4, 02 §4.6/§4.7, 05 §3.3, 03 §5) zaten "düşülür" diyordu; kod çekildi.

**Kesinti formülü (yeni hesap yok — mevcut parçalar):**

- `gas` = `ChargedGasFeeResolver.ResolveRefundFeeAsync(ağ, depozit, iade adresi, gelen tutar, gönderilecek token)` — her ret türünde **zaten** alınan ve en düşük iade eşiğinde kullanılan tahmin; `BUYER_REFUND`'ın sweep öncesi (depozitten) yolu ile aynı çözücü. TRON: sidecar `estimate-fee` (§3.1a — hesap açma + transferin enerjisi, yakma / devretme, canlı TRX/USDT; T149). Ethereum: sidecar `estimate-fee` depozit yolu (T154 + bu görevin §5 hizalaması: `tokenGasLimit (tahmin + %10) × maxFeePerGas + fuelGasLimit (tahmin) × beklenen fiyat`, canlı ETH/USDT). Tahmin alınamazsa ağın yedek ayarı (`refund_gas_fee_estimate_usdt` / `blockchain.ethereum.refund_gas_fee_estimate_usdt`).
- `taban` = gelen tutar (eksik tutar, çoklu transfer, yanlış token, geç ödeme) ya da `gelen − beklenen` (fazla tutar).
- `net = taban − gas` (`FinancialCalculator.CalculateRefund`, `RefundDecisionService` — değişmedi).
- **En düşük iade eşiği (değişmedi):** `net < gas × min_refund_threshold_ratio` (ağ başına, varsayılan 2) → `BelowMinimumThreshold`; `net < 0` → `NegativeAmount`. İkisinde de satır yazılmaz, `REFUND_BLOCKED` admin uyarısı, para depozitte elle karara bekler. Eşik tam sınırda (`net = 2 × gas`) iade gider. Belgelerle (02 §4.6 "net iade < 2 × gas fee", 08 §3.4) birebir.
- **Satır:** `Amount = net`, `GasFee = gas` (snapshot) — `BUYER_REFUND` biçimi; admin ayrıntısı `originalAmount = Amount + GasFee` artık bu türlerde de doğru (eskiden `GasFee` NULL → `originalAmount = brüt`, `gasFee` boş).

**Değişen iade yolları (ağ × tür) — hepsi `AmountValidationService`'te, ağdan bağımsız tek kod; ağ farkı yalnız tahminde:**

| Tür | Satır | TRON | Ethereum |
|---|---|---|---|
| Eksik tutar | `INCORRECT_AMOUNT_REFUND` | ✓ net | ✓ net |
| Fazla tutarın fazlası | `EXCESS_REFUND` | ✓ net | ✓ net |
| Çoklu / fazladan transfer | `EXCESS_REFUND` (`IsMultiPayment`) | ✓ net | ✓ net |
| Yanlış token (USDC) | `WRONG_TOKEN_REFUND` | ✓ net (gelen token cinsinden) | ✓ net (gelen token cinsinden) |
| Geç ödeme (iptal sonrası) | `LATE_PAYMENT_REFUND` | ✓ net | ✓ net |

Servisler değişmedi: TRON `RefundService` ve Ethereum `TransferService.depositRefund` backend'in tutarını olduğu gibi gönderir (TRON servisinin "sidecar only broadcasts the pre-computed net amount" yorumu artık doğru). `BUYER_REFUND` (iptal / uyuşmazlık iadesi) zaten netti, dokunulmadı.

**Bildirimler:** dört olaya (`BuyerPaymentInsufficientEvent`, `BuyerPaymentExcessRefundedEvent`, `WrongTokenRefundRequestedEvent`, `LatePaymentRefundRequestedEvent`) isteğe bağlı `RefundAmount` / `GasFee` eklendi (eski outbox satırları için `null` → brüt, 0 ücret). `INSUFFICIENT_PAYMENT`, `OVERPAYMENT_REFUNDED`, `WRONG_TOKEN_REFUND` metinleri net tutarı ve düşülen ücreti söyler; `LATE_PAYMENT_REFUNDED`'ın `{Amount}`'u artık net (eskiden brüt — "X USDT iade edildi" yanlış olurdu). Dört dil (en/tr/es/zh).

**Dosyalar:** `backend/src/Modules/Skinora.Transactions/Application/Webhooks/AmountValidationService.cs` (`QueueRefundIntent` kararın kendisini alır: `Amount = NetRefund`, `GasFee`; bloklanmış kararla çağrılırsa `InvalidOperationException`), `backend/src/Skinora.Shared/Events/{BuyerPaymentInsufficient,BuyerPaymentExcessRefunded,WrongTokenRefundRequested,LatePaymentRefundRequested}Event.cs`, dört bildirim tüketicisi (`Skinora.Notifications/Application/EventHandlers/`), `NotificationTemplates{,.tr,.es,.zh}.resx`; testler aşağıda; belgeler 02 §4.4 notu + tablo + §4.7 (v4.14), 05 §3.3 (v3.27), 06 §3.8 `GasFee` (v6.33), 08 §3.4 notu + tablo + §3.7.7 (v3.39), `plan/11_F8.md` "T159a kararları" (viii)–(ix) + kabul kriteri (e), `DOC_CHANGELOG.md`.

**Testler:**

- `AmountValidationServiceTests` (Transactions, Unit): yeni 4 teori × (5 tür × 2 ağ) = **40** vaka — (1) satır `Amount = taban − gas`, `GasFee = gas`, `Amount + GasFee = taban`, satırın ağı, tahminin o ağdan istendiği, olayın `RefundAmount` / `GasFee`'si; (2) tam eşikte (`taban = 3 × gas` → `net = 2 × gas`) gider; (3) bir kuruş altı (`taban = 3 × gas − 0,01`) gitmez, `BelowMinimumThreshold` uyarısı doğru net / eşik / gas ile; (4) gas tabanı aşarsa gitmez, `NegativeAmount`. Ücretler e2e yığınıyla aynı: TRON 2,00, Ethereum 0,05. Mevcut 5 vakanın brüt beklentisi nete çekildi (50→48, 10→8, 100→98, 50→48, 100→98) + `GasFee` = 2. Seed Ethereum'a genişletildi (ağ, `0x` adresler, Ethereum USDC kontratı). 63/63.
- `RejectedRefundNetAmountNotificationTests` (Notifications, Unit, yeni): 4 bildirim × 4 dil — parametreler net / ücret, şablon render'ında net ve ücret var, `{` kalmıyor; eski olay (alan yok) → brüt + 0,00. **17** vaka.
- `BlockchainWebhookEndpointTests` (API, Integration, SQLite): eksik / fazla / T144 eksik / yanlış token beklentileri nete çekildi (yedek tahmin 2,00) + `GasFee`. 31/31.
- e2e (`payment-edge-cases.spec.ts`, `multi-network.spec.ts`): beş türde ve iki ağda `Amount + GasFee = taban`, `GasFee > 0` (yerelde koşulmadı — CI'da e2e advisory; `tsc`, ESLint, Prettier (`--end-of-line auto`) yeşil).

**Bozma (mutation) — 5 / 5 yakalandı:**

| # | Bozma | Kırmızı |
|---|---|---|
| R1 | Satır tutarı yine brüt (`Amount = decision.TotalPaid`) | 25 (Transactions) |
| R2 | `GasFee` snapshot'ı yok (`GasFee = null`) | 25 |
| R3 | Geç ödeme olayında `RefundAmount` yok | 2 |
| R4 | Geç ödeme bildiriminde `{Amount}` yine brüt | 4 (Notifications) |
| R5 | Fazla tutar bildiriminde `RefundAmount` yine `ExcessAmount` | 4 |

**⚠ işaretli kararlar (YÖNETİCİ kabul ⚠ PARA):**

1. **Yanlış tokende kesinti gelen token cinsinden:** gas tahmini USDT karşılığıdır, yanlış token (USDC) tutarından 1:1 düşülür. En düşük iade eşiği bu varsayımı T72'den beri kullanıyordu; belge sessiz. Alternatif (kesmeme) sahip kararına ("her durumda") aykırı olurdu.
2. **Düşülen tutar depozitte kalır:** zincirde gas'ı platform öder (TRON'da sıcak cüzdanın TRX'i / yakma, Ethereum'da yakıt cüzdanının ETH'si); alıcıdan düşülen USDT (yanlış tokende USDC) depozit adresinde kalır — sweep yalnız `TotalAmount`'u taşır. `BUYER_REFUND`'ın sweep öncesi yolunda bugün de aynı. Platformun gerçek kazancı bu tozun toplanmasına bağlı; otomatik toplama yok (yeni görev konusu, bu görevde değişmedi).
3. **Tahmin brüt tutarın transferiyle, gönderim net:** `BUYER_REFUND` ile aynı. Transferin gas'ı tutara bağlı değil (EVM'de gönderenin bakiyesini sıfırlamayan gönderim yalnız gas iadesini kaçırır, gas limiti aynıdır; kesinti limit × fiyattan alınır).
4. **Bildirim metinleri (4 dil) değişti** (öneriyle kaydedildi — ⚠): eski metinler "alınan tutar iade ediliyor" / "{fazla} farkı iade ediliyor" diyordu; net ve ücret eklendi.
5. **Eski olaylar:** T159a'dan önce yazılmış outbox olayları `RefundAmount` taşımaz → bildirim brüt + 0,00 gösterir (o iadeler gerçekten brüt gitti — doğru).

## Madde 6 — Bahşiş hesabı (İNCELEME → hata değil; küçük hizalama yapıldı)

- **Kod tabanı 1 gwei değil 0,01 gwei:** `quoteFees` bahşişi `max(medyan, 0,01 gwei)` seçer; medyan `RpcClient.medianPriorityFee` = son 20 bloğun 50. yüzdelik bahşişlerinin üst ortası.
- **Sepolia'nın 1 gwei'si ağın kendi fiyatı:** salt okunur `eth_feeHistory` (2026-10-19, PublicNode Sepolia, 20 blok): taban her blokta 16 wei, **her bloğun 50. yüzdeliği `0x3b9ac9f0` = 999.999.984 wei = 1 gwei − taban** — Sepolia işlemlerinin çoğu 1 gwei sabit fiyatla (eski tip `gasPrice` ya da cüzdan varsayılanı) geliyor, etkin bahşiş = 1 gwei − taban. Servis piyasanın medyanını ödedi; tabanın 60 milyon katı görünmesinin sebebi Sepolia'nın neredeyse sıfır tabanı.
- **Mainnet'te aşırı ödeme yok:** aynı gün mainnet `eth_feeHistory` (20 blok): taban ~0,0416 gwei; blok medyanları 233.000 wei – 60,6 M wei, üst orta **10.000.000 wei = 0,01 gwei** (servisin tabanına eşit). T142'nin 0,05 gwei'si ile aynı mertebe. Taban yalnız medyan 0,01 gwei'nin altına düşerse devreye girer ve en çok 0,01 gwei × ~72.000 gas ≈ 0,0017 $ / akış fazla öder.
- **Küçük tutarsızlık bulundu ve düzeltildi:** T154 tahmini bahşişi alt ortayla ve tabansız seçiyordu (imzacı üst orta + taban); boş bloklarda ya da çift sayıda blokta alıcıdan kesilen tutar imzacının ödediğinden düşük çıkabiliyordu. Artık ikisi de `signingTip` kullanıyor (testli, bozma M9).

---

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `sidecar-ethereum/src/fee/feeMath.ts` | `NATIVE_TRANSFER_GAS` taban olarak belgelendi; `GAS_LIMIT_HEADROOM_PERCENT`, `tokenGasLimit`, `MAX_GAS_ESTIMATE`, `MAX_SIGNABLE_GAS_LIMIT`, `isPlausibleGasEstimate`, `MIN_PRIORITY_FEE_WEI`, `signingTip` |
| `sidecar-ethereum/src/transfer/FeePolicy.ts` | Referans gas sabitleri kaldırıldı; `capShareWei`; `cappedMaxFee(quote, cap, prices, gasLimit, flowGas)`; `GAS_ESTIMATE_UNAVAILABLE` / `GAS_ESTIMATE_ABNORMAL` |
| `sidecar-ethereum/src/transfer/TransferService.ts` | `fuelGasLimitOf` (yakıt probu), tahmin hatası → bekleme, `assertPlausibleEstimate`, tavan tahminle bölüşüm, yakıt gas'ı her yerde tahminden, T153 (xviii) uyarlaması |
| `sidecar-ethereum/src/transfer/SignerPolicy.ts` | `maxGasLimit` (1.100.000), `FuelIntent.gasLimit`, yakıt gas limiti kuralı |
| `sidecar-ethereum/src/transfer/FeeWaitTracker.ts` | İki yeni sebep `FEE_CAP_WAIT_REASONS`'ta |
| `sidecar-ethereum/src/fee/FeeEstimationService.ts` | Yakıt gas probu, depozit yolunda imzacının gas limiti, `signingTip`, anormal tahmin |
| `sidecar-ethereum/src/metrics.ts` | `skinora_ethereum_gas_estimate_abnormal_total{flow,leg}` |
| `sidecar-ethereum/src/wiring.ts` | İmzacı sınırı, tahmine yakıt cüzdanı adresi |
| `sidecar-ethereum/src/testing/FakeEthereumChain.ts` | `GasTariff`, `MAINNET_GAS_TARIFF`, `SEPOLIA_T159_GAS_TARIFF`, gas tükenmesi, düz ETH tahmini |
| `infra/grafana/provisioning/alerting/rules.yml` | `ethereum-gas-estimate-abnormal` (critical); `ethereum-transfer-wait` açıklaması |
| Testler: `Fuel.test.ts`, `TransferService.test.ts`, `SignerPolicy.test.ts`, `TxJournal.test.ts`, `FeeEstimationService.test.ts`, `feeMath.test.ts`, `alertRuleWiring.test.ts`, `wiring.test.ts` | Aşağıda |
| `Docs/08_INTEGRATION_SPEC.md` (v3.38) | §3.7.5 (T142 sayıları ölçüm; "Uygulandı (T159a)"; T153 (xviii) uyarlaması), §3.7.6 imzacı, §3.7.7 tavan ve T154 md. 3, §3.7.8 üç satır |
| `Docs/05_TECHNICAL_ARCHITECTURE.md` (v3.26) | §3.3a yeni bekleme sebepleri |
| `Docs/11_IMPLEMENTATION_PLAN.md` (v1.18) | §F8 "T159a kararları" (⚠), görev T159a |
| `Docs/DEPLOY_RUNBOOK.md` | §J.7 "Gas tahmini", §J.2 alarm satırı, §D adım 8.4 |
| `Docs/IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md` | T159a satırı |

Gas tarifesi turunda backend değişmedi (yeni bekleme sebepleri genel `deferred` yolundan geçiyor; `HttpBlockchainTransferClient` kodu okumuyor). `sidecar-fake` değişmedi (`fuelGasLimit: '21000'` mainnet biçimiyle uyumlu).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Yakıt gas limiti `eth_estimateGas` (hedefin hesap durumu dahil) + güvenlik payı; tavan bölüşümü tahminle; imzacı kuralları tahmini değerle; günlük / idempotency / tek yakıt korunur | ✓ (pay: yakıtta bilinçli olarak yok — gerekçe §1; token'da %10) | `Fuel.test.ts` "T159a — Sepolia tariff" (9 test), "the fuel probe is a plain ETH send of 1 wei…", "fuel + send never cost more than the one 0.50 $ cap…", "…bumped at its recorded 207,391 gas"; T153 testleri (one fuel per request, daily cap, idempotency bulgu e) değişmeden yeşil; `SignerPolicy.test.ts` yakıt gas limiti satırları |
| 2 | Token gönderimi işlem başına tahmin; tavan çevrimi, T154 ucu ve iade kesintisi aynı tahmin; sabitlerin gerekçesi | ✓ | `TransferService.test.ts` "payout to a seller who never held USDT on the Sepolia tariff (168,300)…"; `FeeEstimationService.test.ts` "Sepolia tariff: the fuel's gas is ESTIMATED…", "the tip is the signer's…"; tablo "Sabit kalan sayılar" |
| 3 | Tahmin başarısız → bekleme (deferred); tavan üstü → `FEE_ABOVE_CAP` + 3 sa; > 1.000.000 → imza yok + alarm | ✓ | `Fuel.test.ts` "T159a — the gas estimate fails or is implausible…" (6 test), M36 `GAS_ESTIMATE_UNAVAILABLE` / `GAS_ESTIMATE_ABNORMAL` 503 `{deferred: true}`; `TransferService.test.ts` "the gas estimate answered … → the send WAITS" (5), "implausible (1,000,001) waits"; "at Sepolia's real price … the 0.50 $ cap makes the sweep wait"; `alertRuleWiring.test.ts` `ethereum-gas-estimate-abnormal` |
| 4 | Sahte zincirde 204.600 / 153.000 tarifesi: yakıt geri dönmüyor, sweep / iade tamamlanıyor; mainnet'te davranış değişmiyor; bozmayla kanıt | ✓ | Yukarıdaki testler + "mainnet tariff: the same sweep keeps a 21,000-gas fuel"; eski Fuel / Transfer testleri mainnet tarifesinde (yalnız tavan oranı beklenen değerleri yeni formüle çekildi); bozma 12/12 |
| 5 | İade türlerinde gas kesintisi incelemesi | ✓ inceleme → sahip kararı | §Madde 5 |
| 8 | (e) Sahip kararı 2026-10-20: reddedilen ödeme iadelerinde gas iki ağda alıcıdan, mevcut tahmin + en düşük iade eşiği | ✓ | §"Sahip kararı uygulaması" — 40 tür × ağ vakası, bozma R1–R5 |
| 6 | Bahşiş incelemesi | ✓ inceleme; hata değil; T154 hizalaması düzeltildi | §Madde 6 |
| 7 | Belgeler: 08 §3.7, 11 §F8 "T159a kararları", DEPLOY_RUNBOOK; T142 sabitleri referans | ✓ | Yukarıdaki dosya tablosu |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-ethereum unit | ✓ 828/828 | `npx vitest run` (32 dosya) |
| tsc | ✓ | `npx tsc --noEmit -p .` |
| ESLint | ✓ | `npm run lint` |
| Prettier | ✓ | `npm run format:check` |
| Backend unit (CI filtresi `FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract`) | ✓ 2.198/2.198 | Shared 563, Platform 157, Auth 92, Users 49, Fraud 19, Steam 59, Disputes 25, Transactions 932, Realtime 44, Notifications 150, API 108 (sahip kararı turu) |
| Backend integration (ilgili) | ✓ | API `.Integration` 628/628; Transactions `NetworkChoiceLifecycleTests` + `PaymentBlockchainEntityTests` + Webhooks/Transfers entegrasyon 37/37 (Testcontainers) |
| `dotnet format --verify-no-changes --severity error` | ✓ | |
| e2e | `tsc` / ESLint / Prettier ✓ | Yığın yerelde koşulmadı (Docker paralel ajanda) — CI e2e advisory |

**Değişen eski test beklentileri (davranış değişikliğinin kendisi):** depozit akışı tavan payı 145.381.785.895.507 / 159.238.235.770.107 wei → 149.633.424.995.786 (50.822 / 71.822); "the fuel send's own share of the cap is checked too" (tahmin 20.000 — artık anormal) yerine iki işlemin tek gas başı tavanının sınır testi; M35 taban ücreti 1,42 → 1,46 gwei (yeni tavan ~2,94 gwei); M36 `FEE_ABOVE_CAP` kurgusu; `bulgu 2` tahmin hatası 502 → 503 `GAS_ESTIMATE_UNAVAILABLE` (proje sahibi kararı) + `eth_call` hatasının 502 kaldığını gösteren yeni test; T154 depozit yolu 46.201 → 50.822 ve boş ödül → 0,01 gwei.

### Bozma (mutation) — 12 / 12 yakalandı

| # | Bozma | Kırmızı |
|---|---|---|
| M1 | Yakıt gas'ı sabit 21.000'e dönüş (`fuelGasLimitOf` → 21.000) | 12 (Sepolia testleri: yakıt gas'ı tükenip geri döner, `FUEL_REQUIRED`) |
| M2 | "Kendi ücretinden küçük yakıt" uyarlaması kaldırıldı | 7 |
| M3 | Tavan bölüşümü yakıtı saymaz (token tavanın tamamını alır) | 5 |
| M4 | Tahmin hatası beklemeye değil 502'ye gider (eski davranış) | 8 |
| M5 | Anormal tahmin denetimi kaldırıldı | 5 |
| M6 | İmzacı yakıtta yine tam 21.000 ister | 9 |
| M7 | T154 tahmininde yakıt gas'ı sabit 21.000 | 1 |
| M8 | T154 depozit yolunda imzacının %10 payı yok | 3 |
| M9 | T154 bahşişi eski seçim (alt orta, tabansız) | 2 |
| M10 | `wiring.ts` imzacı sınırı 150.000'e dönüş | 1 |
| M11 | Yakıt probu 0 wei (hesap oluşturmayan prob, Sepolia'da 21.000 döner) | 10 |
| M12 | Kayıtlı yakıtın gas'ı yerine yeniden tahmin (bölüşüm sonradan değişir) | 1 |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |

## Altyapı Değişiklikleri

- Migration: Yok
- Config/env: Yok (yeni env yok; sabitler kodda). Grafana: yeni kural `ethereum-gas-estimate-abnormal`
- Docker: Yok

## Commit & PR

- Dal: `task/T159a-ethereum-gas-tarifesi` (`origin/main` `92154a6d`'den)
- Kod commit'i: `1dba9f87`; belgeler `ab842186`; main birleştirmesi `202478f2` (T159 #355 sonrası; secret-guard yanlış pozitifi BYPASS_LOG'da, `d0e0ab22`)
- Sahip kararı turu (2026-10-20): main birleştirmesi `d5c39bde` (#357 belge düzeni — başlık zincirleri `DOC_CHANGELOG.md`'ye, plan değişikliği `plan/11_F8.md`'ye; #358 hafıza); kod + test + belge `a220afec`
- PR: [#356](https://github.com/turkerurganci/Skinora/pull/356)
- CI: `39201495741` ✓ success (HEAD `d0e0ab22`; Lint, Build, Unit, Integration, Contract, Migration dry-run, JS test, Docker build sidecar-ethereum yeşil). Bu satırı ekleyen commit için yeni koşu PR sayfasında.

## Known Limitations / Açık sorular

1. ~~**⚠ PARA — reddedilen ödeme iadelerinde gas kesintisi**~~ → **kapandı (2026-10-20):** sahip kararı uygulandı (iki ağda net iade). Kalan ⚠: düşülen tutar depozitte toz olarak kalır, yanlış tokende kesinti gelen token cinsinden ("Sahip kararı uygulaması" ⚠ 1–2).
2. **⚠ PARA (öneriyle kaydedildi) — T153 (xviii) uyarlaması:** hiç ETH'si olmayan depozite yakıt kendi ücretinden küçük olsa da gidiyor (Sepolia tarifesinde zorunlu). Mainnet'te etkisiz.
3. **⚠ PARA (öneriyle kaydedildi) — T154 hizalaması:** Ethereum depozit iadesinde alıcıdan kesilen tutar token gas'ının ~%10'u kadar arttı (imzacının gas limiti) ve bahşiş imzacınınkine eşitlendi. Mainnet'te bugün kuruş altı.
4. **Sepolia provası:** Sepolia'nın 1 gwei bahşişiyle yeni depozitten sweep ≈ 0,61 $, ilk kez alan adrese iade ≈ 0,89 $; 0,50 $ tavanında `FEE_ABOVE_CAP` ile bekler (hiçbir şey yanmaz). Bir sonraki Sepolia provasında `blockchain.ethereum.fee_cap_usd_per_transfer` geçici olarak 1,00 yapılmalı (DEPLOY_RUNBOOK §J.7). Mainnet'e aynı tarife gelirse bugünkü fiyatla aynı akış ~0,01 $.
5. Yakıt probu yakıt cüzdanında ≥ 1 wei ister: yakıt cüzdanı tamamen boşsa, kendi ETH'si yeten bir depozitin gönderimi de `GAS_ESTIMATE_UNAVAILABLE` ile bekler (önce `ethereum-wallet-eth-low` çalar). Kabul edilebilir kenar durum; istenirse sıcak cüzdandan prob yedeği eklenebilir.
6. `eth_call` simülasyonunun düğüm hatası 502 geçici kaldı (karar "tahmin başarısızsa" diyor; `eth_call` tahmin değil). Backend 502'de deneme hakkı harcar — istenirse aynı bekleme yoluna alınabilir.
7. Sepolia bloklarındaki `blockAccessListHash` alanı yeni bir hard fork'a işaret ediyor; fork'un adı ve mainnet tarihi doğrulanmadı (T159'dan) — F9 öncesi bakılmalı. Servis artık tarifeye kendiliğinden uyuyor; açık kalan yalnız maliyetin tavana sığıp sığmadığı.
8. PR #355 (T159) yapım sırasında main'e girdi (`5306f88e`); dal main ile birleştirildi — `IMPLEMENTATION_STATUS.md` (T159 → T159a → T159b sırası) ve `MEMORY.md` çakışmaları elle çözüldü, 11'de T159a görevi T159b'nin önüne alındı (T159b T159a'ya bağlı). T159b (müdahalesiz Sepolia yeniden provası) bu değişiklikle koşulabilir; tavanın geçici 1,00 yapılması gerekir (madde 4).

## Notlar

- **Working tree:** başlangıçta temiz. Prettier yerelde `core.autocrlf` yüzünden dosyaları LF'ye çevirdi; commit'e yalnız içerik değişiklikleri girdi.
- **Dış varsayımlar (ölçüldü):** Sepolia bahşiş medyanı = 1 gwei − taban (`eth_feeHistory`, 20 blok, 2026-10-19); mainnet medyanı 0,01 gwei, taban ~0,042 gwei (aynı gün). Sepolia tarifesinin sayıları T159 raporundan (makbuz ve tahmin ölçümleri).
- **Paralel çalışma:** T157 (PR #354) `routes.ts` / `index.ts`'e dokunuyor; bu dal o dosyaları değiştirmedi.
- **Sahip kararı turu (2026-10-20):** `origin/main` (`baefd8d9`) dala birleştirildi; çakışmalar main düzenine göre çözüldü — 05 / 08 / DEPLOY_RUNBOOK başlıkları tek kısa satıra, T159a'nın başlık girişleri `DOC_CHANGELOG.md`'de ilgili bölümün en üstüne; 11'in T159a değişiklikleri (görev + "T159a kararları") `plan/11_F8.md`'ye (v1.19), ana 11 main'deki hâliyle; `MEMORY.md` main'in hâli + F8 satırı. Docker durdurulmadı.
- Docker açılmadı / kapatılmadı; gerçek ağa işlem gönderilmedi; `secrets/` okunmadı.
