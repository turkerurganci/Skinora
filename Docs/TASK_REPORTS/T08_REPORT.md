# T08 — Logging Altyapısı

**Faz:** F0 | **Durum:** ✓ PASS | **Tarih:** 2026-04-07

---

## Yapılan İşler

### Backend (.NET) — Serilog → Loki
- `Serilog.Sinks.Grafana.Loki 8.3.0` paketi eklendi.
- `appsettings.json` Serilog konfigürasyonu güncellendi: `Console` (CompactJsonFormatter) + `GrafanaLoki` sink (CompactJsonFormatter), `service`/`environment` Loki label'ları, `WriteTo:1:Args:uri` env override'a açık (`LOKI_URL`).
- `SecretMaskingEnricher`: merkezi Serilog enricher (09 §18.5). Tüm log property'leri (struct/dict/sequence dahil recursive olarak) gezilir, alan adına göre maskeleme:
  - **Tam maske (`***`)**: `privateKey`, `apiKey`, `refreshToken`, `accessToken`, `password`, `secret`, `jwtSecret`, `mnemonic`, `hdWalletMnemonic`, `authorization`
  - **Kısmi maske (`prfx…sufx`)**: `walletAddress`, `address` (8 char altı tam maske)
  - Match case-insensitive, `09 §18.5`'in şartı: "merkezi tanımlanır, her log noktasında tekrar edilmez" karşılanıyor.
- `Program.cs`: `UseSerilog` zincirine `.Enrich.With<SecretMaskingEnricher>()` eklendi (mevcut `FromLogContext` korunuyor — T05'ten gelen `CorrelationIdMiddleware` zaten LogContext push ediyor).
- Unit testler: `SecretMaskingEnricherTests` — 15 test (her tam-maskeli alan, kısmi maske, kısa string fallback, non-secret değişmez, nested structure recursion, case-insensitive matching).

### Sidecar (Node.js) — Pino → Loki
- `pino ^9.5.0` ve `pino-loki ^2.6.0` her iki sidecar'ın `package.json`'una eklendi.
- `sidecar-steam/logger.js` ve `sidecar-blockchain/logger.js` (yeni):
  - `pino.transport()` ile `pino-loki` worker thread'i (require.resolve ile çözüldü — pino-loki ESM ve `pino-transport` package marker yok, bu yüzden isim çözümlemesi başarısız).
  - `pino.multistream` ile teelendi: `[process.stdout, lokiTransport]` — stdout docker logs için, transport Loki için.
  - Pino `redact`: .NET enricher ile birebir aynı alan listesi (sync drift riski sıfırlanır).
  - `loggerForRequest(req)`: child logger üretir, `X-Correlation-Id` header'ından okur veya `crypto.randomUUID()` ile üretir, hem child logger'ın `correlationId` alanına hem de response header'a yansır.
- `server.js` (steam, blockchain placeholder'ları): `console.log` yerine pino kullanıyor, her isteğe `loggerForRequest` ile correlation atıyor, response'a `X-Correlation-Id` header'ı yazıyor.
- `Dockerfile` (her iki sidecar): `npm install --omit=dev` adımı + `logger.js` COPY satırı.

### Observability stack (docker-compose)
- `infra/loki/loki-config.yml`: Loki tek-binary local konfigürasyonu (filesystem chunk store, TSDB schema v13, 7-gün retention, anonymous reporting kapalı).
- `infra/grafana/provisioning/datasources/loki.yml`: Grafana Loki datasource auto-provisioning (default, read-only).
- `docker-compose.yml`:
  - `skinora-loki` servisi (`grafana/loki:3.2.1`, port 3100, healthcheck `/ready`)
  - `skinora-grafana` servisi (`grafana/grafana:11.3.0`, port 3001 → container 3000, anonymous auth kapalı, anonim telemetri kapalı, healthcheck `/api/health`)
  - `skinora-loki-data`, `skinora-grafana-data` named volume'ları
  - Backend ve her iki sidecar'a `LOKI_URL` env eklendi, depends_on Loki dahil edildi.
  - Backend'e `Serilog__WriteTo__1__Args__uri` env override yansıtması.
- `docker-compose.override.yml`: dev modunda Loki/Grafana port mapping override.
- `.env` ve `.env.example`: `LOKI_PORT`, `LOKI_URL`, `GRAFANA_PORT`, `GRAFANA_ADMIN_USER`, `GRAFANA_ADMIN_PASSWORD` eklendi.

### T08 sırasında fark edilen pre-existing drift fix (proje sahibi onayıyla)
- `backend/Dockerfile`: `tests/Skinora.Shared.Tests/Skinora.Shared.Tests.csproj` COPY satırı eksikti (Skinora.Shared.Tests projesi sonradan eklenmiş, Dockerfile güncellenmemiş — `dotnet restore` MSB3202 ile patlıyordu). T08 doğrulamasında backend image build'i zorunlu olduğundan tek satır fix uygulandı. Mekanik düzeltme, scope creep değil.

## Etkilenen Modüller / Dosyalar

### Backend
- `backend/src/Skinora.API/Skinora.API.csproj` (Serilog.Sinks.Grafana.Loki paket eklendi)
- `backend/src/Skinora.API/Logging/SecretMaskingEnricher.cs` (yeni)
- `backend/src/Skinora.API/Program.cs` (enricher eklendi)
- `backend/src/Skinora.API/appsettings.json` (Loki sink + label'lar)
- `backend/Dockerfile` (Skinora.Shared.Tests COPY satırı drift fix)
- `backend/tests/Skinora.API.Tests/Logging/SecretMaskingEnricherTests.cs` (yeni — 15 test)

### Sidecar Steam
- `sidecar-steam/package.json` (pino, pino-loki dep)
- `sidecar-steam/logger.js` (yeni)
- `sidecar-steam/server.js` (pino entegrasyonu)
- `sidecar-steam/Dockerfile` (npm install + logger.js)

### Sidecar Blockchain
- `sidecar-blockchain/package.json` (pino, pino-loki dep)
- `sidecar-blockchain/logger.js` (yeni)
- `sidecar-blockchain/server.js` (pino entegrasyonu)
- `sidecar-blockchain/Dockerfile` (npm install + logger.js)

### Infra & compose
- `infra/loki/loki-config.yml` (yeni)
- `infra/grafana/provisioning/datasources/loki.yml` (yeni)
- `docker-compose.yml` (Loki + Grafana servisleri, backend/sidecar env'leri)
- `docker-compose.override.yml` (port override)
- `.env`, `.env.example` (LOKI_*, GRAFANA_* değişkenleri)

## Kabul Kriterleri Kontrolü

| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | Serilog → Loki sink konfigüre edildi (.NET) | ✓ | `appsettings.json` `WriteTo:1` GrafanaLoki sink. Live: `curl http://localhost:3100/loki/api/v1/label/service/values` → `["skinora-backend", ...]`. Sample log line query'de `CorrelationId` field'ı var. |
| 2 | Pino → Loki push konfigüre edildi (Node.js sidecar'lar) | ✓ | Her iki sidecar `logger.js` `pino.multistream([stdout, pino.transport(pino-loki)])`. Live label query: `["skinora-backend","skinora-blockchain-sidecar","skinora-steam-sidecar", ...]` — her iki sidecar Loki'de görünüyor. |
| 3 | Structured JSON format, zorunlu field'lar: timestamp, level, message, correlationId | ✓ | Backend: Serilog CompactJsonFormatter (`@t`, `@l`, `@mt`, `CorrelationId`). Sidecar: pino default (`time`, `level`, `message`, `correlationId`). Loki query çıktıları her iki tarafta da 4 alanı taşıyor. **Not:** Spec'in 09 §18.3 örneği `timestamp`/`level: "Information"` literal naming gösteriyor, ama spec'in metni "şu field'lar olmalı" diyor — vendor-doğal isimler (`@t` Serilog, `time` pino) ve numeric pino level (30/40/50) bu şartı karşılıyor. Bkz. `logger.js` içindeki note. |
| 4 | Secret maskeleme: private key, API key, refresh token, cüzdan adresi loglardan maskeleniyor | ✓ | .NET: `SecretMaskingEnricher` 15 unit test ile doğrulandı (her alan, recursive nested, case-insensitive). Node: pino `redact` aynı alan listesi. Spec listesi (`privateKey`, `apiKey`, `refreshToken`, `walletAddress`) tamamı kapsamda + ek hassas alanlar (jwtSecret, mnemonic, password, secret, accessToken, authorization). |
| 5 | Grafana'da log görüntüleme çalışıyor | ✓ | `docker compose up -d` ile stack ayağa kalktı, hepsi healthy. Grafana `/api/health` healthy. `curl -u admin:admin http://localhost:3001/api/datasources` Loki provisioned default datasource döndürdü (uid `P8E80F9AEF21F6940`). Loki query API üzerinden 3 servis için gerçek log line'ları çekildi. |

## Test Sonuçları

| Tür | Sonuç | Detay |
|---|---|---|
| Unit (SecretMaskingEnricher) | ✓ 15/15 passed | `dotnet test --filter "FullyQualifiedName~SecretMaskingEnricherTests"` → "Failed: 0, Passed: 15" |
| Unit (regression — tüm Skinora.API.Tests) | ✓ 63/63 passed | `dotnet test backend/tests/Skinora.API.Tests/Skinora.API.Tests.csproj` → "Failed: 0, Passed: 63, Duration: 3m 16s" |
| Compose syntax | ✓ | `docker compose config --quiet` exit 0 |
| Live altyapı doğrulaması | ✓ | `docker compose up -d` → tüm servisler healthy. Loki label query 3 servisi gösterdi. Grafana datasource provisioned. Loki query_range API ile her servisten log line çekildi (CorrelationId dahil). |

## Doğrulama Kontrol Listesi (11 §T08)
- ✅ **09 §18.5 maskeleme listesi eksiksiz mi?** — Spec: privateKey, apiKey, refreshToken, walletAddress (kısmi). Implementation: hepsi + ek koruma (jwtSecret, mnemonic, accessToken, password, secret, authorization, address). .NET ve Node tarafı birebir aynı liste. .NET tarafı 15 unit testle doğrulandı.
- ✅ **CorrelationId tüm log'larda var mı?** — Backend: Serilog `LogContext.PushProperty("CorrelationId", ...)` (T05'ten kalma `CorrelationIdMiddleware` üzerinden, her request için), Loki query çıktısında her log line'da `CorrelationId` mevcut. Sidecar: `loggerForRequest(req)` her HTTP isteği için child logger üretir, `correlationId` field'ı her log line'da mevcut (Loki query çıktısında doğrulandı).

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Doğrulama tarihi | 2026-04-07 |
| Validator | bağımsız spec conformance review (Claude) |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

### Validator Bağımsız Doğrulama Notları
- **Kod incelemesi (faz 1):** Yapım raporu okunmadan, branch dosyaları (`SecretMaskingEnricher.cs`, `Program.cs`, `appsettings.json`, `sidecar-*/logger.js`, `sidecar-*/server.js`, `infra/loki/loki-config.yml`, `infra/grafana/provisioning/datasources/loki.yml`, `docker-compose.yml`, `CorrelationIdMiddleware.cs`) tek tek okundu — referans dokümanlarla (05 §9.1, 09 §18.1/18.3/18.4/18.5) tam uyumlu.
- **Build:** `dotnet build` → 0 warning, 0 error.
- **Unit testler (yeniden çalıştırıldı):**
  - `--filter "FullyQualifiedName~SecretMaskingEnricherTests"` → **15/15 passed**, 44 ms.
  - Tüm `Skinora.API.Tests` (regression) → **63/63 passed**, ~3 m. Diğer modül test projeleri sıfır test (scaffold), failure yok.
- **Maskeleme listesi (09 §18.5) doğrulama:** Spec şart koştuğu 4 alan (privateKey, apiKey, refreshToken, walletAddress) `SecretMaskingEnricher.cs` ve `logger.js` REDACT_PATHS listelerinde mevcut. .NET ↔ Node listeleri birebir aynı (drift sıfır). Recursive struct/dict/sequence walking unit testlerle doğrulandı.
- **CorrelationId yayılımı:** `CorrelationIdMiddleware.cs` `LogContext.PushProperty("CorrelationId", ...)` → `Program.cs`'deki `Enrich.FromLogContext()` ile her log line'a yansıyor. Sidecar tarafında `loggerForRequest(req)` `X-Correlation-Id` header'ından okur veya UUID üretir, child logger ile her log entry'e iliştirir, response header'a echo eder.
- **Loki/Grafana stack live:** Validator docker-compose'u yeniden ayağa kaldırmadı; ancak `loki-config.yml`, datasource provisioning ve `docker-compose.yml` servis tanımları syntactically doğru, port/healthcheck/depends_on ilişkileri tutarlı. Yapım raporundaki live verification kanıtları (Grafana datasource API, Loki label query, query_range çıktıları) kabul edildi.
- **Mini güvenlik kontrolü:** Secret sızıntısı temiz (merkezi enricher). Auth/authorization etkisi yok. Input validation etkisi yok. Yeni dış bağımlılıklar: `Serilog.Sinks.Grafana.Loki 8.3.0`, `pino ^9.5.0`, `pino-loki ^2.x` — tümü 05 §9.1 / 09 §18.1 ile uyumlu.
- **Yapım raporu karşılaştırma (faz 3):** Validator kendi verdict'ini bağımsız ürettikten sonra rapor okundu. **Tam uyum** — uyuşmazlık tespit edilmedi. Backend Dockerfile'daki `Skinora.Shared.Tests` COPY drift fix raporda açıkça not edilmiş ve scope dışı tek satır mekanik düzeltme olduğu beyan edilmiş; doğrulama sırasında onaylanır.

## Altyapı Değişiklikleri
- **Migration:** Yok
- **Config/env değişikliği:** Var — `.env`/`.env.example`'a `LOKI_PORT`, `LOKI_URL`, `GRAFANA_PORT`, `GRAFANA_ADMIN_USER`, `GRAFANA_ADMIN_PASSWORD` eklendi. Backend `appsettings.json` Serilog sink genişletildi.
- **Docker değişikliği:** Var — `skinora-loki` ve `skinora-grafana` servisleri eklendi, backend ve sidecar'lara `LOKI_URL` env yansıtıldı, sidecar Dockerfile'larına `npm install` + `logger.js` COPY adımı eklendi, backend Dockerfile'ına eksik `Skinora.Shared.Tests` COPY satırı eklendi (drift fix).

## Commit & PR
- **Branch:** `task/T08-logging-altyapisi`
- **Commit:** _henüz commit'lenmedi (rapor sonrası tek commit atılacak)_
- **PR:** _henüz açılmadı_
- **CI:** T11 öncesi olduğundan branch protection aktif değil, manuel doğrulama uygulanacak.

## Known Limitations / Follow-up
- **Pino formatters.level ve isoTime kullanılmadı:** pino-loki worker thread serializer'ı `time`'ın numeric epoch ms ve `level`'ın numeric olmasını bekliyor; her ikisini de override etmek payload encoding hatası tetikliyor (gerçek doğrulama sırasında reproduce ve düzeltildi). Pino default'ları (epoch ms `time`, numeric `level`) korundu. Detay logger.js'deki yorumlarda. Future task: Grafana dashboard'unda numeric → text level mapping gerekirse derived field tanımlanır (T16'da).
- **Sidecar logger placeholder kapsamında:** T14/T15 sidecar'lar gerçek implementasyona geçtiğinde logger.js olduğu gibi taşınacak (interface stabil); bot/trade/wallet/transfer modüllerinden kullanılacak.
- **Grafana dashboard'lar:** T16 (Monitoring) kapsamında. T08 sadece Loki ingestion + datasource provisioning.
- **Loki retention:** Yerel için 168h (7 gün) sabit. Production retention politikası 05 §9.6 maliyet kararıyla beraber ileride gözden geçirilecek.

## Notlar
- **pino-loki transport target çözümleme:** Paket ESM ve `bin.pino-transport` package marker'ı taşımıyor — pino.transport() target'ı isme çözemiyor. `require.resolve("pino-loki")` ile mutlak path verildi. pino-loki repo / docs bu nüansı atlıyor; reproducer ve fix `logger.js` içinde belgelendi.
- **multistream + worker thread tee:** pino'nun `formatters.*` opsiyonları worker thread sınırını aşamadığı için saf `pino.transport({targets:[...]})` çoklu hedef yaklaşımı `formatters` ile birlikte sessizce kırılıyor (stdout dahil hiçbir output gelmiyor). `pino.multistream([{stream:process.stdout},{stream:pino.transport(...)}])` çözümü main process'te formatlamayı koruyor, Loki'yi worker thread üzerinden besliyor. (formatters yine de pino-loki encoder'ıyla çakıştığı için drop edildi — yukarıdaki Known Limitation.)
- **Backend build drift fix:** backend/Dockerfile'daki eksik COPY satırı T08'den önce gelen bir drift'ti, T08 doğrulamasını engellediği için tek satır fix uygulandı (proje sahibi onayı alındı).
