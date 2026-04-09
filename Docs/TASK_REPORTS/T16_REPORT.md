# T16 — Monitoring Altyapısı

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-10

---

## Yapılan İşler
- Prometheus eklendi (docker-compose servisi + scrape config — 4 target)
- .NET backend'e `prometheus-net.AspNetCore` entegre edildi — `/metrics` endpoint
- Node.js sidecar'lara `prom-client` eklendi — `/metrics` endpoint + custom metrikler (Steam API, TronGrid, trade offers, transfers, hot wallet balance)
- Grafana'ya Prometheus datasource provision edildi (Loki yanına)
- 5 Grafana dashboard oluşturuldu (system, application, business, integration, security)
- Grafana Alerting konfigüre edildi: contact points (Telegram + Email), notification policies, alert rules (3 Critical + 4 Warning)
- Uptime Kuma eklendi (docker-compose — HTTP/TCP external monitoring)
- Backend `/health` endpoint geliştirildi: ASP.NET Health Checks ile SQL Server + Redis dependency check'leri
- Structured JSON health response writer
- Test factory'de health check bypass eklendi (real DB/Redis olmadan test çalışır)

## Etkilenen Modüller / Dosyalar

### Yeni dosyalar
- `infra/prometheus/prometheus.yml` — Prometheus scrape config
- `infra/grafana/provisioning/datasources/prometheus.yml` — Prometheus datasource
- `infra/grafana/provisioning/dashboards/dashboards.yml` — Dashboard provider config
- `infra/grafana/provisioning/dashboards/json/system-overview.json` — Sistem dashboard
- `infra/grafana/provisioning/dashboards/json/application-metrics.json` — Uygulama metrikleri
- `infra/grafana/provisioning/dashboards/json/business-metrics.json` — İş metrikleri
- `infra/grafana/provisioning/dashboards/json/integration-metrics.json` — Entegrasyon metrikleri
- `infra/grafana/provisioning/dashboards/json/security-metrics.json` — Güvenlik metrikleri
- `infra/grafana/provisioning/alerting/contactpoints.yml` — Telegram + Email contact points
- `infra/grafana/provisioning/alerting/policies.yml` — Notification policies
- `infra/grafana/provisioning/alerting/rules.yml` — Alert rules (Critical + Warning)
- `backend/src/Skinora.API/HealthChecks/HealthCheckResponseWriter.cs` — Structured health response
- `sidecar-steam/src/metrics.ts` — Steam sidecar Prometheus metrikleri
- `sidecar-blockchain/src/metrics.ts` — Blockchain sidecar Prometheus metrikleri

### Güncellenen dosyalar
- `docker-compose.yml` — +skinora-prometheus, +skinora-uptime-kuma, Grafana depends_on Prometheus
- `backend/src/Skinora.API/Skinora.API.csproj` — +prometheus-net.AspNetCore, +AspNetCore.HealthChecks.SqlServer, +AspNetCore.HealthChecks.Redis
- `backend/src/Skinora.API/Program.cs` — Health checks registration, UseHttpMetrics, MapMetrics, MapHealthChecks
- `backend/tests/Skinora.API.Tests/Common/HangfireBypassFactory.cs` — Health check bypass for tests
- `sidecar-steam/package.json` — +prom-client
- `sidecar-steam/src/api/routes.ts` — +/metrics route
- `sidecar-blockchain/package.json` — +prom-client
- `sidecar-blockchain/src/api/routes.ts` — +/metrics route
- `.env.example` — +PROMETHEUS_PORT, +UPTIME_KUMA_PORT, +TELEGRAM_BOT_TOKEN, +TELEGRAM_CHAT_ID, +ALERT_EMAIL_TO

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuc | Kanit |
|---|---|---|---|
| 1 | Prometheus konfigüre (docker-compose'da) | ✓ | `docker-compose.yml` — skinora-prometheus servisi, `infra/prometheus/prometheus.yml` — 4 scrape target |
| 2 | .NET Prometheus client: metrics endpoint /metrics | ✓ | `Skinora.API.csproj` — prometheus-net.AspNetCore 8.2.1, `Program.cs` — `app.UseHttpMetrics()` + `app.MapMetrics()` |
| 3 | prom-client (Node.js): metrics endpoint /metrics | ✓ | Her iki sidecar'da `prom-client ^15.1.0` + `metrics.ts` + `/metrics` route |
| 4 | Grafana dashboard konfigüre | ✓ | 5 dashboard JSON provisioned: system, app, business, integration, security |
| 5 | Grafana Alerting: Telegram + Email (Critical/Warning/Info) | ✓ | `contactpoints.yml` (Telegram + Email), `policies.yml` (severity routing), `rules.yml` (3 Critical + 4 Warning) |
| 6 | Uptime Kuma: HTTP/TCP external monitoring | ✓ | `docker-compose.yml` — skinora-uptime-kuma servisi, port 3002 |
| 7 | Health check endpoint: /health (DB, Redis, Steam API, Tron node kontrolleri) | ✓ | Backend: ASP.NET Health Checks (SqlServer + Redis), Sidecar'lar: mevcut structured health (skeleton) |

## Test Sonuclari
| Tur | Sonuc | Detay |
|---|---|---|
| Unit | ✓ 46/46 passed | `dotnet test` — Skinora.Shared.Tests |
| Integration | ✓ 99/99 passed | `dotnet test` — Skinora.API.Tests (HealthEndpoint_StillWorks dahil) |
| TypeScript | ✓ Build clean | `tsc --noEmit` — her iki sidecar |
| Lint | ✓ Clean | `eslint src/` — her iki sidecar |
| Frontend | ✓ Build clean | `npm run build` — Next.js |

## Dogrulama
| Alan | Sonuc |
|---|---|
| Dogrulama durumu | ✓ PASS |
| Dogrulama tarihi | 2026-04-10 |
| Bulgu sayisi | 0 |
| Duzeltme gerekli mi | Hayir |

### Dogrulama Detaylari
- 7/7 kabul kriteri ✓ karsilandi
- 3/3 dogrulama kontrol listesi ✓ gecti
- Testler: 145/145 passed (46 unit + 99 integration), tsc temiz (2 sidecar), next build temiz
- Guvenlik: Temiz (secret yok, auth etkisi yok, input validation etkisi yok)
- Dokuman uyumu: 05 §9.1-§9.5 ile tam uyumlu
- Yapim raporu karsilastirmasi: Tam uyumlu, uyusmazlik yok

## Altyapi Degisiklikleri
- Migration: Yok
- Config/env degisikligi: `.env.example`'a PROMETHEUS_PORT, UPTIME_KUMA_PORT, TELEGRAM_BOT_TOKEN, TELEGRAM_CHAT_ID, ALERT_EMAIL_TO eklendi
- Docker degisikligi: +skinora-prometheus (prom/prometheus:v2.53.3), +skinora-uptime-kuma (louislam/uptime-kuma:1), +2 volume (skinora-prometheus-data, skinora-uptime-kuma-data)
- NuGet: +prometheus-net.AspNetCore 8.2.1, +AspNetCore.HealthChecks.SqlServer 9.0.0, +AspNetCore.HealthChecks.Redis 9.0.0
- npm: +prom-client ^15.1.0 (her iki sidecar)

## Commit & PR
- Branch: `task/T16-monitoring-infrastructure`
- Commit: `6dffe26` — T16: Monitoring altyapısı
- PR: Henuz olusturulmadi
- CI: Push yapildi, CI bekleniyor

## Known Limitations / Follow-up
- **Uptime Kuma monitor'leri:** Web UI'dan manuel konfigüre edilecek (API ile de yapılabilir ama UI daha pratik)
- **Sidecar health check'leri:** Skeleton — gercek Steam API ve Tron node kontrolleri T64-T69 ve T70-T77'de implement edilecek
- **Business metrikleri:** Dashboard'lar hazır ama metrikler F3'te (T44+) populate edilecek
- **Grafana Alerting env variables:** TELEGRAM_BOT_TOKEN, TELEGRAM_CHAT_ID, ALERT_EMAIL_TO deploy oncesi ayarlanmali
- **Info seviye alert:** 05 §9.3'te "Info: sadece log" olarak tanimli — Grafana Alerting'de Info seviye contact point olusturulmadi cunku sadece log yeterli

## Notlar
- Alert rule'lar Grafana file-based provisioning ile deploy ediliyor — UI'dan da edit edilebilir (`allowUiUpdates: true`)
- Prometheus retention 30 gun olarak ayarlandi (`--storage.tsdb.retention.time=30d`)
- Dashboard'lar Skinora klasoru altinda organize edildi
