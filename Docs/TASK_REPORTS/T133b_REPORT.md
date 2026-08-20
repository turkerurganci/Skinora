# T133b — DEPLOY_RUNBOOK §G happy path anlatısının P2P'ye çekilmesi

**Faz:** F7 | **Durum:** ⏳ Devam ediyor (doğrulama bekliyor) | **Tarih:** 2026-08-20

---

## Yapılan İşler

- **§G.4 kontrol 10 yeniden yazıldı ve ikiye bölündü (10 + 10a).** Emekli custodial zincir (`işlem → kabul → trade offer → ITEM_ESCROWED → …`) v3.0 P2P akışıyla değiştirildi. Bölünmenin sebebi doküman tercihi değil, ölçülen bir kısıt — aşağıdaki E1.
  - **Kontrol 10** = tek oturumda gözlenebilen zincir: işlem oluşturma → `accept` → `ACCEPTED` → `confirm-ready` → `SELLER_CONFIRMED` (deposit adresi burada açılır) → Nile USDT → `payment-detected` → 20 blok → `payment-confirmed` → `PAYMENT_RECEIVED` (+ `DeliveryDeadline`) → **satıcı doğrudan alıcıya gönderir** (`steamTradeOfferUrl` = alıcının kendi trade URL'i, yalnız satıcıya döner) → `confirm-receipt` → `ITEM_DELIVERED` + `PayoutEligibleAt` damgalandı.
  - **Kontrol 10a** = mutabakat kuyruğu: `settlement-verification` (cron `*/5`) → `SettlementVerifiedAt` → `seller-payout-queue` (cron `*/1`) → dispatch + on-chain onay → `COMPLETED`.
- **Prova kısayolu yazıldı** (kontrol 10a'nın altındaki not bloğu): `UPDATE Transactions SET PayoutEligibleAt = SYSUTCDATETIME()`. Guard zayıflatmaz — `SettlementVerificationJob` envanteri gerçekten yeniden okur, sahte olan tek şey saattir. Üretimde yasak olduğu satırda yazılı.
- **Kontrol 10'un neden alıcı onayı yolundan geçtiği yazıldı:** 02 §9.2'nin envanter kanıtı yolu launch'ta `delivery.inventory_evidence_auto_release_enabled` ile kapalı (§H), dolayısıyla provanın kendiliğinden ilerleyen tek yolu alıcı onayıdır.
- **N1 — §G.4 kontrol 8 düzeltildi:** "59 satır; 19'u configured" → "63 satır; boot sonrası hepsi configured (44 seed + 19 env)".
- **N2 — §G.5 trade-hold tuzağı yeniden yazıldı:** üç platform kapısı adıyla ayrıldı ve P2P'de riskin ağır tarafının **satıcı** olduğu (trade'i satıcı gönderir) yazıldı; prova sonucu §H.2 verdict tablosuna bağlandı.
- **§G bağlam bloğuna "T133b notu"** eklendi (§G'nin kendi konvansiyonu — T133 notunun yanına).
- **Plan §P6 T133b'ye KAPSAM NETLEŞTİRMESİ bloğu** yazıldı (E1/E2/E3 + canlı stack notu).
- **`DEFERRED_BACKLOG` satırı açıldı:** `T133b-LiveRehearsalUnrun` (43 → **44** aktif satır).

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Docs/DEPLOY_RUNBOOK.md` | §G bağlam notu · §G.4 kontrol 8 · §G.4 kontrol 10 → 10 + 10a · §G.4 altına prova notu bloğu · §G.5 trade-hold tuzağı |
| `Docs/11_IMPLEMENTATION_PLAN.md` | §P6 T133b — KAPSAM NETLEŞTİRMESİ (E1/E2/E3) + CANLI STACK NOTU |
| `Docs/DEFERRED_BACKLOG.md` | `T133b-LiveRehearsalUnrun` satırı + durum başlığı 43 → 44 |

**Kod değişikliği yok** — tur tümüyle dokümandır.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | §G.4 kontrol 10'un uçtan uca prova adımı v3.0 P2P akışını anlatıyor: emekli `ITEM_ESCROWED` ve "trade offer" adımları yerine satıcı onayı → alıcı ödemesi → P2P trade → teslimat doğrulaması (02 §9.2) → `COMPLETED` + payout zinciri | ✓ | Kontrol 10 + 10a birlikte zincirin tamamını anlatır (E1: kuyruk tek oturumda gözlenemediği için ikiye ayrıldı, proje sahibi onaylı). Zincirin her adı koddan doğrulandı — aşağıdaki dayanak tablosu |
| 2 | §G tablolarında kalan custodial adım adı yok | ✓ | `grep -n -i "ITEM_ESCROWED\|TRADE_OFFER_SENT" Docs/DEPLOY_RUNBOOK.md` → **0 isabet**. §G (satır 159–275) custodial taraması: kalan `bot`/`escrow` isabetlerinin hepsi ya T133 emekliliğini **belgeleyen** satırlar (§G bağlam, §G.0 `STEAM_API_KEY`, kontrol 4-5), ya Steam'in **kendi** escrow'u (§G.5), ya da Telegram bot'u (§G.6) |

### Anlatının kod dayanağı (kriter 1'in adım adım kanıtı)

| Anlatı adımı | Kod dayanağı |
|---|---|
| `POST /transactions/:id/accept` | `TransactionsController.cs:266` |
| `POST /transactions/:id/confirm-ready` → `SELLER_CONFIRMED` | `TransactionsController.cs:329` · `TransactionReadinessService` (baseline + alıcı MA canlı probe) |
| deposit adresi `SELLER_CONFIRMED`'da açılır | `TransactionDetailService.cs` — `Payment: payment, // T123 — SELLER_CONFIRMED+ deposit address (07 §7.5)` |
| `payment-detected` / `payment-confirmed`, 20 blok | `BlockchainWebhooksController.cs:33`, `:43`; 20-blok eşiği `IBlockchainTransferClient.cs:27` |
| `PAYMENT_RECEIVED` + `DeliveryDeadline` | `AmountValidationService.cs:482` (`Fire(ConfirmPayment)`) + `:515` (`ArmDeliveryDeadlineAsync`) |
| `steamTradeOfferUrl` = alıcının trade URL'i, yalnız satıcıya | `TransactionDetailService.cs:229-234` — `Status == PAYMENT_RECEIVED && role == "seller" ? transaction.BuyerTradeUrl : null` |
| `POST /transactions/:id/confirm-receipt` → `ITEM_DELIVERED` | `TransactionsController.cs:386` · `DeliveryConfirmationService` |
| `PayoutEligibleAt` = teslimat + `payout_settlement_days` | `SettlementWindowStamper.cs:35-42` |
| `settlement-verification`, cron `*/5` | `SettlementVerificationJob.cs:51,59` · kayıt `OutgoingTransferJobsRegistrar.cs:66` |
| `seller-payout-queue`, cron `*/1` | `SellerPayoutQueueJob.cs:84,87` · kayıt `OutgoingTransferJobsRegistrar.cs:47` |
| dispatch → on-chain onay → `COMPLETED` | `OutgoingTransferConfirmationJob.cs:110` → `PayoutCompletedConsumer.cs:99` (`Fire(Complete)`) |
| mutabakat tabanı 7 gün, admin altına inemez | `SystemSettingsValidator.cs:60` (`MinimumSettlementDays = 7`) + `:244-253` (reddetme) |
| ayar sayısı 63 (N1) | `SystemSettingsCatalog.cs` 63 giriş · `SeedDataTests.cs:83-84` `Assert.Equal(63, …)` · runbook giriş paragrafı zaten "63 satır" diyordu |
| üç trade-hold kapısı (N2) | satıcı: `TransactionEligibilityService.cs:82` (`User.MobileAuthenticatorVerified`) · alıcı: `TransactionAcceptanceService.cs:209-232` · alıcı: `TransactionReadinessService.cs:172-184` |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build / Unit / Integration | — | **Koşulmadı, gerekli değil:** `git diff --stat` yalnız üç `.md` dosyası listeliyor, kod dokunulmadı |
| Doküman doğrulaması (kriter 2) | ✓ | `grep -n -i "ITEM_ESCROWED\|TRADE_OFFER_SENT" Docs/DEPLOY_RUNBOOK.md` → 0 isabet |
| Anlatı ↔ kod parity | ✓ | Yukarıdaki 13 satırlık dayanak tablosu; her adım dosya + satır ile |

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ⏳ Bağımsız doğrulama bekliyor |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

## Altyapı Değişiklikleri

- Migration: **Yok**
- Config/env değişikliği: **Yok** — tur hiçbir ayarı değiştirmiyor, yalnız `payout_settlement_days`'in mevcut tabanını belgeliyor
- Docker değişikliği: **Yok**

## Commit & PR

- Branch: `task/T133b-deploy-runbook-p2p-happy-path`
- Commit: `ee7e8e8` — T133b: DEPLOY_RUNBOOK §G happy path anlatısının P2P'ye çekilmesi (+ bu finalize commit'i)
- PR: [#250](https://github.com/turkerurganci/Skinora/pull/250)
- CI: **CI ✓ PASS** — dal HEAD `838483b` run [`32356485612`](https://github.com/turkerurganci/Skinora/actions/runs/32356485612) `success`, **`CI Gate` yeşil**. Bloke edici olmayan joblar `paths-filter` gereği **skipped** (`0. Guard` · vitest · advisory E2E · Build/Unit/Integration/Contract/Migration/Docker) — tur yalnız `.md` dosyalarına dokunduğu için beklenen davranış; koşan iki job `Detect changed paths` + `1. Lint`. Önceki run `32356418240` (`ee7e8e8`) finalize push'uyla concurrency'den **cancelled** — task.md concurrency notu gereği failure sayılmaz. (Bu satırı ekleyen docs-only commit kendi run'ını üretir; yetkili kanıt yukarıdaki `838483b` run'ıdır — anlatı içeriğinin tamamı o commit'te zaten mevcuttu.)

## Known Limitations / Follow-up

- **Canlı prova koşulmadı.** T133b'nin "Neden ayrı task" gerekçesi prova adımlarının canlı stack üzerinde doğrulanmasını istiyordu; bu tur bunu **yapmadı** — gerçek Steam hesabı çifti, `STEAM_API_KEY` ve fonlu Nile testnet cüzdanı gerektiriyor, hiçbiri bu ortamda yok. Anlatı bunun yerine **kod** üzerinden doğrulandı. Kod kanıtı **ad** doğruluğu için daha güçlü, **davranış** doğruluğu için daha zayıftır: sıralama, bekleme süreleri ve Steam'in gerçek hold davranışı ölçülmedi. Kayıt: `DEFERRED_BACKLOG` → `T133b-LiveRehearsalUnrun`.
- **Kontrol 10a'nın prova kısayolu da hiç çalıştırılmadı** — `PayoutEligibleAt` saat kaydırmasının `SettlementVerificationJob`'ı gerçekten tetiklediği kodun sorgu koşullarından okundu (`SettlementVerificationJob.cs:124-138`), canlı gözlenmedi.
- **Steam'in escrow davranışı bilinçli olarak iddia edilmedi.** §G.5'in yeni hold maddesi "item satıcının envanterinde durursa iptal, düşmüş ama alıcıda belirmemişse dispute" diye **iki** sonucu da yazar ve hangisinin olacağını §H.2 verdict tablosuna bağlar. Trade escrow'unun item'ı gönderenin envanterinden düşürüp düşürmediği bu ortamda ölçülemedi; doğrulanamayan bir Steam mekaniği doküman iddiası hâline getirilmedi.

## Notlar

- **Working tree:** temiz (Adım -1 ✓ — `git status --short` boş).
- **Main CI startup check (Adım 0) ✓:** son üç tamamlanmış main run'ı `success` — `32352581013` + `32352580901` (T133a, #249, 2026-08-20) ve `32267172619` (T133, #248, 2026-08-19). **Not:** `gh run list --branch main --limit 5` bayat sonuç döndürdü (en yenisi 2026-08-14); yetkili kanıt filtresiz `gh run list --limit 8` çıktısıdır.
- **Dış varsayımlar (Adım 4):** doküman turu; paket sürümü / plan tier / API limit varsayımı **yok**. Turun dayandığı tek dış olgu Steam'in trade escrow davranışıydı ve o bilinçli olarak iddia edilmedi (yukarıdaki Known Limitations).
- **Mini güvenlik kontrolü:** secret sızıntısı yok (eklenen tek SQL bir `UPDATE` şablonu, kimlik bilgisi taşımıyor) · auth/authorization etkisi yok · input validation etkisi yok · yeni dış bağımlılık yok. Eklenen prova kısayolu **üretimde yasak** olduğu satırda açıkça yazılı ve hiçbir guard'ı zayıflatmıyor — `SettlementVerificationJob`'ın envanter okuması ve verdict'i olduğu gibi kalır.
- **KAPSAM NETLEŞTİRMESİ (proje sahibi onaylı, 2026-08-20)** plan §P6 T133b'ye yazıldı — T137'nin kalıcı dersi gereği (onaylanmış kapsam kaynak dokümana yazılmadıkça gerçekleşmemiştir).
- **Turun en değerli bulgusu:** kabul kriterinin kendisi gerçekleştirilemez bir vaat taşıyordu. Eski kontrol 10 yalnız *custodial* değildi; kuyruğu tek prova adımı olarak vaat ediyordu ve `payout_settlement_days`'in 7 günlük sert tabanı yüzünden o adım **hiçbir zaman** gözlenemezdi. Kriteri harfi harfine uygulayan bir yeniden yazım hatayı sınıf değiştirerek (custodial → gerçekleştirilemez) hayatta bırakırdı. **Kalıcı ders: bir anlatıyı yeniden yazarken "bu satır doğru mu" kadar "bu satır uygulanabilir mi" de sorulmalı** — özellikle prova/runbook dokümanlarında, çünkü orada satırın müşterisi onu adım adım çalıştıracak bir insandır.
