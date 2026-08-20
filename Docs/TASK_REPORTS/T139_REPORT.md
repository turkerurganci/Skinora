# T139 — Ödeme izleyicisinin bağlanması (arm / re-arm / disarm)

**Faz:** F7 | **Durum:** ✓ Tamamlandı (doğrulama tur 3 ✓ PASS) | **Tarih:** 2026-08-20

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
- `backend/tests/Skinora.API.Tests/Unit/Configuration/TransactionsModuleNotificationHandlerTests.cs` *(düzeltme turu 1 — B1 DI bekçisi)*
- `sidecar-blockchain/src/monitor/activeMonitorGauge.ts` + `activeMonitorGauge.test.ts` *(düzeltme turu 2 — N2-2 gauge toplayıcısı)*

**Değişen (kod):**
- `.../PaymentAddresses/IBlockchainSidecarClient.cs` — iki metot + `PaymentMonitorStartRequest`
- `.../PaymentAddresses/HttpBlockchainSidecarClient.cs` — iki implementasyon + iki gövde record'u
- `.../Lifecycle/TransactionReadinessService.cs` — Stage 10b
- `.../PostCancel/PostCancelMonitorStartDispatcher.cs` — devir öncesi `stop`
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — job + registrar kaydı; **düzeltme turu:** `PaymentMonitorStartDispatcher`'ın MediatR handler kaydı (B1)
- `sidecar-blockchain/src/monitor/MonitorRegistry.ts` — sınıf yorumu (emekli `PENDING_PAYMENT (T44 state)` çağıranını tarif ediyordu); **düzeltme turu 2:** gauge yazımı toplayıcıya alındı (N2-2)
- `sidecar-blockchain/src/monitor/PostCancelMonitor.ts` · `src/metrics.ts` — **düzeltme turu 2:** gauge yazımı toplayıcıya alındı + `help` metni düzeltildi (N2-2)
- `.../PaymentMonitoring/EnsurePaymentMonitorJob.cs` — **düzeltme turu:** pencere bedeli doc'u (N1) + devir telafisi ve concurrency ele alımı (N2); **düzeltme turu 2:** `BatchSize` → `PageSize` + `MaxAddressesPerRun`, tüm kümenin sayfalanması, tavan WARN'ı (B1-2)
- `.../Unit/PaymentAddresses/HttpBlockchainSidecarClientTests.cs` — **düzeltme turu 2:** 7 port testi (N1-2)
- Dört test stub'ı (`StubBlockchainSidecarClient` + üç `Skinora.API.Tests` stub'ı) yeni arayüz üyeleriyle

**Değişen (doküman):**
- `Docs/11_IMPLEMENTATION_PLAN.md` — §F7 P7'ye T139 bloğu; F7 aralığı T115–T138 → T115–T139
- `Docs/06_DATA_MODEL.md` v6.12 → **v6.13** — §2.16 + §3.7
- `Docs/08_INTEGRATION_SPEC.md` v3.2 → **v3.5** — §3.4 yaşam döngüsü tablosu (altbilgi sürümü de düzeltildi: v2.6 → v3.3); düzeltme turunda **v3.4** pencere süresinin ölçülmüş bedeli (N1); düzeltme turu 2'de **v3.5** gauge'un iki registry'yi birden saydığı (N2-2)
- `Docs/DEPLOY_RUNBOOK.md` — §G.4 kontrol 10 + elle kurma notu → doğrulama notu
- `Docs/DEFERRED_BACKLOG.md` — `T133b-PaymentMonitorUnarmed` ✅ (45 → 44), düzeltme turunda `T139-ActiveMonitorQuotaAlarm` açıldı (44 → **45**)
- `Docs/IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| AC1 | Port `StartMonitoringAsync` + `StopMonitoringAsync` taşır, gövde sidecar'ın beş zorunlu alanıyla birebir | ✓ | `IBlockchainSidecarClient.cs` + `HttpBlockchainSidecarClient.cs`; `MonitorStartRequestBody` alanları `startMonitorHandler`'ın (`monitorHandlers.ts:33-38`) zorunlu beşlisiyle birebir; `PaymentMonitorStartDispatcherTests.Success_Arms_The_Sidecar_With_The_Mapped_Payload` alan alan doğruluyor |
| AC2 | Arm, geçişle aynı `SaveChanges` içinde; **dispatcher olayı tüketip sidecar'ı kurar**; adres yoksa geçiş bloklanmaz | ✓ *(düzeltme turundan sonra)* | Yayın kolu: `TransactionReadinessService` Stage 10b; `Arming_Rides_The_Same_Unit_Of_Work_As_The_Transition` (2 olay, tek batch) + `A_Missing_Deposit_Address_Does_Not_Block_The_Confirmation`. **Tüketim kolu yapım turunda BAĞLI DEĞİLDİ** (doğrulama bulgusu B1) — `TransactionsModule.cs`'e DI kaydı eklendi, `TransactionsModuleNotificationHandlerTests` bekçisi kaydın varlığını zorunlu kılıyor |
| AC3 | Self-heal: **kurulu olması gereken kümeyi her turda** idempotent olarak yeniden kurar | ✓ *(düzeltme turu 2'den sonra)* | `EnsurePaymentMonitorJob` (`Cron = "* * * * *"`) + `EnsurePaymentMonitorJobRegistrar`; `Arming_Is_Repeated_Every_Run_So_A_Sidecar_Restart_Self_Heals`. **Kriterin "kümeyi" kısmı tur 2'ye kadar KARŞILANMIYORDU** (doğrulama bulgusu B1-2): tek `Take(200)` + `CreatedAt` artan sıra, kümeyi 200'ün üstünde en yeni pencerelere hiç ulaşamaz hâle getiriyordu. Sayfalamaya çevrildi; `Every_Open_Window_Is_Armed_Even_Past_One_Page` + `A_Closed_Window_Past_The_First_Page_Is_Still_Disarmed` sayfa sınırının üstünü kapsıyor |
| AC4 | Disarm: (a) iptal devrinde stop→start sırası, (b) pencere kapanışında stop + `STOPPED` | ✓ | (a) `Handover_Stops_The_Active_Monitor_Before_Starting_PostCancel` çağrı **sırasını** assert eder; (b) `Terminal_Status_Stops_The_Monitor_And_Stamps_Stopped` + `A_Swept_Deposit_Is_Disarmed_While_The_Transaction_Is_Still_Live` |
| AC5 | Birim + entegrasyon kapsaması — **port metotları (statü eşlemesi dahil)** + dispatcher 3 kol + atomik yayın + karar tablosu | ✓ *(düzeltme turu 2'den sonra)* | 5 yeni test dosyası; aşağıdaki test tablosu. **Port yarısı tur 2'ye kadar EKSİKTİ** (doğrulama bulgusu N1-2): `HttpBlockchainSidecarClientTests` yalnız `DeriveAddressAsync`'i kapsıyordu, `api/monitor/start|stop` yolu / alan adları / `SendCommandAsync` statü eşlemesi hiç koşmuyordu. 7 test eklendi, post-cancel ikizlerinin yollarını da kapsıyor |
| AC6 | Doküman: 08 §3.4 yaşam döngüsü, 06 §3.7/§2.16 `ACTIVE` anlamı, runbook `curl` notu kalkar, backlog ✅ | ✓ | Yukarıdaki doküman listesi |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build (Release) | ✓ 0W / 0E | `dotnet build -c Release` |
| `dotnet format` | ✓ exit 0 | `dotnet format --verify-no-changes --no-restore` — 0 bulgu |
| **Tüm backend suite** | ✓ **2817/2817** | 13 assembly, **0 fail** — Shared 409 · Platform 185 · Users 22 · Auth 120 · Steam 39 · Payments 6 · Realtime 39 · Admin 22 · Fraud 91 · Notifications 171 · Disputes 83 · Transactions 1079 · API 551 |
| Yeni testler | ✓ 44/44 | Üç yeni dosya (`Unit.PaymentMonitoring` + `Unit.PostCancel` + `Integration.PaymentMonitoring`); ayrıca `TransactionReadinessServiceTests`'e **3** test eklendi → toplam **47** yeni test |
| **Düzeltme turu (validator koşumu)** | ✓ | Build Release **0W/0E** · `dotnet format --verify-no-changes` exit 0 · **Unit 1437/1437** (API 46 → **47**, yeni DI bekçisi) · `Integration.PaymentMonitoring` + `Integration.Lifecycle.TransactionReadinessServiceTests` **50/50** · üç yeni testin **üçü de ayırt edici** olduğu, düzeltmeler geçici olarak devre dışı bırakılarak kanıtlandı |
| **Düzeltme turu 2 (validator koşumu)** | ✓ | Build Release **0W/0E** · `dotnet format --verify-no-changes --severity error` exit 0 · **Transactions entegrasyon 538/538** (536 → +2 sayfa-aşımı testi) · **Transactions unit 559** (545 → +14 port testi: 6 fact + 8 `Theory` vakası) · **sidecar-blockchain 166/166** (161 → +5 gauge testi) · `npx tsc --noEmit` exit 0 · prettier LF-normalize edilmiş kopyada temiz (lokal `--check` 38 dosyanın **34**'ünü uyarıyor, dokunulmamışlar dahil → `core.autocrlf` artefaktı) · **üç düzeltmenin üçü de ayırt edici**, geçici olarak devre dışı bırakılarak kanıtlandı |
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

## Doğrulama — Tur 2 (2026-08-20, bağımsız doğrulama chat'i)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✗ **FAIL** → düzeltme turu 2 **aynı dalda uygulandı** (proje sahibi kararı) |
| Bulgu sayısı | **3** — 1 bloke edici (B1-2) + 2 bloke etmeyen (N1-2, N2-2) |
| Düzeltme gerekli mi | Evet — üçü de kapatıldı, kayıtları plan §F7 T139 "DÜZELTME TURU 2" bloğunda |

**Giriş kapıları:** working tree temiz ✓ · main son 3 run `success` (`32352581013`, `32352580901`, `32248307699`) ✓ · repo memory T139 satırı mevcut ✓ · dal HEAD `51b1552` ↔ origin senkron ✓

**Bağımsız olarak yeniden üretilenler:** AC1 (beş zorunlu alan ↔ gövde birebir, `expectedSymbol` = enum adı ↔ sidecar allowlist, `expectedContract` iki token için de dolu) · AC2 (`OutboxService.PublishAsync` yalnız change tracker'a ekliyor → atomiklik yapısal; Stage 10b tek `SaveChangesAsync` öncesinde) · AC4 (a+b) · AC6 (runbook'un üç komutu da koşulabilir) · tur 1'in üç düzeltmesinin **üçünün de ayırt ediciliği** (DI bekçisi kaldırıldığında `Unregistered: PaymentMonitorStartRequestedEvent`; devir telafisi kapalıyken `Assert.Single() Failure`; concurrency ele alımı kapalıyken ham `DbUpdateConcurrencyException`) · Transactions entegrasyon **536/536** lokal · sidecar **161/161** lokal · advisory E2E **10/32**, dal HEAD run `32379774377` ↔ `32264946887` (T133) ↔ `32246467184` (T132) **leg-leg birebir**.

### B1-2 — BLOKE EDİCİ (AC3'ün özü): reconciler tek sayfayla sınırlıydı, en yeni pencereler aç kalıyordu

`ExecuteAsync` adayları tek bir `Take(BatchSize = 200)` ile `CreatedAt` **artan** sırada alıyordu. Tavan `EnsurePaymentAddressJob`'dan kopyalanmıştı ve orada güvenlidir çünkü o kümenin satırları işlendikçe **düşer** (`t.PaymentAddress == null` artık doğru değil). Burada arm satırı `ACTIVE` bıraktığı için satır pencerenin tamamı boyunca aday kalır — küme 200'ü aştığı anda her tur aynı en eski dilimi mutabık kılıyor.

Kümenin büyüklüğünü bu görevin **kendi N1 bulgusu** ölçmüştü: `ArmedStates` `ITEM_DELIVERED`'ı içeriyor ve sweep 7 günlük tabandan önce kuyruklanamıyor → küme ≈ **bir haftalık hacim**, eşik ~29 işlem/gün. Aç kalan popülasyon tam olarak para-kritik olan: en yeni `SELLER_CONFIRMED` satırı sıranın **sonunda** doğuyor ve önündeki (küme−200) satır drene olana kadar (100 tx/gün'de ≈ 5 gün) işlenmiyor — 30-120 dakikalık ödeme penceresi için pratikte **hiçbir zaman**. AC3'ün kapattığı üç vakadan ikisi (sidecar restart · düşen outbox teslimi) tam da hızlı yolun çalışmadığı anda ölüyordu. `BatchSize` XML doc'u ise tersini iddia ediyordu ("idle allocations ... can never crowd armed windows out of the batch") — başka bir kalabalıklaşma kaynağını adlandırıp geçerli olanı atlıyordu.

Validator geçici bir sondaj testiyle **yeniden üretti**: 201 aktif satır → 200 arm, en yeni `SELLER_CONFIRMED` penceresi armed değil.

**Kapatma:** tek `Take` yerine **tüm aday kümesi sayfalanıyor** (`PageSize` = DB gidiş-dönüş boyu, `MaxAddressesPerRun = 5000` wedge guard). Tavan bir throughput ayarı değil kama korumasıdır ve çarpıldığında **WARN loglanıyor** — sessizce kırpılmış bir sweep tam kapsama gibi okunur. Bedeli düşük: izleyici başına **dakikada** bir `start`, sidecar'ın aynı adres için zaten 3 saniyede bir yaptığı işin ~%0.5'i. İki kalıcı bekçi testi (`Every_Open_Window_Is_Armed_Even_Past_One_Page`, `A_Closed_Window_Past_The_First_Page_Is_Still_Disarmed`) sayfa sınırının üstünü kapsıyor ve **ikisi de ayırt edici** (tek sayfaya kilitlendiğinde `Assert.Equal() Failure: Values differ` ve `Assert.Single() Failure: The collection was empty`).

### N1-2 — bloke etmez: AC5'in port yarısı ölçülmemişti

AC5 "kurma/durdurma port metotları (**statü eşlemesi dahil**)" diyor, ama `HttpBlockchainSidecarClientTests` yalnız `DeriveAddressAsync`'i kapsıyordu: `api/monitor/start|stop` **yolu**, JSON **alan adları** ve `SendCommandAsync`'in dört yönlü **statü eşlemesi** hiçbir testte koşmuyordu. AC5'in "post-cancel ikizlerinin test dosyaları şablondur" cümlesi yanıltıcıydı — o ikizlerin de port testi yoktu, yani şablon boştu. Eksik olan tam olarak AC1'in konusu: bir alan adı veya yol sapması sidecar'da 400 `INVALID_MONITOR_REQUEST` üretir, `PaymentMonitorStartDispatcher` 400'ü **terminal** sayar ve ödeme bacağı sessizce ölür.

**Kapatma:** yol + beş alan + `api/monitor/stop` gövdesi + post-cancel ikizlerinin yolları + sekiz HTTP statüsünün eşlemesi + transport/timeout/internal-key testleri. Ayırt edicilik kanıtlandı: yol `api/monitor/starts` yapıldığında ve `expectedContract` → `expected_contract` yeniden adlandırıldığında test her iki sapmayı da yakalıyor.

### N2-2 — bloke etmez: N1'in telafisinin dayandığı gösterge yanlıştı

`skinora_blockchain_active_monitors` etiketsiz **tek** bir gauge ve iki registry (`MonitorRegistry` + `PostCancelMonitor`) ikisi de ona `.set(this.monitors.size)` yazıyordu — yayınlanan değer toplam değil **en son yazan** registry'nin sayısıydı; üstelik birinin `shutdown()`'ı diğeri hâlâ yoklarken düz 0 yayınlıyordu. Kusur T71/T75'ten geliyor, T139 üretmedi — ama **taşıyıcı hâle getiren T139'dur**: N1'in telafisi (08 §3.4 "throughput artırılmadan önce bu gauge izlenmeli"), `DEPLOY_RUNBOOK` §G.4'ün kurulum kanıtı, `infra/grafana/.../integration-metrics.json:404` paneli ve `T139-ActiveMonitorQuotaAlarm` hepsi bu sayıya bağlandı.

**Kapatma:** metrik **adı ve (boş) etiket kümesi korundu** — mevcut Grafana paneli dokunulmadan doğru sayıyı çizmeye başlıyor — ve iki registry tek bir toplayıcıdan (`sidecar-blockchain/src/monitor/activeMonitorGauge.ts`) yazıyor. Post-cancel izleyiciler de aynı TronGrid bütçesini tükettiği için toplam zaten kapasite planlamasının istediği sayı. Beş test; registry seviyesindeki ikisi **ayırt edici** (eski yazım geri alındığında `expected 1 to be 2` ve `expected +0 to be 1`).

### Düzeltme turu 2'nin değişiklikleri

- `.../PaymentMonitoring/EnsurePaymentMonitorJob.cs` — `BatchSize` → `PageSize` + `MaxAddressesPerRun`, sayfalama döngüsü, tavan WARN'ı (B1-2)
- `.../Integration/PaymentMonitoring/EnsurePaymentMonitorJobTests.cs` — **2 yeni** sayfa-aşımı testi + `SeedAddressAsync`'e `createdAt` parametresi
- `.../Unit/PaymentAddresses/HttpBlockchainSidecarClientTests.cs` — **7 yeni** port testi (N1-2)
- `sidecar-blockchain/src/monitor/activeMonitorGauge.ts` + `.test.ts` — **yeni** toplayıcı + 5 test (N2-2)
- `sidecar-blockchain/src/monitor/MonitorRegistry.ts` · `PostCancelMonitor.ts` · `src/metrics.ts` — gauge yazımı toplayıcıya alındı, `help` metni düzeltildi
- `Docs/11_IMPLEMENTATION_PLAN.md` §F7 T139 **DÜZELTME TURU 2** bloğu · `Docs/08_INTEGRATION_SPEC.md` v3.4 → **v3.5** · `Docs/DEFERRED_BACKLOG.md` (satır sayısı **değişmedi**, 45)

### Düzeltme turu 2 CI kanıtı

Dal HEAD `111976c` · run [`32399382402`](https://github.com/turkerurganci/Skinora/actions/runs/32399382402) **`success`**, **`CI Gate` yeşil** — on bloke edici job'ın onu da başarılı (`1. Lint` · `2. Build` · `3. Unit test` · `3b. JS test (vitest)` · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` · `7. Docker build (backend)` · `7. Docker build (sidecar-blockchain)` · `CI Gate`); `0. Guard (direct push)` skipped (beklenen).

Advisory E2E **10/32** — geçen 1+3+6, düşen 22; tur 1'in run'ı (`32379774377`) ve iki bağımsız T139-öncesi tabanla (`32264946887` T133 · `32246467184` T132) **birebir**. Tur 2 hem reconciler'ın sorgu şekline hem sidecar'ın iki registry'sine dokunduğu hâlde ağda ne kazanç ne kayıp var.

### KALICI DERS (tur 2)

**Bir tavan, üzerinde durduğu kümenin DRENE OLUP OLMADIĞI sorulmadan kopyalanamaz.** `EnsurePaymentAddressJob`'un `Take(50)`'si doğru, `EnsurePaymentMonitorJob`'un `Take(200)`'ü yanlıştı ve iki satır birbirinin aynısıydı — fark koddan değil, kümenin davranışından geliyor. İkinci ders aynı turun içinden: **bir kararın bedelini bir metriğe havale etmek, o metriğin doğru olduğunu ayrıca doğrulamayı gerektirir** — N1 telafisi yazılırken gösterge zaten iki registry tarafından eziliyordu.

## Doğrulama — Tur 3 (2026-08-20, bağımsız doğrulama chat'i)

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — görev kapandı |
| Bulgu sayısı | **1** — 0 bloke edici + 1 bloke etmeyen (N1-3), proje sahibi kararıyla aynı dalda kapatıldı |
| Düzeltme gerekli mi | Hayır (N1-3 bloke etmez; yine de kapatıldı) |

**Giriş kapıları:** working tree temiz ✓ · main son 3 run `success` (`32361465502`, `32361465451`, `32352581013`) ✓ · repo memory T139 satırı mevcut ✓ · dal HEAD `2ec7762` ↔ origin senkron ✓

### Bağımsız olarak yeniden üretilenler (6/6 kabul kriteri ✓)

- **AC1** — `MonitorStartRequestBody`'nin beş alanı ↔ `startMonitorHandler`'ın zorunlu beşlisi birebir; `expectedSymbol` enum **adı** olarak gidiyor ve `StablecoinType` (USDT/USDC) sidecar'ın `ALLOWED_SYMBOLS` allowlist'iyle örtüşüyor; yol `api/monitor/start|stop` ↔ `router.use('/api', apiRouter)` mount'u.
- **AC2** — `OutboxService.PublishAsync` yalnız change tracker'a `Add`liyor, `SaveChanges` **çağırmıyor** → Stage 10b'nin atomikliği yapısal olarak doğru. Ayrıca `SellerConfirmReady` trigger'ının repo'da **tek** çağıranı var (`TransactionReadinessService:243`), yani arm noktası eksiksiz — `SELLER_CONFIRMED`'a giren ikinci bir yol yok.
- **AC3 / AC4 / AC5 / AC6** — hepsi kanıtla doğrulandı; runbook §G.4'ün üç gözlem komutunun üçü de koşulabilir (`'Monitor started'` log metni gerçek, `/metrics` auth'suz `routes.ts:40`, `EnsurePaymentMonitorJob complete: ... armed=` formatı doğru).

### Planın hiçbir yerinde yazılı olmayan bir sıralama invariant'ı ölçüldü — ve DOĞRU çıktı

`EnsurePaymentMonitorJob`'ın `WindowClosedStates` yorumu "CANCELLED_* satırları normalde `PostCancelMonitorStarter` üzerinden ACTIVE'den çıkar ve bu job'a hiç gelmez" diyor. Bu bir **varsayım**dır ve yanlış olsaydı para ilgili olurdu: iptal geçişi önce commit edilip post-cancel damgası ayrı bir transaction'a kalsaydı, arada koşan reconciler `Disarm` sınıflandırıp satırı `STOPPED` damgalardı — ve `PostCancelMonitorStarter`'ın `STOPPED` guard'ı (satır 61-68) yüzünden gecikmeli izleme **hiç kurulmazdı**. Dört çağıranın (`TransactionCancellationService:216`, `AdminTransactionService:182` + `:564`, `TimeoutExecutor:82`, `DeadlineScannerJob:155`) **dördünde de** `RequestStartAsync` iptal geçişini commit eden `SaveChanges`'ten **önce** duruyor (timeout kolları ayrıca açık `BeginTransactionAsync` içinde), yani `CANCELLED_* + ACTIVE` bileşimi hiç commit edilmiyor. Varsayım korunuyor.

Aynı sınıftan ikinci bir kontrol: `FLAGGED` yalnız oluşturma anında set ediliyor (`TransactionStateMachine:304`, `SELLER_CONFIRMED → FLAGGED` yolu yok), yani `WindowNotOpenStates` gruplaması doğru — armed bir pencere `FLAGGED`e düşüp yeniden kurulamaz hâle gelemiyor.

### Tur 1 + tur 2 düzeltmelerinin ayırt ediciliği — beşi de validator tarafından yeniden kanıtlandı

| Düzeltme | Geçici olarak bozulduğunda |
|---|---|
| B1 DI kaydı | `Unregistered: PaymentMonitorStartRequestedEvent` |
| B1-2 sayfalama | **Yalnız** o 2 sayfa-aşımı testi düştü (diğer 19 geçti) → hedefli |
| N2 devir telafisi | `An_Arm_That_Raced_A_Cancel_Handover_Is_Undone` → `Assert.Single() Failure` |
| N1-2 port yolu | `Assert.Equal() Failure: Strings differ` |
| N2-2 gauge toplamı | `expected 1 to be 2` + `expected +0 to be 1` |

### N1-3 — bloke etmez: B1'in kapattığı SINIFIN kalan yarısı

B1'in kararı "tek örneği değil **sınıfı** kapat" diyordu, ama bekçi sınıfın yalnız bir yarısını ölçüyordu. `TransactionsModuleNotificationHandlerTests` `declared` kümesini handler **tipleri** değil `INotificationHandler<T>` **arayüz tipleri** üzerinden kurup `.Distinct()` uyguluyordu; zaten kayıtlı bir olay tipi için eklenen **ikinci** bir handler aynı arayüz girdisine çöktüğü için bekçi yeşil kalıyor, oysa o handler MediatR'da hiç çözülmez — B1'in kusurunun aynısı.

**Sondajla üretildi:** assembly'ye `PaymentMonitorStartRequestedEvent` için ikinci bir kayıtsız handler eklendi → test **geçti** (`Passed: 1, Failed: 0`).

**Kapatma:** karşılaştırma **sayıma** çevrildi — (a) bir olay tipi için bildirilen handler sayısı, o tipin `INotificationHandler<T>` kayıt sayısını aşamaz; (b) her handler'ın **kendi somut tipi** de kayıtlı olmalı (modülün kalıbı arayüzü somut tipe yönlendiriyor; eksik somut satır fabrikayı resolve anında patlatır). Yeni test **yok**, mevcut bekçi yeniden yazıldı → Unit sayısı 1466'da sabit.

**Ayırt edicilik iki yönde de kanıtlandı:**
- İkinci kayıtsız handler → `PaymentMonitorStartRequestedEvent: 2 handler(s) declared (PaymentMonitorStartDispatcher + ValidatorProbeHandler) but only 1 INotificationHandler<> registration(s) | ValidatorProbeHandler: the concrete type itself is not registered`
- B1'in orijinal kusuru (kayıt tamamen kaldırıldı) → `1 handler(s) declared (PaymentMonitorStartDispatcher) but only 0 INotificationHandler<> registration(s)` — **regresyon yok**, eski vaka hâlâ yakalanıyor.

### Tur 3'ün değişiklikleri

- `backend/tests/Skinora.API.Tests/Unit/Configuration/TransactionsModuleNotificationHandlerTests.cs` — bekçi sayım tabanlı yeniden yazıldı (N1-3)
- `Docs/11_IMPLEMENTATION_PLAN.md` §F7 T139 **DÜZELTME TURU 3** bloğu + başlık girişi

### Tur 3 test kanıtı (validator'ın kendi koşumu, dal HEAD `2ec7762`)

Build Release **0W / 0E** · `dotnet format --verify-no-changes --severity error` exit **0** · **Unit 1466/1466** · **Transactions entegrasyon 538/538** (4 dk 56 sn) · **sidecar-blockchain 166/166** · doğrulanan dal HEAD `2ec7762` CI run [`32402092945`](https://github.com/turkerurganci/Skinora/actions/runs/32402092945) **`success`** · N1-3 düzeltmesinden sonra dal HEAD `4d1b515` CI run [`32407918580`](https://github.com/turkerurganci/Skinora/actions/runs/32407918580) **`success`**, `CI Gate` yeşil.

Advisory E2E **10/32** — validator HEAD run'ından bağımsız saydı (geçen 1+3+6, düşen 22), T139-öncesi tabanla aynı → **regresyon yok**. (Sekiz advisory leg'in T117'den beri düşmesi T138'in sahipliğinde, bu görevin kapsamı dışında.)

### Güvenlik

Secret sızıntısı **temiz** (yeni sır yok; mevcut `X-Internal-Key` mekanizması) · Auth etkisi **yok** (yeni endpoint yok; sidecar uçları zaten `internalKeyAuth` arkasında) · Input validation **etkisiz** (kullanıcı girdisi taşımıyor) · Yeni dış bağımlılık **yok** · Migration **yok**.

### Yapım raporu karşılaştırması

**Tam uyumlu** — uyuşmazlık yok. Raporun bildirdiği her sayı (Unit 1466 · Transactions entegrasyon 538 · sidecar 166 · build 0W/0E) validator'ın kendi koşumuyla birebir eşleşti.

### KALICI DERS (tur 3)

**Bir bekçinin KAPSAMI da kendisi kadar denetlenmelidir.** "Sınıfı kapattım" diyen bir test, sınıfın hangi yarısını ölçtüğü ayrıca sorulmadıkça kapattığını sanılan şeyi kapatmamış olabilir — B1 tam olarak "tek örneği düzeltmek yetmez" diyerek bir bekçi yazdırmıştı ve bekçinin kendisi tek örneği kapatıyordu. Tur 2'nin dersinin ikizi: orada kopyalanan **sabit** yanlış kümeye oturmuştu, burada kopyalanan **ölçüm ekseni**.

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
- **Reconciler'ın tur başına 5000 adres tavanı var** (`MaxAddressesPerRun`, düzeltme turu 2). Tavan bir kama korumasıdır, throughput ayarı değil, ve çarpıldığında **WARN loglanır** — sessizce kırpılmaz. Pratikte ulaşılamaz olması beklenir: 5000 eşzamanlı izleyicide sidecar zaten saniyede ~3300 TronGrid sorgusu üretiyor olur, yani `T139-ActiveMonitorQuotaAlarm` çok önce ateşlenmelidir. Tavanın kendisi test edilmedi (5001 satır seed etmek süiti gereksiz yavaşlatırdı); test edilen şey **sayfa sınırının aşılması**dır.
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

**Dört tur, dört yeşil CI** (her turun kendi dal HEAD'i üzerinde):

| Tur | Dal HEAD | Run | Sonuç |
|---|---|---|---|
| Yapım | `3ad44f7` | [`32367284135`](https://github.com/turkerurganci/Skinora/actions/runs/32367284135) | ✓ `success` |
| Düzeltme 1 | `ad613c8` | [`32377529035`](https://github.com/turkerurganci/Skinora/actions/runs/32377529035) | ✓ `success` |
| Düzeltme 2 | `111976c` | [`32399382402`](https://github.com/turkerurganci/Skinora/actions/runs/32399382402) | ✓ `success` |
| Düzeltme 3 (N1-3) | `4d1b515` | [`32407918580`](https://github.com/turkerurganci/Skinora/actions/runs/32407918580) | ✓ `success` |

**Tur 3 (N1-3) CI kanıtı:** dal HEAD `4d1b515` · run [`32407918580`](https://github.com/turkerurganci/Skinora/actions/runs/32407918580) **`success`**, **`CI Gate` yeşil** — on bloke edici job'ın onu da başarılı (`1. Lint` · `2. Build` · `3. Unit test` · `3b. JS test (vitest)` · `4. Integration test` · `5. Contract test` · `6. Migration dry-run` · `7. Docker build (backend)` · `7. Docker build (sidecar-blockchain)` · `CI Gate`); `0. Guard (direct push)` skipped (beklenen). Advisory E2E **10/32** (geçen 1+3+6, düşen 22) — bir önceki dal HEAD run'ı `32402092945` ile **birebir**, yani bekçinin yeniden yazımı ağa dokunmadı.

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
