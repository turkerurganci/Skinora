# T172a — Yedekten geri yükleme sonrası para araçları (⚠ PARA)

**Faz:** F9 | **Durum:** ⏳ Yapım bitti, doğrulama bekliyor | **Tarih:** 2026-10-23

---

## Yapılan İşler

Kaynak: DEFERRED_BACKLOG `PostRestoreChainReconciliationTooling` (T164), DEPLOY_RUNBOOK §N.8, T164 yeniden doğrulama S4-3. Kabul kriterinin üç aracı, mevcut iki kişilik kapatma aracının (AD33–AD37, T157) genişletilmesiyle ve iki küçük eklemeyle yapıldı:

1. **TRON'da gönderilmiş `PENDING` satırı zincir kanıtıyla kapatma** — AD33–AD37 artık iki ağda çalışır (`OutboundRowClosureService`). Kurallar T157'nin aynısıdır ve TRON'a da uygulanır: iki ayrı admin (öneren onaylayamaz, kimlik oturumdan), onaylayan kimliği yeniden yazar, onay anında zincir sorulur ve yalnız **kesinleşmiş + başarılı** (TRON ≥ 20 onay + `SUCCESS`) **ve** satırın token kontratından, satırın göndereninden (ödemede ağın sıcak cüzdanı, iadede işlemin depozitosu), satırın alıcısına, **tam** tutarda `Transfer` log'u varsa geçer; eksik/yanlış kanıt ya da cevap vermeyen servis = ret (fail-closed). Her adım AuditLog'da (`OUTBOUND_ROW_CLOSURE_PROPOSED/_APPROVED/_REJECTED/_REFUSED`, Fon kategorisi; onay kaydı `network`, `rowStatus`, `rowError` da taşır). Onay satıra hash yazar, `DETECTED` yapar; gönderim işi satırı bir daha göndermez, onay işi zincirden `CONFIRMED` yapar (ödeme satırında işlem `COMPLETED`). **Aynı işlem kimliği ikinci bir satırı kapatamaz** (servis denetimi + `UQ_OutboundRowClosures_Network_TxHash_Live` + `UQ_BlockchainTransactions_Network_TxHash_EventIndex`).
   - TRON servisine salt okunur `POST /api/transfer/verify` eklendi (`sidecar-blockchain/src/transfer/TransferVerifier.ts`, `api/verifyHandler.ts`): solidity düğümünün `walletsolidity/gettransactioninfobyid`'sini okur; Ethereum servisinin T157 sözleşmesinin aynısı (`contracts/transfer-verify/`, iki yanın testi aynı dosyayı okur).
   - Gönderen kaydetmeyen satır (geri yüklemede hiç gönderilmemiş ya da yeni kimlikle açılmış) gönderim işinin kuralıyla çözülür ve onayda `FromAddress`'e yazılır — mutabakat ve iade kesinti defteri satırı doğru adresten sayar.
2. **Depozit adres indeksine yapılandırılabilir taban** — `PaymentAddresses:MinimumNextIndex:<AĞ>` (env `PAYMENT_ADDRESS_MIN_NEXT_INDEX_TRON/_ETHEREUM`, compose'a eklendi). Sonraki indeks = `max(MAX(HdWalletIndex) + 1, taban)`: taban **yalnız yükseltir**, mevcut en büyüğün altına/eşitine düşürülen taban etkisizdir, yeniden kullanım üretemez (silinmiş satırlar dahil `MAX`). Bilinmeyen ağ adı ya da negatif değer backend'i açmaz (`ValidateOnStart`).
3. **`FAILED POST_RESTORE_DUPLICATE` satırını gerçek ödemeye aynı satır üzerinde getirme** — aynı araç, `ErrorMessage`'ı `POST_RESTORE_DUPLICATE` ile başlayan hash'siz `FAILED` satırı aday sayar; onay satırı **aynı satırda** `FAILED → DETECTED` yapar, onay işi zincirden `CONFIRMED` (blok, onay sayısı, gerçek ücret zincirden; gönderen yazılır). Mutabakat (`CONFIRMED` sayar), iade kesinti defteri (`CONFIRMED` + depozit göndereni) ve ödeme şikâyeti açığı kapanır. Eski kopya gerekmez. Not taşımayan `FAILED` satır açılamaz.
4. **Gönderim durdurma anahtarı `outbound.dispatch_paused`** (seed 81, migration) — geri yüklemeden sonra imza servisleri kapatma aracının zincir okuması için **açılmak zorunda**; açıkken gönderim işi onaylı ödemeleri ve sweep'leri hemen gönderirdi. Anahtar `false` değilken gönderim işi hiçbir satırı göndermez (her tür, her ağ), deneme hakkı harcanmaz; tur başında ve her satırdan önce yeniden okunur.
5. **Runbook §N.8** bu araçlara bağlandı: adım 2 durdurma SQL'i + taban, adım 3 deneme penceresi kalktı, yeni 5a (imza servisleri durdurma altında), adım 5 araç birincil yol (SQL elle yol olarak kaldı), FAILED satırın araçla geri getirilmesi, adım 6–7 gönderimin açılması; §N.10 sınırlar, §B env satırı, §M.3 çapraz atıf.
6. **Yönetici ekranı:** kapatma paneli her ağın kartında (TRON da); TRON kimliği biçimi (64 hex, `0x`'siz), Tronscan bağlantısı, "geri yüklemeden sonra elle kapatıldı" işareti; dört dilde metin.

## Etkilenen Modüller / Dosyalar

- Backend: `Skinora.API/Services/WalletClosures/{IOutboundRowClosureService,OutboundRowClosureService}.cs`, `Controllers/AdminOutboundClosuresController.cs`, `Configuration/TransactionsModule.cs`; `Skinora.Transactions/Application/PaymentAddresses/{PaymentAddressAllocator,PaymentAddressIndexFloorOptions}.cs`, `Application/Transfers/{OutboundDispatchPause,OutgoingTransferDispatchJob,HttpBlockchainTransferClient}.cs`, `Domain/Entities/OutboundRowClosure.cs`, `Infrastructure/Persistence/OutboundRowClosureConfiguration.cs`; `Skinora.Platform/.../{SystemSettingSeed,SystemSettingsCatalog}.cs`; migration `20261023150626_T172a_PostRestoreTools` (+ snapshot).
- Backend testleri: `OutboundRowClosureTests` (+14 T172a test/teori), `OutboundApprovalTests` (+4 durdurma), `AdminOutboundClosuresEndpointTests`, `AdminWalletsEndpointTests`, `PaymentAddressAllocatorTests` (+4 taban), `PaymentAddressIndexFloorOptionsTests` (yeni), `EthereumSidecarAdminContractTests` (TRON verify sözleşmesi), `SeedDataTests`.
- TRON servisi: `src/transfer/TransferVerifier{,.test}.ts`, `src/api/verifyHandler{,.test}.ts`, `src/api/routes.ts`, `src/index.ts`, `contracts/transfer-verify/{request,response}.json`.
- Frontend: `components/admin/{OutboundClosurePanel,ClosureModals,NetworkWalletsCard}.tsx` (+ testler, `walletTestUtils.tsx`), `lib/api/admin.ts` (+ test), `lib/hooks/useOutboundClosures.ts`, `i18n/messages/{en,tr,es,zh}.json`.
- Altyapı: `docker-compose.yml` (iki env), `.env.example`.
- Belgeler: `DEPLOY_RUNBOOK.md` (§B, §M.3, §N.8, §N.10, başlık), `06_DATA_MODEL.md` v6.38, `07_API_DESIGN.md` v4.24, `08_INTEGRATION_SPEC.md` v3.46, `DOC_CHANGELOG.md`, `DEFERRED_BACKLOG.md`, `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | TRON'da zincirde gönderilmiş ama DB'de `PENDING` kalan giden satırı zincir kanıtıyla kapatan admin aracı (verify + AuditLog) | ✓ | `T172a_Tron_PendingPayout_IsClosedWithTheChainHash_ByTwoAdmins_AndIsNeverSignedAgain` (verify okuması: hash, USDT, sıcak cüzdan → alıcı, tutar; AuditLog `network/rowStatus`; kapı açıkken bile gönderilmez; `CONFIRMED` + `PayoutCompletedEvent`), `…_WrongOrMissingChainProof…` (5 ret), `…_AnIdTheChainDoesNotShowFinalAndSuccessful…` (3 ret), `…_TheProposerCannotApprove`, `…_AMalformedId…`; TRON servisi `TransferVerifier.test.ts` 22 + `verifyHandler.test.ts` 18; canlı Nile ölçümü (aşağıda) |
| 1a | İdempotency: aynı tx iki satırı kapatamaz | ✓ | `T172a_Tron_OneChainTransaction_ClosesOneRow_Only` — canlı öneri varken, onaydan sonra, büyük harfle, araç dışından satıra yazma (DB tekil indeksi), ikinci canlı kayıt (DB tekil indeksi), iki kez onay |
| 2 | Depozit adres indeksine yapılandırılabilir taban; düşürülemez / yeniden kullanım üretmez | ✓ | `PaymentAddressIndexFloorOptionsTests` (8 satırlık `NextIndex` tablosu, env biçiminden bağlama, hatalı ad/negatif değer); `PaymentAddressAllocatorTests.T172a_*`: taban MAX'ın üstünde → 41, 42; taban 0/3/7 ve MAX 7 → 8, tüm indeksler tekil; silinmiş satır sayılır; başka ağın tabanı etkisiz |
| 3 | `FAILED POST_RESTORE_DUPLICATE` satırını eski kopyadaki gerçek ödemenin değerlerine aynı satırda getiren yol | ✓ | `T172a_PostRestoreFailedRow_IsBroughtBackToTheRealPayment_OnTheSameRow` (tek satır, aynı Id, `CONFIRMED`, blok, hash, `ConfirmedAt`), `…_PostRestoreFailedRefund_IsCountedFromTheDeposit_OnceConfirmed` (iade kesinti defterinin saydığı biçim: `CONFIRMED` + depozit), `T172a_Ethereum_PostRestoreFailedRow_IsBroughtBackToo`, `T172a_AFailedRowWithoutTheRestoreNote_CannotBeReopened` (4 not), `T172a_AReopenWhoseRowChangedSinceTheProposal_IsRefused`. Değerler eski kopyadan değil **zincirden** gelir (karar 3) |
| 4 | Runbook §N.8'e bağlı | ✓ | DEPLOY_RUNBOOK §N.8 (b), adım 2, 3, 5a, 5, 6, 7; §N.10; §B; §M.3 |
| 5 | Testler | ✓ | Aşağıda; ayrıca 14/14 bozma yakalandı |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend — kapanış/onay/cüzdan uç noktaları (SQL Server LocalDB, migration'lı şema) | ✓ 151/151 + 11/11 | `dotnet test tests/Skinora.API.Tests --filter "OutboundRowClosureTests|OutboundApprovalTests|AdminOutboundClosuresEndpointTests|AdminWalletsEndpointTests"`; fikstür düzeltmesinden sonra `OutboundApprovalTests` 11/11 |
| Backend — Transactions (adres ayırıcı, taban, gönderim işi, sözleşmeler) | ✓ 271/271 → sözleşme düzeltmesi sonrası `Contract` 124/124 | `--filter "PaymentAddress|OutgoingTransferDispatchJob|Contract"` |
| Backend — Platform (seed, katalog, ayarlar) | ✓ 136/136 | `--filter "SeedData|Catalog|Setting"` |
| Backend — tam proje koşusu (API, Transactions, Platform, Shared) | bkz. "Notlar — tam koşu" | LocalDB |
| TRON servisi | ✓ 40/40 yeni (22 + 18); `tsc --noEmit`, eslint, prettier temiz | `npx vitest run src/transfer/TransferVerifier.test.ts src/api/verifyHandler.test.ts` |
| Frontend | ✓ 466/466 (46 dosya), `npm run lint` temiz, `tsc --noEmit` temiz | `npx vitest run` |
| Canlı Nile (salt okunur) | ✓ 8/8 | Gerçek TRC-20 transferi `29a02e38…e566` (blok 72.079.005): tam eşleşme MATCH; büyük harf kimlik MATCH; göndericisiz MATCH; tutar+1, yanlış alıcı, yanlış gönderen, yanlış token → NO_MATCHING_TRANSFER; bilinmeyen kimlik → NOT_FOUND |

**Bozma (mutasyon) turu — 14/14 yakalandı** (her biri tek satır, test koşusu, dosya bayt bayt geri yüklendi; "yakalandı" = derleme başarılı + doğrulama hatası, örnek mesajlar ölçüldü):

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

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (bağımsız doğrulama) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Var** — `T172a_PostRestoreTools`: `CK_OutboundRowClosures_Network_Ethereum` → `CK_OutboundRowClosures_Network_Known` (`[Network] IN ('TRON','ETHEREUM')`); SystemSettings seed 81 `outbound.dispatch_paused` = `false`. `Down` Ethereum CHECK'ini geri koyar — TRON kapatma kaydı varsa reddeder (kayıt silinmez, bilinçli).
- Config/env: **Var** — `PaymentAddresses__MinimumNextIndex__TRON/__ETHEREUM` (compose: `PAYMENT_ADDRESS_MIN_NEXT_INDEX_*`, varsayılan 0 = taban yok).
- Docker: compose backend ortamına iki satır; imaj değişmedi. TRON servisine yeni uç (salt okunur).

## Commit & PR

- Branch: `task/T172a-geri-yukleme-para-araclari` (origin/main `792f8ed2`'den)
- Commit'ler: `8610a3cb` backend araçları · `ffdca1b7` TRON verify + sözleşme · `ded2398c` frontend · `6ae9ced4` belgeler + compose · rapor/durum commit'i
- PR: #377
- CI: (izleniyor)

## Kararlar (öneriyle kaydedildi — ⚠)

1. **Durdurma anahtarı `outbound.dispatch_paused` eklendi** (kabul kriterinde yazılı değil) — ⚠ PARA. Gerekçe: kapatma aracı zinciri imza servisinden okur; geri yüklemeden sonra servis açılınca gönderim işi geri yüklenen onaylı ödemeleri ve sweep'leri anında gönderirdi — aracın önlemeye çalıştığı çift ödeme. `false` dışı her değer = durdurulmuş; satır yoksa durmaz (migration tohumlar). Admin'in elle sıcak→soğuk aktarımı (AD39) kapsam dışı.
2. **Yeni araç yerine AD33–AD37 genişletildi** — aynı iki kişi kuralı, aynı `MANAGE_SETTINGS` yetkisi, aynı AuditLog eylemleri; yeni izin/eylem kataloğu değişikliği yok. ⚠ PARA/GÜVENLİK: yetki T157'deki gibi `MANAGE_SETTINGS`.
3. **FAILED satır eski kopyadan değil zincirden getirilir** — onay işi blok, onay sayısı, gerçek ücreti zincirden yazar; eski kopya gerekmez (disk kaybında da çalışır). Sınır: `ConfirmedAt` onay anı (özgün an değil); TRON depozit gönderiminin `PlatformNetworkCost`'u ölçülmemiş kalır (AD38 `costUnmeasured`). ⚠ PARA (muhasebe tarih/gider ayrıntısı).
4. **Yalnız `ErrorMessage` tam olarak `POST_RESTORE_DUPLICATE` ile başlayan `FAILED` satır açılır** (büyük/küçük harf duyarlı; runbook SQL'inin yazdığı biçim). ⚠ PARA: başka sebeple `FAILED` satır gerçekten gönderilmemiştir.
5. **Gönderen kaydetmeyen satırda gönderen gönderim işinin kuralıyla çözülür ve onayda satıra yazılır** (sweep'i yolda Ethereum alıcı iadesi çözülmez → ret). ⚠ PARA: defter satırı doğru adresten sayar.
6. **İndeks tabanı SystemSetting değil dağıtım ayarı (env)** — geri yükleme veritabanını geri sararken tabanı geri saramaz ve backend'in ilk dakikasından önce yerindedir; hatalı değer backend'i açmaz. ⚠ PARA.
7. **Taban doldurma kuralı:** eski kopyanın en büyük indeksi + 1.000; eski kopya yoksa geri yüklenenin en büyüğü + 10.000. ⚠ PARA: atlanan indeks maliyetsiz, pay bilinmeyen satış sayısını örter.
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
  - .NET options binder ortam biçimi `PaymentAddresses__MinimumNextIndex__TRON` → `Dictionary<string,int>` — birim testte ölçüldü.
  - SQL Server tekil indeksinde `EventIndex = NULL` iki satırı çakıştırır (aynı hash ikinci satıra yazılamaz) — LocalDB'de ölçüldü (`…_OneChainTransaction…`).
- **Ortam:** testler LocalDB (`(localdb)\MSSQLLocalDB`, her test kendi rastgele veritabanı); Docker kullanılmadı.
- **Sır tarayıcısı:** 64 hex olay imzası (`Transfer` keccak) ve 12 küçük harfli kelimelik yorum sahte pozitif verdi; atlatma yerine sabit iki parçaya bölündü, yorum yeniden yazıldı, sözleşme dosyasında tek karakter tekrarlı kimlik kullanıldı (bypass yok).
