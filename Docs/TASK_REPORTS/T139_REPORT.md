# T139 — Ödeme izleyicisinin bağlanması (arm / re-arm / disarm)

**Faz:** F7 | **Durum:** ⏳ Devam ediyor (doğrulama tur 1 ✗ FAIL → düzeltme turu uygulandı, yeniden doğrulama bekliyor) | **Tarih:** 2026-08-20

---

## Yapılan İşler

Aktif ödeme izleyicisinin (T71 sidecar ucu) backend tarafında hiç çağıranı yoktu. Kaynak: `DEFERRED_BACKLOG` §4 `T133b-PaymentMonitorUnarmed` — T133b doğrulamasının B4 bulgusu.

**Ölçüm, backlog satırının yazdığından geniş çıktı.** Satır yalnız *kurma* yarısını adlandırıyordu; kod okunduğunda yaşam döngüsünün **üç halkasının da** sahipsiz olduğu görüldü:

| Halka | Post-cancel izleyici (T75) | Aktif izleyici (T71) — T139 öncesi |
|---|---|---|
| Kurma | `PostCancelMonitorStarter` → outbox → `PostCancelMonitorStartDispatcher` | **yok** |
| Restart kurtarması | `PostCancelMonitorRecoveryHook` | **yok** |
| Durdurma | `StopPostCancelMonitoringAsync` + sidecar 30 gün sonunda | **yok** |

Yapılanlar:

1. **Port genişletildi** — `IBlockchainSidecarClient` += `StartMonitoringAsync` / `StopMonitoringAsync`; `HttpBlockchainSidecarClient` ikisini de mevcut `SendCommandAsync` üzerinden `api/monitor/start` ve `api/monitor/stop`'a bağlıyor.
2. **Kurma (fast path)** — `TransactionReadinessService` `ACCEPTED → SELLER_CONFIRMED` geçişiyle **aynı** `SaveChangesAsync` içinde `PaymentMonitorStartRequestedEvent` yayınlıyor; `PaymentMonitorStartDispatcher` olayı tüketip sidecar'ı kuruyor. *(Yapım turunda dispatcher'ın DI kaydı atlanmıştı ve bu cümle o hâlde doğru değildi — doğrulama bulgusu B1, aşağıda; kayıt düzeltme turunda eklendi.)*
3. **Yeniden kurma / self-heal** — `EnsurePaymentMonitorJob` (Hangfire, `* * * * *`) açık penceredeki her adresi her turda idempotent olarak yeniden kuruyor.
4. **Durdurma** — (a) iptal devrinde `PostCancelMonitorStartDispatcher` post-cancel'ı kurmadan **önce** aktif izleyiciyi durduruyor; (b) pencere kapandığında (sweep `CONFIRMED` veya terminal statü) aynı reconciler `stop` çağırıp satırı `MonitoringStatus = STOPPED` damgalıyor.
5. **Doküman** — 08 §3.4'e yaşam döngüsü tablosu, 06 §2.16'ya `ACTIVE`in iki anlamı, `DEPLOY_RUNBOOK` §G.4'ün elle `curl` notu **doğrulama** notuna çevrildi, backlog satırı ✅.

### Neden yalnız "arm" yetmezdi (kapsam kararı D2'nin gerekçesi)

Yalnız `start` çağrısını bağlamak iki yeni kusur üretirdi: (1) hiç durmayan izleyici — `MonitoringStatus.ACTIVE`'in tek yazarı allocator, tek çıkışı iptal koluydu, yani mutlu yolda satır sonsuza kadar `ACTIVE` kalıyor ve `ReconciliationService:183` (`!= STOPPED` olan her adresi günlük snapshot'a alır) sınırsız büyüyordu; (2) iptal yolunda **aynı adresi iki registry** (aktif + post-cancel) birden yoklardı.

### Neden `PAYMENT_RECEIVED`'da durmuyor (kapsam kararı D3)

02 §4.4'ün *fazla tutar* kolu ve 03 §5.5'in *ikinci ödeme* kolu, ödeme kabul edildikten **sonra** gelen bir transferi tarif eder. Onayda duran bir izleyici o transferi hiç görmez. Pencereyi kapatan şey işlemin ilerlemesi değil, **depozitin boşalmasıdır** (sweep) — ya da işlemin terminal olmasıdır.

### Neden enum'a değer eklenmedi (kapsam kararı D4)

`ACTIVE` iki durumu birden taşıyor ("tahsis edildi, pencere açılmadı" ve "kurulu"). Ayrım kolonda değil sidecar registry'sinde durur ve reconciler her dakika işlem statüsünden yeniden türetir. Enum'a üçüncü değer eklemek migration + kardeş projelerdeki parity testleri + `IX_PaymentAddresses_MonitoringStatus_Active` filtre listesi demekti ve karşılığında hiçbir davranış kazanılmıyordu. Belirsizliğin bir daha keşfedilmemesi için anlam 06 §2.16'ya **açıkça** yazıldı.

## Etkilenen Modüller / Dosyalar

**Yeni:**
- `backend/src/Skinora.Shared/Events/PaymentMonitorStartRequestedEvent.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PaymentMonitoring/PaymentMonitorStartDispatcher.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PaymentMonitoring/EnsurePaymentMonitorJob.cs`
- `backend/src/Modules/Skinora.Transactions/Application/PaymentMonitoring/EnsurePaymentMonitorJobRegistrar.cs`
- `backend/tests/Skinora.Transactions.Tests/Unit/PaymentMonitoring/EnsurePaymentMonitorJobClassificationTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Unit/PaymentMonitoring/PaymentMonitorStartDispatcherTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Unit/PostCancel/PostCancelMonitorStartDispatcherTests.cs`
- `backend/tests/Skinora.Transactions.Tests/Integration/PaymentMonitoring/EnsurePaymentMonitorJobTests.cs`

**Değişen (kod):**
- `.../PaymentAddresses/IBlockchainSidecarClient.cs` — iki metot + `PaymentMonitorStartRequest`
- `.../PaymentAddresses/HttpBlockchainSidecarClient.cs` — iki implementasyon + iki gövde record'u
- `.../Lifecycle/TransactionReadinessService.cs` — Stage 10b
- `.../PostCancel/PostCancelMonitorStartDispatcher.cs` — devir öncesi `stop`
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — job + registrar kaydı; **düzeltme turu:** `PaymentMonitorStartDispatcher`'ın MediatR handler kaydı (B1)
- `sidecar-blockchain/src/monitor/MonitorRegistry.ts` — sınıf yorumu (emekli `PENDING_PAYMENT (T44 state)` çağıranını tarif ediyordu)
- Dört test stub'ı (`StubBlockchainSidecarClient` + üç `Skinora.API.Tests` stub'ı) yeni arayüz üyeleriyle

**Değişen (doküman):**
- `Docs/11_IMPLEMENTATION_PLAN.md` — §F7 P7'ye T139 bloğu; F7 aralığı T115–T138 → T115–T139
- `Docs/06_DATA_MODEL.md` v6.12 → **v6.13** — §2.16 + §3.7
- `Docs/08_INTEGRATION_SPEC.md` v3.2 → **v3.4** — §3.4 yaşam döngüsü tablosu (altbilgi sürümü de düzeltildi: v2.6 → v3.3), düzeltme turunda **v3.4**: pencere süresinin ölçülmüş bedeli (N1)
- `Docs/DEPLOY_RUNBOOK.md` — §G.4 kontrol 10 + elle kurma notu → doğrulama notu
- `Docs/DEFERRED_BACKLOG.md` — `T133b-PaymentMonitorUnarmed` ✅ (45 → 44), düzeltme turunda `T139-ActiveMonitorQuotaAlarm` açıldı (44 → **45**)
- `Docs/IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| AC1 | Port `StartMonitoringAsync` + `StopMonitoringAsync` taşır, gövde sidecar'ın beş zorunlu alanıyla birebir | ✓ | `IBlockchainSidecarClient.cs` + `HttpBlockchainSidecarClient.cs`; `MonitorStartRequestBody` alanları `startMonitorHandler`'ın (`monitorHandlers.ts:33-38`) zorunlu beşlisiyle birebir; `PaymentMonitorStartDispatcherTests.Success_Arms_The_Sidecar_With_The_Mapped_Payload` alan alan doğruluyor |
| AC2 | Arm, geçişle aynı `SaveChanges` içinde; **dispatcher olayı tüketip sidecar'ı kurar**; adres yoksa geçiş bloklanmaz | ✓ *(düzeltme turundan sonra)* | Yayın kolu: `TransactionReadinessService` Stage 10b; `Arming_Rides_The_Same_Unit_Of_Work_As_The_Transition` (2 olay, tek batch) + `A_Missing_Deposit_Address_Does_Not_Block_The_Confirmation`. **Tüketim kolu yapım turunda BAĞLI DEĞİLDİ** (doğrulama bulgusu B1) — `TransactionsModule.cs`'e DI kaydı eklendi, `TransactionsModuleNotificationHandlerTests` bekçisi kaydın varlığını zorunlu kılıyor |
| AC3 | Self-heal: dakikalık idempotent yeniden kurma | ✓ | `EnsurePaymentMonitorJob` (`Cron = "* * * * *"`) + `EnsurePaymentMonitorJobRegistrar`; `Arming_Is_Repeated_Every_Run_So_A_Sidecar_Restart_Self_Heals` |
| AC4 | Disarm: (a) iptal devrinde stop→start sırası, (b) pencere kapanışında stop + `STOPPED` | ✓ | (a) `Handover_Stops_The_Active_Monitor_Before_Starting_PostCancel` çağrı **sırasını** assert eder; (b) `Terminal_Status_Stops_The_Monitor_And_Stamps_Stopped` + `A_Swept_Deposit_Is_Disarmed_While_The_Transaction_Is_Still_Live` |
| AC5 | Birim + entegrasyon kapsaması | ✓ | 4 yeni test dosyası; aşağıdaki test tablosu |
| AC6 | Doküman: 08 §3.4 yaşam döngüsü, 06 §3.7/§2.16 `ACTIVE` anlamı, runbook `curl` notu kalkar, backlog ✅ | ✓ | Yukarıdaki doküman listesi |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build (Release) | ✓ 0W / 0E | `dotnet build -c Release` |
| `dotnet format` | ✓ exit 0 | `dotnet format --verify-no-changes --no-restore` — 0 bulgu |
| **Tüm backend suite** | ✓ **2817/2817** | 13 assembly, **0 fail** — Shared 409 · Platform 185 · Users 22 · Auth 120 · Steam 39 · Payments 6 · Realtime 39 · Admin 22 · Fraud 91 · Notifications 171 · Disputes 83 · Transactions 1079 · API 551 |
| Yeni testler | ✓ 44/44 | Üç yeni dosya (`Unit.PaymentMonitoring` + `Unit.PostCancel` + `Integration.PaymentMonitoring`); ayrıca `TransactionReadinessServiceTests`'e **3** test eklendi → toplam **47** yeni test |
| **Düzeltme turu (validator koşumu)** | ✓ | Build Release **0W/0E** · `dotnet format --verify-no-changes` exit 0 · **Unit 1437/1437** (API 46 → **47**, yeni DI bekçisi) · `Integration.PaymentMonitoring` + `Integration.Lifecycle.TransactionReadinessServiceTests` **50/50** · üç yeni testin **üçü de ayırt edici** olduğu, düzeltmeler geçici olarak devre dışı bırakılarak kanıtlandı |
| Yeni + komşu testler | ✓ 75/75 | Yukarıdaki filtre + `TransactionReadinessServiceTests` sınıfının tamamı |
| sidecar-blockchain tsc | ✓ exit 0 | `npx tsc --noEmit` |
| sidecar-blockchain prettier | ✓ (LF) | Lokal `--check` **tüm** pakette uyarıyor (`core.autocrlf=true` artefaktı — dokunulmamış `routes.ts`/`monitorHandlers.ts` de uyarıyor); LF'e normalize edilmiş kopya **temiz** |

> **Suite'i sıralı koşturmak gerekti — bu bir bulgu değil, lokal ortam sınırı.** Solution kökünde `dotnet test` assembly'leri paralel koşturuyor ve her biri kendi veritabanını yarattığı için lokal SQL Server tükeniyor: ilk denemede 5 assembly `CREATE DATABASE failed. Some file names listed could not be created.` + `SSL Provider ... transport-level error` ile düştü (582 "fail"). Assembly'ler **tek tek** koşturulduğunda aynı binary'lerle **13/13 yeşil**. İmza tamamen altyapısaldır (hiçbir assertion hatası yok) ve CI kendi SQL container'ıyla koştuğu için orada görülmez.

## Doğrulama — Tur 1 (2026-08-20, bağımsız doğrulama chat'i)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✗ **FAIL** → düzeltme turu **aynı dalda uygulandı** (proje sahibi kararı) |
| Bulgu sayısı | **3** — 1 bloke edici (B1) + 2 bloke etmeyen (N1, N2) |
| Düzeltme gerekli mi | Evet — üçü de kapatıldı, kayıtları plan §F7 T139 "DÜZELTME TURU" bloğunda |

**Giriş kapıları:** working tree temiz ✓ · main son 3 run `success` ✓ · repo memory T139 satırı mevcut ✓

**Bağımsız olarak yeniden üretilen ve doğru bulunanlar:** AC1 (sidecar'ın beş zorunlu alanı ↔ gövde birebir; 400→`InvalidRequest` / 503→`NotConfigured` / transport→`Unavailable` eşlemesi) · AC3 (`start` idempotency'si `MonitorRegistry.ts` üzerinden teyit edildi) · AC4 (a+b; `Classify` on iki `TransactionStatus`ün on ikisini de tam bir kümeye koyuyor; `SweepQueueJob` gerçekten `PaymentAddressId` yazıyor, yani sweep kapısı ölçülebilir; `MonitoringExpiresAt = null` konvansiyonu `BlockchainWebhookHandler:506` ile tutarlı) · AC6 (runbook'un **üç** gözlem komutunun üçü de koşulabilir: log metinleri, metrik adı, `/metrics` auth'suzluğu ve port eşlemesi tek tek doğrulandı) · advisory E2E **10/32**, iki bağımsız T139-öncesi tabanla (`32264946887` T133 · `32246467184` T132) **leg-leg birebir** → regresyon yok.

### B1 — BLOKE EDİCİ (S3 Eksik): dispatcher DI'a kaydedilmemişti

`OutboxModule.GetMediatRScanAssemblies()` tam olarak üç assembly tarar (API host · Notifications · Realtime). **`Skinora.Transactions` bunların arasında değildir**, bu yüzden o assembly'deki üç kardeş handler `TransactionsModule`'de tek tek elle kaydedilmiştir ve her birinin üstündeki yorum tam bu tuzağı adlandırır. T139 dördüncüyü eklerken kaydı atladı ve yerine *"MediatR discovers it by assembly scan"* diyen bir yorum yazdı — bu assembly için yanlış. `IPublisher.Publish` sıfır handler'la sessizce döner ve `OutboxDispatcher` satırı `PROCESSED` damgalar; yani olay yayınlanıyor, sidecar **hiç çağrılmıyor**.

Ürün ölmedi çünkü AC3'ün reconciler'ı ≤60 sn içinde aynı işi yapıyor — **AC3, AC2'nin yokluğunu maskeledi.** Bu, bulgunun neden yalnız kompozisyon seviyesinde görülebildiğini de açıklar: `PaymentMonitorStartDispatcherTests` sınıfı `new`'leyip `Handle`'ı çağırıyor ve yeşil kalıyor.

**Kapatma:** DI kaydı + yanlış yorumun düzeltilmesi + **sınıfın kapatılması** — `TransactionsModuleNotificationHandlerTests` reflection ile bu assembly'deki her `INotificationHandler<T>`'nin kayıtlı olduğunu zorluyor. Bekçinin ayırt ediciliği kanıtlandı: kayıt geçici olarak kaldırıldığında test `Unregistered: PaymentMonitorStartRequestedEvent` diyerek düşüyor.

### N1 — bloke etmez: D3'ün ölçülmemiş bedeli

`ArmedStates` `ITEM_DELIVERED`'ı içeriyor ve pencereyi kapatan sweep `SettlementVerifiedAt` damgalanmadan kuyruklanamıyor; `payout_settlement_days`'in sert tabanı **7 gün** (`SystemSettingsValidator.MinimumSettlementDays`). Yani her deposit adresi teslimattan sonra **bir hafta veya daha uzun** süre 3 saniyelik aktif kadansta kalıyor — eşzamanlı izleyici sayısı iki saatlik değil **bir haftalık** işlem hacmiyle ölçekleniyor ve TronGrid istek hacmi buna orantılı. Job'ın kendi XML doc'u ise *"An active payment window is 30-120 minutes"* diyerek kendi `ArmedStates`'iyle çelişiyordu.

**Kapatma:** doc cümlesi düzeltildi, bedel **08 §3.4'e** yazıldı (v3.3 → **v3.4**), alarm eşiği `DEFERRED_BACKLOG` → **`T139-ActiveMonitorQuotaAlarm`**'a sahiplendirildi (44 → **45** aktif satır). Bu, raporun Known Limitations'ındaki "alarm yok" maddesinin sahiplendirilmiş hâlidir — proje sahibinin tekrarlanan dersi gereği rapor maddesi sahip sayılmaz.

### N2 — bloke etmez: AC4(a)'nın yarış hâlinde hayatta kalması

Reconciler candidate'ları tek sorguda çekip sırayla sidecar çağırıyor ve döngü içinde durumu yeniden okumuyordu.

- **(a) Arm kolu:** fetch'ten sonra commit'lenen bir iptal, post-cancel devrini yapıp aktif izleyiciyi durdurur; bayat snapshot'tan gelen arm onu **geri kurar** ve satır artık `ACTIVE` olmadığı için job bir daha bakmaz → iki registry aynı adresi sidecar restart'ına kadar yoklar. Tam olarak AC4(a)'nın önlemek için var olduğu kusur, yarış hâlinde.
- **(b) Disarm kolu:** aynı yarış `STOPPED` damgasını `POST_CANCEL_24H`'in üstüne yazmaya çalışır — bu **para ilgili** olurdu (gecikmeli ödeme kurtarma penceresini emekliye ayırır). `RowVersion` concurrency token'ı (09 §10.4) körü körüne ezmeyi engelliyor; eksik olan istisnayı **ele almaktı** (tüm batch'in damgasını düşürüyordu).

**Kapatma:** (a) tur sonunda **tek ek sorgu** ACTIVE'den çıkmış armed adresleri bulup `stop` ile telafi ediyor; (b) `DbUpdateConcurrencyException` yakalanıyor, change tracker temizleniyor, damga bir sonraki tura bırakılıyor (damgalama idempotent). İki entegrasyon testi yarışın her iki kolunu da stub kancalarıyla (`OnMonitorStart` / `OnMonitorStop`) deterministik olarak üretiyor; **ikisi de ayırt edici** — düzeltmeler devre dışı bırakıldığında (a) `Assert.Single() Failure: The collection was empty`, (b) ham `DbUpdateConcurrencyException` ile düşüyor.

### Düzeltme turunun değişiklikleri

- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — dispatcher DI kaydı + yanlış yorumun düzeltilmesi (B1)
- `backend/tests/Skinora.API.Tests/Unit/Configuration/TransactionsModuleNotificationHandlerTests.cs` — **yeni** DI bekçisi (B1, sınıf kapatma)
- `.../PaymentMonitoring/EnsurePaymentMonitorJob.cs` — pencere bedeli doc'u (N1) + devir telafisi ve concurrency ele alımı (N2)
- `.../Integration/PaymentAddresses/StubBlockchainSidecarClient.cs` — `OnMonitorStart` / `OnMonitorStop` kancaları
- `.../Integration/PaymentMonitoring/EnsurePaymentMonitorJobTests.cs` — **2 yeni** yarış testi
- `Docs/08_INTEGRATION_SPEC.md` v3.3 → **v3.4** · `Docs/DEFERRED_BACKLOG.md` 44 → **45** · `Docs/11_IMPLEMENTATION_PLAN.md` §F7 T139 DÜZELTME TURU bloğu

### Düzeltme turu CI kanıtı

Dal HEAD `ad613c8` · run [`32377529035`](https://github.com/turkerurganci/Skinora/actions/runs/32377529035) **`success`**, **`CI Gate` yeşil** — on bloke edici job'ın onu da başarılı (`1. Lint` · `2. Build` · `3. Unit test` · `3b. JS test (vitest)` · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` · `7. Docker build (backend)` · `7. Docker build (sidecar-blockchain)` · `CI Gate`); `0. Guard (direct push)` skipped (beklenen).

Advisory E2E **10/32** — yapım turunun run'ıyla (`32369306467`) ve iki bağımsız T139-öncesi tabanla (`32264946887` T133 · `32246467184` T132) **leg-leg birebir** (T111 3 · T109 1 · T113 6). Düzeltme turu DI kablolamasına ve reconciler'a dokunduğu hâlde ağda ne kazanç ne kayıp var.

### KALICI DERS

**Bir handler'ın VAR olması ile ULAŞILABİLİR olması aynı şey değildir, ve birim testi ikincisini göstermez.** T139 bağlanmamış bir *caller*'ı kapatmak için açıldı ve kapatırken bağlanmamış bir *consumer* üretti — kusurun sınıfı aynı, yalnız yönü ters. İkisini de aynı şey mümkün kıldı: bir uç noktanın kendi testi yeşilken kompozisyonda ölü olabilmesi. Bu yüzden düzeltme tek örneği değil **sınıfı** kapatıyor; aksi hâlde bu assembly'ye eklenen bir sonraki handler aynı sessiz düşüşü tekrar ederdi.

## Altyapı Değişiklikleri

- **Migration: Yok** — D4 gereği enum ve şema değişmedi.
- **Config/env değişikliği: Yok** — yeni SystemSetting yok, yeni env yok.
- **Docker değişikliği: Yok.**
- **Yeni recurring job:** `ensure-payment-monitor` (cron `* * * * *`). Hangfire'a `EnsurePaymentMonitorJobRegistrar` ile kaydedilir.
- **Yeni dış bağımlılık: Yok.**

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** yok — sidecar çağrıları mevcut `X-Internal-Key` başlığını taşıyan `SendCommandAsync` üzerinden gider, yeni credential yok.
- **Auth/authorization:** yeni endpoint yok; `/api/monitor/*` zaten `internalKeyAuth` arkasında.
- **Input validation:** yeni kullanıcı girdisi yok — reconciler'ın tek girdisi veritabanı.
- **Yeni dış bağımlılık:** yok.

## Known Limitations / Follow-up

- **Kurma gecikmesinin üst sınırı 1 dakikadır.** Outbox teslimi düşerse izleyici reconciler turuna kadar kurulmaz. Ödeme penceresi en az 30 dakika olduğu için bu pratikte görünmez, ama sıfır değildir.
- **`skinora_blockchain_active_monitors` üzerinde alarm yok.** Kurulmamış bir pencere bugün yalnız log'dan görülür; bir "armed pencere sayısı ≠ açık pencere sayısı" alarmı bunu gözlenebilir yapardı — T139 kapsamına alınmadı. *(Doğrulama turu N1: madde bir rapor satırı olmaktan çıkarılıp `DEFERRED_BACKLOG` → `T139-ActiveMonitorQuotaAlarm` olarak **sahiplendirildi**; eşiğin kendisi TronGrid plan bütçesi bilinmeden sayı olarak yazılamıyor.)*
- **Canlı prova hâlâ koşulmadı.** `T133b-LiveRehearsalUnrun` açık kalır: bu turun kanıtı testlerdir, gerçek Nile transferi + gerçek sidecar ile uçtan uca ölçüm değil.

## Notlar

- **Working tree:** temiz (task.md Adım -1).
- **Main CI startup check (task.md Adım 0):** son 3 run `success` — `32361465502`, `32361465451`, `32352581013`.
- **Dış varsayımlar:** İki tanesi vardı ve ikisi de kod üzerinden doğrulandı. (1) *Sidecar'ın `start` ucu idempotenttir* — `MonitorRegistry.start` (`MonitorRegistry.ts:122-127`) mevcut adres için `started:false` döner ve `seenEvents`/cursor durumuna dokunmaz; dakikalık yeniden kurmanın güvenliği buna dayanıyor. (2) *`/metrics` auth istemez ve blockchain sidecar 5200'de yayınlanır* — `routes.ts:39` (`// no auth required`) ve `docker-compose.yml:196` (`${BLOCKCHAIN_SIDECAR_PORT:-5200}:5200`); runbook'a yazılan gözlem komutları bu ikisine dayanıyor. Metrik adı da doğrulandı ve düzeltildi (`skinora_blockchain_active_monitors`, `metrics.ts:68`) — T133b'nin "satır koşulabilir mi?" dersi.
- **Sahipsiz komşu, turda düzeltildi:** `08_INTEGRATION_SPEC.md` altbilgisi `v2.6` diyordu, başlık `v3.2`ydi (T133a beş dokümanın altbilgisini hizalamıştı ama 08 o turun kapsamında değildi). Sürüm bump'ı sırasında başlıkla hizalandı.
- **Kaynak yorumu düzeltildi:** `MonitorRegistry.ts` sınıf yorumu "backend calls `start` when a transaction enters PENDING_PAYMENT (T44 state)" diyordu — o statü T117'de emekli edildi, yani yorum **hiç var olmamış** bir çağıranı **emekli** bir statüyle tarif ediyordu. Bu cümlenin varlığı, çağıranın yokluğunun neden aylarca fark edilmediğinin bir parçasıdır: dokümantasyon "bağlıymış" gibi okunuyordu.
- **KALICI DERS:** bir "bağlanmamış caller" backlog satırı, eksik olanın yalnız **çağrı** mı yoksa **yaşam döngüsünün tamamı** mı olduğunu ayrıca sormalıdır. Burada çağrıyı tek başına bağlamak hiç durmayan bir izleyici ve iptal yolunda çift registry üretecekti — yani satırın harfini karşılayan bir düzeltme, kusuru **sınıf değiştirerek** hayatta bırakırdı (T133b'nin kendi dersinin ikizi).

## Commit & PR

- Branch: `task/T139-payment-monitor-lifecycle`
- Commit: `3ad44f7` — T139: Ödeme izleyicisinin bağlanması (arm / re-arm / disarm)
- PR: [#251](https://github.com/turkerurganci/Skinora/pull/251)
- CI: ✓ **PASS** — dal HEAD `3ad44f7` run [`32367284135`](https://github.com/turkerurganci/Skinora/actions/runs/32367284135) `success`, **`CI Gate` yeşil**

**Bloke edici jobların tamamı yeşil:** `1. Lint` · `2. Build` · `3. Unit test` · `3b. JS test (vitest)` · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` · `7. Docker build (backend)` · `7. Docker build (sidecar-blockchain)` · `CI Gate`. `0. Guard (direct push)` skipped (beklenen).

**Sekiz advisory E2E leg'i kırmızı — T139 kaynaklı DEĞİL, ve sayı ölçülerek doğrulandı.** Legler T117'nin custody emekliliğinden beri kırmızıdır, `continue-on-error` + `ci-gate.needs` dışındadır (proje sahibi kararı, `ci.yml:612-625`) ve yeniden yazımlarının sahibi T138'dir. T137'nin kalıcı dersi gereği leg statüsü değil **geçen test kümesi** sayıldı:

| Leg | Geçen | Düşen |
|---|---|---|
| T110 payment edge cases | 0 | 6 |
| T108 cancellation | 0 | 4 |
| T114 downtime | 0 | 3 |
| T112 emergency-hold | 0 | 3 |
| T111 fraud-flags | **3** | 1 |
| T109 timeout | **1** | 3 |
| T113 admin-flows | **6** | 1 |
| happy-path | 0 | 1 |
| **Toplam** | **10** | **22** |

**10/32** — T137a'nın ölçtüğü ve T137'nin düzeltme turundan sonra sabitlenen tabanla **birebir aynı**. T139 bu ağa ne ekledi ne çıkardı, ki bu beklenen sonuçtur: arm çağrısı outbox üzerinden **commit sonrası** gider, dolayısıyla sidecar erişilemez olsa bile geçişin kendisi etkilenmez.
