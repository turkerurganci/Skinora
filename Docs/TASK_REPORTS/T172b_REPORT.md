# T172b — İmza servislerinde depozitten iade günlük tavanı (⚠ PARA)

**Faz:** F9 | **Durum:** ✓ Tamamlandı — doğrulama ✓ PASS (4 × S4; S1/S2/S3 yok — bkz. "Doğrulama", "Kapanış") | **Tarih:** 2026-10-23

**Kaynak:** proje sahibi kararı 2026-10-23 (modal) — T169a'nın backend'e koyduğu "depozitten iadeye ağ başına günlük 100 $" tavanı imza servislerinde de olsun; backend ele geçirilse ya da hatalı olsa bile imzalayıcı bu toplamı aşan depozit iadesini imzalamasın. Sıra: mainnet pilotundan (T173) önce. T169a'nın açık bıraktığı sınır (`DepositRefundLimitBackendOnly`, T169a dalında) bununla kapanır.

## Yapılan İşler

### Ne ayırt ediliyor: "depozitten iade" — servisin kendi bilgisi

- **TRON (`sidecar-blockchain`):** servisin depozit anahtarıyla imzaladığı yalnız iki akış var — sweep (hedef servisin kendi `HOT_WALLET_ADDRESS`'ine sabit, para platformda kalır) ve iade ailesi (`POST /api/transfer/refund`, hedefi çağıran seçer). Depozit anahtarı istekteki indeksten **servis tarafından türetilir** ve istekteki adresle karşılaştırılır (`DEPOSIT_ADDRESS_MISMATCH`). Dolayısıyla `/refund`'un **her** çağrısı depozitten iadedir; tavanı kapatabilecek bir istek alanı (iade türü, kaynak) yoktur — TRON iade gövdesi tür taşımaz. Sayım `RefundService.refund` içinde.
- **Ethereum (`sidecar-ethereum`):** imza planındaki `purpose === 'REFUND' && signer === 'DEPOSIT'` — `signer` istekten okunmaz; depozit yolu her zaman türetilmiş depozit anahtarıyla imzalar. `source: "HOT_WALLET"` ile gelen iade sıcak anahtarla imzalanır ve zaten sıcak cüzdan 24 saatlik tavanına (`ETHEREUM_MAX_DAILY_OUTFLOW_USDT`, 9.000) girer: bir saldırganın "kaynak" alanıyla depozit tavanından kaçması, parayı daha sıkı denetlenen sıcak cüzdan yoluna sokmak demektir (orada da depozit boşsa para yoktur). Sweep sabit hedefli, sayılmaz.

### Pencere, saklama, eşzamanlılık

| | TRON | Ethereum |
|---|---|---|
| Ayar | `DEPOSIT_REFUND_DAILY_LIMIT_USDT` | `ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT` |
| Pencere | kayan 24 sa (`at >= now − 24 sa`), tavana eşit geçer | kayan 24 sa (`createdAt`), tavana eşit geçer |
| Sayım kaynağı | yeni `DepositRefundLedger` — servisin volume'ündeki `deposit-refunds.json` (`DEPOSIT_REFUND_LEDGER_DIR`, volume `skinora-tron-deposit-refunds`), geçici dosya → fsync → yeniden adlandırma | mevcut işlem günlüğü (`TxJournal`, zaten yayından önce diske yazılıyor) |
| Neden zincirden değil | her depozit ayrı adres; "bugün bütün depozitlerden ne çıktı" zincire tek soruda sorulamaz | aynı sebep |
| Kayıt ne zaman | TRX harcanmadan ve imzadan **önce** | imzalı kayıt yayından önce (T152 kuralı) |
| Yeniden başlatma | dosya volume'de → sayım korunur | günlük volume'de → korunur |
| Eşzamanlılık | okuma-karşılaştırma-yazma senkron (Node arada başka işe geçmez) | bütün depozit iadeleri tek `deposit-refund-cap` kilidi altında denetlenip günlüğe yazılır (gönderen kilitleri adres başına olduğu için tek başına yetmiyordu) |
| Sayılmayan | yalnız **kesin** ağa çıkmamış girişim (kurulamadı / enerji adımı transferden önce düştü / anahtar yok) | geri dönen (`mined.status 0`) ve nonce çakışmalı kayıt (para taşımadı) |
| Yeniden deneme | her imza girişimi yeniden sayılır (aynı kimliği tekilleştirmek saldırgana açık kapı olurdu) | günlükteki kimlik yeniden yayınlanır, ikinci kez sayılmaz (zaten sayıldı) |
| USD karşılığı | USDT/USDC 1:1 (ikisi de 6 ondalık, mevcut tavanlar gibi) | aynı |

### Reddetme biçimi — bekler, kaybolmaz

- Tavan aşılırsa, ayar boş/bozuksa ya da (TRON) defter açılamıyorsa: hiçbir şey imzalanmaz, TRX/ETH harcanmaz, yakıt gönderilmez. Cevap **503 `{ error, deferred: true, retryable: true, waitingSince }`** — Ethereum servisinin T152 bekleme biçimi. TRON servisine aynı biçimi veren `TransferDeferredError` eklendi.
- Kodlar: `DEPOSIT_REFUND_DAILY_LIMIT_EXCEEDED`, `DEPOSIT_REFUND_LIMIT_NOT_CONFIGURED`, (TRON) `DEPOSIT_REFUND_LEDGER_UNAVAILABLE`.
- **Backend:** `HttpBlockchainTransferClient` `deferred` bayrağını zaten `TransferBroadcastStatus.Deferred` okuyordu (dispatch işi satırı `PENDING` tutar, deneme hakkı harcamaz, 1 dk sonra yeniden sorar, `FAILED` yapmaz). Ek olarak bu üç kod **bayrak olmasa da** bekleme okunur (sürüklenmiş bir servis 400 dönse bile iade `FAILED` olup yeni kimlikle yeniden açılmasın) ve backend log'una Error satırı düşer. Dispatch işine dokunulmadı (T169a ile çakışma yok).
- **Alarm:** her servis `…_deposit_refund_limit_refusals_total{reason}` sayacı + `…_deposit_refund_window_usdt` / `…_deposit_refund_daily_limit_usdt` göstergeleri yayımlar; Grafana `tron-deposit-refund-limit-refused` ve `ethereum-deposit-refund-limit-refused` (kritik, 1 sa içinde > 0, `for: 0m`). Backend tarafında satır beklemeye devam ettikçe mevcut `outbound-transfer-stuck` de görür.
- **Açılış:** TRON servisi defter açılamasa da açılır (izleme ve sweep sürer), log'a `Deposit refund daily limit: NOT usable … every refund waits` yazar. Ethereum'da bozuk değer servisi açmaz (`INVALID_CONFIG` — servisin mevcut tavan kuralı), boş değer iadeleri bekletir.

### Karar tablosu

| # | Karar | Kaynak |
|---|---|---|
| 1 | Tavan 100 $, ağ başına, imza servislerinde | proje sahibi kararı 2026-10-23 (modal) |
| 2 | Kayan 24 sa, tavana eşit geçer, USDT/USDC 1:1 | T169a ile aynı (öneriyle kaydedildi — ⚠) |
| 3 | Aşan / ayarsız / defteri bozuk iade **bekler** (503 deferred), reddedilmez | en güvenli seçenek: `FAILED` → yeni kimlikle yeniden açma tavanı delerdi (öneriyle kaydedildi — ⚠ PARA) |
| 4 | Compose'da varsayılan **yok** (`:-`), `.env.example` 100 — boş = imzalanmaz | mevcut tavanların fail-closed kuralı (öneriyle kaydedildi — ⚠); yükseltmede mevcut `.env`'e satır eklenmezse bütün iadeler bekler (runbook §B uyarısı) |
| 5 | TRON'da belirsiz biten yayın (reddedildi / ağ hatası) sayılmaya devam eder; yalnız kesin ağa çıkmamış girişim silinir | fail-closed (öneriyle kaydedildi — ⚠ PARA); bedeli: aynı satırın 3 başarısız denemesi tavanı 3 kez tüketebilir, pencere 24 saatte boşalır |
| 6 | TRON'da defter açılamazsa servis açılır, iadeler bekler | izleme/sweep'in durması daha büyük kesinti (öneriyle kaydedildi — ⚠) |
| 7 | Ethereum sıcak cüzdan iadesi depozit tavanına girmez (sıcak cüzdan tavanında) | T169a "Ethereum sweep öncesi" tanımıyla aynı (öneriyle kaydedildi — ⚠) |
| 8 | Backend bu üç kodu bayraksız da bekleme okur | derinlemesine savunma (öneriyle kaydedildi — ⚠) |
| 9 | Tavanın üstü yalnız **sunucuda tek seferlik izinle** (AD42 imza servisinin tavanını kaldırmaz); env yükseltme son çare | **proje sahibi kararı (2026-10-23, modal)** — aşağıda |
| 10 | İzin tutarı **tam eşleşme** (üst sınır değil) | öneriyle kaydedildi — ⚠ PARA: iade tutarı satırda kesin bellidir; üst sınır, aynı kimlik + alıcı için başka tutarlı bir isteğe yer açardı; yazım hatası iadeyi bekletir (güvenli yön) |
| 11 | İzin ayrıca iade kimliğine (`BlockchainTransaction.Id`) bağlı; geçerlilik varsayılan ve en çok 24 sa (en az 1 dk) | öneriyle kaydedildi — ⚠ |
| 12 | İzin yalnız tavan aşılınca okunur (sığan iade izni tüketmez); ayar boş / defter bozuk iken izin işe yaramaz | öneriyle kaydedildi — ⚠ PARA (izin yalnız tavanı aşar, fail-closed kuralları değil) |
| 13 | TRON: transfer kesin ağa çıkmadıysa izin geri verilir, belirsiz yayında verilmez; Ethereum: izin imza + günlük kaydından hemen önce tüketilir (yakıt/ücret beklemesi izni yakmaz) | öneriyle kaydedildi — ⚠ PARA |
| 14 | "İki kişi kuralı" teknik olarak zorlanmıyor (komut tek operatörle çalışır; `--by` + `--reason` zorunlu, log Loki'ye gider) | öneriyle kaydedildi — ⚠ |

### Sunucuda tek seferlik izin — proje sahibi kararı (2026-10-23, modal)

İlk turdaki "⚠ SAHİBE SORU" (100 $ üstü tek iade / AD42 imza serviste geçmez; env yükseltme mi, istisna mekanizması mı?) sahip kararıyla cevaplandı: **sunucuda tek seferlik izin.**

- **Komut, HTTP yok:** `docker exec <servis> node dist/cli/depositRefundPermit.js create --tx <BlockchainTransaction.Id> --to <alıcı> --amount <tutar> --by <operatör> --reason <gerekçe> [--hours ≤24]`; ayrıca `list`, `revoke --id`. İzin servisin kendi volume'üne yazılır (TRON `DEPOSIT_REFUND_LEDGER_DIR/deposit-refund-permits/`, Ethereum `ETHEREUM_TX_JOURNAL_DIR/deposit-refund-permits/`; izin başına bir dosya, geçici dosya → fsync → yeniden adlandırma). API katmanında izin koduna hiçbir atıf yok (test bunu iki serviste de denetler) — backend ya da ağ üzerinden izin yaratılamaz.
- **Bağlar:** ağ (`TRON`/`ETHEREUM` — öbür servisin izni geçmez), iade kimliği, alıcı adresi (kanonik), **tam tutar**, geçerlilik (`expiresAt`).
- **Tek kullanım:** izin imzadan önce diske "kullanıldı" yazılır; ikinci imza bulamaz. İzinli iade yine 24 saatlik toplama yazılır (TRON defter girişi `permitId`, Ethereum günlük kaydı `depositRefundPermitId`) — tavan yalnız o iade için aşılır, başkalarına açılmaz.
- **Log + metrik:** `created` (komut, Loki'ye), `seen`, `used`, `restored`, `expired` (5 dk'da bir ve her tavan aşımında taranır; bir kez), `revoked` — `skinora_blockchain_deposit_refund_permits_total{event}` / `skinora_ethereum_deposit_refund_permits_total{event}`.
- **Ortak kod:** `transfer/DepositRefundPermits.ts` ve `cli/permitCli.ts` iki serviste bayt bayt aynı (parite testi); ağ ve adres doğrulaması parametre.
- Runbook §D adım 2 artık izin komutu; env yükseltme son çare olarak kaldı.

## Etkilenen Modüller / Dosyalar

- `sidecar-blockchain/src/transfer/DepositRefundLimit.ts` (yeni) — defter + tavan; `RefundService.ts` (rezervasyon, serbest bırakma kuralı); `errors/SidecarError.ts` (`TransferDeferredError`); `api/transferHandlers.ts` (503 deferred), `api/routes.ts` (belge); `config/index.ts`, `index.ts` (bağlama + açılış log'u); `metrics.ts`; `Dockerfile` (dizin); `contracts/transfer-refund/deferred.response.json` (yeni)
- `sidecar-ethereum/src/transfer/TransferGuard.ts` (`assertDepositRefundDailyLimit`, `depositRefundsLast24h`), `TransferService.ts` (tek kilit + denetim), `FeePolicy.ts` (iki bekleme sebebi), `config/index.ts`, `wiring.ts`, `metrics.ts`; `contracts/transfer-refund/deferred.response.json` (yeni)
- `backend/src/Modules/Skinora.Transactions/Application/Transfers/HttpBlockchainTransferClient.cs` — `SignerDepositRefundLimitCodes`
- `docker-compose.yml` (iki env, `DEPOSIT_REFUND_LEDGER_DIR`, volume `skinora-tron-deposit-refunds`), `.env.example`, `infra/grafana/provisioning/alerting/rules.yml` (iki kural)
- İzin: `sidecar-{blockchain,ethereum}/src/transfer/DepositRefundPermits.ts`, `src/cli/permitCli.ts`, `src/cli/depositRefundPermit.ts` (yeni); `DepositRefundLimit.ts` / Ethereum `TransferGuard.ts`, `TransferService.ts`, `TxJournal.ts` (izin alanı), `wiring.ts`, iki `index.ts`, iki `metrics.ts`
- Testler: `sidecar-blockchain/src/transfer/DepositRefundLimit.test.ts` (yeni, 25; kapanışta 32), `DepositRefundPermits.test.ts` (yeni, 22), `TransferService.test.ts`, `api/transferHandlers.test.ts`, `config/composeWiring.test.ts`, `alertRuleWiring.test.ts`; `sidecar-ethereum/src/transfer/TransferService.test.ts` (+9 tavan, +13 izin/parite), `Fuel.test.ts`, `api/transferHandlers.test.ts`, `config/index.test.ts`, `config/composeWiring.test.ts`, `alertRuleWiring.test.ts`, `wiring.test.ts`; `backend/tests/Skinora.Transactions.Tests/Contract/DepositRefundLimitDeferredContractTests.cs` (yeni, 6)
- Belgeler: `Docs/plan/11_F9.md` (v1.29, T172b), `Docs/05_TECHNICAL_ARCHITECTURE.md` (v3.37, §3.3), `Docs/08_INTEGRATION_SPEC.md` (v3.48, §3.3a, §3.7.6), `Docs/DEPLOY_RUNBOOK.md` (§B, §C.3, §D, §M.4, §O), `Docs/DOC_CHANGELOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| Kriter | Durum | Kanıt |
|---|---|---|
| İki servis depozit iadesinin ağ başına kayan 24 sa toplamını tutar, 100 $'ı aşanı imzalamaz | ✓ | TRON `DepositRefundLimit.test.ts` "boundary", ETH `T172b — deposit refund daily cap` "exactly to the cap" |
| Ayrım istek alanına değil servisin bilgisine dayanır | ✓ | TRON: tavan `/refund`'un hepsine, anahtar türetilir ("sweeps and payouts do not count … every refund counts"); ETH: `signer` plandan ("sweeps and hot-wallet refunds are not deposit refunds", guard birim testi) |
| Yeniden başlatma sıfırlamaz | ✓ | TRON "a new process on the same volume", "entry is on disk BEFORE the broadcast"; ETH "a restart does not reset it" |
| Eşzamanlı iki iade birlikte aşamaz | ✓ | TRON "60 + 60 against a cap of 100, issued together"; ETH "two refunds from two deposits at once" |
| Env boş/bozuk → hiçbir depozit iadesi imzalanmaz | ✓ | TRON `it.each` (boş, `1e2`, `0`, 7 ondalık), ETH "cap unset" + `config/index.test.ts` (bozuk → `INVALID_CONFIG`) |
| Reddedilen istekte backend'in anlayacağı kod; iade kaybolmaz, bekler + uyarı | ✓ | handler sözleşme testleri (iki servis) + backend `DepositRefundLimitDeferredContractTests` (aynı JSON dosyaları → `Deferred`); alarm kuralları + `alertRuleWiring` testleri |
| USDT/USDC 1:1 | ✓ | TRON "USDC counts 1:1", ETH guard birim testi |
| Kritik satırlar bilerek bozulup yakalandı | ✓ | aşağıda — 16/16 |
| Tek seferlik izin (sahip kararı 2026-10-23, modal): sunucuda komut, HTTP yok, ağ + kimlik + alıcı + tutar + süre, tek kullanım, izinli iade sayılır, log + metrik | ✓ | TRON `DepositRefundPermits.test.ts`, Ethereum `T172b — a one-time permit …` (yanlış adres/tutar/ağ/kimlik, süresi dolmuş, iptal, ikinci kullanım, eşzamanlı, tavanın başkalarına açılmaması, bekleme izni yakmaz, HTTP katmanı izne dokunmaz) |
| `.env.example`, deneme env, DEPLOY_RUNBOOK, 05/08 | ✓ | deneme yayını `.env.trial`'ı `.env.example`'dan kopyalar (§K.4); runbook §B'de mevcut `.env.trial`'a ekleme uyarısı |

## Test Sonuçları

| Kapsam | Komut | Sonuç |
|---|---|---|
| TRON servisi (tamamı) | `cd sidecar-blockchain && npx vitest run` | 37 dosya, **721/721** ✓ (HEAD `7c86f507`; önce 654 — rapor ilk yazıldığında 706'ydı, izin testleri sonra eklendi) · kapanış: **728/728** |
| Ethereum servisi (tamamı) | `cd sidecar-ethereum && npx vitest run` | 37 dosya, **928/928** ✓ (HEAD `7c86f507`; rapor ilk yazıldığında 921) · kapanış: **929/929** |
| Lint + build | `npm run lint && npm run build` (iki servis) | temiz |
| Backend Transactions (tamamı) | `dotnet test tests/Skinora.Transactions.Tests` | 1.888 test: 1.885 ✓ + 3 ✗ — üçü mutasyon ölçümünden kalan bayat DLL (`--no-build`); yeniden derleyince ilgili 256 test (`Contract`, `HttpBlockchainTransferClient`, `OutgoingTransferDispatchJob`) **256/256** ✓ |
| Compose | `docker compose -f docker-compose.yml [-f docker-compose.trial.yml] config`, `node --test scripts/compose-exposure.test.mjs` | geçerli; deneme overlay'inde env + `skinora-trial_skinora-tron-deposit-refunds` volume'ü; 10/10 ✓ |

### Bilerek bozma (mutasyon) — yapım turu: tavan 16/16, izin 13/13 yakalandı

Bu tur pencere **uzunluğunu** (24 sa → 12 sa) ve defter **şemasını** (geçerli JSON, yanlış biçim) bozmadı; doğrulayıcının turunda ikisi kaçtı (S4-1, S4-2; kapanışta kapandı). Doğrulayıcının kendi turu: **28 bozmadan 26'sı yakalandı**; kapanış turu aşağıda "Kapanış".

| # | Bozulan satır | Yakalayan |
|---|---|---|
| T1 | TRON sınır `>` → `>=` | 5 test |
| T2 | TRON pencere filtresi kaldırıldı | 2 |
| T3 | TRON defter diske yazılmıyor (yeniden başlatma sıfırlar) | 4 |
| T4 | TRON yayından **sonra** say | eşzamanlılık testi tek başına (+15 diğer) |
| T5 | TRON boş ayar = sınırsız | 5 |
| T6 | TRON her hatada serbest bırak | 2 |
| T7 | TRON handler bekleme cevabı vermiyor | 1 |
| T8 | TRON tavan denetimi yok | 21 |
| E1 | Ethereum sınır `>` → `>=` | 4 |
| E2 | Ethereum ortak kilit yok | 1 (eşzamanlılık) |
| E3 | Ethereum pencere filtresi yok | 2 |
| E4 | Ethereum sıcak cüzdan iadesini de sayar | 2 |
| E5 | Ethereum boş ayar = sınırsız | 1 |
| E6 | Ethereum tavan denetimi yok | 6 |
| E7 | Ethereum geri dönen kaydı da sayar | 1 |
| B1 | Backend bayraksız kodu bekleme okumuyor | 3 |
| P1 | İzin: alıcı eşleşmesi yok | 1 |
| P2 | İzin: tutar üst sınır gibi (`>=`) | 1 |
| P3 | İzin: süre denetimi yok | 1 |
| P4 | İzin: ağ denetimi yok | 1 |
| P5 | İzin: iade kimliği denetimi yok | 1 |
| P6 | TRON izni tüketmiyor (tek kullanım yok) | 4 |
| P7 | TRON izinli iadeyi toplama yazmıyor | 2 |
| P8 | TRON kesin ağa çıkmamışta izni geri vermiyor | 1 |
| P9 | İptal edilmiş izin geçer | 1 — ilk koşuda **kaçtı** (ikinci katman `consume` reddettiği için iade yine bekledi); test depo düzeyinde güçlendirildi |
| E8 | Ethereum izni denetimde tüketir (bekleme yakar) | 5 |
| E9 | Ethereum izni tüketmiyor | 2 |
| E10 | Ethereum iade kimliğini yanlış iletir | 1 — ilk koşuda **kaçtı** (bütün izin testleri aynı kimlikteydi); "başka iade kimliği" testi eklendi |
| E11 | Ethereum ortak kilit yok (izinle) | 1 (E2'nin eşzamanlılık testi) |

## Doğrulama

| Tur | HEAD | Verdict | Bulgular | Sonuç |
|---|---|---|---|---|
| Bağımsız doğrulama (alt ajan, Opus high) | `7c86f507` | ✓ PASS | 4 — S4: 4 (S4-1 ⚠ PARA, S4-2, S4-3 ⚠ PARA, S4-4 ⚠ PARA) | Kriterlerin hepsi ✓; derlenmiş kodla Docker'sız uçtan uca prova (CLI'nin yazdığı izni servis kodu tüketti; yeniden başlatmadan sonra sayım korundu; bozuk defter → bekler). Bozma 26/28 yakalandı; kaçan iki bozma S4-1/S4-2 oldu. Dört S4 kapanışta kapandı |

Kaynak rapor: `C:\Users\teknodeveloper\.claude\projects\c--projects-Escrow\f9-handoff\T172b_VALIDATION.md` (2026-10-23T18:26:26Z). Yapım raporu uyuşmazlıkları (test sayıları, belge sürümleri, bozma kapsamı, saat etkisinin tarifi) bu raporda düzeltildi.

## Altyapı Değişiklikleri

- Yeni env: `DEPOSIT_REFUND_DAILY_LIMIT_USDT`, `ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT` (`.env.example` 100; compose varsayılansız — boş = bekler), `DEPOSIT_REFUND_LEDGER_DIR` (compose sabit).
- Yeni volume: `skinora-tron-deposit-refunds` (TRON servisi, `/var/lib/skinora-tron/deposit-refunds`; Dockerfile dizini oluşturur).
- Yeni alarmlar: `tron-deposit-refund-limit-refused`, `ethereum-deposit-refund-limit-refused` (kritik).
- Migration / SystemSetting yok (backend ayarı T169a'nın).
- Docker kullanılmadı (yalnız `docker compose config` doğrulaması).

## Commit & PR

- Dal: `task/T172b-imza-servisi-iade-tavani` (origin/main `792f8ed2`'den; main `fcc94d8d` — T169a #376 — merge commit ile alındı)
- Commit'ler: `f1220cf8` (servisler), `07aadcf2` (compose/env/alarm/backend), `8ba6cd35` (belgeler), `24c14548` (PR no), `9e6e5f20` (biçim düzeltmesi — kayıtlı bypass ile koordinatör push etti), `bccba273` (BYPASS_LOG), `672c33cf` (tek seferlik izin), `1b7e0fa6` (main birleştirmesi), sonrası belgeler
- PR: #378
- CI: run 39990390212 (`24c14548`) ✗ — yalnız Lint (prettier, `sidecar-ethereum/src/wiring.test.ts`); düzeltme `9e6e5f20` kayıtlı bypass ile, run 39993478110 ✓ success. Doğrulanan HEAD `7c86f507` run 39998139306 ✓ success (bütün bloke edici işler ve advisory E2E). Kapanış HEAD'inin run'ı "Kapanış"ta.

## T169a ile ilişki

- T169a (#376) main'e girdi (`fcc94d8d`) ve bu dala merge commit ile alındı. Kodda çakışma olmadı; çakışmalar belgelerde (başlık sürümleri: plan v1.29, 05 v3.37, 08 v3.48 — main'deki T169a sürümlerinin bir üstü), `IMPLEMENTATION_STATUS` / `STATUS_CHANGELOG` / `BYPASS_LOG` / memory'de çözüldü.
- DEFERRED_BACKLOG `DepositRefundLimitBackendOnly` ✅ (T172b) — backlog 50/168 → 49/169.
- 05 §3.3 ve 08 §3.3c'deki "ele geçirilmiş backend'e karşı koruma değildir" cümleleri ikinci katmana (T172b) çevrildi; runbook §M.4 adım 4: AD42 imza servisinin tavanını kaldırmaz → tek seferlik izin.
- Davranış birlikte: backend tavanı tutar; geçirdiği iade imza servisinde de sayılır. Aynı değerdeyken servis yalnız ayrışmada (AD42, tek büyük iade, backend hatası/ele geçirilmesi) reddeder. Backend'in sayımı `FAILED` satırları düşer, servisinki belirsiz yayınları düşmez → servis biraz daha sıkı olabilir.

## Kapanış (2026-10-23)

**Girdi:** doğrulama ✓ PASS (HEAD `7c86f507`), 4 × S4.

| Bulgu | Durum |
|---|---|
| S4-1 ⚠ PARA — TRON pencere uzunluğu testte sabit değildi (testler sabiti modülden alıyordu; 12 sa'e indirmek yeşil kalıyordu) | **Kapandı (test):** "the window is exactly 24 hours — pinned by a literal" — süre testte elle yazılı (`24 * HOUR`): 12 sa ve 23 sa 59 dk'da iade bekler, 24 sa + 1 ms'de geçer. Bozma: pencere **12 sa** → 1 test kırıldı; **36 sa** → 2 test kırıldı |
| S4-2 — geçerli JSON ama yanlış biçimli TRON defteri için test yoktu | **Kapandı (test):** `it.each` 4 durum (`{}`, `[{"foo":1}]`, `units:"abc"`, `at:"now"`) → defter açılmaz, iade `DEPOSIT_REFUND_LEDGER_UNAVAILABLE` ile bekler, hiçbir şey imzalanmaz/harcanmaz. Bozma: `isEntry` denetimi atlandı → 3 test kırıldı (`{}` dizi denetiminde zaten düşüyor) |
| S4-3 ⚠ PARA — saat ileri sıçrayınca TRON defterindeki sayılmış girişler dosyadan kalıcı siliniyordu | **Kapandı (kod + test):** `DepositRefundLimit.reserve` 24 saati yalnız **sayımda** uygular; dosyadan silme `DEPOSIT_REFUND_RETENTION_MS` = **7 gün** sonra. İleri tarihli giriş (saat ilerideyken yazılmış) gerçek saat onu geçene kadar sayılır. Testler: "a jump ahead and back does not erase what was counted" (90 → +30 sa'te 5 → saat geri: 10 daha bekler, yeniden başlatmada da), "an entry stamped in the future keeps counting", "entries leave the FILE only after 7 days". Bozmalar: saklama = 24 sa (eski davranış) → 2 test; ileri tarihli giriş sayılmaz → 2 test. **Ethereum:** işlem günlüğü kayıt silmez (`TxJournal`'da silme yolu yok), kalıcı etki yok — eş test "S4-3 twin: clock jumps ahead, then back — the journal keeps every record"; bozma (ileri tarihli kayıt sayılmaz) → 1 test kırıldı. Runbook §D adım 5 (saat notu, NTP), 08 §3.3a |
| S4-4 ⚠ PARA — runbook §D adım 2.4: belirsiz yayından sonra yeni izinden önce zincir kontrolü yoktu | **Kapandı (runbook):** yeni izin yazmadan önce: defterde `txHash` varsa gezginde o hash; yoksa ya da Ethereum'da depozit ve alıcı adresinin token hareketleri + depozit bakiyesi; transfer görünüyorsa yeni izin **yazılmaz** (iade ikinci kez çıkar); görünmüyorsa birkaç dakika bekleyip bir kez daha bakılır |

**Kapanış bozma turu:** 6 bozma (TRON pencere 12 sa, 36 sa, `isEntry` atlama, saklama = pencere, ileri tarihli giriş sayılmaz; Ethereum ileri tarihli kayıt sayılmaz) → **6/6 yakalandı**; her birinden sonra dosya geri yüklendi.

**Birleştirme (main `44d32f54`, T165 #375):** kapanış commit'i `0c55cce7` push edildikten sonra main ilerledi; merge commit ile alındı. Çakışmalar yalnız belgelerde: 05 → **v3.38** (T165 v3.37 aldığı için bir üstü), runbook / 05 başlıkları birleşik, DOC_CHANGELOG, BYPASS_LOG (kronolojik), STATUS_CHANGELOG (main'in T165 ✓ başlığı taşındı), IMPLEMENTATION_STATUS, MEMORY. **T165'in para kuralı testi:** `tron-/ethereum-deposit-refund-limit-refused` kurallarına `category: money` eklendi, `NotificationAlertingProvisioningTests.MoneyRules`'a girdi, sayım 28 → **30**; runbook §P.4 tabloya satır, §P.6 sayı. Backlog sayımı değişmedi (49/169).

**Testler (kapanış):** TRON `npx vitest run` **728/728** (37 dosya) · Ethereum **929/929** (37 dosya) · iki serviste `tsc --noEmit` ve `eslint src/` temiz · prettier (değişen dosyalar) temiz. Backend'e dokunulmadı. Docker kullanılmadı.

## Known Limitations / Follow-up

- Tavandan büyük tek iade / AD42 için yol artık sunucuda tek seferlik izin (sahip kararı 2026-10-23, modal); izin komutu konteyner içinde çalışır — Docker'la canlı prova yapılmadı (birim testleri + derleme), T172 deneme yayınında bir kez denenmeli.
- TRON'da belirsiz biten (ağa çıkmış olabilecek) yayın girişimi sayılmaya devam eder; zincirden txid ile doğrulayıp düşmek (TRON işlemi ~60 sn'de süresi dolar) sonraki bir iyileştirme olabilir.
- TRON defter volume'ü silinirse (`down -v`) son 24 saatin sayımı unutulur — çift ödeme yaratmaz, tavan o gün için iki katına açılabilir (runbook §D madde 4).
- Saat geri alınırsa girişler pencerede daha uzun kalır (daha sıkı). **İleri sıçrarsa** sıçrama sürdükçe eski girişler sayımın dışında kalır (tavan erken açılır). Doğrulama öncesi TRON defteri pencereden çıkan girişleri o anki saate göre **dosyadan kalıcı** siliyordu (S4-3); kapanışta girişler dosyada 7 gün tutuluyor, saat düzelince yeniden sayılıyor. Yalnız **7 günden büyük** ileri sıçrama TRON'da girişleri kalıcı siler (runbook §D adım 5: NTP). Ethereum işlem günlüğü kayıt silmez. Konak erişimi gerektirir; backend tetikleyemez.
