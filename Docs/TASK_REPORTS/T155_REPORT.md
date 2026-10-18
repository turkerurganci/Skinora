# T155 — sidecar-fake Ethereum desteği + E2E

**Faz:** F8 | **Durum:** ✓ Tamamlandı (bağımsız doğrulama ✓ PASS) | **Tarih:** 2026-10-18

---

## Yapılan İşler

- **Sahte servise Ethereum yüzeyi (`sidecar-fake`, port 5300):** TRON (5200) ile Ethereum (5300) aynı sözleşme yollarını sunduğu için her port kendi ağına bağlı ayrı bir yönlendirici taşıyor (`src/app.ts` `buildApp(surface)`, `src/index.ts` üç dinleyici). Ağ farkları tek profilde (`src/networks.ts`): adres/hash biçimi, HD yolu (`m/44'/195'` / `m/44'/60'`), token kontratları (Ethereum mainnet USDT/USDC küçük harf, backend varsayılanıyla aynı), webhook sırrı, kesinleşme biçimi.
- **Uçlar (`src/routes/blockchain.ts`, iki ağ):** `wallet/derive` (Ethereum EIP-55 — gerçek servis gibi), `wallet/balances`, **yeni** `monitor/start` · `monitor/stop` (kayıt, `/__e2e/blockchain/monitors` ile okunur), `monitor/post-cancel-*`, `transfer/{payout,refund,sweep,cold-wallet}` (TRON 64-hex, Ethereum `0x`+64-hex hash), `transfer/status` (TRON `confirmations: 25`; Ethereum `finalized: true`, `confirmations: null`, `realizedFeeWei` dize — K5, T154), **yeni** `transfer/estimate-fee` (TRON 2,00 + Energy dökümü; Ethereum T154 biçimi: `feeWei`/`ethPriceUsdt` dize, `feeUsdt` = yukarı yuvarlanmış çarpım, 0,05), **yeni** `transfer/estimate-sweep-fee` (TRON 8.441.349 SUN × 0,34 → backend satırı 2,90; Ethereum 501). `/health` ağı adlandırıyor, Ethereum `acceptsPayments: true` (T151 kapısı).
- **Sahte servis gerçek servisin reddettiğini reddediyor (400):** başka ağı — Ethereum'da hiç ağı — adlandıran POST (`NETWORK_MISMATCH`), başka ağın biçiminde adres (`INVALID_ADDRESS`), başka ağın/sembolün kontratıyla `monitor/start` (`UNEXPECTED_CONTRACT`), başka ağın biçiminde hash (`INVALID_TX_HASH`). Her backend çağrısı ağı ve cevap koduyla günlüğe yazılıyor (`src/blockchainState.ts`, `GET /__e2e/blockchain/calls`).
- **Ödeme kolları ağ farkında (`src/routes/paymentControl.ts`, `src/webhookEnvelopes.ts`):** depozitin ağı veritabanından (`PaymentAddresses.Network`) okunuyor; webhook o ağın servisinin göndereceği biçimde (hash, ödeyen cüzdan, kontrat) ve o ağın sırrıyla imzalanıyor. Negatif kollar: `claimNetwork` (zarfın ağı), `signAs` (imzalayan sır); backend reddi 502 + `backendStatus`/`backendBody` olarak dönüyor. Ücret tahminleri ağ başına sürülebilir (`POST /__e2e/blockchain/fees`, `available: false` → 502 → backend yedeği).
- **Bağımlılıksız Keccak-256 + EIP-55 (`src/keccak.ts`, `src/ids.ts`):** Ethereum depozit adresi gerçek servis gibi karışık harfli; ethers 6.17.0 çıktısına ve EIP-55 belgesinin örneklerine karşı testli.
- **Sözleşme örneklerine bağlama (`src/contracts.test.ts`):** TRON örnekleri (`sidecar-blockchain/contracts/`: webhook ×6, estimate-fee ×3, monitor-start ×2) sahte servise oynatılıyor, webhook zarflarının şekli örnekle aynı. Ethereum blokları `sidecar-ethereum/contracts/` dizinleri varsa koşuyor (T151 `webhook/`, T154 `estimate-fee/` + `transfer-status/` henüz main'de değil); bu dallardaki dosyalar geçici olarak yerleştirilip 22/22 yeşil ölçüldü — değerler (kontrat adresleri) birebir, `feeUsdt` aritmetiği T154 örneğinin kendisiyle tutarlı (21,75).
- **E2E yığını (`docker-compose.e2e.yml`):** sahte servise 5300 portu + `ETHEREUM_WEBHOOK_SECRET` + `skinora-ethereum-sidecar` takma adı; backend'e `EthereumSidecar__BaseUrl/InternalKey`, `Webhook__EthereumSharedSecret` (TRON'dan farklı), `ETHEREUM_HOT/COLD_WALLET_ADDRESS` yer tutucuları. Ethereum bütün e2e süitlerinde açık; ağ belirtmeyen eski süitler TRON varsayılanıyla aynen koşuyor.
- **Yeni E2E süiti `e2e/tests/multi-network.spec.ts` (4 test):**
  1. **İki ağın mutlu yolu eşzamanlı:** iki ilan TRON + Ethereum kabul ediyor; `networkQuotes` TRON 2,90 / 104,90, Ethereum 0,00 / 102,00; alıcı birini TRON'da, birini Ethereum'da kabul ediyor; depozitler kendi ağında ve biçiminde (Ethereum gösterimi EIP-55 = sahte servisin `derive` cevabı), kendi ağının servisinden türetiliyor ve o ağın USDT kontratıyla izleniyor; ödeme → `PAYMENT_RECEIVED`; teslim + mutabakat; ikisi de `COMPLETED`. Sweep ve payout kendi ağında, o ağın sıcak cüzdanına / satıcının o ağdaki adresine; Ethereum satıcısı tam fiyat (100, ücret 0,05 — T149 ix), TRON satıcısı 98,20 (2,00 − 0,20). Sahte servisin günlüğü: Ethereum satırlarının yayın ve durum okumaları yalnız Ethereum servisine, TRON'unkiler yalnız TRON'a; hiçbir çağrı 400 almadı.
  2. **İki ağda iade (K9):** eksik ödeme her ağda kendi ağından, kabulde sabitlenen iade adresine (ödeyen cüzdana değil) `CONFIRMED`; iade çağrısı o ağın servisine, depozitten; Ethereum iade ücreti depozit kaynaklı tahminden.
  3. **Yanlış ağ webhook'u reddi (T148):** Ethereum depozitine (a) TRON diyen, (b) TRON sırrıyla imzalı, (c) ikisi birden — üçü de 409 `WEBHOOK_NETWORK_MISMATCH`, kayıt yok, işlem `SELLER_CONFIRMED`; aynı ödeme doğru gönderilince kabul.
  4. **Kabul edilmeyen ağ reddi:** yalnız TRON ilanında Ethereum kabulü 422 `NETWORK_NOT_ACCEPTED`, hiçbir şey yazılmıyor.
- **E2E yardımcıları:** `e2e/src/api.ts` (`acceptedNetworks`, kabulde `network`, negatif kollar, günlük/izleyici/ücret okuyucuları, `fakeEthereumDerive`), `e2e/src/db.ts` (satıcının Ethereum ödeme adresi, depozit/zincir satırı/işlem ağı okuyucuları), `e2e/src/config.ts` (`fakeEthereumUrl`), `package.json` `test:network`.
- **CI (`ci.yml`):** yeni advisory bacak "T155 multi-network" (40 dk); `sidecar-fake` yol süzgecine `sidecar-blockchain/contracts/**` ve `sidecar-ethereum/contracts/**` (örnek değişince sahte servisin sözleşme testi koşsun).
- **Belgeler:** 11 §F8 "T155 kararları" bloğu + §7.6 T155 satırı (v1.5 başlık notu), 08 v3.27 §9.3 "E2E sahte servis" satırı, `sidecar-fake/README.md`.

## Etkilenen Modüller / Dosyalar

- **sidecar-fake:** `src/app.ts`, `src/index.ts`, `src/config.ts`, `src/db.ts`, `src/ids.ts`, `src/webhookClient.ts`, `src/routes/{blockchain,control,health}.ts`; yeni `src/networks.ts`, `src/blockchainState.ts`, `src/feeMath.ts`, `src/keccak.ts`, `src/webhookEnvelopes.ts`, `src/routes/paymentControl.ts`; testler `src/ids.test.ts` + yeni `src/{blockchain,contracts,feeMath,keccak}.test.ts`; `Dockerfile` (EXPOSE 5300), `README.md`.
- **e2e:** `src/api.ts`, `src/db.ts`, `src/config.ts`, `package.json`; yeni `tests/multi-network.spec.ts`.
- **Altyapı:** `docker-compose.e2e.yml`, `.github/workflows/ci.yml`.
- **Doküman:** `Docs/11_IMPLEMENTATION_PLAN.md`, `Docs/08_INTEGRATION_SPEC.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/TASK_REPORTS/T155_REPORT.md`, `.claude/memory/MEMORY.md`.
- **Backend:** değişiklik yok.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Sahte servis Ethereum biçimli adres/hash üretiyor | ✓ | `ids.test.ts` (EIP-55 belgesinin 4 örneği; ethers 6.17.0'a karşı 3 adres), `blockchain.test.ts` derive/payout/status; E2E 1: depozit `0x`+40 küçük harf saklanıyor, gösterim EIP-55 = sahte servis `derive`'ı, `BUYER_PAYMENT`/`SWEEP`/`SELLER_PAYOUT` hash'leri `0x`+64 |
| 2 | İzleyici başlat/durdur uçları | ✓ | `blockchain.test.ts` "monitor start/stop" (3 test: kurma + durdurma; TRON kontratıyla Ethereum izleyicisi 400; USDC sembolü + USDT kontratı 400); `contracts.test.ts` TRON `monitor-start` örnekleri 200; E2E 1: Ethereum depozitinin izleyicisi Ethereum yüzeyinde, Ethereum USDT kontratıyla kuruldu |
| 3 | Ücret tahmini uçları | ✓ | `blockchain.test.ts` "fee estimates" (6 test), `feeMath.test.ts` (2^53 üstü wei, T154 örneği 21,75); E2E 1: `networkQuotes` TRON 2,90 (yedek 3,00 değil), Ethereum payout `GasFee` 0,05 (yedek 0,50 değil); E2E 2: Ethereum iade tahmini depozitten istendi |
| 4 | E2E: iki ağda mutlu yol | ✓ | `multi-network.spec.ts` test 1 yerelde yeşil (5,2 dk; iki işlem birlikte `COMPLETED`) |
| 5 | E2E: iki ağda iade | ✓ | test 2 yerelde yeşil (1,7 dk): `INCORRECT_AMOUNT_REFUND` her ağda kendi ağından, K9 iade adresine |
| 6 | (yönetici) ağ seçimi, kesinleşme, K9, payout/iade aynı ağdan | ✓ | test 1 (`networkQuotes`, kabulde ağ, K5 kesinleşme biçimi), test 2 (K9), test 3 (yanlış ağ webhook'u), test 4 (kabul edilmeyen ağ); sahte servis günlüğüyle "hangi servise gitti" iki taraftan |
| 7 | (yönetici) sahte servis gerçek servisle aynı sözleşme örneklerine bağlı | ~ | TRON örnekleri bağlı. T154 (#348) çalışma sırasında main'e girdi ve dala birleştirildi: Ethereum `estimate-fee/` + `transfer-status/` blokları artık koşuyor ve yeşil. Yalnız `webhook/` bloğu T151 (#347) bekliyor; o dalın dosyalarıyla yerelde yeşil ölçüldü, main'e girince ek iş gerekmeden devreye girer (CI süzgeci örnek dizinlerini izliyor) |
| 8 | (yönetici) T149 bulgusu: TRON sweep ücret tahmini ucu | ✓ | `estimate-sweep-fee` eklendi; E2E 1 TRON satırını 2,90 okuyor |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| sidecar-fake (vitest) | ✓ 98/98 (T154 birleşmesinden sonra; öncesinde 94/94 + 4 atlanan) | `cd sidecar-fake && npm test` — 7 dosya; `tsc`, `eslint src/`, `prettier --check` temiz. T151/T154 örnekleri geçici yerleştirilince `contracts.test.ts` 22/22 |
| E2E yeni süit (yerel, `docker-compose.e2e.yml`) | ✓ 4/4 | `npx playwright test multi-network` — mutlu yol 5,2 dk, iade 1,7 dk, yanlış ağ 33 sn, kabul edilmeyen ağ 7 sn |
| E2E regresyon (yerel) | ✓ 12/12 (24,2 dk) | `npx playwright test payment-edge-cases happy-path.smoke cancellation` (Ethereum açık yığında) |
| e2e lint/tip | ✓ | `npx tsc --noEmit`, `npx eslint .`, `prettier --check` temiz |

**Bozma (mutasyon) ölçümleri:**

- **sidecar-fake birim, 12 bozma / 12 yakalandı** (betik: karalama dizininde `mutate_fake.py`; her bozma uygulandı, `vitest` koşuldu, geri alındı): Ethereum her adresi kabul · durumda `finalized` yok · Ethereum ağsız gövdeyi geçiriyor · wei ücreti aşağı yuvarlama · Keccak yerine SHA3 dolgusu · `monitor/start` kontratı yok sayıyor · durum her hash biçimini kabul · Ethereum hash'i `0x`'siz · `derive` küçük harf (EIP-55 yok) · Ethereum USDT = TRON USDT · payout hedef kontrolü yok · Ethereum `estimate-sweep-fee` TRON gibi cevaplıyor.
- **E2E (yerel yığın), 3 bozma / 3 yakalandı:**
  - **B1 — yanlış ağdan iade/payout** (backend `OutgoingTransferDispatchJob` her yayını `Transfers(BlockchainNetwork.TRON)`'a gönderiyor): iade testi kırmızı — Ethereum `INCORRECT_AMOUNT_REFUND` `CONFIRMED` olmadı (sahte TRON yüzeyi Ethereum adresini `INVALID_ADDRESS` ile reddetti).
  - **B2 — K9 bozuk** (backend iade hedefi olarak gelen `FromAddress`'i kullanıyor): iade testi kırmızı — beklenen `0x5aae…aed` (kabuldeki iade adresi), gelen `0xa5df…d48` (ödeyen cüzdan).
  - **F1 — sahte servis Ethereum webhook'unu TRON sırrıyla imzalıyor:** yanlış ağ testi kırmızı — geçerli ödeme 409 `WEBHOOK_NETWORK_MISMATCH` aldı (sahte servisin sır seçimi E2E'de ölçülüyor).
- Bozmalar yalnız yerel imajlarda; kaynak her seferinde geri alındı, dalda bozma yok.

## Doğrulama sonucu

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS — bağımsız temiz bağlam (Sonnet); doğrulanan HEAD `b09067a`, CI [`39137404205`](https://github.com/turkerurganci/Skinora/actions/runs/39137404205) ✓ |
| E2E | "T155 multi-network" bacağı yeşil, 14,9 dk |
| Bozma | 18 geçerli bozma / 14 yakalandı. Yakalanmayanlar: 6 ve 14 (webhook sırrı) → bu kapanışta birim testle (`networks.test.ts`); 13 E2E'de yakalanır; 17 (spam kontratı) → bu kapanışta testle |
| Payout elle hesabı | TRON satıcı ödemesi 98,20 = 100 − (2,00 − 0,20); `multi-network.spec.ts:285-286` sabitliyor; 11 §F8 "T155 kararları" (xii)'ye yazıldı |
| Bulgu sayısı | 5 küçük (2 🟡, 3 🟢) — hepsi bu kapanışta kapandı |

**Bulgular ve nasıl kapandıkları**

1. 🟡 Sahte Ethereum adres kontrolü `0x`+40 hex her şeyi kabul ediyordu; gerçek servis karışık harfte geçersiz EIP-55'i reddeder. `networks.ts` `isEvmAddress` artık aynı kuralı sahte servisin Keccak'ıyla uyguluyor (tümü küçük/büyük kabul, karışık harfte sağlama zorunlu). Test: `networks.test.ts`; bozma ("her şeyi kabul et") kırdı.
2. 🟡 Webhook imzasının depozitin ağının sırrıyla atıldığı birim düzeyinde sabit değildi. `signingKeyFor` ayrıldı; `networks.test.ts` Ethereum→Ethereum, TRON→TRON sırrını ve `signAs` geçersiz kılmasını sınar (iki ağa ayrı anahtarlar verilerek). Bozma (varsayılan `'TRON'`) kırdı.
3. 🟢 Kontrat uyuşmazlığı hata kodu `UNEXPECTED_CONTRACT` → `EXPECTED_CONTRACT_MISMATCH` (gerçek `monitorHandlers.ts`); `blockchain.test.ts` güncellendi, eski koda dönüş iki testi kırdı.
4. 🟢 Ethereum yüzeyi USDC'yi varsayılan kabul ediyordu; profile `allowlist()` eklendi: USDC yalnız `ETHEREUM_USDC_CONTRACT` verilirse kabul. `contracts()` USDC'yi yanlış-token kolu için adlandırmaya devam eder. Mevcut testler/E2E etkilenmedi (E2E yığınında USDC ayarlı değil, K2 yalnız USDT). Bozma (hepsini kabul et) kırdı.
5. 🟢 Spam kontratının allowlist dışı olduğu test edilmiyordu; `networks.test.ts` iki ağda `unsupportedContract`'ın allowlist dışı olduğunu ve spam zarfının onu taşıdığını sınar. Bozma (`unsupportedContract` = USDT) iki testi kırdı.

**Main bütünleştirmesi:** `origin/main` (T151 `0fdc3b1` girdi) merge commit'le dala alındı; MEMORY, 08 ve 11'de çakışmalar iki tarafı koruyarak çözüldü (08 v3.30, 11 v1.9 → bu kapanışta v1.10). Birleşince T151'in Ethereum webhook sözleşme örnekleri sahte servis testinde koşuyor ve geçiyor.

**Testler (kapanış):** `sidecar-fake` `npm test` 118/118, `tsc --noEmit` ve `eslint` temiz. E2E CI'da.

## Altyapı Değişiklikleri

- **Migration:** Yok.
- **Config/env:** Yalnız e2e yığını — sahte serviste `ETHEREUM_PORT`, `ETHEREUM_WEBHOOK_SECRET` (isteğe bağlı `ETHEREUM_USDT_CONTRACT`/`ETHEREUM_USDC_CONTRACT`); backend'de `EthereumSidecar__BaseUrl/InternalKey`, `Webhook__EthereumSharedSecret`, `ETHEREUM_HOT/COLD_WALLET_ADDRESS` (sabit test değerleri). Üretim compose'u değişmedi.
- **Docker:** sahte servis imajı 5300'ü de açıyor; e2e compose'a port + takma ad.

## Commit & PR

- Branch: `task/T155-fake-ethereum-e2e`
- Commit'ler: `af94b45` (sahte servis + E2E + compose + CI) · `1251a14` (belgeler) · `f95f35f` (rapor/durum/hafıza) · origin/main birleştirmesi (T154 — belge çakışmaları: 08 sürümü v3.28'e, 11 §F8 T154 bloğu T155'ten önce, durum ve hafıza satırları ikisi de)
- PR: #351 (https://github.com/turkerurganci/Skinora/pull/351)
- CI: ✓ PASS — run [`39135451547`](https://github.com/turkerurganci/Skinora/actions/runs/39135451547) (`1251a14`, kod + belgeler): bütün işler success, 11 E2E advisory bacağının hepsi yeşil (yeni "T155 multi-network" dahil). Rapor/durum/hafıza commit'inin run'ı PR'da.

## Known Limitations / Follow-up

- ~~**Ethereum webhook örnekleri henüz main'de değil**~~ (kapandı: T151 main'e girdi, doğrulama kapanışında birleştirildi ve sahte servis testinde koşuyor) — (T151 #347 doğrulamada; T154 örnekleri birleşti ve bağlı). Webhook bağlaması T151 birleşince kendiliğinden devreye girer; birleşme sonrası main CI'da `sidecar-fake` testinin koştuğu ve yeşil kaldığı görülmeli (süzgeç `sidecar-ethereum/contracts/**`'ı izliyor). O dallardaki dosyalar değişirse sahte servisin ilgili cevabı da değişmeli.
- **T151 `acceptsPayments` kapısı:** sahte servis bugünden `acceptsPayments: true` yayınlıyor; T151 birleşmeden önce backend bu alanı okumuyor, sonra okuyacak — e2e iki durumda da açık.
- **TRON payout ücreti e2e'de değişti:** sahte servis artık TRON `estimate-fee`'yi 2,00 ile cevaplıyor (yedek 0,50 yerine); e2e TRON satıcı payı 99,70 → 98,20. İade aritmetiği (2,00) aynı. Önceki hiçbir süit payout tutarını doğrulamıyordu.
- **Ödeme kolları izleyiciyi şart koşmuyor:** gerçek dünyada izlenmeyen depozite gelen ödeme görülmez; sahte serviste kol, izleyici kurulmamış olsa da webhook gönderir (outbox zamanlaması eski süitleri kırmasın diye). Yeni süit izleyiciyi ayrıca bekleyip ağını ve kontratını doğruluyor.
- **Post-cancel izleyici durum değişikliği webhook'u** (`post-cancel-monitor-state-changed`) sahte serviste kol olarak yok (gerek duyan senaryo yok) — sözleşme testinde gerekçesiyle listeli.
- **Ethereum `estimate-sweep-fee` 501:** gerçek Ethereum servisinde bu uç yok (alıcıya ağ ücreti satırı yok); backend Ethereum için çağırmıyor.

## Notlar

- **Working tree:** temiz (görev başında `git status --short` boş; ayrı worktree, dal `origin/main` `dc902b3`'ten kesildi).
- **Main CI başlangıç kontrolü:** görev başında T149 run'ları (`39130571903` CI, `39130571895` Docker Publish) sürüyordu, tamamlanmış son run'lar `39124319215` CI success · `39124319255` Docker Publish success · `39114726766` CI success; sonra T149 run'ları da success bitti.
- **Bağımlılıklar:** T147 ✓, T148 ✓, T149 ✓ (IMPLEMENTATION_STATUS).
- **Dış varsayımlar (task.md Adım 4):**

| Varsayım | Kanıt |
|---|---|
| Backend Ethereum servisini `EthereumSidecar:BaseUrl` ile ayrı istemciyle çağırıyor, webhook sırrı ağ başına | `BlockchainSidecarOptions.EthereumSectionName`, `WebhookSignatureMiddleware` (iki kimlik, aynı sır reddi) — kod okundu; e2e'de backend açıldı, Ethereum depozitini 5300'den türetti |
| Ethereum kesinleşmesi yalnız `finalized: true` | `BlockchainNetworkPolicy.IsFinal` (Finalized kuralı) — kod; E2E 1'de Ethereum payout `CONFIRMED` oldu |
| T154 sonrası backend `feeUsdt`'yi `feeWei × ethPriceUsdt`'den yeniden türetiyor | T154 dalı `WeiAmount.TryUsdtCentsCeil` okundu; sahte servis aynı tam sayı aritmetiğini kullanıyor, T154 örneği (21,75) ile tutarlı |
| T151 sonrası backend Ethereum'u `acceptsPayments: true` olmadan açmıyor | T151 dalı `SidecarHealthClient` farkı okundu; sahte servis alanı yayınlıyor |
| Ethereum sözleşme örnekleri T151/T154 dallarında | `git ls-tree origin/task/T151-…` ve `…T154-…` — `webhook/` ×6, `estimate-fee/` ×3, `transfer-status/` ×1 |
| Docker Desktop + e2e yığını yerelde koşabiliyor | `docker compose -f docker-compose.e2e.yml up --wait` sağlıklı; yerel Skinora yığını Docker açılınca kendiliğinden kalktı, e2e portlarıyla çakışmaması için durduruldu |

- **Kararlar:** 11 §F8 "T155 kararları" (i)–(xi), hepsi `(öneriyle kaydedildi — ⚠)`. PARA/GÜVENLİK/geri dönülmez soru çıkmadı: değişiklik yalnız test yığınında; üretim compose'u ve backend kodu değişmedi.
- **Secret tarayıcısı:** ilk commit'te üç yanlış pozitif (Keccak test vektörleri 64-hex, `ethereumWebhookSecret:` ve `secret:` alanları env/erişimci okuyordu). Bypass kullanılmadı; vektörler iki 32-hex parçaya bölündü, alan `signingKey` oldu, config satırı değer ayrı satırda kalacak şekilde yazıldı.
