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
| `Docs/CHECKPOINT_REPORTS/` | Checkpoint raporları (CP1-CP18) |

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

### Araçlar

| Dosya | İçerik |
|---|---|
| `scripts/gpt-review.mjs` | GPT o3 cross-review scripti — dokümanı GPT'ye gönderir, yapılandırılmış bulgu alır |
