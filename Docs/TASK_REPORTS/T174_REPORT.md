# T174 — DEFERRED_BACKLOG §4 triyajı

**Faz:** F9 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-10-23

---

## Yapılan İşler
- 2026-10-17 sayaç düzeltmesinde (`2e70a03f`, PR #334) §4 "Düşük öncelik" tablosunda ⚪ yapılan 16 satır o commit'teki hâlinden çıkarıldı (`git show 2e70a03f:Docs/DEFERRED_BACKLOG.md`, tabloda `| ⚪` ile başlayan satırlar → 16) ve her biri bugünkü kodla (`origin/main` `69c68df6`) ölçüldü.
- Sonuç: **5 ✅ bayat** (kod zaten çözmüş), **10 ⚪ kalır** (metin güncellendi, kapanan alt kalemler ayrıldı), **1 ⚪ → 🟡** (`sanctions-expansion`, öneriyle kaydedildi — ⚠ GÜVENLİK/uyum).
- ✅ satırlar dosyanın deseniyle yazıldı: `~~ID~~` + "**ÇÖZÜLDÜ → <görev> (PR, tarih) — satır bayattı (T174 ölçümü, 2026-10-23).**" + kanıt. ⚪ satırlara "**T174 ölçümü (2026-10-23)**" notu eklendi; Kaynak hücresine `· T174` eklendi.
- Sayım yeniden hesaplandı, dosya başına durum satırı eklendi: **53 aktif / 162 çözülmüş → 48 aktif / 167 çözülmüş**.
- Satır eklenmedi ya da silinmedi; tablo yapısı değişmedi (🟡'ya çıkan satır yerinde, "Düşük öncelik" tablosunda kaldı — öncelik hücresi esastır).

## Etkilenen Modüller / Dosyalar
- `Docs/DEFERRED_BACKLOG.md` — 16 satır + 1 durum satırı
- `Docs/IMPLEMENTATION_STATUS.md` — "Son güncelleme" + T174 satırı (`⏳ Devam ediyor`)
- `Docs/STATUS_CHANGELOG.md` — eski "Son güncelleme" satırı taşındı
- `.claude/memory/MEMORY.md` — T174 satırı
- `Docs/TASK_REPORTS/T174_REPORT.md` — bu rapor
- Kod değişikliği yok.

## Triyaj tablosu (16 satır)

Ölçüm tabanı: `origin/main` `69c68df6`. Satır no'ları o commit'tir.

| # | Satır | 2026-10-17 durumu | Ölçüm kanıtı | Karar |
|---|---|---|---|---|
| 1 | `payout-completed-consumer` | ⚪ — `PayoutCompletedEvent`→COMPLETED geçişi yok | `Transactions/Application/Transfers/PayoutCompletedConsumer.cs` `INotificationHandler<PayoutCompletedEvent>`, `ITEM_DELIVERED`→`Complete`; yayımlayan `OutgoingTransferConfirmationJob.cs:214`; kayıt `TransactionsModule.cs:368`; test `PayoutCompletedConsumerTests`; dosya `07e2f5f9` (WP1) ile eklendi | ✅ WP1 (PR #169, 2026-06-14) |
| 2 | `payout-retry-consumer` | ⚪ — `RETRY_SCHEDULED` var, retry consumer yok | `PayoutIssueService.cs:235` `RETRY_SCHEDULED` + `RetryCount=1`; `SellerPayoutIssueReportedEvent`'in handler'ı yok (grep: yalnız yayımlayan + event tanımı + bir yorum); `RETRY_SCHEDULED`'ı okuyan iş yok. `BlockchainPayoutVerifier` `StillPending`'i yalnız payout satırı onaylı değilse döner; şikâyet yalnız `COMPLETED`'da açılır | ⚪ kalır — metin güncellendi (yol dar, para hareketi yok; açık kayıt satıcının yeniden bildirmesini engeller) |
| 3 | `calculator-caller-wiring` | ⚪ — `CalculateRefund`/`SellerPayout`/`IRefundDecisionService` tüketilmiyor; AD7 gas split=0 | `SellerPayoutQueueJob.cs:294` `ResolveSellerPayoutAsync`; `PaymentRefundToBuyerConsumer.cs:193`; `AmountValidationService.cs:183,272,328,397,466`; `RefundDecisionService.cs:78,92` → `FinancialCalculator`; `gas_fee_protection_ratio` `GasFeeSettingsProvider.cs:18`; AD7 `AdminTransactionQueryService.cs:508-518` `GasFeeFromCommission`/`GasFeeFromSeller` (`git log -S` → `07e2f5f9` WP1) | ✅ WP1 (PR #169) + WP2 (PR #170) |
| 4 | `blockchain-monitor-consumers` | ⚪ — kalan: mempool `PaymentDetected` + DROPPED metrik | `PublishPaymentDetectedAsync` üretimde 0 çağıran (yalnız arayüz + `SignalRTransactionRealtimePublisher.cs:47`); `DROPPED` backend + iki sidecar'da 0 eşleşme; WP16 raporu: "POST_MVP / BY_DESIGN", sahip itiraz etmedi | ⚪ kalır — metin güncellendi |
| 5 | `flagged-allocation-detail` | ⚪ — adres ✅ WP4b; tx-detail alt-DTO'ları null | `FraudFlagService.ApproveAsync` yorumu: "No deposit address here (T147)" — depozit kabulde seçilen ağda türetiliyor (`7cf71c27` T147); `TransactionDetailService.cs:346-352` `Payment`/`SellerPayout`/`Refund`/`Dispute` dolu; `:407-413` null'lar yalnız giriş yapmamış görünüm (07 §7.5) | ✅ WP4b + T147 (PR #344) + T123/WP1/WP2/WP6b |
| 6 | `energy-gas-token-config` | ⚪ — kısmen ✅ WP10; kalan by-design + çoklu sweeper | `sidecar-blockchain/src/config/index.ts:193` `TRANSFER_FEE_LIMIT_SUN`, `:205` `MIN_CONFIRMATIONS` 20, `:107` `tokenDecimals: 6` | ⚪ kalır — tek açık iş çoklu sweeper (ölçek, canlı sonrası) |
| 7 | `admin-alert-consumers` | ⚪ — 6 alarm türünün consumer'ı yok | WP8 (`3b3a3b6f`, PR #177): `RefundBlockedAdminNotificationConsumer`, `TransferDispatchFailedAdminNotificationConsumer`, `SellerPayoutIssueEscalatedNotificationConsumer` var. Yok: stranded-delegation (`EnergyDelegationService.ts:609` yalnız `logger.warn`), izleme `STOPPED`, spam token (`BlockchainWebhookHandler.cs:382` yalnız `LogWarning`) | ⚪ kalır — yarısı kapandı; hedef T165/T170 |
| 8 | `misc-monitoring-probes` | ⚪ — 6 alt kalem | Steam + iki ağ probu `PlatformHealthProbeJob` (WP16, PR #189; bileşenler `STEAM`, `BLOCKCHAIN_TRON`, `BLOCKCHAIN_ETHEREUM`); timeout yeniden kurma WP16 D1/D2; idempotency DB `ProcessedNonce` (T68); `ITEM_ESCROWED` `TransactionStatus`'ta yok (F7) → `ItemEscrowed` konusuz; Telegram probu yok; e-posta bounce sonrası platform içi bildirim yok (`ResendWebhookHandler`'da `Notification` yazımı yok, T78 K1) | ⚪ kalır — kalan: Telegram probu + bounce bildirimi (T165) |
| 9 | `signalr-scaling` | ⚪ — Redis backplane yok | `Program.cs:278` `AddSignalR()` yalın; `AddStackExchangeRedis` 0 eşleşme; `CountdownSyncBroadcaster` var; compose'da tek `skinora-backend`, `replicas` yok | ⚪ kalır — tek kopyada etkisiz |
| 10 | `maintenance-toggle` | ⚪ — tetik yok, endpoint yok | `AdminMaintenanceController.cs`; `AdminMaintenanceService.cs:163,268` `PublishMaintenanceStatusChangedAsync`; `AdminController.cs:378` `RefreshPublicStateAsync`; test `AdminMaintenanceEndpointTests` | ✅ WP7 (PR #176, 2026-06-17) |
| 11 | `uptime-cache-scaleout` | ⚪ — `IMemoryCache` + SET invalidation hook yok | `AdminMaintenanceService.cs:277` `_cache.Remove(MaintenanceCacheKey)`; genel ayar ucu `AdminController.cs:378` çağırıyor (WP7) → hook bayat. `IMemoryCache` iki serviste (`AdminMaintenanceService`, `PlatformPublicService`), dağıtık önbellek yok | ⚪ kalır — yalnız kopyalar arası geçersizleme |
| 12 | `sanctions-expansion` | ⚪ — MANUAL-only, feed yok, ağ boyutu T145'te, AD22 reason UI | `SanctionedAddress.Network` (T145) ✓; T169: OFAC SDN elle + aylık (runbook §O.1); `AdminSanctionsController` GET/POST/DELETE (`MANAGE_SANCTIONS`) var; **frontend'de yaptırım sayfası yok** — `frontend/src/app/[locale]/admin/` = audit-logs, dashboard, disputes, flags, roles, settings, transactions, users, wallets; `admin/sanctions` 0 eşleşme; `MANAGE_SANCTIONS` yalnız `lib/admin/permissionCatalog.ts:25`. Runbook §O.1 ve açılış listesi madde 13 "admin panelinden (yaptırım listesi ekranı)" diyor | **⚪ → 🟡 (öneriyle kaydedildi — ⚠ GÜVENLİK/uyum)** |
| 13 | `vpn-fraud` | ⚪ — `VpnDetection:Enabled` kapalı | `appsettings.json:141` `Enabled: false`; compose dosyaları, `.env.example`, runbook'ta `VpnDetection` 0 eşleşme; `IVpnProxyDetector` Fraud modülünde 0 eşleşme (yalnız Steam girişi) | ⚪ kalır — açılış kararı sahibin (T175 listesine not) |
| 14 | `misc-user-features` | ⚪ — kalan: multi-account user UI | Kaynak T93 K5. Kullanıcı yalnız eylemde `ACCOUNT_FLAGGED` görüyor (`tr.json:1423,1598`); `/users/me` DTO'sunda flag yok; 04'te bu ekranı isteyen satır yok (yalnız S03d askıya alınmış hesap) | ⚪ kalır — spec'siz iyileştirme |
| 15 | `T61-AdminHubJoinBypass` | ⚪ — admin bypass yok | `TransactionsHub.cs:82-88` `HubClaims.IsAdmin` ile admin her odaya katılıyor ("WP9 (T61 K3)" yorumu); test `TransactionsHubEndpointTests` | ✅ WP9 (PR #179, 2026-06-18) |
| 16 | `discord-interactions-userinstall` | ⚪ — interactions + user-install yok | backend'de `Ed25519` / `interactions` 0 eşleşme; Discord yalnız Bot DM + OAuth2 | ⚪ kalır — canlıyı etkilemez |

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 2026-10-17'de ⚪ sayılan 16 satır bugünkü kodla ölçüldü | ✓ | Yukarıdaki tablo; satır listesi `git show 2e70a03f:Docs/DEFERRED_BACKLOG.md \| awk '/^### Düşük öncelik/,/^## 5\./' \| grep -c '^\| ⚪'` → 16 |
| 2 | Bayat olanlar kapandı | ✓ | 5 satır ✅ (1, 3, 5, 10, 15); her biri görev + PR + kod konumuyla |
| 3 | Bloke etmez | ✓ | Kod değişikliği yok; yalnız belge |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Sayaç (aktif) | 53 → **48** | `awk 'BEGIN{s=0}/^## /{s=($0 ~ /Öne Çıkanlar/)?0:1}/^\| (🔴\|🟡\|⚪) /{if(s)n++}END{print n+0}' Docs/DEFERRED_BACKLOG.md` |
| Sayaç (çözülmüş) | 162 → **167** | aynı komut `✅` ile |
| Tablo biçimi | ✓ | değişen 16 satırın her birinde 5 `\|` (4 hücre) |
| Unit / Integration | — | kod değişikliği yok |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (yönetici düzeni — ayrı doğrulama alt ajanı) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: Yok
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T174-backlog-triyaji`
- Commit: ilk commit `T174: DEFERRED_BACKLOG §4 triyajı — 16 satır ölçüldü`
- PR: PR_NO
- CI: CI_SONUC

## Known Limitations / Follow-up
- **⚠ Canlıyı etkileyen kalem — `sanctions-expansion` (🟡):** yaptırım listesi için yönetici ekranı yok; runbook §O.1 ve açılış listesi madde 13 o ekranı varsayıyor. T171'den önce iki yoldan biri seçilmeli: (a) en küçük ekran (liste + ekle + kaldır; ağ ve gerekçe alanı), (b) runbook'u API yoluna (`POST api/v1/admin/sanctions/addresses`, `MANAGE_SANCTIONS` JWT ile) çevirmek. Karar sahibin.
- 🟡'ya çıkan satır "Düşük öncelik" tablosunda yerinde bırakıldı (taşımak yapısal değişiklik olurdu); "Öne Çıkanlar" bölümüne eklenmedi (kapsam yalnız 16 satır + durum satırı).
- `vpn-fraud`: canlıda Tor tespitinin açılıp açılmayacağı bir sahip kararı; T175 kapı listesinde sorulması önerilir.

## Ek gözlemler (dokunulmadı)
- **Runbook drift'i:** `Docs/DEPLOY_RUNBOOK.md` §O.1 (satır 1190) ve kontrol listesi madde 13 (satır 1186) olmayan bir "yaptırım listesi ekranı"ndan söz ediyor — `sanctions-expansion` kararıyla birlikte düzeltilmeli.
- **Kopya kalem:** DROPPED metrik hem `blockchain-monitor-consumers` hem `misc-monitoring-probes` satırında geçiyordu; ikincisine "→ `blockchain-monitor-consumers` satırında" notu düşüldü, satır birleştirilmedi.
- **Bayat yorum:** `PaymentReceivedRealtimeConsumer.cs` (Realtime) `PaymentDetected`'ı "blockchain monitor görevine" bırakıyor; o görev yok, karar WP16'da canlı sonrasına alındı.
- **Bayat yorum:** `PayoutIssueService.cs` / `IPayoutVerifier.cs` `RETRY_SCHEDULED` için "future retry pipeline (T-future)" / "follow-up job (forward devir)" diyor; böyle bir görev planda yok — satır 2 (`payout-retry-consumer`) bunun takibi.
- **Öne Çıkanlar bölümü güncel değil:** başka bölümlerdeki bazı güncel 🟡 satırlar (ör. `SharedSecretsInPlainEnv`, `EthereumHotWalletUsdtLimitUnmonitored`) orada yok; bölüm sayaçtan hariç olduğu için rakamı etkilemiyor.

## Notlar
- **Working tree (Adım -1):** temiz (`git status -sb` → `## task/T174-backlog-triyaji...origin/main`, değişiklik yok).
- **Main CI (Adım 0):** son 3 tamamlanmış run `success` — `39854740205` (Docker Publish, T168), `39850436226` (CI, T169), `39850436112` (Docker Publish, T169). `39854740137` (CI, T168) `in_progress` idi, sayılmadı.
- **Dış varsayım:** yok (belge triyajı; dış servis, paket, plan katmanı varsayımı yok).
- **Ölçüm yöntemi:** her satır için grep / dosya okuma / `git log -S` ile kodun bugünkü hâli; tarihsel kapanışlar `IMPLEMENTATION_STATUS.md` WP satırları ve WP raporlarıyla eşlendi. Canlı prova veya test koşusu yapılmadı (kod değişmedi). Docker kullanılmadı.
- **Kararlar:** `sanctions-expansion` ⚪ → 🟡 öneriyle kaydedildi — ⚠ (yönetici düzeni: kolay karar, faz sonu listesine).
