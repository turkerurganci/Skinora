# WP16 — Monitoring/health probe + uptime heartbeat

**Faz:** Pre-F6 (P5 — Config/altyapı) | **Durum:** ✓ Tamamlandı (bağımsız validator PASS) | **Tarih:** 2026-06-20

---

## Kapsam kararı (keşif → owner)

8-ajanlı keşif workflow'u WP16'nın *literal* kapsamının çoğunun **zaten yapılmış** olduğunu (stale backlog) kanıtladı:

| Alt-alan | Keşif verdict | Aksiyon |
|---|---|---|
| Uptime heartbeat tablo/job | **ALREADY_DONE** (T47: entity + singleton CHECK + seed + 30sn self-reschedule job + testler) | Verify-only |
| Restart-recovery (outage→deadline uzatma) | **PARTIAL** — çekirdek var, audit + resume-gate eksik | **D2** |
| Webhook idempotency | **ALREADY_DONE** (DB `ProcessedNonce`, T68; spec 09 §11.3 *tabloyu* ister) | Yok ("Redis" varyantı post-MVP) |
| platformUptimePercent | **BY_DESIGN** sabit (07 §10.1 kaynak belirtmez, örnek değer) | Yok (sabit kalır) |
| DROPPED metrik / mempool PaymentDetected | **POST_MVP / BY_DESIGN** (DROPPED enum/state yok, 05 §9.2 zorunlu kılmaz) | Yok |
| Steam/blockchain health-probe → auto-freeze | **MVP_GAP** (aktif probe yok; 07 §2481 auto-detect'i WP16'ya bırakmış) | **D3** |
| **ITEM_ESCROWED ödeme-timeout arming** | **MVP_GAP** — production defekti (keşif sırasında bulundu) | **D1** |

**Owner kararları (AskUserQuestion 2026-06-20):**
- **Q1 (ödeme-timeout):** WP16 içinde, **per-tx Hangfire job** (06 §3.5 invariant'ı karşılar).
- **Q2 (health-probe):** **Alert-only** — probe outage tespit edince admin'e ALERT; freeze WP7 motoruyla manuel.
- **Q3 (restart-recovery):** **Audit log + gerçek resume-gate**.
- Düşük öncelik (owner itiraz etmedi): platformUptimePercent sabit kalır; DROPPED + Redis idempotency + Redis cache scale-out post-MVP; heartbeat interval const kalır.

---

## Yapılan İşler

### D1 — ITEM_ESCROWED ödeme-timeout arming (production defekti düzeltildi)
- **Bulgu:** `SchedulePaymentTimeoutAsync` production'da **0 çağıran**; `PaymentDeadline`'ın ilk değerini kuran yer yok → ödeme yapmayan alıcı **hiç timeout almıyor**, ödeme-timeout uyarısı (T48/WP12 ratio) **hiç ateşlenmiyor**. 06 §3.5 ITEM_ESCROWED invariant'ı (`PaymentDeadline` + `PaymentTimeoutJobId` NOT NULL) ihlal.
- **Arm:** `SteamWebhookHandler.AcceptEscrowAsync` — `Fire(EscrowItem)` sonrası `PaymentDeadline = clock.now + PaymentTimeoutMinutes` (servis-katmanı konvansiyonu, `AcceptDeadline` emsali) + `ITimeoutSchedulingService.SchedulePaymentTimeoutAsync` (per-tx timeout + warning job kurulur). `SaveChanges`'e en yakın yere konuldu (atomicity penceresi min; `DeadlineScannerJob` belt-and-suspenders fallback).
- **Cancel-on-payment:** `AmountValidationService.AdvanceStateMachineAsync` — `Fire(ConfirmPayment)` sonrası `CancelTimeoutJobsAsync` (iptal konvansiyonuyla aynı: payment job silinir; warning job zaten self-guard'lı no-op).

### D2 — Restart-recovery audit + gerçek resume-gate (05 §4.4:533-536)
- **Audit:** `RestartRecoveryService` → `IAuditLogger` inject; outage-üstü uzatma sonrası 1 özet `TIMEOUT_AUTO_EXTENDED` satırı (SYSTEM, `EntityType=SystemHeartbeat`, NewValue `{outageSeconds, extendedCount, rescheduledPaymentJobs}`). Yeni `AuditAction.TIMEOUT_AUTO_EXTENDED` → `AuditLogCategoryMap` ADMIN_ACTION (MAINTENANCE_MODE_CHANGED yanı).
- **Resume-gate:** `AddHangfireServer` `AddHangfireModule`'den ayrılıp yeni `AddHangfireProcessingServer`'a taşındı; `Program.cs`'te `TimeoutSchedulerStartupHook`'tan **sonra** kaydedildi. Hosted-service StartAsync sırası = kayıt sırası → cold-start'ta recovery deadline'ları uzatıp job'ları yeniden kurar, **sonra** worker kuyruğu işlemeye başlar. Client (`IBackgroundJobScheduler`) erken kalır (priming hook'lar enqueue edebilsin).

### D3 — Health-probe → admin alert (alert-only)
- `HealthProbeOptions` (appsettings: `Enabled`, `ProbeCron="* * * * *"`, `FailureThreshold=3`).
- `SidecarHealthClient` — Steam + blockchain sidecar `/health`'i `X-Internal-Key` ile yoklar; başarısızlık/timeout=down, baseUrl yoksa **null=izlenmiyor** (yanlış alarm yok).
- `PlatformHealthMonitorState` (singleton) — per-component edge-detection: alert **healthy→degraded** ve **degraded→healthy** geçişlerinde birer kez (her başarısız poll'da değil).
- `PlatformHealthProbeJob` (recurring, `HealthProbeRegistrar`) — geçişte `PLATFORM_OUTAGE_DETECTED` audit (SECURITY_EVENT) + `PlatformOutageAlertEvent` (outbox) → `PlatformOutageAdminNotificationConsumer` (WP8 admin-broadcast deseni) → `ADMIN_PLATFORM_OUTAGE` in-app bildirim (tüm adminler). **Otomatik freeze yok.**
- Yeni `NotificationType.ADMIN_PLATFORM_OUTAGE` + `AuditAction.PLATFORM_OUTAGE_DETECTED`; resx + `EmailCategoryMap`(Account) + `NotificationTargetMapper` + FE enum sync.

## Etkilenen Modüller / Dosyalar

**Yeni:** `Skinora.Shared/Events/PlatformOutageAlertEvent.cs` · `Skinora.Notifications/.../EventHandlers/PlatformOutageAdminNotificationConsumer.cs` · `Skinora.API/Monitoring/{HealthProbeOptions, PlatformHealthMonitorState, ISidecarHealthClient, SidecarHealthClient, IPlatformHealthProbeJob, PlatformHealthProbeJob, HealthProbeRegistrar}.cs` · testler (`PlatformHealthMonitorStateTests`, `PlatformHealthProbeJobTests`).

**Değişen (src):** `SteamWebhookHandler.cs` · `AmountValidationService.cs` · `RestartRecoveryService.cs` · `HangfireModule.cs` · `Program.cs` · `appsettings.json` · `AuditAction.cs` · `NotificationType.cs` · `AuditLogCategoryMap.cs` · `EmailCategoryMap.cs` · `NotificationTargetMapper.cs` · `NotificationTemplates.resx` · FE `types/enums.ts` · FE `lib/utils/notification-icons.ts`.

**Değişen (test):** `SteamWebhookHandlerTests.cs` · `AmountValidationServiceTests.cs` · `RestartRecoveryServiceTests.cs` · `EnumTests.cs` · `AuditLogCategoryMapTests.cs`.

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | ITEM_ESCROWED'de ödeme-timeout + warning kurulur | ✓ | `SteamWebhookHandlerTests.TradeOfferAccepted_OnEscrowDirection...` (PaymentDeadline + PaymentTimeoutJobId NotNull) — Steam 106/106 |
| 2 | Ödeme gelince timeout job iptal edilir | ✓ | `AmountValidationServiceTests` exact-amount → `Cancelled` içerir; underpayment → içermez — Tx 801/801 |
| 3 | Restart-recovery auto-uzatmayı audit'ler (05 §4.4:536) | ✓ | `RestartRecoveryServiceTests` above-threshold → `TIMEOUT_AUTO_EXTENDED` SYSTEM satırı; below → yok — API 531/531 |
| 4 | Resume-gate: worker recovery'den sonra başlar | ✓ | `Program.cs` `AddHangfireProcessingServer` `TimeoutSchedulerStartupHook`'tan sonra; host boot 32 WebApplicationFactory testi yeşil (API 531/531) |
| 5 | Health-probe outage→admin alert (alert-only) | ✓ | `PlatformHealthProbeJobTests` DEGRADED+RECOVERED audit+event; `PlatformHealthMonitorStateTests` edge-detection 6/6 |
| 6 | Enum parity (BE+FE) | ✓ | EnumTests 207, AuditLogCategoryMapTests 40, EmailCategoryMapTests 8, FE tsc 0/eslint 0 |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Transactions | ✓ 801/801 | `dotnet test` full |
| Skinora.API | ✓ 531/531 | full (WebApplicationFactory host-boot dahil → Program.cs reorder doğrulandı) |
| Notifications | ✓ 153/153 | full |
| Platform | ✓ 189/189 | full |
| Steam | ✓ 106/106 | full |
| Shared (EnumTests) | ✓ 207/207 | parity |
| FE | ✓ tsc 0 / eslint 0 | `tsc --noEmit`; enums.ts + notification-icons.ts |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ **PASS** — bağımsız validator (ayrı chat, 2026-06-20, rapor görülmeden kendi verdict'i) |
| Bulgu sayısı | 0 bloke-edici (4 non-blocking gözlem O1–O4) |
| Düzeltme gerekli mi | Hayır |
| Yapım-içi self-check | Build 0W/0E · dokunulan 5 BE suite + FE tsc/eslint yeşil |
| Yapım-içi adversarial review (6-boyut/refute-default workflow) | 2 ham → **1 onaylı S2 + 1 NOTE**; S2 düzeltildi |

### Bağımsız Doğrulama (validator, ayrı chat — 2026-06-20)

**Verdict: ✓ PASS** — 6/6 kabul kriteri ✓, 0 bloke-edici bulgu. Validator kendi verdict'ini yapım raporunu görmeden oluşturdu; rapor sonradan okunduğunda **tam uyumlu** (uyuşmazlık yok).

**Kapılar:** Adım -1 working tree temiz · Adım 0 main son-3 success (`27848423788`/`27848423798`/`27839380260`) · Adım 0b repo memory WP16 satırı mevcut · Adım 8a task CI HEAD `c2df968` run [`27852551614`](https://github.com/turkerurganci/Skinora/actions/runs/27852551614) **11/11 job success** (Lint/Build/Unit/**Integration**/Contract/Migration dry-run/Docker BE+FE/Gate); PR #189 MERGEABLE.

**Validator-çalıştırıldı (firsthand, current branch HEAD `c2df968`):** `dotnet build Skinora.sln -c Release` **0W/0E** · Shared `~EnumTests` **207/207** · Platform `~AuditLogCategoryMap` **40/40** · API `~PlatformHealthMonitorState` **9/9** · Transactions `~AmountValidationServiceTests` **9/9**. Integration suite (PlatformHealthProbeJob/RestartRecovery/SteamWebhook) CI-authoritative (run `27852551614` Integration job yeşil).

**Bağımsız kod/spec teyidi:**
- **D1 defekti teyit edildi:** `SchedulePaymentTimeoutAsync` main'de **0 prod çağıran** (`git grep` ile doğrulandı — yalnız interface/doc referansı); branch'te tek yeni çağıran `SteamWebhookHandler.cs:528`. Arm `PaymentDeadline = now + PaymentTimeoutMinutes` + `SchedulePaymentTimeoutAsync` → `PaymentTimeoutJobId` (06 §3.5 invariant `ITEM_ESCROWED → PaymentDeadline + PaymentTimeoutJobId NOT NULL` birebir). Cancel: `CancelTimeoutJobsAsync` ConfirmPayment'ta — `PaymentTimeoutJobId` state-machine OnExit'te **resetlenmiyor** (tüm atamalar yalnız `TimeoutSchedulingService`/`TimeoutFreezeService`'te → grep ile teyit, orphan-job yok).
- **D2 resume-gate teyidi:** `AddHangfireServer` tek çağıran (`AddHangfireProcessingServer`); `Program.cs` kayıt sırası `TimeoutSchedulerStartupHook` (317) → `AddHangfireProcessingServer` (328); `StartAsync` recovery'i `await` ile tamamlar; `ServicesStartConcurrently` override yok → default sıralı host start → gate geçerli. Audit `TIMEOUT_AUTO_EXTENDED` heartbeat SaveChanges ile atomik (sub-threshold'da yazılmaz).
- **D3 alert-only + atomiklik teyidi:** `IAuditLogger.LogAsync` + `IOutboxService.PublishAsync` **stage-only** (firsthand okundu — yalnız `Add`, SaveChanges yok) → audit+outbox tek `SaveChangesAsync` ile atomik; S2-fix `Revert` mantığı doğru (Degraded→`InOutage=false`, Recovered→`InOutage=true`; edge re-detect). Otomatik freeze yok.
- **Migration yok:** `.csproj` diff boş (0 yeni dep), Migrations/ diff boş, CI Migration dry-run yeşil (model drift yok), enum'lar sona eklendi (CHECK'siz, WP8 emsali).
- **Güvenlik temiz:** yeni HTTP endpoint yok, secret yok, yeni kullanıcı girdisi yok, `X-Internal-Key` mevcut sidecar config'inden.

**Non-blocking gözlemler (validator):**
- **O1** — Restart-recovery audit, outage eşiği aşıldığında `extendedCount=0` (aktif tx yoksa) olsa bile yazılır; outage-recovery pass'ini kaydetmek savunulabilir, gürültü minimal.
- **O2** — Health-state singleton tek-instance (Redis scale-out post-MVP, dokümante).
- **O3** — D1 Hangfire enqueue ↔ outer SaveChanges arası rezidüel atomiklik penceresi (orphan job fire'da no-op; `DeadlineScannerJob` fallback; mevcut konvansiyonla tutarlı).
- **O4** — FE prettier drift (enums.ts/notification-icons.ts main'de zaten uyumsuz; CI prettier zorlamıyor; WP18). tsc/eslint temiz.

**Adversarial self-review (yapım-içi, validator'dan önce):** 4-boyut refute-default workflow + verify. **S2 (onaylı, düzeltildi):** `PlatformHealthMonitorState.InOutage` durable SaveChanges'ten önce mutate ediliyordu → SaveChanges geçici hata verirse audit+outbox rollback olur ama singleton "alerted" kalır → retry'da `Record` `None` döner → **DEGRADED alert kalıcı kaybolur**. **Fix:** `PlatformHealthMonitorState.Revert(component, transition)` (edge'i geri al → sonraki probe yeniden tespit eder) + `ProbeAsync` SaveChanges try/catch → hata durumunda revert + LogError (recurring job sonraki tick'te yeniden tespit eder). Testler: 3 revert-kontrat unit + 1 job-seviyesi failure integration (`ThrowingOnceDbContext` → run1 fail+revert, run2 fresh-scope re-detect+persist; scoped-per-run prod gerçekliği modellendi). **NOTE (gerçek değil, bırakıldı):** `[DisableConcurrentExecution]` yok — lock atomik edge-detection çift-alert'i imkânsız kılar + recurring job'ların çoğu zaten kullanmıyor.

## Altyapı Değişiklikleri
- **Migration: YOK** — tüm kolonlar mevcut; `AuditAction`/`NotificationType` değerleri **sona eklendi** (CHECK constraint yok, WP8 emsali → şema değişmez).
- **Config:** yeni `HealthProbe` appsettings bölümü (`Enabled`/`ProbeCron`/`FailureThreshold`, hepsi default'lu — opsiyonel).
- **Docker:** değişiklik yok.
- **Startup sırası:** Hangfire processing server artık restart-recovery hook'tan sonra başlar (resume-gate).

## Commit & PR
- Branch: `task/WP16-monitoring-timeout`
- PR: [#189](https://github.com/turkerurganci/Skinora/pull/189)
- CI: ✓ **Task CI HEAD `0da488b` run [`27852231807`](https://github.com/turkerurganci/Skinora/actions/runs/27852231807) tüm job success** (Lint/Build/Unit/Integration/Contract/Migration dry-run/Docker/Gate). Önceki kod-tam run `61587af`/`27851797777` de success.

## Known Limitations / Follow-up
- **Health-probe state singleton** = tek-instance MVP; multi-instance Redis paylaşımı **post-MVP** (PRE_F6_PLAN §3). Restart'ta state sıfırlanır → en kötü hâlâ-degraded bileşen için bir kez yeniden alarm (kabul edilebilir).
- **Telegram probe:** MVP-dışı bırakıldı (freeze semantiği yok, salt-gözlemlenebilirlik) — owner'a bildirildi.
- **FE prettier drift:** `enums.ts` + `notification-icons.ts` main'de zaten prettier-uyumsuzdu (WP18 `prettier-drift` kalemi; CI henüz zorlamıyor); eklenen satırlar dosya-içi stille birebir, tsc+eslint temiz.
- **platformUptimePercent** sabit kalır; gerçek hesaplama (outage-history tablosu + provider) post-MVP.

## Notlar
- **Working tree (Adım -1):** temiz.
- **Main CI startup (Adım 0):** son 3 run `success` (27848423788/27848423798/27839380260 — WP15/WP14).
- **Dış varsayımlar (Adım 4):** sidecar `/health` endpoint'leri mevcut (CONTEXT.md HealthController) + backend→sidecar config (`SteamSidecar`/`BlockchainSidecar` BaseUrl+InternalKey) mevcut; yeni dış bağımlılık yok. Enum saklama biçimi CHECK'siz (WP8 emsali doğrulandı → migration yok).
