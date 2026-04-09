# T15 — Blockchain Sidecar Node.js İskeleti

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-09

---

## Yapılan İşler
- Node.js TypeScript projesi oluşturuldu (tronweb ^5.3.5, Express, Pino, ESLint, Prettier)
- 09 §4.4.2 klasör yapısına uygun dizin yapısı: wallet/, monitor/, transfer/, api/, webhook/, health/, config/
- TronGrid API config: Mainnet + Shasta + Nile URL'leri, API key (birincil + ikincil)
- USDT/USDC kontrat adresleri 08 §3.3 birebir eşleşme
- HMAC-SHA256 imzalı webhook callback modülü (09 §17.5)
- Health check endpoint /health (tron-node + hot-wallet check stubs)
- Error hiyerarşisi: SidecarError → InsufficientGasError, TransactionFailedError (09 §17.3)
- Pino logger + Loki push + secret redaction (mnemonic, privateKey, apiKey) (09 §17.4)
- RateLimitedQueue (TronGrid rate limit koruması)
- Express entry point + graceful shutdown (09 §17.9)
- Multi-stage Dockerfile (Node.js 20-alpine, port 5200)
- Stub modüller: WalletManager, AddressGenerator (T70), TransactionMonitor (T71), PostCancelMonitor (T75), TransferService (T73), RefundService (T73)
- correlationId middleware + X-Internal-Key auth (05 §3.4)
- T02'den kalan placeholder dosyalar (server.js, logger.js) kaldırıldı
- .gitignore: global node_modules/ ve dist/ kuralı eklendi

## Etkilenen Modüller / Dosyalar
- `sidecar-blockchain/package.json` — TypeScript proje konfigürasyonu (T02 placeholder yerine)
- `sidecar-blockchain/tsconfig.json` — TypeScript compiler config
- `sidecar-blockchain/.eslintrc.json` — ESLint config
- `sidecar-blockchain/.prettierrc` — Prettier config
- `sidecar-blockchain/Dockerfile` — Multi-stage build (T02 placeholder yerine)
- `sidecar-blockchain/src/config/index.ts` — TronGrid URLs, token contracts, HD wallet config
- `sidecar-blockchain/src/logger.ts` — Pino + Loki + secret redaction
- `sidecar-blockchain/src/errors/SidecarError.ts` — Error class hiyerarşisi
- `sidecar-blockchain/src/queue/RateLimitedQueue.ts` — Rate limiting queue
- `sidecar-blockchain/src/webhook/WebhookClient.ts` — HMAC-SHA256 webhook callback
- `sidecar-blockchain/src/webhook/WebhookPayloads.ts` — Webhook payload type
- `sidecar-blockchain/src/health/HealthController.ts` — /health endpoint
- `sidecar-blockchain/src/api/routes.ts` — Express routes + stub API endpoints
- `sidecar-blockchain/src/api/middleware.ts` — correlationId + internalKeyAuth
- `sidecar-blockchain/src/wallet/WalletManager.ts` — HD Wallet stub (T70)
- `sidecar-blockchain/src/wallet/AddressGenerator.ts` — Address generator stub (T70)
- `sidecar-blockchain/src/monitor/TransactionMonitor.ts` — Payment monitor stub (T71)
- `sidecar-blockchain/src/monitor/PostCancelMonitor.ts` — Post-cancel monitor stub (T75)
- `sidecar-blockchain/src/transfer/TransferService.ts` — Transfer stub (T73)
- `sidecar-blockchain/src/transfer/RefundService.ts` — Refund stub (T73)
- `sidecar-blockchain/src/types/express.d.ts` — Express type augmentation
- `sidecar-blockchain/src/index.ts` — Entry point + graceful shutdown
- `.gitignore` — Global node_modules/ ve dist/ kuralı eklendi
- Silinen: `sidecar-blockchain/server.js`, `sidecar-blockchain/logger.js` (T02 placeholders)

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Node.js TypeScript projesi oluşturuldu | ✓ | `npm run build` → `tsc` başarılı, 0 hata |
| 2 | Klasör yapısı: wallet/, monitor/, transfer/, api/, webhook/, health/, config/ | ✓ | `ls src/` — tüm klasörler mevcut |
| 3 | TronWeb ^5.x kütüphanesi kuruldu | ✓ | `package.json`: `"tronweb": "^5.3.5"`, `npm ls tronweb` → 5.3.5 |
| 4 | TronGrid API bağlantısı konfigüre (Mainnet + Testnet URL, API key) | ✓ | `config/index.ts`: TRON_NETWORKS (mainnet/shasta/nile), TRON_API_KEY |
| 5 | Webhook callback gönderim modülü | ✓ | `webhook/WebhookClient.ts`: HMAC-SHA256 imzalı sendCallback |
| 6 | Health check endpoint: /health | ✓ | `health/HealthController.ts` + `api/routes.ts` GET /health |
| 7 | Error hiyerarşisi: SidecarError, InsufficientGasError, TransactionFailedError | ✓ | `errors/SidecarError.ts`: 3 sınıf, retryable flag |
| 8 | Pino logger + graceful shutdown + rate limiting queue | ✓ | `logger.ts` + `index.ts` shutdown handlers + `queue/RateLimitedQueue.ts` |
| 9 | USDT/USDC kontrat adresleri config'de tanımlı | ✓ | `config/index.ts`: USDT=TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t, USDC=TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8 |
| 10 | Dockerfile | ✓ | Multi-stage Node.js 20-alpine, EXPOSE 5200 |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Unit | N/A | Task tanımı: "Test beklentisi: Yok" |
| Build | ✓ PASS | `npm run build` → 0 hata |
| Lint | ✓ PASS | `npm run lint` → 0 uyarı |
| Format | ✓ PASS | `npm run format:check` → All matched files use Prettier code style |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |
| Doğrulama tarihi | 2026-04-09 |
| Doğrulama yöntemi | Bağımsız validator — kabul kriterleri, build, lint, format, güvenlik, doküman uyumu |

## Doğrulama Kontrol Listesi
| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 08 §3.1 TronGrid endpoint'leri doğru mu? | ✓ | mainnet: api.trongrid.io, shasta: api.shasta.trongrid.io, nile: nile.trongrid.io |
| 2 | 08 §3.3 kontrat adresleri doğru mu? | ✓ | USDT: TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t, USDC: TEkxiTehnzSmSe2XqrBj4w32RUN966rdz8 |

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: Yok (docker-compose.yml T02'de zaten blockchain sidecar servisini tanımlıyor)
- Docker değişikliği: Dockerfile güncellendi (T02 placeholder → multi-stage TypeScript build)
- .gitignore: Global `node_modules/` ve `dist/` kuralı eklendi

## Commit & PR
- Branch: `task/T15-blockchain-sidecar-skeleton`
- Commit: `1939820` — T15: Blockchain Sidecar Node.js iskeleti
- PR: Henüz oluşturulmadı
- CI: Push sonrası bekleniyor

## Known Limitations / Follow-up
- TronWeb ^5.x kullanıldı (08 §3.1 ile uyumlu). npm'de ^6.x mevcut ama spec ^5.x diyor.
- npm audit 9 vulnerability raporluyor — tronweb'in geçişli bağımlılıklarından kaynaklı (google-protobuf deprecated). Fonksiyonel etki yok, production'a kadar izlenecek.

## Notlar
- T14 (Steam Sidecar) ile aynı pattern ve convention takip edildi: aynı tsconfig, eslint, prettier, logger, webhook, middleware, health check yapısı.
- T02'den kalan JavaScript placeholder dosyaları (server.js, logger.js) TypeScript yapısıyla değiştirildi.
