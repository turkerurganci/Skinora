# WP15 — Reputation aggregation tetik

**Faz:** Pre-F6 (P5 — Config/altyapı) | **Durum:** ✓ Tamamlandı (bağımsız validator PASS) | **Tarih:** 2026-06-19

---

## Yapılan İşler

T43'te kurulmuş ama **hiçbir prod caller'ına bağlanmamış** reputation altyapısının
(`IReputationAggregator.RecomputeAsync` + `IUserCancelCooldownEvaluator.EvaluateAsync`)
tetikleyicileri ile, bunların CANCELLED_TIMEOUT sorumluluk-atfı için bağımlı olduğu
**TransactionHistory audit-trail yazımı** (06 §3.6, bugüne dek tamamen yazılmıyordu)
bağlandı.

**Üç ayaklı boşluk kapatıldı:**
1. **COMPLETED hiç recompute etmiyordu** — `PayoutCompletedConsumer` `Complete` ateşliyor ama her iki tarafın `CompletedTransactionCount`/`SuccessfulTransactionRate`'ini güncellemiyordu → kapatıldı.
2. **CANCELLED_TIMEOUT recompute + cooldown yoktu** — `TimeoutExecutor` + `DeadlineScannerJob` reputation/cooldown çağırmıyordu → bağlandı.
3. **Steam decline/expire recompute yoktu** — `SteamWebhookHandler.ApplyStatusChangeAsync` (yorumunda "reputation refresh forward-deferred" diyordu) → bağlandı.
4. **TransactionHistory hiç yazılmıyordu** (06 §3.6 "her state geçişinin tam kaydı") — aggregator timeout sorumluluğunu yalnız `TransactionHistory.PreviousStatus`'tan okuyor; satır olmadan timeout iptalleri reputation/cooldown'dan **sessizce dışlanıyordu** → tüm geçiş noktalarına yazım eklendi.

**Owner kararları (AskUserQuestion):**
- **History kapsamı = Tam audit trail** (tüm geçiş noktaları, 06 §3.6 + 05 §5.4 + Pre-F6 "erteleme yok").
- **Mimari = Sync inline + paylaşılan recorder** (mevcut `TransactionCancellationService` emsali).

**Yeni bileşenler:**
- `TransactionHistoryRecorder` — static shared helper (`WashTradingFilter.Apply` emsali). Caller `Fire()` sonrası çağırır, kendi `SaveChanges`'inde flush eder; sıfır constructor churn (interceptor trigger/actor veremezdi, per-caller injection 11 servisin + direct-`new` testlerinin constructor'ını şişirirdi). Genesis (creation, `PreviousStatus=null`) için string-overload + `GenesisTrigger="Create"`.
- `ITransactionReputationRefresher` + `TransactionReputationRefresher` — `IReputationAggregator` + `IUserCancelCooldownEvaluator`'ı saran ince orkestrasyon (her iki taraf recompute; cooldown yalnız cancel-sınıfı geçişlerde, evaluator non-responsible tarafı kendi içinde atlar). DI: `TransactionsModule`.

**History wiring (genesis + 11 .Fire() noktası, actor haritası):**
| Geçiş | Caller | Actor |
|---|---|---|
| Creation (genesis, prev=null) | `TransactionCreationService` | USER (seller) |
| BuyerAccept | `TransactionAcceptanceService` | USER (buyer) |
| SendTradeOfferToSeller/Buyer | `TradeOfferDispatchJob` ×2 | SYSTEM |
| EscrowItem / DeliverItem | `SteamWebhookHandler` ×2 | SYSTEM |
| ConfirmPayment | `AmountValidationService` | SYSTEM |
| Complete | `PayoutCompletedConsumer` | SYSTEM **+ recompute (her iki taraf)** |
| SellerCancel/Decline/BuyerCancel | `TransactionCancellationService` | USER **(recompute+cooldown zaten vardı; History eklendi)** |
| SellerDecline/BuyerDecline/Timeout (Steam) | `SteamWebhookHandler.ApplyStatusChangeAsync` | SYSTEM **+ recompute+cooldown** |
| Timeout | `TimeoutExecutor` + `DeadlineScannerJob` | SYSTEM **+ recompute+cooldown** |
| AdminCancel ×2 | `AdminTransactionService` | ADMIN |
| AdminApprove / AdminReject | `FraudFlagService` | ADMIN |
| AdminResolveRefund (REFUNDED) | `AdminDisputeService` | ADMIN |

**Sync-inline recompute deseni:** terminal caller `Fire()` → History ekle → **flush** (aggregator/cooldown `AsNoTracking` okuduğundan terminal statü + timeout için History `PreviousStatus` commit-içi görünür olmalı) → `RefreshAsync` → `SaveChanges`. Dedicated-scope caller'lar (`TimeoutExecutor`/`DeadlineScannerJob`/`SteamWebhookHandler`) iki-fazlı `BeginTransactionAsync` ile atomik (09 §13.3, cancel yolu emsali). `PayoutCompletedConsumer` **bilinçle transaction kullanmaz** — outbox dispatcher ile aynı `AppDbContext`'i paylaşır (bekleyen outbox-status değişiklikleriyle dolanmamak için); recompute idempotent + eventual (06 §8.2), retry'da self-heal eder.

## Etkilenen Modüller / Dosyalar

**Yeni (5):**
- `Skinora.Transactions/Application/History/TransactionHistoryRecorder.cs`
- `Skinora.Transactions/Application/Reputation/ITransactionReputationRefresher.cs` + `TransactionReputationRefresher.cs`
- `tests/.../Unit/History/TransactionHistoryRecorderTests.cs` + `Unit/Reputation/TransactionReputationRefresherTests.cs`

**Değişen prod (13):** `TransactionCreationService`, `TransactionAcceptanceService`, `TransactionCancellationService`, `AmountValidationService`, `PayoutCompletedConsumer`, `TimeoutExecutor`, `DeadlineScannerJob`, `AdminTransactionService` (Transactions); `SteamWebhookHandler`, `TradeOfferDispatchJob` (Steam); `FraudFlagService` (Fraud); `AdminDisputeService`, `TransactionsModule` (API).

**Değişen test (11):** PayoutCompleted/Timeout/DeadlineScanner/SideEffects + `TimeoutTestSupport` (no-op + gerçek refresher factory'leri); SteamWebhookHandlerTests/FraudFlagServiceTests (yerel no-op); **3 API endpoint factory `Reset()` (test-infra fix, aşağıda)**.

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | COMPLETED'da her iki tarafın reputation'ı güncellenir (06 §8.2) | ✓ | `PayoutCompletedConsumerTests.Completion_Recomputes_Reputation_For_Both_Parties_And_Writes_History` — seller+buyer `CompletedTransactionCount=1`, `SuccessfulTransactionRate=1.0` + Complete History satırı |
| 2 | CANCELLED_TIMEOUT sorumlu tarafa atfedilir + cooldown tetiklenir | ✓ | `TimeoutExecutorTests.ExecutePaymentTimeout_Writes_History_And_Attributes_Reputation_To_Buyer` — ITEM_ESCROWED timeout → buyer `rate=0.0` (sorumlu), seller `rate=null` (etkilenmez); History `PreviousStatus=ITEM_ESCROWED` |
| 3 | TransactionHistory tüm geçişlerde yazılır (06 §3.6) | ✓ | Recorder unit testleri (prev/new/trigger/actor + genesis null); 12 caller wiring; API endpoint testleri (lifecycle/steam/blockchain) geçişleri sürer, `Reset()` ürettikleri History satırlarını temizler = satırlar gerçekten yazılıyor |
| 4 | Refresher orkestrasyonu doğru (her iki taraf; cooldown yalnız cancel) | ✓ | `TransactionReputationRefresherTests` 3 senaryo: COMPLETED→cooldown atlanır; CANCELLED→ikisi de; null buyer→atlanır |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (yeni) | ✓ 11/11 | Recorder 2 + Refresher 3 + PayoutCompleted 6 (5 mevcut + 1 yeni COMPLETED-recompute) |
| Transactions.Tests (tam) | ✓ 801/801 | unit + integration (SQL Server, Docker UP); timeout-atfı integration testi dahil |
| Steam.Tests | ✓ 106/106 | SteamWebhookHandler cancel-path + dispatch |
| Fraud.Tests | ✓ 91/91 | FraudFlagService approve/reject + scanner |
| API.Tests | ✓ 523/523 | endpoint flow'ları (lifecycle/steam/blockchain webhook) — Reset() fix sonrası |
| Disputes / Users / Platform | ✓ 39 / 22 / 187 | regresyon yok |
| Build | ✓ Solution succeeded | Debug |
| Format | ✓ `dotnet format --verify-no-changes` EXIT=0 | tüm solution |

**Lokal toplam: 1769 test (7 proje) PASS.** Auth/Notifications/Realtime/Shared dokunulmadı → CI-authoritative.

## Doğrulama

| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız validator (ayrı chat, 2026-06-19, kendi verdict'i rapor görülmeden) |
| Yapım-içi self-review | 0 bloke-edici (idempotency status-guard'larla; DI tam container boot'ta API.Tests ile doğrulandı; tüm 12 caller yeşil testlerle exercise edildi) |

**BAĞIMSIZ VALIDATOR — ✓ PASS (4/4 AC, 0 bloke-edici, 4 non-blocking).**

**Kapılar:** Adım -1 temiz · Adım 0 main son-3 success (`27839380260`/`27839380245`/`27834306362`) · Adım 0b memory mevcut · Adım 8a task CI HEAD `6af9ae0` run [`27847024321`](https://github.com/turkerurganci/Skinora/actions/runs/27847024321) **tüm job success** (Lint/Build/Unit/**Integration**/Contract/**Migration dry-run**/Docker/Gate).

**Validator-çalıştırıldı:** `dotnet build -c Release` **0W/0E** · `dotnet format --verify-no-changes` **EXIT=0** · yeni WP15 unit testleri (Recorder/Refresher/PayoutCompleted) **11/11** · Transactions `Category=Unit` **108/108**. Integration (timeout-atfı + History yazımı + 3 Reset-fix) lokal Docker yok → **CI-authoritative** (Integration job yeşil).

**Bağımsız kod/spec teyidi:**
- **Bağımlılık zinciri** — `ReputationAggregator` ve `CancelCooldownEvaluator` CANCELLED_TIMEOUT atfını **yalnız** `TransactionHistory.PreviousStatus`'tan çözüyor (`OrderByDescending(CreatedAt).First()`); satır yoksa `previousStatusByTx` boş → timeout iptali numerator+denominator dışı kalır. Kritik fix iddiası prensipte doğrulandı; `ExecutePaymentTimeout_..._To_Buyer` integration testi gerçek `ReputationAggregator` + gerçek History okumasıyla uçtan uca kanıtlıyor (buyer `rate=0.0` sorumlu, seller `null`, History `PreviousStatus=ITEM_ESCROWED`/Trigger="Timeout"/SYSTEM).
- **12 caller** firsthand diff incelendi: `previousStatus` her zaman `Fire()`'dan önce yakalanmış; genesis `PreviousStatus=null`+`GenesisTrigger="Create"`; actor haritası (USER/SYSTEM/ADMIN) tutarlı, SYSTEM→`SeedConstants.SystemUserId` (06 §8.5 sentinel + §3.6 ActorType-ActorId invariantı).
- **Merkezi yazım invariantı (06 §3.6):** "audit kaydı tek merkezi method üzerinden, doğrudan INSERT yasak" → `TransactionHistoryRecorder` static helper bunu karşılıyor.
- **`ApplyStatusChangeAsync`** 3 çağıranın (Declined/Expired/Cancelling) hepsi iptal-sınıfı trigger geçiriyor (`cancelReason` hep dolu) → `evaluateCooldown:true` semantik doğru, forward yol yok.
- **Recompute sıralaması** (Fire→History→flush→Refresh→save) dedicated-scope caller'larda `BeginTransactionAsync` ile atomik; `AsNoTracking` okuma flush sonrası transaction-içi flushed satırı görür. Retry-strategy yok varsayımı CI Integration job'unun SQL Server'da geçmesiyle doğrulandı.
- **Güvenlik temiz:** yeni dependency YOK (csproj diff boş), migration YOK (Migrations/ diff boş + dry-run yeşil), secret yok, yeni auth/endpoint yüzeyi yok.

**Validator net-yeni non-blocking (rapor değinmedi):**
- **N1 (S3, çok düşük olasılık):** `PayoutCompletedConsumer` yorumu "retry'da self-heal eder" (§Yapılan İşler) tam doğru değil — ilk `SaveChanges` (COMPLETED commit) başarılı + ikinci `SaveChanges` (reputation) başarısız olursa, retry'da `Status != ITEM_DELIVERED` guard'ı recompute'tan **önce** kısa-devre yapar → bu event'in retry'ında recompute çalışmaz. Pratikte düzelme kullanıcının **sonraki** reputation-etkileyen olayında olur (aggregator sıfırdan yeniden hesaplar). Denormalize/türetilen alan, para/state etkisi yok, eventual-consistent (06 §8.2) → bloke etmez; yorum hassasiyeti WP17.

**Rapordaki 3 known-limitation bağımsız teyit edildi** (trigger enum-adı vs 06 §3.6 "ör:" geçmiş-zaman → WP17 · `AdditionalData` null · REFUNDED 06 §3.1 haritasında yok → spec-sadık). Yapım raporuyla tam uyumlu.

## Altyapı Değişiklikleri

- **Migration:** YOK (TransactionHistory tablosu InitialCreate'ten beri mevcut; entity/şema değişmedi — yalnız yazım eklendi).
- **Config/env:** Yok.
- **Docker:** Yok.
- **Yeni dependency:** Yok.
- **DI:** `ITransactionReputationRefresher` → `TransactionReputationRefresher` (`TransactionsModule`); 4 terminal caller'a constructor param.

## Test-Infra Düzeltmesi (Reset FK)

Genesis/transition History yazımı 3 API endpoint factory'sinin `Reset()`'ini kırdı:
`TransactionHistory.ActorId → User` ve `→ Transaction` FK'leri **NO ACTION** (06 §3.6 "audit asla silinmez"). Önceden hiç History satırı yazılmadığından Reset'ler bu tabloyu temizlemiyordu; artık satırlar var → Transaction/User silmeye çalışınca FK bloke ediyordu. Düzeltme (yalnız test):
- 3 `Reset()`'e (Lifecycle/Blockchain/Steam) `TransactionHistory.ExecuteDelete()` parent'lardan önce eklendi (IAppendOnly → ChangeTracker `RemoveRange` `EnforceAppendOnly`'ye takılırdı; `ExecuteDelete` guard'ı bypass eder).
- Steam `Reset()` SYSTEM user'ı siliyordu → SYSTEM-actor History (`ActorId=SystemUserId`) FK-fail → 500; Blockchain/Lifecycle emsali ile SystemUserId korundu.

## Commit & PR

- Branch: `task/WP15-reputation-trigger`
- Commit: `144f12e` (kod+test) + `5079e0e` (PR no docs)
- PR: [#188](https://github.com/turkerurganci/Skinora/pull/188)
- CI: ✓ PASS — HEAD `5079e0e` run [`27846588488`](https://github.com/turkerurganci/Skinora/actions/runs/27846588488) **tüm job success** (Lint/Build/Unit/**Integration**/Contract/Migration dry-run/Docker/Gate)

## Known Limitations / Follow-up

- **Trigger string formatı:** enum adları kullanıldı ("Complete"/"Timeout"/"BuyerAccept"…; genesis "Create"). 06 §3.6 örnekleri ("BuyerAccepted"/"TimeoutExpired") geçmiş-zaman — non-normatif "ör:", non-blocking → istenirse WP17 doc-precision.
- **`AdditionalData`:** tüm satırlarda null (MVP için yeterli).
- **REFUNDED (dispute buyer-favor) reputation:** 06 §3.1 sorumluluk haritasında yok → reputation'a dahil edilmedi (yalnız audit History). Spec gereği doğru; davranış değişimi gerekirse ayrı karar.

## Notlar

- **Working tree (Adım -1):** temiz (WP14 sonrası).
- **Main CI startup (Adım 0):** son-3 run success (`27839380260`/`27839380245`/`27834306341`).
- **Repo memory (Adım 0b):** mevcut.
- **Dış varsayım kontrolü:** İç-varsayım = "TransactionHistory hiç yazılmıyor"; `grep` ile doğrulandı (prod kodda 0 yazım, yalnız aggregator/cooldown okuyor). T43 raporu (satır 222-224) "T44 state machine her transition için TransactionHistory yazacak" forward-commitment'ini hiç onurlandırmamıştı — bu WP15 onu kapatır.
- **Backlog:** `reputation-aggregator-trigger` ✅ Çözüldü.
