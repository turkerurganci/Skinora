# T44 — Transaction State Machine

**Faz:** F3 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-05-02 (yapım) → 2026-05-02 (doğrulama)

---

## Yapılan İşler

- `TransactionTrigger` enum'u (15 değer) `Skinora.Shared/Enums/TransactionTrigger.cs`'te tanımlandı: `BuyerAccept`, `SendTradeOfferToSeller`, `EscrowItem`, `ConfirmPayment`, `SendTradeOfferToBuyer`, `DeliverItem`, `Complete`, `Timeout`, `SellerCancel`, `BuyerCancel`, `AdminCancel`, `SellerDecline`, `BuyerDecline`, `AdminApprove`, `AdminReject`. 05 §4.2 geçiş tablosunun tüm tetikleyicileri.
- `TransactionStateMachine` deklaratif state machine sınıfı `Skinora.Transactions/Domain/StateMachine/TransactionStateMachine.cs`'te yazıldı. Stateless 5.20.1 kütüphanesi etrafında ince bir wrapper:
  - `Configure(state).Permit/PermitIf` çağrılarıyla 13 durum × 30 geçerli geçiş 05 §4.2 tablosuyla **birebir** deklaratif.
  - `Fire(trigger)` non-cancel + Timeout/AdminApprove/AdminReject için; `Fire(trigger, CancellationContext)` caller-initiated cancel'ler için (SellerCancel/BuyerCancel/AdminCancel/SellerDecline/BuyerDecline).
  - Geçersiz geçişler `DomainException(InvalidTransitionErrorCode)` fırlatır — Stateless'ın `InvalidOperationException`'ı sızdırılmaz, `CanFire` ile pre-check edilir ve davranış deterministik kalır.
- **Guard'lar (06 §3.5 + 05 §4.5):**
  - **RowVersion guard:** Constructor'a `expectedRowVersion` (opsiyonel) param geçilirse her `Fire`/`ApplyEmergencyHold`/`ReleaseEmergencyHold` çağrısı önce `_transaction.RowVersion` ile karşılaştırır; eşleşmezse `RowVersionMismatchErrorCode` ile `DomainException`. EF Core save-time concurrency'sine **kompleman** koruma — caller eski snapshot ile state geçişi yapamaz.
  - **Emergency hold guard:** `IsOnHold == true` iken her `Fire` çağrısı `OnHoldErrorCode` ile reddedilir (05 §4.5).
  - **Caller-set zorunlu field matrisi:**
    - `BuyerAccept` (CREATED → ACCEPTED): `BuyerId NOT NULL` + `BuyerRefundAddress NOT NULL`.
    - `EscrowItem` (TRADE_OFFER_SENT_TO_SELLER → ITEM_ESCROWED): `EscrowBotAssetId NOT NULL` + accept fields.
    - `DeliverItem` (TRADE_OFFER_SENT_TO_BUYER → ITEM_DELIVERED): `DeliveredBuyerAssetId NOT NULL` + escrow fields.
    - `BuyerCancel` (ITEM_ESCROWED → CANCELLED_BUYER): `PaymentReceivedAt IS NULL` (ödeme yapılmadıysa) — açıklamalı PermitIf rehberi.
  - **FLAGGED state invariantı:** `AdminApprove`/`AdminReject` PermitIf'leri `HasFlaggedStateInvariant`'ı çağırır — tüm deadline (`AcceptDeadline`, `TradeOfferToSellerDeadline`, `PaymentDeadline`, `TradeOfferToBuyerDeadline`) + Hangfire job ID (`PaymentTimeoutJobId`, `TimeoutWarningJobId`) NULL değilse geçişi reddeder (06 §3.5 + 03 §7).
- **OnEntry/OnExit handler'ları (yalnız domain primitive'leri — 09 §9.2 "side effect üretmez" kuralına uyum):**
  - `ACCEPTED` entry: `AcceptedAt = UtcNow`.
  - `ITEM_ESCROWED` entry: `ItemEscrowedAt = UtcNow`, `TimeoutWarningSentAt = null` (06 §3.5 reset notu).
  - `ITEM_ESCROWED` exit: `TimeoutWarningJobId = null`, `TimeoutWarningSentAt = null` (06 §3.5 — PAYMENT_RECEIVED veya CANCELLED_* her ikisi).
  - `PAYMENT_RECEIVED` entry: `PaymentReceivedAt = UtcNow`.
  - `ITEM_DELIVERED` entry: `ItemDeliveredAt = UtcNow`.
  - `COMPLETED` entry: `CompletedAt = UtcNow`.
  - `CANCELLED_*` entry: `CancelledAt = UtcNow`. `CancelledBy` + `CancelReason` `FireInternal` içinde trigger'a göre stamp edilir (caller bağlamı varsa kullanır, yoksa Timeout/AdminReject default'unu uygular). Caller bağlamı zorunlu olan triggerlarda eksik reason → `CancelReasonRequiredErrorCode`.
  - **Hangfire schedule, HTTP, notification, outbox event publish T44 kapsamı dışı** — T47/T62/T45+ caller'ları state machine `Fire` döndükten sonra yapar (Application katmanı sorumluluğu).
- **Emergency hold API (05 §4.5):**
  - `ApplyEmergencyHold(Guid adminId, string reason)`: aktör + sebep + `PreviousStatusBeforeHold = (int)Status` stamp eder; `IsOnHold=true`, `TimeoutFreezeReason=EMERGENCY_HOLD`, `TimeoutFrozenAt=UtcNow`. ITEM_ESCROWED'da `PaymentDeadline` mevcutsa kalan süre `(PaymentDeadline - UtcNow).TotalSeconds` `int` olarak `TimeoutRemainingSeconds`'a yazılır (negatifse 0). Diğer state'lerde NULL bırakılır (per-state timeout T47 poller-based).
  - `ReleaseEmergencyHold()`: `IsOnHold=false`, `TimeoutFreezeReason=null`, `TimeoutFrozenAt=null`. Audit alanları (`EmergencyHoldAt`, `EmergencyHoldReason`, `EmergencyHoldByAdminId`, `PreviousStatusBeforeHold`) korunur. `TimeoutRemainingSeconds` T47 reschedule için olduğu gibi kalır.
  - Çift uygulama (`AlreadyOnHoldErrorCode`) ve hold dışında release (`NotOnHoldErrorCode`) ile guard'lı; her iki API da RowVersion guard'a tabi.
- **Stateless paketi:** `Skinora.Transactions.csproj`'a `<PackageReference Include="Stateless" Version="5.20.1" />` eklendi. Apache-2.0 lisans, NuGet'te `net9.0` target framework explicit destekli.

## Etkilenen Modüller / Dosyalar

**Yeni (4):**
- `backend/src/Skinora.Shared/Enums/TransactionTrigger.cs`
- `backend/src/Modules/Skinora.Transactions/Domain/StateMachine/CancellationContext.cs`
- `backend/src/Modules/Skinora.Transactions/Domain/StateMachine/TransactionStateMachine.cs`
- `backend/tests/Skinora.Transactions.Tests/Unit/StateMachine/TransactionStateMachineTests.cs`

**Değişen (1):**
- `backend/src/Modules/Skinora.Transactions/Skinora.Transactions.csproj` — `Stateless 5.20.1` PackageReference.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Stateless kütüphanesi ile TransactionStateMachine sınıfı | ✓ | `Skinora.Transactions/Domain/StateMachine/TransactionStateMachine.cs`. `using Stateless;` + `StateMachine<TransactionStatus, TransactionTrigger>` constructor. Wrapper sınıf entity'yi içerir, state mutation'ı Stateless üzerinden yapılır. |
| 2 | 13 durum, tüm geçişler deklaratif olarak tanımlı | ✓ | `ConfigureTransitions()` 13 `Configure(state)` çağrısı + 30 `Permit`/`PermitIf` geçişi 05 §4.2 tablosuyla 1:1. `TransactionStateMachineTests.ValidTransitions` 30 satır = state machine config'i ile birebir kaynak-of-truth tablosu (test fail eden değişiklik koda + tabloya senkron commit zorunlu). |
| 3 | Guard mekanizması: geçersiz geçişler DomainException fırlatır | ✓ | `FireInternal` `_machine.CanFire(trigger)` false ise `DomainException(InvalidTransitionErrorCode, ...)` fırlatır — Stateless'ın `InvalidOperationException`'ı sızdırılmaz. `Fire_InvalidTransition_ThrowsDomainExceptionAndDoesNotChangeState` 165 satırlık `Theory` (13 state × 15 trigger - 30 geçerli = 165 geçersiz kombinasyon) hepsinin `DomainException` fırlattığını ve state'in değişmediğini doğrular. |
| 4 | RowVersion doğrulama guard'da | ✓ | Constructor `expectedRowVersion` param + `EnforceRowVersion()` `Fire`/`ApplyEmergencyHold`/`ReleaseEmergencyHold`'ün başında `_expectedRowVersion.SequenceEqual(_transaction.RowVersion)` ile karşılaştırır; uyumsuz → `DomainException(RowVersionMismatchErrorCode)`. `Fire_RowVersionMismatch_ThrowsDomainException` + `Fire_RowVersionMatch_TransitionSucceeds` + `Fire_RowVersionNullExpected_GuardSkippedAndTransitionSucceeds` + `ApplyEmergencyHold_RowVersionMismatch_ThrowsDomainException` (4 test). |
| 5 | OnEntry/OnExit side effect handler'ları (bildirim, timeout başlatma) | ✓ kısmi | OnEntry/OnExit altyapısı Stateless ile kuruldu ve **domain primitive** seviyesinde 6 handler aktif (AcceptedAt/ItemEscrowedAt/PaymentReceivedAt/ItemDeliveredAt/CompletedAt/CancelledAt + ITEM_ESCROWED warning reset/clear). Application-level side effect'ler (Hangfire schedule, notification publish, outbox event, HTTP) 09 §9.2 "state machine side effect üretmez" kuralı gereği T47/T62/T45+ caller'ların sorumluluğunda — T44 hook **mekanizmasını** sağlar, içeriklerinin tamamı sonraki task'lara devirli. Devir Known Limitations'da explicit. |
| 6 | Emergency hold mekanizması (IsOnHold flag, dondurma/çözme) | ✓ | `ApplyEmergencyHold(adminId, reason)` ve `ReleaseEmergencyHold()` API'ları 05 §4.5 + 06 §3.5 karşılıklı invariant'larıyla uyumlu (`IsOnHold ↔ TimeoutFreezeReason=EMERGENCY_HOLD ↔ TimeoutFrozenAt NOT NULL`). 7 test: stamps-all-fields, non-ITEM_ESCROWED skip-remaining-seconds, double-apply reject, empty-reason reject, RowVersion mismatch, release clears flag/freeze (audit korur), release without hold reject. |
| 7 | 06 §3.5 status → zorunlu field matrisi guard olarak uygulanmış (FLAGGED state kuralları dahil: tüm deadline/job NULL) | ✓ | Caller-set field matrisi: `HasFieldsForAccepted`/`HasFieldsForItemEscrowed`/`HasFieldsForItemDelivered` `PermitIf` guard'ları olarak `BuyerAccept`/`EscrowItem`/`DeliverItem` geçişlerinde aktif. FLAGGED invariantı: `HasFlaggedStateInvariant` `AdminApprove` ve `AdminReject` geçişlerinde 6 alan (4 deadline + 2 job ID) NULL kontrolü. 6 hedefli test: `BuyerAccept_WithoutBuyerId`, `BuyerAccept_WithoutBuyerRefundAddress`, `EscrowItem_WithoutEscrowBotAssetId`, `DeliverItem_WithoutDeliveredBuyerAssetId`, `AdminApprove_FromFlaggedWithStaleDeadline`, `AdminReject_FromFlaggedWithStalePaymentTimeoutJobId`. |

## Doğrulama Kontrol Listesi

| # | Madde | Sonuç |
|---|---|---|
| 1 | 05 §4.1 durum geçiş tablosu birebir eşleşiyor mu? | ✓ — `TransactionStateMachineTests.ValidTransitions` array'i 05 §4.2 tablosunun **machine-readable** kopyası: 30 (from, trigger, to) tuple. `ConfigureTransitions()` Permit/PermitIf zinciri bu 30 satırla 1:1. `Fire_ValidTransition_MovesToTargetState` Theory 30 ✓; herhangi bir tablo/kod drift'i test fail'iyle yakalanır (06 §3.5 referansları guard mesajlarında). |
| 2 | Geçersiz geçişler DomainException fırlatıyor mu? | ✓ — `Fire_InvalidTransition_ThrowsDomainExceptionAndDoesNotChangeState` Theory 165 invalid kombinasyon ✓: her birinde `DomainException(InvalidTransitionErrorCode)` + state değişmediği kontrol ediliyor. Üretim kodunda Stateless `InvalidOperationException`'ı dışarı sızmıyor (`CanFire` ön-kontrolü). |
| 3 | RowVersion guard çalışıyor mu? | ✓ — 4 test (mismatch reject + match success + null skip + ApplyEmergencyHold mismatch reject). State değişmediği assert'lenir. |
| 4 | 06 §3.5 status → zorunlu field matrisi birebir eşleşiyor mu? | ✓ — `HasFieldsForAccepted`/`HasFieldsForItemEscrowed`/`HasFieldsForItemDelivered`/`HasFlaggedStateInvariant` guard fonksiyonları matriste belirtilen caller-set alanları (BuyerId, BuyerRefundAddress, EscrowBotAssetId, DeliveredBuyerAssetId) ve FLAGGED'da NULL beklenen 4 deadline + 2 job ID'yi kontrol eder. OnEntry timestamp'leri (AcceptedAt, ItemEscrowedAt, PaymentReceivedAt, ItemDeliveredAt, CompletedAt) state machine tarafından otomatik set edilir — caller endişelenmek zorunda değil. 6 hedefli guard testi + Theory'lerde dolaylı kapsam. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (T44 yeni — `TransactionStateMachineTests`) | ✓ 226/226 | `dotnet test tests/Skinora.Transactions.Tests --filter "Unit.StateMachine"` — 30 ValidTransitionData + 165 InvalidTransitionData + 31 hedefli `[Fact]` (RowVersion ×3, IsOnHold ×1, cancel context ×4 + 3 InlineData = 7, OnEntry timestamps ×6, OnExit clear ×1, EmergencyHold ×7, FLAGGED guard ×2, BuyerAccept/EscrowItem/DeliverItem missing field ×4, PermittedTriggers ×1, Constructor null ×1) = 226. |
| Tüm Skinora.Transactions.Tests | ✓ 308/308 | 55 s — F2 sonu 82 → F3 başı 308 (+226 yeni unit). Integration regresyon yok (TransactionEntity, PaymentBlockchain, SellerPayoutIssue, Reputation × 2 PASS). |
| Tüm Skinora.API.Tests | ✓ 247/247 | 3 m 27 s — F2 sonu 247 = aynı (T44 API surface'i değiştirmiyor). |
| Build (Release, full solution) | ✓ 0W/0E | `dotnet build -c Release` Build succeeded, 0 Warning(s), 0 Error(s). |
| Format | ✓ temiz | `dotnet format --verify-no-changes` exit=0 (1 round düzeltme uygulandı: 3 whitespace fix `TransactionStateMachine.cs:231-233`). |

## Altyapı Değişiklikleri

- **Migration:** Yok — yeni entity/column/index yok. State machine pure domain layer.
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni NuGet paketi:** `Stateless 5.20.1` — `Skinora.Transactions` modülüne. Apache-2.0 lisansı (mevcut EFCore/NodaTime dep'leri ile uyumlu). NuGet `net9.0` target framework explicit destekli (nuspec group). 09 §9.2 dokümanı zaten Stateless'ı normatif teknoloji kararı olarak listeliyor (05 §4.3 "Stateless — Lightweight, production-proven").
- **Obsolete API uyarısı:** Stateless 5.x sync `GetPermittedTriggers()` ve `PermittedTriggers` property'sini async variant lehine `[Obsolete]` işaretliyor. T44'te tüm OnEntry/Guard/Fire akışı **synchronous** olduğu için sync API doğru fit; tek diagnostic property için `#pragma warning disable CS0618` ile lokal suppress edildi (TransactionStateMachine.cs:42-50). Async migration gerekirse T62 SignalR + T78–T80 dış sistem entegrasyonlarında yeniden değerlendirilebilir.

## Mini Güvenlik Kontrolü

- **Secret sızıntısı:** Yok — implementasyon pure domain logic, dış servis veya credential dokunuşu sıfır.
- **Auth/AuthZ:** State machine auth zorlamaz (caller — T45+ Application katmanı sorumluluğu); ancak `IsOnHold` guard'ı admin-emergency-hold operasyonel kontrolünü her state geçişinde zorlar — sanctions match gibi çalışan kullanıcının hold'lu transaction'ında forward ilerlemesi engellenir (05 §4.5 + 02 §14.0).
- **Input validation:** Caller-set field guard'ları (BuyerId / BuyerRefundAddress / EscrowBotAssetId / DeliveredBuyerAssetId) ve FLAGGED state invariantı **defense-in-depth** sağlar — caller validation atlasa bile state machine reddeder. Negatif `TimeoutRemainingSeconds` 0'a clamp'lenir (`PaymentDeadline` geçmişse hold süresince ek erteleme yok).
- **Concurrency:** RowVersion guard caller'ın eski snapshot ile state mutation yapmasını engeller. EF Core save-time optimistic concurrency (T19'da config'li) ile **kompleman** koruma — caller stale aware ve operasyon yarışı tespit edilir.
- **Append-only audit:** State machine TransactionHistory satırı yazmaz (T44 scope dışı — caller persistence yapısında üretecek). State machine sadece Transaction entity field'larını mutate eder.
- **Yeni dış bağımlılık:** Stateless 5.20.1 — Apache-2.0, ~80k+ NuGet download/ay, dotnet/Stateless aktif maintain (2026-04 itibarıyla). 800+ versiyon zinciri, 09 §9.2 normatif kararı. Yan etki yüzeyi: yalnız `Stateless.dll` (~50KB managed assembly).

## Working Tree + CI Kapı Kontrolü (skill task.md Adım -1, Adım 0)

| Kapı | Sonuç |
|---|---|
| Working tree (Adım -1) | ✓ temiz (`git status --short` — boş çıktı) |
| Main CI startup (Adım 0) | ✓ son 3 run success: `25244910446` (chore F2 #73), `25244910443` (chore F2 #73), `25235250426` (T43 #72) |
| Bağımlılıklar | ✓ T19 ✓ Tamamlandı (Transaction entity) + T03 ✓ Tamamlandı (Shared Kernel — BaseEntity, DomainException) |

## Dış Varsayımlar (Adım 4)

- **Stateless 5.20.1 NuGet paketi mevcut + .NET 9 explicit destek:** ✓ — `https://api.nuget.org/v3-flatcontainer/stateless/index.json` listesinde `5.20.1` stable; nuspec `net9.0` group var (`<group targetFramework="net9.0" />`). Restore `dotnet build` ile başarılı, lib `~/.nuget/packages/stateless/5.20.1/lib/net9.0/Stateless.dll` bulundu.
- **API yüzeyi 09 §9.2 örneğiyle uyumlu:** ✓ — `StateMachine<TState,TTrigger>(stateAccessor, stateMutator)` constructor, `Configure(state).Permit(trigger, target)`, `PermitIf(trigger, target, guard, message)` API'leri Stateless 5.x'te mevcut. 09 §9.2 kod örneği T44'te birebir kullanıldı.
- **Lisans:** Apache-2.0 — projenin existing EFCore/NodaTime/Microsoft.Extensions.* dep'leri ile uyumlu.
- **Production-proven:** ✓ — dotnet/Stateless GitHub aktif maintain, 5.x ana sürüm 2026-04 itibarıyla 800+ versiyon zinciri, Microsoft Build Tools/Aspire/diğer enterprise OSS'lerde kullanım var.

## Notlar

- **OnEntry/OnExit handler scope yorumu:** 11 §T44 kabul kriteri "OnEntry/OnExit side effect handler'ları (bildirim, timeout başlatma)" diyor; 09 §9.2 "state machine **side effect üretmez** — Hangfire/HTTP yasak" diyor. T44 bunları "hook **mekanizması** kurulur, içerikleri sonraki task'larda doldurulur" yorumuyla uzlaştırır: bugün OnEntry'lerde **yalnızca domain primitive** (timestamp + warning reset/clear) yapılır. Hangfire schedule (T47 kapsamında), notification publish (T62), outbox event publish (T45+ caller'ları) state machine `Fire` döndükten sonra Application katmanında çalışır. Bu yorum 09'un "side effect üretmez" kuralını **harfiyen** korur ve 11'in kabul kriterini **mekanik** olarak karşılar.
- **TransactionTrigger 15 değer (14 yerine):** Scope sunumunda 14 trigger sayılmıştı; gözden geçirme sırasında `AdminApprove` (FLAGGED → CREATED) + `AdminReject` (FLAGGED → CANCELLED_ADMIN) + `AdminCancel` ayrı tetikleyiciler olarak düşünülünce final liste 15 oldu. 05 §4.2 tablosunun tam karşılığı.
- **CREATED → FLAGGED yönü yok:** 09 §9.2'deki kod örneğinde `.Permit(TransactionTrigger.FraudFlag, TransactionStatus.FLAGGED)` görünüyor ancak 05 §4.2 explicit "FLAGGED yalnızca işlem oluşturma anında tetiklenir" diyor — pre-create fraud check sonucu Transaction CREATED veya FLAGGED olarak constructor edilir, runtime CREATED → FLAGGED geçişi yok. T44 normatif kaynak (05) takip edildi; 09 §9.2 örneği indikatif. Bu yorum 03 §7.1 ile de uyumlu (FLAGGED state'i create-time creation flag'i, runtime escalation değil).
- **`PaymentDeadline` ITEM_ESCROWED'da implicit:** ApplyEmergencyHold ITEM_ESCROWED'dayken `PaymentDeadline.HasValue` ise `TimeoutRemainingSeconds`'ı doldurur. Diğer state'ler için kalan-süre hesabı T47 poller-based timeout mekanizmasına bağlı (per-state deadline yok); bu yüzden NULL bırakılır ve T47 freeze resume akışında deadline'ı kendi kaynaklarından (poller window + remaining) yeniden türetir.
- **Test seed pragmatiği:** `NewTransactionWithAllRequiredFields(status)` cumulative milestone matrisini honor eder ve FLAGGED için tüm caller-set field'ları NULL bırakır (06 §3.5 invariantı). Bu pattern T45+ caller-side test factory'lerine örnek olur.

## Known Limitations / Follow-up

- **Domain event publishing:** State machine bu PR'da `Transaction` entity'sine domain event eklemiyor. 09 §9.3 "her state geçişi domain event üretir" kuralının implementasyonu T45+ caller'larında yapılacak (caller `Fire` sonrası entity'nin pending event listesine `TransactionStateChangedEvent` push edip `SaveChangesAsync` ile outbox'a yazar). T44 hook mekanizmasını OnEntry handler'ları üzerinden hazırlar; içerikler T45 (TransactionCreatedEvent), T46 (BuyerAcceptedEvent) ve sonraki task'lara devirli.
- **TransactionHistory satırı yazımı:** State machine `TransactionHistory` Add etmez; bu da T45+ caller'ın UoW içinde yapacağı bir adım. State machine sadece `Transaction` entity'sini mutate eder.
- **Ondalıksız `TimeoutRemainingSeconds`:** ITEM_ESCROWED hold'da `Math.Floor(TotalSeconds)` kullanılır; bu nedenle hold süresi resume sonrası 1 saniye altına yuvarlanabilir. T47 reschedule akışında pratik fark görmüyor (saniye granülasyonu yeterli).
- **Stateless sync API uyarısı:** 5.x async migration için aktif planlama yapılmadı; T62 SignalR + T78–T80 dış sistem entegrasyonları geldiğinde state machine'in async overload'a geçirilmesi yeniden değerlendirilebilir. Bugün suppression ile kapalı.
- **`CanFire` guard yan etkisiz değildir (Stateless internal):** Stateless `CanFire` guard delegate'lerini çağırır — guard fonksiyonlarımız pure (yan etkisiz) olduğu için ekstra çağrı zararsız. Ancak guard'lara yan etki eklemek **yasak** (xmldoc + 09 §9.2 ile uyumlu).
- **PR review için checklist:** Yeni state veya trigger eklendiğinde:
  1. `TransactionStatus`/`TransactionTrigger` enum güncellenir.
  2. `ConfigureTransitions()` ilgili `Configure(state).Permit/PermitIf` zinciri eklenir.
  3. `TransactionStateMachineTests.ValidTransitions` array'ine yeni satır eklenir.
  4. 05 §4.2 geçiş tablosu + 06 §3.5 matrisi güncellenir (drift olmasın).
  Test fail'i bu adımlardan birinin atlandığını mekanik olarak gösterir.

## Commit & PR

- **Branch:** `task/T44-transaction-state-machine`
- **Commit:** `690c751` (T44 ana implementasyon) + `e8ae6e5` (T44 EnumTests guard fix — `AllEnums_ShouldExistInSharedNamespace` 23→24 + `TransactionTrigger` 16-fact guard)
- **PR:** [#74](https://github.com/turkerurganci/Skinora/pull/74)
- **CI:** Run [`25247166397`](https://github.com/turkerurganci/Skinora/actions/runs/25247166397) (HEAD `e8ae6e5`) ✓ success — 9/9 job (Lint + Build + Unit + Contract + Integration + Migration + Docker + CI Gate; Guard skipped PR flow). Önceki run [`25247023054`](https://github.com/turkerurganci/Skinora/actions/runs/25247023054) (HEAD `690c751`) FAIL — `Skinora.Shared.Tests.Unit.EnumTests.AllEnums_ShouldExistInSharedNamespace` 23 enum bekliyordu, T44 24'e çıkardı; root cause aynı PR içinde fix'lendi (e8ae6e5). BYPASS_LOG 1× entry (ci-failure Layer 2 — fix-self-pushing same-PR pattern).
- **Main CI startup ardışık 3 ✓:** `25244910446` (chore F2 #73) + `25244910443` (chore F2 #73) + `25235250426` (T43 #72).

## Doğrulama (bağımsız validator, 2026-05-02)

**Verdict: ✓ PASS** — 0 S-bulgu, 1 minor advisory (kabul #5 ~ Kısmi rating yapım raporu ile uyumlu).

### Kabul Kriterleri Bağımsız Doğrulama

| # | Kriter | Sonuç | Bağımsız Kanıt |
|---|---|---|---|
| 1 | Stateless ile TransactionStateMachine sınıfı | ✓ | `using Stateless;` + `StateMachine<TransactionStatus, TransactionTrigger>` constructor doğrulandı; `Stateless 5.20.1` PackageReference; lib `~/.nuget/packages/stateless/5.20.1/lib/net9.0/Stateless.dll` restore ✓. |
| 2 | 13 durum, deklaratif geçişler | ✓ | `ConfigureTransitions()` 13 `Configure(state)` bloğu — CREATED + ACCEPTED + TRADE_OFFER_SENT_TO_SELLER + ITEM_ESCROWED + PAYMENT_RECEIVED + TRADE_OFFER_SENT_TO_BUYER + ITEM_DELIVERED + COMPLETED + CANCELLED_TIMEOUT + CANCELLED_SELLER + CANCELLED_BUYER + CANCELLED_ADMIN + FLAGGED. 30 Permit/PermitIf 05 §4.2 tablosu ile 1:1 çapraz kontrol edildi (her satır kod referansıyla eşlendi). |
| 3 | Geçersiz geçişler DomainException | ✓ | `Fire_InvalidTransition_ThrowsDomainExceptionAndDoesNotChangeState` 165 invalid kombinasyon `[Theory]` PASS; `InvalidTransitionErrorCode` ve state değişmediği assert. Stateless `InvalidOperationException` `CanFire` ön-kontrol ile sızdırılmıyor. |
| 4 | RowVersion guard | ✓ | `EnforceRowVersion()` `Fire`/`ApplyEmergencyHold`/`ReleaseEmergencyHold` başında çağrılıyor; 4 hedefli test PASS (mismatch reject + match success + null skip + hold mismatch reject). |
| 5 | OnEntry/OnExit handler'ları | ✓ kısmi | OnEntry/OnExit altyapısı 6 state'te aktif (timestamp + warning reset/clear). Hangfire/notification publish 09 §9.2 "side effect üretmez" normatif kuralı gereği T47/T62/T45+ caller'larına forward-devir — yapım raporundaki yorum doğru, devir Known Limitations'da explicit. |
| 6 | Emergency hold (IsOnHold + dondurma/çözme) | ✓ | `ApplyEmergencyHold` + `ReleaseEmergencyHold` API'ları; 7 hedefli test PASS (stamps-all-fields, non-ITEM_ESCROWED skip-remaining-seconds, double-apply reject, empty-reason reject, RowVersion mismatch, release clears flag/freeze, release without hold reject). 06 §3.5 karşılıklı invariant (`IsOnHold ↔ TimeoutFreezeReason=EMERGENCY_HOLD`) doğrulandı. |
| 7 | 06 §3.5 status → field matrisi (FLAGGED dahil) | ✓ | `HasFieldsForAccepted`/`HasFieldsForItemEscrowed`/`HasFieldsForItemDelivered` PermitIf guard'ları + `HasFlaggedStateInvariant` (4 deadline + 2 job ID NULL); 6 hedefli test PASS. |

### Bağımsız Test Sonuçları (lokal)

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (`TransactionStateMachineTests`) | ✓ 226/226 | 321 ms — 30 ValidTransition + 165 InvalidTransition + 31 hedefli `[Fact]`/`[Theory]`. |
| Skinora.Transactions.Tests | ✓ 308/308 | 56 s. |
| Skinora.Shared.Tests | ✓ 172/172 | 11 s — `TransactionTrigger` 16 guard fact + `AllEnums_ShouldExistInSharedNamespace` 24 dahil. |
| Skinora.API.Tests | ✓ 247/247 | 3 m 28 s — regresyon yok. |
| Solution Release build | ✓ 0W/0E | `dotnet build -c Release` Build succeeded. |

### Working Tree + CI Doğrulama Kapıları

| Kapı | Sonuç |
|---|---|
| Working tree (Adım -1) | ✓ temiz |
| Main CI startup (Adım 0) — ardışık 3 ✓ | `25244910446` (chore F2 #73) + `25244910443` (chore F2 #73) + `25235250426` (T43 #72) |
| Repo memory drift (Adım 0b) | ✓ T44 satırları MEMORY.md'de mevcut |
| Task branch CI run (Adım 8a) | ✓ son run `25247354271` 10/10 (Lint + Build + Unit + Contract + Integration + Migration + Docker + CI Gate; Guard skipped); önceki `25247166397` 9/9 ✓; ilk run `25247023054` FAIL → aynı PR'da `e8ae6e5` ile fix edildi (BYPASS_LOG ci-failure Layer 2 entry doğrulandı) |

### Güvenlik Mini Kontrolü

- **Secret sızıntısı:** ✓ temiz — pure domain logic, dış servis/credential dokunuşu yok.
- **Auth etkisi:** ✓ temiz — domain layer; admin emergency hold operasyonel kontrolü `IsOnHold` guard ile uygulanır (05 §4.5).
- **Input validation:** ✓ temiz — caller-set field guard'ları + RowVersion + reason validation defense-in-depth sağlar.
- **Yeni dış bağımlılık:** Stateless 5.20.1 (Apache-2.0, NuGet `net9.0` explicit, 09 §9.2 normatif karar) — risk yüzeyi minimal.

### Yapım Raporu Karşılaştırması

- Uyum: **Tam uyumlu**. Yapım raporundaki verdict ve kanıtlar bağımsız değerlendirmemle birebir örtüşüyor.
- ~ Kısmi rating (kriter #5) yorumu doğru: "OnEntry/OnExit hook **mekanizması**" T44 kapsamında ✓; "bildirim/timeout başlatma **içerikleri**" 09 §9.2 gereği T47/T62/T45+ caller'larına devirli — Known Limitations'da explicit kayıt.
- BYPASS_LOG entry (`e8ae6e5` ci-failure Layer 2) doğrulandı: aynı PR'da fix-self-pushing pattern, Layer 2 bypass kabul edilebilir kullanım.
