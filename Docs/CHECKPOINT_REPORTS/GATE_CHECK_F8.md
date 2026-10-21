## Gate Check Sonucu — F8 Çok Ağlı Ödeme (TRON + Ethereum)

**Tarih:** 2026-10-21
**Task aralığı:** T141–T160 (+ T159a, T159b)
**Toplam task:** 22 (T160 bu gate'in kendisi; ölçülen 21 görev T141–T159b)
**PR:** [#361](https://github.com/turkerurganci/Skinora/pull/361) (dal `chore/f8-gate-check`)
**Ölçüm commit'i:** `c1026b8c` (main = origin/main; dal `chore/f8-gate-check` buradan kesildi, gate'in kendi değişiklikleri yalnız belge/kayıt)
**Yapan:** F8 yönetici düzeninde temiz bağlamlı gate alt ajanı (Opus high). Tag atılmadı — `phase/F8-pass` sahibin aşağıdaki "Sahip onayı bekleyen kararlar" listesine bakmasından sonra yönetici tarafından atılır.

### Verdict: ✓ PASS

**0 bloke edici bulgu (S1/S2 yok, traceability boşluğu 0).** 7 bloke etmeyen bulgu (F8-N1 … F8-N7, aşağıda). Biri güvenlik açısından ciddi ama F8 kaynaklı değil: **F8-N4 — frontend `next@16.2.3` için 3 critical npm danışması** (F8'de frontend bağımlılığı değişmedi; canlıya çıkmadan, F9'da kapanmalı → `DEFERRED_BACKLOG` 🔴 `FrontendNextAdvisories`). Sahip bunu F8 tag'i için bloke edici saymak isterse tag'i Next yükseltmesinden sonraya bırakabilir; bu gate F8'in kendi teslimini ölçtüğü için bloke etmedi.

---

### Ön Kontrol

| # | Kontrol | Sonuç |
|---|---|---|
| 1 | Faz tamamlanma | ✓ 21/21 görev (T141–T159, T159a, T159b) `IMPLEMENTATION_STATUS.md`'de `✓ Tamamlandı`; `⛔ BLOCKED` / `✗ FAIL` yok. T159 "KOŞULLU PASS" idi; koşulu (giden bacağın müdahalesiz yeniden provası) T159b ✓ PASS ile kapandı |
| 2 | Rapor tutarlılığı | ✓ 21/21 `Docs/TASK_REPORTS/TXXX_REPORT.md` mevcut ve her birinde doğrulama sonucu bölümü PASS ile kapanmış. **Bayatlık düzeltildi:** 14 raporun başlık satırı (`**Durum:**`) hâlâ "⏳ Devam ediyor / doğrulama bekliyor" diyordu (T141–T148, T150–T152, T156, T158, T159); gövdedeki PASS bölümüne ve status tablosuna göre `✓ Tamamlandı` + PR numarasıyla düzeltildi (F8-N1) |
| 3 | Giriş kapısı — main CI | ✓ Son main run'lar `success`: `39532564128` CI + `39532564136` Docker Publish (`c1026b8c`, T159b merge teyidi) · `39529062386` CI + `39529062379` Docker Publish (`5be40559`, #359). `c1026b8c` ↔ `5be40559` farkı yalnız belge (6 dosya: rapor, status, backlog, memory, BYPASS_LOG) — kod ağacı aynı, bu yüzden `c1026b8c`'nin CI'ı test job'larını atladı; tam koşu `39529062386`'da (11 E2E leg dahil hepsi success) |
| 4 | Working tree / HEAD | ✓ Temiz; `HEAD` = `origin/main` = `c1026b8c` (gate dalının çıkışı) |
| 5 | `IMPLEMENTATION_STATUS.md` bayat satır taraması | ✓ "doğrulama bekliyor" yazan F8 satırı **yok** (hazırlık taslağının işaret ettiği satırlar önceki kapanışlarda düzeltilmişti); bayatlık §7.6'da ve rapor başlıklarındaydı — ikisi de bu gate'te düzeltildi |

**F8 görev listesi (21 + gate):** T141 · T142 · T143 · T144 · T145 · T146 · T147 · T148 · T149 · T150 · T151 · T152 · T153 · T154 · T155 · T156 · T157 · T158 · T159 · T159a · T159b · (T160).

---

### Test Sonuçları (regresyon — F0–F8 aynı koşuda)

**Yerel koşum (2026-10-21, `c1026b8c` ağacı, Release, `--no-build`):** backend integration + contract için **ayrılmış** tek SQL Server 2022 container'ı (`skinora-gate-f8-sql`, `localhost:14338`, `INTEGRATION_TEST_SQL_SERVER` — CI modeli), test projeleri **tek tek, seri**. Filtreler CI ile aynı: unit `!~.Integration&!~.Contract`, integration `~.Integration`, contract `~.Contract`.

| Katman | Tür | Sonuç | Detay |
|---|---|---|---|
| Backend | Unit | ✓ **2218/2218** | 11 proje: API 116 · Auth 92 · Disputes 25 · Fraud 19 · Notifications 150 · Platform 161 · Realtime 44 · Shared 571 · Steam 59 · Transactions 932 · Users 49; 0 fail / 0 skip |
| Backend | Integration (SQL Server) | ✓ **1843/1843** | 10 proje: API 750 · Admin 22 · Auth 40 · Disputes 58 · Fraud 82 · Notifications 60 · Payments 6 · Platform 74 · Shared 16 · Transactions 735; 0 fail / 0 skip (en uzunları API 6 dk 23 sn, Transactions 5 dk 25 sn) |
| Backend | Contract | ✓ **96/96** | API 24 · Shared 5 · Transactions 67 |
| Backend | **Toplam** | ✓ **4157/4157** | F7 gate: 2848 → **+1309** (F7 sonrası backlog turları + F8) |

**Kapsam paritesi (yerel ↔ CI, proje bazında):** aynı kod ağacının main CI run'ı `39529062386` job logları — unit (job `118991951595`) 571 · 49 · 92 · 161 · 44 · 19 · 932 · 59 · 25 · 150 · 116 = **2218**; integration (job `118991951436`) 16 · 22 · 735 · 6 · 40 · 74 · 82 · 58 · 60 · 750 = **1843**; contract (job `118991951530`) 5 · 67 · 24 = **96**. Yerel sayılar **proje bazında birebir aynı** (toplama güvenilmedi, her proje tek tek karşılaştırıldı). `Skinora.Admin.Tests` ve `Skinora.Payments.Tests` unit filtresine, `Skinora.Realtime.Tests` ve `Skinora.Steam.Tests` integration filtresine hiç düşmüyor (F7'de de böyleydi — eksik assembly değil).

| Katman | Tür | Sonuç | Detay |
|---|---|---|---|
| Frontend | eslint | ✓ 0 bulgu | `npm run lint` exit 0 |
| Frontend | i18n parity | ✓ 4 dil × **1578** anahtar, aynı küme | 20 advisory "untranslatable" uyarısı (04 §10.4 verbatim terimleri, bloke etmez; F7: 1285 anahtar / 15 uyarı) |
| Frontend | vitest | ✓ **391/391** | 41 dosya (F7: 152 / 14) |
| sidecar-steam | vitest | ✓ **138/138** | 9 dosya (F7: 83) |
| sidecar-blockchain (TRON) | vitest | ✓ **585/585** | 30 dosya (F7: 166) |
| sidecar-ethereum (yeni) | vitest | ✓ **868/868** | 36 dosya; `eslint` exit 0 |
| sidecar-fake | vitest | ✓ **120/120** | 8 dosya (F7: 38); `eslint` exit 0 |
| scripts | `node --test` | ✓ **37/37** | `scripts/gpt-ask.test.mjs` |
| E2E (Playwright) | main CI | ✓ **11/11 leg success** | run `39529062386` (`5be40559`, kod ağacı = `c1026b8c`): happy-path, happy-path UI, T108 cancellation, T109 timeout, T110 payment edge cases, T111 fraud-flags, T112 emergency-hold, T113 admin-flows, T114 downtime, T138 delivery, **T155 multi-network (yeni)** |

**Biçim kontrolü notu:** yerelde `npm run format:check` (frontend) 290 dosyada uyarı verdi — bu makinede `core.autocrlf=true` olduğu için CRLF artefaktıdır (bilinen durum, repo hafızası `e2e-prettier-crlf-local-artifact`). Yetkili kontrol CI "1. Lint" job'ıdır (LF checkout) ve `39529062386`'da success.

**Kırılan test: 0 → S2 (Kırılma) yok.** F0–F7'den gelen her test aynı koşuda yeşil.

---

### Build

| Proje | Sonuç | Kanıt |
|---|---|---|
| Backend (`Skinora.sln`, Release) | ✓ (uyarılı) | `dotnet restore` + `dotnet build -c Release`: **0 Error, 13 Warning** — 2 dk 17 sn. F7 tabanı 0W/0E → **F8-N2** (nullable akış uyarıları; derlemeyi kırmıyor, CI uyarıyı hata saymıyor) |
| Frontend (Next.js 16.2.3) | ✓ | `npm ci --include=optional` + `npm run build` exit 0 — "Compiled successfully", **37 route** (F7: 34; yeni `/[locale]/admin/wallets` dahil) |
| sidecar-steam | ✓ | `npm ci` + `tsc --noEmit` + `npm run build` exit 0 |
| sidecar-blockchain (TRON) | ✓ | aynı, exit 0 |
| sidecar-ethereum (yeni) | ✓ | aynı, exit 0 |
| sidecar-fake | ✓ | aynı, exit 0 |

---

### Docker Compose

**Yalıtım (görev talimatı):** yerel `escrow` yığınının volume'larına (Nile/Sepolia prova DB'si, `skinora-ethereum-tx-journal` vb.) **dokunulmadı**. Smoke ayrı bir compose projesiyle koşuldu: `docker compose -p skinora-gate -f docker-compose.yml --profile ethereum --env-file <geçici env> …`. `config` çıktısında bütün volume ve secret adları `skinora-gate_*` önekliydi; `escrow` etiketli konteyner yoktu (yığın kapalı). Sonunda yalnız bu proje `down -v --rmi local` ile silindi; `escrow_*` 8 volume'un 8'i duruyor, `escrow_skinora-db-data` (oluşturma 2026-07-30T16:04:41Z) ve `escrow_skinora-ethereum-tx-journal` (2026-10-19T02:05:23Z) ölçüm öncesiyle aynı. Docker Desktop kapatılmadı.

**Ortam:** sırlar sahte — rastgele 64 hex iç anahtar / JWT / webhook sırları, Ethereum tohumu herkese açık test tohumu, iki imza anahtarı boş (imza yeteneği yok); RPC anahtarsız `ethereum-sepolia-rpc.publicnode.com`, TRON Nile `nile.trongrid.io`; Sepolia token adresleri herkese açık (T159'un Tether kopyası + Circle Sepolia USDC). Şema host'tan `dotnet ef database update` ile uygulandı (backend açılışta migrate etmez — DEPLOY_RUNBOOK §G.2 deseni), DB host portu 14339.

| Kontrol | Sonuç | Kanıt |
|---|---|---|
| `docker compose config --quiet` | ✓ exit 0, **0 uyarı** | 13 servis (`ethereum` profili dahil) |
| `build --no-cache` (5 uygulama imajı) | ✓ | backend · frontend · sidecar-steam · sidecar-blockchain · sidecar-ethereum **5/5 Built**, 2 dk 33 sn. İlk deneme durduruldu ve yerel `node_modules`/`dist` silindi (F8-N7 gözlemi); önbellekli ikinci deneme de 5/5 idi, kesin kanıt için `--no-cache` koşuldu |
| `up -d` — servisler | ✓ **12/12 `healthy`** | db · redis · loki · backend · frontend · steam · blockchain (TRON) · **ethereum** · prometheus · grafana · uptime-kuma · reverse-proxy; `grafana-provisioning` tek seferlik, `Exited (0)` |
| Sağlık uçları | ✓ **10/10 HTTP 200** (+ 1 yanlış URL denemesi 404) | backend `:5000/health` (sqlserver + redis healthy) · reverse-proxy `:8080/health` + `:8080/api/v1/platform/maintenance` · frontend `:3000/api/health` · steam `:5100/health` · TRON `:5200/health` (`network: TRON`, solid block 72.005.965) · **Ethereum `:5300/health`** (`network: ETHEREUM`, `chainId 11155111`, `finalized` 11.959.478, yaş 1.170 sn < 30 dk alarm eşiği) · prometheus `/-/healthy` · loki `/ready` · grafana `/api/health`. `:5000/health/ready` 404 — böyle bir uç tanımlı değil (yanlış URL'yi ben denedim, bulgu değil) |
| Cevap verenin kimliği | ✓ | `skinora-backend` imajı `skinora-gate-skinora-backend`, compose projesi `skinora-gate`, başlangıç 02:54:00Z — az önce `--no-cache` kurulan imaj. Sağlık cevabındaki `uptime` alanı `Environment.TickCount64` (Docker VM'inin açık kalma süresi), süreç süresi değil |
| Ethereum ödeme kapısı | ✓ beklendiği gibi | USDT kontratı verilmeden `acceptsPayments: false` (`USDT_NOT_CONFIGURED`) ve backend bunu tasarım gereği "sağlıksız" sayıp Ethereum'u yeni işlemlere kapattı (T151 kapısı); Sepolia Tether kopyası verilince servis `usdt: ok`, `acceptsPayments: true` |

---

### Migration Rehearsal

**Ortam:** ayrılmış, boş SQL Server 2022 container'ı (`skinora-gate-f8-sql`); hedef DB `SkinoraGateF8DryRun` (sıfırdan). Komut: `dotnet ef database update --no-build -c Release --project src/Skinora.Shared --startup-project src/Skinora.API --connection <ayrılmış DB>`.

| Kontrol | Sonuç | Kanıt |
|---|---|---|
| Temiz DB'ye ilk `database update` | ✓ | exit 0, `Done.` — `__EFMigrationsHistory` = **60** (F7: 40 → F7 sonrası backlog turları +11 → F8 **+9**); son kayıt `20261018211249_T158_PaymentAddressMonitorFromBlock` |
| İkinci `database update` (idempotency) | ✓ | exit 0, no-op; satır sayısı 60'ta kaldı |
| Model ↔ snapshot senkron | ✓ | `InitialMigrationTests.Model_HasNoPendingChanges` integration süitinde geçti; ayrıca CI "6. Migration dry-run" (`39529062386`) success |
| Seed data | ✓ | `SystemSettings` **75** (06 §8 "toplam 75 anahtar" ile birebir; F7: 63) — 9'u `blockchain.ethereum.*`; `blockchain.ethereum.fee_cap_usd_per_transfer` = **0.50** (T159b provasının geçici 1,00'i seed'e sızmamış); `SystemHeartbeats` 1; `Users` 1 (System) |
| F8 tabloları | ✓ | `UserWalletAddresses` (T146), `TransactionNetworkOptions` (T147), `OutboundRowClosures` (T157) **var** |
| Uyarı | not | EF model doğrulaması `User` ↔ `WalletAddressHistory` için "global query filter + required navigation" uyarısı (10622) veriyor — T145'ten beri bilinen desen, migration'ı etkilemiyor |

**F8 migration'ları (9):** `T145_NetworkDimension` · `T146_UserWalletAddresses` · `T148_NetworkSettings` · `T147_TransactionNetworkOptions` · `T149_NetworkFeePolicy` · `T150_OutboxRetryBackoff` · `T154_RealizedFeeWei` · `T157_OutboundRowClosures` · `T158_PaymentAddressMonitorFromBlock`.

---

### Faz-Spesifik Kontroller (FK8 — `Docs/plan/11_F8.md` §6)

| Kriter | Sonuç | Kanıt |
|---|---|---|
| **FK8-1** — iki ağda (Nile + Sepolia) mutlu yol, iade ve geç ödeme uçtan uca | ✓ (bir notla) | **Sepolia (canlı, müdahalesiz — T159b, PR #360):** mutlu yol `f7de324d` (`COMPLETED`, yakıt otomatik sweep + taze satıcı adresine payout), sweep öncesi iade `4d21e5dd`, geç ödeme `af2e8dd4` (`LATE_PAYMENT_REFUND` 10,20 → 9,28), ayrıca eksik/fazla/yanlış token iadeleri; 7/7 yakıt + 8/8 token gönderimi zincirde status 1; bağımsız doğrulayıcı 27/27 hash'i zincirden okudu. **Nile (canlı):** mutlu yol T159 `2fe0fd64` ve T159b `da4f02a3` (`COMPLETED`), iade T159b `e7305d8d` (eksik ödeme → K9 iade adresine, T159a kesintisi zincirde 2,67 → 2,30). **Not (F8-N3):** TRON **geç ödeme** F8'de Nile'da canlı koşulmadı; uçtan uca kanıtı sahte zincirde — E2E `payment-edge-cases.spec.ts` §5.4 (main CI ✓) + entegrasyon. TRON geç ödeme yolunda F8'in değiştirdiği iki şey (iade hedefi K9, gas kesintisi T159a) Nile eksik ödeme iadesinde aynı kodla canlı geçti. → T172'ye senaryo önerisi, backlog ⚪ |
| **FK8-2** — bir satışta ödeme, payout ve iade aynı ağda | ✓ | **Kod (bu gate'te okundu):** depozit ağı kabulde seçilen ağdan (`PaymentAddressAllocator`), payout satırı `SellerPayoutQueueJob.cs:278` `transaction.Network`, iade satırları `AmountValidationService.cs:689` (depozitin ağı, "Same-network rule (K3, T148)") ve `PaymentRefundToBuyerConsumer.cs:131` (`deposit?.Network ?? transaction.Network`), webhook satırları `paymentAddress.Network`; ağı depozitle uyuşmayan webhook reddedilir. Kabulde `SellerPayoutAddress` seçilen ağın adresinden sabitlenir (`CK_Transactions_Network_Accepted`). **Test:** E2E `multi-network.spec.ts` — "both networks: TRON and Ethereum sales complete on the network the buyer paid on", "refund on each network goes to the refund address fixed at acceptance (K9)", "a payment webhook naming the wrong network is refused" (main CI ✓). **Canlı:** T159b'de Sepolia işlemlerinin tüm çıkışları Sepolia'da, Nile işlemlerininki Nile'da (27/27 hash) |
| **FK8-3** — HD bilinen-cevap vektörleri bağımsız uygulamayla eşleşiyor | ✓ (bu gate'te yeniden üretildi) | Vektörler (`abandon … about` BIP-39 referans tohumu; TRON `m/44'/195'/0'/0/{i}`, Ethereum `m/44'/60'/0'/0/{i}`; i = 0, 1, 2, 3, 4, 1.000.000, 2.147.483.647) `sidecar-blockchain/src/wallet/HdWalletService.test.ts` ve `sidecar-ethereum/src/wallet/HdWalletService.test.ts`'te; T143'te `@scure`/`@noble` ile, T143 doğrulamasında `node:crypto` + elle Keccak ile üretilmişti. **Bu gate'te üçüncü bağımsız uygulama:** yalnız Python standart kütüphanesi (`hashlib.pbkdf2_hmac`, HMAC-SHA512) + elle yazılmış secp256k1, Keccak-256 (boş girdi vektörüyle sınandı), base58check ve EIP-55 — ethers / `@scure` / ürün kodu kullanılmadan → **14/14 eşleşti**. İki serviste Ethereum vektörleri birebir aynı. Canlı tamamlayıcı: T159'da Sepolia depozit indeks 0 bağımsız türetmeyle servisin açtığı ilk depozitle aynı çıktı |
| **FK8-4** — ekranda gösterilen tutar = beklenen tutar, 2 ondalık (sınır testleri) | ✓ | T144 (PR #340): `PayableAmountGridTests` (10,00–99,99 fiyat ızgarası + uç fiyatlar × komisyon oranları; toplam her zaman 2 ondalık, gösterilen = beklenen) — bu gate'in unit koşusunda (`Skinora.Transactions.Tests` 932/932) yeşil. Canlı: Nile toplam 2,02 / 3,04, Sepolia 10,20 / 5,10 — hepsi 2 ondalık ve zincirde tam o tutar eşleşti |
| **FK8-5** — Ethereum'da aynı ödeme iki kez yayınlanamıyor (yeniden deneme / ücret artırma / yeniden başlatma) | ✓ | T152 (PR #349) testleri, bu gate'te yeşil (`sidecar-ethereum` 868/868): "journals the signed bytes BEFORE the broadcast", "a second call with the same id signs nothing new and re-broadcasts the SAME bytes", "a restart (new process, same journal) re-sends the SAME bytes, never a new nonce", "bumps a head-of-line send … with the SAME nonce … only one lands", "a nonce used outside the journal STOPS signing"; yakıt için aynı aile (`Fuel.test.ts`: "a restart finds the journalled fuel and signs no second one", "re-broadcasts … with the same bytes", "bumps a stuck fuel at the same nonce"). Backend tarafı: Ethereum satırı geçici hatada `FAILED` olmaz, aynı kimlikle yeniden denenir (sahip kararı 2026-10-18; `OutgoingTransferDispatchJob`). Günlük kaybında aradaki satırları kapatma aracı T157 (PR #354). Canlı: T159b'de 8 token + 7 yakıt gönderimi, her depozitte tek sweep, hiçbir çift yayın yok (27/27 hash bağımsız okundu) |
| **FK8-6** — bir ağ dururken diğeri çalışıyor | ✓ | T148 (PR #343) entegrasyon testleri, bu gate'in SQL Server koşusunda yeşil: `PlatformHealthProbeJobTests.Tron_Outage_Freezes_Only_Tron_And_Ethereum_Keeps_Running`, `Ethereum_Outage_Freezes_Only_Ethereum`, `Unconfigured_Ethereum_Is_Never_An_Outage`; onay/gönderim işleri ağ başına döner (T152 rotasyon kriteri). **Canlı prova (bu gate'te, ayrı Docker yığınında):** iki servis de sağlıklıyken 150 sn boyunca Ethereum için olumsuz yoklama 0 → 03:09:31Z'de `skinora-ethereum-sidecar` durduruldu → 03:12:08Z'de backend yalnız `PLATFORM_OUTAGE_DETECTED BLOCKCHAIN_ETHEREUM {"status":"DEGRADED","consecutiveFailures":3}` yazdı; TRON servisi aynı anda `healthy`, TRON için tek bir kesinti kaydı yok → 03:12:18Z'de servis yeniden başlatıldı → 03:13:06Z'de `BLOCKCHAIN_ETHEREUM {"status":"RECOVERED"}`. (İlk denemede USDT kontratı verilmediği için Ethereum açılıştan "sağlıksız" sayılmıştı — ölçüm karıştı, atıldı; kontrat verilip yeniden koşuldu.) |

---

### Traceability ve Boşluk Taraması (§7.6)

**Yöntem:** `Docs/plan/11_F8.md` §7.6 tablosu satır satır okundu; her "Implemented" hücresi görev raporu + merge commit'iyle (`git log phase/F7-pass..HEAD`) eşlendi; boş ve bayat hücreler kanıtla düzeltildi (tablo yapısı bir betikle doğrulandı: 36 satır, hepsi 4 hücre, boş hücre 0).

| Kategori | Eşlenen | Implement | Boşluk (S3) | Not |
|---|---|---|---|---|
| §7.6 kaynak öğeleri | 36 satır | **36/36** | **0** | 34 satır yalnız F8 görevlerine eşli ve ✓; 2 kalem bilerek F9'a eşli (USDC'nin ürün yüzeyinden kalkması → T166; anahtarlı RPC limit probu → T169) — F8 boşluğu değil |
| F8 kabul kriterleri | 21 görev | **21/21** | 0 | Her görev ayrı doğrulama alt ajanında PASS; T141/T148/T152/T157/T158 bir FAIL turundan sonra |
| FK8 kapı kriterleri | 6 | **6/6** | 0 | FK8-1 notu F8-N3 |

**Bu gate'te §7.6'da düzeltilenler:** 20 hücredeki 21 bayat "doğrulama bekliyor" ibaresi → PR numarası; boş 3 hücre kanıtla dolduruldu (T144 iki ondalık · 0,50 $ ücret tavanı · Sepolia Tether kopyası); RPC satırının eksik Task hücresi eklendi (`T150, F9 T169` — satır 3 hücreydi); T159a için yeni satır (sabit gas yok + reddedilen iadelerde gas kesintisi, PARA sahip kararı); boşluk özetine "F8 Gate Check: 0" satırı. Plan dosyası v1.21, `DOC_CHANGELOG` girişli.

**Doküman uyumu taraması (enum · API · entity):**
- Pariteler makinece zorlanıyor ve bu koşuda yeşil: `Skinora.Shared.Tests` `EnumTests` (571 unit içinde), `frontend/src/types/enums.parity.test.ts`, `permissionCatalog.parity.test.ts`, bildirim şablonu paritesi 4 dilde, contract testleri 96/96.
- Nokta doğrulamalar: `BlockchainNetwork` = `TRON`, `ETHEREUM` (06 §1.4 ile aynı); `NETWORK_NOT_ACCEPTED` / `NETWORK_UNAVAILABLE` 07'de tanımlı ve `TransactionErrorCodes` + `TransactionsController`'da kullanılıyor; yeni entity'ler 06 §3.1a `UserWalletAddress`, §3.5b `TransactionNetworkOption`, §3.8b `OutboundRowClosure` ↔ temiz DB'de üç tablo; seed 75 anahtar = 06 §8.
- **Bulunan sapma (F8-N5):** `blockchain.ethereum.fee_cap_usd_per_transfer`'in seed açıklaması (`SystemSettingSeed.cs:216`, admin panelinde görünür) "Tavanın üstünde sweep/payout bekler, **iade beklemez**" diyor; T152 yönetici kararı (2026-10-18) ve 08 §3.7.7 "Ücret tavanı iadeyi de durdurur" — kod da iadeyi tavana bağlıyor. 06 §8 satırı da eksikti ("sweep/payout bekler") → **06 bu gate'te düzeltildi (v6.35)**; kod metni gate kapsamı dışında → backlog 🟡 `FeeCapSeedDescriptionStale`.

---

### Güvenlik Özeti

**F8'in açtığı açık güvenlik bulgusu: 0.** Görev doğrulamalarının güvenlik bulguları (T152 çift ödeme yolu, T156 sunucu tarafı `confirmLowercase` boşluğu, T158 doğrulanamayan gönderim durumu, T157 belirsiz satır eşleşmesi) kendi düzeltme turlarında kapandı.

**Yeni dış bağımlılıklar (F8 aralığı `6922b03c^..c1026b8c`, 24 commit):**

| Tür | Değişiklik | Değerlendirme |
|---|---|---|
| npm — yeni paket | `sidecar-ethereum` (yeni servis): `ethers` 6.17.0 · `express` 4.22.3 · `pino` 9.14.0 · `pino-loki` 2.6.0 · `prom-client` 15.1.3 (hepsi tam sürüm sabitli) | Hepsi repoda zaten kullanılan kütüphaneler (`ethers` 6.17.0 TRON servisinde `overrides` ile zaten sabitli). `npm audit --omit=dev` → **0 vulnerabilities**; CI'da `audit-level=high` bloke edici adım (T150) |
| npm — mevcut paket | `e2e/package.json` yalnız `test:network` script'i | bağımlılık değişikliği yok |
| NuGet | backend `*.csproj` diff **boş** | yeni paket yok (EIP-55 için Keccak-256 bağımlılıksız yazıldı, T145 kararı vi) |
| Frontend / sidecar-steam / sidecar-blockchain | `package.json` + lock diff **boş** | — |
| Dış servisler (yeni) | Ethereum RPC: Alchemy (birincil), Chainstack (yedek), PublicNode (anahtarsız geri düşüş, T151); ETH kuru: Binance + CoinGecko (iki kaynak şartı); Etherscan bağlantıları (yalnız UI linki) | Sağlayıcı anahtarları URL içinde env'den; anahtarlı limit probu yapılmadı → F9 T169 (⚠) |

**Auth / yetki değişiklikleri:**
- Kullanıcı adres uçları ağ başına (`PUT /users/me/wallet/{seller,refund}` + `network`): format ve EIP-55 onay kontrolü tek kullanımlık `X-ReAuth-Token` tüketilmeden önce; **yeni bir ağa ilk adres eklemek de Steam yeniden doğrulaması ister** (T146 xv / T147 xi, tür başına) — ele geçirilmiş oturum ikinci ödeme hedefi ekleyemez.
- Kabul ucunda sunucu da tümü küçük/büyük harfli EVM iade adresi için ikinci onay ister (T156 doğrulama bulgusu, kapandı).
- Admin: S22 `/admin/wallets` + AD33–AD39 (cüzdanlar, bakiyeler, giden satır kapatma aracı) `MANAGE_SETTINGS` iznine bağlı; kapatma iki aşamalı (öneren ≠ onaylayan, DB CHECK) + AuditLog. Ayrı `MANAGE_WALLETS` izni F9 sertleştirmesine bırakıldı (⚠ GÜVENLİK, backlog).
- Ethereum servisi: iç anahtar ≥ 32 karakter yoksa açılmaz; webhook HMAC sırrı TRON'unkinden ayrı (aynı sır → açılmaz, T148); ağı uyuşmayan webhook reddedilir.

**Sır yönetimi:** Ethereum tohumu ve iki imza anahtarı yalnız Docker secret (`/run/secrets/*`), env yedeği yok — kod düzeyinde testle sabit (T158 (c)); `docker-compose.yml` sırları yalnız `skinora-ethereum-sidecar`'a bağlar; backend imza malzemesi görmez. Repo sır taraması: `secrets/` gitignored (yalnız README izlenir). Bu gate'in Docker smoke'u için üretilen sahte değerler (rastgele anahtarlar, herkese açık test tohumu) worktree'nin gitignored `secrets/` dizininde ve scratch env dosyasında kaldı, hiçbiri commit'lenmedi.

**F8 dışı ama bu gate'te ölçülen açık güvenlik maddeleri:**
- **F8-N4 🔴 — frontend `next@16.2.3`:** `npm audit --omit=dev` 3 critical danışma (kimlik doğrulamasız RCE: Image Optimization + AVIF; Windows'ta barındırılan sunucu; `next/og` ImageResponse) + çok sayıda high (middleware/proxy atlatma, SSRF, DoS); transitive `postcss`, `sharp`, `ws`, `nanoid`, `source-map-js` high. npm'in önerdiği düzeltme `next@16.4.2` (semver-major değil). F8 regresyonu **değil** (frontend lock F8'de değişmedi, danışmalar sonradan yayımlandı); CI'da frontend audit adımı **yok**. Canlıya çıkmadan F9'da kapanmalı.
- Bilinen, sahip "kabul edilen risk" kayıtlı: sidecar-steam 4 critical (`request`/`protobufjs`, upstream düzeltme yok), sidecar-blockchain `axios`/`lodash` high (`tronweb` 5.3.5 altında) — `DEFERRED_BACKLOG` `sidecar-npm-audit`.
- `auth.min_steam_account_age_days` = 1 (prova ayarı, üretim 30) → F9 T166.

---

### Bulgular (bloke edici 0)

| # | Seviye | Açıklama | Etkilenen | Öneri / durum |
|---|---|---|---|---|
| **F8-N1** | S3 (kayıt) | 14 görev raporunun başlık `**Durum:**` satırı bayattı ("⏳ Devam ediyor / doğrulama bekliyor") — gövdede PASS bölümü vardı. Aynı şekilde §7.6'da 20 hücrede "doğrulama bekliyor" ibaresi, 3 boş hücre, 1 eksik Task hücresi | T141–T148, T150–T152, T156, T158, T159 raporları; `plan/11_F8.md` §7.6 | **Bu gate'te düzeltildi.** Süreç önerisi: kapanış ajanının kontrol listesine "rapor başlığı + §7.6 hücresi" ekle |
| **F8-N2** | ⚪ (build) | Backend Release build 13 uyarı (F7: 0): üretimde 8 (`CS8620` × 5 `CanonicalChainIdentifierConverter` nullability; `CS8629` `OutgoingTransferDispatchJob.cs:482`; `CS8604` `EvmAddress.cs:50`; `CS8602` `LanguageService.cs:37` — F4'ten), testlerde 5 | T145, T152, T158 dönemi | Backlog ⚪ `BackendNullableWarningsF8`. Akış incelemesinde gerçek null yolu görülmedi |
| **F8-N3** | ⚪ (kanıt) | TRON geç ödemesi F8'de Nile'da canlı koşulmadı (FK8-1); kanıt E2E §5.4 + entegrasyon; F8'in o yolda değiştirdiği iki parça Nile eksik ödeme iadesinde canlı geçti | FK8-1, T159/T159b | Backlog ⚪ `TronLatePaymentLiveNotRehearsedF8` → T172 senaryo listesine ekle |
| **F8-N4** | 🔴 güvenlik (F8 dışı) | Frontend `next@16.2.3` — 3 critical RCE + çok sayıda high npm danışması; CI'da frontend audit yok | frontend (F8 değişmedi) | Backlog 🔴 `FrontendNextAdvisories` → F9 canlı öncesi Next yükseltmesi + frontend audit adımı |
| **F8-N5** | S3 (doküman ↔ kod) | Ücret tavanı seed açıklaması "iade beklemez" diyor; karar ve 08 "iade de bekler"; 06 eksikti | `SystemSettingSeed.cs:216`, 06 §8 | 06 **düzeltildi** (v6.35); kod metni → backlog 🟡 `FeeCapSeedDescriptionStale` |
| **F8-N6** | ⚪ (kayıt) | T159b'de yerel prova DB'sinde geçici değiştirilen ayarların (`fee_cap_usd_per_transfer` 1,00, `max_concurrent_transactions` 8, `min_refund_threshold_ratio` 1,0) geri alındığı raporda yazılı, ancak T159b doğrulayıcısı yerel DB'yi okuyamadı. Bu gate prova yığınının volume'una bilerek dokunmadı (talimat) | yerel `escrow` yığını | Seed ve temiz DB değerleri doğru (0,50 / 5 / 2,0 seed'den). Sahip bir sonraki prova açılışında admin panelinden üç değeri bir kez görsün (yalnız yerel ortam, üretimi etkilemez) |
| **F8-N7** | not (sapma + gözlem) | **Sapma (bilinçli):** Docker smoke'ta çıplak `docker compose down -v` koşulmadı — yerel `escrow` yığınının volume'larında Nile/Sepolia prova DB'si ve `skinora-ethereum-tx-journal` var (GUARDRAILS §4, görev talimatı). Bunun yerine **ayrı compose projesi** (`-p skinora-gate`, yalnız `docker-compose.yml`, `ethereum` profili) sıfırdan kuruldu ve sonunda yalnız o proje `down -v` ile silindi; `escrow_*` volume'ları ölçüm öncesi ve sonrası aynı. Sırlar sahte (rastgele anahtarlar, herkese açık test tohumu, imza anahtarları boş), RPC anahtarsız PublicNode Sepolia + Nile TronGrid. **Gözlem:** `frontend/` ve dört sidecar'da `.dockerignore` yok; Dockerfile'ların `COPY . .` adımı geliştirici ağacındaki `node_modules`/`dist`'i imaja taşır (ilk denemede frontend bağlamı 480 MB oldu). CI temiz checkout'tan kurduğu için etkilenmez; gate'te yerel `node_modules`/`dist` silinip `--no-cache` ile yeniden kuruldu | `frontend/`, `sidecar-*/` | `.dockerignore` eklenmesi önerilir (`node_modules`, `dist`, `.next`) — F9 T161/T171 kurulum günü öncesi |

**Sınıflandırma gerekçesi:** hiçbiri S2 değildir — F0–F8'in tüm testleri bu koşuda yeşil. F8-N4 F8'in getirdiği bir kusur değil, F8'de dokunulmayan bir bağımlılığın sonradan yayımlanan danışmalarıdır; ama canlıya çıkışı bloke eder.

---

### Sahip onayı bekleyen kararlar (F8 yönetici düzeninin "⚠ listesi")

İşaretler:
- **ONAYIN GEREKLİ** — yönetici ya da yapımcı ajan öneriyle verdi; sen onaylayacaksın ya da değiştireceksin.
- **SAHİP KARARI** — kendi verdiğin karar; onay gerekmez, bilgi için.
- **AÇIK RİSK** — karar değil, kayıtlı bir boşluk; nereye taşındığı yazılı.

Kaynak: plan §F8 "Txxx kararları" blokları, görev raporları, F8 yönetici ilerleme notları (T159a/T159b sonrası eklenenler dahil). Rakamlar o günkü ölçümlerdir.

#### A. PARA

**ONAYIN GEREKLİ (23):**

1. **Ethereum ücret tavanı: gönderim başına 0,50 $.** Sweep (yakıtı dahil), payout ve iade ayrı ayrı sayılır; satış başı en çok 1,00 $. Canlı ETH kuruyla gwei'ye çevrilir; kur yoksa gönderim bekler. Ölçüm: satış başı maliyet bugün ~0,05–0,06 $, 30 günün en kötü %1'i 1,73 $. (T142, T152)
2. **Tavanın üstünde gönderim bekler, üst süre sınırı yok.** 3 saat kesintisiz beklerse alarm çalar. Sweep beklerken o satışın payout'u da bekler. İade de tavana bağlıdır (iade beklerken alıcı parasını geç alır). (T142, T152)
3. **TRON ağ ücreti satırı kabulde bir kez sabitlenir, sonra düzeltilmez.** Sweep'in gerçek ücreti satırı aşarsa fark platformdan çıkar; düşük kalırsa fark platformda kalır. Alıcıdan ek ücret istenmez, alıcıya fark dönmez. (T141, T149)
4. **Ücret tahmini alınamazsa sabit yedek ücret yazılır:** TRON 3,00 $, Ethereum 0,50 $ (akıl tavanı 10 $). TRON satırına %0,7 fiyat tamponu (0,007) eklenir; satır 10 USDT'yi geçemez. (T148, T149)
5. **Ethereum'da alıcıya sweep öncesi iade yapılırken** yakıt ve yakıt gönderim ücreti alıcıdan kesilir (bugün ~0,05 $). Ethereum'da satıcı payout'undan ağ ücreti kesilmez (ücreti platform üstlenir, K8 ile uyumlu). (T154)
6. **Sweep sırasında fiyatlanmış bir iade, sweep arızalanırsa yeniden fiyatlanmaz;** gas farkı platformda kalır. (T158)
7. **Yanlış EVM ağına gelen para:** otomatik kurtarma yok. Alıcı destek talebi açar; kurtarma Ethereum tohumuyla elle yapılır (runbook §J.4). Kurtarma gas'ını platform öder, alıcıya tam tutar döner. (T141, T158)
8. **Komisyon kuruşa aşağı yuvarlanır.** Platform işlem başına 1 sentin altını kaybedebilir; karşılığında alıcının ödediği toplam hep 2 ondalık olur (borsalar 2 ondalıktan fazlasını göndermiyor). (T144)
9. **ETH fiyat referansını yalnız iki kaynağın (Binance + CoinGecko) uyuştuğu fiyat ilerletir.** Açılışta kabul edilmiş fiyat yoksa iki kaynak birden şarttır; biri kalıcı kapalıysa ETH fiyatı oluşmaz ve Ethereum gönderimleri bekler. (T154)
10. **Yakıt cüzdanı kuralları:** yakıt yalnız servisin türettiği depozitlere ve tam eksik kadar gider; tek gönderim tavanı 0,001 ETH, günlük 0,02 ETH. Yakıt izleme kaydına bağlanmaz (simülasyon + tavanla sınırlı); bir isteğe ikinci yakıt gönderilmez; yuvarlamadan ~0,006 $ fark platformda kalır. (T153)
11. **ETH'si sıfır olan depozite yakıt her durumda gider** (yakıt probunun cevabı beklenmez). (T159a)
12. **Gönderim günlüğü zincirin gerisindeyse imzalama durur** ve kritik alarm çalar; operatör onayı olmadan yeni gönderim yok (hata durumunda güvenli taraf). (T152)
13. **Günlükte olmayan bir işlem numarası için "başarılı" denmez.** Backend beklenen alıcı ve tutarı gönderir; servis zincirde doğrulayamazsa "bilinmiyor" der ve satır onaylanmaz, alarm çalar. (T152, T158)
14. **Günlük kaybından sonra** aradaki satırlar yönetici aracıyla zincirdeki işlem numarasına bağlanır: önce "eşleşti" kontrolü ve denetim kaydı şart; aynı tutarda birbirinin aynısı N satır varsa satır başı kanıt yerine toplam sayı + tutar yeter. Araç kullanılamazsa elle yol: runbook §D 7.4, iki kişi + denetim kaydı. (T157)
15. **Aynı alıcı + token + tutarda başka açık satır varsa kapatma reddedilir** (belirsiz eşleşme, `AMBIGUOUS_ROW_MATCH`). (T157)
16. **Çöken ağın giden satırları (TRON dahil) "bekliyor"da kalır;** başka ağ etkilenmez. 24 saati aşan kesintide eski bir transferin kaçması riski T158'in "depozit başına şu bloktan izle" çözümüyle kapandı. (T150, T151, T158)
17. **İptal sonrası izleme yalnız backend açılışında yeniden kurulur.** Ödeme servisi tek başına yeniden başlarsa o depozitler izlenmez (eski boşluk; backlog `PostCancelRearmOnlyOnBackendStart`). (T158)
18. **İşlemin iade adresi yoksa** (normal akışta olamaz) iade yazılmaz, para depozitte bekler, yöneticiye uyarı gider; fazla ödemede ödeme yine kabul edilir. Açık soru: `BuyerRefundAddress` için veritabanı kuralı üretim sayımından sonra (F9 öncesi) eklensin mi? (T147)
19. **Yanlış token (USDC) iadesinde** kesilecek gas USDT cinsinden tahmin edilir ve USDC'den 1:1 düşülür. (T159a)
20. **Reddedilen ödeme iadesinde kesilen gas tutarı depozitte token olarak kalıyor,** sweep onu toplamıyor (T159a doğrulama bulgusu 1; T159b'de zincirde görüldü). Karar: kalıntı nasıl toplanacak — F9 öncesi düzeltme mi, kabul mü? Backlog 🟡 `RefundGasDeductionDustStaysOnDeposit`. (T159a, T159b)
21. **İptal iadesinde gas tahmini tek denemelik:** düğüm o an geçici hata verirse sabit yedek ücret (Ethereum 0,50 $) kesilir — mainnet'te gerçek ücret ~0,05 $ iken alıcıdan ~10 katı. T159b'de 2/2 admin iptalinde görüldü. Öneri: F9 canlı öncesi yeniden deneme eklensin. Backlog 🟡 `RefundGasEstimateSingleAttempt`. (T159b B1)
22. **Soğuk cüzdana aktarım da tavana bağlıdır;** USDT `paused` / `deprecated` olursa gönderim kalıcı reddedilir ve yöneticiye alarm gider. (T152)
23. **TRON ağ ücreti satırı her zaman "yakma" yoluyla fiyatlanır;** kilit (staking) kodu açılırsa fiyatlama yeniden ele alınmalı. (T149)

**SAHİP KARARI (bilgi, 5):**

- K1–K9 — ağlar (TRON kalır + Ethereum), yalnız USDT, aynı ağdan payout/iade (ağlar arası para hareketi yok), TRON ücret dağılımı (alıcı sweep'i, satıcı payout'u öder), reddedilen ödemelerin iadesi alıcının o ağdaki iade adresine (2026-10-05 / 10-17).
- Sweep **sonrası** iadede TRON ağ ücreti satırı geri verilmez (2026-10-18).
- Ethereum giden satırları geçici hatada hiç "başarısız" sayılmaz; aynı kimlikle yeniden denenir — çift ödeme olmasın diye (2026-10-18).
- Reddedilen ödeme iadelerinde (eksik, fazla, çoklu, yanlış token, geç) gas **her durumda** alıcıdan kesilir, iki ağda (2026-10-20). T159a'da uygulandı, T159b'de zincirde ölçüldü.
- `auth.min_steam_account_age_days` = 1 prova ayarı şimdilik kalıyor (2026-09-30); üretimde 30 → F9 T166.

**AÇIK RİSK (bilgi):**

- TRON'da Ethereum'dakine benzer çift ödeme riski (geçici hatada `FAILED` → elle yeniden gönderim) — backlog 🟡.
- Soğuk aktarımın yeniden denemesi ikinci bir aktarım yapabilir (para yine soğuk cüzdanda kalır) — backlog.
- TRON'da sweep sonrası alıcı iadesi boş depozitten deneniyor → `FAILED` + alarm, iade elle — backlog 🟡 `TronRefundAfterSweepFromDeposit`.
- TRON'da "sweep çalıştı mı" bilgisi ödeme `DETECTED` iken çalışmamış sayılıyor — backlog.
- Prova için geçici değiştirilen ayarlar (T159b): `fee_cap_usd_per_transfer` 0,50 → 1,00 → 0,50; `max_concurrent_transactions` 5 → 8 → 5; `min_refund_threshold_ratio` 2 → 1 → 2. Rapora göre geri alındı (F8-N6 notu).

#### B. GÜVENLİK

**ONAYIN GEREKLİ (7):**

1. **Yeni bir ağa ilk adres eklerken de Steam ile kimlik yeniden doğrulanır** (ödeme ve iade adresi için ayrı ayrı). (T146 xv, T147 xi)
2. **Kabul ucunda sunucu da** tümü küçük ya da tümü büyük harfli Ethereum iade adresi için ikinci onay ister (yalnız arayüze güvenilmez). (T156)
3. **Yönetici cüzdan ekranı ve giden satır kapatma aracı bugün `MANAGE_SETTINGS` iznini kullanır.** Ayrı bir `MANAGE_WALLETS` izni F9 güvenlik sertleştirmesine bırakıldı. (T157)
4. **Ethereum anahtar düzeni:** ayrı tohum (K7, sahip kararı) Docker secret olarak; imza malzemesi yalnız secret dosyasından okunur (env yedeği yok); yakıt cüzdanının ayrı anahtarı var; imzacı yalnız izin listesini imzalar — USDT `transfer`, USDC yalnız yanlış token iadesinde, yapılandırılmış `chainId`, işlem tipi 0/2; tip 4 (EIP-7702) ve keyfi kontrat çağrısı asla. (T141, T150, T152)
5. **Ödeme izleyici yalnız kesinleşmiş (`finalized`) blokları sayar.** Yanlış token için yalnız USDT/USDC otomatik iade edilir, diğerleri yok sayılır ve yöneticiye görünür; depozite yerel ETH gelirse yönetici alarmı. Payout ve iade öncesi alıcı adresi USDT kara listesinde mi bakılır; öyleyse gönderim yok + alarm. (T141, T142, T151, T152)
6. **Ethereum yaptırım listesi giriş kuralları:** karışık harfli adreste EIP-55 sağlaması zorunlu; tümü küçük/büyük harf sağlamasız kabul edilir; eski `'TRC-20'` değeri artık kabul edilmez. Uyum listesi olduğu için hukuk bakışı gerekebilir. (T145 iv)
7. **Webhook ve servis ayrımı:** ağı depozitle uyuşmayan webhook reddedilir; TRON ve Ethereum servisleri aynı webhook sırrını paylaşırsa backend açılmaz. (T141, T148)

**AÇIK (bilgi / öneri):**

- **YENİ — F8-N4 🔴:** frontend `next@16.2.3` için 3 critical (RCE) + çok sayıda high danışma. Canlıya çıkmadan Next yükseltmesi + CI'a frontend audit adımı. F8 kaynaklı değil.
- Ethereum sıcak cüzdan üst-limit izleyicisi ve nonce aralığı şartı — backlog.
- Anahtarlı RPC limit probu (Alchemy + Chainstack) yapılmadı — F9 T169.
- Üretim sunucusundan Binance/CoinGecko erişim probu (Binance bölge engeli HTTP 451) — runbook'ta yazılı, sunucuda koşulacak (T158 (e)).

#### C. Diğer (kısa; hepsi ONAYIN GEREKLİ, geri dönüşlü)

- Depozit adresi işlem başına kalır (kullanıcı başına sabit adres değil). (T141 b)
- Kilit (hibrit enerji) kodu silinmedi; `ENERGY_STAKING_ENABLED` varsayılan kapalı anahtarın arkasında uykuda. (T149)
- "Net iade < 2 × gas → iade yok" eşiği ağ başına ayar. (T149)
- API'de ağ verilmezse `TRON` varsayılır (eski istemci uyumu); tümü büyük harfli EVM adresi de ikinci onay ister; ağ başına kullanıcı adres tablosu. (T146, T147)
- Veri modeli/API biçimi: `UserWalletAddress`, `TransactionNetworkOption`, `network` alanı, `NETWORK_NOT_ACCEPTED` (422) / `NETWORK_UNAVAILABLE` (503). (T141, T145–T147)
- RPC: Alchemy (birincil) + Chainstack (yedek), PublicNode anahtarsız geri düşüş; `eth_getLogs` ≤ 1.000 adres; `finalized` başı 30 sn'de bir; mutabakat servis içinde saatlik; 30 dk "finalized ilerlemiyor" ve 0,06 ETH bakiye alarmları; Sepolia'da kendi Tether kopyası. (T142, T150, T151, T153, T159)
- TRON için en düşük satış tutarı (ya da ~50 $ altında TRON'u gizlemek) — karara bağlanmadı, F9 T169'da. (T141)
- MASAK notları (alıcının USDT'yi önceden elinde tutması) — F9 T167'de. (T141)

**Sayım:** PARA'da onay bekleyen **23**, GÜVENLİK'te onay bekleyen **7** (+ bilgi için 5 sahip kararı, 1 yeni açık güvenlik maddesi F8-N4).

---

### Süre: tahmin ve gerçekleşen

**Tanım:** gerçekleşen = alt ajanların net çalışma süresi (yapım + doğrulama + düzeltme + bütünleştirme + kapanış). CI beklemesi ve sahip sorularının beklemesi dahil değil. Tahmin düzeltme turlarını içermiyordu, gerçekleşen içerir. Kaynak: F8 yönetici ilerleme tablosu (görev başına satır) ve maliyet günlüğü (ajan başına ölçülen süre).

| Görev | Tahmin | Gerçekleşen (net) | Fark | Not |
|---|---|---|---|---|
| T141 | 3 sa | 1 sa 09 dk | −1 sa 51 | FAIL turu dahil |
| T142 | 2 sa | 59 dk | −1 sa 01 | |
| T143 | 2 sa | 43 dk | −1 sa 17 | |
| T144 | 2 sa 30 dk | 1 sa 40 dk | −50 dk | |
| T145 | 3 sa 30 dk | 2 sa 58 dk | −32 dk | 79 dosya + migration |
| T146 | 2 sa 30 dk | 2 sa 08 dk | −22 dk | |
| T147 | 3 sa 30 dk | 3 sa 12 dk | −18 dk | T148 sonrası entegrasyon dahil |
| T148 | 3 sa 30 dk | 5 sa 26 dk | +1 sa 56 | FAIL turu dahil |
| T149 | 2 sa 30 dk | 3 sa 53 dk | +1 sa 23 | |
| T150 | 2 sa 30 dk | 2 sa 40 dk | +10 dk | |
| T151 | 5 sa | 3 sa 06 dk | −1 sa 54 | |
| T152 | 6 sa | 5 sa 49 dk | −11 dk | FAIL turu + iki düzeltme dahil |
| T153 | 2 sa 30 dk | 2 sa 25 dk | −5 dk | |
| T154 | 2 sa | 2 sa 25 dk | +25 dk | |
| T155 | 3 sa 30 dk | 3 sa 07 dk | −23 dk | |
| T156 | 3 sa 30 dk | 2 sa 35 dk | −55 dk | |
| T157 | 2 sa | 9 sa 57 dk | **+7 sa 57** | Sonnet yapım 3 sa 23; Docker'lı SQL doğrulaması 2 sa 21; FAIL turu |
| T158 | 2 sa 30 dk | 4 sa 55 dk | +2 sa 25 | FAIL turu dahil |
| T159 | 3 sa 30 dk | 2 sa 42 dk | −48 dk | KOŞULLU PASS |
| T159a | — (plan dışı) | 2 sa 51 dk + ölçülmeyen | — | yapım 83 + sahip kararı/bütünleştirme 50 + doğrulama 19 + kapanış 19; ilk doğrulama PC yeniden başlayınca yarıda kaldı (süresi ölçülmedi), belge bütünleştirmesi ölçülmedi |
| T159b | — (plan dışı) | 1 sa 37 dk | — | prova 82 + doğrulama 8 + kapanış 7 (+ hazırlık 3) |
| T160 | 2 sa 30 dk | ≈ 1 sa 10 dk | −1 sa 20 | 02:12–03:22 UTC; PR CI beklemesi hariç | bu gate (tek alt ajan) |
| **Toplam T141–T159** (tahmini olan 19 görev) | **58 sa** | **61 sa 49 dk** | **+3 sa 49** | T157 tek başına +7 sa 57 |
| **Toplam T141–T159b** | 58 sa (T159a/b tahminsiz) | **66 sa 17 dk** + ölçülmeyen | | |

Tahmini bütün F8 (T141–T160): 60 sa 30 dk net + 6–10 düzeltme turu (≈ 10–20 sa). Düzeltme turları dahil gerçekleşen, T160 hariç **66 sa 17 dk** — üst sınırın (≈ 80 sa) altında. Gerçekleşen içinde düzeltme turları zaten var; yani yalnız yapım + doğrulama tahmini (58 sa) ile karşılaştırıldığında aşım %7.

**Tutarsızlıkların çözümü:** İlerleme tablosundaki "≈ toplam" değerleri ile aynı satırdaki bileşenlerin toplamı sekiz görevde farklıydı. Her birinde **bileşen toplamı** seçildi, çünkü bileşenlerin çoğu maliyet günlüğünde ajan başına ayrıca ölçülmüş ve onunla birebir tutuyor; "≈" değerleri elle yuvarlanmış ve bazılarına sahip sorusu beklemesi karışmış görünüyor (tanım gereği dışarıda):
- T148: ≈ 5 sa ↔ 180 + 26 + 62 + 41 + 17 = **326 dk** (yapım "~3 sa" yuvarlak).
- T149: ≈ 4 sa 15 ↔ 136 + 35 + 62 = **233 dk** (+22 dk fark: satırda "sahip sorusu 1" var; bekleme sayılmadı).
- T151: ≈ 3 sa 10 ↔ 87 + 70 + 29 = **186 dk** (yuvarlama).
- T152: ≈ 6 sa 20 ↔ 92 + 56 + 43 + 35 + 65 + 28 + 30 = **349 dk** (günlükteki yedi ölçüm birebir; +31 dk farkın kaynağı bulunamadı, sahip sorusu beklemesi olabilir — ölçülmedi).
- T153: ≈ 2 sa 45 ↔ 61 + 50 + 34 = **145 dk** (günlükle birebir).
- T155: ≈ 3 sa 15 ↔ ~110 + 10 + 47 + 20 = **187 dk** (doğrulama günlükte 10 dk).
- T156: ≈ 2 sa 50 ↔ ~106 + 14 + 35 = **155 dk** (günlükle birebir).
- T158: ≈ 6 sa 15 ↔ 107 + 52 + 69 + 35 + 32 = **295 dk** (günlükteki beş ölçüm birebir; +80 dk farkın kaynağı bulunamadı).
- T157 (hazırlık taslağında "devam ediyor ~9 sa 20"): sonradan bitti; 203 + 141 + 105 + 93 + 18 (durdurulan ilk kapanış) + 37 (ikinci kapanış) = **597 dk**.

**Düzeltme turları:** öngörü 6–10 tur. Gerçekleşen: 5 FAIL turu (T141, T148, T152, T157, T158) + T151/T154'te doğrulama sonrası küçük düzeltme + T159'un KOŞULLU PASS'inden doğan iki ek görev (T159a, T159b) + bir CI kararsızlık düzeltmesi (#359, 44 dk, F8 görevi sayılmadı).

---

### Faz Tag

- Tag: `phase/F8-pass` — **atılmadı.** Sahip yukarıdaki "Sahip onayı bekleyen kararlar" listesine baktıktan sonra yönetici bu gate PR'ının squash-merge commit'i üzerinde atar (`git tag phase/F8-pass <merge-sha>` + `git push origin phase/F8-pass`).
- Ölçüm commit'i: `c1026b8c`.
