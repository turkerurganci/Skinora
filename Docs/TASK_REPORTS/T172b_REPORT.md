# T172b — İmza servislerinde depozitten iade günlük tavanı (⚠ PARA)

**Faz:** F9 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-10-23

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
| 9 | **Tavandan büyük tek iade ve AD42 serbest bırakması imza servisinde de geçmez**; yol: iki kişi kuralıyla env'i geçici yükselt → servisi yeniden oluştur → geri al (runbook §D) | ⚠ SAHİBE SORU (aşağıda) — şimdilik en güvenli seçenek uygulandı |

### ⚠ SAHİBE SORU

1. **100 $'dan büyük tek iade / AD42 ile serbest bırakılan iade:** işlem tutarı 10–2.900 $ olduğu için 100 $'ın üstündeki her depozit iadesi (TRON'da bütün iadeler depozitten) imza servisinde **hiçbir pencerede** geçmez; backend'deki AD42 serbest bırakması imza servisinin tavanını kaldırmaz (kaldırsaydı ele geçirilmiş backend/admin de kaldırabilirdi — sahip kararının amacı bu). Şimdiki yol runbook §D: env'i geçici yükseltip servisi yeniden oluşturmak (sunucu erişimi gerekir, uygulamada iz bırakmaz). Alternatif: imza servisinde tek seferlik, süreli, kimliğe bağlı bir istisna (ör. çevrimdışı anahtarla imzalı izin dosyası). Hangisi?
2. **Değer eşitliği:** imza servisinin değeri backend'inkinden (T169a ayarı, admin panelinden değiştirilebilir) bağımsızdır; runbook ikisinin aynı tutulmasını ister. Backend değeri artırılıp env unutulursa iadeler serviste bekler (alarm çalar) — bu bilinçli: admin paneli imza servisinin tavanını açamamalı.

## Etkilenen Modüller / Dosyalar

- `sidecar-blockchain/src/transfer/DepositRefundLimit.ts` (yeni) — defter + tavan; `RefundService.ts` (rezervasyon, serbest bırakma kuralı); `errors/SidecarError.ts` (`TransferDeferredError`); `api/transferHandlers.ts` (503 deferred), `api/routes.ts` (belge); `config/index.ts`, `index.ts` (bağlama + açılış log'u); `metrics.ts`; `Dockerfile` (dizin); `contracts/transfer-refund/deferred.response.json` (yeni)
- `sidecar-ethereum/src/transfer/TransferGuard.ts` (`assertDepositRefundDailyLimit`, `depositRefundsLast24h`), `TransferService.ts` (tek kilit + denetim), `FeePolicy.ts` (iki bekleme sebebi), `config/index.ts`, `wiring.ts`, `metrics.ts`; `contracts/transfer-refund/deferred.response.json` (yeni)
- `backend/src/Modules/Skinora.Transactions/Application/Transfers/HttpBlockchainTransferClient.cs` — `SignerDepositRefundLimitCodes`
- `docker-compose.yml` (iki env, `DEPOSIT_REFUND_LEDGER_DIR`, volume `skinora-tron-deposit-refunds`), `.env.example`, `infra/grafana/provisioning/alerting/rules.yml` (iki kural)
- Testler: `sidecar-blockchain/src/transfer/DepositRefundLimit.test.ts` (yeni, 25), `TransferService.test.ts`, `api/transferHandlers.test.ts`, `config/composeWiring.test.ts`, `alertRuleWiring.test.ts`; `sidecar-ethereum/src/transfer/TransferService.test.ts` (+9), `Fuel.test.ts`, `api/transferHandlers.test.ts`, `config/index.test.ts`, `config/composeWiring.test.ts`, `alertRuleWiring.test.ts`, `wiring.test.ts`; `backend/tests/Skinora.Transactions.Tests/Contract/DepositRefundLimitDeferredContractTests.cs` (yeni, 6)
- Belgeler: `Docs/plan/11_F9.md` (v1.28, T172b), `Docs/05_TECHNICAL_ARCHITECTURE.md` (v3.35, §3.3), `Docs/08_INTEGRATION_SPEC.md` (v3.47, §3.3a, §3.7.6), `Docs/DEPLOY_RUNBOOK.md` (§B, §C.3, §D, §O), `Docs/DOC_CHANGELOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`

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
| `.env.example`, deneme env, DEPLOY_RUNBOOK, 05/08 | ✓ | deneme yayını `.env.trial`'ı `.env.example`'dan kopyalar (§K.4); runbook §B'de mevcut `.env.trial`'a ekleme uyarısı |

## Test Sonuçları

| Kapsam | Komut | Sonuç |
|---|---|---|
| TRON servisi (tamamı) | `cd sidecar-blockchain && npx vitest run` | 36 dosya, **684/684** ✓ (önce 654) |
| Ethereum servisi (tamamı) | `cd sidecar-ethereum && npx vitest run` | 37 dosya, **907/907** ✓ |
| Lint + build | `npm run lint && npm run build` (iki servis) | temiz |
| Backend Transactions (tamamı) | `dotnet test tests/Skinora.Transactions.Tests` | 1.888 test: 1.885 ✓ + 3 ✗ — üçü mutasyon ölçümünden kalan bayat DLL (`--no-build`); yeniden derleyince ilgili 256 test (`Contract`, `HttpBlockchainTransferClient`, `OutgoingTransferDispatchJob`) **256/256** ✓ |
| Compose | `docker compose -f docker-compose.yml [-f docker-compose.trial.yml] config`, `node --test scripts/compose-exposure.test.mjs` | geçerli; deneme overlay'inde env + `skinora-trial_skinora-tron-deposit-refunds` volume'ü; 10/10 ✓ |

### Bilerek bozma (mutasyon) — 16/16 yakalandı

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

## Altyapı Değişiklikleri

- Yeni env: `DEPOSIT_REFUND_DAILY_LIMIT_USDT`, `ETHEREUM_DEPOSIT_REFUND_DAILY_LIMIT_USDT` (`.env.example` 100; compose varsayılansız — boş = bekler), `DEPOSIT_REFUND_LEDGER_DIR` (compose sabit).
- Yeni volume: `skinora-tron-deposit-refunds` (TRON servisi, `/var/lib/skinora-tron/deposit-refunds`; Dockerfile dizini oluşturur).
- Yeni alarmlar: `tron-deposit-refund-limit-refused`, `ethereum-deposit-refund-limit-refused` (kritik).
- Migration / SystemSetting yok (backend ayarı T169a'nın).
- Docker kullanılmadı (yalnız `docker compose config` doğrulaması).

## Commit & PR

- Dal: `task/T172b-imza-servisi-iade-tavani` (origin/main `792f8ed2`'den)
- Commit'ler: `f1220cf8` (servisler), `07aadcf2` (compose/env/alarm/backend), belgeler commit'i
- PR: #PR_NO
- CI: aşağıda (PR açıldıktan sonra)

## T169a ile ilişki

- T169a (PR #376) henüz birleşmedi; bu dal onun kodunu içermez. Çakışma beklenen dosyalar: `Docs/plan/11_F9.md`, `Docs/IMPLEMENTATION_STATUS.md`, `Docs/DOC_CHANGELOG.md`, `Docs/08_INTEGRATION_SPEC.md`, `Docs/05_TECHNICAL_ARCHITECTURE.md`, `Docs/DEPLOY_RUNBOOK.md`, `.env.example`, `infra/grafana/provisioning/alerting/rules.yml` (kurallar farklı yere eklendi), `.claude/memory/MEMORY.md`. Kodda çakışma yok (dispatch işine dokunulmadı; `HttpBlockchainTransferClient` T169a'da değişmiyor).
- İkisi birleşince: DEFERRED_BACKLOG `DepositRefundLimitBackendOnly` (T169a dalında açılıyor) T172b ile ✅ kapatılmalı; 08 §3.3c (T169a) ile §3.3a'daki T172b satırı birbirine atıf vermeli.
- Davranış birlikte: backend tavanı tutar (satır `DepositRefundLimitHeldAt`, 15 dk'da bir), geçirdiği iade imza servisinde de sayılır. İkisi aynı değerdeyken servis yalnız ayrışmada (AD42, tek büyük iade, backend hatası/ele geçirilmesi) reddeder. Backend'in sayımı `FAILED` satırları düşer, servisinki belirsiz yayınları düşmez → servis biraz daha sıkı olabilir (iade bekler, alarm çalar).

## Known Limitations / Follow-up

- **⚠ SAHİBE SORU 1** — tavandan büyük tek iade / AD42 için imza servisinde istisna mekanizması yok (yukarıda).
- TRON'da belirsiz biten (ağa çıkmış olabilecek) yayın girişimi sayılmaya devam eder; zincirden txid ile doğrulayıp düşmek (TRON işlemi ~60 sn'de süresi dolar) sonraki bir iyileştirme olabilir.
- TRON defter volume'ü silinirse (`down -v`) son 24 saatin sayımı unutulur — çift ödeme yaratmaz, tavan o gün için iki katına açılabilir (runbook §D madde 4).
- Saat geri alınırsa girişler pencerede daha uzun kalır (daha sıkı); ileri alınırsa erken düşer — konak erişimi gerektirir.
