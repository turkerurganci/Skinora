# T140 — `DELIVERY_EXPECTED` bildiriminin yayıncısının bağlanması

**Faz:** F7 (P2P Geçişi) | **Durum:** ⏳ Devam ediyor (yapım bitti, doğrulama bekliyor) | **Tarih:** 2026-08-22

---

## Yapılan İşler

- **AC1 — Yayıncı bağlandı.** `AmountValidationService.AdvanceStateMachineAsync` artık `ConfirmPayment` geçişiyle **aynı** `SaveChangesAsync` içinde `TransactionStatusChangedEvent(SELLER_CONFIRMED → PAYMENT_RECEIVED)` yayınlıyor. Bu, `HappyPathMilestoneNotificationConsumer`'ın `PAYMENT_RECEIVED` kolunun eksik olan tek parçasıydı; kol T118'den beri v3.0'a göre yazılıydı ama erişilemezdi.
- **Kök neden düzeltildi (planda AC değil — yapım turunda ölçüldü).** `TransactionStatusChangedEvent`'in XML doc'unun çelişkili iki paragrafı hizalandı; ödeme onayı artık "bu olayı yayınlamaz" listesinden çıkarıldı ve **neden** kasıtlı istisna olduğu, **neden** eski kuralın custody dönemine ait olduğu dosyada kayıtlı.
- **AC3 — Realtime çift-push'u sahiplikle çözüldü.** `PaymentReceivedRealtimeConsumer`'dan `PublishStatusChangedAsync` bloğu kaldırıldı; sınıf saf `PaymentConfirmed` push'una indi ve hardcoded `FromStatus: SELLER_CONFIRMED` kaldıracı onunla birlikte silindi. Status push'un sahibi artık `TransactionStatusChangedRealtimeConsumer`'ın verbatim relay'i.
- **AC2 — İki tarafta da kanıtlandı.** Backend tarafında alıcının bu bildirimi almadığı açıkça assert edildi; E2E tarafında iddia **tipten alıcıya yükseltildi** (yeni `getNotificationRecipients` yardımcısı).
- **AC4 — Kendini kapatan işaret çevrildi.** `happy-path.smoke.spec.ts`'in gap marker bloğu silindi, `DELIVERY_EXPECTED` `EXPECTED_NOTIFICATIONS`'a döndü ve yerine satıcı-alıcı iddiası kondu.
- **Negatif kollar kilitlendi.** Durum ilerlemeyen her yolda (eksik ödeme, emergency-hold) olayın yayınlanmadığı ayrı ayrı assert edildi.

## Etkilenen Modüller / Dosyalar

**Production kaynağı (3 dosya):**
| Dosya | Değişiklik |
|---|---|
| `backend/src/Modules/Skinora.Transactions/Application/Webhooks/AmountValidationService.cs` | `AdvanceStateMachineAsync` ikinci outbox yayını (AC1) |
| `backend/src/Skinora.Shared/Events/TransactionStatusChangedEvent.cs` | XML doc — çelişkili kuralın düzeltilmesi (kök neden) |
| `backend/src/Modules/Skinora.Realtime/Application/EventHandlers/PaymentReceivedRealtimeConsumer.cs` | `PublishStatusChangedAsync` bloğu + hardcoded `FromStatus` kaldırıldı (AC3) |

**Test (3 dosya):**
| Dosya | Değişiklik |
|---|---|
| `backend/tests/Skinora.Transactions.Tests/Unit/Webhooks/AmountValidationServiceTests.cs` | 3 yeni test + 2 mevcut testte negatif kol assertion'ı |
| `backend/tests/Skinora.Realtime.Tests/Unit/RealtimeConsumerTests.cs` | 1 test yeniden yazıldı + 1 yeni çapraz-tüketici testi |
| `backend/tests/Skinora.Notifications.Tests/Integration/HappyPathNotificationConsumerTests.cs` | AC2 alıcı-negatif assertion'ı |

**E2E (2 dosya):**
| Dosya | Değişiklik |
|---|---|
| `e2e/src/db.ts` | Yeni `getNotificationRecipients(type)` yardımcısı |
| `e2e/tests/happy-path.smoke.spec.ts` | Gap marker → alıcı iddiası; `DELIVERY_EXPECTED` listeye döndü (AC4) |

**Doküman (4 dosya):** `Docs/11_IMPLEMENTATION_PLAN.md` (§F7 T140 bloğu: ÖNERİ → onaylı + yapım turu), `Docs/DEFERRED_BACKLOG.md`, `Docs/IMPLEMENTATION_STATUS.md`, `.claude/memory/MEMORY.md`.

**Migration / config / docker değişikliği: YOK.**

---

## Ölçüm — açık bağımsız yeniden üretildi

Yapım turu T138 raporuna güvenmeden ölçtü:

```
$ grep -rn "new TransactionStatusChangedEvent(" --include=*.cs backend/src/
DeliveryConfirmationService.cs:233 · DeliveryDisputeRound.cs:184 · DeliveryTimeoutRound.cs:254
TransactionReadinessService.cs:285 · SettlementVerificationJob.cs:384
```

Beş yayıncı, **hiçbiri ödeme onayı yolu değil**. Tek `→ PAYMENT_RECEIVED` üreticisi `AmountValidationService.AdvanceStateMachineAsync` yalnız `PaymentReceivedEvent` yayınlıyordu (`AmountValidationService.cs:521`). Açık gerçek. ✓

---

## Kök neden — turun asıl getirisi

Açık **unutkanlık değildi**. `TransactionStatusChangedEvent`'in XML doc'u kendisiyle çelişiyordu:

| | Ne diyordu | Sonucu |
|---|---|---|
| **Paragraf 1** | *"Transitions that already raise a specific domain event ... do NOT publish this event, so no double-push occurs: ... **the payment confirmation (`PaymentReceivedEvent`)** ..."* | Ödeme onayı yolunu **adıyla** yasaklıyordu |
| **Paragraf 2** | *"the P2P producers for those two legs are written in T123 (`seller_confirm_ready`) and **T124 (`confirm_payment`)**"* | Aynı yolu üretici olarak **görevlendiriyordu** |

T123 kendi bacağını bağladı; T124 bağlamadı — **yazılı talimata uydu**.

Kural custody döneminde **doğruydu**: o çağda bu bacağın bildirimi `TRADE_OFFER_SENT_TO_BUYER` idi, bot dispatch job'ından geliyordu ve bu olay yalnız realtime badge taşıyordu. v3.0 bildirimi olayın üstüne taşıdı ve alıcısını satıcıya çevirdi; kural bayat kaldı, çelişkisi dört görev boyunca okunmadan yaşadı.

**Neden bu düzeltme kapsamın parçası:** yalnız yayıncıyı bağlayıp paragrafı bırakmak açığı **yeniden üretilebilir** hâlde bırakırdı — bir sonraki okuyucu aynı talimatı görüp aynı kararı verirdi. Paragraf düzeltildi, ödeme onayının **kasıtlı istisna** olduğu ve neden kuralın değiştiği dosyada kayıtlı.

Bu, T138 doğrulamasının B1 dersinin kardeşidir: T138 bir *kapatma iddiasının* kanıt komutunun kapsamı kadar doğru olduğunu ölçtü; T140 bir *bağlanmama kararının* onu emreden yorumun güncelliği kadar doğru olduğunu ölçtü. İkisi de aynı yere çıkıyor — **sahipsiz yorum ucuz değildir**.

---

## AC3 — çift-push varsayımsal değildi

`PaymentReceivedRealtimeConsumer` **zaten** `TransactionStatusChanged(SELLER_CONFIRMED → PAYMENT_RECEIVED)` push ediyordu, `FromStatus` hardcoded; gerekçesi kendi yorumunda yazılıydı: *"the state-machine guard ... means the pre-transition state can be hardcoded."* Bu bir kaldıraçtı — generic olay yayınlanmadığı için vardı. `RealtimeConsumerTests`'in `PaymentReceived_PushesPaymentConfirmed_Then_StatusChanged` testi davranışı pinliyordu.

AC1 tek başına uygulansaydı generic relay **birebir aynı payload'ı** ikinci kez push edecekti → FE aynı badge'i iki kez alırdı.

**Proje sahibi kararı D1 (2026-08-22): push'un sahibi generic relay olur.**

| Seçenek | Karar | Gerekçe |
|---|---|---|
| `PaymentReceivedRealtimeConsumer`'dan status push'unu kaldır | ✓ **Seçildi** | Hardcoded `FromStatus` kaldıracı da silinir; T123'ün `SELLER_CONFIRMED` bacağıyla simetrik; net etki bir kaldıraç **eksiltmek** |
| Relay'e `ToStatus == PAYMENT_RECEIVED` skip filtresi | ✗ Reddedildi | Relay'in tek sözleşmesi *"producer'ın kararını sorgulamadan verbatim ilet"*; ilk per-status istisna onu kalıcı olarak deler **ve** kaldıracı yaşatır |

**İki olayın birlikte yaşaması kasıtlıdır ve tüketicileri ayrıktır:**

| Olay | Taşıdığı | Sürdüğü |
|---|---|---|
| `PaymentReceivedEvent` | para (tutar / token / txHash) | `PaymentConfirmed` push'u + satıcının `PAYMENT_RECEIVED` bildirimi |
| `TransactionStatusChangedEvent` | durum çifti | `StatusChanged` push'u + satıcının `DELIVERY_EXPECTED` bildirimi (**tek üretici**) |

`ProcessedEventStore` anahtarı `(EventId, ConsumerName)` olduğu için iki ayrı `EventId` birbirini maskelemez; idempotency her iki olay için korunur.

**Push payload'ı birebir aynı kaldı** — sahip değişti, içerik değişmedi:

| Alan | Eski (`PaymentReceivedRealtimeConsumer`) | Yeni (`TransactionStatusChangedRealtimeConsumer`) |
|---|---|---|
| `TransactionId` | `domainEvent.TransactionId` | `domainEvent.TransactionId` |
| `FromStatus` | `SELLER_CONFIRMED` (hardcoded) | `domainEvent.FromStatus` = `previousStatus` = `SELLER_CONFIRMED` |
| `ToStatus` | `PAYMENT_RECEIVED` (hardcoded) | `domainEvent.ToStatus` = `PAYMENT_RECEIVED` |
| `Timestamp` | `domainEvent.OccurredAt` | `domainEvent.OccurredAt` (aynı `_clock`, aynı çağrı yeri) |

**Sıralama riski değerlendirildi ve yok.** Eskiden `PaymentConfirmed` → `StatusChanged` tek tüketiciden sırayla gidiyordu; artık iki ayrı outbox satırından geliyorlar ve göreli sıra kesin garanti değil. FE bundan etkilenmiyor: `RealtimeProvider.tsx:69` ve `:95` handler'larının ikisi de **saf cache invalidation** (`queryClient.invalidateQueries`) — durum makinesi yok, biri diğerinin gelmiş olmasına dayanmıyor, sıra değişse sonuç aynı. Admin sayfası da (`admin/transactions/[id]/page.tsx:21,23`) ikisini de `refetch()`'e bağlıyor.

---

## Kapsam yapımda iki yerde genişletildi

**1 — Yayın "tam tutar" koluna değil `AdvanceStateMachineAsync`'in İÇİNE kondu.**
Böylece **fazla ödeme** kolu da kapsanır. Gerekçe ölçülebilir: fazla ödeme durum makinesini ilerletir (`ConfirmedPayment_Overpayment_AdvancesStateAndQueuesExcessRefund`), yani satıcı item'ı **aynen** borçludur ve `ArmDeliveryDeadlineAsync` saati ona karşı başlatır. Yalnız temiz kola bağlanmış bir yayıncı o satıcıyı **tam olarak aynı sessiz yolla** cezalandırırdı — T140'ın kapattığı açığın kendisi. `ConfirmedPayment_Overpayment_AlsoPublishesStatusChanged` ile pinlendi.

**2 — AC2'nin E2E yarısı tipten alıcıya yükseltildi.**
`EXPECTED_NOTIFICATIONS`'a tipi geri koymak yalnız *"bir üretici ateşledi"*yi kanıtlar; **doğru tarafa gittiğini kanıtlamaz.** v3.0'da taraf değiştiren tam da bu bacak (alıcı → satıcı). Yanlış tarafa adreslenmiş bir `DELIVERY_EXPECTED` satıcıyı T140'ın kapattığı açık kadar uyarısız bırakır ve tip-bazlı iddia yeşil kalırdı. Yeni `getNotificationRecipients(type)` yardımcısıyla alıcı kümesinin `[sellerId]`'e eşitliği assert ediliyor.

---

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | `AdvanceStateMachineAsync` geçişle AYNI `SaveChangesAsync` içinde `TransactionStatusChangedEvent(SELLER_CONFIRMED → PAYMENT_RECEIVED)` yayınlar (09 §13.3): geri alınan bir ödeme onayı bildirim doğurmamalı | ✓ | `AmountValidationService.cs` — yayın, `machine.Fire()` ile caller'ın `SaveChanges`'i arasında. `IOutboxService.PublishAsync` yalnız change tracker'a satır ekler (`OutboxService.cs` remarks), yani atomiklik yapısaldır. Testler: `ConfirmedPayment_PublishesStatusChangedEvent_SoTheSellerIsToldToDeliver` (çift doğru), `ConfirmedPayment_PublishesBothEvents_InTheCallersUnitOfWork` (yakalama `SaveChanges` beklerken olur), **negatif:** `ConfirmedPayment_OnEmergencyHold_DoesNotAdvanceOrRefund` + `ConfirmedPayment_Underpayment_AboveThreshold_QueuesIncorrectAmountRefund` (ikisinde de `Assert.Empty(...OfType<TransactionStatusChangedEvent>())`) |
| 2 | Satıcı `DELIVERY_EXPECTED` inbox satırını alır; alıcı ALMAZ | ✓ | Backend: `HappyPathNotificationConsumerTests.StatusChanged_PaymentReceived_NotifiesSeller_NoParams` — `Assert.Equal(_seller.Id, request.UserId)` + yeni `Assert.DoesNotContain(dispatcher.Requests, r => r.UserId == _buyer.Id)`. E2E: `happy-path.smoke.spec.ts` — `getNotificationRecipients('DELIVERY_EXPECTED')` `[sellerId]`'e eşit; CI'da **geçti** (run `32564788118`, happy-path leg 1 passed) |
| 3 | İkinci tüketici gözden geçirilir: çift push üretilmemeli; `ProcessedEventStore` idempotency'si iki olay için de korunmalı | ✓ | Gözden geçirme çift-push'u **ölçtü** (yukarıdaki §AC3). Düzeltme: `PaymentReceivedRealtimeConsumer`'dan status push'u kaldırıldı. Testler: `PaymentReceived_PushesPaymentConfirmed_And_NoStatusChanged` + `PaymentConfirmationTransition_PushesStatusChanged_ExactlyOnce_AcrossBothConsumers` (iki tüketici aynı geçiş üzerinde koşturulur, `StatusChanged` tam **bir** kez). İdempotency: anahtar `(EventId, ConsumerName)` (`NotificationConsumerBase:75`), iki olayın `EventId`'leri ayrı → maskeleme yok |
| 4 | E2E kendini kapatan işaret ÇEVRİLİR: gap marker bloğu silinir, `DELIVERY_EXPECTED` `EXPECTED_NOTIFICATIONS`'a alınır | ✓ | `happy-path.smoke.spec.ts` — blok silindi, tip listeye döndü, yerine alıcı iddiası kondu. **UÇTAN UCA KANITLANDI:** CI run [`32564788118`](https://github.com/turkerurganci/Skinora/actions/runs/32564788118), `E2E happy-path (advisory)` leg'i `✓ 1 passed (7.7m)` — yani gerçek stack'te `DELIVERY_EXPECTED` **üretildi**, `EXPECTED_NOTIFICATIONS`'ın yedi tipinin yedisi de geldi ve alıcı kümesi `[sellerId]`'e eşit çıktı. T138'in kırılmak üzere bıraktığı işaretin cevabı bu leg'in yeşile dönmesidir |

---

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Build | ✓ 0 uyarı / 0 hata | `dotnet build -v q --nologo` |
| Unit (tam suite) | ✓ **1470/1470** (lokal **ve** CI, birebir aynı) | `dotnet test Skinora.sln --filter "FullyQualifiedName!~.Integration&FullyQualifiedName!~.Contract"` — CI'nin kendi filtresi. Modül dağılımı: Shared 388 · Transactions 562 · Notifications 111 · Platform 120 · Auth 83 · API 62 · Realtime 40 · Steam 39 · Disputes 25 · Users 22 · Fraud 18 |
| Integration | ✓ **1369/1369** (CI) | CI run [`32564788118`](https://github.com/turkerurganci/Skinora/actions/runs/32564788118) job "4. Integration test" — 0 failed. Lokal koşu kısmi düştü, **kök nedeni ölçüldü ve CI yalanladı** → §Lokal integration koşusu |
| E2E (advisory) | ✓ **36/36**, 10/10 leg | T110 6 · T113 7 · T108 5 · T109 4 · T111 4 · T114 3 · T112 3 · T138 delivery 2 · happy-path 1 · happy-path UI 1 |
| Contract | ✓ 9/9 | CI job "5. Contract test" |
| Migration dry-run | ✓ | CI job "6. Migration dry-run" |
| e2e typecheck | ✓ 0 | `npx tsc --noEmit` |
| e2e lint | ✓ 0 | `npx eslint .` |
| e2e prettier | (bulgu değil) | Lokal uyarılar 15 dosyada, dokunulmayanlar dahil — `core.autocrlf` CRLF artifaktı; yetkili ölçüm CI "1. Lint" leg'i (LF) |

---

## Lokal integration koşusu — düştü, kök nedeni ölçüldü, CI yalanladı

Kayda geçiyor çünkü **doğrulayıcı bunu görecek** ve "yapım turu kırık integration ile ilerledi" diye okunabilir.

**Ölçüm:** lokal integration koşusunda `Skinora.Transactions.Tests` **64 fail** verdi (546 test, 27 dk). Diğer dokuz assembly'nin dokuzu da temiz geçti — ki bunların içinde **benim dokunduğum tüketicinin assembly'si de var**: `Skinora.Notifications.Tests` **60/60**.

**Ayırt edici deney:** aynı assembly **tek başına** yeniden koşturuldu → fail sayısı **64 → 4**'e düştü. Paralelliğe göre ölçeklenen bir sayı, test mantığına göre ölçeklenmez.

**Hata sınıflandırması — belirleyici olan bu:** gözlenen fail'lerin **%100'ü** `Microsoft.Data.SqlClient.SqlException`, ve hepsi **aynı yerde**: `IntegrationTestBase.CreateDatabaseAsync` (`IntegrationTestBase.cs:176`), `InitializeAsync` içinden. Mesajlar: *"Could not obtain exclusive lock on database 'model'. Retry the operation later."* (×3) ve *"Execution Timeout Expired"* (×1). **Assertion fail sayısı: 0** (`grep -c "Assert."` → 0).

Yani testler **kendi kodlarına hiç ulaşmadan**, per-test veritabanı oluşturma aşamasında düştü. SQL Server eşzamanlı `CREATE DATABASE` çağrılarını `model` veritabanı üzerinde serileştirir; on assembly aynı anda kendi container'ını ve veritabanlarını kurunca tek bir Windows makinesinde çakışırlar.

**Yapısal teyit:** `AmountValidationService` için **hiç integration testi yoktur** — `Skinora.Transactions.Tests/Integration/` altında böyle bir dosya bulunmuyor. Yani bu turun tek Transactions-modülü production değişikliğinin, bu assembly'de kırabileceği bir yüzey yok.

**Yalanlama:** CI'nin "4. Integration test" job'ı aynı filtreyle **1369/1369, 0 failed** verdi. Kaynak lokal ortam çekişmesiydi; **turun kodu değil.**

---

## Altyapı Değişiklikleri

- Migration: **Yok**
- Config/env değişikliği: **Yok**
- Docker değişikliği: **Yok**
- Yeni dış bağımlılık: **Yok**

## Mini Güvenlik Kontrolü

| Kontrol | Sonuç |
|---|---|
| Secret sızıntısı | Yok — eklenen kod yalnız var olan bir domain olayını outbox'a yazıyor |
| Auth / authorization etkisi | Yok — yeni endpoint veya yetki yolu yok |
| Input validation etkisi | Yok — yeni kullanıcı girdisi yok; olayın alanları iç durum makinesinden geliyor |
| Yeni dış bağımlılık | Yok |
| Bilgi ifşası | Bildirim satıcıya gidiyor ve parametre taşımıyor (`Assert.Empty(request.Parameters)`); alıcı-negatif assertion'ı yanlış tarafa sızmayı kapatıyor |

---

## Commit & PR

- Branch: `task/T140-delivery-expected-publisher`
- Commit: `d77f57f` — T140: DELIVERY_EXPECTED bildiriminin yayıncısının bağlanması
- PR: [#256](https://github.com/turkerurganci/Skinora/pull/256)
- CI: **✓ PASS** — run [`32564788118`](https://github.com/turkerurganci/Skinora/actions/runs/32564788118) `conclusion: success`, **CI Gate ✓**. Bloke edici jobların hepsi `success` (Lint · Build · Unit · Integration · Contract · Migration dry-run · Docker build) ve **10/10 advisory E2E leg YEŞİL**
- Branch izolasyon check: `git log main..HEAD --format=%s | grep -oE "^T[0-9]+..."` → **yalnız `T140`** ✓

---

## Known Limitations / Follow-up

- **E2E süiti lokalde koşturulmadı** (T137 / T137a / T138'in izlediği yolun aynısı) — lokalde `tsc` + `eslint` ile statik doğrulandı, çalıştırma kanıtı CI'dan geldi ve **yeşil**: `E2E happy-path (advisory)` `1 passed (7.7m)`. T138 bu leg'i bilerek kırmızıya hazırlamıştı; yeşile dönmesi turun ana kanıtıdır. Bu bir kısıt değil, **kapanmış bir kanıt yolu** olarak kayda geçti.
- **Lokal integration koşusu kısmi düştü** — kök nedeni ölçüldü (container çekişmesi, 0 assertion fail) ve CI 1369/1369 ile yalanladı; §Lokal integration koşusu.
- `T133b-AdminDtoItemReturnedXmlDoc` (⚪) satırına dokunulmadı — kapsam dışı, kendi sahibi var.
- `DEFERRED_BACKLOG` bu turdan sonra **60 aktif / 65 çözülmüş**; kapanan satır listenin **tek 🔴'sıydı, listede artık 🔴 yok.**

---

## Notlar

**Giriş kapıları (task.md Adım -1 / Adım 0):**
- Working tree: **temiz** (`git status --short` boş).
- Main CI son 3 tamamlanmış run: `32531800402` `success` · `32531800419` `success` · `32497982838` `success` → **hepsi `success`**, HARD STOP yok.
- Bağımlılık: **yok** (plan: `Bağımlılık: —`).
- Dal `origin/main` HEAD'inden (`d2d5500`) kesildi.

**Dış varsayımlar (task.md Adım 4): YOK.** Task tamamen repo-içi; yeni paket, dış API, plan tier veya ortam değişkeni bağımlılığı yok. Yine de scope'u etkileyebilecek iki varsayım kod üzerinden doğrulandı:
- `DELIVERY_EXPECTED` bildirim şablonları dört dilde mevcut → `NotificationTemplates{,.tr,.es,.zh}.resx` dördünde de `DELIVERY_EXPECTED_Title` + `_Body` var. Yani yayıncı bağlandığında dispatch şablon eksikliğinden düşmez.
- Frontend tarafı hazır → `frontend/src/types/enums.ts:132` (enum değeri) + `lib/utils/notification-icons.ts:28` (ikon eşlemesi). **FE işi yok**; başlık/gövde backend resx'inden render edildiği için per-tip FE i18n anahtarı gerekmiyor.

**Bloke etmeyen gözlem, dalda düzeltildi — alıntı sapması.** `HappyPathMilestoneNotificationConsumer`'ın XML doc'u `DELIVERY_EXPECTED`'ı `03 §3.5 step 3`'e bağlıyordu. Adım 3 **alıcının inbox satırı almadığını** söyleyen maddedir; satıcıya *"Ödeme alındı, item'ı şimdi gönder"* diyen madde **adım 2**'dir. Sapma davranışı etkilemiyordu ama tam olarak bu turun canlandırdığı kolun üstündeydi ve turun kendi konusu bayat yorumların gerçek hataya dönüşmesi — düzeltildi, adım 3 de "eşlik eden negatif" olarak açıkça anıldı. (T138'in gap marker'ı ve planın T140 tanımı da "adım 3" diyordu; ikisi de tarihsel kayıt, kod sözleşmesi değil, dokunulmadı.)

**Bloke etmeyen gözlem → proje sahibi "çöz" dedi (2026-08-22), ÇÖZÜLDÜ.** `05_TECHNICAL_ARCHITECTURE.md` §5.3 domain event tablosunun `PaymentReceivedEvent` satırı *"Satıcıya «item'ı gönder» bildirimi + **trade bağlantısı**, teslimat süresini başlat, sweep job (**§3.3 custody**)"* diyordu — yani `DELIVERY_EXPECTED`'ı **yanlış olaya** bağlıyor ve iki custody kalıntısı taşıyordu.

**Ölçüm kapsamı bir satırdan geniş çıkardı — ve yarım düzeltme yapılmadı.** Tablonun on bir olay adı tek tek kodla karşılaştırıldı (`ls backend/src/Skinora.Shared/Events/`): **yedisinin kodda karşılığı yok.**

| Tablodaki ad | Kodda | Gerçek karşılığı |
|---|---|---|
| `TransactionCreatedEvent` | ✓ var | — |
| `TransactionAcceptedEvent` | ✗ yok | `BuyerAcceptedEvent` |
| `SellerConfirmedReadyEvent` | ✗ yok | `TransactionStatusChangedEvent` (→ `SELLER_CONFIRMED`) + `PaymentMonitorStartRequestedEvent` |
| `PaymentReceivedEvent` | ✓ var | aksiyonu yanlıştı — `DELIVERY_EXPECTED` bu olayda değil, `TransactionStatusChangedEvent` (→ `PAYMENT_RECEIVED`) üzerinde |
| `ItemDeliveredEvent` | ✗ yok | `TransactionStatusChangedEvent` (→ `ITEM_DELIVERED`) — üç yayıncı |
| `SettlementCompletedEvent` | ✗ yok | `SettlementReviewRequiredEvent` / `TransactionStatusChangedEvent` (`SettlementVerificationJob`) |
| `DeliveryReversedEvent` | ✗ yok | `SettlementReversalDetectedEvent` |
| `TransactionCompletedEvent` | ✗ yok | `PayoutCompletedEvent` |
| `TransactionCancelledEvent` | ✓ var | — |
| `TransactionFlaggedEvent` | ✗ yok | `FraudFlagCreatedEvent` |
| `TimeoutWarningEvent` | ✓ var | — |

Yalnız `PaymentReceivedEvent` satırını düzeltip yedi yanlış adı bırakmak, INSTRUCTIONS §5'in açıkça yasakladığı yarım çözüm olurdu. Tablo **kodda var olan tip adlarına, yayıncılarına ve tüketicilerine** göre yeniden yazıldı; `TransactionStatusChangedEvent` üç ayrı bacağıyla (→ `SELLER_CONFIRMED`, → `PAYMENT_RECEIVED`, → `ITEM_DELIVERED`) ayrı satırlarda gösterildi. İki not eklendi: (1) tablonun **neden** kavramsal ad değil gerçek tip adı taşımak zorunda olduğu — gerekçesi bu turun kendisi; (2) ödeme onayının **neden** iki olay birden yayınladığı ve realtime çift-push'un bastırmayla değil **sahiplikle** önlendiği. Doküman **v3.4 → v3.5**; sürüm notu sessizce üzerine yazmadı, önceki sürümü koruyarak yazıldı (GUARDRAILS §5).

**Bu sapmanın maliyeti kozmetik değildi:** `DELIVERY_EXPECTED`'ın yayıncısının hangi olay olduğu **hiçbir yerde doğru yazılı değildi** — ne C# XML doc'unda (çelişkiliydi), ne tüketicinin alıntısında (adım 3 ≠ adım 2), ne de 05 §5.3'te (yanlış olay). Üç bilgi kaynağının üçü de yanlıştı ve bildirim dört görev boyunca hiç üretilmedi. Üçü de bu turda düzeltildi.

**Yapım turunun bilerek doğrulamaya bıraktığı başka bir şey yok** — AC3'ün gerektirdiği tek karar (push sahipliği) yapım öncesinde proje sahibine sunuldu ve karara bağlandı (D1), plana yazıldı.

**Proje sahibi onayı:** T140 planda `ÖNERİ — proje sahibi onayı bekliyor` durumundaydı; 2026-08-22'de onaylandı, dört AC de korunarak. Plan başlığı ÖNERİ etiketinden arındırıldı ve onay tarihi kaydedildi.
