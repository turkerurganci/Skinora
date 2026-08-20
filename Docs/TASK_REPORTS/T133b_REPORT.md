# T133b — DEPLOY_RUNBOOK §G happy path anlatısının P2P'ye çekilmesi

**Faz:** F7 | **Durum:** ✓ Tamamlandı — doğrulama **✓ PASS** | **Tarih:** 2026-08-20

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
- **(Doğrulama turu, 2026-08-20 — proje sahibi onaylı)** sekiz bulgu aynı dalda kapatıldı: kontrol 8'in gözlemi var olmayan `isConfigured` alanından `value != null`'a çevrildi · §G.5'in "üçü de fail-closed" cümlesi "ikisi canlı, satıcı kapısı kalıcı bayrak (bayat `true` geçirebilir)" olarak düzeltildi · kısayolun öncül/ardıl sıralaması düzeltildi ve ön koşulu (`NoDeliveryReference` riski) yazıldı · kontrol 10'a **elle ödeme izleyicisi kurma** adımı eklendi (bağlanmamış caller — ayrı backlog satırı) · üç `02 §4.5.1` atfının ikisi `§16.2`'ye çevrildi · cron literalleri kayıtlı stringlere çekildi · 10a zincirine `PayoutCompletedConsumer` eklendi. İkinci `DEFERRED_BACKLOG` satırı: `T133b-PaymentMonitorUnarmed` (44 → **45**).

## Etkilenen Modüller / Dosyalar

| Dosya | Değişiklik |
|---|---|
| `Docs/DEPLOY_RUNBOOK.md` | §G bağlam notu · §G.4 kontrol 8 · §G.4 kontrol 10 → 10 + 10a · §G.4 altına prova notu bloğu · §G.5 trade-hold tuzağı |
| `Docs/11_IMPLEMENTATION_PLAN.md` | §P6 T133b — KAPSAM NETLEŞTİRMESİ (E1/E2/E3) + CANLI STACK NOTU |
| `Docs/DEFERRED_BACKLOG.md` | `T133b-LiveRehearsalUnrun` satırı + durum başlığı 43 → 44 · **(doğrulama)** `T133b-PaymentMonitorUnarmed` satırı (§4) + başlık 44 → 45 |
| `Docs/DEPLOY_RUNBOOK.md` **(doğrulama turu)** | §G.4 kontrol 8 gözlemi · kontrol 10 ödeme-izleyici adımı + yeni not bloğu · kontrol 10a cron/`PayoutCompletedConsumer`/§16.2 · kısayol paragrafı sıralama + ön koşul · §G.5 platform kapıları maddesi |

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
| Doğrulama durumu | **✓ PASS** (2026-08-20, bağımsız chat) |
| Bulgu sayısı | **8 — bloke edici 0.** Altısı (B1–B6) proje sahibi onayıyla **aynı dalda kapatıldı**, B7/B8 de birlikte alındı |
| Düzeltme gerekli mi | Hayır — düzeltmeler merge öncesi bu dalda uygulandı |

### Kapılar

- **Adım -1 (working tree):** ✓ `git status --short` boş.
- **Adım 0 (main CI):** ✓ son üç tamamlanmış main run'ı `success` — `32352581013`, `32352580901` (T133a #249), `32267172619` (T133 #248).
- **Adım 0b (repo memory):** ✓ `.claude/memory/MEMORY.md:58` T133b satırını taşıyor.
- **Adım 8a (dal CI):** ✓ dal HEAD `8341b02` run [`32356870769`](https://github.com/turkerurganci/Skinora/actions/runs/32356870769) `success`, **`CI Gate` yeşil**. Koşan iki job `Detect changed paths` + `1. Lint`; kalanlar `paths-filter` gereği skipped (docs-only tur). Raporun ilk yazımı bir önceki commit'in run'ını (`838483b` / `32356485612`) gösteriyordu — yetkili kanıt dal HEAD'ine ait olan yukarıdaki run'dır.

### Kabul kriterleri — bağımsız yeniden üretim

| # | Kriter | Sonuç | Validator kanıtı |
|---|---|---|---|
| 1 | Kontrol 10 v3.0 P2P akışını anlatıyor | ✓ | Kontrol 10 + 10a'daki **her** uç, status, kolon, job id, ayar anahtarı ve sınıf adı kodda **birebir** doğrulandı; sıralama `TransactionStateMachine`'in izin verdiği geçişlerle uyumlu. E1'in bölme gerekçesi bağımsız ölçüldü: `SystemSettingsValidator.cs:60` `MinimumSettlementDays = 7` + `:244-252` reddetme + seed varsayılanı `"8"` (`SystemSettingSeed.cs:177`) → kuyruk tek oturumda ayardan gözlenemez, bölme **zorunlu** |
| 2 | §G tablolarında custodial adım adı yok | ✓ | Emekli enum listesi `git show 82bff4d^:…/TransactionStatus.cs`'ten türetildi (`ITEM_ESCROWED`, `TRADE_OFFER_SENT_TO_BUYER`, `TRADE_OFFER_SENT_TO_SELLER`) + ~30 custody terimi; **tüm dosyada** grep → emekli adlarda **0 isabet**. Kalan isabetler: `trade offer` ×1 ve `custodial` ×1 emekliliğin **kendi ifadesi**, `escrow` ×2 Steam'in **kendi** escrow'u, `emanet` ×1 v3.0'da **duran** ödeme emaneti, `bot` ×8 (5 emekliliği belgeliyor, 3 `TELEGRAM_BOT_TOKEN`) |

**Yöntem:** altı iddia kümesi paralel doğrulandı, her küme ayrı bir **çürütme** turundan geçirildi (aynı iddialar ikinci kez, "yanlışla" talimatıyla), üstüne iki bağımsız eleştirmen — biri kümelerin kapsamadığı iddiaları ve **her çapraz referansı** (02 §2.2/§4.5.1/§9.1/§9.2/§16.2, 03 §4.4, 05 §3.2, §C.1, §H/§H.2/§H.3, §A) kaynak bölümü açarak, diğeri kriterlerin kendisini + plan değişikliğinin bütünlüğünü denetledi. En yüksek riskli iki iddia ayrıca elle teyit edildi.

### Bulgular

| # | Sev | Bulgu | Durum |
|---|---|---|---|
| B1 | S1 | **Kontrol 8 çalıştırılamazdı:** `GET /admin/settings` yanıtı `isConfigured` **taşımıyor** — `SettingItemDto` (`SystemSettingDtos.cs:7-14`) yedi alan taşır, `IsConfigured` 07 §9.8 gereği bilinçli olarak projekte edilmez (kodda tek `isConfigured` bir audit-log payload'u, `SystemSettingsService.cs:139-140`). Sayılar (63/44/19) doğruydu, **adı verilen gözlem** yoktu | ✅ kapatıldı — gözlem `value != null`'a çevrildi; 19'un kanıtı boot log satırı, 44'ünki `IsConfigured = 1` sorgusu olarak yazıldı |
| B2 | S1 | **"Üçü de fail-closed: Steam sorgulanamazsa `STEAM_UNAVAILABLE`"** yanlıştı — satıcı kapısı Steam'e **hiç çıkmaz**; `TransactionEligibilityService.cs:9-16` kendi yorumunda *"no live sidecar call"* diyor ve `MOBILE_AUTHENTICATOR_REQUIRED` döner. Madde iki cümle önce bunu **doğru** anlatıp sonra kendisiyle çelişiyordu | ✅ kapatıldı — "ikisi canlı + fail-closed, satıcı kapısı kalıcı bayrak" olarak ayrıldı; **bayat `true` riski** ve provada satıcı MA'sının elle teyidi yazıldı |
| B3 | S1 | **Kısayol paragrafında sıralama tersti:** "satır kuyruğa düştükten sonra `SettlementVerificationJob` … damgalar". Kuyruk `SettlementVerifiedAt != null`'ı **önkoşul** olarak arıyor (`SellerPayoutQueueJob.cs:124-136`), yani doğrulama kuyruğun **öncülü**. Paragraf hemen üstündeki 10a satırıyla çelişiyordu; gerçek garanti yazılandan **daha güçlü** | ✅ kapatıldı — öncül/ardıl ilişkisi düzeltildi, kuyruğun **iki iş turunda** (≤5 dk + ≤1 dk) göründüğü yazıldı |
| B4 | S1 | **Kontrol 10 gerçek stack'te ilerlemezdi:** sidecar'ın T71 ucu `POST /api/monitor/start`'ı çağıran **hiçbir backend kodu yok** (`IBlockchainSidecarClient` yalnız derive · post-cancel-start/stop · balances · cold-wallet taşıyor) ve `MonitorRegistry.start` yalnız o route'tan erişilebilir. Allocator DB'ye `MonitoringStatus.ACTIVE` yazıyor ama sidecar'ı **kurmuyor** → Nile transferi `payment-detected` üretmez, prova `SELLER_CONFIRMED`'da sessizce durur. **Devralınmış** (emekli satır da aynı adımı vaat ediyordu) ama turun "her adım kod üzerinden doğrulandı" iddiasının **ad** düzeyinde kaldığını gösteriyor | ✅ kapatıldı — elle kurma `curl`'ü §G.4'e yazıldı **ve** kalıcı açık `DEFERRED_BACKLOG` §4'e `T133b-PaymentMonitorUnarmed` olarak kaydedildi (sınıfı `T81-PriceConsumerWireup` ile aynı) |
| B5 | S2 | Kısayol **koşullu** yeterliydi: alıcı envanteri readiness'ta veya turda okunamıyorsa verdict `NoDeliveryReference` olur, satır kuyruğa girmek yerine eskale edilir (§I.5) — ve bunu söyleyen bir hata mesajı yok | ✅ kapatıldı — "Kısayolun ön koşulu" paragrafı eklendi |
| B6 | S1– | "02 §4.5.1" üç kez tabanın **dayanağı** olarak gösteriliyordu; §4.5.1 yalnız gerekçeyi (8 = 7 + 1 gün pay) verir, uygulanabilir kural 02 **§16.2** — runbook'un kendi §C satırı ve validator'ın kendi yorumu da §16.2'yi anıyor | ✅ kapatıldı — iki yerde §16.2'ye çevrildi, kısayol paragrafında kural (§16.2) ile gerekçe (§4.5.1) ayrıldı |
| B7 | trivial | Cron literalleri `*/5` / `*/1` yazılmıştı; kayıtlı stringler `*/5 * * * *` / `* * * * *` — bare hâli Hangfire/Cronos'ta parse edilmez ve kodda grep'lenmez | ✅ kapatıldı |
| B8 | trivial | 10a zinciri `PayoutCompletedConsumer`'ı atlıyordu — `Complete` geçişini yazan o (raporun kendi dayanak tablosu bunu **doğru** biliyordu) | ✅ kapatıldı |

**Devralınmış, T133b'nin sorumluluğu değil (iş üretmez):** §G.5'in "MA 7 günden yeniyse → 15 günlük escrow" tetikleyicisinin repo'da kaynağı yok (kapı `escrow_end_duration_seconds != 0`'a bakar, MA yaşına değil) — cümle emekli §G.5'ten **aynen** taşındı, bu tur onu üretmedi. `DEFERRED_BACKLOG`'un mutlak "aktif satır" sayacı bir birim sapmalı; **delta doğru** ve sapma T133a'dan devralınmış.

### Güvenlik kontrolü

- Secret sızıntısı: **Temiz** — eklenen tek SQL bir `UPDATE` şablonu; doğrulama turunun eklediği `curl` `$INTERNAL_KEY`'i **env değişkeni olarak** referanslar, literal taşımaz.
- Auth/authorization etkisi: **Temiz** — kod dokunulmadı. Doğrulama `steamTradeOfferUrl`'in yalnız satıcıya döndüğünü bağımsız teyit etti (`TransactionDetailService.cs:227-234`).
- Input validation: **Temiz.**
- Yeni bağımlılık: **Yok.**
- **B4'ün güvenlik okuması:** kayıt bir açığı **açmaz**, kapalı kalmış bir bacağı görünür kılar — izleyici kurulmadığı için ödeme hiç algılanmaz, yani hata yönü **fail-closed**'dır (para eksik hareket eder, fazla değil).

### Yapım raporu karşılaştırması

**Tam uyumlu.** Raporun 13 satırlık dayanak tablosundaki her atıf bağımsız olarak yeniden üretildi. İki not: (1) CI atfı bir commit eskiydi (yukarıda düzeltildi); (2) raporun *"kod kanıtı **ad** doğruluğu için güçlü, **davranış** doğruluğu için zayıf"* itirafı B4'ü **önceden doğru teşhis etmiş** — doğrulama o zayıflığın somut bir örneğini buldu ve sahibini yazdı.

**Turun kalıcı dersi doğrulandı ve bir adım ilerletildi.** Yapım turu "bu satır UYGULANABİLİR mi?" sorusunu sordu ve kriterin kendisindeki gerçekleştirilemez vaadi (E1) yakaladı. Doğrulama aynı soruyu **kendi yeniden yazımına** sordu ve iki yerde daha kaldı: kontrol 8 var olmayan bir alanı okutuyordu (B1), kontrol 10 bağlanmamış bir caller'a dayanıyordu (B4). **Bir prova dokümanında "doğru" ile "koşulabilir" ayrı iki denetimdir ve ikincisi ancak her adımın gerçekten bir çağıranı olup olmadığı sorulunca geçilir** — ad aramak yetmez, çağıran aramak gerekir.

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
