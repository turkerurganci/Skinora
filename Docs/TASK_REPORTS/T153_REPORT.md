# T153 — Yakıt cüzdanı

**Faz:** F8 | **Durum:** ✓ Tamamlandı — bağımsız doğrulama ✓ PASS | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Yakıt yolu (`sidecar-ethereum/src/transfer/TransferService.ts`, yakıt bölümü):**
  - Ayrı bir yakıt ucu yok. Sweep ya da sweep öncesi iade, T152'nin bütün imza öncesi denetimlerinden geçtikten sonra (USDT durumu, alıcı kara listesi, `eth_estimateGas` + `eth_call` simülasyonu, ücret tavanı, depozitin nonce / `JOURNAL_BEHIND_CHAIN` denetimi — bu denetim artık bakiye kontrolünden önce) depozitin **bu isteğe ayrılabilir** ETH'si gönderimin en kötü ücretinden (`gasLimit × maxFeePerGas`) azsa yakıt cüzdanı **tam eksik kadarını** gönderir.
  - Ayrılabilir ETH = bakiye − depozitin kazılmamış gönderimlerinin en kötü ücreti − başka isteklerin bloğa girmiş ama sahibi henüz imzalanmamış yakıtı.
  - Hedef: istekteki indeksten servisin tohumundan yeniden türetilen depozit; platform cüzdanları (sıcak, soğuk, yakıt) asla.
  - Kayıt `fuel:<BlockchainTransaction.Id>` — bir istek, bir yakıt; günlüğe yayından önce yazılır.
  - Yakıt gönderilince istek 503 `FUEL_PENDING` (`deferred: true`) döner; token gönderimi yalnız yakıt fiyatlanan `latest` başın altında bir bloğa girmişken imzalanır. Kısa kalan yakıta ek yakıt yok: gönderim karşılanan ücretle (≥ taban + bahşiş) imzalanır, değilse `FUEL_REQUIRED` ile bekler.
  - Ücret tavanı: tek 0,50 $ iki işleme bölünür — token işlemi T152'nin payını, yakıt işlemi 21.000 / referans gas'ı alır (`FeePolicy.fuelCapWei`), ikisi aynı ETH okumasıyla (`cappedMaxFee` artık okuduğu fiyatı döndürüyor). Yakıtın kendi payı aşılırsa yakıt yok, `FEE_ABOVE_CAP`.
  - Yakıtın gönderilemediği her durum **bekleme**: `FUEL_REQUIRED` (anahtar ya da tavan ayarı yok, yakıt geri döndü, yakıt bugünkü ücrete yetmiyor), `FUEL_LIMIT_REACHED` (tek / 24 saatlik tavan), `FUEL_WALLET_LOW` (bekleyen yakıtlar ayrılmış sayılarak yakıt cüzdanı ödeyemiyor).
  - 24 saatlik tavan günlükten (`fuelSpentLast24hWei`; geri dönen ve çakışan dahil). Bekçi yakıtı da yeniden yayınlar ve aynı nonce'la, aynı değerle, kendi payı içinde artırır (`bumpFuel`; kendi tutarı günlük tavana iki kez sayılmaz).
- **İmzacı politikası (`SignerPolicy.ts` `assertSignableFuel`):** T152'nin yakıt istisnasına üç kural eklendi — değer gönderimin ihtiyacını aşamaz (`maxForSendWei`), yakıt işleminin en kötü ücreti kendi payını aşamaz (`feeCapWei`), platform cüzdanı hedef olamaz (`platformAddresses`).
- **FuelLedger (`reconcile/FuelLedger.ts`):** kayıt hash'e göre tekil, yeniden düzenleme bloğu değiştirir; bekçi ve yakıtı bekleyen istek yakıtı bloğa girince yazar; açılışta günlükten yeniden kurulur (`wiring.ts` `seedFuelLedger`). Kayıtlı yakıt `ethereum-deposit-native-eth` alarmı vermez.
- **Cüzdan bakiye alarmı (`wallet/WalletBalanceMonitor.ts`, yeni):** yakıt ve sıcak cüzdanın ETH'si 5 dk'da bir okunur; `skinora_ethereum_wallet_eth_balance{wallet}` ve `skinora_ethereum_wallet_eth_floor{wallet}`; Grafana kuralı `ethereum-wallet-eth-low` (oran < 1, 10 dk, warning). Env: `ETHEREUM_FUEL_WALLET_MIN_ETH`, `ETHEREUM_HOT_WALLET_MIN_ETH` (0,06), `ETHEREUM_WALLET_BALANCE_INTERVAL_MS` (300.000).
- **Metrikler:** `skinora_ethereum_fuel_sends_total{flow}`, `skinora_ethereum_wallet_balance_read_failures_total{wallet}`; `kind="gas"` beklemesi yeni dört sebebi de sayar (`GAS_WAIT_REASONS`).
- **Kablolama:** `buildOutbound` yakıt anahtarını, T152 tavanlarını, soğuk cüzdanı ve FuelLedger'ı TransferService'e verir; `index.ts` açılış satırı (`Ethereum fuel wallet configured`), yakıt anahtarı yoksa uyarı.
- **Test düzeneği:** `FakeSendChain.chargeGas` (düğüm gibi: `value + gasLimit × maxFeePerGas` karşılanmazsa yayın reddi ve kazılmama, kazılınca `gasUsed × fiyat` düşülür), düz ETH gönderiminde göndericiden düşme, `revertEthTo`.
- **Belgeler:** 05 v3.24 (§3.3a "Uygulandı (T153)"), 08 v3.34 (§3.7.5 "Uygulandı (T153)" + artık ETH gerekçesi, §3.7.6 imzacı istisnası ve gas satırı, §3.7.8 yeni satır), 11 v1.14 ("T153 kararları" (i)–(xiii), kaynak eşleme satırları), DEPLOY_RUNBOOK (§B yakıt tavanları + yeni taban satırı, §C.3 yakıt tavanı boyutlandırması, §D iki parite satırı, adım 5 notu, yeni adım 8), `.env.example`, `docker-compose.yml`.

## Etkilenen Modüller / Dosyalar

- **sidecar-ethereum (değişen):** `src/transfer/{TransferService,SignerPolicy,FeePolicy,FeeWaitTracker}.ts`, `src/reconcile/FuelLedger.ts`, `src/config/index.ts`, `src/metrics.ts`, `src/wiring.ts`, `src/index.ts`, `src/testing/FakeEthereumChain.ts`.
- **sidecar-ethereum (yeni):** `src/wallet/WalletBalanceMonitor.ts`, `src/transfer/Fuel.test.ts`, `src/wallet/WalletBalanceMonitor.test.ts`.
- **sidecar-ethereum testleri (değişen):** `SignerPolicy.test.ts`, `Reconciler.test.ts`, `config/index.test.ts`, `config/composeWiring.test.ts`, `alertRuleWiring.test.ts`, `api/transferHandlers.test.ts`, `wiring.test.ts`.
- **Altyapı:** `docker-compose.yml` (3 env), `infra/grafana/provisioning/alerting/rules.yml` (`ethereum-wallet-eth-low`), `.env.example`.
- **Doküman:** `Docs/05_TECHNICAL_ARCHITECTURE.md`, `08_INTEGRATION_SPEC.md`, `11_IMPLEMENTATION_PLAN.md`, `DEPLOY_RUNBOOK.md`, `IMPLEMENTATION_STATUS.md`, bu rapor, `.claude/memory/MEMORY.md`.
- **Dokunulmayan:** backend (sözleşme değişmedi — yakıt beklemeleri T152'nin genel `deferred` yolu), sidecar-blockchain, sidecar-fake, frontend.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Sweep/iade öncesi depozite tam gereken kadar ETH | ✓ | `Fuel.test.ts` "an empty deposit … exactly gasLimit × maxFeePerGas", "already holding part of the gas gets only the rest; … all of it gets none", "a refund before the sweep is fuelled the same way"; politika `maxForSendWei`; bozma M01, M02, M23 |
| 2 | Hedef yalnız servisin kendi türettiği adresler | ✓ | depozit yeniden türetilir (`DEPOSIT_ADDRESS_MISMATCH`), imzacı `isOwnDeposit` + `platformAddresses`; testler "index that does not derive the named deposit", "index that derives a platform wallet is never fuelled", politika testleri; bozma M15, M25, M27 |
| 3 | Tek ve günlük tavan | ✓ | "above the single-send cap: FUEL_LIMIT_REACHED", "the 24-hour cap counts every fuel the journal signed, survives a restart and frees up after 24 h", "no single / daily cap → FUEL_REQUIRED"; bozma M09, M10, M11, M20, M26 |
| 4 | Ücret tavanı sweep ve iade yakıtını durdurur, gönderim bekler ve alarm verir | ✓ | "sweep / refund above the cap: no fuel is sent, the send waits FEE_ABOVE_CAP and its wait clock runs" (fee-cap bekleme saati → `ethereum-transfer-wait`), "the fuel send's own share of the cap is checked too", "priced on its own share", "fuel and token send together never cost more than the one 0.50 $ cap"; bozma M07, M08, M19, M30 |
| 5 | İade ölçülen ücreti düşüyor ve en düşük iade eşiğine bağlı | ✓ (değişmedi) | Backend `RefundDecisionService` (T149/T154): `RefundDecisionServiceTests` ağ başına eşik (TRON 2 / Ethereum 5 örneği) ve kesinti; T153 yalnız gönderim tarafını ekler, kesinti kuralına dokunmaz |
| 6 | Yakıt cüzdanı bakiyesine alarm | ✓ | `WalletBalanceMonitor.test.ts`, `alertRuleWiring.test.ts` "ethereum-wallet-eth-low", `config/index.test.ts` tabanlar, `composeWiring.test.ts` env; bozma M29 |
| 7 | (Yönetici) Çift yakıt yok | ✓ | "a retry before the fuel lands signs no second fuel", "two parallel calls with the same id sign one fuel", "a restart finds the journalled fuel", "short fuel … no top-up"; bozma M04 |
| 8 | (Yönetici) Yakıt kesinleşmeden token işlemi imzalanmaz | ✓ | "the token send is signed only once the fuel is in a block — never before" (yayın anında yakıt makbuzu var), "a fuel mined in a block above the priced head is not counted yet", "a fuel that reverted is not taken as delivered"; bozma M03, M28 |
| 9 | (Yönetici) Yakıt cüzdanı bakiyesi / sırası / günlük | ✓ | "FUEL_WALLET_LOW", "the fuel wallet's unmined fuel is not spent twice", "a fuel wallet whose journal is behind the chain signs nothing", "a deposit whose journal is behind the chain is not fuelled"; bozma M12, M13, M14b |
| 10 | (T151) Kayıtlı yakıt ETH girişi alarm vermemeli | ✓ | "feeds the FuelLedger … once only", "a restart rebuilds the FuelLedger from the journal", `Reconciler.test.ts` FuelLedger üç test; bozma M16, M17, M18 |
| 11 | (Yönetici) Başka isteğin yakıtı alınmaz | ✓ | "a refund arriving after a sweep's fuel landed gets its own fuel; both sends go out", "a deposit send already signed reserves its gas"; bozma M05, M06 |
| 12 | (T152) Geçici durumda FAILED yok | ✓ | yakıtın her engeli 503 `deferred` (handler testi dört sebep için 503 + `deferred: true`); bozma M26 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-ethereum (vitest) | ✓ 690/690 | `cd sidecar-ethereum && npx vitest run` — 28 dosya (önce 621 / 26; +69 test: `Fuel.test.ts` 44, `WalletBalanceMonitor.test.ts` 4, politika +3, config +7, FuelLedger +3, handler +4, alarm +1, …) |
| Derleme / biçim | ✓ | `npx tsc -p .`, `npx eslint src`, `npx prettier --check "src/**/*.ts"` temiz |
| Backend | değişmedi | Kod değişikliği yok; CI'da koşar |

**Bozma denemeleri** (her biri tek değişiklik; `src/transfer`, `src/reconcile`, `src/wallet`, `wiring.test.ts`, `transferHandlers.test.ts` koşuldu; sonra geri alındı): **31 bozma / 31 yakalandı.** _(Doğrulama notu: bağımsız ölçüm 45 bozma / 34 yakalandı verdi — bu iddia uyuşmuyor; ayrıntı "Doğrulama sonucu".)_
M01 yakıt depozitteki ETH'yi hesaba katmıyor · M02 1 wei fazla yakıt · M03 başın üstündeki bloktaki yakıt sayılıyor · M04 yeniden denemede günlük sorulmuyor · M05 başka isteğin yakıtı ayrılmıyor · M06 kazılmamış depozit gönderimi rezerv tutmuyor · M07 yakıtın tavan payı denetlenmiyor · M08 yakıt payı = tavanın tamamı · M09 tek tavan ön denetimi yok · M10 günlük tavan ön denetimi yok · M11 günlük pencere zamanı yok sayıyor · M12 yakıt cüzdanı bakiyesi denetlenmiyor · M13 bekleyen yakıtlar ayrılmıyor · M14b depozit nonce denetimi yakıttan sonra · M15 platform cüzdanları dışlanmıyor · M16 bekçi FuelLedger'ı beslemiyor · M17 FuelLedger tekil değil · M18 açılışta FuelLedger kurulmuyor · M19 yakıt artırması tavanı aşıyor · M20 artırma kendi yakıtını iki kez sayıyor · M21 kısa yakıt hiç kullanılmıyor · M22 kısa yakıt taban + bahşişin altında kullanılıyor · M23 politika: fazla değer · M24 politika: yakıt ücret tavanı · M25 politika: platform hedefi · M26 eksik tavan ayarı 400 ret · M27 yakıt başka adrese · M28 geri dönen yakıt teslim sayılıyor · M29 bakiye metriği yazılmıyor · M30 yakıt token işleminin tavanıyla fiyatlanıyor · (+ M14 ilk biçimi derlemeyi bozduğu için geçersiz sayıldı, yerine M14b).
İlk turda iki bozma kaçtı ve testle kapatıldı: **M30** (varsayılan fiyatlarda iki tavan aynı sonucu veriyordu → iki tabanda ayırt edici test) ve **M28** (yakıt geri dönmesini sahte zincir modellemiyordu → `revertEthTo` + test).

## Doğrulama sonucu

- **✓ PASS** — bağımsız temiz bağlam doğrulaması (Opus, F8 yönetici düzeni); doğrulanan HEAD `bcc74854`, CI [`39156856045`](https://github.com/turkerurganci/Skinora/actions/runs/39156856045) ✓. Merge yöneticide.
- **Bozma (bağımsız ölçüm): 45 bozma / 34 yakalandı.** Yapım raporundaki "31 bozma / 31 yakalandı" iddiası bağımsız ölçümle **uyuşmuyor** — yapımın kendi bozma kümesi para kurallarının bir kısmını hiç denemiyordu. Kaçanların doğrulamada adı verilenleri ve bu kapanıştaki kapanışı: **M35** tavan bağlarken yakıtın tavansız fiyatla hesaplanması → gönderilen yakıt tutarı `gasLimit × tavanlı fiyat`'a sabitlendi · **M36** yakıt engellerinin yalnız kodu denetleniyordu → gerçek servis + HTTP katmanıyla altı engel (FUEL_PENDING / FUEL_REQUIRED / iki FUEL_LIMIT_REACHED / FUEL_WALLET_LOW / FEE_ABOVE_CAP) için 503 + `deferred: true` + `retryable: true` · **M44** başka isteğin henüz kazılmamış yakıtı ayrılmış sayılsa → bu isteğin yakıt tutarı sabitlendi · **M40** 24 sa sınırı (tam 24 sa önceki yakıt sayılır, 1 ms sonra sayılmaz) · **M33** sıcak cüzdanın ETH bakiye izlemesine kablolaması (`buildOutbound` çıktısında iki cüzdan, kendi tabanlarıyla) · **M27/M29** geri dönmüş yakıtın FuelLedger'a canlı ya da açılışta yazılmaması · **M02** tek yakıt tavanı eşitliği (servis ve imzacı); ayrıca (a) ve (b) savunma kusurları. Doğrulamanın 11 kaçağından geri kalanlar bu kapanışa ayrıca listelenmedi; bu kapanışın kendi bozma turu aşağıda.
- **Bulgular ve kapanış (yönetici kararları):**
  - **(a) PARA — yakıtın kendi ücreti günlük tavana sayılmıyordu, yakıt adedi sınırsızdı** (prob: 40 kimlik × 1 wei → tavana 40 wei, ücrete 8,8e14 wei). **Kapanış:** `fuelSpentLast24hWei` = değer + `21.000 × maxFeePerGas` (son deneme); servisin ön denetimi ve imzacı (`assertSignableFuel`) aynı toplamı sayar. **En düşük yakıt kuralı (seçim — ⚠ PARA, 11 T153 (xviii)):** eksik, yakıt işleminin kendi en kötü ücretinden küçükse yakıt gönderilmez; depozit kendi ETH'sinin karşıladığı ücretle (en az taban + bahşiş) imzalar, karşılamıyorsa `FUEL_REQUIRED` ile bekler. Yuvarlama seçilmedi (imzacının "tam eksik kadar" sınırını gevşetirdi, depozite artık bırakırdı); düz bekleme de seçilmedi (eksik bir yakıt ücretinden azsa depozit hemen her zaman taban + bahşişi kendisi öder). Testler: "40 small fuels: value + fee hits the daily cap where value alone would not", "lacking less than a fuel's own fee gets no fuel — it signs at the fee it affords", "…and waits FUEL_REQUIRED", imzacıda eşitlik + 1 wei.
  - **(b) İmzacı sınırı:** `maxForSendWei` artık `gasLimit × maxFeePerGas − ayrılabilir ETH` (tutardan ayrı hesaplanır); `SignerPolicy.ts` JSDoc ve 11 T153 (ii) aynı tanım. Test: imzacıya verilen niyet (geçirgen casus) depozitte 1/3 ETH varken eksiği taşır.
  - **(c), (d) test boşlukları:** yukarıdaki M listesi.
  - **(e) Yalnız yakıt kaydı varken kimlik çakışması:** yakıt kaydı isteğin kimliğini `fuelFor` alanında saklar; aynı kimlik başka depozit / indeks / tutar / akışla gelirse `IDEMPOTENCY_CONFLICT` (kalıcı ret, yeniden başlatmada da), aynı istek beklemeyi sürdürür.
- **Karar (1) — kötüye kullanım değerlendirmesi (yakıt izleme kaydına bağlanmaz, ⚠ PARA):** ele geçirilmiş bir backend en çok günlük tavan kadar ETH'yi (önerilen 0,02 ETH/gün) yalnız kendi depozitlerimize taşıtabilir — değer servisin kendi anahtarlarında kalır; kaybolabilecek tek şey yakıt ücretidir ve o yüzey (a) ile kapandı (ücret de tavana sayılıyor, ücretinden küçük yakıt yok). Bu yüzey T152 iade yüzeyinden küçük → **kabul**. Diğer yönetici kararları: (2) ikinci yakıt yok, bekler; (3) ~0,006 $ fark platformda; (4) sıcak cüzdan ETH alarmı bu görevde — 11 T153 (xiv)–(xviii).
- **Bu kapanışın bozma turu:** 19 bozma / 19 yakalandı (`Fuel.test.ts`, `SignerPolicy.test.ts`, `wiring.test.ts`): günlük toplamda ücret yok · servis ön denetiminde kendi ücreti yok · imzacıda ücret yok · artırmada ilk denemenin fiyatı · en düşük kural kaldırıldı · en düşük kural `<=` · `maxForSendWei = bütçe` · yakıt tavansız fiyattan · yakıt engeli düz `SidecarError` · kazılmamış yabancı yakıt ayrılmış · pencere `<=` · sıcak cüzdan izlenmiyor · geri dönen yakıt canlı kayıtta · geri dönen yakıt açılışta · servis tek tavan `>=` · imzacı tek tavan `>=` · yakıt-kimlik denetimi yok · `fuelFor` yok sayılıyor · indeks karşılaştırılmıyor (ilk koşuda kaçtı — farklı adres de veren testte eşdeğerdi → "aynı depozit, başka indeks" vakası eklendi, yakalandı).
- **Belgeler:** 11 v1.15 ("T153 kararları" (ii), (viii), (xiii) + (xiv)–(xviii)); 08 v3.35 §3.7.5; DEPLOY_RUNBOOK §B, §C.3 (günlük tavan notu; değer 0,02 kaldı — ETH 500 $'da en kötü hâlde ~20 yakıt, aşan gün bekler).
- **Kapanış testleri:** sidecar-ethereum vitest **713/713** (28 dosya), `tsc --noEmit`, eslint ve prettier (değişen dosyalar) temiz. Gerçek ağa işlem gönderilmedi, Docker açılmadı.

## Altyapı Değişiklikleri

- Migration: yok. Enum: yok (TypeScript birleşim tipine dört bekleme sebebi eklendi; backend sebebi metin olarak taşır).
- Config/env: **Var** — `ETHEREUM_FUEL_WALLET_MIN_ETH`, `ETHEREUM_HOT_WALLET_MIN_ETH` (varsayılan 0,06), `ETHEREUM_WALLET_BALANCE_INTERVAL_MS` (300.000); compose, `.env.example`, DEPLOY_RUNBOOK §B/§D. T152'nin `ETHEREUM_FUEL_MAX_SINGLE_ETH` / `_DAILY_ETH`'si artık kullanılıyor (boş = yakıt yok, sweep'ler bekler); önerilen değerler §C.3 (0,001 / 0,02 ETH).
- Alarm: `ethereum-wallet-eth-low` (warning).
- Yeni dış bağımlılık: yok.

## Commit & PR

- Dal: `task/T153-yakit-cuzdani` (origin/main `0c7373f0`'dan)
- Kod commit'i: `8aad5235`
- PR: #352
- CI: rapor commit'inden sonraki run — yöneticiye dönüşte bildirildi.

## Known Limitations / Açık sorular

**Yöneticiye bırakılan (⚠, en temkinli varsayımla ilerlendi):**
1. **"Backend'in izlediği depozit" izleme kaydına bağlanmadı (PARA, T153 kararı iii).** Bağlansaydı izlemesi kapanmış (`REFUNDED` / `COMPLETED`, ya da kayıt yeniden kurulmadan önce) bir depozitin meşru iadesi süresiz beklerdi. Yerine yakıt, depozitin gerçekten tuttuğu token'ı gönderen ve simülasyonu geçen bir isteğe bağlı; ele geçirilmiş bir backend'in en kötü etkisi: tavanlar içinde, servisin kendi adreslerine (para servisin anahtarında kalır) yakıt göndertmek. Kayda bağlamak istenirse küçük bir değişiklik.
2. **Kısa kalan yakıta ek yakıt yok (T153 kararı iv).** Yakıt gönderildikten sonra taban ücret iki katından fazla artarsa gönderim ücret düşene kadar bekler (`FUEL_REQUIRED`; 3 sa `outbound-transfer-stuck`). Ek yakıt, "bir isteğe bir yakıt" garantisini bozacağı için seçilmedi.
3. **İade kesintisi ile gerçek yakıt arasındaki küçük fark (T153 kararı x).** T154 tahmini gas tahminiyle, yakıt +%10 paylı gas limitiyle hesaplanır; fark (bugün ~0,006 $) platformda kalır.
4. **Sıcak cüzdan ETH alarmı da bu görevde** (T142 satırı ikisini birlikte veriyordu; aynı mekanizma). Panel görünürlüğü T157, doldurma runbook'unun tamamı T158.

**Sınırlar:**
- Gerçek ağda prova yapılmadı (Sepolia ETH yok — T159). Yakıt akışı sahte zincirde (`FakeSendChain.chargeGas`) düğüm gibi gas ve bakiye denetimiyle sınandı.
- Bakiye alarmı anahtarı bağlı cüzdanlar için yayımlanır; yakıt anahtarı yoksa alarm susar (sweep'ler `FUEL_REQUIRED` ile bekler, backend `outbound-transfer-stuck` 3 sa sonra çalar; açılış log'u uyarır).
- Backend'in admin bildirimi yok (backend yakıt adresini bilmiyor); alarm Grafana'da.

## Notlar

- **Working tree:** temiz (dal origin/main `0c7373f0`'dan kesildi).
- **Main CI startup check:** `39153430208` CI ✓ (T152 merge'ü; açılışta sürüyordu, tamamlanması beklendi), `39153430245` Docker Publish ✓, `39144820003` CI ✓ (T155).
- **Bağımlılıklar:** T152 ✓.
- **Dış varsayımlar:**
  - Bir EOA'ya düz ETH gönderimi 21.000 gas tüketir ve geri dönmez (yalnız kod taşıyan hesapta dönebilir; depozitler kod taşımaz, imzacı tip 4 imzalamaz) — Ethereum sarı kağıdı / geth; T142 makbuz ölçümü (yakıt 21.000).
  - Düğüm, işlemi `value + gasLimit × maxFeePerGas` göndericinin bakiyesini aşıyorsa havuza almaz ("insufficient funds for gas * price + value") — geth `txpool` davranışı; sahte zincir aynı kuralı uygular.
  - Bloğa giren yakıt `latest` blokta bakiyede görünür; `finalized` beklenmez (T141 "Ethereum akış ayrıntıları" kararı).
  - Gerçek sağlayıcıda prob yapılmadı (anahtar/ETH yok, T158/T159).
- **Enum değişikliği:** yok.
- **Docker:** açılmadı.
- **Commit hook notu:** bir test başlığı (12 küçük harfli İngilizce kelime) sır taramasının BIP-39 kalıbına takıldı; başlık "a fee bump is not counted twice against the 24-hour fuel cap" yapıldı (sır yok, bypass kullanılmadı).
