# T169 — Kalan sahip kararları

**Faz:** F9 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-10-22

---

## Yapılan İşler

- **Karar tablosu** (aşağıda) — proje sahibinin 2026-10-21 modal kararları ve yönetici düzeninde öneriyle kaydedilenler (⚠) yetkili oldukları yere işlendi: `SystemSettingSeed` + migration `T169_OwnerDecisions` (seed satırları ve env'den dolan satırlar; mevcut veritabanında **yalnız satır hâlâ eski varsayılanı taşıyorsa** yeni değere çekilir — admin'in değiştirdiği değer korunur), `.env.example`, deneme yayını overlay'i (`docker-compose.trial.yml`), DEPLOY_RUNBOOK, 02 / 06 / 07.
- **50 $ altında TRON yok (karar 2):** yeni ayar `blockchain.tron.min_transaction_amount_usdt` = 50 (seed 79, `BlockchainNetworkSettingKeys.Tron.MinTransactionAmountUsdt`; Ethereum'da anahtar yok). Tek kural `NetworkMinimumPrice` (bozuk/eksik satır kod varsayılanına düşer, kural sessizce kalkmaz). Sunucu: ilan oluşturma (`TransactionCreationService`, kabul edilen her ağ) ve kabul (`TransactionAcceptanceService`, seçilen ağ — kuraldan önce açılmış ilanı da kapsar) 422 `NETWORK_MIN_AMOUNT_NOT_MET` + `details.network` + `details.minAmount` (07 §1.3 hata zarfı, 07'deki diğer ağ hatalarıyla aynı biçim). Form parametreleri `networkMinPrices` (07 §7.4), detay `networkQuotes[].unavailableReason/minAmount` (07 §7.5). Arayüz: sihirbaz 2. adımda TRON devre dışı + neden (4 dil), kabul formunda TRON devre dışı + neden, tek seçilebilir ağ kendiliğinden seçili; POST hata kodu çevirisi (4 dil).
- **Test boyunca bütün ödemeler admin onaylı (karar 3, ⚠ PARA):** yeni ayar `outbound.auto_send_enabled` = `false` (seed 80; `Default(...)` ⇒ env'den doldurulamaz, T125/T129 kapılarıyla aynı kural; satır okunamazsa kapı **kapalı**). `BlockchainTransaction` += `ApprovedAt`, `ApprovedByAdminId`. Gönderim işi (`OutgoingTransferDispatchJob`) kapı kapalıyken onaysız `SELLER_PAYOUT` / `*_REFUND` satırını aday sorgusunda almaz ve satırı açarken ikinci kez sınar; sweep beklemez. Yeni uçlar AD40 `GET /admin/wallets/outbound-approvals`, AD41 `POST /admin/wallets/outbound-approvals/:id/approve` (`MANAGE_SETTINGS`), audit `OUTBOUND_TRANSFER_APPROVED` (Fon). Admin paneli S22 cüzdan ekranının üstünde "Onay bekleyen ödemeler" paneli (kapı durumu, liste, "geri alınamaz" uyarılı onay penceresi).
- **Yasaklı ülkeler (karar 4):** `auth.banned_countries` seed varsayılanı `NONE` → `IR,KP,CU,MM`; migration hâlâ `NONE` olan satırı taşır.
- **Tutar sınırları (karar 1):** `min_transaction_amount` 10, `max_transaction_amount` 2900 (`.env.example`, runbook §A, `SettingsBootstrapTests` fikstürü; migration hâlâ eski örnek değerdeki satırı taşır).
- **Teslimat süresi 720 dk (⚠; yönetici notu — T168 doğrulaması B1):** `.env.example` 60 → 720; deneme yayını overlay'i `SKINORA_SETTING_DELIVERY_TIMEOUT_MINUTES=${…:-720}` (`.env.trial` boş bırakırsa 720); runbook §A #6 örneği 720 + uyarı notu; `SettingsBootstrapTests` fikstürü 720; migration hâlâ 60 olan veritabanını 720'ye çeker; runbook yeni §M (T171 kurulum günü) satır 1 "teslimat süresi 720 dk mı?".
- **KMS/HSM (karar 5):** karar yok — runbook §M.2 "pilot (T173) sonrası proje sahibine sorulacak".
- **T169a (karar 6):** bu dalda yapılmadı; `plan/11_F9.md`'ye görev satırı (verilen metinle) + `IMPLEMENTATION_STATUS.md` F9 tablosuna `⬚ Bekliyor`; DEFERRED_BACKLOG `DepositRefundDailyLimitUndecided` ⚪ → 🟡 (karar verildi, hedef T169a).
- **Bulgular:** `.env.example`'a `ETHEREUM_MAX_SINGLE_TRANSFER_USDT` / `ETHEREUM_MAX_DAILY_OUTFLOW_USDT` satırları eklendi (3000 / 9000). Ethereum sıcak cüzdan USDT üst limiti izlenmiyor — ucuz değil (izleyiciyi ağ başına yapmak AuditLog/SignalR yükünde ağ alanı = sözleşme değişikliği, ETH 18 ondalık ölçeği, testler); backlog 🟡 `EthereumHotWalletUsdtLimitUnmonitored`, hedef T170.
- **e2e:** `seedHappyPath` kapıyı açar (`outbound.auto_send_enabled = true`) — süitler para hattını (iade CONFIRMED, payout → COMPLETED) ölçer, onay adımını değil; onay yolu backend testlerinde.
- **Devralma:** önceki yapım ajanının commit edilmemiş 51 dosyalık yarım işi `4d92d9b0` (WIP) ile güvenceye alındı; backend büyük ölçüde doğruydu. Düzeltilenler: kabul testinde izlenen varlık nedeniyle oluşan eşzamanlılık hatası (ChangeTracker), onay testlerinde CHECK kısıtına uymayan tohum satırları ve tanımsız sıralama, 07'de var olan §9.33'e (AD38) çarpan bölüm atfı (→ yeni §9.35), prettier biçimi; eksikler tamamlandı (aşağıdaki testler, belgeler, env/compose, e2e).

### Karar tablosu

| # | Karar | Kaynak | Ayar anahtarı / yer | Eski → yeni |
|---|---|---|---|---|
| 1a | En düşük işlem tutarı | sahip (2026-10-21) | `min_transaction_amount` (env `SKINORA_SETTING_MIN_TRANSACTION_AMOUNT`) | 1.0 → **10** |
| 1b | En yüksek işlem tutarı | sahip | `max_transaction_amount` (env) | 10000.0 → **2900** |
| 2 | 50 $ altında TRON yok | sahip | `blockchain.tron.min_transaction_amount_usdt` (yeni, seed 79) | — → **50** |
| 3 | Test boyunca ödemeler admin onaylı | sahip | `outbound.auto_send_enabled` (yeni, seed 80) | — → **false** |
| 4 | Yasaklı ülkeler | sahip | `auth.banned_countries` | NONE → **IR,KP,CU,MM** |
| 5 | KMS/HSM eşiği | sahip: karar yok | runbook §M.2 | — → "pilot sonrası sahibe sorulacak" |
| 6 | T169a (tam maliyet kesintisi + depozit iade 100 $/gün) | sahip | `plan/11_F9.md`, STATUS | — → görev ⬚ |
| 7 | Teslimat süresi | yönetici ⚠ (sahip 2026-10-04: 12 saat) | `delivery_timeout_minutes` (env), trial overlay varsayılanı | 60 → **720** |
| 8 | Tek / günlük gönderim tavanı (TRON + Ethereum USDT) | yönetici ⚠ (mainnet öncesi yeniden sorulur) | `MAX_SINGLE_TRANSFER_USDT`, `MAX_DAILY_OUTFLOW_USDT`, `ETHEREUM_MAX_SINGLE_TRANSFER_USDT`, `ETHEREUM_MAX_DAILY_OUTFLOW_USDT` (`.env.example`) | boş → **3000 / 9000** |
| 9 | Ethereum yakıt tavanları | yönetici ⚠ | `ETHEREUM_FUEL_MAX_SINGLE_ETH`, `ETHEREUM_FUEL_MAX_DAILY_ETH` | boş → **0.001 / 0.02** |
| 10 | Sıcak cüzdan limiti (ağ başına; aşınca sıcakta ≤ 500 $) | yönetici ⚠ | `hot_wallet_limit` (env) | 100000.0 → **4000** |
| 11 | TRON sıcak TRX alt eşiği | yönetici ⚠ | `hot_wallet.trx_balance_minimum` | 100 → **1500** |
| 12 | TRON iade yedek ücreti | yönetici ⚠ | `blockchain.refund_gas_fee_estimate_usdt` | 2.0 → **2.60** |
| 13 | TRON payout yedek ücreti | yönetici ⚠ | `blockchain.payout_gas_fee_estimate_usdt` | 0.50 → **2.25** |
| 14 | TRON ağ ücreti satırı yedeği | yönetici ⚠ | `blockchain.tron.network_fee_fallback_usdt` | 3.00 → 3.00 (kalır) |
| 15 | Ethereum iade yedek ücreti | yönetici ⚠ | `blockchain.ethereum.refund_gas_fee_estimate_usdt` | 0.50 → **0.20** |
| 16 | Ethereum payout yedeği / ücret tavanı | yönetici ⚠ | `blockchain.ethereum.payout_gas_fee_estimate_usdt`, `…fee_cap_usd_per_transfer` | 0.50 / 0.50 (kalır) |
| 17 | Yaptırım listesi | yönetici ⚠ | runbook §M.1 | OFAC SDN elle + aylık |
| 18 | TronGrid planı | yönetici ⚠ | runbook §M satır 12 | ücretsiz (mainnet anahtarı ayrı hesaptan) |
| 19 | Ethereum RPC planları | yönetici ⚠ | runbook §M satır 12 | Alchemy Free + Chainstack Developer |
| 20 | Fiyat sapma kuralı | yönetici ⚠ | `price_deviation_threshold` | 1.0 (kapalı kalır) |
| 21 | `MANAGE_WALLETS` izni | yönetici ⚠ | — | gerekmez (`MANAGE_SETTINGS`) |
| 22 | MASAK "2 kat" yorumu | yönetici ⚠ | — | avukata |
| 23 | Onay için tek admin yeterli | yapımcı ⚠ | AD41 | tek admin (bugün platformda tek admin var) |

## Etkilenen Modüller / Dosyalar

- Backend yeni: `Skinora.Transactions/Application/Networks/NetworkMinimumPrice.cs`, `…/Transfers/OutboundApprovalGate.cs`, `Skinora.API/Controllers/AdminOutboundApprovalsController.cs`, `Skinora.API/Services/OutboundApprovals/OutboundApprovalService.cs`, migration `20261021145849_T169_OwnerDecisions(.Designer).cs`.
- Backend değişen: `SystemSettingSeed.cs`, `SystemSettingsCatalog.cs`, `AuditLogCategoryMap.cs`, `AuditAction.cs`, `BlockchainNetworkSettingKeys.cs`, `BlockchainNetworkSettingsProvider.cs`, `GasFeeSettingsProvider.cs`, `TransactionCreationService.cs`, `TransactionAcceptanceService.cs`, `TransactionDetailService.cs` + `TransactionDetailDto.cs`, `TransactionParamsService.cs`, `TransactionLimitsProvider.cs` + `ITransactionLimitsProvider.cs`, `TransactionLifecycleDtos.cs`, `TransactionErrorCodes.cs`, `TransactionsController.cs`, `OutgoingTransferDispatchJob.cs`, `BlockchainTransaction.cs` + `BlockchainTransactionConfiguration.cs`, `TransactionsModule.cs`, `AppDbContextModelSnapshot.cs`.
- Backend test: yeni `Skinora.API.Tests/Integration/NetworkDimension/OutboundApprovalTests.cs`; değişen `OutgoingTransferDispatchJobTests`, `TransactionCreationServiceTests`, `TransactionAcceptanceServiceTests`, `TransactionDetailServiceTests`, `TransactionParamsServiceTests`, `HotWalletMonitorServiceTests`, `AuditLogCategoryMapTests`, `EnumTests`, `SeedDataTests`, `SettingsBootstrapTests`, `NetworkContractTests`, `NetworkFeePolicyMigrationRehearsalTests`, `NetworkQueueIsolationTests`, `AdminWalletOverviewTests`, `BlockchainWebhookEndpointTests`, `TransactionLifecycleEndpointTests`.
- Frontend yeni: `components/admin/OutboundApprovalPanel.tsx` (+ test), `lib/hooks/useOutboundApprovals.ts`; değişen `WalletsOverview.tsx` (+ test), `AcceptForm.tsx` (+ test), `NewTransactionForm.tsx` (+ networks test), `Step2Details.tsx`, `lib/api/admin.ts`, `lib/api/transactions.ts`, `lib/transactions/networkChoice.ts` (+ test), `types/enums.ts`, `i18n/messages/{en,tr,es,zh}.json`, `i18n/catalog-parity.test.ts`.
- Yapılandırma: `.env.example`, `docker-compose.trial.yml`, `e2e/src/db.ts`.
- Doküman: `02_PRODUCT_REQUIREMENTS.md` v4.19, `06_DATA_MODEL.md` v6.37, `07_API_DESIGN.md` v4.21, `DEPLOY_RUNBOOK.md` (§A, §C, §C.3, yeni §L, §M), `plan/11_F9.md` v1.22, `DEFERRED_BACKLOG.md`, `DOC_CHANGELOG.md`, `IMPLEMENTATION_STATUS.md`, `STATUS_CHANGELOG.md`, `.claude/memory/MEMORY.md`, bu rapor.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | En düşük/en yüksek işlem tutarı kararlaştırıldı ve ayarlara işlendi | ✓ | 10 / 2900: `.env.example:352-357`, runbook §A #7/#8, migration `SetIfStillDefault(min_transaction_amount…)`; `SettingsBootstrapTests` fikstürü |
| 2 | Sıcak cüzdan limiti | ✓ (⚠) | 4000: `.env.example`, runbook §A #18, migration; Ethereum izleyicisi yok → backlog `EthereumHotWalletUsdtLimitUnmonitored` (T170) |
| 3 | Tek/günlük gönderim tavanı (ağ başına) | ✓ (⚠) | `.env.example` TRON + Ethereum 3000/9000; runbook §C.3, §M satır 6 |
| 4 | Yaptırım listesi | ✓ (⚠) | runbook §M.1 (OFAC SDN elle + aylık) |
| 5 | Yasaklı ülkeler | ✓ | seed `IR,KP,CU,MM`; migration; `SeedDataTests` + `CleanDatabase_Has_Every_Seeded_Key…` (her seed değeri migration sonrası DB'de) |
| 6 | Depozitten iadeye günlük tavan | ✓ (karar) | sahip kararı → görev T169a (plan + status), backlog satırı 🟡 |
| 7 | KMS/HSM eşiği | ~ | Karar yok (sahip); runbook §M.2 "pilot sonrası sahibe sorulacak" |
| 8 | TronGrid ve Ethereum RPC planları | ✓ (⚠) | runbook §M satır 12 |
| 9 | TRON için ayrı en düşük satış tutarı (50 $ altında TRON yok) — sunucu + arayüz, testli | ✓ | Backend: `TransactionCreationServiceTests` (T169 testleri: TRON-only reddedilir, iki ağlıda reddedilir, 50'de kabul, Ethereum-only kabul, bozuk satır varsayılana düşer), `TransactionAcceptanceServiceTests.T169_Tron_Below_Its_Minimum_Is_Refused_At_Acceptance_And_Ethereum_Still_Accepts`, `TransactionDetailServiceTests` (quote `unavailableReason`), `TransactionParamsServiceTests` (`networkMinPrices`). Frontend: `networkChoice.test.ts` (`withPriceMinimums`, `buyerNetworkOptions`), `NewTransactionForm.networks.test.tsx` (4 dil: TRON devre dışı + neden; POST'ta TRON yok), `AcceptForm.test.tsx` (TRON devre dışı, Ethereum seçili) |
| 10 | Test boyunca ödemeler admin onaylı (T175 kapısı kapalı başlıyor) | ✓ | `OutboundApprovalTests` (migre DB'de kapı kapalı; payout + iade onaysız gönderilmez, onaylanan aynen gider, audit; sweep beklemez ve listelenmez; tek sefer / açık satır / bilinmeyen satır reddi hiçbir şey yazmaz; kapı açıkken onaysız gider), `OutgoingTransferDispatchJobTests` (T169 birim testleri), `OutboundApprovalPanel.test.tsx`, `WalletsOverview.test.tsx` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Backend unit (tüm Unit filtresi — enum eklendi) | ✓ 2292/2292 | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` (son koşu) — Shared 571, Fraud 19, Disputes 25, Users 49, Steam 59, Auth 96, Platform 161, Realtime 44, API 137, Transactions 981, Notifications 150 |
| Backend integration (dokunulan projeler, LocalDB) | ✓ 1609/1609 | Son koşu: Shared 16/16, Platform 74/74, Transactions 744/744, API 775/775. İlk koşuda Transactions'ta 10 adet 1 ms'lik LocalDB kuruluş titremesi ve API'de 13 kırmızı (beklenen değerler T169'a çekildi, onay testlerinin tohum satırları düzeltildi) vardı; düzeltmeden sonra tam yeniden koşu temiz |
| Frontend | ✓ | `tsc --noEmit` temiz; `eslint` hata yok; prettier (değişen dosyalar, LF) temiz; `i18n:check` parity OK (4 dil × 1626 anahtar); `vitest` 44 dosya / 425 test geçti; `next build` başarılı |
| e2e harness | ✓ | `tsc --noEmit`, prettier, eslint (`e2e/src/db.ts`) temiz; Docker kullanılmadığı için e2e süitleri yerelde koşulmadı (CI advisory leg'leri) |
| Compose | ✓ | `node --test scripts/compose-exposure.test.mjs` 10/10 (trial overlay render) |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (yönetici, ayrı alt ajan) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- Migration: **Var** — `20261021145849_T169_OwnerDecisions`: `BlockchainTransactions.ApprovedAt`, `ApprovedByAdminId` (nullable); iki yeni `SystemSettings` satırı (seed 79–80); üç açıklama `UpdateData`; değer taşımaları yalnız satır hâlâ eski varsayılanı taşıyorsa (`UPDATE … WHERE [Value] IN (eski değerler) AND IsConfigured = 1`), Down tersini aynı koşulla yapar.
- Config/env değişikliği: **Var** — `.env.example` (tutar sınırları, teslimat 720, sıcak limit 4000, TRON + Ethereum gönderim tavanları, Ethereum yakıt tavanları; iki Ethereum tavan satırı ilk kez eklendi).
- Docker değişikliği: **Var** — `docker-compose.trial.yml` backend'e `SKINORA_SETTING_DELIVERY_TIMEOUT_MINUTES=${…:-720}`. Taban `docker-compose.yml` tavan varsayılanları bilinçli olarak boş kaldı (fail-closed — değer `.env`'den gelir).

## Commit & PR
- Branch: `task/T169-sahip-kararlari`
- Commit: `4d92d9b0` — T169: yarım iş (devralındı); `1381036f` — testler, env, trial, e2e, belgeler; son commit dal HEAD'i
- PR: #371
- CI: __CI__

## Admin onay kapısının tasarımı (⚠ PARA)
- **T160c ile birleşme (main `403fe257`):** T160c yeni bir giden tür ekledi, `REFUND_FEE_SWEEP` (depozitte kalan iade kesintisini sıcak cüzdana toplar). Platformun kendi adresleri arası olduğu için kapı onu **tutmaz** (sweep gibi); `OutboundApprovalGateTests` her enum değerini sınıflandırır — yeni bir giden tür eklenirse test kırılır ve bilinçli yerleştirme ister.
- **Ne tutulur:** `SELLER_PAYOUT`, `BUYER_REFUND`, `EXCESS_REFUND`, `WRONG_TOKEN_REFUND`, `INCORRECT_AMOUNT_REFUND`, `LATE_PAYMENT_REFUND`. **Ne tutulmaz:** `SWEEP` ve `REFUND_FEE_SWEEP` (platformun kendi adresleri arası), sıcak→soğuk aktarım (AD39, zaten elle).
- **Nerede:** tek gönderim noktası `OutgoingTransferDispatchJob` (repoda `BroadcastAsync`'in tek çağrıcısı — `grep` ile doğrulandı). Aday sorgusu `autoSend || tür onay gerektirmez || ApprovedAt != null`; `DispatchOneAsync` aynı koşulu satır açılınca yeniden sınar. Onaysız satır `PENDING` kalır, deneme hakkı harcanmaz, `FAILED` olmaz, parti yeri almaz.
- **Onay:** AD41 yalnız `ApprovedAt`/`ApprovedByAdminId` yazar + audit; hiçbir şey göndermez, tutar/alıcı değişmez. Onaylayan oturumdaki admin. Sweep 422, açık olmayan satır 409, ikinci onay 409, bilinmeyen 404.
- **Fail-closed:** ayar yok / yapılandırılmamış / bozuk ⇒ kapı kapalı. Env'den açılamaz; yalnız admin AD9 ile açar (audit'li).
- **Alarm etkileşimi:** Ethereum ücret "bekle" alarmının yaşı onay bekleyen satırı saymaz; onaylanan satır onay anından sayılır.
- **Mevcut akış:** kapı açıkken davranış T169 öncesiyle aynı (`Gate_Open_Rows_Leave_Without_Approval…`, mevcut dispatch testleri kapıyı açarak aynen geçiyor).

## Known Limitations / Follow-up
- Onay bekleyen satır için admin'e anlık bildirim (SignalR/Telegram) yok; her dispatch turunda ağ başına bir uyarı log satırı yazılır ve S22 paneli listeyi gösterir. Bildirim kanalı T165'te bağlanabilir.
- Eşzamanlı iki onay isteği ikisi de geçerse iki audit satırı yazabilir (satır yine tek kez gönderilir — gönderim satır kimliğiyle tekildir); `RowVersion` kontrolü eklenmedi.
- Onay bekleyen bir Ethereum `BUYER_REFUND`'ın beklerken sweep'i biterse kaynak dispatch anında yeniden çözülür (mevcut `RefundSourceResolver` davranışı); TRON iadeleri iptal edilmiş işlemlerde olduğundan sweep edilmez.
- Ethereum sıcak cüzdan USDT üst limiti izlenmiyor → `EthereumHotWalletUsdtLimitUnmonitored` (T170).
- `docker-compose.e2e.yml` test fikstürü değerlerini (teslimat 60 vb.) bilerek korur; e2e harness kapıyı açar.
- KMS/HSM eşiği kararsız (pilot sonrası).
- Paralel dallar: T163 (#369, compose/.env.example), T168 (#370, i18n), T160c (#365) ile çakışma riski — değişiklikler küçük bloklarda; çözüm yöneticide.

## Notlar
- **main birleştirmesi:** dal açıkken T160c (#365) main'e girdi (`403fe257`) ve PR belge başlıklarında/changelog'larda çakıştı (çakışmalı PR'da GitHub CI run'ı yaratmıyor). `origin/main` dala birleştirildi: iki görevin belge girişleri korundu, T169 başlıkları bir üst sürüme alındı (02 v4.19, 06 v6.37, 07 v4.21, 11_F9 v1.22), migration `T169_OwnerDecisions` Designer'ı birleşik model snapshot'ıyla hizalandı (`dotnet ef migrations has-pending-model-changes` → değişiklik yok). Kodda çakışma yoktu.
- Working tree: başlangıçta önceki ajanın commit edilmemiş 51 dosyası (yönetici talimatıyla devralındı) → `4d92d9b0` WIP commit.
- Main CI startup: son 3 main run — `39615975640` CI success, `39615975624` Docker Publish success, `39607353298` Docker Publish success.
- Dış varsayım: yok (değerler karar sayfasındaki 2026-10-21 ölçümlerine dayanır; yeni dış bağımlılık yok).
- Docker kullanılmadı (RAM sınırı, talimat); integration LocalDB `(localdb)\MSSQLLocalDB`.
- Yönetici notu (2026-10-22): teslimat 720 dk `.env.example`, deneme yayını overlay'i ve runbook örneğinde uygulandı; runbook §M satır 1 eklendi; karar tablosu satır 7.
