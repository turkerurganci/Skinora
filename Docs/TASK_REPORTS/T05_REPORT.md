# T05 — Middleware Pipeline

**Faz:** F0 | **Durum:** ✓ Tamamlandı | **Tarih:** 2026-04-06

---

## Yapılan İşler
- ExceptionHandlingMiddleware: Global exception → HTTP status mapping (400/404/409/422/502/500), error envelope (07 §2.4), traceId, 500→Error / diğer→Warning loglama
- CorrelationIdMiddleware: X-Correlation-Id header üretme/okuma, response header'a ve Serilog LogContext'e taşıma
- ApiResponseWrapperFilter: Başarılı response'ları ApiResponse<T> envelope'una otomatik sarmalama (çifte sarmalama korumalı)
- SecurityHeadersMiddleware: CSP, X-Content-Type-Options, X-Frame-Options, Referrer-Policy, Permissions-Policy header'ları
- CORS konfigürasyonu: Sadece konfigüre edilen origin'lerden izin, credentials destekli
- CSRF koruması: Anti-forgery token (X-XSRF-TOKEN header, SameSite=Strict cookie)
- HTTPS zorlaması: HTTP → HTTPS redirect
- Serilog entegrasyonu: Structured JSON logging, LogContext enrichment
- Pipeline sıralaması: HTTPS → SecurityHeaders → CorrelationId → SerilogRequestLogging → ExceptionHandling → CORS → Routing → Antiforgery → Endpoints
- DiagnosticsController: Test/development için exception fırlatan endpoint'ler
- Integration testler: WebApplicationFactory tabanlı 12 test

## Etkilenen Modüller / Dosyalar
- `backend/src/Skinora.API/Middleware/CorrelationIdMiddleware.cs` (yeni)
- `backend/src/Skinora.API/Middleware/ExceptionHandlingMiddleware.cs` (yeni)
- `backend/src/Skinora.API/Middleware/SecurityHeadersMiddleware.cs` (yeni)
- `backend/src/Skinora.API/Filters/ApiResponseWrapperFilter.cs` (yeni)
- `backend/src/Skinora.API/Controllers/DiagnosticsController.cs` (yeni)
- `backend/src/Skinora.API/Program.cs` (güncelleme — pipeline konfigürasyonu)
- `backend/src/Skinora.API/Skinora.API.csproj` (güncelleme — FluentValidation, Serilog paketleri)
- `backend/src/Skinora.API/appsettings.json` (güncelleme — Serilog + CORS konfigürasyonu)
- `backend/tests/Skinora.API.Tests/Skinora.API.Tests.csproj` (güncelleme — Mvc.Testing paketi)
- `backend/tests/Skinora.API.Tests/Integration/MiddlewarePipelineTests.cs` (yeni)

## Kabul Kriterleri Kontrolü
| # | Kriter | Sonuç | Kanıt |
|---|---|---|---|
| 1 | ExceptionHandlingMiddleware: global exception → HTTP status mapping, error envelope, traceId, loglama | ✓ Karşılandı | 5 exception tipi test ile doğrulandı (404, 422, 409, 502, 500). Error envelope format 07 §2.4 ile eşleşiyor. |
| 2 | CorrelationIdMiddleware: X-Correlation-Id header üretme/okuma, tüm loglara taşıma | ✓ Karşılandı | 3 test: üretme, koruma, hata response'larında var. Serilog LogContext.PushProperty ile loglara taşınıyor. |
| 3 | ApiResponseWrapperFilter: başarılı response'ları ApiResponse<T> ile sarmalama | ✓ Karşılandı | Test: /ping → {success: true, data: {...}, error: null, traceId: "..."} |
| 4 | CORS middleware (sadece kendi domain) | ✓ Karşılandı | WithOrigins konfigürasyonu, AllowCredentials. appsettings'den okunuyor. |
| 5 | CSRF koruması (SameSite cookie + anti-forgery) | ✓ Karşılandı | AddAntiforgery: X-XSRF-TOKEN header, SameSite=Strict, Secure, HttpOnly cookie. |
| 6 | CSP header middleware | ✓ Karşılandı | Test: Content-Security-Policy header mevcut. default-src 'self'; frame-ancestors 'none'. |
| 7 | HTTPS zorlaması | ✓ Karşılandı | app.UseHttpsRedirection() pipeline'ın ilk sırasında. |
| 8 | Pipeline sıralaması doğru | ✓ Karşılandı | HTTPS → SecurityHeaders → CorrelationId → SerilogRequestLogging → ExceptionHandling → CORS → Routing → Antiforgery → Endpoints |

## Doğrulama Kontrol Listesi
| # | Kontrol | Sonuç | Kanıt |
|---|---|---|---|
| 1 | 07 §2.4 hata envelope formatı eşleşiyor mu? | ✓ | {success, data, error: {code, message, details}, traceId} — birebir eşleşiyor |
| 2 | 05 §6.3'teki güvenlik middleware'leri eksiksiz mi? | ✓ | Rate limiting (T07'de), CORS ✓, CSRF ✓, Input validation (FluentValidation mapping hazır), XSS/CSP ✓, HTTPS ✓ |
| 3 | 500 hataları Error, diğerleri Warning seviyesinde loglanıyor mu (09 §8.3)? | ✓ | ExceptionHandlingMiddleware: statusCode == 500 → LogError, diğerleri → LogWarning |

## Test Sonuçları
| Tür | Sonuç | Detay |
|---|---|---|
| Integration | ✓ 13/13 passed | `dotnet test tests/Skinora.API.Tests/` — MiddlewarePipelineTests (13 test) |
| Mevcut testler | ✓ 8/8 passed | EfCoreGlobalConfigTests regresyon yok |

## Doğrulama
| Alan | Sonuç |
|---|---|
| Doğrulama durumu | ✓ PASS |
| Validator | Claude (bağımsız doğrulama, 2026-04-06) |
| Bulgu sayısı | 0 |
| Düzeltme gerekli mi | Hayır |

### Güvenlik Kontrolü
- [x] Secret sızıntısı: Temiz (appsettings.json'da REPLACE_IN_ENV placeholder)
- [x] Auth etkisi: Temiz (T06'ya bırakıldı, placeholder yorum mevcut)
- [x] Input validation: Temiz (FluentValidation mapping hazır)
- [x] Yeni bağımlılık: FluentValidation.DependencyInjectionExtensions 11.11.0, Serilog.AspNetCore 9.0.0 — her ikisi de beklenen

### Yapım Raporu Karşılaştırması
- Uyum: Tam uyumlu
- Minor: Test sayısı 12 → 13 olarak düzeltildi (HealthEndpoint_StillWorks dahil edildi)

## Altyapı Değişiklikleri
- Migration: Yok
- Config/env değişikliği: appsettings.json'a Serilog ve CORS konfigürasyonu eklendi
- Docker değişikliği: Yok

## Commit & PR
- Branch: `task/T05-middleware-pipeline`
- Yapım commit: `cb20ae0`
- Merge commit: (merge sonrası güncellenecek)

## Known Limitations / Follow-up
- Rate limiting → T07'de implement edilecek
- Authentication/Authorization → T06'da implement edilecek (Program.cs'te placeholder yorum mevcut)
- Serilog → Loki sink → T08'de konfigüre edilecek (şu an Console sink aktif)
- DiagnosticsController production'da kısıtlanacak (T06+ auth ile)

## Notlar
- 09 §8.1 notu gereği controller'lar raw result döner, ApiResponseWrapperFilter sarar. Controller'da ApiResponse.Success çağrılmaz.
- ExceptionHandlingMiddleware error envelope, ApiResponseWrapperFilter success envelope sorumluluğunu taşır (sorumluluk ayrımı).
- `public partial class Program;` satırı WebApplicationFactory erişimi için eklendi.
