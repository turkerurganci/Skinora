# T157 — Admin ekranları ağ başına

**Faz:** F8 | **Durum:** ⏳ Doğrulama tur 1 ✗ FAIL → düzeltme turu yapıldı, yeniden doğrulama bekliyor | **Tarih:** 2026-10-19

---

## Yapılan İşler

- **⚠ PARA — gönderim satırı kapatma aracı** (`Skinora.API/Services/WalletClosures/`, `AdminOutboundClosuresController`, 07 §9.32 AD33–AD37): Ethereum servisi günlüğünü kaybedince (`JOURNAL_BEHIND_CHAIN`, runbook §D adım 7) zincirde olan ama backend'de `PENDING` kalan giden satırı zincirdeki hash'e bağlar. **Para göndermez**: onay yalnız `TxHash` yazar ve `PENDING → DETECTED` yapar, olağan onay işi bitirir. Kurallar: yetki `MANAGE_SETTINGS`; öneren ≠ onaylayan (oturumdan; servis + veritabanı CHECK'i); onaylayan hash'i yeniden yazar; yalnız Ethereum + giden tür + `PENDING` + hash'siz satır (öneri ve onay anında, onay yazımı koşullu güncelleme); hash `0x`+64 hane, bir hash en çok bir satıra/canlı kayda (servis + süzgeçli tekil indeksler); durum ucu **kesinleşmiş + başarılı** demeli; ayrıca servisin yeni `verify` ucu tx'in **alıcı + tutar + token (+ gönderen)** ile satırı gerçekten ödediğini doğrulamalı — fail-closed (cevap vermeyen/desteklemeyen servis = ret); her adım audit log'a (4 yeni `AuditAction`).
- **Veri modeli:** `OutboundRowClosure` (06 §3.8b) + `OutboundRowClosureStatus` (§2.27); migration `T157_OutboundRowClosures` (CHECK'ler, süzgeçli tekil indeksler, Down kayıt varken reddedilir, SQL 50157).
- **Cüzdan/bakiye/yakıt/transfer görünürlüğü ağ başına** (AD38 `GET /admin/wallets`, 07 §9.33; S22 `/admin/wallets`): sıcak/soğuk/(Ethereum) yakıt cüzdanı, bakiyeler her ağın kendi servisinden, okunamayan bakiye 0 gösterilmez, taban altı ve sıcak-adres uyuşmazlığı uyarısı, giden satır özeti, son soğuk aktarımlar ve giden satırlar. Yakıt adresi yalnız servisten: Ethereum servisine `POST /api/wallet/platform` (sözleşme `contracts/platform-wallets/response.json`).
- **Sıcak→soğuk aktarım ağ başına** (AD39, 07 §9.34): `network` gövdede (verilmezse TRON), Ethereum yalnız USDT, ücret tavanı isteğe eklenir, servis beklerse 503 `TRANSFER_DEFERRED` (yazım yok). `HotWalletService`, `HttpBlockchainSidecarClient` (`Deferred` durumu, `feeCapUsd`).
- **AD6/AD7 `network`** alanı + liste `network` süzgeci; S15 Ağ kolonu/süzgeci; S16'da tx hash bağlantıları işlemin ağına göre Etherscan/Tronscan (T156'dan devir).
- **Ethereum servisi (sidecar-ethereum):** `POST /api/wallet/platform`, `POST /api/transfer/verify` (`TransferVerifier`, salt okunur), sözleşme dosyaları (`contracts/{cold-wallet,platform-wallets,transfer-verify}`).
- **Frontend:** `/admin/wallets` sayfası (`WalletsOverview`, `NetworkWalletsCard`, `ColdTransferForm`, `OutboundClosurePanel`, `ClosureModals`), API/hook'lar, sidebar + izin haritası, 4 dilde metinler, S15/S16 değişiklikleri.
- **Belgeler:** 04 v4.11 (S22), 06 v6.32, 07 v4.15, 08 v3.36, 11 v1.16 ("T157 kararları"), DEPLOY_RUNBOOK (§D adım 7.4 araçla, adım 8.1/8.6).

## Etkilenen Modüller / Dosyalar

- **Backend (yeni):** `Domain/Entities/OutboundRowClosure.cs`, `…/OutboundRowClosureConfiguration.cs`, `Skinora.Shared/Enums/OutboundRowClosureStatus.cs`, migration `20261018205550_T157_OutboundRowClosures`, `Services/WalletClosures/*`, `Services/AdminWallets/AdminWalletOverviewService.cs`, `Controllers/AdminOutboundClosuresController.cs`.
- **Backend (değişen):** `AuditAction`, `AuditLogCategoryMap`, `HotWalletService`, `IHotWalletService`, `AdminWalletsController`, `HttpBlockchainSidecarClient`, `IBlockchainSidecarClient` (varsayılan arayüz metodu `GetPlatformWalletsAsync`), `IBlockchainTransferClient` + `HttpBlockchainTransferClient` (`VerifyTransferAsync`), `AdminTransactionQueryService` + DTO/sorgu/denetleyici, `TransactionsModule` (DI).
- **sidecar-ethereum:** `src/transfer/TransferVerifier.ts`, `src/api/{verifyHandler,platformWalletsHandler}.ts`, `routes.ts`, `index.ts`, `contracts/*`.
- **Frontend:** bkz. yukarı (`src/components/admin/*`, `src/app/[locale]/admin/wallets`, `lib/api/admin.ts`, hook'lar, i18n 4 dil, `types/enums.ts`).
- **Dokunulmayan:** sidecar-blockchain (TRON), sidecar-fake, `docker-compose*`, `.env.example` (yeni env yok).

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Cüzdanlar, bakiyeler, yakıt cüzdanı ve transferler ağ başına görünür | ✓ | `AdminWalletOverviewTests` (6): ağ başına blok, ETH 18 hane, yakıt adresi servisten, UNAVAILABLE/NOT_CONFIGURED sıfır değil, uyuşmazlık, sayaçlar ağ başına; FE `WalletsOverview.test.tsx`; bozma M40–M46 |
| 2 | Admin ekranındaki Tronscan bağlantısı ağa göre Etherscan/Tronscan | ✓ | `TransactionNetwork.test.tsx` (Ethereum → etherscan, TRON/null → tronscan); `AdminTransactionNetworkTests` (AD6/AD7 `network`, süzgeç); bozma M47–M48 |
| 3 | ⚠ PARA aracı: yalnız uygun yetkili admin | ✓ | `AdminOutboundClosuresEndpointTests`: anonim 401, `MANAGE_SETTINGS`'siz 403 (6 uç); bozma M33 |
| 4 | Öneren ≠ onaylayan | ✓ | `Approve_ByTheProposer_IsRefused…`, şema testi (CHECK), uç testi: kimlik oturumdan; bozma M06, M21, M31, M32 |
| 5 | Yalnız Ethereum ve yalnız kesinleşmemiş (PENDING, hash'siz) giden satır | ✓ | `Propose_ForATronRow…`, `…InboundRow…`, `…NotPending` (3 durum), `…RowMoved…`; bozma M01–M03, M22, M24 |
| 6 | Hash biçimi doğrulanır, aynı hash iki satıra bağlanamaz | ✓ | 8 biçim vakası, `Propose_AHashAlreadyOnAnotherRow…`, canlı kayıt/reddedilmiş yeniden kullanım, şema tekil indeksi; bozma M04, M17, M23 |
| 7 | Her adım audit log'a | ✓ | öneri/onay/ret/reddedilen onay satırları; bozma M14–M16, M21 |
| 8 | Araç para göndermez, yalnız satırı kapatır | ✓ | `Approve_ByADifferentAdmin…` (Broadcasts boş), `AClosedRow_IsNotSignedAgain_ByTheDispatcher…` (dispatcher yeniden göndermez, onay işi CONFIRMED yapar); bozma M12, M13 |
| 9 | Hash'in zincirde o satırın alıcı/tutarına denk geldiğini servis üzerinden doğrula | ✓ | yeni `POST /api/transfer/verify` (`TransferVerifier.test.ts` 17, `verifyHandler.test.ts` 15) + backend `Approve_UnlessTheServiceConfirms…`, istemci sözleşme testleri; bozma M25–M29, M49–M52, S01–S10 |
| 10 | Sıcak→soğuk aktarım Ethereum'da (T152 (vii) devri) | ✓ | `HotWalletServiceTests` Ethereum (5), `AdminWalletsEndpointTests` (+6), `EthereumSidecarAdminContractTests`; bozma M34–M39, M53 |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend yeni/değişen | ✓ API 854/854 (tek tek ve tüm çözümde; paralel tam çözüm koşusunda bir yaşam döngüsü testi LocalDB yükü yüzünden bir kez kırıldı, tek başına 61/61), Transactions 1637/1637, Platform 235/235, Shared 592/592, Transactions sözleşme 30/30 | `dotnet test` (LocalDB: `INTEGRATION_TEST_SQL_SERVER=(localdb)\MSSQLLocalDB`) |
| sidecar-ethereum (vitest) | ✓ 750/750 | `npx vitest run`; `tsc`, `eslint`, `prettier` temiz |
| Frontend (vitest) | ✓ 387/387 | `npx tsc --noEmit`, `npm run lint`, prettier, `npm run build`, `i18n:check` (1574 anahtar × 4 dil) temiz |

**Bozma denemeleri:** **65 bozma / 64 yakalandı, 1 eşdeğer** — kapatma servisi M01–M30 (29/30; M30 eşdeğer: `IgnoreQueryFilters` sorgu genelinde geçerli, kullanıcı birleşimindeki yinelenen çağrı gereksiz), denetleyici M31–M33 (3/3), AD39 M34–M39 (6/6), genel bakış M40–M46 (7/7), AD6/AD7 M47–M48 (2/2), istemciler M49–M54 (6/6), Ethereum servisi `verify`/platform S01–S11 (11/11); her biri tek değişiklik, sonra geri alındı. Örnekler: öneren=onaylayan, hash'in yeniden yazılmaması, zincir cevabının yok sayılması, doğrulayıcının fail-open olması, alıcı/tutar/token/gönderen/reorg log karşılaştırmalarından birinin kalkması, denetleyicinin kimliği oturumdan almaması, ETH'nin 6 haneyle ölçeklenmesi, okunamayan bakiyenin 0 gösterilmesi. Testler: backend API 854/854 (+ rehearsal 4, kapatma 50, genel bakış 6, AD6/AD7 5), Transactions sözleşme 30/30, sidecar-ethereum 750/750, frontend 387/387

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (ayrı doğrulama chat'i) |

## Altyapı Değişiklikleri

- Migration: **Var** — `T157_OutboundRowClosures` (yeni tablo; Down kayıt varken 50157 ile reddeder). Prova: `OutboundRowClosureMigrationRehearsalTests` (4).
- Config/env değişikliği: Yok.
- Docker değişikliği: Yok.

## Commit & PR

- Branch: `task/T157-admin-ag-basina`
- PR: #354
- CI: ✓ PASS — run 39177506961 (HEAD bc0d421b; ilk run 39175939001 yalnız yeni test dosyalarında dotnet format boşluk hatasıyla kırıldı, düzeltme ileri taşındı)

## Known Limitations / Follow-up

- **Açık sorular (yöneticiye)** — 11 §F8 "T157 kararları": (1) ayrı `MANAGE_WALLETS` izni; (2) servisin `JOURNAL BEHIND CHAIN` nonce aralığını verip aracın onayı "hash'in nonce'u aralıkta" şartına bağlaması; (3) Ethereum sıcak cüzdan stablecoin üst-limit izleyicisi.
- ~~`verify` aynı alıcıya aynı tutarı ödeyen iki özdeş açık satırı ayıramaz.~~ Düzeltme turunda yönetici kararıyla ret kuralı oldu (`AMBIGUOUS_ROW_MATCH`).
- Cevabı kaybolan Ethereum soğuk aktarımında yeniden deneme ikinci aktarım yapabilir (kayıp yok, mutabakat fazlayı gösterir) — runbook §D adım 8.6.
- sidecar-fake'te `/api/wallet/platform` ve `/api/transfer/verify` yok: e2e'de cüzdan ekranı Ethereum için UNAVAILABLE gösterir (kapsam dışı).
- Araç sonrası `ETHEREUM_JOURNAL_ACKNOWLEDGED_NONCES` (adım 5) hâlâ elle.

## Notlar

- Working tree: temiz (dal `origin/main` `99b808cf`'ten). Main CI son 3 run: `39164777560`, `39164777570`, `39153430245` hepsi success.
- Dış varsayımlar: Ethereum servisinin durum ucu günlüksüz hash için alıcı/tutarı denetlemez → kodla doğrulandı (`TransferService.status` → `contractRetOf(null, …)` yalnız `status === 1`), bu yüzden `verify` ucu eklendi. Sepolia/mainnet'e canlı çağrı yapılmadı (RPC anahtarı yok); doğrulayıcı makbuz biçimi birim testlerinde sahte makbuzla sınandı.
- Frontend yapımı alt ajanla yapıldı (aynı dal, ayrı dizin).

---

## Düzeltme turu (2026-10-19)

Bağımsız doğrulama tur 1 ✗ FAIL verdi (HEAD `47842493`). Arada main'e T158 (`92154a6d`) girdi. Bu tur: main birleştirmesi + bulguların düzeltilmesi. Merge yok; hedefli yeniden doğrulama bekleniyor.

### Main birleştirmesi

- Çakışmalar: MEMORY, 06, 08, 11, DEPLOY_RUNBOOK, IMPLEMENTATION_STATUS (iki tarafın kayıtları korundu; 06 v6.33, 08 v3.38, 11 v1.18), `IBlockchainTransferClient.cs` (T158'in beklentili `GetStatusAsync` aşırı yüklemesi + T157 `VerifyTransferAsync` birlikte), `sidecar-ethereum/src/api/routes.ts` (finality'li `wallet/derive` + `wallet/platform`).
- İki testte T158'in yeni kurucu parametreleri (sıcak cüzdan sağlayıcısı, kontrat kaydı, ikinci `HttpClient`).
- Snapshot iki migration'ı da içeriyor (`T157_OutboundRowClosures`, `T158_PaymentAddressMonitorFromBlock`); `dotnet ef migrations has-pending-model-changes` → değişiklik yok.
- **Anlamca çakışma (⚠ PARA):** T158 (g)'den sonra Ethereum servisi günlüğünde olmayan bir hash'e beklentisiz durum sorgusunda `UNKNOWN` döner. Kapatma aracının durum sorgusu beklentisizdi; birleştirmeden sonra araç **hiçbir satırı onaylayamazdı** (her onay `CHAIN_SERVICE_UNAVAILABLE`). Düzeltildi: sorgu satırın beklentisini taşır, `Unverified` → `HASH_DOES_NOT_MATCH_ROW`.

### Bulgular

| # | Bulgu | Ne yapıldı | Bozma kanıtı |
|---|---|---|---|
| a | 🟠 PARA — `verify` `row.Token` gönderiyordu; yanlış token iadesinde zincirde giden token `ActualTokenAddress`'teki token | Yeni ortak kural `OutboundTransferExpectation` (token, gönderen, kontrat). Gönderim işi, onay işinin durum sorgusu ve kapatma aracı aynı kuralı kullanır. Çözülemezse öneri/onay reddedilir (`ROW_TRANSFER_UNRESOLVED`). Aday listesi ve onay penceresi `chainToken`'ı gösterir | D01, D02, D08, D10, D15 |
| T158 | Kapatılan satırın onay işiyle `CONFIRMED` olması | Gerçek SQL'de iki uçtan uca test: USDC'li yanlış token iadesi ve sweep sonrası iade (gönderen sıcak cüzdan) — öneri → onay → **gerçek onay işi** → `CONFIRMED`. Zincir sahtesi beklentiyi denetler (uymazsa `UNKNOWN`). Ödeme testi de aynı sahteyle | D03, D10 |
| b | 🟡 `SIDECAR_UNAVAILABLE` "Hiçbir şey gönderilmedi" diyordu | 4 dilde "gönderilmiş olabilir; yeniden denemeden önce Etherscan/Tronscan'de kontrol edin"; 07 §9.34 hata satırı; FE testi | D16 |
| c | 🟡 Belgeler koddan geride | 07 §9.32 kurallar + hata listesi (`HASH_DOES_NOT_MATCH_ROW`, `ROW_TRANSFER_UNRESOLVED`, `AMBIGUOUS_ROW_MATCH`), "Bilinen sınır" notu değişti; 08 §3.7.6 uç listesi + "Admin aracı" (b); DEPLOY_RUNBOOK §D 7.4 (makine denetimi, benzer satır, elle yol) ve §J.5 | — |
| d | 🟢 Test boşlukları | B28 `Reject_AnApprovedClosure_IsRefused…`; S7 `Approval` olayı (sidecar); B5 hash'siz `FAILED`/`DETECTED` satıra öneri; S12 servis hata kodu 502/400 (sidecar) + 200 ile gelen hata gövdesi backend'de `Unavailable` | D11, D12, D13, D14 |
| e | Yönetici kararı (⚠ PARA) — benzer açık satır | Aynı ağda aynı alıcı + aynı (zincirdeki) token + aynı tutarda başka açık (`PENDING`/`DETECTED`) giden satır varsa öneri ve onay reddedilir (409 `AMBIGUOUS_ROW_MATCH`, onayda reddedilme audit'li). 07, runbook (elle yol), 11 §F8 | D04, D05, D06, D07 |

**Bozma:** 16 bozma / 16 yakalandı (ayrıntı 11 §F8 "T157 düzeltme turu" (g)). Her biri tek değişiklik, test koşturuldu, geri alındı.

### Düzeltme turu testleri

- Backend: `OutboundRowClosureTests` 69/69 (gerçek SQL Server, Testcontainers); tüm çözüm yeşil — API 878, Transactions 1690, Shared 592, Platform 235, Notifications 193, Auth 132, Fraud 101, Disputes 83, Steam 59, Users 49, Realtime 44, Admin 22, Payments 6 (entegrasyon dahil, Docker açık). `dotnet format --verify-no-changes` temiz. `has-pending-model-changes` temiz.
- sidecar-ethereum: `npm test` 828/828, `tsc --noEmit` temiz, `eslint src` temiz, prettier (satır sonu yok sayılarak) temiz.
- Frontend: vitest 391/391, `tsc --noEmit` temiz, `npm run lint` temiz, `i18n:check` 1578 anahtar × 4 dil eşit, prettier temiz.
- Gerçek ağa işlem gönderilmedi.

### Açık noktalar (yöneticiye)

- **Elle yolun ayrıntısı benim önerim (⚠ PARA):** runbook §D 7.4'te `AMBIGUOUS_ROW_MATCH` / `ROW_TRANSFER_UNRESOLVED` için yazdığım yol — hangi satırın imzalandığını eski günlük yedeğinden ya da servis log'undan kanıtla; kanıt varsa yönetici kararı + iki kişi + audit notuyla veritabanında bağla; kanıt yoksa satırlar `PENDING` kalır ve o hesabın günlük onayı (alt adım 5) verilmez. Onayınız gerekir.
- Eski açık sorular (ayrı izin, nonce aralığı, Ethereum sıcak cüzdan üst-limit izleyicisi) aynen duruyor.
