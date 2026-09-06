# Skinora — AI Context

Skinora: CS2 item ticaretinde alıcı ve satıcı arasında güvenli, otomatik bir escrow platformu.

---

## Dosya Haritası

### Proje Konfigürasyonu

| Dosya | İçerik |
|---|---|
| `CLAUDE.md` | AI giriş noktası — alt dosya referansları |
| `.claude/CONTEXT.md` | Bu dosya — proje bağlamı ve dosya haritası |
| `.claude/INSTRUCTIONS.md` | AI çalışma talimatları |
| `.claude/GUARDRAILS.md` | AI sınırları ve yasakları |
| `.claude/PROMPTS.md` | Prompt kütüphanesi |
| `.claude/skills/checkpoint.md` | `/checkpoint` skill — aşama doğrulama |
| `.claude/skills/handoff.md` | `/handoff` skill — chat geçişi |
| `.claude/skills/deep-review.md` | `/deep-review` skill — 8 katmanlı doküman kalite analizi |
| `.claude/skills/audit.md` | `/audit` skill — envanter bazlı sistematik doküman denetimi |
| `.claude/skills/gpt-cross-review.md` | `/gpt-cross-review` skill — GPT o3 ile ikinci AI review döngüsü (doküman bazlı, round'lu) |
| `.claude/skills/gorus.md` | `/gorus` skill — anlık ikinci görüş; soruyu hazırlar, onayla gönderir, cevabı getirir, karar sahibinde |
| `.claude/skills/task.md` | `/task` skill — implementation yapım chat'i başlatma |
| `.claude/skills/validate.md` | `/validate` skill — implementation doğrulama chat'i |
| `.claude/skills/gate-check.md` | `/gate-check` skill — faz sonu doğrulama |

### Proje Dokümanları

| Dosya | İçerik |
|---|---|
| `Docs/00_PROJECT_METHODOLOGY.md` | Proje metodolojisi — tüm aşamaların yol haritası |
| `Docs/01_PROJECT_VISION.md` | Ürün vizyonu, problem, hedef, konumlandırma |
| `Docs/02_PRODUCT_REQUIREMENTS.md` | Tüm iş kuralları ve ürün kararları |
| `Docs/03_USER_FLOWS.md` | Her aktörün adım adım deneyimi |
| `Docs/04_UI_SPECS.md` | Ekran bazında UI tanımları |
| `Docs/05_TECHNICAL_ARCHITECTURE.md` | Sistem mimarisi ve teknoloji kararları |
| `Docs/06_DATA_MODEL.md` | Entity'ler, ilişkiler, şema |
| `Docs/07_API_DESIGN.md` | Endpoint'ler, request/response yapıları |
| `Docs/08_INTEGRATION_SPEC.md` | Üçüncü parti servis entegrasyonları |
| `Docs/09_CODING_GUIDELINES.md` | Kod standartları, klasör yapısı |
| `Docs/10_MVP_SCOPE.md` | MVP kapsamı ve sınırları |
| `Docs/11_IMPLEMENTATION_PLAN.md` | Sıralı task listesi ve bağımlılıklar |
| `Docs/12_VALIDATION_PROTOCOL.md` | Doğrulama kuralları ve cross-check |
| `Docs/PRODUCT_DISCOVERY_STATUS.md` | Tüm ürün kararlarının kayıt dosyası |
| `Docs/IMPLEMENTATION_STATUS.md` | Implementation ilerleme tablosu (tüm task'lar) |
| `Docs/TASK_REPORTS/` | Task bazlı detaylı raporlar (TXX_REPORT.md) |
| `Docs/AUDIT_REPORTS/` | Doküman audit raporları (00-12) |
| `Docs/GPT_REVIEW_REPORTS/` | GPT cross-review raporları (round bazlı, doküman denetimi) |
| `Docs/GPT_OPINIONS/` | `/gorus` kayıtları — tek soruluk anlık GPT görüşleri ve sahibinin kararı |
| `Docs/CHECKPOINT_REPORTS/` | Checkpoint raporları (CP1-CP18) + Gate Check raporları (F0) |

### Transactions Modülü (T19–T20)

| Dosya | İçerik |
|---|---|
| `backend/src/Modules/Skinora.Transactions/Domain/Entities/Transaction.cs` | Transaction entity — 06 §3.5 birebir, ~50+ field |
| `backend/src/Modules/Skinora.Transactions/Domain/Entities/TransactionHistory.cs` | TransactionHistory entity — 06 §3.6, append-only audit trail |
| `backend/src/Modules/Skinora.Transactions/Domain/Entities/PaymentAddress.cs` | PaymentAddress entity — 06 §3.7, 1:1 Transaction, soft delete |
| `backend/src/Modules/Skinora.Transactions/Domain/Entities/BlockchainTransaction.cs` | BlockchainTransaction entity — 06 §3.8, 17 field, type/status semantiği |
| `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionConfiguration.cs` | EF Core config — 9 check constraint, filtered index, FK'ler |
| `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionHistoryConfiguration.cs` | EF Core config — IDENTITY PK, FK'ler, index |
| `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/PaymentAddressConfiguration.cs` | EF Core config — 3 unique index, 1 filtered (MonitoringStatus), FK'ler |
| `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/BlockchainTransactionConfiguration.cs` | EF Core config — 9 CHECK constraint (5 type + 4 status), filtered unique TxHash, 3 perf index |
| `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/TransactionsModuleDbRegistration.cs` | Modül assembly kaydı |

### Teslimat Doğrulama (T125 · T126 · T127)

| Dosya | İçerik |
|---|---|
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryVerificationService.cs` | 02 §9.2 kanıt motoru — **saf/yan etkisiz**, polling'e hazır; kilit durumunu okumaz |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryVerificationResult.cs` | `DeliveryVerdict` (5 değer) + kanıt capture payload'ı |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryVerificationService.cs` | Port — çağıranlar T126 (✓) / T127 / T130 |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryEvidenceCaptureRecorder.cs` | Launch kapısı audit satırını çağıranın `SaveChanges`'ine ekler (statik helper) |
| `backend/src/Modules/Skinora.Transactions/Domain/Entities/DeliveryEvidenceCapture.cs` | 06 §3.5a — append-only kanıt kaydı (DEPLOY_RUNBOOK §H) |
| `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/DeliveryEvidenceCaptureConfiguration.cs` | EF config — long IDENTITY, FK NO ACTION, 2 index, `Evidence` int (flags) |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryConfirmationService.cs` | **T126** — 07 §7.6b alıcı onayı; `PAYMENT_RECEIVED → ITEM_DELIVERED`'ın tek üreticisi. Kanıtı **önce** işler, sonra motoru çağırır (short-circuit → sıfır Steam okuması) |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryConfirmationService.cs` | Port — çağıran `TransactionsController.ConfirmReceipt` |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryConfirmationDtos.cs` | `ConfirmReceiptResponse` / `ConfirmReceiptOutcome` / `ConfirmReceiptStatus` (5 değer) |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/DeliveryTimeoutRound.cs` | **T127** — 05 §4.4 timeout öncesi doğrulama turu; 5 verdict → 3 aksiyon. İptali yetkilendiren tek koşul: satıcı envanteri okundu **ve** item hâlâ orada |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryTimeoutRound.cs` | Port + `DeliveryTimeoutDecision` (`Delivered` / `Cancel` / `Held`). Çağıran `DeadlineScannerJob` |
| `backend/src/Modules/Skinora.Transactions/Application/Delivery/IDeliveryMisdeliveryEscalator.cs` | **T127** — yanlış-teslimat eskalasyon portu. Adapter Disputes'te (bağımlılık yönü Disputes → Transactions) |
| `backend/src/Modules/Skinora.Disputes/Application/Disputes/MisdeliveryDisputeEscalator.cs` | Adapter — SYSTEM tarafından açılan `DELIVERY`/`ESCALATED` dispute; filtresiz UQ nedeniyle mevcut satırı yükseltir |

### Steam Modülü (T21 · T117'de salt-okunur proxy'ye küçüldü)

**v3.0 (P2P):** Bot custody katmanı T117'de silindi — `TradeOffer`, `PlatformSteamBot`, `BotRecoveryItem` entity'leri, bot seçimi, dispatch, recovery ve Steam webhook yüzeyi yok. Modülün kalan görevi **envanter okuma** (teslimat doğrulamasının temeli, 02 §9.2) ve **trade-hold probu** (alıcı MA doğrulaması). **T133'te sidecar yarısı da kapandı:** `sidecar-steam` salt-okunur bir proxy'dir — bot havuzu, trade offer gönderimi/takibi ve webhook yayıncısı silindi, hiçbir Steam hesabı kimlik bilgisi taşımaz (tek credential `STEAM_API_KEY`).

| Dosya | İçerik |
|---|---|
| `backend/src/Modules/Skinora.Steam/Application/Inventory/SteamInventoryQueryService.cs` | Envanter sorgu servisi — sidecar üzerinden |
| `backend/src/Modules/Skinora.Steam/Application/Inventory/SidecarSteamInventoryReader.cs` | `ISteamInventoryReader` port implementasyonu (cross-module) |
| `backend/src/Modules/Skinora.Steam/Application/Inventory/HttpSteamSidecarInventoryClient.cs` | Sidecar HTTP client — envanter + cache invalidation |
| `backend/src/Modules/Skinora.Steam/Application/Inventory/HttpSteamTradeHoldClient.cs` | `GetTradeHoldDurations` — Mobile Authenticator doğrulaması (08 §2.2) |
| `backend/src/Modules/Skinora.Steam/Infrastructure/Persistence/SteamModuleDbRegistration.cs` | Modül assembly kaydı |

### Disputes Modülü (T22)

| Dosya | İçerik |
|---|---|
| `backend/src/Modules/Skinora.Disputes/Domain/Entities/Dispute.cs` | Dispute entity — 06 §3.11, 14 field, soft delete |
| `backend/src/Modules/Skinora.Disputes/Infrastructure/Persistence/DisputeConfiguration.cs` | EF Core config — 1 CHECK (CLOSED→ResolvedAt), unfiltered unique (TransactionId+Type), 3 FK, 2 perf index |
| `backend/src/Modules/Skinora.Disputes/Infrastructure/Persistence/DisputesModuleDbRegistration.cs` | Modül assembly kaydı |

### Fraud Modülü (T22)

| Dosya | İçerik |
|---|---|
| `backend/src/Modules/Skinora.Fraud/Domain/Entities/FraudFlag.cs` | FraudFlag entity — 06 §3.12, 13 field, soft delete |
| `backend/src/Modules/Skinora.Fraud/Infrastructure/Persistence/FraudFlagConfiguration.cs` | EF Core config — 4 CHECK (scope + review), 3 FK, 3 perf index |
| `backend/src/Modules/Skinora.Fraud/Infrastructure/Persistence/FraudModuleDbRegistration.cs` | Modül assembly kaydı |

### Platform Modülü (T25)

| Dosya | İçerik |
|---|---|
| `backend/src/Modules/Skinora.Platform/Domain/Entities/SystemSetting.cs` | SystemSetting entity — 06 §3.17, 7 field, admin-yönetimli platform parametresi |
| `backend/src/Modules/Skinora.Platform/Domain/Entities/SystemHeartbeat.cs` | SystemHeartbeat entity — 06 §3.23, singleton (Id=1 CHECK), uptime takibi |
| `backend/src/Modules/Skinora.Platform/Domain/Entities/AuditLog.cs` | AuditLog entity — 06 §3.20, 11 field, IAppendOnly (immutable audit trail) |
| `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemSettingConfiguration.cs` | EF Core config — DataType CHECK ('int','decimal','bool','string'), UQ Key, Category perf index |
| `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/SystemHeartbeatConfiguration.cs` | EF Core config — singleton CHECK (Id = 1), ValueGeneratedNever |
| `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/AuditLogConfiguration.cs` | EF Core config — long IDENTITY, 5 perf index (ActorId, UserId, EntityType+EntityId, Action, CreatedAt) |
| `backend/src/Modules/Skinora.Platform/Infrastructure/Persistence/PlatformModuleDbRegistration.cs` | Modül assembly kaydı |

### Payments Modülü — ColdWalletTransfer (T25)

| Dosya | İçerik |
|---|---|
| `backend/src/Modules/Skinora.Payments/Domain/Entities/ColdWalletTransfer.cs` | ColdWalletTransfer entity — 06 §3.22, 8 field, IAppendOnly (hot→cold ledger) |
| `backend/src/Modules/Skinora.Payments/Infrastructure/Persistence/ColdWalletTransferConfiguration.cs` | EF Core config — long IDENTITY, UQ TxHash, FK User (InitiatedByAdminId) |
| `backend/src/Modules/Skinora.Payments/Infrastructure/Persistence/PaymentsModuleDbRegistration.cs` | Modül assembly kaydı |

### Transactions — SellerPayoutIssue (T25)

| Dosya | İçerik |
|---|---|
| `backend/src/Modules/Skinora.Transactions/Domain/Entities/SellerPayoutIssue.cs` | SellerPayoutIssue entity — 06 §3.8a, 10 field, workflow record (RESOLVED = frozen) |
| `backend/src/Modules/Skinora.Transactions/Infrastructure/Persistence/SellerPayoutIssueConfiguration.cs` | EF Core config — state-dependent CHECK (ESCALATED/RESOLVED/RETRY_SCHEDULED), filtered UQ TransactionId WHERE != RESOLVED, 3 FK + 3 perf index |

### Append-Only Altyapı (T25)

| Dosya | İçerik |
|---|---|
| `backend/src/Skinora.Shared/Domain/IAppendOnly.cs` | Marker interface — IAppendOnly implement eden entity'lerde UPDATE/DELETE AppDbContext.EnforceAppendOnly() ile reddedilir (06 §4.2) |

### CI/CD & Git Hooks (T11)

| Dosya | İçerik |
|---|---|
| `.github/workflows/ci.yml` | CI pipeline — 09 §21.4 6 adım + guard-direct-push + docker-build-check + ci-gate |
| `.github/workflows/docker-publish.yml` | main push'unda 4 servis image'ini ghcr.io'ya push |
| `.github/pull_request_template.md` | PR şablonu — 09 §21.3 kuralları + mini güvenlik checklist |
| `.gitattributes` | Shell script + YAML LF line ending zorunluluğu |
| `Docs/CI_CD_SETUP.md` | Branch protection setup kılavuzu (discipline-only rejim + hedef konfigürasyon) |
| `Docs/BYPASS_LOG.md` | Direct push bypass kayıtları (pre-push hook otomatik yazar) |
| `scripts/git-hooks/pre-push` | main/develop direct push bloklama + bypass auto-log |
| `scripts/git-hooks/install.sh` | `git config core.hooksPath scripts/git-hooks` ile hook kurulumu |
| `scripts/git-hooks/README.md` | Hook onboarding, test, bypass, devre dışı bırakma rehberi |

### Frontend — Next.js (T13)

| Dosya | İçerik |
|---|---|
| `frontend/src/app/[locale]/layout.tsx` | Root layout (i18n + providers) |
| `frontend/src/app/[locale]/page.tsx` | Landing page |
| `frontend/src/app/[locale]/(auth)/` | Auth layout grubu (callback) |
| `frontend/src/app/[locale]/(main)/` | Main layout grubu (dashboard, transactions, profile, notifications) |
| `frontend/src/app/[locale]/admin/` | Admin layout grubu (dashboard, transactions, flags, users, settings, roles, audit-logs) |
| `frontend/src/app/api/health/route.ts` | Health check endpoint |
| `frontend/src/lib/api/client.ts` | API client — fetch wrapper, ApiResponse<T> unwrap, ApiError, Bearer token (07 §2.4) |
| `frontend/src/lib/providers.tsx` | TanStack Query provider |
| `frontend/src/lib/stores/auth-store.ts` | Zustand auth store |
| `frontend/src/lib/signalr/connection.ts` | SignalR client — HubConnectionBuilder, auto-reconnect |
| `frontend/src/lib/hooks/useAuth.ts` | Auth hook |
| `frontend/src/lib/utils/format.ts` | Para, tarih formatlama |
| `frontend/src/types/api.ts` | ApiResponse<T>, PagedResult<T> types |
| `frontend/src/types/enums.ts` | 23 TypeScript enum (06 §2 birebir) |
| `frontend/src/i18n/routing.ts` | next-intl routing config (4 dil, fallback EN) |
| `frontend/src/i18n/request.ts` | next-intl server request config |
| `frontend/src/i18n/messages/` | 4 dil dosyası (en, zh, es, tr) |
| `frontend/src/middleware.ts` | i18n middleware |
| `frontend/Dockerfile` | Multi-stage Next.js standalone build |

### Steam Sidecar — Node.js (T14)

| Dosya | İçerik |
|---|---|
| `sidecar-steam/src/index.ts` | Entry point — Express server + graceful shutdown |
| `sidecar-steam/src/config/index.ts` | Environment config (port, URLs, keys, rate limits) |
| `sidecar-steam/src/logger.ts` | Pino logger (Loki push, correlationId, secret redaction) |
| `sidecar-steam/src/errors/SidecarError.ts` | Error hiyerarşisi: SidecarError → SteamApiError |
| `sidecar-steam/src/health/HealthController.ts` | /health endpoint (tek check: `steam-api`) |
| `sidecar-steam/src/api/routes.ts` | Express router — `/health`, `/metrics` + iki salt-okunur uç (envanter, trade-hold) |
| `sidecar-steam/src/api/middleware.ts` | correlationId + X-Internal-Key auth middleware |
| `sidecar-steam/src/trade/InventoryService.ts` | Anonim Steam Community envanter okuma (T67 · 08 §2.3) |
| `sidecar-steam/src/trade/TradeHoldService.ts` | Trade-hold / Mobile Authenticator probu (WP6 · 08 §2.2) |
| `sidecar-steam/src/cache/InventoryCache.ts` | Redis + in-memory envanter cache (T120) |
| `sidecar-steam/src/queue/RateLimitedQueue.ts` | Web API ve Community için AYRI rate limit kuyrukları (T120 · 08 §2.6) |
| `sidecar-steam/Dockerfile` | Multi-stage Node.js 20-alpine build |

### Blockchain Sidecar — Node.js (T15)

| Dosya | İçerik |
|---|---|
| `sidecar-blockchain/src/index.ts` | Entry point — Express server + graceful shutdown |
| `sidecar-blockchain/src/config/index.ts` | Environment config (TronGrid URLs, token contracts, HD wallet, rate limits) |
| `sidecar-blockchain/src/logger.ts` | Pino logger (Loki push, correlationId, secret redaction) |
| `sidecar-blockchain/src/errors/SidecarError.ts` | Error hiyerarşisi: SidecarError → InsufficientGasError, TransactionFailedError |
| `sidecar-blockchain/src/queue/RateLimitedQueue.ts` | Rate-limited istek kuyruğu (TronGrid API) |
| `sidecar-blockchain/src/webhook/WebhookClient.ts` | HMAC-SHA256 imzalı webhook callback (05 §3.4) |
| `sidecar-blockchain/src/webhook/WebhookPayloads.ts` | Webhook payload type |
| `sidecar-blockchain/src/health/HealthController.ts` | /health endpoint |
| `sidecar-blockchain/src/api/routes.ts` | Express router (health + stub API routes) |
| `sidecar-blockchain/src/api/middleware.ts` | correlationId + X-Internal-Key auth middleware |
| `sidecar-blockchain/src/wallet/WalletManager.ts` | HD Wallet yönetimi stub (T70) |
| `sidecar-blockchain/src/wallet/AddressGenerator.ts` | Adres üretimi stub (T70) |
| `sidecar-blockchain/src/monitor/TransactionMonitor.ts` | Ödeme izleme stub (T71) |
| `sidecar-blockchain/src/monitor/PostCancelMonitor.ts` | İptal sonrası izleme stub (T75) |
| `sidecar-blockchain/src/transfer/TransferService.ts` | TRC-20 transfer stub (T73) |
| `sidecar-blockchain/src/transfer/RefundService.ts` | İade transfer stub (T73) |
| `sidecar-blockchain/Dockerfile` | Multi-stage Node.js 20-alpine build |

### Monitoring & Alerting (T16)

| Dosya | İçerik |
|---|---|
| `infra/prometheus/prometheus.yml` | Prometheus scrape config (4 target: backend, steam, blockchain, prometheus) |
| `infra/grafana/provisioning/datasources/prometheus.yml` | Prometheus datasource for Grafana |
| `infra/grafana/provisioning/dashboards/dashboards.yml` | Dashboard provider config (auto-provision from JSON) |
| `infra/grafana/provisioning/dashboards/json/system-overview.json` | Sistem dashboard (CPU, RAM, uptime) |
| `infra/grafana/provisioning/dashboards/json/application-metrics.json` | Uygulama metrikleri (request rate, duration, errors) |
| `infra/grafana/provisioning/dashboards/json/business-metrics.json` | İş metrikleri (transactions, trade offers, transfers) |
| `infra/grafana/provisioning/dashboards/json/integration-metrics.json` | Entegrasyon metrikleri (Steam API, TronGrid) |
| `infra/grafana/provisioning/dashboards/json/security-metrics.json` | Güvenlik metrikleri (auth failures, rate limits, errors) |
| `infra/grafana/provisioning/alerting/contactpoints.yml` | Telegram + Email contact points |
| `infra/grafana/provisioning/alerting/policies.yml` | Notification policies (severity-based routing) |
| `infra/grafana/provisioning/alerting/rules.yml` | Alert rules (3 Critical + 4 Warning) |
| `backend/src/Skinora.API/HealthChecks/HealthCheckResponseWriter.cs` | Structured JSON health response writer |
| `sidecar-steam/src/metrics.ts` | Steam sidecar Prometheus metrikleri (prom-client) |
| `sidecar-blockchain/src/metrics.ts` | Blockchain sidecar Prometheus metrikleri (prom-client) |

### Araçlar

| Dosya | İçerik |
|---|---|
| `scripts/gpt-review.mjs` | GPT o3 cross-review scripti — dokümanı GPT'ye gönderir, yapılandırılmış bulgu alır |
| `scripts/gpt-ask.mjs` | `/gorus` taşıyıcısı — onaylı soruyu ChatGPT'ye gönderir (codex CLI → API → manuel), cevabı `Docs/GPT_OPINIONS/`'a yazar |
| `scripts/lib/` | Ortak script yardımcıları — repo kökü, sır bekçisi (dışa giden yol), codex taşıyıcısı, OpenAI istemcisi, pano |
