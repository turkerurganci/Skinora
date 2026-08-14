# T124 — ConfirmPayment yeniden bağlanması + DeliveryDeadline

**Faz:** F7 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-08-14 (doğrulama 2026-08-14)

---

## Yapılan İşler

**Satıcının teslimat penceresi ilk kez armlanıyor (AC2).** `DeliveryDeadline` kolonunun her tarafta okuyucusu vardı — `DeadlineScannerJob`, `TimeoutFreezeService` (freeze/resume), `RestartRecoveryService` (kesinti sonrası uzatma), `CountdownSyncBroadcaster`, detay/liste geri sayım blokları, `TransactionStateMachine.ApplyEmergencyHold` — ama **yazan tek satır kod yoktu**. Sonuç, non-delivery riskinin tamamını taşıyan fazın hiçbir zaman sınırının olmamasıydı. T123'ün `SellerConfirmDeadline`'da kapattığı boşluğun teslimat ayağıdır.

- `ITimeoutSchedulingService.ArmDeliveryDeadlineAsync` (yeni) + `TimeoutSchedulingService` implementasyonu: `delivery_timeout_minutes` SystemSetting'ini okur, `DeliveryDeadline = now + dk` yazar, **`SaveChanges` çağırmaz** (09 §13.3 — unit of work çağıranın).
- **Hangfire job açmaz.** 05 §4.4 "Aşama ayrımı" yalnız ödeme fazına per-transaction delayed job veriyor; teslimat fazı scanner-driven. Job da açmak faza iki bağımsız executor verirdi.
- Çağıran: `AmountValidationService.AdvanceStateMachineAsync` — `ConfirmPayment` ateşlendikten ve ödeme timeout job'ları iptal edildikten sonra. Geçişle **aynı `SaveChanges`** içinde: rollback olan bir `PAYMENT_RECEIVED` arkasında teslimat deadline'ı bırakamaz.
- Kapsam doğal olarak doğru: tam tutar ve **fazla ödeme** bacakları bu yardımcıdan geçtiği için ikisi de armlar; eksik ödeme, multi-payment ve emergency-hold bacakları state'i ilerletmediği için armlamaz.

**Teslimat timeout'u T127'ye kadar tüketilmiyor (AC3).** 05 §4.4 ve 03 §4.4 iptalden **önce** bir teslimat doğrulama turu şart koşuyor; o tur T127'de. Kapı olmadan aradaki pencerede item'ı gerçekten göndermiş ama alıcısı onay vermemiş satıcının işlemi haksız yere iptal edilir ve para alıcıya iade edilirdi.

- `DeadlineScannerJob`'ın tüketen sorgusu üç faza indirildi (CREATED / ACCEPTED / SELLER_CONFIRMED).
- Süresi dolmuş `PAYMENT_RECEIVED` satırları `ReportGatedDeliveryTimeoutsAsync` ile **ayrı, salt-okunur** bir sorguda sayılıp uyarı logu üretir. Hiçbir şey yazılmaz → satır aynen kalır, T127 ilk taramasında devralır.
- **Ayrı sorgu bilinçli** (proje sahibi kararı, seçenek a): döngü içinde atlamak kalıcı gated satırların `DeadlineScannerBatchSize`'ı doldurmasına ve diğer üç fazın timeout'unun sessizce hiç işlenmemesine yol açabilirdi. Bu, `Scanner_Still_Consumes_Other_Phases_When_Gated_Delivery_Rows_Fill_The_Batch` testiyle sabitlendi.

**AC1 zaten karşılanmıştı — kanıtlandı, yeniden yazılmadı.** `AmountValidationService`'in `SELLER_CONFIRMED → PAYMENT_RECEIVED` bacağı T117'de (`82bff4d`) bağlanmış; o commit yalnız `ITEM_ESCROWED → SELLER_CONFIRMED` rename'ini yaptı. Uçtan uca kanıt HTTP webhook seviyesinde mevcuttu (`PaymentConfirmed_ExactAmount_AdvancesStateAndPublishesPaymentReceivedEvent`) ve bu görevde `DeliveryDeadline` iddiasıyla genişletildi. Task başlığındaki "yeniden bağlanma" T117 öncesi durumu anlatıyor; plana not olarak yazıldı.

## Etkilenen Modüller / Dosyalar

**Değişen — üretim**
- `Skinora.Transactions/Application/Timeouts/ITimeoutSchedulingService.cs` — `ArmDeliveryDeadlineAsync` sözleşmesi
- `Skinora.Transactions/Application/Timeouts/TimeoutSchedulingService.cs` — implementasyon + `DeliveryTimeoutKey` + `DefaultDeliveryTimeoutMinutes` + ayar okuyucu
- `Skinora.Transactions/Application/Timeouts/DeadlineScannerJob.cs` — tüketen sorgu üç faza indi, `ReportGatedDeliveryTimeoutsAsync` eklendi
- `Skinora.Transactions/Application/Timeouts/IDeadlineScannerJob.cs` — kapı notu (XML doc)
- `Skinora.Transactions/Application/Webhooks/AmountValidationService.cs` — armlama çağrısı + log alanı

**Değişen — test**
- `Skinora.Transactions.Tests/Integration/Timeouts/TimeoutSchedulingServiceTests.cs` — 4 yeni test (7 vaka)
- `Skinora.Transactions.Tests/Integration/Timeouts/DeadlineScannerJobTests.cs` — 1 test ters çevrildi, 1 yeni test
- `Skinora.Transactions.Tests/Integration/Timeouts/DeadlineScannerJobSideEffectsTests.cs` — 1 test ters çevrildi
- `Skinora.Transactions.Tests/Integration/Timeouts/TimeoutTestSupport.cs` — `ConfigureSettingAsync`'e opsiyonel `dataType`
- `Skinora.Transactions.Tests/Unit/Webhooks/AmountValidationServiceTests.cs` — stub + 5 iddia
- `Skinora.Transactions.Tests/Integration/Lifecycle/TransactionCancellationServiceTests.cs` — stub (iptal yolu armlamamalı → `NotSupportedException`)
- `Skinora.API.Tests/Integration/BlockchainWebhookEndpointTests.cs` — `ConfigureSettingAsync` yardımcısı + uçtan uca deadline iddiası

**Değişen — doküman**
- `Docs/11_IMPLEMENTATION_PLAN.md` v0.7→**v0.8** — T124'e üç karar + ara dönem etkisi + test beklentisi; **T127'ye kapı kaldırma kabul kriteri**
- `Docs/DEPLOY_RUNBOOK.md` — §A #6 tüketici bağlandı, düşük değerin T127 sonrası doğrudan para hareketi ürettiği uyarısı
- `Docs/DEFERRED_BACKLOG.md` — `P2P-DeliveryTimeoutWarning` ön koşulu karşılandı olarak işaretlendi (kalem açık)

**Migration:** Yok. Yeni kolon veya CHECK yok; `DeliveryDeadline` T117'de eklenmişti.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `AmountValidationService` SELLER_CONFIRMED → PAYMENT_RECEIVED | ✓ | T117'de (`82bff4d`) bağlanmıştı; bu görevde kanıtlandı ve korundu. HTTP: `PaymentConfirmed_ExactAmount_AdvancesStateAndPublishesPaymentReceivedEvent` (webhook → `PAYMENT_RECEIVED` + `PaymentReceivedEvent`), `PaymentConfirmed_Overpayment_AdvancesStateAndQueuesExcessRefund`. Negatif taraf: `ConfirmedPayment_Underpayment_*` ve `ConfirmedPayment_MultiPayment_*` ilerletmiyor, `ConfirmedPayment_OnEmergencyHold_DoesNotAdvanceOrRefund` |
| 2 | `DeliveryDeadline` armlanıyor ve zamanında ateşleniyor; süreyi `delivery_timeout_minutes` besliyor | ✓ | Armlama: `ArmDeliveryDeadline_Writes_Deadline_From_Configured_Setting` (90 dk ayar → `now+90`), uçtan uca `PaymentConfirmed_ExactAmount_*` (ayar 45 → gerçek DI grafiğinde `now+45`, sabit değil). Çağrı yeri: `ConfirmedPayment_ExactAmount_*` + `ConfirmedPayment_Overpayment_*` armlıyor; eksik/multi/hold armlamıyor. Hangfire job açılmıyor: `ArmDeliveryDeadline_Schedules_No_Hangfire_Job` (05 §4.4). Yanlış state reddediliyor: `ArmDeliveryDeadline_Rejects_Non_PaymentReceived_State`. "Zamanında ateşleniyor" = scanner deadline geçince satırı **görüyor** (aşağıdaki kapı testleri gated sayımı üzerinden bunu gösteriyor); iptal bacağı AC3 gereği T127'ye kadar kapalı |
| 3 | `DeadlineScannerJob`'ın PAYMENT_RECEIVED dalı T127'ye kadar tüketmiyor; işlem taranabilir kalır | ✓ | `Scanner_Does_Not_Consume_Overdue_PAYMENT_RECEIVED_Until_T127` — iki ardışık taramadan sonra da `PAYMENT_RECEIVED`, `CancelledAt`/`CancelledBy` NULL, `DeliveryDeadline` değişmemiş (satır aynen T127'ye devrediliyor). Yan etki yok: `Delivery_Timeout_Publishes_Nothing_While_Gated_Until_T127` (outbox boş → iade eventi yok). Açlık koruması: `Scanner_Still_Consumes_Other_Phases_When_Gated_Delivery_Rows_Fill_The_Batch` (batch=1, 3 gated satır → CREATED yine iptal oluyor). Kapının kaldırılması T127 kabul kriteri olarak plana yazıldı |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 error / 0 warning | `dotnet build` (backend solution) |
| Format | ✓ temiz | `dotnet format --verify-no-changes -v diag` → *"Formatted 0 of 1074 files"* |
| Unit + Integration | ✓ **2561/2561** | 13 test projesi **sırayla** (`dotnet test --no-build`, proje başına). Paralel `Skinora.sln` koşumu tek SQL Server'a 13 assembly'yi aynı anda yüklediği için artefakt üretir (T123 ölçüm notu) |

Proje dağılımı: Shared 399 · Steam 39 · Platform 189 · Auth 120 · Admin 22 · Users 22 · Payments 6 · Realtime 40 · Fraud 91 · Disputes 60 · Notifications 171 · **Transactions 870** · **API 532**.

Doğrudan T124 kapsamı — **5 yeni test metodu = 8 yeni çalıştırma** (biri 4 vakalık `[Theory]`); aritmetik kapanıyor: T123'ün 2553'ü + 8 = **2561**.
- `TimeoutSchedulingServiceTests` — **4 yeni metod / 7 çalıştırma**: `ArmDeliveryDeadline_Writes_Deadline_From_Configured_Setting`, `_Schedules_No_Hangfire_Job`, `_Falls_Back_When_Setting_Unusable` (`[Theory]` — unconfigured / `0` / `-15` / `not-a-number`), `_Rejects_Non_PaymentReceived_State`
- `DeadlineScannerJobTests` — **1 yeni metod**: `Scanner_Still_Consumes_Other_Phases_When_Gated_Delivery_Rows_Fill_The_Batch`; ayrıca `Scanner_Fires_Timeout_On_Overdue_PAYMENT_RECEIVED` → `Scanner_Does_Not_Consume_Overdue_PAYMENT_RECEIVED_Until_T127` **ters çevrildi** (sayı değişmedi)
- `DeadlineScannerJobSideEffectsTests` — `Delivery_Timeout_Publishes_Notification_And_PaymentRefund` → `Delivery_Timeout_Publishes_Nothing_While_Gated_Until_T127` **ters çevrildi** (sayı değişmedi)
- `AmountValidationServiceTests` — mevcut 4 teste **5 yeni iddia** (armlıyor: exact + overpayment · armlamıyor: underpayment, multi-payment, emergency hold)
- `BlockchainWebhookEndpointTests` — mevcut teste **2 yeni iddia** (deadline NOT NULL + ayardan gelen 45 dk penceresi içinde)

Ölçüm bütünlüğü notu: ilk tam koşum (2561/2561) `DeadlineScannerJob`'a eklenen son savunma `try/catch`'inden **önceki** derleme üzerindeydi; rakam yukarıda **final kod yeniden derlenip suite tekrar koşularak** doğrulandı.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız doğrulama, ayrı chat, 2026-08-14, **yapım raporu görülmeden** |
| Bulgu sayısı | **2** — ikisi de **bloke edici değil**, ikisi de **T124 kaynaklı regresyon değil** |
| Düzeltme gerekli mi | Hayır — merge bloklanmadı. **Her iki bulgu da merge sonrası ayrı chore PR'ında kapatıldı** (proje sahibi onayı, 2026-08-14): T127 başlığı düzeltildi + faz kayması T127'ye kabul kriteri olarak eklendi (11 v0.8→v0.9) |

### Validator kapıları

| Kapı | Sonuç |
|---|---|
| Adım -1 — working tree | ✓ Temiz (`git status --short` boş) |
| Adım 0 — main son 3 CI run | ✓ `31802083727` · `31802083622` · `31733188607` — üçü de `completed`/`success` |
| Adım 0b — repo memory drift | ✓ `.claude/memory/MEMORY.md`'de T124 satırı mevcut |
| Adım 8a — task branch CI | ✓ HEAD `6c1beeb` run [`31813382780`](https://github.com/turkerurganci/Skinora/actions/runs/31813382780) — **CI Gate `success`**, 11 bloke edici job yeşil. (Raporun üstünde yazan `31809828883` bir önceki commit `44f42b4` içindi; rapor commit'i de ayrıca yeşil koştu) |

### Kabul kriterleri — bağımsız kanıt

Validator kanıtları yapım raporundan bağımsız üretildi; testler lokalde yeniden koşuldu.

| # | Kriter | Sonuç | Validator kanıtı |
|---|---|---|---|
| 1 | `AmountValidationService` SELLER_CONFIRMED → PAYMENT_RECEIVED | ✓ | Kontrol akışı kodun kendisinden izlendi: `AmountValidationService.cs:87` `Status != SELLER_CONFIRMED` → multi-payment dalına sapıyor, dolayısıyla `:103` (tam tutar) ve `:350` (fazla ödeme) **yalnız** SELLER_CONFIRMED'dan çağrılıyor; `AdvanceStateMachineAsync:482` `machine.Fire(ConfirmPayment)`. Uçtan uca lokal koşum: `PaymentConfirmed_ExactAmount_AdvancesStateAndPublishesPaymentReceivedEvent` + `_Overpayment_*` **Passed** |
| 2 | `DeliveryDeadline` armlanıyor; süreyi `delivery_timeout_minutes` besliyor | ✓ | `TimeoutSchedulingService.cs:168-190` — anahtar `delivery_timeout_minutes` (`:25`), `SaveChanges` yok, Hangfire çağrısı yok. **06 §3.5 normatif matrisiyle birebir**: `PAYMENT_RECEIVED → DeliveryDeadline NOT NULL, job yok` — bu satır T124 öncesi üretimde **ihlal ediliyordu** (kolonun yazarı yoktu), görev onu kapatıyor. Fallback'in "üretimde ulaşılamaz" iddiası bağımsız doğrulandı: `SettingsBootstrapService.ExecuteAsync:78-90` **tüm** unconfigured satırlarda startup fail-fast ediyor ve `SystemSettingsValidator.ValidateRange:288-292` int'lerde `d <= 0`'ı reddediyor (hem admin hem env yolu). Lokal koşum: `ArmDeliveryDeadline_*` 7 vaka **Passed** |
| 3 | Scanner'ın PAYMENT_RECEIVED dalı T127'ye kadar tüketmiyor | ✓ | **Kapının eksiksizliği bağımsız tarandı:** backend'de `Fire(TransactionTrigger.Timeout)` yalnız iki yerde — `TimeoutExecutor.cs:64` (ilk guard `:55` `Status != SELLER_CONFIRMED → return`, teslimatı tüketemez) ve `DeadlineScannerJob.cs:151` (delivery dalı `:115-123` sorgusundan çıkarılmış). Teslimat timeout'unu tüketebilecek **üçüncü yol yok**. `ReportGatedDeliveryTimeoutsAsync:219-243` salt-okunur (Count + `Select(Id).Take`), yazma yok, `:130-137` try/catch enforcement turunu maliyetlendirmiyor. Lokal koşum: `Scanner_Does_Not_Consume_..._Until_T127`, `Scanner_Still_Consumes_Other_Phases_When_Gated_...`, `Delivery_Timeout_Publishes_Nothing_While_Gated_Until_T127` **Passed** |

**Ters çevrilen testlerin bıraktığı boşluk kapalı:** `TimeoutSideEffectPublisherTests.Delivery_Phase_Emits_Notification_And_PaymentRefund` lokalde **Passed** — publisher'ın teslimat fan-out'u (bildirim + alıcı iadesi) hâlâ kaplı; bu görevin kestiği şey publisher değil, **scanner→publisher kablosu**.

**Açlık testinin gerçekten kısıtladığı doğrulandı:** `TimeoutTestFixtures.Options(batchSize: 1)` fiilen `DeadlineScannerBatchSize = 1` yazıyor (`TimeoutTestSupport.cs:222-232`), yani test kararın (a) gerekçesini gerçekten sabitliyor.

### Test sonuçları — validator lokal koşumu

| Tür | Sonuç | Komut |
|---|---|---|
| Backend tam suite | ✓ **2561/2561** (13 assembly, exit 0) | `dotnet test Skinora.sln` + `Skinora.Shared.Tests` ayrı koşum |
| T124 kapsamı, isim bazında | ✓ **11/11 vaka** | `--filter` ile `ArmDeliveryDeadline_*` · `Scanner_Does_Not_Consume_*` · `Scanner_Still_Consumes_*` · `Delivery_Timeout_Publishes_Nothing_*` · `Delivery_Phase_Emits_*` |
| Webhook uçtan uca | ✓ **6/6** | `--filter "…BlockchainWebhookEndpointTests.PaymentConfirmed"` |

Dağılım yapım raporuyla birebir örtüştü: Shared 399 · Steam 39 · Platform 189 · Auth 120 · Admin 22 · Users 22 · Payments 6 · Realtime 40 · Fraud 91 · Disputes 60 · Notifications 171 · Transactions 870 · API 532.

### E2E advisory bacakları — bağımsız baseline karşılaştırması

Validator, raporun iddiasını kendi ölçümüyle yeniden üretti: HEAD run `31813382780` log'unda kök sebep imzası `Invalid object name 'PlatformSteamBots'` **tam 8 kez, leg başına 1**; T124 yüzeylerinden (`ArmDeliveryDeadline` / `DeliveryDeadline` / `delivery_timeout_minutes` / gated delivery) **0 iz**; baseline main run `31802083622` (T123 merge) **aynı 8 bacağı aynı şekilde** kırık bırakmış. → T124 kaynaklı yeni kırılma yok.

### Güvenlik kontrolü (Katman 1)

| Alan | Sonuç |
|---|---|
| Secret sızıntısı | ✓ Temiz — yeni log satırları yalnız transaction Id (GUID) + deadline timestamp içeriyor |
| Auth/authorization | ✓ Temiz — yeni uç yok; webhook mevcut HMAC + nonce arkasında |
| Input validation | ✓ Temiz — tek dış girdi `delivery_timeout_minutes`; okuyucuda `int.TryParse` + `> 0`, ayrıca `SystemSettingsValidator`. **Taşma ayağı ayrıca kontrol edildi:** üst sınır yok ama `TimeSpan.FromMinutes(int.MaxValue)` ≈ 4083 yıl → `DateTime` taşmıyor (yıl ~6109 < 9999), yani absürt bir değer istisna üretip ödeme onayını düşüremez |
| Yeni dış bağımlılık | ✓ Yok |

### Bulgular (ikisi de bloke edici değil)

| # | Seviye | Açıklama | Etkilenen dosya |
|---|---|---|---|
| 1 | S1 — doküman | **T127'nin başlığı ile T124'ün altına eklediği kabul kriteri çelişiyor.** Başlık `Task T127: TimeoutExecutor'a teslimat doğrulama turu` diyor, ama teslimat fazı 05 §4.4 gereği scanner-driven ve `TimeoutExecutor.cs:55` ilk satırında `Status != SELLER_CONFIRMED → return` ile teslimatı zaten reddediyor. T124'ün eklediği kriter doğru yeri (`DeadlineScannerJob.ReportGatedDeliveryTimeoutsAsync`) adlandırıyor. Başlık T124'ten eski, ama kapıyı kaldırma sorumluluğunu bu göreve bağlayan kriteri T124 yazdı; yalnız başlığı okuyan bir T127 yapımcısı turu yanlış executor'a bağlar ve **kapı hiç kalkmaz**. **KAPATILDI (proje sahibi onayı, 2026-08-14):** başlık `Task T127: DeadlineScannerJob'a teslimat doğrulama turu` olarak düzeltildi, gerekçe plana not olarak yazıldı | `Docs/11_IMPLEMENTATION_PLAN.md:2588` |
| 2 | S1 — dayanıklılık (**pre-existing**, T127 ön koşulu) | **Freeze/resume faz kayması `DeliveryDeadline`'ı ödeme fazının artığıyla eziyor.** Zincir üretimde ulaşılabilir ve validator tarafından kod üzerinden doğrulandı: işlem `SELLER_CONFIRMED` iken bulk freeze (`PLATFORM_MAINTENANCE` / `BLOCKCHAIN_DEGRADATION`) `TimeoutRemainingSeconds`'ı **ödeme** deadline'ından yakalıyor → freeze sürerken ödeme onaylanıyor (`Application/Webhooks/` altında `TimeoutFrozenAt` kontrolü **hiç yok**, grep boş; state machine yalnız `IsOnHold`'a bakıyor) → T124 teslimat penceresini armlıyor → resume `ResumeAsync:81,86` `now + TimeoutRemainingSeconds`'ı **güncel** state'e göre dağıtıyor, yani `SetActiveDeadline:185-187` ödeme artığını `DeliveryDeadline`'a yazıyor. **T124 regresyonu değil, aksine:** `TimeoutFreezeService` bu dalda dokunulmadı ve main'de bu ezilmiş değer scanner tarafından **tüketiliyordu** (iptal + alıcıya iade + satıcıya kusur); T124'ün kapısı ara dönemde bunu etkisizleştiriyor. Ancak T127 kapıyı kaldırdığında zarar geri gelir ve tam olarak kapının önlemek için kurulduğu vakayı üretir. Ayrıca yapım raporunun dış-varsayım tablosundaki *"freeze/resume … armlanan kolon bu yollarda doğru davranır"* satırı bu faz-kayması vakasını kapsamıyor. **KAPATILDI (proje sahibi onayı, 2026-08-14):** backlog kalemi yerine **T127'ye kabul kriteri** olarak eklendi — "kapı kalkmadan ÖNCE faz kayması kapatılmış ve testle sabitlenmiş olmalı"; iki kabul edilebilir çözüm adlandırıldı (`ConfirmPayment`'ı `TimeoutFrozenAt`'e karşı korumak **veya** freeze altında faz değişince artığı yeniden yakalamak). Gerekçe: zarar tam olarak T127'de doğduğu için ön koşul olması atlanamaz kılıyor | `.../Timeouts/TimeoutFreezeService.cs:81-96,185-187` (T50 kodu, bu dalda değişmedi) |

**Çok mercekli bağımsız tarama:** 5 mercek (AC uyumu · regresyon avı · implementasyon doğruluğu · test+doküman · güvenlik/para) × merceğe düşen bulgu başına 3 farklı açıdan çürütme turu (23 ajan). **Filed 6 bulgu, 0'ı çürütmeden sağ çıktı.** Yukarıdaki iki bulgu, validator'ın çürütme gerekçelerini kendi ölçümüyle yeniden kontrol ettikten sonra "T124 kusuru değil ama kayda değer" olarak bilinçle raporlanmıştır — sağ kalan bloke edici bulgu yoktur.

**Ayrıca bağımsız doğrulanan regresyon riskleri (hiçbiri bulgu değil):**
- **FLAGGED invariantı kırılmıyor:** `HasFlaggedStateInvariant` (tüm deadline'lar NULL) yalnız FLAGGED'den **çıkış** geçişlerinde (`AdminApprove` / `AdminReject`, `TransactionStateMachine.cs:306-308`) guard'dır ve FLAGGED yalnız oluşturmada set edilir → `PAYMENT_RECEIVED` hiç FLAGGED'a gidemez, armlanan kolon invariantı ihlal edemez.
- **`RestartRecoveryService:111-112`** kesinti kaymasını `DeliveryDeadline`'a da ekliyor ve teslimat fazı için **Hangfire job yeniden kurmuyor** (`:120` yalnız `SELLER_CONFIRMED`) → ikinci executor doğmuyor.
- **FE kırılmıyor:** detay/liste timeout bloğunun `"delivery"` tipi T117'den beri üretilebilir durumdaydı ve `signalr/events.ts:30` `TimeoutPhase` birliği `"Delivery"`yi zaten içeriyor; T124 FE değişikliği gerektirmiyor (CI'da `3b. JS test` beklendiği gibi skipped).

### Yapım raporu karşılaştırması

- **Uyum: 3/3 kabul kriterinde tam uyumlu.** Test toplamı (2561), proje dağılımı, E2E baseline analizi ve "kapının tek noktada olması" money-safety iddiası validator tarafından **bağımsız olarak yeniden üretildi** ve birebir tuttu.
- **2 küçük uyuşmazlık:** (a) rapordaki CI referansı `31809828883` bir önceki commit `44f42b4` içindir; HEAD `6c1beeb` için ayrıca yeşil bir run (`31813382780`) mevcut — validator HEAD run'ını esas aldı. (b) Dış-varsayım tablosundaki freeze/resume satırı olduğundan güçlü — Bulgu 2'ye bakınız.

## Altyapı Değişiklikleri

- **Migration:** Yok
- **Config/env değişikliği:** Yok — `SKINORA_SETTING_DELIVERY_TIMEOUT_MINUTES` T123'te tanımlanmıştı, bu görev onu **tüketmeye** başladı. Yeni env yok, ama ayarın değeri artık davranışa bağlı: DEPLOY_RUNBOOK §A #6 uyarısı güncellendi
- **Docker değişikliği:** Yok

## Commit & PR

- Branch: `task/T124-confirm-payment-delivery-deadline`
- Commit: `44f42b4` — T124: ConfirmPayment yeniden bağlanması + DeliveryDeadline
- PR: [#232](https://github.com/turkerurganci/Skinora/pull/232)
- CI: ✓ **PASS** — run [`31809828883`](https://github.com/turkerurganci/Skinora/actions/runs/31809828883), **CI Gate `success`**

### CI sonucu

**Bloke edici 8 job yeşil:** Detect changed paths · 1. Lint · 2. Build · 3. Unit test · 4. Integration test · 5. Contract test · 6. Migration dry-run · 7. Docker build (backend) · **CI Gate**.
`0. Guard (direct push)` ve `3b. JS test (vitest)` **skipped** — sırasıyla PR event'i ve frontend'e dokunulmaması nedeniyle beklenen.

**8 advisory E2E leg kırmızı — T124 kaynaklı DEĞİL, kanıtlandı:**

| Kontrol | Sonuç |
|---|---|
| Baseline (kodun değiştiği son main run'ı — T123 merge, [`31802083622`](https://github.com/turkerurganci/Skinora/actions/runs/31802083622)) | **Aynı 8 leg orada da `failure`** |
| Bu run'da kök sebep imzası | `Invalid object name 'PlatformSteamBots'` — **leg başına tam 1 iz, 8/8** |
| T124 yüzeylerinden iz (`DeliveryDeadline` / `delivery_timeout_minutes` / `ArmDeliveryDeadline` / `ReportGatedDelivery`) | **8 leg'in hepsinde 0** |

Yani yeni kırılma yok; kök sebep T117'nin düşürdüğü `PlatformSteamBots` tablosunu temizlemeye çalışan `e2e/src/db.ts` seed'i ve custodial akışı süren spec'ler — sahiplik **T137 → T138**. Legler `continue-on-error` olduğu için gate'i bloklamıyorlar.

**Düzeltme (bu raporun ilk taslağına göre):** T124 `e2e/**` dosyalarına dokunmadığı için E2E leglerinin tetiklenmeyeceğini yazmıştım — yanlıştı. `ci.yml`'deki `e2e-stack` path filtresi `backend/**`'i de kapsıyor (`.github/workflows/ci.yml:86-92`), dolayısıyla salt-backend değişiklikleri de bu legleri çalıştırır.

## Known Limitations / Follow-up

- **Teslimat timeout'u T127'ye kadar hiçbir şey tetiklemiyor.** Süresi dolan işlem `PAYMENT_RECEIVED`'da kalır; alıcının parası emanette bekler ve scanner her taramada (varsayılan 30 sn) uyarı logu üretir. Bu plan sırasının kabul edilmiş sonucudur — 05 §4.4 iptalden önce doğrulama turu şart koşuyor ve o tur T127'de. Kapının kaldırılması T127 kabul kriteri olarak yazıldı.
- **Ara dönemde kullanıcıya görünen etki:** deadline armlandığı andan itibaren detay/liste ekranları ve `CountdownSyncBroadcaster` teslimat geri sayımını gösterir; geri sayım sıfırlanınca hiçbir şey olmaz. FE tarafında ek bir çalışma yapılmadı (T134/T135 kapsamı).
- **Teslimat fazı için timeout uyarısı yok.** `WarningDispatcher` yalnız ödeme fazını uyarıyor; teslimat penceresinin sahibi satıcı hiçbir uyarı almıyor. `DEFERRED_BACKLOG` `P2P-DeliveryTimeoutWarning` — ön koşulu bu görevle karşılandı, kalem açık.
- **E2E teslimat-timeout spec'i bayat.** `e2e/tests/timeout.spec.ts` v2.0 akışını (bot dispatch, `TRADE_OFFER_SENT_TO_BUYER`) sürüyor; yeniden yazımı T138'in kapsamında (T137 bağımlı). Bu görevde dokunulmadı.
- **`delivery_timeout_minutes` değeri hâlâ ölçülmemiş.** T122 gerçek teslimat gecikmesini ölçemedi; launch değeri muhafazakâr yüksek tutulmalı, kapanış T125 launch kapısına bağlı.

## Notlar

**Working tree:** temiz (`git status --short` boş).

**Main CI startup check (task.md Adım 0):** `31802083727` ✓ success · `31802083622` ✓ success · `31733188607` ✓ success — son üç tamamlanmış run yeşil.

**Dış varsayımlar (task.md Adım 4) — kırık yok:**

| Varsayım | Kanıt |
|---|---|
| `delivery_timeout_minutes` satırı seed'de mevcut | `SystemSettingSeed.cs:50` (Id=6, `Unconfigured`) |
| Env hydrate + startup fail-fast var, yani üretimde satır her zaman configured | `SettingsBootstrapService.ExecuteAsync` (`stillMissing` → `InvalidOperationException`), `.env.example:175`, `docker-compose.e2e.yml:127`, `SettingsBootstrapTests.cs:48` |
| Değer `>0` hem admin hem env yolunda zorlanıyor → kod fallback'i pratikte ulaşılamaz | `SystemSettingsValidator.ValidateRange` generic int kuralı (`dataType is "int" or "decimal"` → `d <= 0` reddedilir) |
| Scanner'ın PAYMENT_RECEIVED dalı ve `(Status, DeliveryDeadline)` index'i mevcut | `DeadlineScannerJob.cs` (T124 öncesi hâli), `TransactionConfiguration.cs:247` |
| Freeze/resume + restart recovery `DeliveryDeadline`'ı zaten biliyor → armlanan kolon bu yollarda doğru davranır | `TimeoutFreezeService.cs:163,186` + `FreezeAsync_PAYMENT_RECEIVED_Captures_DeliveryDeadline_Remainder`; `RestartRecoveryService.cs:111` + `RestartRecoveryServiceTests.cs:116` |
| Teslimat fazı yan etki bacağı (iade eventi) publisher'da mevcut → T127 kapıyı kaldırınca çalışacak | `TimeoutSideEffectPublisher` `TimeoutPhase.Delivery` bacağı + `TimeoutSideEffectPublisherTests.Delivery_Phase_Emits_Notification_And_PaymentRefund` (bu görevde dokunulmadı) |

**Yapım öncesi üç karar proje sahibine soruldu, üçü de onaylandı (2026-08-14):**

| Karar | Seçim | Gerekçe |
|---|---|---|
| Kapı şekli | Ayrı salt-okunur sorgu | Döngüde atlamak `DeadlineScannerBatchSize`'ı kalıcı gated satırlarla doldurup diğer üç fazın timeout'unu sessizce durdurabilirdi — testle sabitlendi |
| `delivery_timeout_minutes` kod fallback'i | 1440 dk (24 sa) | Ulaşılamaz savunma yolu; yön DEPLOY_RUNBOOK §A #6'nın "muhafazakâr YÜKSEK" uyarısından. İki hata yönü simetrik değil: uzun = alıcı bekler, kısa = (T127 sonrası) teslim etmiş satıcıyı haksız iptal eder |
| Doküman yansıması | Üçü de | T127 kabul kriteri + runbook + backlog ön koşulu |

**Mini güvenlik kontrolü (Katman 1):**
- Secret sızıntısı: yok — yeni secret/credential yok, log satırları yalnız transaction Id + deadline içeriyor
- Auth/authorization: değişmedi — yeni uç yok; webhook yolu mevcut HMAC + nonce doğrulamasının arkasında
- Input validation: yeni kullanıcı girdisi yok. Tek dış girdi `delivery_timeout_minutes` ayarı; parse + `>0` kontrolü hem okuyucuda hem `SystemSettingsValidator`'da
- Yeni dış bağımlılık: yok

**Kapının tek noktada olması (money-safety notu):** `PAYMENT_RECEIVED`'dan `Timeout` tetikleyebilecek tek üretim yolu scanner'dı — `TimeoutExecutor.ExecutePaymentTimeoutAsync` ilk satırında `Status != SELLER_CONFIRMED` ise no-op ediyor. Dolayısıyla kapı tek yerde ve eksiksiz. `AdminCancel` ayrı bir tetikleyicidir ve bilinçli olarak kapatılmadı — admin müdahalesi bu kapının konusu değil.
