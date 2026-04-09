# T14 — Steam Sidecar Node.js İskeleti

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-09

---

## Yapılan İşler
- T02'deki JS placeholder (`server.js`, `logger.js`) tamamen kaldırıldı
- TypeScript proje altyapısı kuruldu (tsconfig, ESLint, Prettier)
- 09 §4.4.1 klasör yapısı: `src/bot/`, `src/trade/`, `src/api/`, `src/webhook/`, `src/health/`, `src/config/`
- Steam kütüphaneleri eklendi: steam-tradeoffer-manager ^2.13.x, steamcommunity ^3.x, steam-totp ^2.x, steam-user ^5.x
- Config modülü (environment variables)
- Error class hiyerarşisi: `SidecarError` → `SteamApiError`, `BotSessionExpiredError`
- Pino logger (Loki push, correlationId, secret masking — T08 logger'ın TypeScript portu)
- Rate limiting istek kuyruğu (`RateLimitedQueue`)
- Webhook client (HMAC-SHA256 imzalama, timestamp/nonce, correlationId forwarding)
- Health check endpoint (`/health`)
- Express API routes iskeleti + X-Internal-Key auth middleware
- Graceful shutdown handler (SIGTERM/SIGINT)
- Multi-stage Dockerfile (builder + runner)
- 08 §2.5 doküman düzeltmesi: `steam-tradeoffer-manager` ^3.x → ^2.13.x (3.x npm'de mevcut değil)

## Etkilenen Modüller / Dosyalar

### Oluşturulan
- `sidecar-steam/package.json` — dependencies + scripts
- `sidecar-steam/tsconfig.json`
- `sidecar-steam/.eslintrc.json`
- `sidecar-steam/.prettierrc.json`
- `sidecar-steam/.gitignore`
- `sidecar-steam/src/index.ts` — entry point
- `sidecar-steam/src/config/index.ts` — environment config
- `sidecar-steam/src/logger.ts` — Pino logger (Loki + stdout)
- `sidecar-steam/src/errors/SidecarError.ts` — error hierarchy
- `sidecar-steam/src/queue/RateLimitedQueue.ts` — rate limiter
- `sidecar-steam/src/webhook/WebhookClient.ts` — HMAC signing
- `sidecar-steam/src/webhook/WebhookPayloads.ts` — payload types
- `sidecar-steam/src/health/HealthController.ts` — /health endpoint
- `sidecar-steam/src/api/routes.ts` — Express router
- `sidecar-steam/src/api/middleware.ts` — correlationId + auth
- `sidecar-steam/src/bot/BotManager.ts` — stub (T64)
- `sidecar-steam/src/bot/BotSession.ts` — stub (T64)
- `sidecar-steam/src/bot/BotHealthCheck.ts` — stub (T64)
- `sidecar-steam/src/trade/TradeOfferService.ts` — stub (T65)
- `sidecar-steam/src/trade/InventoryService.ts` — stub (T67)
- `sidecar-steam/src/types/express.d.ts` — Express request augmentation

### Güncellenen
- `sidecar-steam/Dockerfile` — multi-stage TypeScript build
- `Docs/08_INTEGRATION_SPEC.md` — §2.5 versiyon düzeltmesi

### Silinen
- `sidecar-steam/server.js` — T02 placeholder
- `sidecar-steam/logger.js` — T02 placeholder

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Node.js TypeScript projesi oluşturuldu | ✓ | `tsconfig.json` + `npm run build` başarılı |
| 2 | Klasör yapısı: bot/, trade/, api/, webhook/, health/, config/ | ✓ | `find src -type f` çıktısı — tüm klasörler mevcut |
| 3 | Kütüphaneler: steam-tradeoffer-manager, steamcommunity, steam-totp, steam-user | ✓ | `package.json` dependencies — versiyonlar 08 §2.5'e uygun (tradeoffer-manager ^2.13.x not ile) |
| 4 | Webhook callback: HMAC-SHA256 imzalama, timestamp/nonce/signature header | ✓ | `src/webhook/WebhookClient.ts` — 05 §3.4 + 09 §17.5 uyumlu |
| 5 | Health check endpoint: /health | ✓ | `src/health/HealthController.ts` + `src/api/routes.ts` route kaydı |
| 6 | Error class hiyerarşisi: SidecarError, SteamApiError, BotSessionExpiredError | ✓ | `src/errors/SidecarError.ts` — 09 §17.3 uyumlu |
| 7 | Rate limiting istek kuyruğu | ✓ | `src/queue/RateLimitedQueue.ts` — configurable maxRequests/windowMs |
| 8 | Graceful shutdown handler | ✓ | `src/index.ts` SIGTERM/SIGINT handler — 09 §17.9 uyumlu |
| 9 | Pino logger (Loki push, correlationId) | ✓ | `src/logger.ts` — secret masking + Loki transport |
| 10 | ESLint + Prettier | ✓ | `.eslintrc.json` + `.prettierrc.json`, `npm run lint` + `npm run format:check` temiz (validator: WebhookClient.ts format düzeltmesi sonrası) |
| 11 | Dockerfile | ✓ | Multi-stage builder + runner, Node.js 20-alpine |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Unit | N/A | Task tanımında test beklentisi yok |
| Integration | N/A | Task tanımında test beklentisi yok |
| Build | ✓ | `npm run build` — tsc başarılı, 0 hata |
| Lint | ✓ | `npx eslint src/` — 0 hata, 0 uyarı |
| Type check | ✓ | `npx tsc --noEmit` — 0 hata |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Doğrulama tarihi | 2026-04-09 |
| Validator | Bağımsız doğrulama chat'i |
| Bulgu sayısı | 1 (minor — Prettier format, doğrulama sırasında düzeltildi) |
| Düzeltme gerekli mi | Hayır (doğrulama sırasında çözüldü) |

### Validator Notları
- 11 kabul kriterinin tümü bağımsız olarak doğrulandı
- Yapım raporuyla 1 uyuşmazlık: kriter #10 (Prettier) — yapım raporu ✓, validator `prettier --check` → 1 dosya fail. Doğrulama sırasında `prettier --write` ile düzeltildi
- Doğrulama kontrol listesi (3/3 geçti): klasör yapısı §4.4.1 uyumlu, kütüphaneler 08 §2.5 uyumlu, webhook imzalama 05 §3.4 uyumlu
- Güvenlik: Temiz (secret yok, env var kullanımı, logger redaction)
- Build/lint/format: Tümü temiz (düzeltme sonrası)

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: Yok (docker-compose'da `skinora-steam-sidecar` servisi T02'de zaten tanımlı)
- Docker değişikliği: `sidecar-steam/Dockerfile` multi-stage TypeScript build'e güncellendi

## Commit & PR
- Branch: `task/T14-steam-sidecar-skeleton`
- Commit: `836ffc3` (yapım) + validator düzeltme commit'i
- PR: #8

## Known Limitations / Follow-up
- `steam-tradeoffer-manager` ^3.x npm'de mevcut değil — ^2.13.x kullanıldı, 08 §2.5 güncellendi
- Bot/trade/inventory servisleri stub — gerçek implementasyon T64–T69'da
- Health check skeleton modunda — gerçek Steam API/bot session kontrolleri T64'te eklenecek
- API route'ları 501 döner — gerçek handler'lar T64–T69'da

## Notlar
- docker-compose.yml değişikliği gerekmedi — `skinora-steam-sidecar` servisi T02'de tanımlı, `context: ./sidecar-steam` doğru
- T08'de kurulan Pino logger (CommonJS) → TypeScript'e port edildi, aynı secret masking ve Loki transport korundu
- Express seçildi (Fastify değil) — sidecar API kapsamı dar, basitlik yeterli
