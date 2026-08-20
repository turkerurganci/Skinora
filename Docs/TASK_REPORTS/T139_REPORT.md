# T139 — Ödeme izleyicisinin bağlanması (arm / re-arm / disarm)

**Faz:** F7 | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-08-20

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
2. **Kurma (fast path)** — `TransactionReadinessService` `ACCEPTED → SELLER_CONFIRMED` geçişiyle **aynı** `SaveChangesAsync` içinde `PaymentMonitorStartRequestedEvent` yayınlıyor; `PaymentMonitorStartDispatcher` olayı tüketip sidecar'ı kuruyor.
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
- `backend/src/Skinora.API/Configuration/TransactionsModule.cs` — job + registrar kaydı
- `sidecar-blockchain/src/monitor/MonitorRegistry.ts` — sınıf yorumu (emekli `PENDING_PAYMENT (T44 state)` çağıranını tarif ediyordu)
- Dört test stub'ı (`StubBlockchainSidecarClient` + üç `Skinora.API.Tests` stub'ı) yeni arayüz üyeleriyle

**Değişen (doküman):**
- `Docs/11_IMPLEMENTATION_PLAN.md` — §F7 P7'ye T139 bloğu; F7 aralığı T115–T138 → T115–T139
- `Docs/06_DATA_MODEL.md` v6.12 → **v6.13** — §2.16 + §3.7
- `Docs/08_INTEGRATION_SPEC.md` v3.2 → **v3.3** — §3.4 yaşam döngüsü tablosu (altbilgi sürümü de düzeltildi: v2.6 → v3.3)
- `Docs/DEPLOY_RUNBOOK.md` — §G.4 kontrol 10 + elle kurma notu → doğrulama notu
- `Docs/DEFERRED_BACKLOG.md` — satır ✅, 45 → **44** aktif satır
- `Docs/IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| AC1 | Port `StartMonitoringAsync` + `StopMonitoringAsync` taşır, gövde sidecar'ın beş zorunlu alanıyla birebir | ✓ | `IBlockchainSidecarClient.cs` + `HttpBlockchainSidecarClient.cs`; `MonitorStartRequestBody` alanları `startMonitorHandler`'ın (`monitorHandlers.ts:33-38`) zorunlu beşlisiyle birebir; `PaymentMonitorStartDispatcherTests.Success_Arms_The_Sidecar_With_The_Mapped_Payload` alan alan doğruluyor |
| AC2 | Arm, geçişle aynı `SaveChanges` içinde; adres yoksa geçiş bloklanmaz | ✓ | `TransactionReadinessService` Stage 10b; `Arming_Rides_The_Same_Unit_Of_Work_As_The_Transition` (2 olay, tek batch) + `A_Missing_Deposit_Address_Does_Not_Block_The_Confirmation` |
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
| Yeni + komşu testler | ✓ 75/75 | Yukarıdaki filtre + `TransactionReadinessServiceTests` sınıfının tamamı |
| sidecar-blockchain tsc | ✓ exit 0 | `npx tsc --noEmit` |
| sidecar-blockchain prettier | ✓ (LF) | Lokal `--check` **tüm** pakette uyarıyor (`core.autocrlf=true` artefaktı — dokunulmamış `routes.ts`/`monitorHandlers.ts` de uyarıyor); LF'e normalize edilmiş kopya **temiz** |

> **Suite'i sıralı koşturmak gerekti — bu bir bulgu değil, lokal ortam sınırı.** Solution kökünde `dotnet test` assembly'leri paralel koşturuyor ve her biri kendi veritabanını yarattığı için lokal SQL Server tükeniyor: ilk denemede 5 assembly `CREATE DATABASE failed. Some file names listed could not be created.` + `SSL Provider ... transport-level error` ile düştü (582 "fail"). Assembly'ler **tek tek** koşturulduğunda aynı binary'lerle **13/13 yeşil**. İmza tamamen altyapısaldır (hiçbir assertion hatası yok) ve CI kendi SQL container'ıyla koştuğu için orada görülmez.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | Bekliyor (ayrı chat) |
| Bulgu sayısı | — |
| Düzeltme gerekli mi | — |

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
- **`skinora_blockchain_active_monitors` üzerinde alarm yok.** Kurulmamış bir pencere bugün yalnız log'dan görülür; bir "armed pencere sayısı ≠ açık pencere sayısı" alarmı bunu gözlenebilir yapardı — T139 kapsamına alınmadı.
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
- Commit: (aşağıda)
- PR: (aşağıda)
- CI: (aşağıda)
