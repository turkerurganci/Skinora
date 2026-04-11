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
| `.claude/skills/gpt-cross-review.md` | `/gpt-cross-review` skill — GPT o3 ile ikinci AI review döngüsü |
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
| `Docs/GPT_REVIEW_REPORTS/` | GPT cross-review raporları (round bazlı) |
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
| `sidecar-steam/src/errors/SidecarError.ts` | Error hiyerarşisi: SidecarError → SteamApiError, BotSessionExpiredError |
| `sidecar-steam/src/queue/RateLimitedQueue.ts` | Rate-limited istek kuyruğu (Steam API) |
| `sidecar-steam/src/webhook/WebhookClient.ts` | HMAC-SHA256 imzalı webhook callback (05 §3.4) |
| `sidecar-steam/src/webhook/WebhookPayloads.ts` | Webhook payload type |
| `sidecar-steam/src/health/HealthController.ts` | /health endpoint |
| `sidecar-steam/src/api/routes.ts` | Express router (health + stub API routes) |
| `sidecar-steam/src/api/middleware.ts` | correlationId + X-Internal-Key auth middleware |
| `sidecar-steam/src/bot/BotManager.ts` | Bot yönetimi stub (T64) |
| `sidecar-steam/src/bot/BotSession.ts` | Bot session stub (T64) |
| `sidecar-steam/src/bot/BotHealthCheck.ts` | Bot health check stub (T64) |
| `sidecar-steam/src/trade/TradeOfferService.ts` | Trade offer stub (T65) |
| `sidecar-steam/src/trade/InventoryService.ts` | Envanter stub (T67) |
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
