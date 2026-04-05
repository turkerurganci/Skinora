# Skinora — Implementation Plan

**Versiyon: v0.5** | **Bağımlılıklar:** `02_PRODUCT_REQUIREMENTS.md`, `03_USER_FLOWS.md`, `04_UI_SPECS.md`, `05_TECHNICAL_ARCHITECTURE.md`, `06_DATA_MODEL.md`, `07_API_DESIGN.md`, `08_INTEGRATION_SPEC.md`, `09_CODING_GUIDELINES.md`, `10_MVP_SCOPE.md` | **Son güncelleme:** 2026-03-28

---

## 1. Amaç ve Kapsam

Bu doküman, Skinora escrow platformunun MVP implementasyonunu sıralı, bağımlılıklı ve test beklentili task'lara böler. Her task'ın ne olduğunu, neye bağımlı olduğunu, hangi dokümanlarla yapılacağını ve nasıl kabul edileceğini tanımlar.

**Kapsam:** 10_MVP_SCOPE §2'de "dahil" olarak tanımlanan tüm özellikler (MVP-IN-001 – MVP-IN-069). "Hariç" özellikler §9 Post-MVP Parkı'nda kayıt altındadır.

**Denetim mekanizması:**
- **Traceability Matrix** (§7): Her doküman çıktısı en az bir task'a eşlenmiştir. Eşlenmeyen çıktı = eksik task.
- **Boşluk kontrolü**: Çift yönlü — ileriye (çıktı→task) ve geriye (task→çıktı).
- **MVP filtresi**: 10_MVP_SCOPE "hariç" öğeler task listesinden çıkarılmış, §9'da kayıt altındadır.

---

## 2. Kaynak Envanteri Özeti

Aşağıdaki dokümanlardan implementasyon öğeleri çıkarılmıştır:

| Doküman | Öğe Sayısı | Kapsam |
|---|---|---|
| 02 Product Requirements | 23 section | İş kuralları, kısıtlamalar, edge case'ler |
| 03 User Flows | 336 öğe | Akış adımları, dallanmalar, validasyonlar, bildirimler |
| 04 UI Specs | 199 öğe | 25 ekran, 17 ortak bileşen, 8 modal, 20 form, 62 state varyantı, 18 validasyon |
| 05 Technical Architecture | 113 öğe | Servisler, altyapı, middleware, job'lar, güvenlik, monitoring |
| 06 Data Model | 211 öğe | 25 entity, 23 enum, 56 constraint, 35 index, 3 seed data |
| 07 API Design | 299 öğe | 67 endpoint, 103 DTO, 40 validasyon, 16 SignalR event, 58 hata tanımı |
| 08 Integration Spec | 157 öğe | Entegrasyonlar, API çağrıları, webhook'lar, hata yönetimi |
| 09 Coding Guidelines | 66 öğe | Proje iskeleti, middleware, pattern, EF Core config, CI/CD, test altyapısı |
| 10 MVP Scope | 87 öğe | 69 dahil, 18 hariç |

> Detaylı envanter listeleri bu dokümanın hazırlanmasında kullanılmış olup doküman içine dahil edilmemiştir. Traceability Matrix (§7) her task'ın hangi kaynak öğeleri kapsadığını gösterir.

---

## 3. Faz Tanımları

Tüm task'lar 7 faza ayrılmıştır. Her faz bir önceki faz tamamlanmadan başlamaz (§3.1'de belirtilen kontrollü paralellik istisnaları hariç).

| Faz | Ad | Kapsam | Task Aralığı |
|---|---|---|---|
| F0 | Proje İskeleti | Solution yapısı, Docker, middleware, logging, CI/CD, test altyapısı, shared kernel | T01–T16 |
| F1 | Veri Katmanı | Entity'ler, enum'lar, constraint'ler, index'ler, migration, seed data | T17–T28 |
| F2 | Çekirdek Servisler | Auth, kullanıcı yönetimi, bildirim altyapısı, admin altyapısı, audit log | T29–T43 |
| F3 | İş Mantığı | Escrow akışı, state machine, timeout, komisyon, fraud, dispute, SignalR, retention | T44–T63b |
| F4 | Entegrasyonlar | Steam sidecar, blockchain sidecar, email, Telegram, Discord, sanctions, geo-block | T64–T83 |
| F5 | Kullanıcı Arayüzü | Tüm ekranlar, bileşenler, formlar, state varyantları, responsive, i18n | T84–T106 |
| F6 | Uçtan Uca Doğrulama | E2E testler, senaryo testleri, regresyon | T107–T114 |

### 3.1 Faz Bağımlılık Diyagramı

```
F0 → F1 → F2 → F3 → F4 → F5 → F6
                 ↘         ↗
                  F4 (kısmen F2 ile paralel başlayabilir — sidecar iskeletleri F0'da kurulur)
```

> **Not:** F4'ün sidecar iskeletleri F0'da kurulur (T14, T15). F4'ün iş mantığı task'ları F3'e bağımlıdır. F5 frontend task'ları F2+F3+F4 API'lerine bağımlıdır.

---

## 4. Hata Sınıflandırması ve Çözüm Akışı

### 4.1 Hata Seviyeleri

| Seviye | Tanım | Örnek |
|---|---|---|
| S1 — Sapma | Task tamamlandı ama dokümanla uyumsuz | Endpoint path dokümanla eşleşmiyor, entity field eksik, validasyon kuralı atlanmış |
| S2 — Kırılma | Bir task başka bir task'ın çıktısını bozuyor | Migration değişikliği mevcut servisi kırıyor, API sözleşmesi değişmiş ama consumer güncellenmemiş |
| S3 — Eksik | Traceability'de eşlenmiş bir öğe implement edilmemiş | Bir iş kuralı hiçbir yerde uygulanmamış, bir endpoint tanımlı ama kodu yok |

### 4.2 Tespit Mekanizması

| Seviye | Tespit Yöntemi | Zamanlama |
|---|---|---|
| Tümü | Task kabul kriterleri + doğrulama kontrol listesi | Her task tamamlandığında |
| S1, S3 | Traceability matrix "implemented" kolonu kontrolü | Her faz sonunda (gate check) |
| S2 | Regresyon testi (önceki fazların testleri tekrar çalıştırılır) | Her faz sonunda (gate check) |
| S3 | Boşluk taraması (eşlenip de implement edilmeyen öğeler) | Her faz sonunda (gate check) |

> **Not:** Doğrulama kontrol listesi, kodu yazan agent'tan farklı bir context'te çalıştırılır. Detaylı kurallar, VAL maddeleri, kanıt standardı ve süreç tanımı `12_VALIDATION_PROTOCOL.md`'de tanımlıdır.

### 4.3 Çözüm Akışı

```
Hata tespit edildi
  → Seviye belirlenir (S1/S2/S3)
  → Etki analizi: başka hangi task'lar etkileniyor?
  → Düzeltme task'ı oluşturulur (mevcut faza eklenir)
  → Düzeltme task'ı tamamlanır
  → Etkilenen task'ların doğrulama kontrol listeleri tekrar çalıştırılır
  → Gate check tekrar değerlendirilir
```

**Kritik kural:** Düzeltme task'ı bir sonraki faza ertelenmez — hatanın oluştuğu fazda çözülür.

### 4.4 Tıkanma Stratejisi

Bir task ilerleyemiyorsa:
1. Task daha küçük alt task'lara bölünür
2. Alt task'ların bağımlılıkları güncellenir
3. Faz kapısı (gate check) yeniden değerlendirilir
4. Tıkanmanın sebebi (eksik bilgi, teknik kısıt, dış bağımlılık) kayıt altına alınır

---

## 5. Task Listesi

### Task Yapısı

Her task aşağıdaki bilgileri içerir:

```
Task TXX: [Task adı]
  Bağımlılık: [Önceden tamamlanmış olması gereken task'lar]
  Dokümanlar: [Agent'a verilecek dosyalar]
  Kabul kriterleri: [Ne olduğunda "tamam"]
  Test beklentisi: [Unit / Integration / Contract / Yok]
  Doğrulama kontrol listesi: [Cross-check'te neye bakılacak]
```

---

### F0 — Proje İskeleti (T01–T16)

```
Task T01: .NET Solution ve proje yapısı oluşturma
  Bağımlılık: Yok
  Dokümanlar: 09 §4.1, §4.2
  Kabul kriterleri:
    - Skinora.sln oluşturuldu
    - src/ altında tüm modül projeleri var: Transactions, Payments, Steam, Users, Auth, Notifications, Admin, Disputes, Fraud
    - Skinora.Shared ve Skinora.API projeleri var
    - tests/ altında her modül için test projesi var
    - Proje referans kuralları doğru (API → modüller + Shared; modüller → Shared; modüller arası referans yok)
    - dotnet build başarılı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 09 §4.2'deki klasör yapısı birebir eşleşiyor mu?
    - [ ] Proje referans kuralları (§4.2.2) ihlal edilmiyor mu?
```

```
Task T02: Docker Compose ve ortam konfigürasyonu
  Bağımlılık: T01
  Dokümanlar: 05 §8.1, 09 §4.1
  Kabul kriterleri:
    - docker-compose.yml ve docker-compose.override.yml (dev) oluşturuldu
    - Servisler: backend (.NET), frontend (Next.js), steam-sidecar, blockchain-sidecar, sqlserver, redis, nginx
    - Her servis için Dockerfile var
    - .env.example dosyası tüm ortam değişkenlerini listeliyor
    - docker-compose up ile tüm servisler ayağa kalkıyor
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 05 §8.1'deki container listesi eksiksiz mi?
    - [ ] Health check tanımları var mı?
    - [ ] Secret'lar .env.example'da açıklanmış mı (değerler hariç)?
```

```
Task T03: Shared Kernel — base sınıflar, exception'lar, interface'ler
  Bağımlılık: T01
  Dokümanlar: 09 §4.2, §6.4, §8.3
  Kabul kriterleri:
    - BaseEntity (Id, CreatedAt, UpdatedAt), IAuditableEntity, ISoftDeletable (IsDeleted, DeletedAt) tanımlı
    - IDomainEvent interface (EventId GUID + OccurredAt) tanımlı
    - Exception hiyerarşisi: DomainException, BusinessRuleException, NotFoundException, IntegrationException
    - ApiResponse<T>, PagedResult<T> tanımlı
    - IUnitOfWork, IOutboxService interface tanımlı
    - Shared enum'lar: StablecoinType, NotificationType, TransactionStatus (ve diğer 06 §2'deki tüm enum'lar)
  Test beklentisi: Unit — enum değer kontrolleri
  Doğrulama kontrol listesi:
    - [ ] 06 §2'deki tüm enum'lar tanımlı mı?
    - [ ] IDomainEvent'te EventId ve OccurredAt zorunlu mu (09 §6.4)?
    - [ ] Shared/Events altında modüller arası event contract'lar var mı?
```

```
Task T04: EF Core global konfigürasyon
  Bağımlılık: T03
  Dokümanlar: 09 §7.1, §10.3, §10.4, §10.6
  Kabul kriterleri:
    - UtcDateTimeConverter oluşturuldu, ConfigureConventions'da tüm DateTime'lara uygulandı
    - Soft delete global query filter: HasQueryFilter(e => !e.IsDeleted) tüm ISoftDeletable entity'lerde
    - RowVersion property base'de tanımlı, IsRowVersion() EF config'de
    - Tüm FK'lerde DeleteBehavior.NoAction zorunlu
    - Nullable reference types aktif
  Test beklentisi: Integration — UTC converter doğru çalışıyor mu, soft delete filtresi aktif mi
  Doğrulama kontrol listesi:
    - [ ] 09 §7.1 UTC kuralı uygulanmış mı?
    - [ ] 09 §10.6 cascade kuralı uygulanmış mı?
    - [ ] Soft delete query filter'ı IgnoreQueryFilters() olmadan silinmiş kayıtları getirmiyor mu?
```

```
Task T05: Middleware pipeline
  Bağımlılık: T03
  Dokümanlar: 09 §8.1–§8.3, §18.4, 05 §6.3
  Kabul kriterleri:
    - ExceptionHandlingMiddleware: global exception → HTTP status mapping, error envelope, traceId, loglama
    - CorrelationIdMiddleware: X-Correlation-Id header üretme/okuma, tüm loglara taşıma
    - ApiResponseWrapperFilter: başarılı response'ları ApiResponse<T> ile sarmalama
    - CORS middleware (sadece kendi domain)
    - CSRF koruması (SameSite cookie + anti-forgery)
    - CSP header middleware
    - HTTPS zorlaması
    - Pipeline sıralaması doğru
  Test beklentisi: Integration — exception middleware doğru status dönüyor mu, correlation ID taşınıyor mu
  Doğrulama kontrol listesi:
    - [ ] 07 §2.4 hata envelope formatı eşleşiyor mu?
    - [ ] 05 §6.3'teki güvenlik middleware'leri eksiksiz mi?
    - [ ] 500 hataları Error, diğerleri Warning seviyesinde loglanıyor mu (09 §8.3)?
```

```
Task T06: Authentication altyapısı
  Bağımlılık: T03
  Dokümanlar: 05 §6.1, §6.2, 07 §2.3
  Kabul kriterleri:
    - JWT Bearer authentication konfigüre edildi (15dk access token)
    - Refresh token mekanizması tanımlı (HttpOnly + Secure + SameSite=Strict cookie)
    - Policy-based authorization tanımlı (kullanıcı, admin, permission bazlı)
    - [Authorize], [AllowAnonymous] attribute'ları kullanıma hazır
    - JWT signing key rotation desteği (grace period)
  Test beklentisi: Integration — geçerli/geçersiz JWT, expired token, policy kontrolü
  Doğrulama kontrol listesi:
    - [ ] 05 §6.1 JWT konfigürasyonu eşleşiyor mu?
    - [ ] Refresh token cookie flag'leri doğru mu (07 §2.3)?
    - [ ] Admin endpoint'leri permission kontrolü yapıyor mu?
```

```
Task T07: Rate limiting konfigürasyonu
  Bağımlılık: T05
  Dokümanlar: 07 §2.9, 05 §6.3
  Kabul kriterleri:
    - Redis-based rate limiting konfigüre edildi
    - Endpoint grupları: Auth 10/dk, GET 60/dk, POST/PUT/DELETE 20/dk, Steam inventory 5/dk, Admin okuma 120/dk, Admin yazma 30/dk, Public 30/dk
    - 429 response + Retry-After header
    - X-RateLimit-Limit, X-RateLimit-Remaining, X-RateLimit-Reset header'ları
  Test beklentisi: Integration — rate limit aşıldığında 429 dönüyor mu
  Doğrulama kontrol listesi:
    - [ ] 07 §2.9'daki tüm endpoint grupları tanımlı mı?
    - [ ] Header'lar doğru formatda mı?
```

```
Task T08: Logging altyapısı
  Bağımlılık: T02
  Dokümanlar: 05 §9.1, 09 §18.1, §18.3, §18.5
  Kabul kriterleri:
    - Serilog → Loki sink konfigüre edildi (.NET)
    - Pino → Loki push konfigüre edildi (Node.js sidecar'lar)
    - Structured JSON format, zorunlu field'lar: timestamp, level, message, correlationId
    - Secret maskeleme: private key, API key, refresh token, cüzdan adresi loglardan maskeleniyor
    - Grafana'da log görüntüleme çalışıyor
  Test beklentisi: Yok (altyapı doğrulaması — docker-compose up ile test)
  Doğrulama kontrol listesi:
    - [ ] 09 §18.5 maskeleme listesi eksiksiz mi?
    - [ ] CorrelationId tüm log'larda var mı?
```

```
Task T09: Hangfire setup ve background job altyapısı
  Bağımlılık: T02, T04
  Dokümanlar: 05 §2.2, 09 §13.1, §13.3–§13.7
  Kabul kriterleri:
    - Hangfire SQL Server storage konfigüre edildi
    - UTC timezone ayarı
    - AutomaticRetry(Attempts = 3) varsayılan
    - Timeout scheduling pattern tanımlı (delayed job schedule/cancel)
    - Job handler state doğrulama pattern tanımlı (güncel state kontrol, koşul tutmuyorsa no-op)
    - Timeout freeze/resume pattern tanımlı
    - Hangfire dashboard erişilebilir
  Test beklentisi: Integration — job schedule/cancel çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 09 §13.3 timeout scheduling pattern uygulanmış mı?
    - [ ] 09 §13.6 freeze/resume pattern uygulanmış mı?
    - [ ] Hangfire dashboard admin auth arkasında mı?
```

```
Task T10: Outbox pattern altyapısı
  Bağımlılık: T04, T09
  Dokümanlar: 05 §5.1, 09 §9.3, §13.4
  Kabul kriterleri:
    - IOutboxService implementasyonu: entity + outbox event yazma aynı DB transaction'da
    - Outbox Dispatcher: Hangfire self-rescheduling delayed job, saniye bazlı polling, distributed lock
    - Consumer idempotency: ProcessedEvent tablosu, EventId bazlı duplikasyon kontrolü
    - Program.cs'de dispatcher başlangıç tetiklemesi
    - External idempotency: X-Idempotency-Key header gönderim/alma pattern'ı, ExternalIdempotencyRecord lease mekanizması (05 §5.1)
    - Dispatcher PENDING ve FAILED durumları birlikte işler, max retry sonrası admin alert tetiklenir
  Test beklentisi: Integration — outbox event yazılıyor ve dispatcher tarafından işleniyor mu, duplikasyon engeliyor mu, external idempotency lease çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 05 §5.1 outbox pattern kuralları uygulanmış mı?
    - [ ] Atomik commit garantisi var mı (entity + event aynı transaction)?
    - [ ] Dispatcher distributed lock kullanıyor mu?
    - [ ] External idempotency key gönderim/alma ve lease mekanizması çalışıyor mu?
    - [ ] Dispatcher PENDING + FAILED birlikte işliyor mu, max retry sonrası admin alert tetikleniyor mu?
```

```
Task T11: CI/CD pipeline
  Bağımlılık: T01, T02
  Dokümanlar: 05 §8.4, 09 §21.1–§21.4
  Kabul kriterleri:
    - GitHub Actions workflow: Lint → Build → Unit test → Integration test → Contract test → Migration dry-run
    - Branch protection: main'e doğrudan push yasağı, CI geçmeden merge yasağı
    - Branch stratejisi: main, develop, feature branches
    - Docker image build ve push (ghcr.io)
  Test beklentisi: Yok (CI/CD kendisi test altyapısı)
  Doğrulama kontrol listesi:
    - [ ] 09 §21.4'teki 6 adımlı sıralama doğru mu?
    - [ ] Branch protection kuralları aktif mi?
```

```
Task T12: Test altyapısı
  Bağımlılık: T01, T04
  Dokümanlar: 09 §19.2, §19.6, §12.7
  Kabul kriterleri:
    - xUnit + Moq test projeleri her modül için kuruldu
    - IntegrationTestBase: TestContainers ile SQL Server container, EF Core migration, seed
    - Contract test altyapısı: sidecar ↔ backend sözleşme doğrulama (JSON schema)
    - Test naming convention: {MethodName}_{Scenario}_{ExpectedResult}
    - Test yapısı: Arrange-Act-Assert
  Test beklentisi: Yok (test altyapısının kendisi)
  Doğrulama kontrol listesi:
    - [ ] 09 §19.6 IntegrationTestBase yapısı doğru mu?
    - [ ] Her modül için Unit/ ve Integration/ klasörleri var mı?
```

```
Task T13: Next.js Frontend iskeleti
  Bağımlılık: T02
  Dokümanlar: 09 §4.3, §16.3, §16.4, §16.6
  Kabul kriterleri:
    - Next.js App Router projesi oluşturuldu
    - [locale] route grupları: auth, main, admin
    - Klasör yapısı: components/ui/, components/features/, lib/api/, lib/hooks/, lib/signalr/, types/, i18n/
    - API client (lib/api/client.ts): fetch wrapper, ApiResponse<T> unwrap, ApiError, Bearer token
    - State management: TanStack Query + Zustand
    - i18n: next-intl, 4 dil (EN, ZH, ES, TR), fallback EN
    - TypeScript enum'ları (C# karşılıkları): types/enums.ts
    - SignalR client setup: lib/signalr/connection.ts
    - ESLint + Prettier konfigüre
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 09 §4.3 klasör yapısı eşleşiyor mu?
    - [ ] API client 07 §2.4 envelope formatını unwrap ediyor mu?
    - [ ] i18n 4 dil dosyası mevcut mu?
```

```
Task T14: Steam Sidecar Node.js iskeleti
  Bağımlılık: T02
  Dokümanlar: 09 §4.4.1, §17.1–§17.9, 08 §2.5
  Kabul kriterleri:
    - Node.js TypeScript projesi oluşturuldu
    - Klasör yapısı: bot/, trade/, api/, webhook/, health/, config/
    - Kütüphaneler: steam-tradeoffer-manager ^3.x, steamcommunity ^3.x, steam-totp ^2.x, steam-user ^5.x
    - Webhook callback gönderim modülü: HMAC-SHA256 imzalama, timestamp/nonce/signature header
    - Health check endpoint: /health
    - Error class hiyerarşisi: SidecarError, SteamApiError, BotSessionExpiredError
    - Rate limiting istek kuyruğu (Steam API'ye)
    - Graceful shutdown handler
    - Pino logger (Loki push, correlationId)
    - ESLint + Prettier
    - Dockerfile
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 09 §4.4.1 klasör yapısı eşleşiyor mu?
    - [ ] 08 §2.5 kütüphane versiyonları doğru mu?
    - [ ] Webhook imzalama 05 §3.4 ile uyumlu mu?
```

```
Task T15: Blockchain Sidecar Node.js iskeleti
  Bağımlılık: T02
  Dokümanlar: 09 §4.4.2, §17.1–§17.9, 08 §3.1
  Kabul kriterleri:
    - Node.js TypeScript projesi oluşturuldu
    - Klasör yapısı: wallet/, monitor/, transfer/, api/, webhook/, health/, config/
    - TronWeb ^5.x kütüphanesi kuruldu
    - TronGrid API bağlantısı konfigüre (Mainnet + Testnet URL, API key)
    - Webhook callback gönderim modülü
    - Health check endpoint: /health
    - Error class hiyerarşisi: SidecarError, InsufficientGasError, TransactionFailedError
    - Pino logger + graceful shutdown + rate limiting queue
    - USDT/USDC kontrat adresleri config'de tanımlı
    - Dockerfile
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 08 §3.1 TronGrid endpoint'leri doğru mu?
    - [ ] 08 §3.3 kontrat adresleri doğru mu?
```

```
Task T16: Monitoring altyapısı
  Bağımlılık: T02, T08
  Dokümanlar: 05 §9.1–§9.5
  Kabul kriterleri:
    - Prometheus konfigüre (docker-compose'da)
    - .NET Prometheus client: metrics endpoint /metrics
    - prom-client (Node.js): metrics endpoint /metrics
    - Grafana dashboard konfigüre
    - Grafana Alerting: Telegram + Email (Critical/Warning/Info)
    - Uptime Kuma: HTTP/TCP external monitoring
    - Health check endpoint: /health (DB, Redis, Steam API, Tron node kontrolleri)
  Test beklentisi: Yok (altyapı doğrulaması)
  Doğrulama kontrol listesi:
    - [ ] 05 §9.2 metrik kaynakları tanımlı mı?
    - [ ] 05 §9.3 alert seviyeleri konfigüre mi?
    - [ ] 05 §9.5 health check bileşenleri eksiksiz mi?
```

---

### F1 — Veri Katmanı (T17–T28)

```
Task T17: Enum tanımları (C# + EF Core migration)
  Bağımlılık: T03
  Dokümanlar: 06 §2
  Kabul kriterleri:
    - 23 enum tanımlı: TransactionStatus (13), StablecoinType (2), BuyerIdentificationMethod (2), CancelledByType (4), BlockchainTransactionType (9), BlockchainTransactionStatus (4), TradeOfferDirection (3), TradeOfferStatus (6), DisputeType (3), DisputeStatus (3), FraudFlagType (4), ReviewStatus (3), NotificationType (20), NotificationChannel (3), PlatformSteamBotStatus (4), MonitoringStatus (5), OutboxMessageStatus (4), ActorType (3), AuditAction (12), TimeoutFreezeReason (4), FraudFlagScope (2), PayoutIssueStatus (5), DeliveryStatus (3)
    - EF Core'da string olarak saklanıyor (HasConversion)
    - Her enum değeri 06 §2 ile birebir eşleşiyor
  Test beklentisi: Unit — enum değer sayıları ve isimleri doğru mu
  Doğrulama kontrol listesi:
    - [ ] 06 §2'deki her enum tanımlı mı?
    - [ ] Her enum'ın değer sayısı dokümanla eşleşiyor mu?
```

```
Task T18: User, UserLoginLog, RefreshToken entity'leri
  Bağımlılık: T04, T17
  Dokümanlar: 06 §3.1–§3.3, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - User entity: tüm field'lar 06 §3.1'e göre (SteamId, DisplayName, AvatarUrl, DefaultPayoutAddress, DefaultRefundAddress, CompletedTransactionCount, SuccessfulTransactionRate, ReputationScore, CooldownExpiresAt, MobileAuthenticatorActive, Language, TosAcceptedAt, TosVersion, IsDeactivated, IsDeleted, vb.)
    - UserLoginLog entity: 06 §3.2'ye göre
    - RefreshToken entity: 06 §3.3'e göre (Token, ReplacedByTokenId self-ref)
    - Unique constraint'ler: User.SteamId, RefreshToken.Token
    - FK ilişkileri: UserLoginLog→User, RefreshToken→User, RefreshToken→RefreshToken (self)
    - Index'ler: User.DefaultPayoutAddress, User.DefaultRefundAddress, UserLoginLog.UserId/IpAddress/DeviceFingerprint, RefreshToken.UserId
    - Soft delete: User (kalıcı), RefreshToken (kalıcı)
  Test beklentisi: Integration — CRUD + soft delete + unique constraint violation
  Doğrulama kontrol listesi:
    - [ ] 06 §3.1 tüm User field'ları var mı?
    - [ ] 06 §5.1 unique constraint'ler tanımlı mı?
    - [ ] 06 §5.2 index'ler tanımlı mı?
```

```
Task T19: Transaction, TransactionHistory entity'leri
  Bağımlılık: T04, T17, T18
  Dokümanlar: 06 §3.5–§3.6, §4.1, §5.1, §5.2, §8.3, §8.7
  Kabul kriterleri:
    - Transaction entity: tüm field'lar 06 §3.5'e göre (Status, SellerId, BuyerId, item bilgileri, fiyat/komisyon, timeout süreleri, iptal bilgileri, hold bilgileri, freeze bilgileri, RowVersion, vb.)
    - TransactionHistory entity: 06 §3.6'ya göre (append-only)
    - Check constraint'ler: iptal state'lerinde CancelledBy/CancelReason/CancelledAt NOT NULL, hold constraint'leri, freeze constraint'leri, BuyerIdentificationMethod constraint
    - FK'ler: Transaction→User (seller, buyer, holdAdmin), TransactionHistory→Transaction, TransactionHistory→User (actor)
    - Unique: Transaction.InviteToken (filtered, WHERE NOT NULL)
    - Index'ler: Status (filtered), SellerId, BuyerId, CreatedAt, EscrowBotId; TransactionHistory.TransactionId
    - Optimistic concurrency: RowVersion
    - Computed field'lar: CommissionAmount, TotalAmount (06 §8.3)
  Test beklentisi: Integration — CRUD + check constraint + RowVersion concurrency
  Doğrulama kontrol listesi:
    - [ ] 06 §3.5 tüm field'lar ve check constraint'ler var mı?
    - [ ] RowVersion (§8.7) optimistic concurrency çalışıyor mu?
    - [ ] Komisyon hesaplama formülü doğru mu (§8.3)?
```

```
Task T20: PaymentAddress, BlockchainTransaction entity'leri
  Bağımlılık: T04, T17, T19
  Dokümanlar: 06 §3.7–§3.8, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - PaymentAddress entity: 06 §3.7'ye göre (Address, HdWalletIndex, MonitoringStatus, vb.)
    - BlockchainTransaction entity: 06 §3.8'e göre (TxHash, Type, Status, Amount, ConfirmationCount, ActualTokenAddress, vb.)
    - Unique: PaymentAddress.TransactionId, PaymentAddress.Address, PaymentAddress.HdWalletIndex; BlockchainTransaction.TxHash (filtered)
    - Check constraint'ler: BlockchainTransaction type-specific kurallar (BUYER_PAYMENT, WRONG_TOKEN_*, SPAM_*, giden transferler, status-specific)
    - FK'ler: PaymentAddress→Transaction, BlockchainTransaction→Transaction, BlockchainTransaction→PaymentAddress
    - Index'ler: BlockchainTransaction.TransactionId, Status (filtered PENDING), FromAddress; PaymentAddress.MonitoringStatus (filtered)
  Test beklentisi: Integration — CRUD + check constraint + unique constraint
  Doğrulama kontrol listesi:
    - [ ] 06 §3.8 check constraint'leri eksiksiz mi?
    - [ ] BlockchainTransactionType + status kombinasyonları doğru mu?
```

```
Task T21: TradeOffer, PlatformSteamBot entity'leri
  Bağımlılık: T04, T17, T19
  Dokümanlar: 06 §3.9–§3.10, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - TradeOffer entity: 06 §3.9'a göre (SteamTradeOfferId, Direction, Status, SentAt, RespondedAt, vb.)
    - PlatformSteamBot entity: 06 §3.10'a göre (SteamId, BotName, Status, ActiveEscrowCount, DailyTradeOfferCount, vb.)
    - Unique: TradeOffer.SteamTradeOfferId (filtered), PlatformSteamBot.SteamId
    - Check constraint'ler: TradeOffer status-specific (SENT→SentAt NOT NULL, ACCEPTED→RespondedAt NOT NULL, vb.)
    - FK'ler: TradeOffer→Transaction, TradeOffer→PlatformSteamBot; Transaction→PlatformSteamBot
    - Index'ler: TradeOffer.TransactionId, PlatformSteamBotId
    - Soft delete: PlatformSteamBot (kalıcı)
    - Denormalized: ActiveEscrowCount, DailyTradeOfferCount
  Test beklentisi: Integration — CRUD + check constraint
  Doğrulama kontrol listesi:
    - [ ] 06 §3.9 check constraint'leri doğru mu?
    - [ ] Bot denormalized field'ları güncellenebilir mi?
```

```
Task T22: Dispute, FraudFlag entity'leri
  Bağımlılık: T04, T17, T18, T19
  Dokümanlar: 06 §3.11–§3.12, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - Dispute entity: 06 §3.11'e göre (Type, Status, AutoCheckResult, EscalationDetail, AdminNote, vb.)
    - FraudFlag entity: 06 §3.12'ye göre (Type, Scope, ReviewStatus, Evidence, vb.)
    - Unique: Dispute (TransactionId + Type) unfiltered
    - Check constraint'ler: Dispute CLOSED→ResolvedAt NOT NULL; FraudFlag scope-specific + review-specific
    - FK'ler: Dispute→Transaction, User(opener), User(admin); FraudFlag→Transaction(opt), User(opt), User(reviewer)
    - Index'ler: Dispute.TransactionId, Status (filtered); FraudFlag.TransactionId, UserId, Status (filtered)
  Test beklentisi: Integration — CRUD + unique constraint (aynı türde tekrar dispute açılamaz)
  Doğrulama kontrol listesi:
    - [ ] 06 §3.12 FraudFlag scope constraint'leri doğru mu?
```

```
Task T23: Notification, NotificationDelivery, UserNotificationPreference entity'leri
  Bağımlılık: T04, T17, T18, T19
  Dokümanlar: 06 §3.4, §3.13–§3.13a, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - Notification entity: 06 §3.13'e göre (Type, UserId, TransactionId, Message, IsRead, vb.)
    - NotificationDelivery entity: 06 §3.13a'ya göre (Channel, DeliveryStatus, TargetExternalId, LastError, RetryCount, vb.)
    - UserNotificationPreference entity: 06 §3.4'e göre (Channel, IsEnabled, ExternalId, vb.)
    - Unique: NotificationDelivery (NotificationId + Channel); UserNotificationPreference (UserId + Channel, filtered); (Channel + ExternalId, filtered)
    - Check constraint'ler: DeliveryStatus-specific
    - FK'ler: Notification→User, Transaction(opt); NotificationDelivery→Notification; UserNotificationPreference→User
    - Index'ler: Notification (UserId + IsRead) composite, CreatedAt
    - Soft delete: UserNotificationPreference (kalıcı)
  Test beklentisi: Integration — CRUD + unique constraint
  Doğrulama kontrol listesi:
    - [ ] 06 §3.13a check constraint'leri doğru mu?
    - [ ] 06 §5.1 UserNotificationPreference unique filtered index'leri doğru mu?
```

```
Task T24: Admin entity'leri (AdminRole, AdminRolePermission, AdminUserRole)
  Bağımlılık: T04, T17, T18
  Dokümanlar: 06 §3.14–§3.16, §4.1, §5.1
  Kabul kriterleri:
    - AdminRole entity: 06 §3.14'e göre (Name, Description)
    - AdminRolePermission entity: 06 §3.15'e göre (Permission string)
    - AdminUserRole entity: 06 §3.16'ya göre (AssignedByAdminId)
    - Unique: AdminRole.Name; AdminRolePermission (AdminRoleId + Permission, filtered); AdminUserRole (UserId + AdminRoleId, filtered)
    - FK'ler: AdminRolePermission→AdminRole; AdminUserRole→User, AdminRole, User(assigner)
    - Soft delete: AdminRole, AdminRolePermission, AdminUserRole (kalıcı)
  Test beklentisi: Integration — CRUD + unique
  Doğrulama kontrol listesi:
    - [ ] 06 §3.14–§3.16 field'ları eksiksiz mi?
```

```
Task T25: Altyapı entity'leri (SystemSetting, OutboxMessage, ProcessedEvent, ExternalIdempotencyRecord, AuditLog, ColdWalletTransfer, SystemHeartbeat, SellerPayoutIssue)
  Bağımlılık: T04, T17, T18, T19
  Dokümanlar: 06 §3.17–§3.23, §3.8a, §4.1, §5.1, §5.2
  Kabul kriterleri:
    - SystemSetting: Key, Value, DataType, Category, Description, IsConfigured, vb. Check: DataType IN ('int','decimal','bool','string')
    - OutboxMessage: EventType, Payload, Status, ProcessedAt, ErrorMessage, vb. Check: status-specific
    - ProcessedEvent: EventId, ConsumerName. Unique: (EventId + ConsumerName)
    - ExternalIdempotencyRecord: ServiceName, IdempotencyKey, Status, LeaseExpiresAt, ResultPayload, vb. Check: status-specific + Status IN (...)
    - AuditLog: ActorType, ActorId, Action, EntityType, EntityId, Detail, vb. FK: ActorId→User, UserId→User(opt). Append-only (immutable)
    - ColdWalletTransfer: TxHash (unique), Amount, vb. Append-only
    - SystemHeartbeat: Id CHECK (Id = 1) singleton, LastHeartbeat
    - SellerPayoutIssue: 06 §3.8a. Check: status-specific. Unique: TransactionId (filtered WHERE != RESOLVED)
    - Tüm index'ler tanımlı (§5.2)
  Test beklentisi: Integration — CRUD + constraint'ler + AuditLog immutability (update/delete engeli)
  Doğrulama kontrol listesi:
    - [ ] 06 §3.17–§3.23 ve §3.8a tüm entity'ler ve constraint'ler var mı?
    - [ ] AuditLog'a UPDATE/DELETE yapılamıyor mu?
    - [ ] SystemHeartbeat singleton garantisi var mı?
```

```
Task T26: Seed data
  Bağımlılık: T18, T25
  Dokümanlar: 06 §8.9
  Kabul kriterleri:
    - SYSTEM service account: User tablosunda sabit GUID (00000000-0000-0000-0000-000000000001), SteamId="00000000000000001", IsDeactivated=true
    - SystemHeartbeat: Id=1 ile tek satır
    - SystemSetting: 27 platform parametresi seed edildi (accept_timeout_minutes, commission_rate, vb.), varsayılanı olanlar IsConfigured=true, olmayanlar false
    - Env var bootstrap: SKINORA_SETTING_{KEY_UPPER} formatında env var ile SystemSetting hydration
    - Startup fail-fast: IsConfigured=false olan zorunlu parametreler kontrol edildi
  Test beklentisi: Integration — seed data doğru yükleniyor mu, fail-fast çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 06 §8.9'daki tüm seed kayıtları var mı?
    - [ ] 27 SystemSetting parametresi eksiksiz mi?
    - [ ] Env var bootstrap doğru çalışıyor mu?
```

```
Task T27: Performans index'leri ve filtered index'ler
  Bağımlılık: T18–T25
  Dokümanlar: 06 §5.2
  Kabul kriterleri:
    - 06 §5.2'deki tüm index'ler tanımlı (35 index)
    - Filtered index'ler HasFilter() ile SQL Server'a özgü tanımlanmış
    - Composite index'ler doğru sırada
  Test beklentisi: Yok (migration ile doğrulanır)
  Doğrulama kontrol listesi:
    - [ ] 06 §5.2'deki her index migration'da var mı?
    - [ ] Filtered index koşulları doğru mu?
```

```
Task T28: Initial migration ve migration testi
  Bağımlılık: T17–T27
  Dokümanlar: 05 §2.4, 09 §21.4
  Kabul kriterleri:
    - dotnet ef migrations add InitialCreate ile migration oluşturuldu
    - Migration boş bir SQL Server'a uygulandığında hatasız çalışıyor
    - Seed data migration sonrası doğru yükleniyor
    - CI pipeline'da migration dry-run adımı var
  Test beklentisi: Integration — temiz DB'ye migration + seed doğrulaması
  Doğrulama kontrol listesi:
    - [ ] Tüm entity'ler, constraint'ler, index'ler migration'da var mı?
    - [ ] Migration idempotent mi (tekrar çalıştırılınca hata vermiyor mu)?
```

---

### F2 — Çekirdek Servisler (T29–T43)

```
Task T29: Steam OpenID authentication (login + callback + token üretimi)
  Bağımlılık: T06, T18
  Dokümanlar: 07 §4.2–§4.3, 08 §2.1, 03 §2.1
  Kabul kriterleri:
    - GET /auth/steam → Steam OpenID sayfasına redirect
    - GET /auth/steam/callback → assertion doğrulama, kullanıcı oluşturma/güncelleme, JWT + refresh token üretimi
    - Güvenlik: assertion backend'de doğrulanır (claimed_id güvenilmez), return URL kontrolü, nonce replay koruması, HTTPS zorunlu
    - İlk kez giriş: ToS gösterilmeli (tosAccepted kontrolü)
    - returnUrl sadece relative path kabul eder
    - GetPlayerSummaries çağrısı ile profil bilgileri çekilir
    - Geo-block kontrolü (IP bazlı, yasaklı bölge → engel)
    - Sanctions eşleşmesi kontrolü (profil adresi)
    - Hesap askıya alınmış mı kontrolü (kısıtlı oturum)
  Test beklentisi: Integration — geçerli/geçersiz callback, yeni kullanıcı oluşturma, mevcut kullanıcı güncelleme
  Doğrulama kontrol listesi:
    - [ ] 08 §2.1 güvenlik kuralları uygulanmış mı?
    - [ ] 03 §2.1 akış adımları karşılanmış mı?
    - [ ] 07 §4.2–§4.3 endpoint sözleşmesi eşleşiyor mu?
```

```
Task T30: ToS kabul, yaş gate, geo-block
  Bağımlılık: T29, T26
  Dokümanlar: 07 §4.4, 02 §21.1, 03 §2.1, §11a
  Kabul kriterleri:
    - POST /auth/tos/accept → ToS versiyonu kaydedilir
    - Yaş gate: 18+ beyanı + Steam hesap yaşı kontrolü, başarısız → erişim engeli
    - Geo-block: IP bazlı coğrafi engelleme, yasaklı ülke listesi admin tarafından yönetilebilir
    - VPN/proxy tespiti destekleyici sinyal olarak (tek başına engelleme sebebi değil)
  Test beklentisi: Integration — ToS kabul, geo-block engeli, yaş gate engeli
  Doğrulama kontrol listesi:
    - [ ] 07 §4.4 ToS endpoint sözleşmesi doğru mu?
    - [ ] 02 §21.1 erişim kuralları eksiksiz mi?
```

```
Task T31: Steam re-verify ve authenticator kontrolü
  Bağımlılık: T29
  Dokümanlar: 07 §4.6–§4.8, 08 §2.2
  Kabul kriterleri:
    - POST /auth/steam/re-verify → Steam re-auth başlatma (purpose + returnUrl)
    - GET /auth/steam/re-verify/callback → reAuthToken üretimi (kısa ömürlü)
    - POST /auth/check-authenticator → GetTradeHoldDurations ile MA kontrolü
    - Referrer-Policy: same-origin (reAuthToken sızma koruması)
    - X-ReAuth-Token header doğrulaması (cüzdan değişikliğinde kullanılacak)
  Test beklentisi: Integration — re-verify akışı, authenticator kontrolü
  Doğrulama kontrol listesi:
    - [ ] 07 §4.6–§4.8 endpoint sözleşmeleri doğru mu?
    - [ ] 08 §2.2 GetTradeHoldDurations çağrısı doğru mu?
```

```
Task T32: Refresh token yönetimi
  Bağımlılık: T29
  Dokümanlar: 07 §4.9–§4.10, 05 §6.1
  Kabul kriterleri:
    - POST /auth/refresh → access token yenileme (refresh cookie'den)
    - POST /auth/logout → refresh token revoke, cookie temizleme
    - GET /auth/me → mevcut oturum bilgisi
    - Token rotation: kullanılan refresh token invalidate, yeni üretilir
    - DB source of truth + Redis cache
    - Expired/revoked token cleanup (periyodik)
  Test beklentisi: Integration — refresh, logout, expired token, rotation
  Doğrulama kontrol listesi:
    - [ ] 07 §4.9–§4.10 sözleşmeleri doğru mu?
    - [ ] Token rotation çalışıyor mu?
    - [ ] Kullanılmış refresh token ile tekrar istek → 401?
```

```
Task T33: User profil servisi
  Bağımlılık: T29, T18
  Dokümanlar: 07 §5.1–§5.2, §5.5
  Kabul kriterleri:
    - GET /users/me → kendi profil (wallet adresleri, skor, istatistikler)
    - GET /users/me/stats → dashboard hızlı istatistikler
    - GET /users/:steamId → public profil (sınırlı alanlar)
  Test beklentisi: Integration — kendi profil, public profil, user not found
  Doğrulama kontrol listesi:
    - [ ] 07 §5.1–§5.5 response DTO'ları doğru mu?
```

```
Task T34: Cüzdan adresi yönetimi
  Bağımlılık: T31, T33
  Dokümanlar: 07 §5.3–§5.4, 02 §12, 03 §9
  Kabul kriterleri:
    - PUT /users/me/wallet/seller → satıcı ödeme adresi kaydet/güncelle
    - PUT /users/me/wallet/refund → alıcı iade adresi kaydet/güncelle
    - Merkezi doğrulama pipeline: TRC-20 format + sanctions screening
    - Mevcut adres varsa X-ReAuth-Token zorunlu (Steam re-verify)
    - Cooldown: satıcı → yeni işlem başlatma engeli; alıcı → yeni işlem başlatma + kabul engeli
    - Aktif işlemler eski adresle tamamlanır (snapshot prensibi)
    - Adres onay adımı
  Test beklentisi: Integration — adres kayıt, güncelleme, format validation, sanctions block, cooldown
  Doğrulama kontrol listesi:
    - [ ] 02 §12 tüm kurallar uygulanmış mı?
    - [ ] Sanctions eşleşmesinde hesap flag'leniyor mu?
    - [ ] Cooldown mekanizması çalışıyor mu?
```

```
Task T35: Hesap ayarları (dil, bildirim tercihleri, Telegram/Discord bağlama)
  Bağımlılık: T33, T23
  Dokümanlar: 07 §5.6–§5.15, §5.16a
  Kabul kriterleri:
    - GET /users/me/settings → hesap ayarları
    - PUT /users/me/settings/language → dil değiştirme (en, zh, es, tr)
    - PUT /users/me/settings/notifications → bildirim tercihleri
    - POST/DELETE telegram/discord bağlantı endpoint'leri
    - Email doğrulama akışı (send-verification + verify)
    - PUT /users/me/settings/steam/trade-url → trade URL kayıt + MA doğrulama
  Test beklentisi: Integration — dil değiştirme, bildirim tercih, trade URL kayıt
  Doğrulama kontrol listesi:
    - [ ] 07 §5.6–§5.16a tüm endpoint'ler var mı?
    - [ ] Trade URL kaydında MA kontrolü yapılıyor mu (08 §2.2)?
```

```
Task T36: Hesap deaktif ve silme
  Bağımlılık: T33
  Dokümanlar: 07 §5.17, 02 §19, 06 §6.2
  Kabul kriterleri:
    - POST /users/me/deactivate → hesap deaktif (aktif işlem kontrolü)
    - DELETE /users/me → hesap silme (confirmation="SİL", aktif işlem kontrolü)
    - Silme: soft delete + PII temizleme (SteamId→ANON_{GUID}, DisplayName→"Deleted User", adresler temiz)
    - UserNotificationPreference soft delete + ExternalId temiz
    - RefreshToken revoke + soft delete
    - NotificationDelivery.TargetExternalId masked format
    - İşlem geçmişi ve audit log anonim olarak saklanır
  Test beklentisi: Integration — deaktif, silme, PII temizleme, aktif işlem engeli
  Doğrulama kontrol listesi:
    - [ ] 06 §6.2 anonimleştirme formatı birebir eşleşiyor mu?
    - [ ] Silinen kullanıcının audit log'ları korunuyor mu?
```

```
Task T37: Bildirim altyapı servisi
  Bağımlılık: T10, T23
  Dokümanlar: 05 §7.2–§7.5, 02 §18
  Kabul kriterleri:
    - Domain event → Notification entity dönüşümü
    - Kanal dispatching: kullanıcı tercihlerine göre hangi kanallara gönderileceği belirlenir
    - Bildirim retry stratejisi: exponential backoff, 3 deneme, başarısızlıkta admin alert
    - NotificationDelivery kaydı oluşturma (kanal bazlı teslimat takibi)
    - Lokalizasyon altyapısı: .resx resource dosya yapısı, 4 dil desteği, kanal bazlı format (placeholder metinlerle — final mesaj içerikleri Post-MVP)
  Test beklentisi: Integration — event → notification oluşturma, kanal dispatching, retry
  Doğrulama kontrol listesi:
    - [ ] 02 §18.2 tüm bildirim tetikleyicileri tanımlı mı?
    - [ ] 05 §7.5 retry stratejisi uygulanmış mı?
```

```
Task T38: Platform içi bildirim kanalı
  Bağımlılık: T37
  Dokümanlar: 05 §7.2, 07 §8.1–§8.4
  Kabul kriterleri:
    - GET /notifications → bildirim listesi (paginated)
    - GET /notifications/unread-count → okunmamış sayı
    - POST /notifications/mark-all-read → tümünü okundu
    - PUT /notifications/:id/read → tek bildirim okundu
    - Notification tablosuna yazma
  Test beklentisi: Integration — bildirim listeleme, okundu işaretleme, sayaç
  Doğrulama kontrol listesi:
    - [ ] 07 §8.1–§8.4 endpoint sözleşmeleri doğru mu?
```

```
Task T39: Admin rol ve yetki yönetimi
  Bağımlılık: T24, T06
  Dokümanlar: 07 §9.11–§9.18, 02 §16
  Kabul kriterleri:
    - GET /admin/roles → rol listesi + mevcut yetkiler
    - POST /admin/roles → yeni rol oluşturma
    - PUT /admin/roles/:id → rol güncelleme
    - DELETE /admin/roles/:id → rol silme (atanmış kullanıcı varsa engel)
    - GET /admin/users → admin kullanıcı listesi
    - GET /admin/users/:steamId → kullanıcı detay
    - PUT /admin/users/:id/role → rol atama
    - 11 yetki tanımı (MANAGE_FLAGS, CANCEL_TRANSACTIONS, EMERGENCY_HOLD, vb.)
  Test beklentisi: Integration — rol CRUD, yetki atama, rol silme engeli
  Doğrulama kontrol listesi:
    - [ ] 07 §9.11–§9.18 endpoint'leri eksiksiz mi?
    - [ ] Atanmış kullanıcılı rol silinemez mi?
```

```
Task T40: Admin RBAC (policy-based authorization)
  Bağımlılık: T39
  Dokümanlar: 05 §6.2, 07 §9
  Kabul kriterleri:
    - Her admin endpoint'inde permission kontrolü
    - Policy-based authorization .NET built-in ile
    - Dinamik rol grupları (DB'den okunan yetkiler)
    - INSUFFICIENT_PERMISSION (403) hata dönüşü
  Test beklentisi: Integration — yetkili/yetkisiz erişim, dinamik yetki değişikliği
  Doğrulama kontrol listesi:
    - [ ] 07 §9'daki her endpoint'in hangi yetkiyi gerektirdiği belirli mi?
```

```
Task T41: Admin parametre yönetimi
  Bağımlılık: T26, T40
  Dokümanlar: 07 §9.8–§9.9, 02 §16.2
  Kabul kriterleri:
    - GET /admin/settings → tüm platform parametreleri
    - PUT /admin/settings/:key → tek parametre güncelleme
    - Parametre değişikliği anında aktif olur, aktif işlemleri etkilemez
    - AuditLog kaydı oluşturulur
    - Tüm 02 §16.2'deki parametreler yönetilebilir
  Test beklentisi: Integration — parametre okuma/güncelleme, audit log
  Doğrulama kontrol listesi:
    - [ ] 02 §16.2'deki tüm parametreler mevcut mu?
    - [ ] 07 §9.8–§9.9 sözleşmeleri doğru mu?
```

```
Task T42: AuditLog servisi
  Bağımlılık: T25, T03
  Dokümanlar: 05 §5.4, 06 §3.20, 09 §18.6
  Kabul kriterleri:
    - Merkezi AuditLog servisi: tüm audit kayıtları bu servis üzerinden yazılır
    - ActorType + ActorId invariantı zorunlu
    - Doğrudan INSERT yasağı (sadece servis üzerinden)
    - Immutable kayıt (UPDATE/DELETE engeli)
    - GET /admin/audit-logs → audit log listesi (paginated, filtrelenebilir)
    - 12 AuditAction türü destekleniyor
  Test beklentisi: Integration — audit kaydı oluşturma, listeleme, filtreleme, immutability
  Doğrulama kontrol listesi:
    - [ ] 06 §3.20 AuditLog yapısı doğru mu?
    - [ ] 09 §18.6 merkezi servis kuralları uygulanmış mı?
```

```
Task T43: User itibar skoru hesaplama
  Bağımlılık: T18, T19
  Dokümanlar: 02 §13, 06 §8.2
  Kabul kriterleri:
    - Tamamlanan işlem sayısı denormalized güncelleme (COMPLETED'da)
    - Başarılı işlem oranı hesaplama (sorumluluk bazlı — kimin iptal ettiğine göre)
    - Hesap yaşı hesaplama
    - İptal oranı skoru etkiliyor
    - Wash trading: aynı çift arasındaki işlemler skora etki etmiyor
    - CooldownExpiresAt hesaplama (iptal limiti aşıldığında)
  Test beklentisi: Unit — skor hesaplama formülleri; Integration — denormalized güncelleme
  Doğrulama kontrol listesi:
    - [ ] 02 §13 skor kriterleri uygulanmış mı?
    - [ ] 06 §8.2 denormalized field güncelleme kuralları doğru mu?
```

---

### F3 — İş Mantığı (T44–T63)

```
Task T44: Transaction State Machine
  Bağımlılık: T19, T03
  Dokümanlar: 05 §4.1–§4.5, 09 §9.2
  Kabul kriterleri:
    - Stateless kütüphanesi ile TransactionStateMachine sınıfı
    - 13 durum, tüm geçişler deklaratif olarak tanımlı
    - Guard mekanizması: geçersiz geçişler DomainException fırlatır
    - RowVersion doğrulama guard'da
    - OnEntry/OnExit side effect handler'ları (bildirim, timeout başlatma)
    - Emergency hold mekanizması (IsOnHold flag, dondurma/çözme)
    - 06 §3.5 status → zorunlu field matrisi guard olarak uygulanmış (FLAGGED state kuralları dahil: tüm deadline/job NULL)
  Test beklentisi: Unit — her durum × her trigger (geçerli + geçersiz), 05 §4.1 durum geçiş tablosuyla birebir eşleşme
  Doğrulama kontrol listesi:
    - [ ] 05 §4.1 durum geçiş tablosu birebir eşleşiyor mu?
    - [ ] Geçersiz geçişler DomainException fırlatıyor mu?
    - [ ] RowVersion guard çalışıyor mu?
    - [ ] 06 §3.5 status → zorunlu field matrisi birebir eşleşiyor mu?
```

```
Task T45: İşlem oluşturma akışı
  Bağımlılık: T44, T34, T43
  Dokümanlar: 07 §7.1–§7.4, 02 §2, §6, §8, §14.4, 03 §2.2
  Kabul kriterleri:
    - GET /transactions/eligibility → uygunluk kontrolü (MA, concurrent limit, cooldown, new account limit, flag, cooldown)
    - GET /transactions/params → form parametreleri (fiyat aralığı, komisyon, timeout aralığı, stablecoin'ler)
    - POST /transactions → işlem oluşturma
    - Validasyonlar: stablecoin, fiyat min/max, timeout aralığı, buyerIdentificationMethod, Steam ID, item tradeable
    - Steam envanter okuma (API çağrısı T67'de implement edilecek, burada interface üzerinden)
    - Fraud pre-check: fiyat sapması eşiği → FLAGGED (pre-create)
    - Alıcı belirleme: Steam ID veya açık link (admin toggle)
    - Cüzdan adresi zorunluluk kontrolü
    - Outbox event: TransactionCreatedEvent
    - Bildirim: alıcıya davet (kayıtlıysa), satıcıya davet linki
  Test beklentisi: Unit — validasyonlar, fraud pre-check; Integration — tam oluşturma akışı
  Doğrulama kontrol listesi:
    - [ ] 07 §7.1–§7.4 endpoint sözleşmeleri doğru mu?
    - [ ] 02 §2, §6, §8, §14.4 iş kuralları eksiksiz mi?
    - [ ] 03 §2.2 akış adımları karşılanmış mı?
```

```
Task T46: Alıcı kabul akışı
  Bağımlılık: T44, T34
  Dokümanlar: 07 §7.5–§7.6, 02 §6, 03 §3.1–§3.2
  Kabul kriterleri:
    - GET /transactions/:id → işlem detay (public/authenticated, role bazlı varyant)
    - POST /transactions/:id/accept → alıcı kabulü
    - Steam ID eşleşme kontrolü (Yöntem 1) veya açık link (Yöntem 2, ilk gelen)
    - İade adresi zorunlu (TRC-20 format + sanctions)
    - Alıcı refund-address cooldown kontrolü
    - State geçişi: CREATED → ACCEPTED
    - Outbox event: BuyerAcceptedEvent
    - Bildirim: satıcıya "alıcı kabul etti"
  Test beklentisi: Unit — Steam ID eşleşme, validasyonlar; Integration — kabul akışı
  Doğrulama kontrol listesi:
    - [ ] 07 §7.5–§7.6 sözleşmeleri doğru mu?
    - [ ] 03 §3.1–§3.2 akış adımları karşılanmış mı?
    - [ ] Yöntem 1 ve 2 ayrımı doğru mu?
```

```
Task T47: Timeout scheduling
  Bağımlılık: T44, T09
  Dokümanlar: 02 §3, 05 §4.4, 09 §13.3
  Kabul kriterleri:
    - Her state geçişinde ilgili timeout Hangfire delayed job olarak schedule edilir
    - Job ID entity'ye kaydedilir
    - İptal/tamamlanma/state değişikliğinde mevcut job temizlenir ve yeni schedule yapılır
    - Deadline scanner/poller job: AcceptDeadline, TradeOfferToSellerDeadline, TradeOfferToBuyerDeadline enforce
    - Heartbeat job: 30sn periyodik, LastHeartbeat güncelleme
    - Restart recovery: outage window hesaplama, aktif işlem timeout'larını uzatma
  Test beklentisi: Integration — job schedule/cancel, deadline enforce
  Doğrulama kontrol listesi:
    - [ ] 02 §3 tüm timeout adımları schedule ediliyor mu?
    - [ ] 05 §4.4 heartbeat ve recovery pattern'ları uygulanmış mı?
```

```
Task T48: Timeout warning
  Bağımlılık: T47, T37
  Dokümanlar: 02 §3.4, 05 §4.4
  Kabul kriterleri:
    - Timeout süresi dolmadan önce uyarı (admin tarafından ayarlanabilir oran)
    - TimeoutWarningEvent üretimi
    - Bildirim: ilgili tarafa tüm kanallarda "süreniz dolmak üzere"
  Test beklentisi: Unit — uyarı eşiği hesaplama; Integration — uyarı event üretimi
  Doğrulama kontrol listesi:
    - [ ] 02 §3.4 uyarı kuralları uygulanmış mı?
```

```
Task T49: Timeout execution
  Bağımlılık: T47, T44
  Dokümanlar: 02 §3.2, 03 §4.1–§4.5
  Kabul kriterleri:
    - Kabul timeout → CANCELLED_TIMEOUT (iade gerekmez)
    - Trade offer timeout → CANCELLED_TIMEOUT (iade gerekmez — item henüz platformda değil)
    - Ödeme timeout → CANCELLED_TIMEOUT (item satıcıya iade)
    - Teslim timeout → CANCELLED_TIMEOUT (item satıcıya iade, ödeme alıcıya iade)
    - Her senaryoda doğru iade tetikleme
    - Gecikmeli ödeme izleme başlatma (ödeme timeout sonrası)
  Test beklentisi: Unit — her timeout senaryosu; Integration — state geçişi + iade tetikleme
  Doğrulama kontrol listesi:
    - [ ] 03 §4.1–§4.5 timeout sonuçları birebir eşleşiyor mu?
    - [ ] 02 §3.2 iade kuralları doğru mu?
```

```
Task T50: Timeout freeze/resume
  Bağımlılık: T47
  Dokümanlar: 02 §3.3, 05 §4.4–§4.5
  Kabul kriterleri:
    - Platform bakımı: tüm aktif işlemlerin timeout'ları dondurulur
    - Steam kesintisi: Steam bağımlı adımlardaki timeout'lar dondurulur
    - Blockchain degradasyonu: ödeme adımındaki timeout'lar dondurulur
    - Emergency hold: tek işlem dondurma
    - TimeoutFrozenAt, TimeoutFreezeReason, TimeoutRemainingSeconds set edilir
    - Resume: frozen süre hesaplanır, deadline uzatılır, job yeniden schedule
  Test beklentisi: Unit — freeze/resume hesaplama; Integration — freeze/resume cycle
  Doğrulama kontrol listesi:
    - [ ] 02 §3.3 tüm freeze senaryoları var mı?
    - [ ] 05 §4.5 emergency hold mekanizması doğru mu?
```

```
Task T51: İptal akışı
  Bağımlılık: T44, T37
  Dokümanlar: 07 §7.7, 02 §7, 03 §2.5, §3.3
  Kabul kriterleri:
    - POST /transactions/:id/cancel → satıcı/alıcı iptali
    - Kontroller: ödeme gönderilmişse iptal engeli, taraf kontrolü, state kontrolü
    - İptal sebebi zorunlu (min 10 karakter)
    - Item platformdaysa → satıcıya iade tetikleme
    - Ödeme alınmışsa → alıcıya iade tetikleme (fiyat + komisyon - gas fee)
    - CANCELLED_SELLER / CANCELLED_BUYER / CANCELLED_TIMEOUT / CANCELLED_ADMIN state'leri
    - İptal kaydı itibar skoruna yansıtılır
    - İptal cooldown hesaplama
    - Bildirimler: karşı tarafa iptal bildirimi
  Test beklentisi: Unit — her iptal senaryosu, validasyonlar; Integration — iptal + iade + bildirim
  Doğrulama kontrol listesi:
    - [ ] 02 §7 tüm iptal kuralları uygulanmış mı?
    - [ ] 07 §7.7 sözleşmesi doğru mu?
```

```
Task T52: Komisyon ve finansal hesaplamalar
  Bağımlılık: T19
  Dokümanlar: 02 §5, §4.6–§4.7, 06 §8.3, 09 §14
  Kabul kriterleri:
    - CommissionAmount = ROUND(Price × CommissionRate, 6, MidpointRounding.ToZero)
    - TotalAmount = Price + CommissionAmount
    - İade tutarı = Price + CommissionAmount - GasFee
    - Gas fee koruma eşiği: gas fee > komisyon × %10 → aşan kısım satıcı payından kesilir
    - decimal kullanımı zorunlu, ara adımda yuvarlama yok
    - Payment validation: gelen tutar beklenenle tam eşleşme (tolerance yok)
  Test beklentisi: Unit — tüm hesaplama formülleri, boundary value analysis (09 §14.5)
  Doğrulama kontrol listesi:
    - [ ] 09 §14 hesaplama kuralları eksiksiz mi?
    - [ ] 06 §8.3 formüller birebir eşleşiyor mu?
```

```
Task T53: Gas fee yönetimi
  Bağımlılık: T52
  Dokümanlar: 02 §4.7, 09 §14
  Kabul kriterleri:
    - Satıcıya gönderim: gas fee komisyondan karşılanır
    - Koruma eşiği aşılırsa: aşan kısım satıcı payından kesilir
    - İade: gas fee iade tutarından düşülür
    - Minimum iade eşiği: tutar < 2× gas fee → iade yapılmaz, admin alert
  Test beklentisi: Unit — eşik hesaplamaları, minimum iade kontrolü
  Doğrulama kontrol listesi:
    - [ ] 02 §4.7 gas fee kuralları eksiksiz mi?
```

```
Task T54: Fraud flag sistemi
  Bağımlılık: T22, T44, T42
  Dokümanlar: 02 §14.0, 07 §9.2–§9.5, 03 §7–§8.2
  Kabul kriterleri:
    - Hesap flag: fon akışı aksiyonları engellenir, mevcut işlemler devam eder
    - İşlem flag (pre-create): işlem CREATED öncesi durdurulur, timeout başlamaz
    - Admin flag kuyruğu: GET /admin/flags, GET /admin/flags/:id
    - Admin onay: POST /admin/flags/:id/approve → işlem devam
    - Admin red: POST /admin/flags/:id/reject → işlem iptal
    - Yüksek risk durumlarında (sanctions, hesap ele geçirme): aktif işlemlere otomatik EMERGENCY_HOLD
    - AuditLog kaydı tüm flag aksiyonlarında
    - Bildirimler: admin'e flag bildirimi, taraflara sonuç bildirimi
  Test beklentisi: Unit — flag oluşturma kuralları; Integration — flag → admin review → sonuç
  Doğrulama kontrol listesi:
    - [ ] 02 §14.0 flag kategorileri ve etkileri doğru mu?
    - [ ] 07 §9.2–§9.5 endpoint sözleşmeleri doğru mu?
```

```
Task T55: AML kontrolü (fiyat sapması, yüksek hacim)
  Bağımlılık: T54
  Dokümanlar: 02 §14.4, 03 §7.1–§7.2
  Kabul kriterleri:
    - Piyasa fiyatından sapma eşiği kontrolü (işlem oluşturma anında)
    - Eşik aşılırsa → FLAGGED (pre-create), timeout başlamaz
    - Kısa sürede yüksek hacim tespiti → flag
    - Dormant hesap anomali tespiti: hesap yaşı vs işlem hacmi orantısızlığı (hiç işlem yapmayan hesabın aniden yüksek hacimli işlem yapması)
    - Eşikler admin tarafından SystemSetting'den okunur
  Test beklentisi: Unit — sapma hesaplama, hacim kontrolü, dormant hesap anomali; Integration — flag oluşturma
  Doğrulama kontrol listesi:
    - [ ] 02 §14.4 AML kuralları eksiksiz mi?
    - [ ] Dormant hesap anomali tespiti çalışıyor mu?
```

```
Task T56: Çoklu hesap tespiti
  Bağımlılık: T54, T18
  Dokümanlar: 02 §14.3, 03 §7.4
  Kabul kriterleri:
    - Güçlü sinyal: aynı cüzdan adresi birden fazla hesapta → flag
    - Destekleyici sinyal: aynı gönderim adresi (exchange hariç) → tek başına flag değil
    - Destekleyici sinyal: aynı IP/cihaz parmak izi → tek başına flag değil
    - Sinyal kombinasyonu değerlendirmesi
    - Admin'e bildirim
  Test beklentisi: Unit — sinyal eşleştirme mantığı; Integration — flag oluşturma
  Doğrulama kontrol listesi:
    - [ ] 02 §14.3 tüm sinyal türleri uygulanmış mı?
```

```
Task T57: Wash trading kontrolü
  Bağımlılık: T43
  Dokümanlar: 02 §14.1
  Kabul kriterleri:
    - Aynı alıcı-satıcı çifti arasında ardışık işlemler arasında min 1 ay kontrolü
    - Bu süreden kısa → işlem engellenmez, skor etkisi kaldırılır
  Test beklentisi: Unit — 1 ay kuralı, skor etkisi
  Doğrulama kontrol listesi:
    - [ ] 02 §14.1 kuralları birebir mi?
```

```
Task T58: Dispute sistemi
  Bağımlılık: T44, T22, T37
  Dokümanlar: 07 §7.8–§7.10, 02 §10, 03 §6
  Kabul kriterleri:
    - POST /transactions/:id/disputes → dispute açma (sadece alıcı)
    - 3 tür: PAYMENT, DELIVERY, WRONG_ITEM
    - Otomatik doğrulama: blockchain kontrol (ödeme), Steam kontrol (teslim), item karşılaştırma (yanlış item)
    - POST /transactions/:id/disputes/:disputeId/submit-txhash → TX hash ile yeniden doğrulama
    - POST /transactions/:id/disputes/:disputeId/escalate → admin'e iletme
    - Dispute timeout'u durdurmaz
    - Aynı türde tekrar açılamaz, eşzamanlı farklı türler mümkün
    - Rate limiting: işlem başına
  Test beklentisi: Unit — otomatik doğrulama mantığı; Integration — dispute açma → otomatik kontrol → eskalasyon
  Doğrulama kontrol listesi:
    - [ ] 02 §10 dispute kuralları eksiksiz mi?
    - [ ] 07 §7.8–§7.10 sözleşmeleri doğru mu?
```

```
Task T59: Emergency hold
  Bağımlılık: T44, T50, T40
  Dokümanlar: 07 §9.20–§9.22, 02 §7
  Kabul kriterleri:
    - POST /admin/transactions/:id/cancel → admin doğrudan iptal
    - POST /admin/transactions/:id/emergency-hold → hold uygulama
    - POST /admin/transactions/:id/release-hold → hold kaldırma (RESUME veya CANCEL)
    - CANCEL_TRANSACTIONS ve EMERGENCY_HOLD ayrı yetkiler
    - ITEM_DELIVERED hold'unda CANCEL yasak, yalnızca RESUME
    - Timeout durur, akış bekler
    - Tüm aksiyonlar AuditLog'a yazılır
    - Bildirimler: taraflara hold/release bildirimi
  Test beklentisi: Unit — yetki kontrolü, ITEM_DELIVERED kısıtı; Integration — hold → resume/cancel cycle
  Doğrulama kontrol listesi:
    - [ ] 02 §7 emergency hold kuralları eksiksiz mi?
    - [ ] 07 §9.20–§9.22 sözleşmeleri doğru mu?
```

```
Task T60: Satıcı payout issue
  Bağımlılık: T44, T25, T37
  Dokümanlar: 07 §7.11, 02 §10.3, 06 §3.8a
  Kabul kriterleri:
    - POST /transactions/:id/report-payout-issue → sadece COMPLETED işlemler, sadece satıcı
    - Otomatik doğrulama: tx hash ile blockchain kontrolü
    - Retry: gönderim başarısız/stuck ise otomatik yeniden deneme
    - Eskalasyon: otomatik çözüm başarısızsa admin'e
    - SellerPayoutIssue entity state'leri: REPORTED → VERIFYING → RETRY_SCHEDULED / ESCALATED → RESOLVED
  Test beklentisi: Integration — sorun bildirme, otomatik doğrulama, retry, eskalasyon
  Doğrulama kontrol listesi:
    - [ ] 06 §3.8a SellerPayoutIssue yapısı doğru mu?
    - [ ] 07 §7.11 sözleşmesi doğru mu?
```

```
Task T61: SignalR hub — işlem real-time güncellemeler
  Bağımlılık: T44
  Dokümanlar: 07 §11.1
  Kabul kriterleri:
    - /hubs/transactions hub'ı
    - Client→Server: JoinTransaction, LeaveTransaction
    - Server→Client: TransactionStatusChanged, CountdownSync (30sn + freeze/unfreeze), PaymentDetected, PaymentConfirmed, DisputeUpdate, FlagResolved, EmergencyHoldApplied, EmergencyHoldReleased
    - JWT authentication (query param)
    - Grup bazlı mesajlaşma (transaction ID)
  Test beklentisi: Integration — hub bağlantısı, event push
  Doğrulama kontrol listesi:
    - [ ] 07 §11.1 tüm event'ler tanımlı mı?
    - [ ] Auth doğru çalışıyor mu?
```

```
Task T62: SignalR hub — bildirim push
  Bağımlılık: T38
  Dokümanlar: 07 §11.2
  Kabul kriterleri:
    - /hubs/notifications hub'ı
    - T38'den gelen Notification entity'lerini real-time push olarak iletir
    - Server→Client: NewNotification, UnreadCountChanged, TelegramConnected, DiscordConnected, MaintenanceStatusChanged
    - User bazlı mesajlaşma (user ID)
  Test beklentisi: Integration — bildirim push, T38 notification → real-time iletim
  Doğrulama kontrol listesi:
    - [ ] 07 §11.2 tüm event'ler tanımlı mı?
    - [ ] T38 Notification entity'leri real-time push ediliyor mu?
```

```
Task T63: Admin dashboard ve işlem yönetimi API
  Bağımlılık: T40, T42, T19
  Dokümanlar: 07 §9.1, §9.6–§9.7, §9.19, 02 §16
  Kabul kriterleri:
    - GET /admin/dashboard → özet (aktif işlem, flag sayısı, Steam hesap durumu)
    - GET /admin/transactions → tüm işlem listesi (paginated, filtrelenebilir)
    - GET /admin/transactions/:id → işlem tam admin görünümü (status history, payment detail, payout, refund, notification, dispute, flag history)
    - GET /admin/audit-logs → audit log listesi (paginated, filtrelenebilir)
    - GET /admin/users/:steamId/transactions → kullanıcının işlem geçmişi
    - GET /admin/steam-accounts → Steam bot hesapları durumu
  Test beklentisi: Integration — dashboard veri, işlem listesi/filtre, audit log
  Doğrulama kontrol listesi:
    - [ ] 07 §9.1–§9.19 admin endpoint'leri eksiksiz mi?
```

```
Task T63a: Platform public endpoint'leri (backend)
  Bağımlılık: T04
  Dokümanlar: 07 §10.1–§10.2
  Kabul kriterleri:
    - GET /platform/stats → platform istatistikleri (tamamlanan işlem sayısı, toplam hacim vb.), 15dk cache
    - GET /platform/maintenance → bakım durumu (aktif/pasif, mesaj, tahmini bitiş)
    - Anonim erişim (auth gerekmez)
  Test beklentisi: Integration — stats endpoint doğru veri döndürüyor mu, maintenance durumu doğru mu, cache çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 07 §10.1–§10.2 endpoint sözleşmeleri doğru mu?
    - [ ] Cache mekanizması çalışıyor mu?
```

```
Task T63b: Retention job'ları (toplu temizlik)
  Bağımlılık: T09, T25, T23
  Dokümanlar: 06 §8.2, §3.18, §3.19, §3.20
  Kabul kriterleri:
    - Hangfire recurring job: OutboxMessage + ProcessedEvent + ExternalIdempotencyRecord — 30 gün sonra toplu hard delete (silme sırası: önce ProcessedEvent, sonra OutboxMessage)
    - Hangfire recurring job: Bağımsız bildirimler (Notification, TransactionId = NULL) + ilgili NotificationDelivery kayıtları — retention süresi sonrası toplu purge (önce delivery, sonra notification)
    - Soft-deleted entity'ler için retention-based hard purge (06 §8.2 lifecycle'a uygun)
    - Retention süreleri SystemSetting'den okunur (admin tarafından ayarlanabilir)
    - Batch büyüklüğü sınırlandırılmış (DB yükü kontrolü)
  Test beklentisi: Integration — retention süresi dolmuş kayıtlar temizleniyor mu, silme sırası doğru mu, batch limit çalışıyor mu
  Doğrulama kontrol listesi:
    - [ ] 06 §8.2 retention kuralları eksiksiz uygulanmış mı?
    - [ ] Silme sırası FK-safe mi (ProcessedEvent → OutboxMessage)?
    - [ ] Bağımsız bildirim retention ayrımı doğru mu?
```

---

### F4 — Entegrasyonlar (T64–T83)

```
Task T64: Steam Sidecar — bot session yönetimi
  Bağımlılık: T14
  Dokümanlar: 08 §2.4–§2.5, §2.7, 05 §3.2
  Kabul kriterleri:
    - Bot login: username, password, shared_secret ile oturum açma
    - Session expire tespiti ve otomatik re-login
    - Health check: 60sn periyodik Steam bot session kontrolü
    - Failover: session başarısız → cookie yenileme → re-login → bot havuzdan çıkarma → admin alert
    - steam-totp ile mobile confirmation otomatik onayı
  Test beklentisi: Unit — session state yönetimi; Contract — sidecar ↔ backend sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §2.7 hata yönetimi zinciri doğru mu?
    - [ ] Bot health check periyodu ve logic doğru mu?
```

```
Task T65: Steam Sidecar — trade offer gönderme
  Bağımlılık: T64
  Dokümanlar: 08 §2.4, 05 §3.2
  Kabul kriterleri:
    - Trade offer gönderme (satıcıya item emanet, alıcıya item teslim, satıcıya iade)
    - steam-tradeoffer-manager ile offer oluşturma ve gönderme
    - Mobile confirmation otomatik onayı
    - Retry: exponential backoff (5s, 15s, 45s), timeout süresi içinde
    - Counter offer handling: desteklenmiyor, orijinal offer iptal
    - Webhook callback: trade offer durumu backend'e bildirilir
  Test beklentisi: Contract — offer gönderim/durum callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §2.4 trade offer durum yönetimi eksiksiz mi?
```

```
Task T66: Steam Sidecar — trade offer durum izleme
  Bağımlılık: T65
  Dokümanlar: 08 §2.4, §2.7
  Kabul kriterleri:
    - 10sn aralıkla polling (steam-tradeoffer-manager built-in)
    - Durum değişikliğinde webhook callback: Accepted, Declined, Expired, Countered, InvalidItems
    - InvalidItems → kullanıcıya bilgi, işlem iptal
    - FAILED durumu: retry geçerli
  Test beklentisi: Contract — durum callback payload sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §2.4 tüm offer durumları ele alınmış mı?
    - [ ] 08 §2.7 hata senaryoları karşılanmış mı?
```

```
Task T67: Steam Sidecar — envanter okuma
  Bağımlılık: T64
  Dokümanlar: 08 §2.3, 07 §6.1
  Kabul kriterleri:
    - Steam Community envanter endpoint: inventory/{steamId}/730/2
    - Pagination desteği (5000+ item, start_assetid/more_items)
    - Assets + descriptions merge (classid + instanceid join)
    - Redis cache: 2dk TTL, işlem sonrası invalidation
    - API endpoint: GET /steam/inventory (backend → sidecar HTTP çağrısı)
    - Private envanter tespiti → kullanıcıya uyarı
  Test beklentisi: Contract — envanter response sözleşmesi; Integration (backend) — API endpoint
  Doğrulama kontrol listesi:
    - [ ] 08 §2.3 pagination ve merge kuralları doğru mu?
    - [ ] 07 §6.1 endpoint sözleşmesi doğru mu?
```

```
Task T68: Steam Sidecar — webhook callback ve backend entegrasyonu
  Bağımlılık: T65, T66, T05
  Dokümanlar: 05 §3.4, 09 §11.3, §17.5
  Kabul kriterleri:
    - Sidecar → Backend webhook: HMAC-SHA256 imzalama, timestamp, nonce, signature header
    - Backend webhook handler: WebhookSignatureMiddleware ile doğrulama
    - Replay koruması: timestamp ±5dk, nonce tekrar kontrolü (ProcessedNonce)
    - Trade offer durum güncellemelerini backend'de işleme → state machine tetikleme
    - Idempotent işleme
  Test beklentisi: Integration — webhook doğrulama, durum güncellemesi → state geçişi
  Doğrulama kontrol listesi:
    - [ ] 05 §3.4 güvenlik kuralları eksiksiz mi?
    - [ ] Replay koruması çalışıyor mu?
```

```
Task T69: Steam Sidecar — bot failover ve capacity-based seçim
  Bağımlılık: T64, T21
  Dokümanlar: 05 §3.2, 02 §15
  Kabul kriterleri:
    - Capacity-based bot seçimi: en az emanet item olan aktif bot
    - Kısıtlı bot tespiti: yeni işlemler diğer botlara yönlendirme
    - Kısıtlı botta emanet item'lar: recovery/manual intervention akışı
    - Admin bildirim: bot kısıtlandı uyarısı
  Test beklentisi: Unit — bot seçim algoritması; Integration — failover senaryosu
  Doğrulama kontrol listesi:
    - [ ] 02 §15 bot yönetimi kuralları doğru mu?
```

```
Task T70: Blockchain Sidecar — HD wallet adres üretimi
  Bağımlılık: T15
  Dokümanlar: 08 §3.2, 05 §3.3
  Kabul kriterleri:
    - BIP-44 derivation path: m/44'/195'/0'/0/{index}
    - Backend → sidecar HTTP çağrısı ile adres üretimi
    - Index artırma, DB kayıt (PaymentAddress), UNIQUE constraint
    - Master seed güvenliği: vault/secrets (prod), env var (dev)
    - Private key sadece imzalama anında memory'ye yüklenir, sonra temizlenir
  Test beklentisi: Unit — derivation path doğru mu; Contract — adres üretim sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §3.2 HD wallet kuralları eksiksiz mi?
    - [ ] 06 §5.1 PaymentAddress.HdWalletIndex UNIQUE mi?
```

```
Task T71: Blockchain Sidecar — ödeme izleme
  Bağımlılık: T70
  Dokümanlar: 08 §3.4, 05 §3.3
  Kabul kriterleri:
    - 3sn polling aralığı ile deposit adresi izleme
    - Aşama 1: beklenen token sorgusu (contract_address filtreli, only_confirmed, fingerprint pagination)
    - Aşama 2: yanlış token taraması (filtresiz, tüm TRC-20)
    - Kayıt türü filtresi: yalnızca Transfer türü (Authorization/Approval/TRC-721 skip)
    - 20 blok minimum onay (finality: currentSolidBlock - txBlock >= 20)
    - İdempotent işleme: txid + event_index bileşik anahtar
    - Wrong-token: allowlist'te → iade, spam → ignore + log
    - Backend'e webhook callback: PaymentDetected, PaymentConfirmed
  Test beklentisi: Unit — finality hesaplama, wrong-token logic; Contract — izleme callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §3.4 tüm izleme kuralları uygulanmış mı?
    - [ ] Finality hesaplaması doğru mu?
```

```
Task T72: Blockchain Sidecar — tutar doğrulama ve edge case'ler
  Bağımlılık: T71
  Dokümanlar: 08 §3.4, 02 §4.4
  Kabul kriterleri:
    - Doğru tutar → PAYMENT_RECEIVED
    - Eksik tutar → iade + bildirim
    - Fazla tutar → doğru tutarı kabul, fazlayı iade + bildirim
    - Yanlış token (desteklenen TRC-20) → iade + bildirim
    - Desteklenmeyen token → admin review
    - Çoklu/parçalı ödeme → birleştirmez, ilk doğru kabul, sonraki iade
    - Minimum iade eşiği: tutar < 2× gas fee → iade yapılmaz, admin alert
    - İade kaynak adrese gönderilir (source address parse)
  Test beklentisi: Unit — her edge case senaryosu; Contract — callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 02 §4.4 tüm edge case'ler karşılanmış mı?
```

```
Task T73: Blockchain Sidecar — TRC-20 transfer (payout, refund, sweep)
  Bağımlılık: T70
  Dokümanlar: 08 §3.1, §3.3, 05 §3.3
  Kabul kriterleri:
    - Satıcıya payout: TRC-20 transfer, retry 3 deneme (1dk, 5dk, 15dk), başarısızlıkta admin alert
    - Alıcıya refund: TRC-20 transfer, retry 3 deneme
    - Sweep: deposit → hot wallet, sweep sonrası delegation geri alımı
    - Sweep hata yönetimi: retry + fallback (deposit'ten doğrudan gönderim)
    - Transaction broadcasting: broadcasttransaction endpoint
    - Onay takibi: gettransactioninfobyid ile doğrulama
  Test beklentisi: Contract — transfer callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §3.1 TronGrid API çağrıları doğru mu?
    - [ ] Retry stratejisi doğru mu?
```

```
Task T74: Blockchain Sidecar — energy delegation
  Bağımlılık: T73
  Dokümanlar: 08 §3.3
  Kabul kriterleri:
    - Sweep öncesi deposit adresine geçici Energy delegation
    - delegateresource çağrısı
    - Sweep sonrası undelegateresource ile geri alım
    - Fallback: delegation başarısızsa deposit adresine minimum TRX transfer
  Test beklentisi: Contract — delegation callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 08 §3.3 energy delegation akışı doğru mu?
```

```
Task T75: Blockchain Sidecar — gecikmeli ödeme izleme
  Bağımlılık: T71
  Dokümanlar: 08 §3.4, 02 §4.4
  Kabul kriterleri:
    - İptal sonrası kademeli polling: 30s → 5dk → 1sa → durdur (MonitoringStatus: POST_CANCEL_24H → 7D → 30D → STOPPED)
    - Gecikmeli ödeme tespit edilirse → alıcının iade adresine otomatik iade
    - Gas fee düşülür
  Test beklentisi: Unit — polling aralığı geçişleri; Contract — callback sözleşmesi
  Doğrulama kontrol listesi:
    - [ ] 06 §2.16 MonitoringStatus değerleri doğru mu?
    - [ ] 02 §4.4 gecikmeli ödeme kuralları eksiksiz mi?
```

```
Task T76: Blockchain Sidecar — reconciliation job
  Bağımlılık: T73
  Dokümanlar: 05 §3.3
  Kabul kriterleri:
    - Günlük reconciliation: on-chain bakiye vs platform ledger karşılaştırma
    - Uyumsuzluk tespit edilirse admin alert
  Test beklentisi: Yok (operasyonel job)
  Doğrulama kontrol listesi:
    - [ ] 05 §3.3 reconciliation kuralları doğru mu?
```

```
Task T77: Blockchain Sidecar — hot wallet yönetimi
  Bağımlılık: T73, T25
  Dokümanlar: 05 §3.3, 06 §3.22
  Kabul kriterleri:
    - Hot wallet bakiye monitoring (TRX + USDT + USDC)
    - Limit aşımında admin alert (cold wallet transferi MVP'de admin tarafından manuel başlatılır — 05 §3.3)
    - Manuel cold wallet transferi sonrası ColdWalletTransfer ledger kaydı (DB'de, tx hash + tutar + tarih)
    - Hot wallet TRX bakiyesi eşik altında → admin alert
  Test beklentisi: Unit — limit kontrolü, alert tetikleme; Integration — manuel transfer sonrası ledger kaydı + reconciliation eşleşmesi
  Doğrulama kontrol listesi:
    - [ ] 06 §3.22 ColdWalletTransfer yapısı doğru mu?
```

```
Task T78: Email entegrasyonu (Resend)
  Bağımlılık: T37
  Dokümanlar: 08 §4.1–§4.3
  Kabul kriterleri:
    - IEmailSender interface + Resend implementasyonu
    - POST /emails çağrısı (Authorization: Bearer)
    - Email şablonları: .resx ile 4 dil, kanal bazlı format (işlem, güvenlik, hesap, timeout)
    - Retry: 5xx → 3 deneme (1dk, 5dk, 15dk), 422 → retry yok
    - Deferred: geçici hata → DEFERRED state, arka plan job (30dk, 1sa, 4sa)
    - Resend webhook handler: bounced, delivery_delayed, complained, failed, suppressed
    - Webhook güvenlik: Svix header doğrulama, replay koruması (5dk), idempotency (svix-id)
    - DNS: DKIM, SPF, DMARC, Return-Path
  Test beklentisi: Integration — email gönderim (sandbox), webhook handler
  Doğrulama kontrol listesi:
    - [ ] 08 §4.1–§4.3 tüm webhook event'leri ele alınmış mı?
    - [ ] Güvenlik kuralları eksiksiz mi?
```

```
Task T79: Telegram entegrasyonu
  Bağımlılık: T37, T35
  Dokümanlar: 08 §5.1–§5.5
  Kabul kriterleri:
    - Telegram Bot: BotFather ile oluşturma, token alma
    - Deep Link bağlantı: benzersiz kod (10dk TTL, single-use, 122+ bit entropy), /start ile eşleşme, chat_id kayıt
    - Webhook: POST /webhooks/telegram, secret_token doğrulaması
    - Webhook idempotency: update_id ile duplicate filtreleme (Redis, 24sa TTL)
    - sendMessage: MarkdownV2 format, escape helper
    - Rate limit: chat başına 1 msg/s, farklı chat'ler 30 msg/s, sıralı kuyruk
    - Hata yönetimi: 429 → retry_after bekle, 403 neden ayrıştırma (blocked/deactivated/can't send/can't initiate), 400 → bağlantı kopmuş, 5xx → 3 deneme
    - setWebhook: url, secret_token, max_connections=40, allowed_updates=["message"]
  Test beklentisi: Integration — bağlantı akışı, mesaj gönderimi, webhook handler
  Doğrulama kontrol listesi:
    - [ ] 08 §5.1–§5.5 tüm entegrasyon detayları uygulanmış mı?
    - [ ] 403 neden ayrıştırma doğru mu?
```

```
Task T80: Discord entegrasyonu
  Bağımlılık: T37, T35
  Dokümanlar: 08 §6.1–§6.5
  Kabul kriterleri:
    - Discord Bot: Developer Portal, OAuth2 scope: identify
    - MVP Guild Install: Skinora sunucusu, bot invite
    - OAuth2 bağlantı: identify scope, callback, discord_user_id kayıt
    - State parametresi: server-side session correlation (CSRF koruması)
    - DM kanal: POST /users/@me/channels → POST /channels/{id}/messages
    - Mention koruması: allowed_mentions: { "parse": [] }
    - Rate limit: header-driven (X-RateLimit-*), kuyruk + throttle
    - Hata yönetimi: 401 → admin alert, 403 → DM kapalı/mutual guild yok, 404 → kanal devre dışı, 5xx → 3 deneme
    - DM channel ID cache: Redis
  Test beklentisi: Integration — OAuth2 akışı, DM gönderimi
  Doğrulama kontrol listesi:
    - [ ] 08 §6.1–§6.5 tüm entegrasyon detayları uygulanmış mı?
```

```
Task T81: Steam Market fiyat API
  Bağımlılık: T67
  Dokümanlar: 08 §7.1–§7.4
  Kabul kriterleri:
    - Steam Market priceoverview çağrısı (public, auth yok)
    - Fiyat parse: median_price → lowest_price → no-price (kontrol atla)
    - Currency sembolü strip, binlik ayracı kaldır, nokta ondalık
    - Cache: SQL Server ItemPriceCache, 24s fresh / 48s stale / 48+ expired
    - On-demand fetch: cache kontrol → stale ise arka plan yenileme → expired ise API çağrısı
    - IPriceService interface ile abstraction
    - Rate limit: ~20 req/dk, bekleme + cache kullan
    - Erişilemez → cache ≤48s kullan, yoksa kontrol atla + log
  Test beklentisi: Unit — fiyat parse, cache logic; Integration — API çağrısı + cache
  Doğrulama kontrol listesi:
    - [ ] 08 §7.1–§7.4 tüm kurallar uygulanmış mı?
```

```
Task T82: Sanctions screening servisi
  Bağımlılık: T34
  Dokümanlar: 02 §21.1, §12.3, 03 §11a.3
  Kabul kriterleri:
    - Cüzdan adresi yaptırımlı adres listesiyle karşılaştırma
    - Eşleşme: yeni işlem/adres kaydı engellenir, hesap flag'lenir
    - Yüksek risk: aktif işlemlere otomatik EMERGENCY_HOLD
    - Tarama listesi admin tarafından güncellenebilir
    - Merkezi doğrulama pipeline'ın parçası
  Test beklentisi: Unit — adres eşleştirme; Integration — flag oluşturma + hold tetikleme
  Doğrulama kontrol listesi:
    - [ ] 02 §21.1 sanctions kuralları eksiksiz mi?
```

```
Task T83: Geo-block servisi
  Bağımlılık: T30
  Dokümanlar: 02 §21.1, 03 §11a.1
  Kabul kriterleri:
    - IP adresinden coğrafi konum tespiti
    - Yasaklı bölge → bilgilendirme sayfası, erişim engeli
    - Yasaklı ülke listesi admin tarafından yönetilebilir
    - VPN/proxy tespiti destekleyici sinyal (tek başına engelleme değil)
  Test beklentisi: Unit — IP → ülke eşleşme; Integration — engelleme akışı
  Doğrulama kontrol listesi:
    - [ ] 02 §21.1 geo-block kuralları eksiksiz mi?
```

---

### F5 — Kullanıcı Arayüzü (T84–T106)

```
Task T84: Ortak UI bileşenleri (C01–C17)
  Bağımlılık: T13
  Dokümanlar: 04 §5
  Kabul kriterleri:
    - C01 Status Badge: 14 durum, renk kodlu
    - C02 Countdown Timer: gerçek zamanlı, renk geçişli, frozen state
    - C03 Item Card: Compact / Detailed / Selectable
    - C04 User Card: Compact / Detailed
    - C05 Transaction Timeline: 8 adımlı ilerleme çubuğu
    - C06 Cancel Modal: sebep textarea, iade bilgisi, onay
    - C07 Dispute Form: 3 adımlı
    - C08 Maintenance Banner: 4 varyant
    - C09 Toast Notification: bilgi/başarı/uyarı/hata
    - C10 Language Selector: 4 dil
    - C11 Wallet Address Input: TRC-20 validation + sanctions + onay
    - C12 Copy Button
    - C13 Empty State
    - C14 Loading State: Skeleton/Spinner/Progress
    - C15 Error State
    - C16 Pagination
    - C17 Filter Bar
  Test beklentisi: Yok (görsel bileşenler — E2E'de test edilecek)
  Doğrulama kontrol listesi:
    - [ ] 04 §5'teki tüm bileşenler ve varyantları var mı?
```

```
Task T85: Global layout (header, navigation, footer)
  Bağımlılık: T84
  Dokümanlar: 04 §7.1 (header), §8.1 (admin header/menü)
  Kabul kriterleri:
    - Kullanıcı header: logo, bildirim, profil, dil, ayarlar
    - Suspended header: logo, dil, destek, çıkış (kısıtlı)
    - Admin header: logo, admin adı, çıkış
    - Admin sol menü: dashboard, flag'ler, işlemler, ayarlar, steam hesapları, roller, kullanıcılar, audit log
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.1 ve §8.1 layout tanımları doğru mu?
```

```
Task T86: Landing page (S01)
  Bağımlılık: T85
  Dokümanlar: 04 §6.1, 07 §10.1–§10.2
  Kabul kriterleri:
    - Hero section, "Nasıl Çalışır" bölümü, güven göstergeleri, footer
    - GET /platform/stats çağrısı (15dk cache)
    - GET /platform/maintenance → bakım durumu gösterimi
    - Bakım state: C08 banner aktif, CTA devre dışı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §6.1 tüm bölümler var mı?
```

```
Task T87: Auth akışı ekranları
  Bağımlılık: T85
  Dokümanlar: 04 §6.2–§6.7
  Kabul kriterleri:
    - S02 Steam Login: pre-redirect loading, callback loading, auth başarısız
    - S03 MA Uyarısı: adım adım talimat, kontrol et butonu
    - S03a Geo-Block: bilgilendirme sayfası
    - S03b Yaş Gate: 18+ onay
    - S03c Sanctions Uyarı
    - S03d Hesap Askıya Alındı: kısıtlı oturum
    - ToS Modal: 18+ checkbox + ToS checkbox
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §6.2–§6.7 tüm ekranlar ve state'ler var mı?
```

```
Task T88: Dashboard (S05)
  Bağımlılık: T85, T84
  Dokümanlar: 04 §7.1
  Kabul kriterleri:
    - İşlem listesi: tab yapısı (Aktif/Tamamlanan/İptal), satır: ID, item, status badge, fiyat, karşı taraf, tarih, countdown
    - Hızlı istatistik kartları: işlem sayısı, başarı oranı, skor
    - State'ler: yeni kullanıcı (empty), aktif işlem var, yükleniyor (skeleton), hata, suspended session
    - GET /transactions, GET /users/me/stats çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.1 tüm state'ler var mı?
```

```
Task T89: İşlem oluşturma (S06)
  Bağımlılık: T84
  Dokümanlar: 04 §7.2
  Kabul kriterleri:
    - 4 adımlı form: Adım 1 (item seçimi), Adım 2 (detaylar), Adım 3 (alıcı + cüzdan), Adım 4 (özet)
    - Adım göstergesi (step indicator)
    - Envanter grid: arama/filtre, skeleton loading, boş/hata state
    - Validasyonlar: fiyat min/max, timeout aralığı, Steam ID format, non-tradeable engel, payout adresi zorunlu
    - Engel state'leri: concurrent limit, cooldown, yeni hesap limiti, MA pasif, flag aktif, address cooldown
    - GET /transactions/eligibility, /params, /steam/inventory çağrıları
    - POST /transactions çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.2 tüm adımlar, validasyonlar ve engel state'leri var mı?
```

```
Task T90: İşlem detay sayfası (S07) — tüm state varyantları
  Bağımlılık: T84, T96
  Dokümanlar: 04 §7.3
  Kabul kriterleri:
    - State × role varyantları: CREATED (satıcı/alıcı/public), ACCEPTED, TRADE_OFFER_SENT_TO_SELLER, ITEM_ESCROWED, PAYMENT_RECEIVED, TRADE_OFFER_SENT_TO_BUYER, ITEM_DELIVERED, COMPLETED, CANCELLED_*, FLAGGED, EMERGENCY_HOLD
    - Her state'te satıcı ve alıcı görünümü farklı
    - Suspended session override
    - Ödeme edge case banner'ları: eksik/fazla/yanlış token/gecikmeli ödeme
    - Dispute aktif gösterimi
    - İptal bilgileri (sebep, tür, iade özeti)
    - GET /transactions/:id çağrısı
    - SignalR real-time güncellemeler (T96 ile bağlantılı)
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.3 tüm state × role varyantları var mı?
    - [ ] 07 §7.5 TransactionDetailResponse tüm alanları ekrana yansıtılmış mı?
```

```
Task T91: Ödeme bilgileri ve edge case UI
  Bağımlılık: T90
  Dokümanlar: 04 §7.3 (ödeme section)
  Kabul kriterleri:
    - Ödeme bilgileri bölümü: adres, tutar, token, ağ, exchange uyarısı
    - Copy button (adres kopyalama)
    - Ödeme özeti: fiyat, gas fee, net ödeme, tx hash
    - Edge case banner'lar: eksik tutar uyarı, fazla tutar bilgi, yanlış token uyarı, gecikmeli ödeme iade bilgisi
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] Tüm ödeme edge case'leri UI'da gösterilmiş mi?
```

```
Task T92: Dispute UI
  Bağımlılık: T90
  Dokümanlar: 04 §7.3 (dispute section), §5 (C07)
  Kabul kriterleri:
    - C07 Dispute Form: 3 adımlı (tür seçimi → otomatik kontrol → eskalasyon)
    - Dispute tür seçimi: ödeme, teslim, yanlış item
    - Otomatik kontrol sonucu gösterimi
    - TX hash girme imkanı (ödeme dispute)
    - Admin'e iletme butonu + detay textarea
    - Dispute durumu gösterimi
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] C07 3 adım doğru mu?
```

```
Task T93: Profil sayfaları (S08, S09)
  Bağımlılık: T85
  Dokümanlar: 04 §7.4–§7.5
  Kabul kriterleri:
    - S08 Kendi profil: avatar, ad, Steam ID, skor, istatistikler, cüzdan adresleri (C11 ile yönetim)
    - S09 Public profil: sınırlı bilgi (avatar, ad, skor, işlem sayısı, hesap yaşı)
    - Cüzdan adresi değişikliği: Steam re-auth akışı tetikleme
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.4–§7.5 tüm alanlar var mı?
```

```
Task T94: Hesap ayarları (S10)
  Bağımlılık: T85
  Dokümanlar: 04 §7.6
  Kabul kriterleri:
    - Bildirim tercihleri: platform içi, email (toggle+input), Telegram (toggle + bağlama akışı), Discord (toggle + OAuth)
    - Dil tercihi (dropdown)
    - Telegram bağlama: doğrulama kodu + bot link
    - Discord bağlama: Discord OAuth
    - Hesabı deaktif et / sil modal'ları
    - Hesap sil: "SİL" yazarak onay
    - Aktif işlem kontrolü (deaktif/sil engeli)
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.6 tüm ayarlar ve modal'lar var mı?
```

```
Task T95: Bildirimler sayfası (S11)
  Bağımlılık: T85
  Dokümanlar: 04 §7.7
  Kabul kriterleri:
    - Bildirim listesi: okunmamış vurgusu, ikon, metin, zaman, tıklanabilir
    - "Tüm bildirimleri okundu işaretle" linki
    - State'ler: yok (empty), yeni bildirimler, yükleniyor
    - Pagination
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §7.7 tüm state'ler var mı?
```

```
Task T96: SignalR client entegrasyonu
  Bağımlılık: T13, T61, T62
  Dokümanlar: 07 §11.1–§11.2, 04 §7.3 (countdown)
  Kabul kriterleri:
    - Transaction hub bağlantısı: join/leave room, event listener'lar
    - Notification hub bağlantısı: real-time bildirim push
    - CountdownSync: 30sn periyodik + freeze/unfreeze
    - PaymentDetected/PaymentConfirmed → UI güncelleme
    - TransactionStatusChanged → state varyantı değişimi
    - MaintenanceStatusChanged → banner gösterimi
    - JWT authentication (query param)
    - Bağlantı kopma/yeniden bağlanma
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 07 §11.1–§11.2 tüm event'ler client'ta dinleniyor mu?
```

```
Task T97: i18n (4 dil desteği)
  Bağımlılık: T13
  Dokümanlar: 04 §10
  Kabul kriterleri:
    - next-intl ile 4 dil: EN, 中文, ES, TR
    - Tarih/saat formatı dil bazlı
    - Sayı formatı dil bazlı (stablecoin hariç)
    - Çevrilmeyecek terimler listesi (USDT, Steam ID, Trade offer vb.)
    - Metin uzunluk esnekliği (EN 1.5x'e kadar)
    - Tüm ekranlarda dil desteği
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §10 tüm lokalizasyon kuralları uygulanmış mı?
```

```
Task T98: Responsive tasarım
  Bağımlılık: T84–T97
  Dokümanlar: 04 §9
  Kabul kriterleri:
    - 3 breakpoint: Desktop ≥1024, Tablet 768-1023, Mobil <768
    - Dashboard responsive: 3 layout
    - İşlem oluşturma: merkezi form → tam genişlik
    - İşlem detay: 2 kolon → tek kolon
    - Admin: sol menü → hamburger menü
    - Tablo → kart dönüşümü (mobilde)
    - Timeline yatay → dikey (mobilde)
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §9 tüm responsive kuralları uygulanmış mı?
```

```
Task T99: Admin Dashboard (S12)
  Bağımlılık: T85
  Dokümanlar: 04 §8.1
  Kabul kriterleri:
    - Özet kartları: aktif işlemler, bekleyen flag'ler, günlük/haftalık tamamlanan
    - Son flag'lenmiş işlemler tablosu (son 5)
    - Steam hesapları durum kartları
    - Kısıtlı/banned bot uyarısı
    - GET /admin/dashboard çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.1 tüm bileşenler var mı?
```

```
Task T100: Admin Flag kuyruğu + detay (S13, S14)
  Bağımlılık: T85
  Dokümanlar: 04 §8.2–§8.3
  Kabul kriterleri:
    - S13 Flag kuyruğu: filtreleme (kategori, tür, durum, tarih), liste
    - S14 Flag detay: işlem flag varyantı (fiyat sapması, yüksek hacim) + hesap flag varyantı
    - Admin notu textarea, "devam ettir" / "iptal et" butonları
    - Onay modal'ı
    - GET /admin/flags, GET /admin/flags/:id, POST approve/reject çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.2–§8.3 tüm varyantlar ve aksiyonlar var mı?
```

```
Task T101: Admin İşlem listesi + detay (S15, S16)
  Bağımlılık: T85
  Dokümanlar: 04 §8.4–§8.5
  Kabul kriterleri:
    - S15 İşlem listesi: filtre (durum, tarih, kullanıcı, tutar, stablecoin), sayfalama
    - S16 İşlem detay (admin): durum geçmişi timeline, ödeme/payout/refund detayları, admin aksiyonlar (iptal, hold)
    - Admin iptal modal'ı, emergency hold modal'ı
    - GET /admin/transactions, GET /admin/transactions/:id çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.4–§8.5 tüm bileşenler ve aksiyonlar var mı?
```

```
Task T102: Admin Parametre yönetimi (S17)
  Bağımlılık: T85
  Dokümanlar: 04 §8.6
  Kabul kriterleri:
    - Parametre grupları: timeout, komisyon, işlem limitleri, iptal kuralları, yeni hesap, gas fee, fraud, alıcı belirleme, erişim/uyumluluk, blockchain health
    - Inline edit: düzenle → kaydet/iptal
    - Etki kapsamı bilgi kutusu (yeni işlem vs. runtime)
    - GET /admin/settings, PUT /admin/settings/:key çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.6 tüm parametre grupları var mı?
```

```
Task T103: Admin Steam hesapları (S18)
  Bağımlılık: T85
  Dokümanlar: 04 §8.7
  Kabul kriterleri:
    - Hesap kartları: Steam ID, durum (aktif/kısıtlı/banned), emanet sayısı, günlük trade, son kontrol
    - State'ler: aktif (yeşil), kısıtlı (turuncu + banner + emanet listesi), banned (kırmızı + acil uyarı)
    - Recovery queue: işlem ID, item, taraflar, state, recovery durumu, sorumlu admin, not
    - GET /admin/steam-accounts çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.7 tüm state'ler ve recovery queue var mı?
```

```
Task T104: Admin Rol & yetki yönetimi (S19)
  Bağımlılık: T85
  Dokümanlar: 04 §8.8
  Kabul kriterleri:
    - Roller listesi tablosu: ad, açıklama, atanmış kullanıcı, aksiyonlar
    - Yetki matrisi: 11 yetki checkbox listesi
    - Yeni rol oluştur modal'ı
    - Kullanıcı-rol atama (dropdown)
    - GET /admin/roles, POST/PUT/DELETE roles çağrıları
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.8 tüm bileşenler var mı?
```

```
Task T105: Admin Kullanıcı detay (S20)
  Bağımlılık: T85
  Dokümanlar: 04 §8.9
  Kabul kriterleri:
    - Profil bilgileri: avatar, ad, Steam ID, hesap yaşı, durum badge'leri
    - İstatistikler kartı: toplam işlem, başarı/iptal/flag sayıları, hacim, son işlem
    - Cüzdan adresi geçmişi (mevcut + önceki, tarihlerle)
    - Alıcı-satıcı ilişkileri tablosu
    - GET /admin/users/:steamId çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.9 tüm bileşenler var mı?
```

```
Task T106: Admin Audit log (S21)
  Bağımlılık: T85
  Dokümanlar: 04 §8.10
  Kabul kriterleri:
    - Filtre formu: kategori, tarih, kullanıcı, işlem ID
    - Log tablosu: kategori, aksiyon, aktör, konu, işlem ID, detay, tarih
    - State'ler: log var, filtre sonucu boş, yükleniyor
    - GET /admin/audit-logs çağrısı
  Test beklentisi: Yok
  Doğrulama kontrol listesi:
    - [ ] 04 §8.10 tüm bileşenler ve state'ler var mı?
```

---

### F6 — Uçtan Uca Doğrulama (T107–T114)

```
Task T107: E2E — Happy path (tam escrow akışı)
  Bağımlılık: F5 tamamlanmış
  Dokümanlar: 03 §2–§3
  Kabul kriterleri:
    - Satıcı giriş → işlem oluştur → alıcı kabul → item emanet → ödeme → doğrulama → teslim → payout → COMPLETED
    - Tüm bildirimler doğru tetikleniyor
    - Tüm state geçişleri UI'da doğru gösteriliyor
  Test beklentisi: E2E — Playwright (staging)
  Doğrulama kontrol listesi:
    - [ ] 03 §2–§3 tüm happy path adımları çalışıyor mu?
```

```
Task T108: E2E — İptal senaryoları
  Bağımlılık: T107
  Dokümanlar: 03 §2.5, §3.3
  Kabul kriterleri:
    - Satıcı iptali (ödeme öncesi)
    - Alıcı iptali (ödeme öncesi)
    - Admin iptali
    - Her senaryoda doğru iade + bildirim
  Test beklentisi: E2E
  Doğrulama kontrol listesi:
    - [ ] Her iptal senaryosunda iade doğru mu?
```

```
Task T109: E2E — Timeout senaryoları
  Bağımlılık: T107
  Dokümanlar: 03 §4
  Kabul kriterleri:
    - Kabul timeout, trade offer timeout, ödeme timeout, teslim timeout
    - Her senaryoda doğru iade tetikleme + bildirim
    - Gecikmeli ödeme izleme başlatma (ödeme timeout sonrası)
  Test beklentisi: E2E (kısa timeout ile)
  Doğrulama kontrol listesi:
    - [ ] 03 §4 tüm timeout senaryoları çalışıyor mu?
```

```
Task T110: E2E — Ödeme edge case'ler
  Bağımlılık: T107
  Dokümanlar: 03 §5
  Kabul kriterleri:
    - Eksik tutar → iade
    - Fazla tutar → kabul + fazla iade
    - Yanlış token → iade
    - Gecikmeli ödeme → iade
    - Çoklu ödeme → ilk kabul, sonraki iade
  Test beklentisi: E2E (testnet)
  Doğrulama kontrol listesi:
    - [ ] 03 §5 tüm edge case'ler çalışıyor mu?
```

```
Task T111: E2E — Fraud/flag senaryoları
  Bağımlılık: T107
  Dokümanlar: 03 §7–§8
  Kabul kriterleri:
    - Fiyat sapması → flag → admin onay/red
    - Yüksek hacim → flag
    - Hesap flag'i → fon akışı engeli
  Test beklentisi: E2E
  Doğrulama kontrol listesi:
    - [ ] Flag akışı uçtan uca çalışıyor mu?
```

```
Task T112: E2E — Emergency hold
  Bağımlılık: T107
  Dokümanlar: 03 §8.8
  Kabul kriterleri:
    - Hold uygulama → timeout durur → resume → devam
    - Hold uygulama → cancel (ITEM_DELIVERED hariç)
    - ITEM_DELIVERED'da hold → sadece resume
  Test beklentisi: E2E
  Doğrulama kontrol listesi:
    - [ ] Hold/resume/cancel akışları doğru mu?
```

```
Task T113: E2E — Admin akışları
  Bağımlılık: T107
  Dokümanlar: 03 §8
  Kabul kriterleri:
    - Admin giriş ve dashboard
    - Flag inceleme ve onay/red
    - İşlem listesi ve detay
    - Parametre değişikliği
    - Rol yönetimi
    - Audit log görüntüleme
  Test beklentisi: E2E
  Doğrulama kontrol listesi:
    - [ ] Admin paneli tüm akışları çalışıyor mu?
```

```
Task T114: E2E — Downtime ve bakım senaryoları
  Bağımlılık: T107
  Dokümanlar: 03 §11
  Kabul kriterleri:
    - Platform bakımı: timeout dondurma, bakım banner, resume sonrası devam
    - Steam kesintisi: timeout dondurma, bildirim
    - Blockchain degradasyonu: ödeme timeout dondurma
  Test beklentisi: E2E (simüle)
  Doğrulama kontrol listesi:
    - [ ] Downtime senaryolarında freeze/resume doğru mu?
```

---

## 6. Faz Geçiş Kapıları (Gate Check)

Her faz tamamlandığında aşağıdaki kontroller yapılır. Tümü geçmedikçe bir sonraki faza geçilmez.

### 6.1 Genel Gate Check (tüm fazlarda)

| # | Kontrol | Açıklama |
|---|---|---|
| G1 | Task tamamlanma | Bu fazdaki tüm task'lar "tamamlandı" durumunda mı? |
| G2 | Kabul kriterleri | Her task'ın kabul kriterleri karşılandı mı? |
| G3 | Doğrulama kontrol listesi | Her task'ın kontrol listesi geçti mi? |
| G4 | Test | Bu fazdaki tüm task'ların test beklentileri karşılandı mı? |
| G5 | CI | CI pipeline (build + test) yeşil mi? |
| G6 | Regresyon | Önceki fazların testleri hâlâ geçiyor mu? |
| G7 | Boşluk | Traceability matrix'te bu faza ait eşlenip implement edilmeyen öğe var mı? |

### 6.2 Faz-Spesifik Kontroller

| Faz | Ek Kontrol |
|---|---|
| F0 | Docker-compose up ile tüm servisler ayağa kalkıyor mu? Monitoring dashboard'lar çalışıyor mu? |
| F1 | Migration temiz DB'ye hatasız uygulanıyor mu? Seed data doğru mu? Tüm constraint'ler test edildi mi? |
| F2 | Auth akışı uçtan uca çalışıyor mu? Admin RBAC doğru mu? |
| F3 | State machine tüm geçişleri doğru mu? Timeout scheduling doğru mu? Finansal hesaplamalar boundary value ile test edildi mi? |
| F4 | Sidecar'lar health check'te yeşil mi? Webhook iletişimi çift yönlü çalışıyor mu? |
| F5 | Tüm ekranlar 3 breakpoint'te doğru mu? 4 dil çalışıyor mu? SignalR real-time çalışıyor mu? |
| F6 | Tüm E2E senaryoları geçiyor mu? Staging ortamında test edildi mi? |

---

## 7. Traceability Matrix

Bu bölüm her task'ın hangi kaynak doküman öğelerini kapsadığını gösterir. "Implemented" kolonu, task tamamlandığında ✓ olarak işaretlenir.

> **Not:** Öğe ID'leri envanter taramasından alınmıştır. Her satır "bu öğe şu task'ta implement ediliyor" ilişkisini gösterir. Eşlenmeyen öğe = eksik task.

### 7.1 Veri Modeli → Task Eşleme (06)

| Öğe Grubu | Öğe ID Aralığı | Task | Implemented |
|---|---|---|---|
| Enum'lar | DM-026 – DM-048 | T17 | |
| User, UserLoginLog, RefreshToken | DM-001 – DM-003, DM-049–050, DM-105–107, DM-141–143, DM-159–163, DM-166, DM-179–181, DM-195–196, DM-200–202, DM-204 | T18 | |
| UserNotificationPreference | DM-004, DM-060–061, DM-108, DM-201, DM-204 | T23 | |
| Transaction, TransactionHistory | DM-005–006, DM-056, DM-070–075, DM-109–114, DM-141–146, DM-184–186, DM-188, DM-199, DM-206–207 | T19 | |
| PaymentAddress, BlockchainTransaction | DM-007–008, DM-051–054, DM-076–084, DM-115–117, DM-147–149, DM-165, DM-208 | T20 | |
| TradeOffer, PlatformSteamBot | DM-010–011, DM-055, DM-057, DM-088–089, DM-118–119, DM-150–151, DM-182–183, DM-204, DM-208 | T21 | |
| Dispute, FraudFlag | DM-012–013, DM-064, DM-090–093, DM-120–125, DM-154–158, DM-206 | T22 | |
| Notification, NotificationDelivery | DM-014–015, DM-068, DM-094–095, DM-126–128, DM-152–153, DM-198, DM-203, DM-206, DM-208 | T23 | |
| AdminRole, AdminRolePermission, AdminUserRole | DM-016–018, DM-058, DM-062–063, DM-129–132, DM-204 | T24 | |
| Altyapı entity'leri | DM-019–025, DM-059, DM-065–067, DM-069, DM-085–087, DM-096–104, DM-133–139, DM-164, DM-167–175, DM-197, DM-205, DM-209–211 | T25 | |
| Seed data | DM-176–178, DM-193–194 | T26 | |
| Performans index'leri | DM-141–175 | T27 | |
| Migration | DM-187–192 | T28, T04 | |
| Cascade | DM-140 | T04 | |
| Retention | DM-195–199 | T18, T25, T63b (retention job'ları) | |
| Anonimleştirme | DM-200–203 | T36 | |

### 7.2 API → Task Eşleme (07)

| Endpoint Grubu | API ID Aralığı | Task | Implemented |
|---|---|---|---|
| Auth (login, callback, ToS, me, re-verify, authenticator, logout, refresh) | API-001 – API-009 | T29, T30, T31, T32 | |
| User profil ve wallet | API-010 – API-014 | T33, T34 | |
| User settings | API-015 – API-027 | T35, T36 | |
| Steam inventory | API-028 | T67 | |
| Transactions (list, create, eligibility, params, detail, accept, cancel) | API-029 – API-035 | T45, T46, T51 | |
| Disputes | API-036 – API-038 | T58 | |
| Payout issue | API-039 | T60 | |
| Notifications | API-040 – API-043 | T38 | |
| Admin dashboard, flags | API-044 – API-048 | T63, T54 | |
| Admin transactions, settings, steam, roles, users, audit | API-049 – API-065 | T63, T41, T39, T59, T42 | |
| Platform public | API-066 – API-067 | T63a (backend), T86 (frontend) | |
| Telegram webhook | API-068 | T79 | |
| SignalR | API-069 – API-085 | T61, T62 | |
| DTO'lar | API-086 – API-189 | İlgili endpoint task'ları | |
| Validasyonlar | API-190 – API-229 | İlgili endpoint task'ları | |
| Middleware | API-230 – API-241 | T05, T06, T07, T68 | |
| Hata tanımları | API-242 – API-299 | İlgili endpoint task'ları | |

### 7.3 Entegrasyon → Task Eşleme (08)

| Entegrasyon | INT ID Aralığı | Task | Implemented |
|---|---|---|---|
| Steam OpenID | INT-001 – INT-007 | T29 | |
| Steam Web API | INT-008 – INT-011 | T29, T31, T67 | |
| Steam Community (envanter) | INT-012 – INT-015 | T67 | |
| Steam Trade Offer | INT-016 – INT-019, INT-157 | T65, T66 | |
| Steam Sidecar setup | INT-020 – INT-022 | T14 | |
| Steam hata yönetimi | INT-023 – INT-032 | T64, T65, T66 | |
| TRON setup | INT-033 – INT-043 | T15, T73, T74 | |
| HD Wallet | INT-044 – INT-048 | T70 | |
| TRON token config | INT-049 – INT-056 | T15, T73, T74 | |
| Ödeme izleme | INT-057 – INT-067 | T71, T72, T75 | |
| TRON hata yönetimi | INT-068 – INT-076 | T71, T73 | |
| Email (Resend) | INT-077 – INT-099 | T78 | |
| Telegram | INT-100 – INT-116 | T79 | |
| Discord | INT-117 – INT-134 | T80 | |
| Steam Market fiyat | INT-135 – INT-145 | T81 | |
| Cross-cutting | INT-146 – INT-156 | T05, T08, T16, T36 (circuit breaker: T64–T80'de uygulanır) | |

### 7.4 UI → Task Eşleme (04)

| Ekran/Bileşen Grubu | UI ID Aralığı | Task | Implemented |
|---|---|---|---|
| Ortak bileşenler (C01–C17) | UI-026 – UI-042 | T84 | |
| Modal'lar | UI-043 – UI-050 | T84, T87, T94, T100, T101 | |
| Landing page (S01) | UI-001, UI-081–082 | T86 | |
| Auth ekranları (S02, S03, S03a–d) | UI-002 – UI-007, UI-083–086 | T87 | |
| Dashboard (S05) | UI-008, UI-087–091, UI-174, UI-179–181 | T88 | |
| İşlem oluşturma (S06) | UI-009, UI-051–054, UI-092–101, UI-147–152, UI-161, UI-178 | T89 | |
| İşlem detay (S07) | UI-010, UI-055–056, UI-102–132, UI-153–155, UI-175, UI-182–183 | T90, T91, T92 | |
| Profil (S08, S09) | UI-011–012, UI-057–058 | T93 | |
| Hesap ayarları (S10) | UI-013, UI-059–062, UI-156–160 | T94 | |
| Bildirimler (S11) | UI-014, UI-133–135, UI-196–197 | T95 | |
| Admin Dashboard (S12) | UI-015, UI-136, UI-176–177, UI-184–186 | T99 | |
| Admin Flag (S13, S14) | UI-016–018, UI-050, UI-063–065 | T100 | |
| Admin İşlemler (S15, S16) | UI-019–020, UI-048–049, UI-066, UI-187 | T101 | |
| Admin Parametreler (S17) | UI-021, UI-067, UI-071–080, UI-198–199 | T102 | |
| Admin Steam (S18) | UI-022, UI-070, UI-137–139, UI-188–189 | T103 | |
| Admin Roller (S19) | UI-023, UI-047, UI-068, UI-159, UI-190–191 | T104 | |
| Admin Kullanıcı (S20) | UI-024, UI-192–195 | T105 | |
| Admin Audit Log (S21) | UI-025, UI-069, UI-140–142 | T106 | |
| Responsive | UI-162 – UI-168 | T98 | |
| Lokalizasyon | UI-169 – UI-173 | T97 | |
| Suspended session | UI-143 | T87, T90 | |

---

## 8. Boşluk Raporu

Kaynak envanteri taraması ve traceability matrix eşlemesi sonucunda tespit edilen boşluklar:

| # | Açıklama | Durum |
|---|---|---|
| — | Tüm kaynak öğeleri en az bir task'a eşlenmiştir | ✓ Boşluk yok |

> **Not:** Bu bölüm ilk yazılım sırasında boştur. İmplementasyon sürecinde yeni boşluklar tespit edilirse buraya eklenir ve ilgili düzeltme task'ları oluşturulur.

---

## 9. Post-MVP Parkı

Aşağıdaki özellikler MVP kapsamı dışıdır (10_MVP_SCOPE §3). Task listesine dahil edilmemiştir ancak kayıp olmaması için kayıt altındadır.

| ID | Özellik | Kaynak |
|---|---|---|
| MVP-OUT-001 | Barter (item-item takas) | 10 §3.1 |
| MVP-OUT-002 | Çoklu item işlemleri | 10 §3.1 |
| MVP-OUT-003 | Trade lock'lu item desteği | 10 §3.1 |
| MVP-OUT-004 | Diğer Steam oyunları (Dota 2, TF2, Rust) | 10 §3.1 |
| MVP-OUT-005 | Platform cüzdanı (bakiye yükleme) | 10 §3.2 |
| MVP-OUT-006 | Ek blockchain ağları | 10 §3.2 |
| MVP-OUT-007 | Fiat ödeme desteği | 10 §3.2 |
| MVP-OUT-008 | Mobil uygulama | 10 §3.3 |
| MVP-OUT-009 | Kullanıcı yorum/değerlendirme sistemi | 10 §3.3 |
| MVP-OUT-010 | Kullanıcıya piyasa fiyatı gösterimi | 10 §3.3 |
| MVP-OUT-011 | Premium üyelik | 10 §3.4 |
| MVP-OUT-012 | Ek gelir kanalları (komisyon dışı) | 10 §3.4 |
| MVP-OUT-013 | KYC | 10 §3.5 |
| MVP-OUT-014 | Admin eskalasyon süreci detayları | 10 §3.6 |
| MVP-OUT-015 | Kullanıcı sözleşmesi içeriği (metin yazılmadı) | 10 §3.6 |
| MVP-OUT-016 | Bildirim mesaj içerikleri — final/polished metinler (MVP'de placeholder metinler kullanılır, T37) | 10 §3.6 |
| MVP-OUT-017 | Platform Steam hesapları yönetim detayları | 10 §3.6 |
| MVP-OUT-018 | Steam Mobile Authenticator kontrol mekanizması detayları | 10 §3.6 |

---

*Skinora — Implementation Plan v0.5*
