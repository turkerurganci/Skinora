# T172a — Yedekten geri yükleme sonrası para araçları (⚠ PARA)

**Faz:** F9 | **Durum:** ✓ Tamamlandı — ilk doğrulama ✗ FAIL (HEAD `8009a52f`) → yeniden doğrulama ✓ PASS (HEAD `1c884579`) | **Tarih:** 2026-10-23

---

## Yapılan İşler

Kaynak: DEFERRED_BACKLOG `PostRestoreChainReconciliationTooling` (T164), DEPLOY_RUNBOOK §N.8, T164 yeniden doğrulama S4-3. Kabul kriterinin üç aracı, mevcut iki kişilik kapatma aracının (AD33–AD37, T157) genişletilmesiyle ve iki küçük eklemeyle yapıldı:

1. **TRON'da gönderilmiş `PENDING` satırı zincir kanıtıyla kapatma** — AD33–AD37 artık iki ağda çalışır (`OutboundRowClosureService`). Kurallar T157'nin aynısıdır ve TRON'a da uygulanır: iki ayrı admin (öneren onaylayamaz, kimlik oturumdan), onaylayan kimliği yeniden yazar, onay anında zincir sorulur ve yalnız **kesinleşmiş + başarılı** (TRON ≥ 20 onay + `SUCCESS`) **ve** satırın token kontratından, satırın göndereninden (ödemede ağın sıcak cüzdanı, iadede işlemin depozitosu), satırın alıcısına, **tam** tutarda `Transfer` log'u varsa geçer; eksik/yanlış kanıt ya da cevap vermeyen servis = ret (fail-closed). Her adım AuditLog'da (`OUTBOUND_ROW_CLOSURE_PROPOSED/_APPROVED/_REJECTED/_REFUSED`, Fon kategorisi; onay kaydı `network`, `rowStatus`, `rowError` da taşır). Onay satıra hash yazar, `DETECTED` yapar; gönderim işi satırı bir daha göndermez, onay işi zincirden `CONFIRMED` yapar (ödeme satırında işlem `COMPLETED`). **Aynı işlem kimliği ikinci bir satırı kapatamaz** (servis denetimi + `UQ_OutboundRowClosures_Network_TxHash_Live` + `UQ_BlockchainTransactions_Network_TxHash_EventIndex`).
   - TRON servisine salt okunur `POST /api/transfer/verify` eklendi (`sidecar-blockchain/src/transfer/TransferVerifier.ts`, `api/verifyHandler.ts`): solidity düğümünün `walletsolidity/gettransactioninfobyid`'sini okur; Ethereum servisinin T157 sözleşmesinin aynısı (`contracts/transfer-verify/`, iki yanın testi aynı dosyayı okur).
   - Gönderen kaydetmeyen satır (geri yüklemede hiç gönderilmemiş ya da yeni kimlikle açılmış) gönderim işinin kuralıyla çözülür ve onayda `FromAddress`'e yazılır — mutabakat ve iade kesinti defteri satırı doğru adresten sayar.
2. **Depozit adres indeksine yapılandırılabilir taban** — `PaymentAddresses:MinimumNextIndex:<AĞ>` (env `PAYMENT_ADDRESS_MIN_NEXT_INDEX_TRON/_ETHEREUM`, compose'a eklendi). Sonraki indeks = `max(MAX(HdWalletIndex) + 1, taban)`: taban **yalnız yükseltir**, mevcut en büyüğün altına/eşitine düşürülen taban etkisizdir, yeniden kullanım üretemez (silinmiş satırlar dahil `MAX`). ~~Bilinmeyen ağ adı ya da negatif değer backend'i açmaz (`ValidateOnStart`).~~ **Düzeltme turu (F1):** yapımdaki bu ifade eksikti — değer `int` olarak bağlandığı için `1.000`, `1e4`, `abc`, `99999999999` binder tarafından sessizce atılıyor, backend tabansız açılıyordu. Artık değer metin olarak bağlanır ve katı ayrıştırılır (yalnız ASCII rakam, `int` aralığı; boş = taban yok); aksi her değer ve bilinmeyen ağ adı backend'i açmaz; açılışta ağ başına etkin taban log'a yazılır.
3. **`FAILED POST_RESTORE_DUPLICATE` satırını gerçek ödemeye aynı satır üzerinde getirme** — aynı araç, `ErrorMessage`'ı `POST_RESTORE_DUPLICATE` ile başlayan hash'siz `FAILED` satırı aday sayar; onay satırı **aynı satırda** `FAILED → DETECTED` yapar, onay işi zincirden `CONFIRMED` (blok, onay sayısı, gerçek ücret zincirden; gönderen yazılır). Mutabakat (`CONFIRMED` sayar), iade kesinti defteri (`CONFIRMED` + depozit göndereni) ve ödeme şikâyeti açığı kapanır. Eski kopya gerekmez. Not taşımayan `FAILED` satır açılamaz.
4. **Gönderim durdurma anahtarı `outbound.dispatch_paused`** (seed 83 — main birleştirmesinden önce 81; migration) — geri yüklemeden sonra imza servisleri kapatma aracının zincir okuması için **açılmak zorunda**; açıkken gönderim işi onaylı ödemeleri ve sweep'leri hemen gönderirdi. Anahtar `false` değilken — ve düzeltme turundan beri satır yokken de (S4-2) — gönderim işi hiçbir satırı göndermez (her tür, her ağ), deneme hakkı harcanmaz; tur başında ve her satırdan önce yeniden okunur. Kapsamadığı: Ethereum servisinin zaten imzalanmış işlemleri yeniden yayınlaması (keeper, aynı nonce — S4-3) ve AD39.
5. **Runbook §N.8** bu araçlara bağlandı: adım 2 durdurma SQL'i + taban, adım 3 deneme penceresi kalktı, yeni 5a (imza servisleri durdurma altında), adım 5 araç birincil yol (SQL elle yol olarak kaldı), FAILED satırın araçla geri getirilmesi, adım 6–7 gönderimin açılması; §N.10 sınırlar, §B env satırı, §M.3 çapraz atıf.
6. **Yönetici ekranı:** kapatma paneli her ağın kartında (TRON da); TRON kimliği biçimi (64 hex, `0x`'siz), Tronscan bağlantısı, "geri yüklemeden sonra elle kapatıldı" işareti; dört dilde metin.

## Etkilenen Modüller / Dosyalar

- Backend: `Skinora.API/Services/WalletClosures/{IOutboundRowClosureService,OutboundRowClosureService}.cs`, `Controllers/AdminOutboundClosuresController.cs`, `Configuration/TransactionsModule.cs`; `Skinora.Transactions/Application/PaymentAddresses/{PaymentAddressAllocator,PaymentAddressIndexFloorOptions}.cs`, `Application/Transfers/{OutboundDispatchPause,OutgoingTransferDispatchJob,HttpBlockchainTransferClient}.cs`, `Domain/Entities/OutboundRowClosure.cs`, `Infrastructure/Persistence/OutboundRowClosureConfiguration.cs`; `Skinora.Platform/.../{SystemSettingSeed,SystemSettingsCatalog}.cs`; migration `20261023150626_T172a_PostRestoreTools` (+ snapshot).
- Backend testleri: `OutboundRowClosureTests` (+14 T172a test/teori), `OutboundApprovalTests` (+4 durdurma), `AdminOutboundClosuresEndpointTests`, `AdminWalletsEndpointTests`, `PaymentAddressAllocatorTests` (+4 taban), `PaymentAddressIndexFloorOptionsTests` (yeni), `EthereumSidecarAdminContractTests` (TRON verify sözleşmesi), `SeedDataTests`.
- TRON servisi: `src/transfer/TransferVerifier{,.test}.ts`, `src/api/verifyHandler{,.test}.ts`, `src/api/routes.ts`, `src/index.ts`, `contracts/transfer-verify/{request,response}.json`.
- Frontend: `components/admin/{OutboundClosurePanel,ClosureModals,NetworkWalletsCard}.tsx` (+ testler, `walletTestUtils.tsx`), `lib/api/admin.ts` (+ test), `lib/hooks/useOutboundClosures.ts`, `i18n/messages/{en,tr,es,zh}.json`.
- Altyapı: `docker-compose.yml` (iki env), `.env.example`.
- Belgeler: `DEPLOY_RUNBOOK.md` (§B, §J.2, §M.3, §N.8, §N.10, başlık), `06_DATA_MODEL.md` v6.40, `07_API_DESIGN.md` v4.26, `08_INTEGRATION_SPEC.md` v3.48 (main birleştirmeleriyle — T169a, T165 — numaralar kaydı), `DOC_CHANGELOG.md`, `DEFERRED_BACKLOG.md`, `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | TRON'da zincirde gönderilmiş ama DB'de `PENDING` kalan giden satırı zincir kanıtıyla kapatan admin aracı (verify + AuditLog) | ✓ | `T172a_Tron_PendingPayout_IsClosedWithTheChainHash_ByTwoAdmins_AndIsNeverSignedAgain` (verify okuması: hash, USDT, sıcak cüzdan → alıcı, tutar; AuditLog `network/rowStatus`; kapı açıkken bile gönderilmez; `CONFIRMED` + `PayoutCompletedEvent`), `…_WrongOrMissingChainProof…` (5 ret), `…_AnIdTheChainDoesNotShowFinalAndSuccessful…` (3 ret), `…_TheProposerCannotApprove`, `…_AMalformedId…`; TRON servisi `TransferVerifier.test.ts` 22 + `verifyHandler.test.ts` 18; canlı Nile ölçümü (aşağıda) |
| 1a | İdempotency: aynı tx iki satırı kapatamaz | ✓ | `T172a_Tron_OneChainTransaction_ClosesOneRow_Only` — canlı öneri varken, onaydan sonra, büyük harfle, araç dışından satıra yazma (DB tekil indeksi), ikinci canlı kayıt (DB tekil indeksi), iki kez onay |
| 2 | Depozit adres indeksine yapılandırılabilir taban; düşürülemez / yeniden kullanım üretmez | ✓ (yapımda ~ Kısmi idi — F1; düzeltme turu aşağıda) | `PaymentAddressIndexFloorOptionsTests` (8 satırlık `NextIndex` tablosu, env biçiminden bağlama, hatalı ad/negatif değer); `PaymentAddressAllocatorTests.T172a_*`: taban MAX'ın üstünde → 41, 42; taban 0/3/7 ve MAX 7 → 8, tüm indeksler tekil; silinmiş satır sayılır; başka ağın tabanı etkisiz |
| 3 | `FAILED POST_RESTORE_DUPLICATE` satırını eski kopyadaki gerçek ödemenin değerlerine aynı satırda getiren yol | ✓ | `T172a_PostRestoreFailedRow_IsBroughtBackToTheRealPayment_OnTheSameRow` (tek satır, aynı Id, `CONFIRMED`, blok, hash, `ConfirmedAt`), `…_PostRestoreFailedRefund_IsCountedFromTheDeposit_OnceConfirmed` (iade kesinti defterinin saydığı biçim: `CONFIRMED` + depozit), `T172a_Ethereum_PostRestoreFailedRow_IsBroughtBackToo`, `T172a_AFailedRowWithoutTheRestoreNote_CannotBeReopened` (4 not), `T172a_AReopenWhoseRowChangedSinceTheProposal_IsRefused`. Değerler eski kopyadan değil **zincirden** gelir (karar 3) |
| 4 | Runbook §N.8'e bağlı | ✓ | DEPLOY_RUNBOOK §N.8 (b), adım 2, 3, 5a, 5, 6, 7; §N.10; §B; §M.3 |
| 5 | Testler | ✓ | Aşağıda. Yapımın "14/14 bozma" iddiası eksikti: doğrulama 20 backend bozmasından 3'ünün yaşadığını gösterdi (B12 CAS hash koşulu, B19 `Bind`, B20 `ValidateOnStart`). Düzeltme turunda bu üçü ve yeni kurallar bozularak yakalandı (aşağıda) |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend — kapanış/onay/cüzdan uç noktaları (SQL Server LocalDB, migration'lı şema) | ✓ 151/151 + 11/11 | `dotnet test tests/Skinora.API.Tests --filter "OutboundRowClosureTests|OutboundApprovalTests|AdminOutboundClosuresEndpointTests|AdminWalletsEndpointTests"`; fikstür düzeltmesinden sonra `OutboundApprovalTests` 11/11 |
| Backend — Transactions (adres ayırıcı, taban, gönderim işi, sözleşmeler) | ✓ 271/271 → sözleşme düzeltmesi sonrası `Contract` 124/124 | `--filter "PaymentAddress|OutgoingTransferDispatchJob|Contract"` |
| Backend — Platform (seed, katalog, ayarlar) | ✓ 136/136 | `--filter "SeedData|Catalog|Setting"` |
| Backend — tam proje koşusu (LocalDB) | API 1010/1012 → 2 gerçek kırık düzeltildi (migration provası testleri eski CHECK adı ve 77 ayar sayısını bekliyordu) → `MigrationRehearsalTests` 30/30; Transactions 1900/1905 → 5 hata 1 ms'lik kurulum hatası (LocalDB eşzamanlı yük), tek başına 8/8 ✓; Platform 235/235; Shared 593/593 | `dotnet test tests/<proje>` |
| TRON servisi | ✓ 40/40 yeni (22 + 18); `tsc --noEmit`, eslint, prettier temiz | `npx vitest run src/transfer/TransferVerifier.test.ts src/api/verifyHandler.test.ts` |
| Frontend | ✓ 466/466 (46 dosya), `npm run lint` temiz, `tsc --noEmit` temiz | `npx vitest run` |
| Canlı Nile (salt okunur) | ✓ 8/8 | Gerçek TRC-20 transferi `29a02e38…e566` (blok 72.079.005): tam eşleşme MATCH; büyük harf kimlik MATCH; göndericisiz MATCH; tutar+1, yanlış alıcı, yanlış gönderen, yanlış token → NO_MATCHING_TRANSFER; bilinmeyen kimlik → NOT_FOUND |

**Bozma (mutasyon) turu — yapım, 14/14** (⚠ düzeltme turu notu: bu liste kablolamayı ve CAS hash koşulunu içermiyordu; doğrulama B12/B19/B20'yi yaşatarak gösterdi — "14/14" yalnız bu listenin sonucudur, testlerin tamamının bozmaya dayanıklı olduğunu göstermez) (her biri tek satır, test koşusu, dosya bayt bayt geri yüklendi; "yakalandı" = derleme başarılı + doğrulama hatası, örnek mesajlar ölçüldü):

| # | Bozulan kural | Yakalayan |
|---|---|---|
| M1 | Taban MAX + 1 alt sınırını bırakır (taban < MAX → yeniden kullanım) | 6 test (`Failed: 6`) |
| M2 | Not şartı kalkar (her `FAILED` açılır) | `T172a_AFailedRowWithoutTheRestoreNote…` |
| M3 | Hash "bağlı" denetimi kalkar | `…_OneChainTransaction…` — `Expected HashAlreadyBound, Actual Conflict` (DB tekil indeksi yine ikinci kapatmayı reddetti) |
| M4 | `NO_MATCHING_TRANSFER`/`REVERTED` geçer sayılır | `Failed: 2` |
| M5 | Kesinleşmemiş hash geçer | `…_AnIdTheChainDoesNotShowFinalAndSuccessful` |
| M6 | İadenin göndereni sıcak cüzdan sanılır | `…_ARefundWithoutARecordedSender…` |
| M7 | Doğrulanan gönderen satıra yazılmaz | `…_PostRestoreFailedRefund…`, `…_PendingPayout…` |
| M8 | İki kişi kuralı kalkar | `…_TheProposerCannotApprove` |
| M9 | Durdurmada bozuk değer "durmuyor" okunur | `T172a_Any_Value_But_False_Reads_As_Paused` |
| M10 | Durdurmanın satır başı yeniden okuması kalkar | `T172a_Pausing_Mid_Run…` — `Assert.Single() … contained 2 items` |
| M11–M14 | TRON servisi: gönderen / tam tutar / `SUCCESS` / kontrat denetimi kalkar | `TransferVerifier.test.ts` (1 / 2 / 4 / 1 başarısız) |

## Düzeltme turu (doğrulama ✗ FAIL, HEAD `8009a52f` → 2026-10-23)

Kaynak: `T172a_VALIDATION.md` (1 × S1 ⚠ PARA, 5 × S4). Her bulgu: yapılan → kanıt. "Bozma" = tek değişiklik, derleme, hedefli test koşusu, dosya bayt bayt geri yüklendi.

| Bulgu | Yapılan | Kanıt |
|---|---|---|
| **F1 S1 ⚠ PARA** — taban tam sayı olmayan/taşan değerde sessizce 0 | `PaymentAddressIndexFloorOptions.MinimumNextIndex` artık `Dictionary<string,string?>`; `TryParseFloor` yalnız ASCII rakam + `int.TryParse(NumberStyles.None, Invariant)`; boş/boşluk = taban yok; `Errors()` her biçimsiz değeri reddeder (`ValidateOnStart` backend'i durdurur); `FloorFor` biçimsiz değerde 0 değil istisna. Yeni `PaymentAddressIndexFloorStartupLog` (hosted service): açılışta `Deposit index floor at start-up: TRON = …, ETHEREUM = …` (taban varsa Warning). `PaymentAddressAllocator.FloorFor` tahsis yolunun okuduğu taban. Kod yorumu (TransactionsModule, options) ve backlog kapanışı gerçeğe eşitlendi. Runbook §B, §N.8 (b) sayılar ayraçsız (`+ 1000` / `+ 10000`) + Türkçe binlik ayıracı uyarısı; adım 3'te açılış satırı `grep`; `.env.example` notu | **Açılış düzeyinde:** `PaymentAddressIndexFloorHostTests` (gerçek `WebApplicationFactory<Program>` host, 11 test): `1.000`, `1,000`, `12 345`, `1e4`, `abc`, ETHEREUM `99999999999`, `-1`, `TRONN` → host açılmıyor (hata `MinimumNextIndex`); `5000` → allocator `FloorFor(TRON) = 5000`, açılış satırı `TRON = 5000, ETHEREUM = none`; ayarsız → 0. **Bozma (4/4 yakalandı):** `.Bind` silindi → 10 test kırıldı; `.ValidateOnStart()` silindi → `…Even_Without_The_Start_Up_Line` kırıldı; `.Validate(...)` silindi → 2 test; `AddHostedService<…StartupLog>` silindi → `A_Valid_Floor…` kırıldı. Birim: `PaymentAddressIndexFloorOptionsTests` 34 test (13 biçimsiz değer, sınırlar, boş, binder metni korur); bozma `FloorFor` sessiz 0 → 13 test kırıldı. Eşdeğer bozma: rakam döngüsünü silmek yaşadı — `NumberStyles.None` aynı değerleri zaten reddediyor (savunma katmanı, davranış farkı yok) |
| **S4-1** — CAS `b.TxHash == null` korumasız | `ChainStub.OnVerify` kancası: onayın okuması ile yazması arasında satıra başka bir hash yazılır | `T172a_S4_1_A_Row_That_Got_A_Hash_Since_The_Read_Is_Not_Overwritten` (PENDING + notlu FAILED): `RowNotOpen`, hash korunur, onay audit'i yok. Bozma: CAS'tan `b.TxHash == null` silindi → 2/2 kırıldı |
| **S4-2 ⚠ PARA** — durdurma satırı yoksa "durmadı" | **Satır yoksa durmuş sayılır** (öneriyle kaydedildi — ⚠ PARA). Gerekçe: T172a migration'ından eski bir yedek geri yüklenince satır yoktur ve runbook'un `UPDATE`'i 0 satır etkiler; onaylatma kapısıyla (`OutboundApprovalGate`, satır yok = kapalı) aynı kural. Mevcut ortamlara etkisi: migration satırı `false` ile ekler (seed + `InsertData`), yani migration'lı her veritabanında davranış aynı; satırsız veritabanı görünür biçimde durur (her tur uyarı + S4-4 alarmı). `OutboundDispatchPause.ReadAsync` (durum, satır yok, ham değer). Runbook adım 2: "(1 row affected) olmalı; 0 ise önce migration, sonra `UPDATE`" + sıra uyarısı (UPDATE'ten sonra uygulanan migration satırı `false` getirir) | `T172a_S4_2_A_Missing_Pause_Row_Reads_As_Paused_Nothing_Is_Sent` (satır silinir: gönderim yok, `PENDING`, deneme 0, gösterge `true`). Bozma: satır yok → `Paused: false` → kırıldı |
| **S4-3** — Ethereum keeper notu | Runbook adım 2, 5a, §N.10 ve 08 §3.3b: durdurmada Ethereum servisi günlüğündeki zaten imzalanmış işlemleri yeniden yayınlar / ücret yükseltir; aynı nonce, ikinci ödeme yok, yalnız gas | Belge (kod değişmedi) |
| **S4-4** — durdurma unutulursa ayrı alarm | `IOutboundDispatchPauseObserver` + `PrometheusOutboundDispatchPauseObserver` (`skinora_backend_outbound_dispatch_paused` 1/0; her tur, durdurmada da limit göstergeleri tazelenir); Grafana `outbound-dispatch-paused` (warning, `for: 30m`, T169a deseni `or vector(0)`); runbook §J.2 satırı, adım 7 | `T172a_Every_Run_Reports_The_Pause_State`, `T172a_Paused_Nothing_Is_Sent_And_The_Limit_Gauges_Stay_Fresh`, `PrometheusOutboundDispatchPauseObserverTests` (gösterge + kural metni dosyadan). Bozma: rapor satırı silindi → 2 test; kural `for: 0m` → kırıldı |
| **S4-5** — not eşleşmesi tutarsız; kullanılmayan sabit | Aday listesi ve onay CAS'ı notu `EF.Functions.Collate(…, "Latin1_General_100_BIN2")` ile harf duyarlı eşler (`IsOpen` ordinal ile aynı; küçük harfli not aday değil). `MaxDerivableIndex` kaldırıldı; `NextIndex` `long` ile hesaplar, `MAX = 2^31 − 1` sonrası istisna (sarıp 0'ı yeniden vermez) | `T172a_S4_5_A_Lowercase_Note_Is_Neither_Listed_Nor_Reopenable`; bozma: aday sorgusunda düz `StartsWith` → kırıldı. `NextIndex_Refuses_To_Wrap_Past_The_Last_Index` |
| **Main birleştirmesi** | `fcc94d8d` (T169a #376) merge commit ile alındı (`70fb0547`). Çakışmalar: seed sırası (T169a 81–82, T172a 83), ayar sayısı 80 (`SeedDataTests`, `NetworkFeePolicyMigrationRehearsalTests`), snapshot + T172a migration yeniden üretildi, gönderim işinde durdurma T169a'nın `DispatchReachableAsync` ayrımından önce, sürüm başlıkları (06 v6.40, 07 v4.26, 08 v3.48 — ikinci birleştirmeyle), STATUS/STATUS_CHANGELOG (T169a merge teyidi CI `39995712182` ✓, Docker Publish `39995711969` ✓), DEFERRED_BACKLOG 49/169, DOC_CHANGELOG, MEMORY. **İkinci birleştirme:** push sonrası PR çakışmalı çıktı (main `44d32f54`, T165 #375 ilerlemişti; çakışmalı PR'da CI koşmaz) → merge commit; yalnız belge çakışmaları (sürüm 06 v6.40, 07 v4.26, runbook başlığı, STATUS/STATUS_CHANGELOG, DOC_CHANGELOG, MEMORY). T165'in "her kural para / para değil sınıflı" testi gereği yeni `outbound-dispatch-paused` kuralı `category: money` aldı (29 para kuralı, runbook §P.4 satırı) | Derleme 0 hata; testler aşağıda |

**Düzeltme turu test koşusu (LocalDB):** tam proje koşuları (birleştirme + düzeltmeler, HEAD `ed99021a`) — Transactions **1940/1940**, Platform **235/235**, Shared **593/593**, API **1051/1051** (`dotnet test tests/<proje>`); frontend `src/i18n src/components/admin src/lib/api` 77/77 (birleşen i18n). Docker kullanılmadı. Commit'ler: `70fb0547` main birleştirmesi · `06377835` F1 + S4-2 + S4-4 kodu · `ed99021a` S4-1 · S4-5 · alarm · belgeler · rapor commit'i (bu satır). İkinci birleştirmeden (T165) sonra hedefli koşu: API (Monitoring, OutboundApproval, OutboundRowClosure, DepositRefundDailyLimit, PaymentAddressIndexFloorHost, MigrationRehearsal) **272/272**, Transactions (OutgoingTransferDispatchJob, PaymentAddress) **208/208**. Dal CI: PR #377'de.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✗ FAIL (HEAD `8009a52f`, bağımsız doğrulama) → düzeltme turu bitti, yeniden doğrulama bekliyor |
| Bulgu sayısı | 1 × S1 ⚠ PARA (F1), 5 × S4 |
| Düzeltme gerekli mi | Yapıldı (yukarıda "Düzeltme turu") |

## Kapanış (yeniden doğrulama PASS, 2026-10-23)

- **Yeniden doğrulama:** hedefli, ✓ PASS, HEAD `1c884579` (bağımsız alt ajan, Opus high). F1 (S1 ⚠ PARA) ve önceki 5 × S4 kapandı; yeni bulgu 3 × S4. Bozma 17/18 yakalandı. Kaynak rapor: `C:/Users/teknodeveloper/.claude/projects/c--projects-Escrow/f9-handoff/T172a_REVALIDATION.md`.
- **3 × S4 kapandı:**
  - **S4-1** — onay CAS'ındaki harf duyarlı not eşleşmesini (`Latin1_General_100_BIN2`) hiçbir test korumuyordu (M14 yaşadı). `T172a_S4_1_Rev_A_Note_That_Turned_Lowercase_Since_The_Read_Is_Not_Reopened` eklendi: okuma ile yazma arasında not küçük harfe dönerse onay satırı açmaz. `Collate` çağrısı kaldırılınca test kırılıyor (1 başarısız), geri konunca geçiyor.
  - **S4-2** — main `c3b49409` (T172b #378, chore #380) dala merge commit ile alındı. Çakışmalar: belge sürüm başlıkları (08 v3.49), DOC_CHANGELOG, DEFERRED_BACKLOG sayacı (dosya satırlarından yeniden sayıldı: 48 aktif / 170 çözülmüş), STATUS/STATUS_CHANGELOG, runbook (iki dalın alarm satırları korundu), `NotificationAlertingProvisioningTests` (iki dalın para kuralları listede; `MoneyRules` = **31**). `rules.yml` iki dalın kurallarıyla kendiliğinden birleşti.
  - **S4-3** — DEFERRED_BACKLOG durum satırı T165 (`44d32f54`) birleştirmesini de anıyor.
- **Testler (LocalDB, API projesi):** `OutboundRowClosure` + `NotificationAlertingProvisioning` filtresi 148/148 geçti (her kuralın para / para dışı sınıflaması dahil).

## Altyapı Değişiklikleri

- Migration: **Var** — `T172a_PostRestoreTools`: `CK_OutboundRowClosures_Network_Ethereum` → `CK_OutboundRowClosures_Network_Known` (`[Network] IN ('TRON','ETHEREUM')`); SystemSettings seed **83** `outbound.dispatch_paused` = `false` (Id `…0053`; main birleştirmesinde T169a 81–82'yi aldığı için migration birleşik model üzerinde aynı zaman damgasıyla yeniden üretildi — Up/Down yalnız satır Id'si farklı). `Down` Ethereum CHECK'ini geri koyar — TRON kapatma kaydı varsa reddeder (kayıt silinmez, bilinçli).
- Config/env: **Var** — `PaymentAddresses__MinimumNextIndex__TRON/__ETHEREUM` (compose: `PAYMENT_ADDRESS_MIN_NEXT_INDEX_*`, varsayılan 0 = taban yok).
- Docker: compose backend ortamına iki satır; imaj değişmedi. TRON servisine yeni uç (salt okunur).

## Commit & PR

- Branch: `task/T172a-geri-yukleme-para-araclari` (origin/main `792f8ed2`'den)
- Commit'ler: `8610a3cb` backend araçları · `ffdca1b7` TRON verify + sözleşme · `ded2398c` frontend · `6ae9ced4` belgeler + compose · `af75eadf` rapor/durum · `f8189793` PR numarası · `9eae672c` migration provası testleri
- PR: #377
- CI: ✓ `39986431856` success (HEAD `9eae672c`; kod değişikliğinin son hâli). Bu satırı yazan rapor commit'inin run'ı ayrıca izlendi.

## Kararlar (öneriyle kaydedildi — ⚠)

1. **Durdurma anahtarı `outbound.dispatch_paused` eklendi** (kabul kriterinde yazılı değil) — ⚠ PARA. Gerekçe: kapatma aracı zinciri imza servisinden okur; geri yüklemeden sonra servis açılınca gönderim işi geri yüklenen onaylı ödemeleri ve sweep'leri anında gönderirdi — aracın önlemeye çalıştığı çift ödeme. `false` dışı her değer = durdurulmuş; ~~satır yoksa durmaz (migration tohumlar)~~ **düzeltme turu (S4-2): satır yoksa da durmuş sayılır.** Admin'in elle sıcak→soğuk aktarımı (AD39) kapsam dışı.
2. **Yeni araç yerine AD33–AD37 genişletildi** — aynı iki kişi kuralı, aynı `MANAGE_SETTINGS` yetkisi, aynı AuditLog eylemleri; yeni izin/eylem kataloğu değişikliği yok. ⚠ PARA/GÜVENLİK: yetki T157'deki gibi `MANAGE_SETTINGS`.
3. **FAILED satır eski kopyadan değil zincirden getirilir** — onay işi blok, onay sayısı, gerçek ücreti zincirden yazar; eski kopya gerekmez (disk kaybında da çalışır). Sınır: `ConfirmedAt` onay anı (özgün an değil); TRON depozit gönderiminin `PlatformNetworkCost`'u ölçülmemiş kalır (AD38 `costUnmeasured`). ⚠ PARA (muhasebe tarih/gider ayrıntısı).
4. **Yalnız `ErrorMessage` tam olarak `POST_RESTORE_DUPLICATE` ile başlayan `FAILED` satır açılır** (büyük/küçük harf duyarlı; runbook SQL'inin yazdığı biçim). ⚠ PARA: başka sebeple `FAILED` satır gerçekten gönderilmemiştir.
5. **Gönderen kaydetmeyen satırda gönderen gönderim işinin kuralıyla çözülür ve onayda satıra yazılır** (sweep'i yolda Ethereum alıcı iadesi çözülmez → ret). ⚠ PARA: defter satırı doğru adresten sayar.
6. **İndeks tabanı SystemSetting değil dağıtım ayarı (env)** — geri yükleme veritabanını geri sararken tabanı geri saramaz ve backend'in ilk dakikasından önce yerindedir. ⚠ PARA. (Yapımdaki "hatalı değer backend'i açmaz" yalnız ad ve negatif sayı için doğruydu — F1; düzeltme turunda her hatalı değer için doğru.)
7. **Taban doldurma kuralı:** eski kopyanın en büyük indeksi + 1000; eski kopya yoksa geri yüklenenin en büyüğü + 10000 (ayraçsız — F1). ⚠ PARA: atlanan indeks maliyetsiz, pay bilinmeyen satış sayısını örter.
8. **TRON kimliği** 64 onaltılık hane, öneksiz; küçük harfle saklanır; `0x`'li biçim reddedilir.
9. **TRON verify yalnız solidity düğümünü okur** (geri dönülmez işlem), düğüm başka kimliğe cevap verirse 502.
10. **Elle SQL yolları runbook'ta yedek olarak kaldı** (araç kullanılamazsa), araç birincil yol.

⚠ SAHİBE SORU: yok.

## Known Limitations / Follow-up

- Araçla getirilen satırın `ConfirmedAt`'i onay anı; TRON depozit gönderiminde platform harcaması ölçülmez (karar 3).
- Aynı alıcı/token/tutarda iki açık satır araçla kapanmaz (`AMBIGUOUS_ROW_MATCH`, T157 kuralı) — elle yol.
- Eski kopyada olup geri yüklenende olmayan depozit adresleri izlenmez (runbook (b), gezginde elle).
- Uçtan uca geri yükleme provası (gerçek imza servisleri + durdurma + araç) Docker gerektirir; bu görevde Docker kullanılmadı — T172 sunucu provasında (Nile + Sepolia) koşulmalı.

## Notlar

- **Working tree:** temiz (Adım -1). Dal, yönetici tarafından `792f8ed2`'ye ilerletildi (T174 dahil); ilk oturumun kaydedilmemiş değişiklikleri yoktu, yapım baştan yazıldı.
- **Main CI startup (Adım 0):** `39930836478` ✓ success, `39930828460` ✓ success, `39883489767` ✓ success.
- **Bağımlılık:** T164 ✓ (runbook §N.8), T169 ✓ (onay kapısı), T157 ✓ (AD33–AD37).
- **Dış varsayımlar:**
  - TronGrid `walletsolidity/gettransactioninfobyid` cevap biçimi — **ölçüldü** (Nile, 2026-10-23, `29a02e38…e566`): `id`, `blockNumber`, `receipt.result = "SUCCESS"`, `log[].address` 20 bayt hex (`41`'siz), 3 konu (`ddf252ad…`), 64 hex `data`; bilinmeyen kimlik `{}`; büyük harfli kimlikle sorgu aynı işlemi küçük harf `id` ile döndürür.
  - `TronWeb.isAddress` / `address.toHex` — yerelde ölçüldü (geçersiz base58 `TXqH2JBk…` `false`; testler buna dayanır).
  - ~~.NET options binder ortam biçimi `PaymentAddresses__MinimumNextIndex__TRON` → `Dictionary<string,int>` — birim testte ölçüldü.~~ **Yanlış genelleme (F1):** birim test yalnız geçerli tam sayıyı ve doğrudan `Bind` çağrısını denemişti; binder çevrilemeyen öğeyi sessizce atıyor. Düzeltme turunda `Dictionary<string,string?>` + katı ayrıştırma; host düzeyinde ölçüldü (`PaymentAddressIndexFloorHostTests`).
  - SQL Server tekil indeksinde `EventIndex = NULL` iki satırı çakıştırır (aynı hash ikinci satıra yazılamaz) — LocalDB'de ölçüldü (`…_OneChainTransaction…`).
- **Ortam:** testler LocalDB (`(localdb)\MSSQLLocalDB`, her test kendi rastgele veritabanı); Docker kullanılmadı.
- **Sır tarayıcısı:** 64 hex olay imzası (`Transfer` keccak) ve 12 küçük harfli kelimelik yorum sahte pozitif verdi; atlatma yerine sabit iki parçaya bölündü, yorum yeniden yazıldı, sözleşme dosyasında tek karakter tekrarlı kimlik kullanıldı (bypass yok).
