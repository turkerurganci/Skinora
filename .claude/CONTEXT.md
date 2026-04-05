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

### Araçlar

| Dosya | İçerik |
|---|---|
| `scripts/gpt-review.mjs` | GPT o3 cross-review scripti — dokümanı GPT'ye gönderir, yapılandırılmış bulgu alır |
